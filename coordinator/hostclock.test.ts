import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, readdir, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { WebSocket } from 'ws';
import { hostMonotonicMs, interleave, parseChannel, stamped, HOST_EPOCH_UTC } from './hostclock.ts';
import { frame } from './synthetic-pose.ts';

test('the host axis advances monotonically and sits on the wall clock', () => {
  const a = hostMonotonicMs(), b = hostMonotonicMs();
  assert.ok(b >= a, 'never goes backwards');
  assert.ok(Math.abs(a - Date.now()) < 1000, `anchored to wall clock, was ${a - Date.now()}ms off`);
  assert.ok(!Number.isNaN(Date.parse(HOST_EPOCH_UTC)));
  assert.equal(typeof stamped({x: 1}).hostMonotonicMs, 'number');
});

test('channels recorded separately interleave onto one axis', () => {
  const pose = [{hostMonotonicMs: 10, c: 'pose'}, {hostMonotonicMs: 30, c: 'pose'}];
  const imu = [{hostMonotonicMs: 5, c: 'imu'}, {hostMonotonicMs: 20, c: 'imu'}, {hostMonotonicMs: 40, c: 'imu'}];
  assert.deepEqual(interleave(pose, imu).map(s => s.c), ['imu', 'pose', 'imu', 'pose', 'imu']);
});

test('unstamped and malformed lines are dropped, never guessed at', () => {
  const parsed = parseChannel<{hostMonotonicMs: number}>('{"hostMonotonicMs":1}\n\nnot json\n{"noStamp":true}\n');
  assert.equal(parsed.length, 2, 'blank and torn lines skipped, valid JSON kept');
  assert.deepEqual(interleave(parsed).map(s => s.hostMonotonicMs), [1], 'the unstamped sample is dropped');
});

test('live: both relays stamp their recordings on the same axis', {timeout: 20000}, async () => {
  const directory = await mkdtemp(join(tmpdir(), 'kinesthetic-clock-'));
  const posePort = 18777, golfPort = 18778;
  const bridge = spawn(process.execPath, ['server.ts'], {cwd: import.meta.dirname,
    env: {...process.env, KINESTHETIC_PORT: String(posePort), KINESTHETIC_RECORDINGS_DIRECTORY: directory,
          KINESTHETIC_PLANS_DIRECTORY: join(directory, 'plans')}, stdio: ['ignore', 'pipe', 'pipe']});
  const relay = spawn(process.execPath, ['golf-relay.ts'], {cwd: import.meta.dirname,
    env: {...process.env, KINESTHETIC_GOLF_PORT: String(golfPort), KINESTHETIC_GOLF_RECORDINGS: directory},
    stdio: ['ignore', 'pipe', 'pipe']});
  const sockets: WebSocket[] = [];
  try {
    await Promise.all([once(bridge.stdout, 'data'), once(relay.stdout, 'data')]);
    const open = async (url: string) => { const ws = new WebSocket(url); sockets.push(ws); await once(ws, 'open'); return ws; };

    const sessionId = randomUUID();
    const poseProducer = await open(`ws://127.0.0.1:${posePort}/pose?role=producer`);
    const f = frame(0, 5);
    poseProducer.send(JSON.stringify({schemaVersion: 'kinesthetic.session.v1', type: 'pose.frame', sessionId,
      sourceId: 'test', sequence: 0, payload: {frameID: 1, source: 'recorded-video', subjectDetected: true,
      observedAtMonotonicMs: 1, ...f}}));

    const imuProducer = await open(`ws://127.0.0.1:${golfPort}/golf?role=producer&player=patient`);
    imuProducer.send(JSON.stringify({type: 'club.motion', playerId: 'patient', sourceId: 'Right', sessionId,
      sequence: 0, sensorTime: 1, quaternion: [0, 0, 0, 1], rotationRate: [0, 0, 1]}));

    // Poll rather than sleep: two spawned servers writing under a loaded parallel test run do not
    // finish on any fixed schedule, and a fixed wait made this flake.
    const readRows = async () => {
      const files = await readdir(directory);
      const poseFile = files.find(f => f === `${sessionId}.jsonl`);
      const imuFile = files.find(f => /^patient-\d+\.jsonl$/.test(f));
      if (!poseFile || !imuFile) return null;
      const poseRows = parseChannel<any>(await readFile(join(directory, poseFile), 'utf8'));
      const imuRows = parseChannel<any>(await readFile(join(directory, imuFile), 'utf8'));
      return poseRows.length && imuRows.length ? {poseRows, imuRows} : null;
    };
    let rows = null as Awaited<ReturnType<typeof readRows>>;
    for (let attempt = 0; attempt < 100 && !rows; attempt++) {
      rows = await readRows();
      if (!rows) await new Promise(r => setTimeout(r, 100));
    }
    assert.ok(rows, `both recordings should appear; directory held ${(await readdir(directory)).join(', ')}`);
    const {poseRows, imuRows} = rows!;
    for (const r of [...poseRows, ...imuRows]) assert.ok(Number.isFinite(r.hostMonotonicMs), 'every sample is stamped');

    // The point of the shared axis: samples from two processes are orderable against each other.
    const merged = interleave<any>(poseRows, imuRows);
    assert.equal(merged.length, poseRows.length + imuRows.length);
    const spread = merged.at(-1)!.hostMonotonicMs - merged[0].hostMonotonicMs;
    assert.ok(spread >= 0 && spread < 20_000, `both processes agree to within the test window, spread ${spread}ms`);
  } finally {
    for (const ws of sockets) ws.close();
    bridge.kill(); relay.kill();
    await Promise.all([once(bridge, 'exit'), once(relay, 'exit')]);
    await rm(directory, {recursive: true, force: true});
  }
});
