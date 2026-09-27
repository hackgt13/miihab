// The trajectory has to find what the simulator put there, through arbitrary mounts and a drifting heading:
// the reps, the elbow angle, the swing and the sideways drift. And its scores have to move the right way —
// a cheat, a hitch or a short rep scores lower than a clean one, never higher.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { analyseCurl, samplesFromLog } from './trajectory.ts';
import { synthCurl, DEMO_SET, type RepPlan } from './curl-synth.ts';

const clean = (n: number, peak = 120): RepPlan[] => Array.from({ length: n }, () => ({ peak }));
const run = (reps: RepPlan[], extra = {}) => analyseCurl(synthCurl({ reps, seed: 3, ...extra }), { targetDeg: 110 });

test('clean curls: every rep found, the elbow angle recovered, and a near-perfect path', () => {
  const t = run(clean(6));
  assert.equal(t.reps.length, 6);
  for (const r of t.reps) {
    assert.ok(Math.abs(r.peakElbowDeg - 120) < 4, `peak ${r.peakElbowDeg}`);
    assert.ok(r.elbowDriftDeg < 3, `drift ${r.elbowDriftDeg}`);
    assert.ok(r.pathAccuracy > 90, `path ${r.pathAccuracy}`);
    assert.ok(r.smoothness > 90, `smooth ${r.smoothness}`);
  }
  assert.ok(t.set!.score > 90);
  assert.ok(t.meta.hz > 20 && t.meta.hz < 30);
});

test('the heading never matters: drifting yaw leaves every score where it was', () => {
  const still = run(clean(4), { headingDriftDegPerS: 0 }), drifting = run(clean(4), { headingDriftDegPerS: 25 });
  assert.equal(still.reps.length, drifting.reps.length);
  still.reps.forEach((r, i) => {
    assert.ok(Math.abs(r.peakElbowDeg - drifting.reps[i].peakElbowDeg) < 1);
    assert.ok(Math.abs(r.pathAccuracy - drifting.reps[i].pathAccuracy) < 1);
  });
});

test('swinging the upper arm is seen as elbow drift, and costs path accuracy and stability', () => {
  const t = run([{ peak: 120 }, { peak: 120, swing: 25 }]);
  const [good, swung] = t.reps;
  assert.ok(Math.abs(swung.elbowDriftDeg - 25) < 4, `drift ${swung.elbowDriftDeg}`);
  assert.ok(Math.abs(swung.peakElbowDeg - 120) < 5, 'the elbow angle is still the elbow angle');
  assert.ok(swung.pathAccuracy < good.pathAccuracy - 15);
  assert.ok(swung.stability < good.stability - 50);
  assert.ok(swung.score < good.score);
});

test('sideways drift is seen against the set\'s own plane, where gravity can see it', () => {
  // One rep drifting in a set of clean ones: the plane is the set's, so the odd rep stands out. Low in the curl a
  // sideways drift moves the forearm's vertical nearly one-for-one.
  const reps = run([{ peak: 40 }, { peak: 40 }, { peak: 40 }, { peak: 40 }, { peak: 40, lateral: 18 }]).reps;
  const seen = Math.asin(Math.cos(40 * Math.PI / 180) * Math.sin(18 * Math.PI / 180)) * 180 / Math.PI;
  const drifted = reps[4];
  assert.ok(drifted.lateralDeg > 8 && drifted.lateralDeg < seen + 3, `lateral ${drifted.lateralDeg}, observable ${seen.toFixed(1)}`);
  assert.ok(reps.slice(0, 4).every(r => r.lateralDeg < drifted.lateralDeg / 2), 'the clean reps show much less');
  // At the top of a full curl the same drift is a turn of heading, which no AirPod can see; it is not invented.
  const high = run([{ peak: 90 }, { peak: 90 }, { peak: 90 }, { peak: 90 }, { peak: 90, lateral: 18 }]).reps[4];
  assert.ok(high.lateralDeg < drifted.lateralDeg);
});

test('a hitch on the way down costs smoothness; a short rep costs range', () => {
  const [smooth, hitched, short] = run([{ peak: 120 }, { peak: 120, hitch: true }, { peak: 60 }]).reps;
  assert.ok(hitched.smoothness < smooth.smoothness - 5, `${hitched.smoothness} vs ${smooth.smoothness}`);
  assert.ok(Math.abs(short.range - 100 * 60 / 110) < 5);
  assert.equal(smooth.range, 100);
});

test('the ideal circle hangs from the elbow, and the points carry which rep they belong to', () => {
  const t = run(clean(2));
  const [first, last] = [t.ideal[0], t.ideal[t.ideal.length - 1]];
  assert.ok(Math.abs(first[1] + t.meta.upperArmM + t.meta.forearmM) < 1e-3, 'starts hanging straight down');
  assert.ok(Math.hypot(last[0], last[1] + t.meta.upperArmM) - t.meta.forearmM < 1e-3);
  assert.deepEqual([...new Set(t.points.map(p => p.rep))].sort(), [0, 1, 2]);
});

test('the demo set tells its story in the scores', () => {
  const t = analyseCurl(synthCurl({ reps: DEMO_SET, seed: 11 }), { targetDeg: 110 });
  assert.equal(t.reps.length, DEMO_SET.length);
  const worst = [...t.reps].sort((a, b) => a.score - b.score).slice(0, 3).map(r => r.rep).sort();
  assert.ok(worst.includes(4), 'the swung rep is among the worst');
  assert.ok(t.set!.consistency < 90, 'the tiring end spreads the peaks');
});

test('a session log reads back into samples, and one stream alone gives no trajectory', () => {
  const line = (role: string, t: number) => JSON.stringify({ type: 'motion.sample', role,
    payload: { quaternion: [0, 0, 0, 1], rotationRate: [0, 0, 0], hostMonotonicMs: t } });
  const samples = samplesFromLog([line('imu', 1), line('ref', 2), '{"type":"exercise.event"}', 'garbage'].join('\n'));
  assert.equal(samples.length, 2);
  const onlyUpper = synthCurl({ reps: clean(2) }).filter(s => s.role === 'ref');
  assert.equal(analyseCurl(onlyUpper).set, null);
});

test('live trace: the rep being made, the last few finished ones, the arm now, and only when something changed', async () => {
  const { CurlTrace } = await import('./curl-trace.ts');
  const trace = new CurlTrace(110);
  assert.equal(trace.next(), null, 'nothing yet');
  for (const s of synthCurl({ reps: DEMO_SET, seed: 11 })) trace.push(s.role, s.tMs, { quaternion: [...s.quaternion], rotationRate: [...(s.rotationRate ?? [])] });
  const p = trace.next()!;
  assert.equal(p.reps.length, DEMO_SET.length);
  assert.equal(p.recent.length, 3);
  assert.deepEqual(p.recent.map(r => r.rep), [6, 7, 8]);
  assert.ok(p.recent.every(r => r.path.length <= 40));
  assert.ok(p.live.length > 0 && p.live.length <= 160);
  assert.ok(p.arm && Math.abs(p.arm.elbowDeg) < 10, 'the set ended at rest');
  assert.equal(p.set!.reps, 8);
  assert.equal(trace.next(), null, 'no new samples, nothing to send');
  assert.ok(trace.next(true), 'unless asked for the final picture');
});
