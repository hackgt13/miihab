/**
 * Muse Spark as Alex's brain, through ElevenLabs' "custom LLM".
 *
 * ElevenLabs keeps the ears, the voice and the turn-taking and asks an OpenAI-compatible endpoint what Alex says
 * next. Meta's Muse Spark speaks that dialect but refuses any field it does not know, and ElevenLabs sends a few
 * that only OpenAI accepts (`user_id`, `elevenlabs_extra_body`, `stop`, …), so a direct connection answers every
 * turn with a 400 and Alex never speaks. This adapter sits between them on the Mac: it drops what Muse refuses,
 * asks for minimal reasoning (about a second faster to Alex's first word), and streams Muse's answer straight back.
 *
 * The Muse key never leaves the Mac. ElevenLabs holds only MUSE_PROXY_TOKEN, a password for this adapter, which it
 * sends as its Bearer token; anything without it is refused. The adapter listens on the Mac's loopback; the one way
 * in from ElevenLabs' servers is a Tailscale Funnel onto this port (scripts/start_alex_muse.sh).
 */
import { createServer, type IncomingMessage, type Server } from "node:http";
import { timingSafeEqual } from "node:crypto";

const MUSE_URL = process.env.MUSE_BASE_URL ?? "https://api.meta.ai/v1";
const MUSE_MODEL = process.env.MUSE_MODEL ?? "muse-spark-1.3";
const BODY_LIMIT = 1 << 20;   // a whole conversation plus tools is tens of KB

/** Fields Muse Spark refuses outright ("unknown parameter" / "not supported"). */
const REFUSED = ["user_id", "user", "elevenlabs_extra_body", "stop", "service_tier", "store", "metadata", "logit_bias"];

/** The request Muse will accept, carrying everything else ElevenLabs asked for unchanged. */
export function forMuse(body: Record<string, unknown>): Record<string, unknown> {
  const out: Record<string, unknown> = { ...body };
  for (const key of REFUSED) delete out[key];
  out["model"] = MUSE_MODEL;
  // Muse takes one of the two, and at least 1.
  if (out["max_completion_tokens"] != null && out["max_tokens"] != null) delete out["max_tokens"];
  for (const key of ["max_tokens", "max_completion_tokens"]) {
    const n = Number(out[key]);
    if (out[key] != null && !(Number.isFinite(n) && n >= 1)) delete out[key];
  }
  // Only "auto" is supported. With no tools there is nothing to choose.
  const tools = out["tools"];
  if (!Array.isArray(tools) || tools.length === 0) { delete out["tools"]; delete out["tool_choice"]; delete out["parallel_tool_calls"]; }
  else if (out["tool_choice"] != null) out["tool_choice"] = "auto";
  // Alex answers a sentence or two; minimal reasoning is plenty and is what keeps the pause short.
  out["reasoning_effort"] = "minimal";
  return out;
}

function authorised(req: IncomingMessage, token: string): boolean {
  const given = Buffer.from(String(req.headers["authorization"] ?? "").replace(/^Bearer\s+/i, ""));
  const want = Buffer.from(token);
  return given.length === want.length && timingSafeEqual(given, want);
}

async function readBody(req: IncomingMessage): Promise<string> {
  let body = "";
  for await (const chunk of req) { body += chunk; if (body.length > BODY_LIMIT) throw Error("Body too large"); }
  return body;
}

export function createMuseProxy(opts: { museKey: string; token: string }): Server {
  return createServer(async (req, res) => {
    if (req.method !== "POST" || !/\/chat\/completions$/.test(new URL(req.url ?? "/", "http://x").pathname)) {
      res.writeHead(404).end(); return;
    }
    if (!authorised(req, opts.token)) { res.writeHead(401).end(); return; }
    let body: Record<string, unknown>;
    try { body = JSON.parse(await readBody(req)); }
    catch { res.writeHead(400, { "Content-Type": "application/json" }).end('{"error":{"message":"invalid JSON"}}'); return; }

    try {
      const upstream = await fetch(`${MUSE_URL}/chat/completions`, {
        method: "POST", signal: AbortSignal.timeout(60_000),
        headers: { Authorization: `Bearer ${opts.museKey}`, "Content-Type": "application/json" },
        body: JSON.stringify(forMuse(body)),
      });
      res.writeHead(upstream.status, { "Content-Type": upstream.headers.get("content-type") ?? "application/json", "Cache-Control": "no-store" });
      if (!upstream.body) { res.end(); return; }
      if (upstream.status >= 400) console.warn("Muse refused a turn:", upstream.status);
      for await (const chunk of upstream.body as unknown as AsyncIterable<Uint8Array>) res.write(chunk);
      res.end();
    } catch (err) {
      console.warn("Muse proxy:", err instanceof Error ? err.message : err);
      if (!res.headersSent) res.writeHead(502, { "Content-Type": "application/json" }).end('{"error":{"message":"Muse unreachable"}}');
      else res.end();
    }
  });
}
