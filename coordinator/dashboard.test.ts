import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { PlanStore } from './plans.ts';
import { buildDashboard, dayKey, golfUnlock, planUpdate } from './dashboard.ts';

const NOW = new Date(2026, 8, 26, 15, 0);          // Saturday 26 Sep 2026, 3 pm local
const at = (daysAgo: number, hour = 10) => new Date(2026, 8, 26 - daysAgo, hour).toISOString();

function plans() {
  const store = new PlanStore(mkdtempSync(join(tmpdir(), 'dash-')));
  return store.list().map(p => ({ ...p, approvedAt: at(20, 9) }));   // program started 20 days ago
}
let n = 0;
/** One rehab session: the engine summary and the activity record it also writes. */
function rehab(daysAgo: number, opts: { valid?: number; peak?: number; simulated?: boolean; calibrated?: boolean; hour?: number } = {}) {
  const id = `ex-${n++}`, endedAt = at(daysAgo, opts.hour);
  return {
    summary: { exerciseId: id, prescriptionId: 'arm-elevation-right', endedAt, attempted: 8, valid: opts.valid ?? 8,
      medianValidPeakDeg: opts.peak ?? 52, simulated: !!opts.simulated, calibrated: opts.calibrated ?? true },
    envelope: { activitySessionId: id, activityId: 'rehab.studio', endedAt, completed: (opts.valid ?? 8) >= 8 },
  };
}
const build = (sessions: ReturnType<typeof rehab>[], extra: any[] = []) => buildDashboard({ plans: plans(), now: NOW,
  summaries: sessions.map(s => s.summary), envelopes: [...sessions.map(s => s.envelope), ...extra] });

test('with no real sessions the board is not measured, but today is the real prescription', () => {
  const d = build([rehab(0, { simulated: true }), rehab(1, { calibrated: false })]);
  assert.equal(d.measured, false, 'simulated and uncalibrated sessions are not the patient\'s work');
  assert.equal(d.streakDays, 0);
  assert.deepEqual(d.today.map(t => [t.activityId, t.title, t.detail]), [
    ['rehab.studio', 'Shoulder raise', '8 shoulder raises · right · to 45°'],
    ['rehab.studio', 'Biceps curl', '10 biceps curls · right · to 90°'],
    ['golf.adaptive', 'Golf', '9 holes with a friend'],
  ]);
  assert.equal(d.goal, 'Play golf again with their best friend'); assert.equal(d.targetDeg, 45);
  assert.equal(d.programDay, 21);
});

test('streaks count consecutive days, stay alive until today ends, and remember the best run', () => {
  // Best run: 10, 9, 8 days ago. Current: 3, 2, 1 days ago (nothing yet today).
  const d = build([10, 9, 8, 3, 2, 1].map(x => rehab(x)));
  assert.equal(d.measured, true);
  assert.equal(d.streakDays, 3); assert.equal(d.bestStreakDays, 3);
  const withToday = build([10, 9, 8, 3, 2, 1, 0].map(x => rehab(x)));
  assert.equal(withToday.streakDays, 4); assert.equal(withToday.bestStreakDays, 4);
  assert.equal(build([2, 1].map(x => rehab(x))).streakDays, 2);
  assert.equal(build([3].map(x => rehab(x))).streakDays, 0, 'a missed day breaks it');
});

test('week, calendar and today reflect real sessions', () => {
  // Saturday: this week (from Monday) has Mon, Wed and today; one day has two sessions.
  const golf = { activitySessionId: 'golf-1', activityId: 'golf.adaptive', endedAt: at(0, 12), completed: true };
  const d = build([rehab(5), rehab(3), rehab(3, { hour: 16 }), rehab(0, { valid: 8 }), rehab(9)], [golf]);
  assert.equal(d.weekSessionsDone, 3, 'distinct active days since Monday');
  const cell = (daysAgo: number) => d.calendar.find(c => c.date === dayKey(new Date(2026, 8, 26 - daysAgo)))!;
  assert.equal(cell(3).level, 2); assert.equal(cell(0).level, 2); assert.equal(cell(4).level, 0);
  assert.equal(d.calendar.at(-1)!.date, dayKey(NOW)); assert.equal(new Date(`${d.calendar[0].date}T00:00`).getDay(), 0, 'starts on a Sunday');
  assert.deepEqual(d.today.map(t => t.done), [true, false, true], 'raise done (8 valid), curl not started, golf played');
  assert.deepEqual(d.history.map(h => h.label), ['Sep 17', 'Sep 21', 'Sep 23', 'Sep 23', 'Sep 26']);
});

