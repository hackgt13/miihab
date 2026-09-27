// A milestone is a fact about turning up with an id that never changes, so a share or a "not now" is final.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { milestoneFrom, MilestoneStore } from './milestones.ts';

const day = (streakDays: number, bestStreakDays = streakDays, weekSessionsDone = 0) =>
  ({ streakDays, bestStreakDays, weekSessionsDone, weekSessionsGoal: 5 });

test('streak landmarks come first and are named by the day the run started', () => {
  const now = new Date(2026, 8, 26, 15);
  assert.deepEqual(milestoneFrom(day(7, 9, 5), now), { id: 'streak-7-2026-09-20', fact: '7 days in a row' });
  // The same run a day later is a new best, not the same milestone again.
  assert.equal(milestoneFrom(day(8, 8), new Date(2026, 8, 27))?.id, 'best-8-2026-09-20');
});

test('a full week counts only when nothing streak-shaped does, and nothing is not a milestone', () => {
  const now = new Date(2026, 8, 26);
  assert.equal(milestoneFrom(day(2, 9, 5), now)?.id, 'week-2026-09-21');
  assert.equal(milestoneFrom(day(2, 9, 4), now), null);
  assert.equal(milestoneFrom(day(0, 0, 0), now), null);
});

test('an answer is remembered across restarts', () => {
  const dir = mkdtempSync(join(tmpdir(), 'milestones-'));
  new MilestoneStore(dir).answer('streak-7-2026-09-20', 'shared');
  assert.equal(new MilestoneStore(dir).answered('streak-7-2026-09-20'), true);
  assert.equal(new MilestoneStore(dir).answered('week-2026-09-21'), false);
});
