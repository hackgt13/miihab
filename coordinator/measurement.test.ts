import test from 'node:test';
import assert from 'node:assert/strict';
import { ShoulderRaiseSession } from './measurement.ts';
import { frame } from './synthetic-pose.ts';

/** Rest 1 s, then each rep: rise 0.6 s, hold `hold` s at peak, lower 0.6 s, rest 0.5 s. 30 fps. */
function run(s: ShoulderRaiseSession, reps: { peak: number; lean?: number; hold?: number; dropout?: boolean }[], jitter = 0) {
  let t = 0; const dt = 1000 / 30;
  const step = (arm: number, lean = 0, hide = false) => { s.push(frame(t, arm, lean, { hideElbow: hide, jitter })); t += dt; };
  for (let i = 0; i < 30; i++) step(5);
  for (const r of reps) {
    const lean = r.lean ?? 0, hold = r.hold ?? .5;
    for (let i = 0; i <= 18; i++) step(5 + (r.peak - 5) * i / 18, lean * i / 18);
    for (let i = 0; i < hold * 30; i++) step(r.peak, lean, r.dropout && i > 2 && i < 14);
    for (let i = 18; i >= 0; i--) step(5 + (r.peak - 5) * i / 18, lean * i / 18);
    for (let i = 0; i < 15; i++) step(5);
  }
  return s.summary();
}

test('five clean reps at target are all valid with correct peak', () => {
  const s = run(new ShoulderRaiseSession({ side: 'right', targetDeg: 80, prescribedReps: 5 }), Array(5).fill({ peak: 90 }));
  assert.equal(s.calibrated, true);
  assert.equal(s.attempted, 5); assert.equal(s.valid, 5); assert.equal(s.completed, 5);
  assert.ok(Math.abs(s.medianValidPeakDeg! - 90) < 1, `median peak ${s.medianValidPeakDeg}`);
});

test('short rep is counted as attempted but invalid', () => {
  const s = run(new ShoulderRaiseSession({ side: 'right', targetDeg: 80 }), [{ peak: 90 }, { peak: 60 }, { peak: 90 }]);
  assert.equal(s.attempted, 3); assert.equal(s.valid, 2);
  assert.deepEqual(s.invalidReasons, { did_not_reach_target: 1 });
  assert.ok(Math.abs(s.reps[1].peakDeg - 60) < 1);
});

test('reaching the target by leaning the trunk is flagged as compensation', () => {
  const s = run(new ShoulderRaiseSession({ side: 'right', targetDeg: 80 }), [{ peak: 90 }, { peak: 90, lean: 20 }]);
  assert.equal(s.valid, 1); assert.deepEqual(s.invalidReasons, { trunk_compensation: 1 });
  assert.ok(s.reps[1].trunkMaxDeg > 15);
});

test('losing the elbow mid-rep invalidates that rep and reports tracking loss', () => {
  const s = run(new ShoulderRaiseSession({ side: 'right', targetDeg: 80 }), [{ peak: 90, hold: .8, dropout: true }, { peak: 90 }]);
  assert.equal(s.trackingLossEvents, 1);
  assert.equal(s.reps[0].valid, false); assert.equal(s.reps[0].reason, 'tracking_lost');
  assert.equal(s.reps[1].valid, true);
});

test('a brief touch of the target without holding does not count', () => {
  const s = run(new ShoulderRaiseSession({ side: 'right', targetDeg: 80, holdMs: 400 }), [{ peak: 85, hold: .1 }]);
  assert.equal(s.valid, 0); assert.deepEqual(s.invalidReasons, { did_not_reach_target: 1 });
});

test('landmark jitter around rest does not create phantom reps', () => {
  const s = new ShoulderRaiseSession({ side: 'right', targetDeg: 80 });
  for (let i = 0; i < 300; i++) s.push(frame(i * 33, 5 + 22 + Math.sin(i) * 6, 0, { jitter: .01 }));
  assert.equal(s.summary().attempted, 0);
});

test('measurement ignores the untracked side and rejects collapsed arms', () => {
  const s = new ShoulderRaiseSession({ side: 'right', targetDeg: 80 });
  run(s, [{ peak: 90 }]);
  const f = frame(99999, 90); f.worldLandmarks[14] = { ...f.worldLandmarks[12], y: f.worldLandmarks[12].y + .02, visibility: .99 };
  assert.equal(s.measure(f), null);
});

test('config rejects a target inside the rest band', () => {
  assert.throws(() => new ShoulderRaiseSession({ side: 'left', targetDeg: 30 }));
});
