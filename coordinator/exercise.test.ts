import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, readdir, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { WebSocket } from 'ws';
import { frame } from './synthetic-pose.ts';
import { ready, stop } from './test-process.ts';

test('live exercise: pose stream → rep events on /exercise, pose viewers untouched, summary saved', {timeout:40000}, async () => {
  const directory = await mkdtemp(join(tmpdir(), 'kinesthetic-exercise-'));
  const port = 18769;
  const child = spawn(process.execPath, ['server.ts'], {cwd:import.meta.dirname,
    env:{...process.env, KINESTHETIC_PORT:String(port), KINESTHETIC_CAMERA_MEASUREMENT:'1', KINESTHETIC_RECORDINGS_DIRECTORY:directory, KINESTHETIC_PLANS_DIRECTORY:join(directory,"plans"), KINESTHETIC_PROPOSALS_DIRECTORY:join(directory,"proposals")}, stdio:['ignore','pipe','pipe']});
  const sockets: WebSocket[] = [];
  const open = async (path: string) => { const ws = new WebSocket(`ws://127.0.0.1:${port}${path}`); sockets.push(ws); await once(ws, 'open'); return ws; };
  try {
    await ready(child);
    const poseViewer = await open('/pose?role=viewer');
    const poseTypes = new Set<string>(); poseViewer.on('message', m => poseTypes.add(JSON.parse(m.toString()).type));
    const exerciseViewer = await open('/exercise?role=viewer');
    const exerciseMessages: any[] = []; exerciseViewer.on('message', m => exerciseMessages.push(JSON.parse(m.toString())));
    const producer = await open('/pose?role=producer');

    const started = await (await fetch(`http://127.0.0.1:${port}/exercise/start`, {method:'POST',
      body:JSON.stringify({side:'right', targetDeg:80, prescribedReps:2, planVersion:1, exercise:'shoulder-raise.v1'})})).json();
    assert.match(started.exerciseId, /^[0-9a-f-]{36}$/);

    const sessionId = randomUUID(); let sequence = 0, t = 0;
    const send = (arm: number) => { const f = frame(t, arm);
      producer.send(JSON.stringify({schemaVersion:'kinesthetic.session.v1', type:'pose.frame', sessionId, sourceId:'test', sequence:sequence++,
        payload:{frameID:sequence, source:'recorded-video', subjectDetected:true, observedAtMonotonicMs:t+1, ...f}})); t += 33; };
    for (let i = 0; i < 30; i++) send(5);
    for (const peak of [90, 60]) {
      for (let i = 0; i <= 18; i++) send(5 + (peak - 5) * i / 18);
      for (let i = 0; i < 15; i++) send(peak);
      for (let i = 18; i >= 0; i--) send(5 + (peak - 5) * i / 18);
      for (let i = 0; i < 15; i++) send(5);
    }
    await new Promise(r => setTimeout(r, 300));
    const summary = await (await fetch(`http://127.0.0.1:${port}/exercise/stop`, {method:'POST'})).json();
    assert.equal(summary.attempted, 2); assert.equal(summary.valid, 1); assert.equal(summary.planVersion, 1);
    assert.deepEqual(summary.invalidReasons, {did_not_reach_target: 1});

    await new Promise(r => setTimeout(r, 100));
    const events = exerciseMessages.filter(m => m.type === 'exercise.event').map(m => m.payload.type);
    assert.deepEqual(events, ['calibration.complete','rep.started','target.reached','rep.completed','rep.started','rep.completed']);
    assert.ok(exerciseMessages.some(m => m.type === 'exercise.sample' && m.payload.angleDeg > 85));
    assert.ok(exerciseMessages.some(m => m.type === 'exercise.summary'));
    assert.deepEqual([...poseTypes], ['pose.frame'], 'pose viewers must only see pose frames');

    const files = await readdir(directory);
    const saved = JSON.parse(await readFile(join(directory, `exercise-${started.exerciseId}.summary.json`), 'utf8'));
    assert.equal(saved.valid, 1);
    const poseLog = (await readFile(join(directory, `${sessionId}.jsonl`), 'utf8')).trim().split('\n').map(l => JSON.parse(l));
    assert.ok(poseLog.every(e => e.type === 'pose.frame'), 'pose recording stays loadable by Unity replay');
    assert.ok(files.includes(`exercise-${started.exerciseId}.jsonl`));
    assert.equal((await fetch(`http://127.0.0.1:${port}/exercise/stop`, {method:'POST'})).status, 409);
  } finally {
    for (const ws of sockets) ws.close();
    await stop(child);
    await rm(directory, {recursive:true, force:true});
  }
});
