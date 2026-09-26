import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { randomUUID } from 'node:crypto';
import { WebSocket } from 'ws';
import { PlanStore } from './plans.ts';
import { frame } from './synthetic-pose.ts';

test('plan store seeds a goal-based plan, approves immutable versions, and validates against the library', () => {
  const dir = mkdtempSync(join(tmpdir(), 'plans-'));
  const store = new PlanStore(dir);
  const v1 = store.active();
  assert.equal(v1.version, 1); assert.equal(v1.schema, 'kinesthetic.plan.v2'); assert.match(v1.goal.text, /golf/);
  assert.deepEqual(v1.exercises.map(e => e.id), ['shoulder-raise-right', 'curl-right']);
  const raise = v1.exercises[0];
  assert.equal(raise.targetDeg, 45); assert.equal(raise.maxSafeDeg, 60);
  const v1File = readFileSync(join(dir, 'plan-v1.json'), 'utf8');

  const bad = (input: any, pattern: RegExp) => assert.throws(() => store.approve({ rationale: 'a valid rationale', ...input }), pattern);
  assert.throws(() => store.approve({ rationale: '' }), /rationale/);
  bad({ changes: { 'shoulder-raise-right': { targetDeg: 170 } } }, /targetDeg must be between/);
  bad({ changes: { 'shoulder-raise-right': { targetDeg: 95 } } }, /inside the progression envelope/);
  bad({ changes: { 'shoulder-raise-right': { maxSafeDeg: 45 } } }, /ceiling must be above/);
  bad({ changes: { 'curl-right': { maxTrunkDeviationDeg: 10 } } }, /needs the camera/);
  bad({ changes: { 'curl-right': { sensor: 'pose' } } }, /can be measured with imu/);
  bad({ changes: { nope: { targetDeg: 50 } } }, /No exercise "nope"/);
  bad({ add: [{ type: 'moonwalk' }] }, /Unknown exercise type/);
  bad({ remove: ['shoulder-raise-right', 'curl-right'] }, /at least one exercise/);
  bad({ expectedActiveVersion: 7 }, /reload/);

  const v2 = store.approve({ rationale: 'Reach improved; progress one level.', changes: { 'shoulder-raise-right': { targetDeg: '50' as any } } });
  assert.equal(v2.version, 2); assert.equal(v2.origin, 'clinician');
  assert.equal(v2.exercises[0].targetDeg, 50); assert.equal(v2.exercises[0].maxSafeDeg, 65, 'ceiling keeps its margin');
  assert.equal(v2.exercises[0].prescribedReps, 8); assert.equal(v2.exercises[1].targetDeg, 90, 'other exercises unchanged');

  const v3 = store.approve({ rationale: 'Swap curl for left side.', remove: ['curl-right'], add: [{ type: 'biceps_curl', side: 'left', loadKg: 1 }] });
  assert.deepEqual(v3.exercises.map(e => e.id), ['shoulder-raise-right', 'biceps-curl-left']);
  assert.equal(readFileSync(join(dir, 'plan-v1.json'), 'utf8'), v1File, 'v1 unchanged');
  assert.equal(new PlanStore(dir).active().version, 3, 'survives restart');
});

test('plans saved in the v1 format are read as v2', () => {
  const dir = mkdtempSync(join(tmpdir(), 'plans-v1-'));
  writeFileSync(join(dir, 'plan-v1.json'), JSON.stringify({ schema: 'kinesthetic.plan.v1', version: 1, approvedBy: 'Dr. A', approvedAt: '2026-09-25T00:00:00Z',
    rationale: 'Initial', basedOnExerciseIds: [], coachingNote: 'Sit tall.',
    exercise: { type: 'seated_shoulder_raise', side: 'left', targetDeg: 80, prescribedReps: 8, holdMs: 400, maxTrunkDeviationDeg: 12 } }));
  const plan = new PlanStore(dir).active();
  assert.equal(plan.schema, 'kinesthetic.plan.v2');
  const [e] = plan.exercises;
  assert.equal(e.id, 'shoulder-raise-left'); assert.equal(e.sensor, 'pose'); assert.equal(e.targetDeg, 80); assert.equal(e.maxSafeDeg, 95);
  assert.equal(e.maxTrunkDeviationDeg, 12);
});

