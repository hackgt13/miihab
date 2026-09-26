// Rep qualities: hold at the top, tempo by phase, control (hitches) and set consistency — judged on
// the same trace for any exercise, never deciding whether a rep counts.
import test from 'node:test';
import assert from 'node:assert/strict';
import { RepSession, type ImuSample } from './kind.ts';
import { armElevation, elbowFlexion } from './arm-elevation.ts';
import { bindQualities, qualityIdsFor, qualityLimits, phaseOf, beginTrace, stepTrace, QUALITIES } from './quality.ts';
import { createSession } from './registry.ts';
import { PlanStore } from '../plans.ts';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

type Q = [number, number, number, number];
/** A rotation about the device x-axis by `deg` reads back as exactly `deg` of tilt from rest. */
const tilt = (deg: number): Q => { const h = deg * Math.PI / 360; return [Math.sin(h), 0, 0, Math.cos(h)]; };

/** A scripted rep: `[deg, ms]` segments the arm moves through linearly, sampled at 25 Hz. */
type Segment = [deg: number, ms: number];
class Stream {
  samples: ImuSample[] = []; t = 0;
  at(deg: number, moving: boolean) {
    this.samples.push({ quaternion: tilt(deg), rotationRate: moving ? [1.2, 0, 0] : [0.01, 0, 0], hostMonotonicMs: (this.t += 40) });
  }
  still(deg: number, ms: number) { for (let i = 0; i < ms / 40; i++) this.at(deg, false); return this; }
  move(from: number, to: number, ms: number) { const n = Math.max(1, Math.round(ms / 40)); for (let i = 1; i <= n; i++) this.at(from + (to - from) * i / n, true); return this; }
  /** Up to `peak` over raiseMs, hold there holdMs, down over lowerMs, then rest. */
  rep(peak: number, { raiseMs = 2000, holdMs = 2000, lowerMs = 3000 } = {}) {
    return this.move(0, peak, raiseMs).still(peak, holdMs).move(peak, 0, lowerMs).still(0, 800);
  }
}
const session = (kind = armElevation, params: Record<string, unknown> = {}, qualityParams: Record<string, unknown> = {}) =>
  new RepSession(kind, { side: 'right', targetDeg: 45, targetMaxDeg: 60, prescribedReps: 8, holdMs: 400, ...params } as any,
    bindQualities(kind.id, qualityParams));
function drive(s: RepSession<any>, stream: Stream) {
  const lives: any[] = [];
  for (const imu of stream.samples) { s.pushFused({ tMs: imu.hostMonotonicMs, imu }); if (s.live) lives.push(s.live); }
  return { summary: s.summary() as Record<string, any>, lives, reps: s.events.filter(e => e.type === 'rep.completed') as any[] };
}
const calibrate = () => new Stream().still(0, 2000);

test('a rep made to the tempo, held at the top and lowered smoothly passes every quality', () => {
  const { summary, reps, lives } = drive(session(), calibrate().rep(52));
  assert.equal(reps.length, 1);
  const q = reps[0].quality;
  assert.equal(reps[0].valid, true);
  assert.ok(q.hold.ok && q.hold.longestMs >= 2000, `held ${q.hold.longestMs}ms`);
  assert.deepEqual([q.tempo.raise, q.tempo.lower], ['good', 'good']);
  assert.ok(Math.abs(q.tempo.raiseMs - 2000 * 45 / 52) < 250, `raise ${q.tempo.raiseMs}ms`);   // rest to target at the speed seen
  assert.ok(Math.abs(q.tempo.lowerMs - 3000) < 400, `lower ${q.tempo.lowerMs}ms`);            // peak to rest at the speed seen
  assert.equal(q.control.hitches, 0);
  assert.equal(reps[0].streak, 1);
  assert.ok(reps[0].score > .85, `score ${reps[0].score}`);
  // While the rep ran, the hold timer was live and reached its target.
  const holding = lives.filter(l => l.hold?.holding);
  assert.ok(holding.length > 0 && holding.at(-1).hold.met, 'hold readout reached its target live');
  assert.ok(lives.some(l => l.tempo?.phase === 'raise') && lives.some(l => l.tempo?.phase === 'lower'));
  assert.equal(summary.quality.hold.metReps, 1);
  assert.equal(summary.quality.streak.best, 1);
  assert.equal(summary.quality.formScore, reps[0].score);
});

test('a dropped arm is a fast lowering: still counted, but the tempo says so and the streak breaks', () => {
  const stream = calibrate().rep(52).rep(52, { lowerMs: 600 }).rep(52);
  const { summary, reps } = drive(session(), stream);
  assert.deepEqual(reps.map(r => r.valid), [true, true, true], 'validity is the engine\'s, not the tempo\'s');
  assert.deepEqual(reps.map(r => r.quality.tempo.lower), ['good', 'fast', 'good']);
  assert.deepEqual(reps.map(r => r.quality.tempo.ok), [true, false, true]);
  assert.deepEqual(reps.map(r => r.streak), [1, 0, 1]);
  assert.equal(summary.quality.tempo.fastLowers, 1);
  assert.equal(summary.quality.tempo.controlledLowers, 2);
  assert.ok(reps[1].score < reps[0].score);
});

