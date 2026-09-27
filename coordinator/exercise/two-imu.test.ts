// Two-AirPod movements: the second AirPod certifies the posture (arm raise, curl) or supplies the other half of the
// joint (squat), and nothing depends on the two pairs agreeing on a heading.
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawn } from 'node:child_process';
import { WebSocketServer, type WebSocket } from 'ws';
import { armRaise, bicepsCurl, squat } from './two-imu.ts';
import { RepSession, type ExerciseKind, type ImuSample, type RepParams } from './kind.ts';
import { ready, stop } from '../test-process.ts';

type Q = [number, number, number, number];
const axisAngle = (axis: [number, number, number], deg: number): Q => {
  const h = deg * Math.PI / 360, n = Math.hypot(...axis), s = Math.sin(h);
  return [axis[0] / n * s, axis[1] / n * s, axis[2] / n * s, Math.cos(h)];
};
const mul = ([ax, ay, az, aw]: Q, [bx, by, bz, bw]: Q): Q => [
  aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
  aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz];
// Each pair keeps its own arbitrary heading (xArbitraryZVertical), and they never agree.
const LIMB_HEADING = axisAngle([0, 0, 1], 37), REF_HEADING = axisAngle([0, 0, 1], -118);
const at = (heading: Q, deg: number, t: number, moving: boolean): ImuSample =>
  ({ quaternion: mul(heading, axisAngle([1, 0, 0], deg)), rotationRate: moving ? [1.2, 0, 0] : [0, 0, 0], hostMonotonicMs: t });

/** One rep per entry: the limb rises to `limb` while the neighbouring segment moves to `ref`, holds, and both return. */
function run(kind: ExerciseKind<any>, params: RepParams, reps: { limb: number; ref: number; refSilent?: boolean }[]) {
  const s = new RepSession(kind, params); let t = 0;
  const step = (limb: number, ref: number, moving: boolean, silent = false) => {
    t += 40; s.pushFused({ tMs: t, imu: at(LIMB_HEADING, limb, t, moving), ref: silent ? null : at(REF_HEADING, ref, t, moving) });
  };
  for (let i = 0; i < 30; i++) step(0, 0, false);
  for (const r of reps) {
    for (let i = 0; i <= 20; i++) step(r.limb * i / 20, r.ref * i / 20, true);
    for (let i = 0; i < 20; i++) step(r.limb, r.ref, false, r.refSilent && i > 3 && i < 16);
    for (let i = 20; i >= 0; i--) step(r.limb * i / 20, r.ref * i / 20, true);
    for (let i = 0; i < 15; i++) step(0, 0, false);
  }
  return s.summary() as Record<string, any>;
}

test('arm raise: a rep made by leaning the trunk does not count, and the lean is reported', () => {
  const s = run(armRaise, { side: 'right', targetDeg: 60 }, [{ limb: 75, ref: 3 }, { limb: 75, ref: 16 }]);
  assert.deepEqual(s.reps.map((r: any) => r.reason), [null, 'trunk_lean']);
  assert.ok(Math.abs(s.trunkLean.maxDuringRepsDeg - 16) < .5, `lean ${s.trunkLean.maxDuringRepsDeg}`);
  assert.ok(Math.abs(s.medianValidPeakDeg - 75) < .5);
});

test('biceps curl: swinging the upper arm forward is caught; a steady elbow counts', () => {
  const s = run(bicepsCurl, { side: 'right', targetDeg: 90 }, [{ limb: 100, ref: 6 }, { limb: 100, ref: 28 }]);
  assert.deepEqual(s.reps.map((r: any) => r.reason), [null, 'upper_arm_swing']);
  assert.equal(s.upperArmSwing.maxDuringRepsDeg > 27, true);
});

test('squat: the knee angle is the shin and thigh tilts together, which neither AirPod sees alone', () => {
  // Shin 25 forward-of-vertical plus thigh 35 the other way: a 60 degree knee. Neither alone reaches the 45 target.
  const s = run(squat, { side: 'right', targetDeg: 45 }, [{ limb: 25, ref: 35 }, { limb: 15, ref: 20 }]);
  assert.deepEqual(s.reps.map((r: any) => r.valid), [true, false]);
  assert.ok(Math.abs(s.reps[0].peakDeg - 60) < .5, `knee ${s.reps[0].peakDeg}`);
  assert.equal(s.invalidReasons.did_not_reach_target, 1);
});

test('the two pairs never share a heading, and it does not matter', () => {
  // Same movement, each pair turned about the vertical by a different amount: identical result.
  const a = run(armRaise, { side: 'right', targetDeg: 60 }, [{ limb: 75, ref: 16 }]);
  assert.equal(a.reps[0].reason, 'trunk_lean');
  assert.ok(Math.abs(a.reps[0].peakDeg - 75) < .01);
});

test('a second AirPod that goes quiet mid-rep is tracking loss, never a silent pass', () => {
  const s = run(armRaise, { side: 'right', targetDeg: 60 }, [{ limb: 75, ref: 0, refSilent: true }, { limb: 75, ref: 0 }]);
  assert.deepEqual(s.reps.map((r: any) => r.reason), ['tracking_lost', null]);
  const lonely = new RepSession(armRaise, { side: 'right', targetDeg: 60 });
  for (let t = 40; t < 2000; t += 40) lonely.pushFused({ tMs: t, imu: at(LIMB_HEADING, 0, t, false), ref: null });
  assert.equal(lonely.calibrated, false, 'one AirPod cannot calibrate a two-AirPod movement');
});

