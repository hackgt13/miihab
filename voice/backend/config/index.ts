/**
 * Centralised configuration — reads all process.env in one place.
 *
 * Required vars throw at startup so misconfiguration is caught immediately.
 * Load via Node's built-in flag:  node --env-file=.env server.ts
 */

function required(key: string): string {
  const val = process.env[key]?.trim();
  if (!val) throw new Error(`Missing required env var: ${key}`);
  return val;
}

function optional(key: string, fallback: string): string {
  return process.env[key]?.trim() || fallback;
}

export const config = {
  elevenlabs: {
    apiKey: required("ELEVENLABS_API_KEY"),
    agentId: optional("ELEVENLABS_AGENT_ID", ""),
    voiceId: optional("ELEVENLABS_VOICE_ID", "21m00Tcm4TlvDq8ikWAM"),
  },
  supabase: {
    url: required("SUPABASE_URL"),
    serviceKey: required("SUPABASE_SERVICE_KEY"),
  },
  // The coordinator owns the care plan and measured results; Alex only reads them.
  coordinator: {
    url: optional("COORDINATOR_URL", "http://127.0.0.1:8766"),
  },
  server: {
    port: Number(optional("VOICE_PORT", "8769")),
  },
} as const;

export type Config = typeof config;
