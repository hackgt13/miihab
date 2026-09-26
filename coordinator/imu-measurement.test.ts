import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { WebSocketServer } from 'ws';
import { ImuRepSession, verticalInDevice, type MotionSample } from './imu-measurement.ts';

type Q = [number, number, number, number];
const axisAngle = (axis: [number, number, number], deg: number): Q => {
  const h = deg * Math.PI / 360, n = Math.hypot(...axis), s = Math.sin(h);
  return [axis[0] / n * s, axis[1] / n * s, axis[2] / n * s, Math.cos(h)];
};
const mul = ([ax, ay, az, aw]: Q, [bx, by, bz, bw]: Q): Q => [
  aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
  aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz];
// The handle rests tilted and twisted in the hand; the limb then rotates it about a horizontal world axis.
const REST = mul(axisAngle([0, 0, 1], 70), axisAngle([1, 0.3, 0], 25));
const HORIZONTAL: [number, number, number] = [0.6, 0.8, 0];

/** A stream at 25 Hz: still rest, then each rep rises to its peak, holds, returns, rests. */
function stream(peaks: number[], opts: { holdS?: number; restS?: number; gapAt?: number } = {}): MotionSample[] {
  const out: MotionSample[] = []; let t = 0;
  const at = (deg: number, moving: boolean, yaw = 0) => {
    const q = mul(axisAngle([0, 0, 1], yaw), mul(axisAngle(HORIZONTAL, deg), REST));
    out.push({ sensorTime: (t += .04), quaternion: q, rotationRate: moving ? [0, 1.5, 0] : [0.01, 0, 0] });
  };
  for (let i = 0; i < 30; i++) at(0, false);
  peaks.forEach((peak, n) => {
    for (let i = 0; i <= 20; i++) at(peak * i / 20, true);
    if (opts.gapAt === n) t += 1;                                  // stream silent for a second at the top
    for (let i = 0; i < (opts.holdS ?? .6) / .04; i++) at(peak, false);
    for (let i = 20; i >= 0; i--) at(peak * i / 20, true);
    for (let i = 0; i < (opts.restS ?? .6) / .04; i++) at(0, false);
  });
  return out;
}
const run = (samples: MotionSample[], config: Partial<ConstructorParameters<typeof ImuRepSession>[0]> = {}) => {
  const s = new ImuRepSession({ exercise: 'seated_shoulder_raise', side: 'right', targetDeg: 45, targetMaxDeg: 60, prescribedReps: 3, ...config });
  for (const m of samples) s.push(m);
  return s;
};

test('the angle is the tilt from rest about a horizontal axis, whatever the handle\'s resting twist', () => {
  const s = run(stream([55]));
  const peak = Math.max(...s.samples.map(x => x.angleDeg ?? 0));
  assert.ok(Math.abs(peak - 55) < .01, `peak ${peak}`);
  assert.deepEqual(verticalInDevice([0, 0, 0, 1]), [0, 0, 1]);
});

test('reps in the band count; overshoots are flagged; short reps do not count', () => {
  const summary = run(stream([55, 72, 38])).summary();
  assert.equal(summary.sensor, 'imu'); assert.equal(summary.calibrated, true);
  assert.equal(summary.attempted, 3); assert.equal(summary.valid, 2); assert.equal(summary.overshoots, 1);
  assert.deepEqual(summary.invalidReasons, { did_not_reach_target: 1 });
  assert.deepEqual(summary.reps.map(r => r.aboveTargetMax), [false, true, false]);
  assert.equal(Math.round(summary.medianValidPeakDeg!), 64);
});

test('turning the handle about the vertical is not a rep', () => {
  const samples: MotionSample[] = []; let t = 0;
  for (let i = 0; i < 30; i++) samples.push({ sensorTime: (t += .04), quaternion: REST, rotationRate: [0, 0, 0] });
  for (let i = 0; i <= 40; i++) samples.push({ sensorTime: (t += .04), quaternion: mul(axisAngle([0, 0, 1], 90 * i / 40), REST), rotationRate: [0, 0, 1.5] });
  const s = run(samples);
  assert.equal(s.summary().attempted, 0);
  assert.ok(Math.max(...s.samples.map(x => x.angleDeg ?? 0)) < 1e-6);
});

