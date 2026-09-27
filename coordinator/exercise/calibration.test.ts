// Calibration is strict: the starting position held still, for the whole hold, before a reference is fixed. A
// settling or creeping limb, or one moving faster than a held arm, never becomes "rest". The live studio holds for
// 4 s (server.ts CALIBRATION_MS); these run the engine with that hold directly.
import test from 'node:test';
import assert from 'node:assert/strict';
import { RepSession, CALIBRATION_DRIFT_DEG, type ImuSample } from './kind.ts';
import { armElevation } from './arm-elevation.ts';

type Q = [number, number, number, number];
const tilt = (deg: number): Q => { const h = deg * Math.PI / 360; return [Math.sin(h), 0, 0, Math.cos(h)]; };

/** 25 Hz samples: `deg` of tilt, turning at `rate` rad/s. */
function feed(s: RepSession<any>, from: number, ms: number, deg: (i: number) => number, rate = 0.01) {
  let t = from;
  for (let i = 0; i < ms / 40; i++) {
    const imu: ImuSample = { quaternion: tilt(deg(i)), rotationRate: [rate, 0, 0], hostMonotonicMs: (t += 40) };
    s.pushFused({ tMs: t, imu });
  }
  return t;
}
const session = () => new RepSession(armElevation, { side: 'right', targetDeg: 45, targetMaxDeg: 60, prescribedReps: 3, holdMs: 400, calibrationMs: 4000 } as any);
const calibrated = (s: RepSession<any>) => s.events.some(e => e.type === 'calibration.complete');

test('the hold is the whole hold: three seconds still is not enough, four is', () => {
  const s = session();
  const t = feed(s, 0, 3000, () => 0);
  assert.equal(calibrated(s), false);
  feed(s, t, 1200, () => 0);
  assert.equal(calibrated(s), true);
});

test('a limb still settling is not rest: creeping past the drift limit starts the hold again', () => {
  const s = session();
  // Creeps 12° over four seconds, slowly enough to count as still frame by frame.
  const creep = feed(s, 0, 4000, i => 12 * i / 100, 0.05);
  assert.equal(calibrated(s), false, `crept ${12}°, more than ${CALIBRATION_DRIFT_DEG}° allowed`);
  // Then held where it ended up: calibrates four seconds later.
  feed(s, creep, 4200, () => 12);
  assert.equal(calibrated(s), true);
});

test('an arm turning faster than a held one never calibrates, however long it goes on', () => {
  const s = session();
  feed(s, 0, 6000, () => 0, 0.25);   // about 14°/s: once still enough, now not
  assert.equal(calibrated(s), false);
});
