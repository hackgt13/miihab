// ElevenLabs' requests reach Muse in a shape Muse accepts, and only with the adapter's password.
import { test } from "node:test";
import assert from "node:assert/strict";
import { once } from "node:events";
import { forMuse, createMuseProxy } from "../backend/museProxy.ts";

test("what Muse refuses is dropped, the rest passes through", () => {
  const out = forMuse({ model: "gpt-4o", messages: [{ role: "user", content: "hi" }], stream: true, temperature: 0.5,
    user_id: "u", elevenlabs_extra_body: {}, stop: [], max_tokens: -1, tools: [{ type: "function" }], tool_choice: "required" });
  assert.equal(out.model, "muse-spark-1.3");
  for (const k of ["user_id", "elevenlabs_extra_body", "stop", "max_tokens"]) assert.ok(!(k in out), k);
  assert.equal(out.tool_choice, "auto");
  assert.equal(out.reasoning_effort, "minimal");
  assert.equal(out.temperature, 0.5);
  assert.equal(out.stream, true);
});

test("one token limit, never both; no tools means no tool_choice", () => {
  const out = forMuse({ messages: [], max_tokens: 200, max_completion_tokens: 300, tools: [], tool_choice: "none" });
  assert.equal(out.max_completion_tokens, 300);
  assert.ok(!("max_tokens" in out) && !("tools" in out) && !("tool_choice" in out));
});

test("the adapter refuses anyone without its password, and anything but chat completions", async () => {
  const server = createMuseProxy({ museKey: "not-used", token: "s3cret-token" });
  server.listen(0, "127.0.0.1"); await once(server, "listening");
  const base = `http://127.0.0.1:${(server.address() as any).port}`;
  try {
    assert.equal((await fetch(base + "/v1/chat/completions", { method: "POST", body: "{}" })).status, 401);
    assert.equal((await fetch(base + "/v1/chat/completions", { method: "POST", body: "{}", headers: { Authorization: "Bearer wrong-token!" } })).status, 401);
    assert.equal((await fetch(base + "/v1/models", { headers: { Authorization: "Bearer s3cret-token" } })).status, 404);
    assert.equal((await fetch(base + "/v1/chat/completions", { method: "POST", body: "not json", headers: { Authorization: "Bearer s3cret-token" } })).status, 400);
  } finally { server.close(); }
});
