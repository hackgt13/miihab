// The two-IMU kernel. What is worth asserting: that two streams only become one reading when they are
// about the same instant, that the base sensor's movement is taken out of the angle rather than added
// to it, and that a half-pair reports nothing rather than measuring against a sensor that stopped.
import test from 'node:test';
import assert from 'node:assert/strict';
import { PairSync, imuPair, PAIR_WINDOW_MS } from './imu-pair.ts';
import { shoulderRaisePaired, neckFlexionPaired } from './imu-pair-library.ts';
import { RepSession, type ImuSample } from './kind.ts';
import { bindQualities } from './quality.ts';

/** A rotation about the device x-axis by `deg`: reads back as exactly `deg` of tilt from upright. */
const tilt = (deg: number): [number, number, number, number] => {
  const h = deg * Math.PI / 360; return [Math.sin(h), 0, 0, Math.cos(h)];
};
const sample = (deg: number, tMs: number, moving = false): ImuSample =>
  ({ quaternion: tilt(deg), rotationRate: moving ? [1.2, 0, 0] : [0.01, 0, 0], hostMonotonicMs: tMs });

// ── Synchronising two streams ───────────────────────────────────────────────────────────────────

test('a pair needs both sensors, and neither on its own is a reading', () => {
  const sync = new PairSync();
  assert.equal(sync.pair(), null, 'nothing at all');
  sync.push('moving', sample(10, 1000));
  assert.equal(sync.pair(), null, 'one sensor is not a pair');
  sync.push('base', sample(2, 1010));
  assert.ok(sync.pair(), 'both, close in time');
});

test('samples too far apart in time are two moments, not one', () => {
  const sync = new PairSync();
  sync.push('moving', sample(10, 1000));
  sync.push('base', sample(2, 1000 + PAIR_WINDOW_MS + 1));
  assert.equal(sync.pair(), null, 'outside the window');
  sync.push('base', sample(2, 1000 + PAIR_WINDOW_MS));
  assert.equal(sync.pair(), null, 'an older sample never replaces a newer one');
  sync.push('base', sample(2, 1020));
  assert.equal(sync.pair(), null, 'still older than what is held');
});

test('the pair is stamped at the later half, and carries how far apart they were', () => {
  const sync = new PairSync();
  sync.push('moving', sample(10, 1040));
  sync.push('base', sample(2, 1000));
  const p = sync.pair()!;
  assert.equal(p.tMs, 1040);
  assert.equal(p.skewMs, 40);
});

test('a sensor that goes quiet can be dropped, so nothing stale pairs with a live sample', () => {
  const sync = new PairSync();
  sync.push('moving', sample(10, 1000));
  sync.push('base', sample(2, 1000));
  sync.drop('base');
  assert.equal(sync.pair(), null);
});

// ── Reading two segments ────────────────────────────────────────────────────────────────────────

/** Read one held position through a kind, against an upright calibration for both sensors. */
const upright = { axes: [[0, 0, 1], [0, 0, 1]], scaleM: 1 } as any;
const params = { side: 'right', targetDeg: 40, targetMaxDeg: 90, prescribedReps: 8, holdMs: 0 } as any;
const read = (kind: ReturnType<typeof imuPair>, movingDeg: number, baseDeg: number) =>
  kind.observe({ tMs: 1, imu: sample(movingDeg, 1), imuBase: sample(baseDeg, 1) } as any, params, upright)!;

test('the base segment is taken out of the angle, not added to it', () => {
  // The arm reaches 60° from vertical either way. Upright, all 60° is the shoulder; with 20° of lean,
  // only 40° of it is — and a single wrist sensor cannot tell those apart.
  const honest = read(shoulderRaisePaired, 60, 0).primaryDeg;
  const leaned = read(shoulderRaisePaired, 60, 20).primaryDeg;
  assert.ok(Math.abs(honest - 60) < 1, `upright reads ${honest.toFixed(1)}°`);
  assert.ok(Math.abs(leaned - 40) < 1, `leaning 20° into it reads ${leaned.toFixed(1)}°, not 60°`);
  assert.ok(leaned < honest, 'the lean does not add reach');
});

test('the lean itself is reported, which is what one sensor could never see', () => {
  assert.ok(read(shoulderRaisePaired, 60, 0).compensationDeg! < 1, 'an upright trunk compensates nothing');
  const leaned = read(shoulderRaisePaired, 60, 20).compensationDeg!;
  assert.ok(Math.abs(leaned - 20) < 1, `a 20° lean reports ${leaned.toFixed(1)}° of compensation`);
});

test('a half-pair is no reading at all', () => {
  const kind = shoulderRaisePaired;
  const p = params, ref = upright;
  assert.equal(kind.observe({ tMs: 1, imu: sample(30, 1), imuBase: null } as any, p, ref), null);
  assert.equal(kind.observe({ tMs: 1, imu: null, imuBase: sample(0, 1) } as any, p, ref), null);
  assert.ok(kind.observe({ tMs: 1, imu: sample(30, 1), imuBase: sample(0, 1) } as any, p, ref));
});

test('calibration refuses a sensor that is moving, either of them', () => {
  const kind = shoulderRaisePaired;
  const p = params;
  assert.equal(kind.observe({ tMs: 1, imu: sample(0, 1, true), imuBase: sample(0, 1) } as any, p, null), null,
    'the moving sensor is not at rest');
  assert.equal(kind.observe({ tMs: 1, imu: sample(0, 1), imuBase: sample(0, 1, true) } as any, p, null), null,
    'nor is the base one');
  const both = kind.observe({ tMs: 1, imu: sample(0, 1), imuBase: sample(0, 1) } as any, p, null);
  assert.equal(both?.axes.length, 2, 'and a rest gives the engine two references to median');
});

test('a profile may measure more than one angle', () => {
  const kind = shoulderRaisePaired;
  const p = params, ref = upright;
  const o = kind.observe({ tMs: 1, imu: sample(60, 1), imuBase: sample(20, 1) } as any, p, ref)!;
  assert.ok(o.metrics, 'the open bag is there when a profile fills it');
  assert.ok(o.metrics!.trunkShareDeg > 15, `the trunk's own share was ${o.metrics!.trunkShareDeg}°`);
});

test('the kernel is not the exercise: the same code reads a neck and a shoulder', () => {
  const p = params, ref = upright;
  const neck = neckFlexionPaired.observe({ tMs: 1, imu: sample(30, 1), imuBase: sample(0, 1) } as any, p, ref)!;
  const shoulder = shoulderRaisePaired.observe({ tMs: 1, imu: sample(30, 1), imuBase: sample(0, 1) } as any, p, ref)!;
  // The neck profile reads signed about an axis; the shoulder reads unsigned between segments. Same
  // input, different profile, no branch in the kernel.
  assert.ok(Math.abs(neck.primaryDeg) > 1 && Math.abs(shoulder.primaryDeg) > 1);
  assert.equal(neckFlexionPaired.calibration(ref).placements!.base, 'AirPod clipped at your sternum');
  assert.equal(shoulderRaisePaired.calibration(ref).placements!.moving, 'AirPod on your wrist');
});
