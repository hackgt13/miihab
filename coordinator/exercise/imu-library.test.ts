// The single-IMU activity library: every spec is prescribable and measurable, and the signed in-ear kinds
// tell direction apart where the unsigned tilt cannot.
import test from 'node:test';
import assert from 'node:assert/strict';
import { IMU_LIBRARY, armHold, armRaise, neckExtension, neckFlexion, neckLateralFlexion, trunkLateralFlexion } from './imu-library.ts';
import { RepSession, type ExerciseKind, type ImuSample, type RepParams } from './kind.ts';
import { LIBRARY } from '../exercises.ts';

type Q = [number, number, number, number];
const axisAngle = (axis: [number, number, number], deg: number): Q => {
  const h = deg * Math.PI / 360, n = Math.hypot(...axis), s = Math.sin(h);
  return [axis[0] / n * s, axis[1] / n * s, axis[2] / n * s, Math.cos(h)];
};
const mul = ([ax, ay, az, aw]: Q, [bx, by, bz, bw]: Q): Q => [
  aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
  aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz];

/** 25 Hz: still rest, then per rep rotate about `axis` to the peak, hold, return, rest. `yaw` twists the whole head. */
function drive(kind: ExerciseKind<any>, params: RepParams, axis: [number, number, number], peaks: number[], opts: { holdS?: number; yaw?: number } = {}) {
  const s = new RepSession(kind, params); let t = 0;
  const yaw = axisAngle([0, 0, 1], opts.yaw ?? 0);
  const at = (deg: number, moving: boolean) => {
    const imu: ImuSample = { quaternion: mul(yaw, axisAngle(axis, deg)), rotationRate: moving ? [1.5, 0, 0] : [0, 0, 0], hostMonotonicMs: (t += 40) };
    s.pushFused({ tMs: t, imu });
  };
  for (let i = 0; i < 30; i++) at(0, false);
  for (const peak of peaks) {
    for (let i = 0; i <= 20; i++) at(peak * i / 20, true);
    for (let i = 0; i < (opts.holdS ?? .6) / .04; i++) at(peak, false);
    for (let i = 20; i >= 0; i--) at(peak * i / 20, true);
    for (let i = 0; i < 15; i++) at(0, false);
  }
  return s.summary() as Record<string, any>;
}
const X: [number, number, number] = [1, 0, 0], Y: [number, number, number] = [0, 1, 0];

test('every library kind has a clinical entry whose defaults sit inside its limits and above its rest band', () => {
  for (const kind of IMU_LIBRARY) {
    const entry = LIBRARY[kind.id];
    assert.ok(entry, `${kind.id} has no LIBRARY entry in exercises.ts`);
    const [lo, hi] = kind.limits.targetDeg;
    assert.ok(lo <= entry.defaults.targetDeg && entry.defaults.targetDeg <= hi, `${kind.id} default target outside ${lo}-${hi}`);
    const [hlo, hhi] = kind.limits.holdMs;
    assert.ok(hlo <= entry.defaults.holdMs && entry.defaults.holdMs <= hhi, `${kind.id} default hold outside ${hlo}-${hhi}`);
    assert.doesNotThrow(() => new RepSession(kind, { side: 'right', targetDeg: lo }), `${kind.id}: lowest target must clear the rest band`);
  }
});

test('every library kind counts a clean rep to its default target and rejects a short one', () => {
  for (const kind of IMU_LIBRARY) {
    const { targetDeg, holdMs } = LIBRARY[kind.id].defaults;
    // The in-ear kinds are signed; drive each the way it counts. +x is toward the right ear, so a positive
    // rotation about it lifts the chin, and a positive rotation about +y (forward) tips the head right.
    const axis = kind === neckLateralFlexion ? Y : X;
    const dir = kind === neckFlexion ? -1 : 1;
    const summary = drive(kind, { side: 'right', targetDeg, holdMs }, axis,
      [dir * (targetDeg + 5), dir * (targetDeg - 4)], { holdS: holdMs / 1000 + .4 });
    assert.equal(summary.calibrated, true, kind.id);
    assert.deepEqual(summary.reps.map((r: any) => r.valid), [true, false], kind.id);
    assert.equal(summary.trunkDeviation.meanDeg, null, `${kind.id}: compensation is unknown, not zero`);
  }
});