test('server: a two-AirPod movement reads both relay streams and learns which is the limb by which one moves', { timeout: 30000 }, async () => {
  const relay = new WebSocketServer({ port: 18830, host: '127.0.0.1' });
  const viewers = new Set<WebSocket>(); relay.on('connection', ws => viewers.add(ws));
  const dir = mkdtempSync(join(tmpdir(), 'two-imu-')), port = 18831, base = `http://127.0.0.1:${port}`;
  const child = spawn(process.execPath, ['server.ts'], { cwd: join(import.meta.dirname, '..'), stdio: ['ignore', 'pipe', 'pipe'],
    env: { ...process.env, KINESTHETIC_PORT: String(port), KINESTHETIC_RECORDINGS_DIRECTORY: join(dir, 'rec'), KINESTHETIC_PLANS_DIRECTORY: join(dir, 'plans'),
      KINESTHETIC_PROPOSALS_DIRECTORY: join(dir, 'prop'), KINESTHETIC_SOCIAL_DIRECTORY: join(dir, 'social'),
      KINESTHETIC_MOTION_URL: 'ws://127.0.0.1:18830/golf?role=viewer', KINESTHETIC_WRIST_MOTION_URL: 'ws://127.0.0.1:18830/bowling-motion?role=viewer' } });
  const post = (path: string, body?: unknown) => fetch(base + path, { method: 'POST', body: body ? JSON.stringify(body) : undefined });
  const sensors = async () => (await fetch(base + '/api/sensors')).json();
  try {
    await ready(child);
    const started = await (await post('/exercise/start', { activityId: 'movement.arm-raise' })).json();
    assert.equal(started.exerciseKind, 'arm-raise.v1');
    // Nothing is assumed from the channels: until both pairs are live and one has been moved, nothing is measured.
    assert.deepEqual([started.sensors.mode, started.sensors.phase, started.sensors.imu], ['two', 'waiting', null]);
    assert.match(started.sensors.instruction, /one AirPod on your wrist and one on your chest/);
    // The plan prescribes a one-AirPod shoulder raise; the gallery's arm raise measures that same prescription.
    assert.equal(started.prescriptionId, 'arm-elevation-right'); assert.equal(started.practice, false);
    while (viewers.size < 2) await new Promise(r => setTimeout(r, 20));
    let t = 5e6, seq = 0;
    // Paced, as the AirPods are (~25 Hz): two sockets drain in whatever order they like, so a burst would hand the
    // server one pair's whole stream before the other's.
    const send = async (limb: number, ref: number, moving: boolean, refMoving = moving) => {
      await new Promise(r => setTimeout(r, 3));
      t += 40; seq++;
      const packet = (type: string, sample: ImuSample) => JSON.stringify({ type, playerId: 'patient', sourceId: 'Left', sessionId: 's', sequence: seq,
        quaternion: sample.quaternion, rotationRate: sample.rotationRate, hostMonotonicMs: t });
      // The neighbouring segment's sample lands first, so it rides with the limb's step.
      for (const ws of viewers) ws.send(packet('club.motion', at(REF_HEADING, ref, t, refMoving)));
      for (const ws of viewers) ws.send(packet('bowling.motion', at(LIMB_HEADING, limb, t, moving)));
    };
    // Both live and still: the studio is asked to move the limb's AirPod.
    for (let i = 0; i < 10; i++) await send(0, 0, false);
    let s = await sensors();
    assert.equal(s.phase, 'identify'); assert.match(s.instruction, /Move the AirPod on your wrist/);
    // The wrist pair is wiggled while the chest pair rests: it is the limb. (Had the chest pair been the one wiggled,
    // the roles would be the other way round — the channels' names never decide.)
    for (let i = 0; i < 20; i++) await send(3 * Math.sin(i), 0, true, false);
    s = await sensors();
    assert.deepEqual([s.phase, s.imu, s.ref, s.locked], ['ready', 'wrist', 'club', false]);
    for (let i = 0; i < 30; i++) await send(0, 0, false);
    assert.equal((await sensors()).locked, true, 'calibrated against these streams: fixed for the set');
    for (const [limb, ref] of [[70, 2], [70, 18]]) {
      for (let i = 0; i <= 20; i++) await send(limb * i / 20, ref * i / 20, true);
      for (let i = 0; i < 20; i++) await send(limb, ref, false);
      for (let i = 20; i >= 0; i--) await send(limb * i / 20, ref * i / 20, true);
      for (let i = 0; i < 15; i++) await send(0, 0, false);
    }
    await new Promise(r => setTimeout(r, 300));
    const summary = await (await post('/exercise/stop')).json();
    assert.equal(summary.attempted, 2, JSON.stringify({frames: summary.frames, ratio: summary.validFrameRatio, calibrated: summary.calibrated, lost: summary.trackingLossEvents}));
    assert.deepEqual(summary.reps.map((r: any) => r.reason), [null, 'trunk_lean']);
    assert.equal(summary.exerciseKind, 'arm-raise.v1');
    assert.deepEqual(summary.sensors, { imu: 'wrist', ref: 'club' });

    // With someone else in the session, every pair but one is theirs: a two-AirPod movement is refused, a
    // one-AirPod movement goes ahead.
    assert.equal((await post('/api/groups', { activityId: 'movement.arm-raise' })).status, 201);
    const refused = await post('/exercise/start', { activityId: 'movement.arm-raise' });
    assert.equal(refused.status, 409); assert.match((await refused.json()).error, /one-AirPod movement/);
    const one = await (await post('/exercise/start', { prescriptionId: 'arm-elevation-right' })).json();
    assert.equal(one.sensors.mode, 'one');
    await post('/exercise/stop'); await post('/api/groups/leave');
  } finally { await stop(child); relay.close(); }
});
