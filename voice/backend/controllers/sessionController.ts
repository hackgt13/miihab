/**
 * Session controller — owns the WebSocket session lifecycle.
 *
 * Responsibilities:
 *   - Handshake (session_start / session_started)
 *   - Supabase session creation / teardown
 *   - Wiring PatientService + AnalyticsService + ToolService per session
 *   - Bridging Unity audio ↔ ElevenLabs conversation
 *   - Emitting structured events back to Unity
 */

import { WebSocket } from "ws";
import type { Config } from "../config/index.ts";
import { ElevenLabsConversation } from "../conversation.ts";
import type { PatientService } from "../services/patientService.ts";
import type { AnalyticsService } from "../services/analyticsService.ts";
import type { CoordinatorService } from "../services/coordinatorService.ts";
import { ToolService } from "../services/toolService.ts";
import { TUTORIAL_LINES } from "../prompts.ts";
import { listeners } from "../listeners.ts";

interface SessionControllerDeps {
  config: Config;
  patientService: PatientService;
  analyticsService: AnalyticsService;
  coordinator: CoordinatorService;
}

/** Returns a handler function to pass to WebSocketServer's 'connection' event. */
export function createSessionController(deps: SessionControllerDeps) {
  return function onConnection(unity: WebSocket): void {
    console.log("Unity client connected");
    handleSession(unity, deps).catch((err) => {
      console.error("Session error:", err);
      safeClose(unity, 1011, "Internal error");
    });
  };
}

// ── Session lifecycle ─────────────────────────────────────────────────────────

async function handleSession(
  unity: WebSocket,
  deps: SessionControllerDeps,
): Promise<void> {
  const { config, patientService, analyticsService, coordinator } = deps;

  // 1. Handshake ─────────────────────────────────────────────────────────────
  const raw = await waitForMessage(unity, 30_000);
  if (raw === null) {
    safeClose(unity, 1008, "Timeout waiting for session_start");
    return;
  }

  let init: Record<string, unknown>;
  try {
    init = JSON.parse(raw) as Record<string, unknown>;
  } catch {
    safeClose(unity, 1003, "Invalid JSON");
    return;
  }

  if (init["type"] !== "session_start") {
    safeClose(unity, 1002, "First message must be type=session_start");
    return;
  }

  const patientId = String(init["patient_id"] ?? "").trim();
  if (!patientId) {
    safeClose(unity, 1002, "session_start missing patient_id");
    return;
  }
  const mode = String(init["mode"] ?? "").trim();
  const isTutorial = mode === "tutorial";

  if (isTutorial) {
    await handleTutorial(unity, config);
    return;
  }

  // ── Regular rehab session ────────────────────────────────────────────────

  // 2. Create session record ─────────────────────────────────────────────────
  let session: { id: string; startedAt: string };
  try {
    session = await patientService.createSession(patientId);
  } catch (err) {
    emit(unity, { type: "error", message: `DB error: ${JSON.stringify(err)}` });
    console.error("DB error detail:", err);
    safeClose(unity);
    return;
  }
  const sessionId = session.id;
  console.log(`Session started — patient=${patientId} session=${sessionId}`);

  // 3. Wire up services ──────────────────────────────────────────────────────
  const emitEvent = (event: Record<string, unknown>) => emit(unity, event);

  const toolService = new ToolService(
    patientService,
    analyticsService,
    coordinator,
    patientId,
    session,
    emitEvent,
  );

  const conv = new ElevenLabsConversation({
    agentId: config.elevenlabs.agentId,
    apiKey: config.elevenlabs.apiKey,
    onAudio: (chunk) =>
      emit(unity, { type: "audio", data: chunk.toString("base64") }),
    onTranscript: (role, text) => {
      emit(unity, { type: "transcript", role, text });
      // The clinician's coach feed: what Alex said, and what the patient said to him.
      coordinator.coachEvent({ conversation: sessionId, source: role === "agent" ? "coach" : "patient",
        kind: role === "agent" ? "llm_reply" : "speak", content: text });
    },
    onToolCall: (_id, name, params) => {
      coordinator.coachEvent({ conversation: sessionId, source: "coach", kind: "tool_call", tool: name,
        content: `${name}(${Object.entries(params ?? {}).map(([k, v]) => `${k}: ${JSON.stringify(v)}`).join(", ")})` });
      return toolService.dispatch(name, params);
    },
    onInterrupt: () => emit(unity, { type: "interrupt" }),
    // Unity plays and records at these rates (pcm_16000 → 16 kHz, 16-bit mono).
    onFormat: (format) => emit(unity, { type: "audio_format", ...format }),
    onError: (msg) => {
      console.error("ElevenLabs error:", msg);
      emit(unity, { type: "error", message: msg });
    },
    // ElevenLabs hung up: tell Unity and end the session instead of leaving it waiting on silence.
    onClose: () => {
      emit(unity, { type: "error", message: "Voice agent disconnected" });
      safeClose(unity, 1011, "Voice agent disconnected");
    },
  });

  // 4. Start ElevenLabs conversation ─────────────────────────────────────────
  try {
    await conv.start();
  } catch (err) {
    emit(unity, {
      type: "error",
      message: `Failed to start conversation: ${err}`,
    });
    safeClose(unity);
    return;
  }
  emit(unity, { type: "session_started" });

  // 5. Relay audio until session_end or disconnect ───────────────────────────
  await new Promise<void>((resolve) => {
    unity.on("message", (data: Buffer) => {
      let msg: Record<string, unknown>;
      try {
        msg = JSON.parse(data.toString()) as Record<string, unknown>;
      } catch {
        return;
      }

      if (msg["type"] === "audio") {
        const pcm = Buffer.from(String(msg["data"]), "base64");
        conv.sendAudio(pcm);
      } else if (msg["type"] === "context") {
        // What just happened in the studio (a rep, a set ending, a plan change), for Alex to react to.
        const text = String(msg["text"] ?? "").trim().slice(0, 600);
        if (text) {
          if (msg["speak"] === true) conv.sendPrompt(text); else conv.sendContext(text);
          // What the studio measured, as the clinician's feed shows it beside what Alex said about it.
          coordinator.coachEvent({ conversation: sessionId, source: "engine", kind: text.startsWith("[rep]") ? "measurement" : "report", content: text });
        }
      } else if (msg["type"] === "session_end") {
        console.log("session_end received");
        resolve();
      }
    });
    unity.on("close", resolve);
    unity.on("error", (err: Error) => {
      console.error("Unity WS error:", err.message);
      resolve();
    });
  });

  // 6. Teardown ──────────────────────────────────────────────────────────────
  conv.end();
  try {
    await patientService.endSession(sessionId);   // no-op fields if close_session already ran
  } catch (err) {
    console.warn("Failed to stamp ended_at:", err);
  }

  console.log(`Session closed — session=${sessionId}`);
}

