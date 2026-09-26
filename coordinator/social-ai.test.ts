// The model's reply is the untrusted part of this feature: it can name someone
// who is not in the roster, repeat a person, or answer with the wrong types.
// None of that should reach the panel, and none of it needs a network call to
// check. The request itself is covered by the degrade path — with no key
// configured, every entry point must return null rather than throw.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { normaliseSpotlight, daysSince, available, spotlight, recap } from './social-ai.ts';

const people = [
  { id: 'maya', displayName: 'Maya', daysSinceActive: 3, daysSinceTheyWrote: 3, daysSinceIWrote: 9, unread: 1, sample: false },
  { id: 'arun', displayName: 'Arun', daysSinceActive: 0, daysSinceTheyWrote: null, daysSinceIWrote: null, unread: 0, sample: true },
];

test('a choice nobody made falls back to a real person', () => {
  // Spotlighting an id that is not in the roster would surface nobody at all.
  const out = normaliseSpotlight({ choose: 'nobody', activity: [] }, people);
  assert.equal(out.choose, 'maya');
});

test('lines about strangers, repeats and non-strings are dropped', () => {
  const out = normaliseSpotlight({
    choose: 'arun',
    activity: [
      { id: 'ghost', line: 'was never here' },
      { id: 'maya', line: '  returned after three days  ' },
      { id: 'maya', line: 'a second line for the same person' },
      { id: 'arun', line: 42 },
      null,
    ],
  }, people);
  assert.equal(out.choose, 'arun');
  assert.deepEqual(out.activity, [{ id: 'maya', line: 'returned after three days' }]);
});

test('a runaway line is cut to something a row can hold', () => {
  const out = normaliseSpotlight(
    { choose: 'maya', activity: [{ id: 'maya', line: 'x'.repeat(500) }] }, people);
  assert.equal(out.activity[0].line.length, 80);
});

test('a missing or malformed reply still yields a usable answer', () => {
  for (const input of [null, undefined, {}, { activity: 'not an array' }]) {
    const out = normaliseSpotlight(input, people);
    assert.equal(out.choose, 'maya');
    assert.deepEqual(out.activity, []);
  }
});

test('days are whole, clamped at zero, and null when never', () => {
  assert.equal(daysSince(null), null);
  assert.equal(daysSince('not a date'), null);
  assert.equal(daysSince(new Date().toISOString()), 0);
  assert.equal(daysSince(new Date(Date.now() + 60_000).toISOString()), 0);  // clock skew
  assert.equal(daysSince(new Date(Date.now() - 3 * 86400000 - 1000).toISOString()), 3);
});

test('with no key configured every entry point declines instead of throwing', async () => {
  const saved = [process.env.ANTHROPIC_API_KEY, process.env.ANTHROPIC_AUTH_TOKEN];
  delete process.env.ANTHROPIC_API_KEY;
  delete process.env.ANTHROPIC_AUTH_TOKEN;
  try {
    assert.equal(available(), false);
    assert.equal(await spotlight(people), null);
    assert.equal(await recap('Maya', [
      { fromMe: false, kind: 'Nice one', text: '', at: new Date().toISOString() },
      { fromMe: true, kind: null, text: 'thank you', at: new Date().toISOString() },
      { fromMe: false, kind: 'With you', text: '', at: new Date().toISOString() },
    ]), null);
  } finally {
    if (saved[0]) process.env.ANTHROPIC_API_KEY = saved[0];
    if (saved[1]) process.env.ANTHROPIC_AUTH_TOKEN = saved[1];
  }
});