test('calibration waits for the handle to be still', () => {
  const moving: MotionSample[] = Array.from({ length: 40 }, (_, i) => ({ sensorTime: .04 * (i + 1), quaternion: REST, rotationRate: [0, 2, 0] }));
  const s = run(moving);
  assert.equal(s.phase, 'calibrating'); assert.equal(s.summary().calibrated, false);
});

test('a silent stream mid-rep invalidates that rep as tracking lost', () => {
  const summary = run(stream([55, 55], { gapAt: 0 })).summary();
  assert.equal(summary.trackingLossEvents, 1);
  assert.deepEqual(summary.reps.map(r => r.reason), ['tracking_lost', null]);
});

test('curls use the same tilt with their own rest band and target', () => {
  const summary = run(stream([100, 95]), { exercise: 'biceps_curl', targetDeg: 90, targetMaxDeg: 110, restMaxDeg: 20, prescribedReps: 2 }).summary();
  assert.equal(summary.exercise, 'biceps_curl'); assert.equal(summary.valid, 2); assert.equal(summary.overshoots, 0);
});

test('bad samples are rejected and duplicates ignored', () => {
  const s = run([...stream([]), { sensorTime: 99, quaternion: [0, 0, 0, 0], rotationRate: [0, 0, 0] },
    { sensorTime: .04, quaternion: REST, rotationRate: [0, 0, 0] }]);
  assert.equal(s.summary().rejectedSamples, 1); assert.equal(s.samples.length, 30);
});

test('server: an IMU exercise reads the patient\'s handle AirPod from the motion relay and progresses on it', {timeout:30000}, async () => {
  const relay = new WebSocketServer({ port: 18777, host: '127.0.0.1' });
  const viewers = new Set<import('ws').WebSocket>(); relay.on('connection', ws => viewers.add(ws));
  const dir = mkdtempSync(join(tmpdir(), 'imuapi-')), port = 18776, base = `http://127.0.0.1:${port}`;
  const child = spawn(process.execPath, ['server.ts'], {cwd:import.meta.dirname, stdio:['ignore','pipe','pipe'],
    env:{...process.env, KINESTHETIC_PORT:String(port), KINESTHETIC_RECORDINGS_DIRECTORY:join(dir, 'rec'), KINESTHETIC_PLANS_DIRECTORY:join(dir, 'plans'),
      KINESTHETIC_PROPOSALS_DIRECTORY:join(dir, 'prop'), KINESTHETIC_MOTION_URL:'ws://127.0.0.1:18777/golf?role=viewer'}});
  const post = (path: string, body?: unknown) => fetch(base + path, {method:'POST', body: body ? JSON.stringify(body) : undefined});
  let session = 0;
  const run = async (peaks: number[]) => {
    const started = await (await post('/exercise/start', {prescribedReps: peaks.length})).json();
    assert.equal(started.sensor, 'imu'); assert.equal(started.planExerciseId, 'shoulder-raise-right');
    while (!viewers.size) await new Promise(r => setTimeout(r, 20));
    const sessionId = `airpod-session-${session++}`;
    stream(peaks).forEach((m, i) => {
      const packet = (playerId: string) => JSON.stringify({type:'club.motion', playerId, sourceId:'Left', sessionId, sequence:i, ...m});
      for (const ws of viewers) { ws.send(packet('patient')); ws.send(packet('friend')); }   // the friend's AirPod is ignored
    });
    await new Promise(r => setTimeout(r, 200));
    return (await post('/exercise/stop')).json();
  };
  try {
    await once(child.stdout, 'data');
    const first = await run([52, 70]);
    assert.equal(first.sensor, 'imu'); assert.equal(first.attempted, 2, 'one AirPod counted, not two');
    assert.equal(first.valid, 2); assert.equal(first.overshoots, 1); assert.equal(first.simulated, false);
    assert.equal(first.progression.decision, 'hold');
    await run([52, 53]); const up = await run([54, 52]);
    assert.equal(up.progression.decision, 'progress'); assert.equal(up.progression.status, 'applied');
    const plan = await (await fetch(base + '/api/plans/active')).json();
    assert.equal(plan.exercises[0].targetDeg, 50); assert.equal(plan.exercises[0].sensor, 'imu');
    const curl = await (await post('/exercise/start', {exerciseId:'curl-right'})).json();
    assert.equal(curl.sensor, 'imu'); assert.equal(curl.exercise, 'biceps_curl'); assert.equal(curl.config.restMaxDeg, 20);
    await post('/exercise/stop');
  } finally { child.kill(); await once(child, 'exit'); relay.close(); }
});
