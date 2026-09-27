import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { CoachLog } from './coach-log.ts';

test('the coach feed keeps what was said and measured, per conversation, and survives a restart', () => {
  const dir = mkdtempSync(join(tmpdir(), 'coach-'));
  const log = new CoachLog(dir);
  log.add({ conversation: 'a', source: 'coach', kind: 'llm_reply', content: 'Hey, it is Alex.' });
  log.add({ conversation: 'b', source: 'engine', kind: 'measurement', content: '[rep] Rep 1 (1 of 8 counted): counted, peak 52°' });
  log.add({ conversation: 'b', source: 'coach', kind: 'llm_reply', content: 'Nice, that one counted.' });
  assert.deepEqual(log.latestConversation().map(e => e.content), ['[rep] Rep 1 (1 of 8 counted): counted, peak 52°', 'Nice, that one counted.']);
  assert.equal(new CoachLog(dir).recent().length, 3, 'read back from its file');
  assert.throws(() => log.add({ conversation: 'b', source: 'pharma', kind: 'llm_reply', content: 'x' }), /source/);
  assert.throws(() => log.add({ conversation: 'b', source: 'coach', kind: 'llm_reply', content: '  ' }), /content/);
});
