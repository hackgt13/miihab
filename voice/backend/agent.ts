/**
 * ElevenLabs agent setup.
 *
 * Create (run once, copy the printed ID into .env):
 *   node --env-file=.env backend/agent.ts create
 *
 * Update existing agent (prompt, first message, voice, tools):
 *   node --env-file=.env backend/agent.ts update
 */

import { ElevenLabsClient } from "@elevenlabs/elevenlabs-js";
import { config } from "./config/index.ts";
import { SYSTEM_PROMPT, FIRST_MESSAGE } from "./prompts.ts";
import { TOOL_DEFINITIONS } from "./services/toolService.ts";

const client = new ElevenLabsClient({ apiKey: config.elevenlabs.apiKey });

const agentBody = {
  name: "Virtual PT — Alex",
  conversationConfig: {
    agent: {
      prompt: {
        prompt: SYSTEM_PROMPT,
        tools: TOOL_DEFINITIONS as any,
      },
      firstMessage: FIRST_MESSAGE,
      language: "en",
    },
    tts: {
      voiceId: config.elevenlabs.voiceId,
    },
  },
};

const cmd = process.argv[2] ?? "create";

if (cmd === "update") {
  if (!config.elevenlabs.agentId) {
    console.error("ELEVENLABS_AGENT_ID not set in .env — run `create` first.");
    process.exit(1);
  }
  await client.conversationalAi.agents.update(config.elevenlabs.agentId, agentBody);
  console.log(`\nAgent ${config.elevenlabs.agentId} updated.`);
} else {
  const agent = await client.conversationalAi.agents.create(agentBody);
  console.log("\nAgent created successfully!");
  console.log(`Add this to your .env file:\n\nELEVENLABS_AGENT_ID=${agent.agentId}\n`);
}
