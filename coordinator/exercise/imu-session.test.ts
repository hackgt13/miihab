// IMU tilt kinds (arm-elevation, elbow-flexion) and the server path that feeds them from the motion relay.
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { WebSocketServer, type WebSocket } from 'ws';
import { ArmElevationSession, ElbowFlexionSession, verticalInDevice } from './arm-elevation.ts';
import type { ImuSample, RepSession } from './kind.ts';

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

/** IMU at 25 Hz on the host axis: still rest, then each rep rises, holds, returns, rests. null = no sample. */
function stream(peaks: number[], opts: { holdS?: number; gapAt?: number; t0?: number } = {}): (ImuSample | null)[] {
  const out: (ImuSample | null)[] = []; let t = opts.t0 ?? 0;
  const at = (deg: number, moving: boolean) =>
    out.push({ quaternion: mul(axisAngle(HORIZONTAL, deg), REST), rotationRate: moving ? [0, 1.5, 0] : [0.01, 0, 0], hostMonotonicMs: (t += 40) });
  for (let i = 0; i < 30; i++) at(0, false);
  peaks.forEach((peak, n) => {
    for (let i = 0; i <= 20; i++) at(peak * i / 20, true);
    if (opts.gapAt === n) for (let i = 0; i < 25; i++) { t += 40; out.push(null); }   // a second with no samples
    for (let i = 0; i < (opts.holdS ?? .6) / .04; i++) at(peak, false);
    for (let i = 20; i >= 0; i--) at(peak * i / 20, true);
    for (let i = 0; i < 15; i++) at(0, false);
  });
  return out;
}
function drive(s: RepSession<any>, samples: (ImuSample | null)[]) {
  let t = 0;
  for (const imu of samples) { t = imu?.hostMonotonicMs ?? t + 40; s.pushFused({ tMs: t, imu }); }
  return s.summary() as Record<string, any>;
}
const raise = () => new ArmElevationSession({ side: 'right', targetDeg: 45, targetMaxDeg: 60, prescribedReps: 3 });

test('elevation is the tilt from rest against gravity, whatever the handle\'s resting twist, with no camera', () => {
  const s = raise(); const summary = drive(s, stream([55]));
  assert.equal(summary.calibrated, true);
  const peak = Math.max(...s.samples.map(x => x.angleDeg ?? 0));
  assert.ok(Math.abs(peak - 55) < .01, `peak ${peak}`);
  assert.equal(summary.trunkDeviation.meanDeg, null, 'no camera, so compensation is unknown rather than zero');
  assert.deepEqual(verticalInDevice([0, 0, 0, 1]), [0, 0, 1]);
});

test('reps in the band count; overshoots are flagged; short reps do not count', () => {
  const summary = drive(raise(), stream([55, 72, 38]));
  assert.equal(summary.attempted, 3); assert.equal(summary.valid, 2); assert.equal(summary.overshoots, 1);
  assert.deepEqual(summary.invalidReasons, { did_not_reach_target: 1 });
  assert.deepEqual(summary.reps.map((r: any) => r.aboveTargetMax), [false, true, false]);
});

test('turning the handle about the vertical is not a rep', () => {
  const s = raise(); let t = 0;
  for (let i = 0; i < 30; i++) s.pushFused({ tMs: (t += 40), imu: { quaternion: REST, rotationRate: [0, 0, 0], hostMonotonicMs: t } });
  for (let i = 0; i <= 40; i++) s.pushFused({ tMs: (t += 40), imu: { quaternion: mul(axisAngle([0, 0, 1], 90 * i / 40), REST), rotationRate: [0, 0, 1.5], hostMonotonicMs: t } });
  assert.equal((s.summary() as any).attempted, 0);
  assert.ok(Math.max(...s.samples.map(x => x.angleDeg ?? 0)) < 1e-6);
});

test('calibration waits for the handle to be still', () => {
  const s = raise();
  for (let i = 1; i <= 40; i++) s.pushFused({ tMs: 40 * i, imu: { quaternion: REST, rotationRate: [0, 2, 0], hostMonotonicMs: 40 * i } });
  assert.equal(s.phase, 'calibrating'); assert.equal(s.calibrated, false);
});

test('a silent stream mid-rep invalidates that rep as tracking lost', () => {
  const summary = drive(raise(), stream([55, 55], { gapAt: 0 }));
  assert.equal(summary.trackingLossEvents, 1);
  assert.deepEqual(summary.reps.map((r: any) => r.reason), ['tracking_lost', null]);
});

