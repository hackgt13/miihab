import test from 'node:test';
import assert from 'node:assert/strict';
import { SeatedTrunkRotationSession, seatedTrunkRotation } from './seated-trunk-rotation.ts';
import { frame } from '../synthetic-pose.ts';

/** Rest 1 s, then each rep: rotate 0.6 s, hold at peak, return 0.6 s, rest 0.5 s. 30 fps. */
function run(s: SeatedTrunkRotationSession, reps: { peak: number; lean?: number; hold?: number }[]) {
  let t = 0; const dt = 1000 / 30;
  const step = (rot: number, lean = 0) => { s.push(frame(t, 5, lean, { rotateDeg: rot })); t += dt; };
  for (let i = 0; i < 30; i++) step(0);
  for (const r of reps) {
    const hold = r.hold ?? .5, lean = r.lean ?? 0;
    for (let i = 0; i <= 18; i++) step(r.peak * i / 18, lean * i / 18);
    for (let i = 0; i < hold * 30; i++) step(r.peak, lean);
    for (let i = 18; i >= 0; i--) step(r.peak * i / 18, lean * i / 18);
    for (let i = 0; i < 15; i++) step(0);
  }
  return s.summary() as Record<string, any>;
}

test('five clean rotations to target are all valid with correct peak', () => {
  const s = run(new SeatedTrunkRotationSession({ side: 'left', targetDeg: 45, prescribedReps: 5 }), Array(5).fill({ peak: 60 }));
  assert.equal(s.calibrated, true);
  assert.equal(s.attempted, 5); assert.equal(s.valid, 5); assert.equal(s.completed, 5);
  assert.ok(Math.abs(s.medianValidPeakDeg - 60) < 2, `median peak ${s.medianValidPeakDeg}`);
});

test('rotating short of the target counts as attempted but invalid', () => {
  const s = run(new SeatedTrunkRotationSession({ side: 'left', targetDeg: 45 }), [{ peak: 60 }, { peak: 30 }, { peak: 60 }]);
  assert.equal(s.attempted, 3); assert.equal(s.valid, 2);
  assert.deepEqual(s.invalidReasons, { did_not_reach_target: 1 });
});

test('rotating the wrong way is not partial credit', () => {
  const s = run(new SeatedTrunkRotationSession({ side: 'right', targetDeg: 45 }), [{ peak: 60 }]);
  assert.equal(s.attempted, 0, 'a left rotation must not start a right-side rep');
});

test('leaning to reach the target is flagged with this exercise own compensation reason', () => {
  const s = run(new SeatedTrunkRotationSession({ side: 'left', targetDeg: 45 }), [{ peak: 60 }, { peak: 60, lean: 20 }]);
  assert.equal(s.valid, 1);
  assert.deepEqual(s.invalidReasons, { trunk_lean: 1 });
  assert.ok(s.trunkLean.maxDuringRepsDeg > 15, `trunk lean ${s.trunkLean.maxDuringRepsDeg}`);
});

test('the summary carries this exercise own vocabulary, not the shoulder raise one', () => {
  const s = run(new SeatedTrunkRotationSession({ side: 'left', targetDeg: 45 }), [{ peak: 60 }]);
  assert.equal(s.exerciseKind, 'trunk-rotation.v1');
  assert.equal(s.algorithmVersion, 'trunk-rotation.v1');
  assert.ok('trunkLean' in s, 'compensation is published under the kind own key');
  assert.ok(!('trunkDeviation' in s), 'the shoulder raise key must not leak in');
});

test('config rejects a target inside this exercise tighter rest band', () => {
  assert.throws(() => new SeatedTrunkRotationSession({ side: 'left', targetDeg: 12 }));
  assert.equal(seatedTrunkRotation.defaults.restMaxDeg, 10);
});