test('the live tempo readout nudges "slower" while the arm is coming down too fast', () => {
  // A higher target leaves more arc between the top and the rest band, so the nudge comes with arc to spare.
  const high = () => session(armElevation, { targetDeg: 80, targetMaxDeg: 95 });
  const { lives } = drive(high(), calibrate().rep(85, { lowerMs: 1000 }));
  const descending = lives.filter(l => l.tempo?.pace != null && l.tempo.phase !== 'raise');
  const nudgedAt = descending.find(l => l.tempo.guidance === 'slower');
  assert.ok(nudgedAt, 'nudged during a 1s lowering against a 3s tempo');
  assert.ok(nudgedAt.tempo.elapsedMs < 500, `said early, at ${nudgedAt.tempo.elapsedMs}ms into the descent`);
  const { lives: good } = drive(high(), calibrate().rep(85));
  assert.ok(good.filter(l => l.tempo?.phase !== 'raise').every(l => l.tempo.guidance === null), 'no nudge at the tempo');
  // With the seed plan's 45° target the visible arc is short, but a dropped arm still gets the word before rest.
  assert.ok(drive(session(), calibrate().rep(52, { lowerMs: 700 })).lives.some(l => l.tempo?.guidance === 'slower'));
});

test('a hold that never reaches its target is scored by how long it lasted, and drifting over the ceiling stops the clock', () => {
  const short = drive(session(), calibrate().rep(52, { holdMs: 800 })).reps[0].quality.hold;
  assert.equal(short.ok, false);
  // 0.8s still at the top, plus the slow travel through the band either side of it.
  assert.ok(short.longestMs >= 800 && short.longestMs < 1900, `held ${short.longestMs}ms`);
  assert.ok(short.score > .4 && short.score < .95);
  // Up into the band for a second, over the ceiling for two, back in the band for a second: no single hold made two seconds.
  const over = calibrate().move(0, 52, 2000).still(52, 1000).move(52, 75, 300).still(75, 2000).move(75, 52, 300).still(52, 1000).move(52, 0, 3000).still(0, 800);
  const drifted = drive(session(), over).reps[0].quality.hold;
  assert.equal(drifted.ok, false, 'time above the ceiling is not a hold in range');
  assert.ok(drifted.longestMs < 1800, `longest ${drifted.longestMs}ms`);
});

test('a hitch on the way up or down is counted; the wobble of an honest hold is not', () => {
  // The engine only sees a rep above the rest band (30°, plus 5° of hysteresis), so the hitches happen above it.
  const hitched = calibrate()
    .move(0, 42, 1600).move(42, 36, 200).move(36, 52, 600)          // stalls and dips on the way up
    .still(52, 2000)
    .move(52, 36, 900).move(36, 44, 200).move(44, 0, 1900).still(0, 800);   // catches and bounces on the way down
  const q = drive(session(), hitched).reps[0].quality.control;
  assert.deepEqual([q.raiseHitches, q.lowerHitches, q.ok], [1, 1, false]);
  const wobbly = calibrate().move(0, 52, 2000).still(52, 500).move(52, 47, 200).move(47, 52, 200).still(52, 1200).move(52, 0, 3000).still(0, 800);
  assert.equal(drive(session(), wobbly).reps[0].quality.control.hitches, 0, 'a dip inside the band during the hold is not a hitch');
  assert.equal(drive(session(), calibrate().rep(52)).reps[0].quality.control.hitches, 0);
});

test('peaks that shrink across the set read as fatigue; a steady set does not', () => {
  let s = calibrate();
  for (const peak of [57, 56, 56, 50, 48, 46]) s = s.rep(peak);
  const tired = drive(session(), s).summary.quality.consistency;
  assert.equal(tired.reps, 6);
  assert.ok(tired.fatigueDropDeg >= 9, `drop ${tired.fatigueDropDeg}`);
  assert.equal(tired.fatigued, true);
  let e = calibrate();
  for (let i = 0; i < 6; i++) e = e.rep(54);
  const even = drive(session(), e).summary.quality.consistency;
  assert.equal(even.fatigued, false);
  assert.ok(even.peakSpreadDeg < 1);
  assert.equal(drive(session(), calibrate().rep(52).rep(52)).summary.quality.consistency.fatigueDropDeg, null, 'two reps are not a trend');
});

test('a rep that did not count carries its verdicts but no score, and never extends the streak', () => {
  const { reps, summary } = drive(session(), calibrate().rep(52).rep(38).rep(52));
  assert.deepEqual(reps.map(r => r.valid), [true, false, true]);
  assert.equal(reps[1].score, null);
  assert.ok('tempo' in reps[1].quality && 'hold' in reps[1].quality);
  assert.deepEqual(reps.map(r => r.streak), [1, 0, 1]);
  assert.equal(summary.reps[1].score, null);
  assert.equal(summary.attempted, 3); assert.equal(summary.valid, 2);
});

