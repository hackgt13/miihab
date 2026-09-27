/**
 * Virtual PT Voice Server — entry point.
 *
 *   node --env-file=.env server.ts
 *
 * WebSocket:   ws://localhost:8769/voice
 * Health:      GET http://localhost:8769/health
 */

import { createServer } from "node:http";
import { WebSocketServer } from "ws";
import { config } from "./backend/config/index.ts";
import { PatientService } from "./backend/services/patientService.ts";
import { AnalyticsService } from "./backend/services/analyticsService.ts";
import { CoordinatorService } from "./backend/services/coordinatorService.ts";
import { createSessionController } from "./backend/controllers/sessionController.ts";
import { handleHealth } from "./backend/controllers/healthController.ts";
import { listeners } from "./backend/listeners.ts";

if (!config.elevenlabs.agentId) {
  console.warn(
    "ELEVENLABS_AGENT_ID not set — run `node --env-file=.env backend/agent.ts` first.",
  );
}

// ── Services ──────────────────────────────────────────────────────────────────

const patientService = new PatientService(
  config.supabase.url,
  config.supabase.serviceKey,
);
const coordinator = new CoordinatorService(config.coordinator.url);
const analyticsService = new AnalyticsService(patientService, coordinator);
const onConnection = createSessionController({
  config,
  patientService,
  analyticsService,
  coordinator,
});

// ── Supabase connectivity check ────────────────────────────────────────────────
patientService.ping().then((err) => {
  if (err) console.error("Supabase check failed:", JSON.stringify(err));
  else console.log("Supabase connected OK");
});

// ── HTTP server ────────────────────────────────────────────────────────────────

const server = createServer((req, res) => {
  if (handleHealth(req, res, config)) return;
  res.writeHead(404).end();
});

// ── WebSocket server ───────────────────────────────────────────────────────────

const wss = new WebSocketServer({ noServer: true });

server.on("upgrade", (req, socket, head) => {
  const url = new URL(req.url ?? "/", `http://localhost:${config.server.port}`);
  // Unity and the headset never send an Origin; a browser always does. Without this any web page open on this Mac
  // could start a session, spend ElevenLabs credit and put words in the coach feed.
  if (url.pathname !== "/voice" || req.headers.origin) {
    socket.destroy();
    return;
  }
  // A headset that only listens: it hears Alex and never starts a conversation of its own (backend/listeners.ts).
  if (url.searchParams.get("role") === "listen") {
    wss.handleUpgrade(req, socket, head, (ws) => {
      ws.on("error", () => ws.close());
      listeners.addListener(ws);
    });
    return;
  }
  wss.handleUpgrade(req, socket, head, (ws) => wss.emit("connection", ws));
});

wss.on("connection", (ws) => {
  listeners.addSession(ws);
  return onConnection(ws);
});

// ── Start ──────────────────────────────────────────────────────────────────────

server.listen(config.server.port, "127.0.0.1", () => {
  console.log(
    `Virtual PT voice server: ws://localhost:${config.server.port}/voice`,
  );
});

function shutdown() {
  for (const ws of wss.clients) ws.close();
  wss.close();
  server.close();
}
process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);