// ── Tutorial session (scripted TTS, no conversational agent) ─────────────────

async function handleTutorial(unity: WebSocket, config: Config): Promise<void> {
  console.log("Tutorial session started");
  emit(unity, { type: "audio_format", output: "pcm_16000", input: "pcm_16000" });
  emit(unity, { type: "session_started" });

  // Every line goes through one queue: the intro first, then each cue in the order it arrived. Two lines never
  // stream at once (their 100 ms chunks would interleave into noise), and a line that fails — ElevenLabs down, the
  // network gone — is logged and skipped rather than taking the whole voice server down with an unhandled rejection.
  let speaking: Promise<void> = Promise.resolve();
  const say = (line: string) => {
    speaking = speaking.then(() => speakLine(unity, line, config))
      .catch((err) => console.error("TTS failed:", err instanceof Error ? err.message : err));
  };
  say(TUTORIAL_LINES.intro ?? "");

  await new Promise<void>((resolve) => {
    unity.on("message", (data: Buffer) => {
      let msg: Record<string, unknown>;
      try { msg = JSON.parse(data.toString()) as Record<string, unknown>; } catch { return; }

      if (msg["type"] === "cue") {
        const line = TUTORIAL_LINES[String(msg["step"] ?? "")];
        if (line) say(line);
      } else if (msg["type"] === "session_end") {
        resolve();
      }
    });
    unity.on("close", resolve);
    unity.on("error", () => resolve());
  });

  console.log("Tutorial session closed");
}

/** Speak a single line via ElevenLabs TTS and stream the PCM audio to Unity. */
async function speakLine(unity: WebSocket, text: string, config: Config): Promise<void> {
  emit(unity, { type: "transcript", role: "agent", text });

  const resp = await fetch(
    `https://api.elevenlabs.io/v1/text-to-speech/${config.elevenlabs.voiceId}/stream?output_format=pcm_16000`,
    {
      method: "POST",
      headers: {
        "xi-api-key": config.elevenlabs.apiKey,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ text, model_id: "eleven_turbo_v2_5" }),
    },
  );

  if (!resp.ok) {
    console.error("TTS failed:", resp.status, await resp.text());
    return;
  }

  // Buffer into regular 100ms chunks (16kHz × 2 bytes × 0.1s = 3200 bytes)
  // so Unity's ring buffer stays fed without gaps.
  const CHUNK = 3200;
  let buf = Buffer.alloc(0);
  const reader = resp.body!.getReader();

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    buf = Buffer.concat([buf, Buffer.from(value)]);
    while (buf.length >= CHUNK) {
      emit(unity, { type: "audio", data: buf.subarray(0, CHUNK).toString("base64") });
      buf = buf.subarray(CHUNK);
    }
  }
  if (buf.length > 0) {
    emit(unity, { type: "audio", data: buf.toString("base64") });
  }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function emit(ws: WebSocket, msg: Record<string, unknown>): void {
  if (ws.readyState === WebSocket.OPEN) {
    try {
      ws.send(JSON.stringify(msg));
    } catch {}
  }
  // A headset listening hears Alex too (backend/listeners.ts).
  listeners.mirror(msg);
}

function safeClose(ws: WebSocket, code?: number, reason?: string): void {
  try {
    ws.close(code, reason);
  } catch {}
}

function waitForMessage(
  ws: WebSocket,
  timeoutMs: number,
): Promise<string | null> {
  return new Promise((resolve) => {
    const timer = setTimeout(() => {
      off();
      resolve(null);
    }, timeoutMs);
    function off() {
      ws.off("message", onMsg);
      ws.off("close", onClose);
      clearTimeout(timer);
    }
    function onMsg(data: Buffer) {
      off();
      resolve(data.toString());
    }
    function onClose() {
      off();
      resolve(null);
    }
    ws.once("message", onMsg);
    ws.once("close", onClose);
  });
}