test('neck flexion and extension are the same axis read with opposite signs', () => {
  // Chin down, then chin up.
  const flexion = drive(neckFlexion, { side: 'right', targetDeg: 30 }, X, [-40, 40]);
  assert.deepEqual(flexion.reps.map((r: any) => r.valid), [true], 'chin up is not a flexion rep');
  const extension = drive(neckExtension, { side: 'right', targetDeg: 30 }, X, [-40, 40]);
  assert.deepEqual(extension.reps.map((r: any) => r.valid), [true], 'chin down is not an extension rep');
  assert.ok(flexion.reps[0].startMs < extension.reps[0].startMs, 'flexion counted the first movement, extension the second');
});

test('neck lateral flexion follows the prescribed side', () => {
  // Ear to the left shoulder, then to the right.
  const right = drive(neckLateralFlexion, { side: 'right', targetDeg: 20 }, Y, [-30, 30]);
  const left = drive(neckLateralFlexion, { side: 'left', targetDeg: 20 }, Y, [-30, 30]);
  assert.equal(right.valid, 1); assert.equal(left.valid, 1);
  assert.ok(left.reps[0].startMs < right.reps[0].startMs, 'each side counts only its own direction');
});

test('turning the head is yaw and moves nothing', () => {
  const s = new RepSession(neckFlexion, { side: 'right', targetDeg: 30 }); let t = 0;
  for (let i = 0; i < 30; i++) s.pushFused({ tMs: (t += 40), imu: { quaternion: [0, 0, 0, 1], rotationRate: [0, 0, 0], hostMonotonicMs: t } });
  for (let i = 0; i <= 40; i++) s.pushFused({ tMs: (t += 40), imu: { quaternion: axisAngle([0, 0, 1], 80 * i / 40), rotationRate: [0, 0, 1.5], hostMonotonicMs: t } });
  assert.ok(Math.max(...s.samples.map(x => Math.abs(x.angleDeg ?? 0))) < 1e-6);
  // And a nod made while turned away still reads as a nod.
  assert.equal(drive(neckFlexion, { side: 'right', targetDeg: 30 }, X, [-40], { yaw: 60 }).valid, 1);
});

test('the unsigned tilt counts either direction, which is why its notes say plane-blind', () => {
  assert.equal(drive(trunkLateralFlexion, { side: 'left', targetDeg: 15 }, Y, [-25, 25]).valid, 2);
});

test('raise-and-hold only counts a rep once the hold completes', () => {
  const summary = drive(armHold, { side: 'right', targetDeg: 45, holdMs: 3000 }, X, [60, 60], { holdS: 1 });
  assert.equal(summary.valid, 0); assert.deepEqual(summary.invalidReasons, { did_not_reach_target: 2 });
  assert.equal(drive(armHold, { side: 'right', targetDeg: 45, holdMs: 3000 }, X, [60], { holdS: 3.4 }).valid, 1);
});

test('the arm raise is the one-AirPod movement: one pair on the wrist calibrates it and counts it, nothing else is asked for', () => {
  assert.deepEqual(armRaise.requires, ['imu']);
  assert.equal(LIBRARY['arm-raise.v1'].reference, undefined, 'no second AirPod in the catalog either');
  assert.equal(LIBRARY['arm-elevation.v1'].supersededBy, 'arm-raise.v1', 'the gallery has one arm raise, and it is this one');
  const s = drive(armRaise, { side: 'right', targetDeg: 60 }, X, [75, 45]);
  assert.equal(s.calibrated, true);
  assert.deepEqual(s.reps.map((r: any) => r.valid), [true, false]);
  assert.ok(Math.abs(s.reps[0].peakDeg - 75) < .5, `peak ${s.reps[0].peakDeg}`);
  assert.equal(s.invalidReasons.did_not_reach_target, 1);
});
