/**
 * CLI test client for the Virtual PT voice server.
 *
 * Connects to ws://localhost:8768/voice, starts a session, and bridges
 * raw 16kHz mono PCM audio from stdin → ElevenLabs and TTS audio → stdout.
 *
 * This lets you pipe any audio tool in/out without native Node audio deps:
 *
 *   Windows (PowerShell, requires ffmpeg):
 *     ffmpeg -f dshow -i audio="<your mic>" -ar 16000 -ac 1 -f s16le - `
 *       | node --env-file=.env test/cli.ts [patient-id] `
 *       | ffplay -f s16le -ar 16000 -ac 1 -
 *
 *   macOS:
 *     sox -d -r 16000 -c 1 -e signed -b 16 -t raw - \
 *       | node --env-file=.env test/cli.ts [patient-id] \
 *       | sox -r 16000 -c 1 -e signed -b 16 -t raw - -d
 *
 *   Headless / event-log only (no audio):
 *     node --env-file=.env test/cli.ts [patient-id] --no-audio
 *
 * The patient-id is optional — a random UUID is generated if omitted.
 * Transcript and event lines are written to stderr so they don't pollute
 * the raw PCM stdout stream.
 */

import { randomUUID } from "node:crypto";
import { spawn, spawnSync } from "node:child_process";
import type { ChildProcessByStdio } from "node:child_process";
import type { Writable } from "node:stream";
import { WebSocket } from "ws";
import { Readable } from "node:stream";

// ── Args ──────────────────────────────────────────────────────────────────────

const args = process.argv.slice(2);
const noAudio = args.includes("--no-audio");
const micFlag = args.find((a) => a.startsWith("--mic="))?.slice(6) ?? null;
const patientId = args.find((a) => !a.startsWith("--")) ?? randomUUID();
const serverUrl =
  process.env["VOICE_SERVER_URL"] ?? "ws://localhost:8768/voice";

// Auto-detect first available dshow audio device if --mic not provided
function detectMic(): string | null {
  try {
    const result = spawnSync(
      'ffmpeg',
      ['-list_devices', 'true', '-f', 'dshow', '-i', 'dummy'],
      { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] },
    );
    const output = result.stderr ?? result.stdout ?? '';
    const match = output.match(/"([^"]+)"\s*\(audio\)/);
    return match?.[1] ?? null;
  } catch {
    return null;
  }
}

const micDevice: string | null = noAudio
  ? null
  : (micFlag ?? detectMic());

log(`Connecting to ${serverUrl}`);
log(`Patient ID: ${patientId}  (pass as first arg to reuse across sessions)`);
if (micDevice) log(`Mic device: ${micDevice}`);

// ── Audio playback via ffplay ──────────────────────────────────────────────────

let player: ChildProcessByStdio<Writable, null, null> | null = null;

if (!noAudio) {
  try {
    player = spawn('ffplay', ['-f', 's16le', '-ar', '16000', '-nodisp', '-'], {
      stdio: ['pipe', 'ignore', 'inherit'],
    });
    player?.on('error', () => {
      log('ffplay not found — install ffmpeg to hear audio. Running transcript-only.');
      player = null;
    });
    log('Audio playback ready (ffplay)');
  } catch {
    log('ffplay not found — install ffmpeg to hear audio. Running transcript-only.');
  }
}

// ── Connect ───────────────────────────────────────────────────────────────────

const ws = new WebSocket(serverUrl);

ws.on("error", (err: Error) => {
  log(`WS error: ${err.message}`);
  process.exit(1);
});
ws.on("close", (code: number) => {
  log(`Disconnected (${code})`);
  player?.stdin.end();
  process.exit(0);
});

ws.on("open", () => {
  log("Connected — sending session_start");
  ws.send(JSON.stringify({ type: "session_start", patient_id: patientId }));
});

ws.on("message", (data: Buffer) => {
  let msg: Record<string, unknown>;
  try {
    msg = JSON.parse(data.toString()) as Record<string, unknown>;
  } catch {
    return;
  }

  switch (msg["type"]) {
    case "session_started":
      log("Session started — speak now (Ctrl+C to end)");
      if (!noAudio) startAudioBridge();
      process.on("SIGINT", () => {
        log("Sending session_end");
        ws.send(JSON.stringify({ type: "session_end" }));
        setTimeout(() => process.exit(0), 1000);
      });
      break;

    case "audio": {
      const pcm = Buffer.from(String(msg["data"]), "base64");
      if (player?.stdin.writable) {
        player.stdin.write(pcm);
      } else if (!noAudio) {
        log(`[AUDIO]  ${pcm.length} bytes (install ffmpeg to hear audio)`);
      }
      break;
    }

    case "transcript": {
      const role = String(msg["role"]).toUpperCase().padEnd(5);
      log(`[${role}] ${msg["text"]}`);
      break;
    }

    case "interrupt":
      log("[INTERRUPT] Agent speech interrupted");
      break;

    case "milestone":
      log(`[MILESTONE] ${JSON.stringify(msg["data"])}`);
      break;

    case "session_summary":
      log(`[SUMMARY]   ${JSON.stringify(msg["data"], null, 2)}`);
      break;

    case "error":
      log(`[ERROR] ${msg["message"]}`);
      break;

    default:
      log(`[EVENT] ${JSON.stringify(msg)}`);
  }
});

// ── Audio bridge: mic/stdin → server ─────────────────────────────────────────

function startAudioBridge(): void {
  const CHUNK = 3200; // 100ms of 16kHz mono s16le
  let buf = Buffer.alloc(0);
  let audioSource: Readable;

  if (micDevice) {
    // Capture from a named dshow device (auto-detected or --mic="...")
    log(`Starting mic capture from: ${micDevice}`);
    const mic = spawn('ffmpeg', [
      '-f', 'dshow',
      '-i', `audio=${micDevice}`,
      '-ar', '16000',
      '-ac', '1',
      '-f', 's16le',
      '-',
    ], { stdio: ['ignore', 'pipe', 'inherit'] });
    mic.on('error', (err) => log(`ffmpeg mic error: ${err.message}`));
    audioSource = mic.stdout as Readable;
    log('Mic capture started (ffmpeg dshow)');
  } else if (!process.stdin.isTTY) {
    // Piped audio from stdin (e.g. ffmpeg piped externally)
    audioSource = process.stdin;
    log('Reading audio from stdin');
  } else {
    log('No mic source. Use --mic="<device name>" or pipe audio to stdin.');
    log('List devices: ffmpeg -list_devices true -f dshow -i dummy');
    return;
  }

  audioSource.on("data", (chunk: Buffer) => {
    buf = Buffer.concat([buf, chunk]);
    while (buf.length >= CHUNK) {
      const frame = buf.subarray(0, CHUNK);
      buf = buf.subarray(CHUNK);
      if (ws.readyState === WebSocket.OPEN) {
        ws.send(
          JSON.stringify({ type: "audio", data: frame.toString("base64") }),
        );
      }
    }
  });

  audioSource.on("end", () => {
    log("audio source closed — sending session_end");
    if (ws.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: "session_end" }));
    }
  });
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function log(msg: string): void {
  process.stderr.write(
    `[voice-cli] ${new Date().toISOString().slice(11, 23)}  ${msg}\n`,
  );
}
