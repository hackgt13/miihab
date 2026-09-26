import type { IncomingMessage, ServerResponse } from "node:http";
import type { Config } from "../config/index.ts";

export function handleHealth(
  req: IncomingMessage,
  res: ServerResponse,
  config: Config,
): boolean {
  if (req.method === "GET" && req.url === "/health") {
    res.writeHead(200, { "Content-Type": "application/json" }).end(
      JSON.stringify({
        status: "ok",
        agent_configured: Boolean(config.elevenlabs.agentId),
      }),
    );
    return true;
  }
  return false;
}
