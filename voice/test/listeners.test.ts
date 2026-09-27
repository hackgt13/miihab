// A headset listening hears Alex, and only Alex: the voice, its format and interruptions — never errors or tools.
import { test } from "node:test";
import assert from "node:assert/strict";
import { Listeners } from "../backend/listeners.ts";

class Fake {
  readyState = 1; sent: any[] = []; private closers: (() => void)[] = [];
  send(text: string) { this.sent.push(JSON.parse(text)); }
  on(_e: "close", f: () => void) { this.closers.push(f); }
  close() { this.readyState = 3; for (const f of this.closers) f(); }
}

test("a listener hears the voice and nothing else", () => {
  const hub = new Listeners(), headset = new Fake();
  hub.addListener(headset);
  for (const type of ["audio", "audio_format", "interrupt", "session_started", "transcript", "error", "tool_result"])
    hub.mirror({ type });
  assert.deepEqual(headset.sent.map((m) => m.type), ["audio", "audio_format", "interrupt", "session_started", "transcript"]);
});

test("the Mac is told when a headset starts and stops listening, so Alex is heard once", () => {
  const hub = new Listeners(), mac = new Fake(), headset = new Fake();
  hub.addSession(mac);
  hub.addListener(headset);
  headset.close();
  assert.deepEqual(mac.sent.map((m) => m.count), [0, 1, 0]);
  assert.equal(hub.count, 0);
});

test("a closed listener is skipped, not written to", () => {
  const hub = new Listeners(), gone = new Fake();
  hub.addListener(gone);
  gone.readyState = 3;
  hub.mirror({ type: "audio", data: "" });
  assert.equal(gone.sent.length, 0);
});
