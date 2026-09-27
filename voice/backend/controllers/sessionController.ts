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
    onTranscript: (role, text) =>
      emit(unity, { type: "transcript", role, text }),
    onToolCall: (_id, name, params) => toolService.dispatch(name, params),
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
        if (text) { if (msg["speak"] === true) conv.sendPrompt(text); else conv.sendContext(text); }
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

  // Set up cue listener BEFORE speaking intro so no cues are lost.
  // Cues that arrive during the intro TTS are queued and spoken after it finishes.
  const cueQueue: string[] = [];
  let introDone = false;

  await new Promise<void>((resolve) => {
    unity.on("message", async (data: Buffer) => {
      let msg: Record<string, unknown>;
      try { msg = JSON.parse(data.toString()) as Record<string, unknown>; } catch { return; }

      if (msg["type"] === "cue") {
        const step = String(msg["step"] ?? "");
        const line = TUTORIAL_LINES[step];
        if (!line) return;
        if (!introDone) { cueQueue.push(step); return; }
        await speakLine(unity, line, config);
      } else if (msg["type"] === "session_end") {
        resolve();
      }
    });
    unity.on("close", resolve);
    unity.on("error", () => resolve());

    // Speak the full intro (greeting + "watch me"), then drain any queued cues.
    speakLine(unity, TUTORIAL_LINES.intro ?? "", config).then(async () => {
      introDone = true;
      for (const step of cueQueue) {
        const line = TUTORIAL_LINES[step];
        if (line) await speakLine(unity, line, config);
      }
      cueQueue.length = 0;
    });
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