test('qualities compose per exercise: the catalog picks them, a plan param tunes them, and an unknown kind gets all of them', () => {
  assert.deepEqual(qualityIdsFor('arm-elevation.v1'), ['hold', 'tempo', 'control', 'consistency']);
  assert.deepEqual(qualityIdsFor('elbow-flexion.v1'), ['tempo', 'control', 'consistency']);
  assert.deepEqual(qualityIdsFor('shoulder-raise.v1'), QUALITIES.map(q => q.id));
  assert.deepEqual(qualityLimits('elbow-flexion.v1'), { raiseMs: [500, 10000], lowerMs: [500, 10000], hitchDeg: [2, 15], fatigueDropDeg: [3, 30] });
  const tuned = bindQualities('arm-elevation.v1', { holdTargetMs: 3000, lowerMs: '4000', side: 'right', targetDeg: 45 });
  assert.deepEqual(tuned.map(b => b.config), [{ holdTargetMs: 3000 }, { raiseMs: 2000, lowerMs: 4000 }, { hitchDeg: 4 }, { fatigueDropDeg: 8 }]);
  // The same 2s hold that passed the default target fails a 3s one; nothing else about the rep changes.
  const { reps } = drive(session(armElevation, {}, { holdTargetMs: 3000 }), calibrate().rep(52));
  assert.equal(reps[0].valid, true); assert.equal(reps[0].quality.hold.ok, false); assert.equal(reps[0].quality.hold.targetMs, 3000);
  // A curl is not judged on a hold, and its summary says which qualities it was judged on.
  const curl = drive(session(elbowFlexion, { targetDeg: 90, targetMaxDeg: 110, holdMs: 0 }), calibrate().rep(95, { holdMs: 200 }));
  assert.equal(curl.reps[0].quality.hold, undefined);
  assert.deepEqual(Object.keys(curl.summary.quality), ['tempo', 'control', 'consistency', 'streak', 'formScore']);
  assert.equal(curl.reps[0].streak, 1, 'no hold to fail, so a good curl keeps the streak');
  const registry = createSession('arm-elevation.v1', { side: 'right', targetDeg: 45 }, { holdTargetMs: 2500 });
  assert.deepEqual(registry.qualities.configs[0], { id: 'hold', holdTargetMs: 2500 });
  // A raise prescribed with no hold has no hold to judge; one prescribed with a 5s hold is asked for 5s, not the 2s default.
  assert.deepEqual(session(armElevation, { holdMs: 0 }).qualities.configs.map(c => c.id), ['tempo', 'control', 'consistency']);
  const endurance = drive(session(armElevation, { holdMs: 5000 }), calibrate().rep(52, { holdMs: 5200 })).reps[0].quality.hold;
  assert.equal(endurance.targetMs, 5000); assert.equal(endurance.ok, true);
});

test('a plan may prescribe a quality config, bounded like any param', () => {
  const store = new PlanStore(mkdtempSync(join(tmpdir(), 'plans-')));
  const v2 = store.approve({ approvedBy: 'test', rationale: 'hold longer',
    changes: { 'arm-elevation-right': { params: { holdTargetMs: 3000, lowerMs: 4000 } } } } as any);
  const x = v2.activities.find(a => a.id === 'arm-elevation-right')!;
  assert.equal(x.params.holdTargetMs, 3000); assert.equal(x.params.lowerMs, 4000);
  assert.throws(() => store.approve({ approvedBy: 'test', rationale: 'silly', changes: { 'arm-elevation-right': { params: { holdTargetMs: 60000 } } } } as any),
    /holdTargetMs must be between 500 and 10000/);
});

test('the phase of a rep is read from its trace alone', () => {
  const p = { targetDeg: 45, hysteresisDeg: 5 } as any;
  const t = beginTrace(1, 0, 32);
  assert.equal(phaseOf(t, p), 'raise');
  stepTrace(t, 100, 40, p); assert.equal(phaseOf(t, p), 'raise');
  stepTrace(t, 200, 46, p); assert.equal(phaseOf(t, p), 'hold'); assert.equal(t.reachedMs, 200);
  stepTrace(t, 300, 42, p); assert.equal(phaseOf(t, p), 'hold', 'inside the hysteresis is still the hold');
  stepTrace(t, 400, 36, p); assert.equal(phaseOf(t, p), 'lower'); assert.equal(t.leftMs, 300);
  stepTrace(t, 500, 46, p); assert.equal(phaseOf(t, p), 'lower', 'a bounce back into the band is still the lowering');
  const short = beginTrace(2, 0, 32);
  stepTrace(short, 100, 40, p); stepTrace(short, 200, 33, p);
  assert.equal(phaseOf(short, p), 'raise', 'a rep that has not reached the target is still raising, whatever it does');
});