test('curls use the same tilt with their own rest band and target', () => {
  const s = new ElbowFlexionSession({ side: 'right', targetDeg: 90, targetMaxDeg: 110, prescribedReps: 2 });
  assert.equal(s.params.restMaxDeg, 20);
  const summary = drive(s, stream([100, 95]));
  assert.equal(summary.exerciseKind, 'elbow-flexion.v1'); assert.equal(summary.valid, 2); assert.equal(summary.overshoots, 0);
});

test('server: an IMU prescription reads the patient\'s handle AirPod from the motion relay and progresses on it', {timeout:30000}, async () => {
  const relay = new WebSocketServer({ port: 18781, host: '127.0.0.1' });
  const viewers = new Set<WebSocket>(); relay.on('connection', ws => viewers.add(ws));
  const dir = mkdtempSync(join(tmpdir(), 'imuapi-')), port = 18782, base = `http://127.0.0.1:${port}`;
  const child = spawn(process.execPath, ['server.ts'], {cwd:join(import.meta.dirname, '..'), stdio:['ignore','pipe','pipe'],
    env:{...process.env, KINESTHETIC_PORT:String(port), KINESTHETIC_RECORDINGS_DIRECTORY:join(dir, 'rec'), KINESTHETIC_PLANS_DIRECTORY:join(dir, 'plans'),
      KINESTHETIC_PROPOSALS_DIRECTORY:join(dir, 'prop'), KINESTHETIC_SOCIAL_DIRECTORY:join(dir, 'social'),
      KINESTHETIC_MOTION_URL:'ws://127.0.0.1:18781/golf?role=viewer'}});
  const post = (path: string, body?: unknown) => fetch(base + path, {method:'POST', body: body ? JSON.stringify(body) : undefined});
  let session = 0;
  const run = async (peaks: number[], opts: { gapAt?: number } = {}, body: Record<string, unknown> = {}) => {
    const started = await (await post('/exercise/start', {prescribedReps: peaks.length, ...body})).json();
    while (!viewers.size) await new Promise(r => setTimeout(r, 20));
    const sessionId = `airpod-session-${session++}`;
    // The relay stamps the shared host clock; streams sent here are timed on it, not on arrival.
    stream(peaks, { ...opts, t0: session * 1e6 }).forEach((imu, i) => {
      if (!imu) return;
      const packet = (playerId: string) => JSON.stringify({type:'club.motion', playerId, sourceId:'Left', sessionId, sequence:i,
        sensorTime: imu.hostMonotonicMs / 1000, quaternion: imu.quaternion, rotationRate: imu.rotationRate, hostMonotonicMs: imu.hostMonotonicMs});
      for (const ws of viewers) { ws.send(packet('patient')); ws.send(packet('friend')); }   // the friend's AirPod is ignored
    });
    await new Promise(r => setTimeout(r, 200));
    return { started, stopped: await (await post('/exercise/stop')).json() };
  };
  try {
    await once(child.stdout, 'data');
    const first = await run([52, 70]);
    assert.equal(first.started.sensor, 'imu'); assert.equal(first.started.prescriptionId, 'arm-elevation-right');
    assert.equal(first.stopped.sensor, 'imu'); assert.equal(first.stopped.attempted, 2, 'one AirPod counted, not two');
    assert.equal(first.stopped.valid, 2); assert.equal(first.stopped.overshoots, 1); assert.equal(first.stopped.simulated, false);
    assert.equal(first.stopped.progression.decision, 'hold');
    const gap = await run([52, 52], { gapAt: 0 });
    assert.deepEqual(gap.stopped.reps.map((r: any) => r.reason), ['tracking_lost', null], 'a silent relay stream is tracking loss');
    await run([52, 53]); const up = (await run([54, 52])).stopped;
    assert.equal(up.progression.decision, 'progress'); assert.equal(up.progression.status, 'applied');
    const plan = await (await fetch(base + '/api/plans/active')).json();
    assert.equal(plan.activities[0].params.targetDeg, 50); assert.equal(plan.activities[0].params.targetMaxDeg, 65);
    assert.equal(plan.origin, 'auto-progression');
    const camera = await post('/exercise/start', {exercise:'shoulder-raise.v1'});
    assert.equal(camera.status, 400, 'camera (MediaPipe) measurement is off by default');
    assert.match((await camera.json()).error, /AirPod/);
    const curl = await (await post('/exercise/start', {prescriptionId:'elbow-flexion-right'})).json();
    assert.equal(curl.sensor, 'imu'); assert.equal(curl.exerciseKind, 'elbow-flexion.v1'); assert.equal(curl.config.restMaxDeg, 20);
    await post('/exercise/stop');
    const sessions = await (await fetch(base + '/api/activity-sessions')).json();
    assert.ok(sessions.some((s: any) => s.exerciseKinds.includes('elbow-flexion.v1')), 'every session also lands in the activity record');
  } finally { child.kill(); await once(child, 'exit'); relay.close(); }
});
