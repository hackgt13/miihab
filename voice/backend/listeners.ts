// Headsets that hear Alex. The conversation belongs to the Mac (its microphone, its one ElevenLabs session); a
// headset opens a listen-only socket (/voice?role=listen) and gets a copy of what Alex says, so the patient hears
// the coach from the coach, inside the headset, instead of from AirPods strapped to their wrist. Session clients are
// told how many headsets are listening, so the Mac can go quiet and Alex is never heard twice.
import { WebSocket } from "ws";

/** What a listener needs to play Alex: the audio, its format, when to cut it off, and the words for subtitles. */
const MIRRORED = new Set(["audio", "audio_format", "interrupt", "session_started", "transcript"]);

interface Socket {
  readyState: number;
  send(data: string): void;
  on(event: "close", listener: () => void): unknown;
}

export class Listeners {
  private readonly listeners = new Set<Socket>();
  private readonly sessions = new Set<Socket>();

  get count(): number { return this.listeners.size; }

  addListener(ws: Socket): void {
    this.listeners.add(ws);
    ws.on("close", () => { this.listeners.delete(ws); this.announce(); });
    this.announce();
  }

  addSession(ws: Socket): void {
    this.sessions.add(ws);
    ws.on("close", () => this.sessions.delete(ws));
    this.tell(ws);
  }

  /** A message just sent to a session client; listeners get it too when it is part of Alex's voice. */
  mirror(msg: Record<string, unknown>): void {
    if (!MIRRORED.has(String(msg["type"])) || this.listeners.size === 0) return;
    const text = JSON.stringify(msg);
    for (const ws of this.listeners) send(ws, text);
  }

  private announce(): void { for (const ws of this.sessions) this.tell(ws); }
  private tell(ws: Socket): void { send(ws, JSON.stringify({ type: "listeners", count: this.listeners.size })); }
}

function send(ws: Socket, text: string): void {
  if (ws.readyState !== WebSocket.OPEN) return;
  try { ws.send(text); } catch { /* a socket that is closing; its close handler removes it */ }
}

export const listeners = new Listeners();
