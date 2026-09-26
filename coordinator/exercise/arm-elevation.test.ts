import test from 'node:test';
import assert from 'node:assert/strict';
import { ArmElevationSession, armElevation } from './arm-elevation.ts';
import { frame } from '../synthetic-pose.ts';
import type { ImuSample } from './kind.ts';

/** Attitude tilting the device +Z axis by `deg` about +X, so elevation from rest equals `deg`. */
const attitude = (deg: number, tMs = 0): ImuSample => {
  const h = deg * Math.PI / 360;
  return {quaternion: [Math.sin(h), 0, 0, Math.cos(h)], rotationRate: [0, 0, 0], hostMonotonicMs: tMs};
};

/** Rest 1 s, then each rep: raise 0.6 s, hold, lower 0.6 s, rest 0.5 s. 30 fps. */
function run(s: ArmElevationSession, reps: {peak: number; lean?: number; hold?: number; blind?: boolean}[]) {
  let t = 0; const dt = 1000 / 30;
  const step = (deg: number, lean = 0, blind = false) =>
    { s.pushFused({tMs: t, imu: attitude(deg, t), pose: blind ? null : frame(t, 5, lean)}); t += dt; };
  for (let i = 0; i < 30; i++) step(0);
  for (const r of reps) {
    const hold = r.hold ?? .5, lean = r.lean ?? 0;
    for (let i = 0; i <= 18; i++) step(r.peak * i / 18, lean * i / 18);
    for (let i = 0; i < hold * 30; i++) step(r.peak, lean, r.blind);
    for (let i = 18; i >= 0; i--) step(r.peak * i / 18, lean * i / 18);
    for (let i = 0; i < 15; i++) step(0);
  }
  return s.summary() as Record<string, any>;
}

test('elevation comes from the IMU and matches the commanded angle', () => {
  const s = run(new ArmElevationSession({side: 'right', targetDeg: 90, prescribedReps: 4}), Array(4).fill({peak: 110}));
  assert.equal(s.calibrated, true);
  assert.equal(s.attempted, 4); assert.equal(s.valid, 4);
  assert.ok(Math.abs(s.medianValidPeakDeg - 110) < 1, `median peak ${s.medianValidPeakDeg}`);
});

test('an arm that does not reach the prescribed elevation does not count', () => {
  const s = run(new ArmElevationSession({side: 'right', targetDeg: 90}), [{peak: 110}, {peak: 60}]);
  assert.equal(s.attempted, 2); assert.equal(s.valid, 1);
  assert.deepEqual(s.invalidReasons, {did_not_reach_target: 1});
});

test('the camera catches the lean the IMU cannot see', () => {
  // Same IMU trace both reps: inclination alone cannot tell these apart, which is why the camera
  // polices compensation whenever it can see the patient.
  const s = run(new ArmElevationSession({side: 'right', targetDeg: 90}), [{peak: 110}, {peak: 110, lean: 20}]);
  assert.equal(s.valid, 1);
  assert.deepEqual(s.invalidReasons, {trunk_compensation: 1});
  assert.ok(s.trunkDeviation.maxDuringRepsDeg > 15);
});

test('losing the camera mid-rep keeps the IMU scoring; only the lean check pauses', () => {
  // The demo is IMU-first: the AirPod owns the rep, the camera is an optional cheat check.
  const s = run(new ArmElevationSession({side: 'right', targetDeg: 90}), [{peak: 110, hold: .8, blind: true}, {peak: 110}]);
  assert.equal(s.trackingLossEvents, 0);
  assert.deepEqual(s.reps.map((r: any) => r.valid), [true, true]);
});

test('a collapsed or unnormalised attitude is rejected, not trusted', () => {
  const s = new ArmElevationSession({side: 'right', targetDeg: 90});
  const bad = {quaternion: [0, 0, 0, 0] as [number, number, number, number], rotationRate: [0, 0, 0] as [number, number, number], hostMonotonicMs: 0};
  assert.equal(s.observeFused({tMs: 0, imu: bad, pose: frame(0, 5)}), null);
  assert.equal(s.observeFused({tMs: 0, imu: null, pose: frame(0, 5)}), null, 'imu is required');
  assert.notEqual(s.observeFused({tMs: 0, imu: attitude(0), pose: null}), null, 'the camera is optional');
});

test('this exercise requires only the IMU', () => {
  assert.deepEqual([...armElevation.requires], ['imu']);
});
