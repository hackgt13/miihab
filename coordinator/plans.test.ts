import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { randomUUID } from 'node:crypto';
import { WebSocket } from 'ws';
import { PlanStore } from './plans.ts';
import { frame } from './synthetic-pose.ts';

test('plan store seeds v1, approves immutable new versions, and validates changes', () => {
  const dir = mkdtempSync(join(tmpdir(), 'plans-'));
  const store = new PlanStore(dir);
  assert.equal(store.active().version, 1);
  const v1 = readFileSync(join(dir, 'plan-v1.json'), 'utf8');
  assert.throws(() => store.approve({ rationale: '' }), /rationale/);
  assert.throws(() => store.approve({ rationale: 'too far', exercise: { targetDeg: 170 } }), /targetDeg/);
  assert.throws(() => store.approve({ rationale: 'stale view', expectedActiveVersion: 7, exercise: { targetDeg: 85 } }), /reload/);
  const v2 = store.approve({ rationale: 'Reach improved; keep target, tighten trunk control.', exercise: { targetDeg: 85, maxTrunkDeviationDeg: 10 }, basedOnExerciseIds: ['x'] });
  assert.equal(v2.version, 2); assert.equal(v2.exercise.targetDeg, 85); assert.equal(v2.exercise.prescribedReps, 8);
  assert.equal(readFileSync(join(dir, 'plan-v1.json'), 'utf8'), v1, 'v1 unchanged');
  assert.equal(new PlanStore(dir).active().version, 2, 'survives restart');
});

test('approving v2 through the API changes the next session but not the running one', {timeout:15000}, async () => {
  const recordings = mkdtempSync(join(tmpdir(), 'rec-')), plansDir = mkdtempSync(join(tmpdir(), 'plansapi-'));
  const port = 18771, base = `http://127.0.0.1:${port}`;
  const child = spawn(process.execPath, ['server.ts'], {cwd:import.meta.dirname,
    env:{...process.env, KINESTHETIC_PORT:String(port), KINESTHETIC_RECORDINGS_DIRECTORY:recordings, KINESTHETIC_PLANS_DIRECTORY:plansDir}, stdio:['ignore','pipe','pipe']});
  const post = (path: string, body?: unknown) => fetch(base + path, {method:'POST', body: body ? JSON.stringify(body) : undefined});
  try {
    await once(child.stdout, 'data');
    const producer = new WebSocket(`ws://127.0.0.1:${port}/pose?role=producer`); await once(producer, 'open');
    const sessionId = randomUUID(); let seq = 0, t = 0;
    const send = (arm: number) => { producer.send(JSON.stringify({schemaVersion:'kinesthetic.session.v1', type:'pose.frame', sessionId, sourceId:'t', sequence:seq++,
      payload:{frameID:seq, source:'recorded-video', subjectDetected:true, observedAtMonotonicMs:t+1, ...frame(t, arm)}})); t += 33; };

    const first = await (await post('/exercise/start', {})).json();           // no fields: pins the active plan (v1)
    assert.equal(first.config.planVersion, 1); assert.equal(first.config.targetDeg, 80);
    const v2 = await post('/api/plans', {rationale:'Reach consistently above 90°; progress target.', expectedActiveVersion:1, exercise:{targetDeg:95}});
    assert.equal(v2.status, 201);
    for (let i = 0; i < 30; i++) send(5);
    for (let i = 0; i <= 18; i++) send(5 + 85 * i / 18); for (let i = 0; i < 15; i++) send(90);
    for (let i = 18; i >= 0; i--) send(5 + 85 * i / 18); for (let i = 0; i < 15; i++) send(5);
    await new Promise(r => setTimeout(r, 200));
    const s1 = await (await post('/exercise/stop')).json();
    assert.equal(s1.planVersion, 1); assert.equal(s1.valid, 1, '90° counts under v1 (target 80) even though v2 was approved mid-session');

    const second = await (await post('/exercise/start', {})).json();
    assert.equal(second.config.planVersion, 2); assert.equal(second.config.targetDeg, 95);
    for (let i = 0; i < 30; i++) send(5);
    for (let i = 0; i <= 18; i++) send(5 + 85 * i / 18); for (let i = 0; i < 15; i++) send(90);
    for (let i = 18; i >= 0; i--) send(5 + 85 * i / 18); for (let i = 0; i < 15; i++) send(5);
    await new Promise(r => setTimeout(r, 200));
    const s2 = await (await post('/exercise/stop')).json();
    assert.equal(s2.planVersion, 2); assert.equal(s2.valid, 0, 'the same 90° rep falls short of the v2 target');

    const sessions = await (await fetch(base + '/api/sessions')).json();
    assert.deepEqual(sessions.map((s: any) => s.planVersion).sort(), [1, 2]);
    assert.equal((await (await fetch(base + '/api/plans')).json()).length, 2);
    assert.equal((await fetch(base + '/portal/')).status, 200);
    producer.close();
  } finally { child.kill(); await once(child, 'exit'); }
});
