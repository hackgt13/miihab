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

test('plan v2 prescribes several activities, including one that measures nothing clinical', () => {
  const store = new PlanStore(mkdtempSync(join(tmpdir(), 'plansv2-')));
  const v2 = store.approve({
    rationale: 'Add the golf round the patient is actually working towards.',
    activities: [
      { activityId: 'rehab.studio', exerciseKind: 'shoulder-raise.v1', targetCount: 10,
        params: { side: 'right', targetDeg: 85, holdMs: 400, maxCompensationDeg: 10 } },
      { activityId: 'golf.adaptive', exerciseKind: null, targetCount: 9, note: 'Nine holes with a friend.' },
    ],
  });
  assert.equal(v2.activities.length, 2);
  assert.equal(v2.activities[1].activityId, 'golf.adaptive', 'golf is prescribable at all — v1 could not say this');
  assert.equal(v2.activities[1].exerciseKind, null);
  assert.deepEqual(v2.activities.map(a => a.order), [0, 1]);

  // A third exercise kind the registry knows but v1's string literal could never name.
  const v3 = store.approve({ rationale: 'Swap in trunk rotation for this block.',
    activities: [{ activityId: 'rehab.studio', exerciseKind: 'trunk-rotation.v1', targetCount: 12,
      params: { side: 'left', targetDeg: 45 } }] });
  assert.equal(v3.activities[0].exerciseKind, 'trunk-rotation.v1');

  // Bounds come from the prescribed kind, not one global table: 45° is legal for rotation, not for a raise.
  assert.throws(() => store.approve({ rationale: 'out of band for rotation',
    activities: [{ activityId: 'rehab.studio', exerciseKind: 'trunk-rotation.v1', targetCount: 5, params: { targetDeg: 120 } }] }), /targetDeg/);
  assert.throws(() => store.approve({ rationale: 'unknown kind',
    activities: [{ activityId: 'rehab.studio', exerciseKind: 'nope.v9', targetCount: 5 }] }), /Unknown exercise/);
  assert.throws(() => store.approve({ rationale: 'empty list', activities: [] }), /at least one/);
});

test('a v1 plan file on disk still loads, unrewritten, and reads as v2', () => {
  const dir = mkdtempSync(join(tmpdir(), 'plansmig-'));
  const legacy = {
    schema: 'kinesthetic.plan.v1', version: 1, approvedBy: 'Dr Legacy', approvedAt: '2026-01-01T00:00:00.000Z',
    rationale: 'Authored under the old schema.', basedOnExerciseIds: [],
    exercise: { type: 'seated_shoulder_raise', side: 'left', targetDeg: 75, prescribedReps: 6, holdMs: 300, maxTrunkDeviationDeg: 9 },
    coachingNote: 'Steady.',
  };
  const raw = JSON.stringify(legacy, null, 2);
  writeFileSync(join(dir, 'plan-v1.json'), raw);

  const store = new PlanStore(dir);                       // must not seed over an existing plan
  const active = store.active();
  assert.equal(active.version, 1);
  assert.equal(active.schema, 'kinesthetic.plan.v2', 'upgraded on read');
  assert.equal(active.activities.length, 1);
  assert.equal(active.activities[0].exerciseKind, 'shoulder-raise.v1', 'the v1 type literal maps to a registry id');
  assert.equal(active.activities[0].targetCount, 6);
  assert.equal(active.activities[0].params.targetDeg, 75);
  assert.equal(active.activities[0].params.maxCompensationDeg, 9, 'maxTrunkDeviationDeg is the v1 spelling');
  assert.equal(readFileSync(join(dir, 'plan-v1.json'), 'utf8'), raw, 'an approved record is never rewritten');

  // The derived v1 view keeps existing readers (server.ts, the portal) working untouched.
  assert.equal(active.exercise.targetDeg, 75);
  assert.equal(active.exercise.prescribedReps, 6);
  assert.equal(active.exercise.maxTrunkDeviationDeg, 9);
  assert.equal(active.exercise.side, 'left');

  // Approving on top of a v1 file writes clean v2, with no derived view on disk.
  const next = store.approve({ rationale: 'Progress the target after a good week.', exercise: { targetDeg: 90 } });
  assert.equal(next.version, 2);
  assert.equal(next.activities[0].params.targetDeg, 90);
  assert.equal(next.activities[0].targetCount, 6, 'reps carry over');
  const stored = JSON.parse(readFileSync(join(dir, 'plan-v2.json'), 'utf8'));
  assert.equal(stored.schema, 'kinesthetic.plan.v2');
  assert.equal(stored.exercise, undefined, 'the compatibility view is computed, never persisted');
});
