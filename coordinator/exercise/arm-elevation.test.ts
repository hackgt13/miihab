import test from 'node:test';
import assert from 'node:assert/strict';
import { ArmElevationSession } from './arm-elevation.ts';
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

test('trunk lean is not measured: the AirPod alone cannot see the trunk, and the camera is off', () => {
  // Same IMU trace both reps. Without camera pose the lean goes unseen, and the summary says unknown, not zero.
  const s = run(new ArmElevationSession({side: 'right', targetDeg: 90}), [{peak: 110}, {peak: 110, lean: 20}]);
  assert.equal(s.valid, 2);
  assert.equal(s.trunkDeviation.meanDeg, null);
});

test('camera frames coming and going change nothing', () => {
  const s = run(new ArmElevationSession({side: 'right', targetDeg: 90}), [{peak: 110, hold: .8, blind: true}, {peak: 110}]);
  assert.equal(s.trackingLossEvents, 0);
  assert.deepEqual(s.reps.map((r: any) => r.valid), [true, true]);
});

test('a collapsed or unnormalised attitude is rejected, not trusted', () => {
  const s = new ArmElevationSession({side: 'right', targetDeg: 90});
  const bad = {quaternion: [0, 0, 0, 0] as [number, number, number, number], rotationRate: [0, 0, 0] as [number, number, number], hostMonotonicMs: 0};
  assert.equal(s.observeFused({tMs: 0, imu: bad, pose: frame(0, 5)}), null);
  assert.equal(s.observeFused({tMs: 0, imu: null, pose: frame(0, 5)}), null, 'imu is required');
  assert.notEqual(s.observeFused({tMs: 0, imu: attitude(0), pose: null}), null, 'no camera needed');
});