test('approving v2 through the API changes the next session but not the running one', {timeout:15000}, async () => {
  const recordings = mkdtempSync(join(tmpdir(), 'rec-')), plansDir = mkdtempSync(join(tmpdir(), 'plansapi-'));
  const port = 18771, base = `http://127.0.0.1:${port}`;
  const child = spawn(process.execPath, ['server.ts'], {cwd:import.meta.dirname,
    env:{...process.env, KINESTHETIC_PORT:String(port), KINESTHETIC_RECORDINGS_DIRECTORY:recordings, KINESTHETIC_PLANS_DIRECTORY:plansDir,
      KINESTHETIC_PROPOSALS_DIRECTORY:mkdtempSync(join(tmpdir(), 'prop-'))}, stdio:['ignore','pipe','pipe']});
  const post = (path: string, body?: unknown) => fetch(base + path, {method:'POST', body: body ? JSON.stringify(body) : undefined});
  try {
    await once(child.stdout, 'data');
    const producer = new WebSocket(`ws://127.0.0.1:${port}/pose?role=producer`); await once(producer, 'open');
    const sessionId = randomUUID(); let seq = 0, t = 0;
    const send = (arm: number) => { producer.send(JSON.stringify({schemaVersion:'kinesthetic.session.v1', type:'pose.frame', sessionId, sourceId:'t', sequence:seq++,
      payload:{frameID:seq, source:'recorded-video', subjectDetected:true, observedAtMonotonicMs:t+1, ...frame(t, arm)}})); t += 33; };
    const rep = (peak: number) => {
      for (let i = 0; i < 30; i++) send(5);
      for (let i = 0; i <= 18; i++) send(5 + (peak - 5) * i / 18); for (let i = 0; i < 15; i++) send(peak);
      for (let i = 18; i >= 0; i--) send(5 + (peak - 5) * i / 18); for (let i = 0; i < 15; i++) send(5);
    };

    const first = await (await post('/exercise/start', {sensor:'pose'})).json();   // no plan fields: pins the active plan (v1)
    assert.equal(first.config.planVersion, 1); assert.equal(first.config.targetDeg, 45); assert.equal(first.config.targetMaxDeg, 60);
    const v2 = await post('/api/plans', {rationale:'Reach consistently above 90°; progress target.', expectedActiveVersion:1, origin:'auto-progression',
      changes:{'shoulder-raise-right':{targetDeg:95, progression:{maxTargetDeg:100}}}});
    assert.equal(v2.status, 201); assert.equal((await v2.json()).origin, 'clinician', 'the API cannot mark a version automatic');
    rep(55);
    await new Promise(r => setTimeout(r, 200));
    const s1 = await (await post('/exercise/stop')).json();
    assert.equal(s1.planVersion, 1); assert.equal(s1.valid, 1, '55° counts under v1 (target 45) even though v2 was approved mid-session');
    assert.equal(s1.overshoots, 0);

    const second = await (await post('/exercise/start', {sensor:'pose'})).json();
    assert.equal(second.config.planVersion, 2); assert.equal(second.config.targetDeg, 95);
    rep(90);
    await new Promise(r => setTimeout(r, 200));
    const s2 = await (await post('/exercise/stop')).json();
    assert.equal(s2.planVersion, 2); assert.equal(s2.valid, 0, 'the same kind of rep falls short of the v2 target');

    const sessions = await (await fetch(base + '/api/sessions')).json();
    assert.deepEqual(sessions.map((s: any) => s.planVersion).sort(), [1, 2]);
    assert.ok(sessions.every((s: any) => s.planExerciseId === 'shoulder-raise-right'));
    assert.equal((await (await fetch(base + '/api/plans')).json()).length, 2);
    assert.equal((await post('/exercise/start', {exerciseId:'curl-right', sensor:'pose'})).status, 400, 'the camera cannot measure a curl');
    assert.equal((await fetch(base + '/portal/')).status, 200);
    producer.close();
  } finally { child.kill(); await once(child, 'exit'); }
});