test('the reach baseline is the median of the first measured week, not the oldest of the last eight', () => {
  // Week one: 30, 32, 34 (median 32). Then a busy day months later, whose sessions fill the last eight.
  const early = [rehab(100, { peak: 34 }), rehab(102, { peak: 30 }), rehab(104, { peak: 32 })];
  const busy = [71, 54, 63, 61, 57, 58, 59, 65].map((peak, i) => rehab(0, { peak, hour: 8 + i }));
  const d = build([...early, ...busy]);
  assert.equal(d.history.length, 8); assert.equal(d.history[0].medianPeakDeg, 71);
  assert.equal(d.baselinePeakDeg, 32, 'a session 8+ days after the first is not week one');
  assert.equal(build([]).baselinePeakDeg, null);
});

test('rehab unlocks golf: swing power follows the level inside the envelope', () => {
  const store = new PlanStore(mkdtempSync(join(tmpdir(), 'unlock-')));
  const day1 = golfUnlock(store.active());                        // seed: 45° in a 40–90° envelope, 5° steps
  assert.equal(day1.level, 2); assert.equal(day1.levels, 11); assert.equal(day1.swingPowerCap, .64);
  assert.equal(day1.message, 'Rehab level 2 of 11 · 64% of a full drive');
  const up = store.approve({ rationale: 'level up', changes: { 'arm-elevation-right': { params: { targetDeg: 70 } } } });
  assert.equal(golfUnlock(up).swingPowerCap, .84);
  const top = store.approve({ rationale: 'top of the envelope', changes: { 'arm-elevation-right': { params: { targetDeg: 90 } } } });
  assert.equal(golfUnlock(top).swingPowerCap, 1); assert.equal(golfUnlock(top).message, 'Full drive unlocked');
  const noProgression = store.approve({ rationale: 'golf only', activities: [{ activityId: 'golf.adaptive', exerciseKind: null, targetCount: 9 }] });
  assert.equal(golfUnlock(noProgression).swingPowerCap, 1, 'nothing measured: golf is unchanged');
});

test('a plan change the patient has not seen is announced from the visit board, and quiet once seen', () => {
  // A level-up: the shoulder raise's target moves on, with the reason the patient should hear.
  const store = new PlanStore(mkdtempSync(join(tmpdir(), 'dash-update-')));
  const first = store.active(), raise = first.activities.find(a => a.exerciseKind)!;
  store.approve({ rationale: 'Two good sessions in the safe band.', approvedBy: 'Auto-progression', origin: 'auto-progression',
    changes: { [raise.id]: { params: { targetDeg: Number(raise.params.targetDeg) + 5 } } } });
  const all = store.list();
  const active = all[all.length - 1];
  const update = planUpdate({ plans: all, notes: [], seen: { planVersion: first.version, at: new Date().toISOString() } });
  assert.ok(update, 'a version newer than the one seen is announced');
  assert.equal(update!.fromVersion, first.version); assert.equal(update!.origin, 'auto-progression');
  assert.equal(update!.planVersion, active.version);
  assert.ok(update!.changes.length >= 1 && update!.changes.length <= 3);
  assert.ok(update!.changes.every(c => c.heading && c.detail), 'every change says what and how, in the visit\'s words');
  assert.match(update!.headline, /plan/);
  assert.equal(planUpdate({ plans: all, notes: [], seen: { planVersion: active.version, at: new Date().toISOString() } }), null);
  assert.equal(planUpdate({ plans: all.slice(0, 1), notes: [], seen: null }), null, 'one version: nothing changed');
});
