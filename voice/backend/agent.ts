/**
 * ElevenLabs agent setup.
 *
 * Create (run once, copy the printed ID into .env):
 *   node --env-file=.env backend/agent.ts create
 *
 * Update existing agent (prompt, first message, voice, tools):
 *   node --env-file=.env backend/agent.ts update
 *
 * Alex's brain is Meta's Muse Spark when MUSE_PROXY_URL and MUSE_PROXY_TOKEN are set (scripts/start_alex_muse.sh
 * sets both and runs this): ElevenLabs keeps the ears, the voice and the turn-taking, and asks the adapter on the
 * Mac (backend/museProxy.ts) what Alex says and which tools he calls. ElevenLabs holds only the adapter's password,
 * as a workspace secret; the Muse key stays on the Mac. Without them the agent keeps ElevenLabs' default model.
 */

import { ElevenLabsClient } from "@elevenlabs/elevenlabs-js";
import { config } from "./config/index.ts";
import { SYSTEM_PROMPT, FIRST_MESSAGE } from "./prompts.ts";
import { TOOL_DEFINITIONS } from "./services/toolService.ts";

const client = new ElevenLabsClient({ apiKey: config.elevenlabs.apiKey });

const PROXY_SECRET = "miihab-muse-proxy";
const proxyUrl = process.env.MUSE_PROXY_URL?.trim();
const proxyToken = process.env.MUSE_PROXY_TOKEN?.trim();
const useMuse = !!(proxyUrl && proxyToken);

/** The workspace secret holding the adapter's password (never the Muse key): created once, refreshed if it changes. */
async function proxySecretId(): Promise<string> {
  const found = await client.conversationalAi.secrets.list({ search: PROXY_SECRET });
  const existing = (found.secrets ?? []).find((s: any) => s.name === PROXY_SECRET) as any;
  if (existing) {
    await client.conversationalAi.secrets.update(existing.secretId, { name: PROXY_SECRET, value: proxyToken! } as any);
    return existing.secretId;
  }
  return ((await client.conversationalAi.secrets.create({ name: PROXY_SECRET, value: proxyToken! })) as any).secretId;
}

/** Muse Spark, through the adapter on the Mac (backend/museProxy.ts), as Alex's model; else ElevenLabs' default. */
async function brain(): Promise<Record<string, unknown>> {
  // Named, not left out: ElevenLabs keeps a custom LLM on an update that does not replace it, so switching back
  // off Muse has to say which model. qwen35-397b-a17b is what Alex ran on before Muse.
  if (!useMuse) return { llm: process.env.ELEVENLABS_LLM ?? "qwen35-397b-a17b", customLlm: null };
  return {
    llm: "custom-llm",
    customLlm: { url: proxyUrl, modelId: process.env.MUSE_MODEL ?? "muse-spark-1.3",
      apiKey: { secretId: await proxySecretId() }, apiType: "chat_completions" },
  };
}

const agentBody = {
  name: "Virtual PT — Alex",
  conversationConfig: {
    agent: {
      prompt: {
        prompt: SYSTEM_PROMPT,
        tools: TOOL_DEFINITIONS as any,
        ...(await brain()),
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
