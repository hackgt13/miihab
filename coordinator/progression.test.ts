import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { randomUUID } from 'node:crypto';
import { WebSocket } from 'ws';
import { PlanStore, type Plan } from './plans.ts';
import { evaluate, type SessionEvidence } from './progression.ts';
import { frame } from './synthetic-pose.ts';

// Plan history for the pure-rule tests: v1 at 45° (band 45–60), approved before any session.
function plans(changes: Parameters<PlanStore['approve']>[0]['changes'] = undefined): Plan[] {
  const store = new PlanStore(mkdtempSync(join(tmpdir(), 'prog-')));
  if (changes) store.approve({ rationale: 'test setup', changes });
  return store.list().map(p => ({ ...p, approvedAt: '2026-09-26T00:00:00Z' }));
}
let clock = 0;
const session = (peaks: number[], extra: Partial<SessionEvidence> = {}): SessionEvidence => ({
  exerciseId: randomUUID(), prescriptionId: 'arm-elevation-right', planVersion: 1, endedAt: `2026-09-26T0${1 + (clock++ % 8)}:00:00Z`,
  targetDeg: 45, targetMaxDeg: 60, prescribed: 8, simulated: false, calibrated: true,
  reps: peaks.map(p => ({ peakDeg: p, valid: p >= 45 })), ...extra });
const good = () => session(Array(8).fill(52));

test('no sessions at the level: hold', () => {
  const p = evaluate(plans(), 'arm-elevation-right', []);
  assert.equal(p.decision, 'hold'); assert.equal(p.status, 'info'); assert.match(p.reasons[0], /No real sessions at 45°/);
});

test('two good sessions in a row move up one step and keep the ceiling margin', () => {
  const p = evaluate(plans(), 'arm-elevation-right', [good(), good()]);
  assert.equal(p.decision, 'progress'); assert.deepEqual(p.to, { targetDeg: 50, targetMaxDeg: 65 });
  assert.equal(p.evidence.length, 2); assert.equal(p.status, 'pending');
});

test('one good session is not enough; a weak latest session says what was missing', () => {
  const p = evaluate(plans(), 'arm-elevation-right', [good(), session([52, 52, 52, 52, 52, 30, 30, 30])]);
  assert.equal(p.decision, 'hold');
  assert.match(p.reasons.join(' '), /0 of 2 good sessions/); assert.match(p.reasons.join(' '), /5\/8 reps in the band; 7 needed/);
});

test('overshooting the safe ceiling more than allowed steps back; at the floor it goes to the clinician', () => {
  const overshoot = session([52, 70, 72, 75, 52, 52, 52, 52]);
  const back = evaluate(plans({ 'arm-elevation-right': { params: { targetDeg: 50 } } }), 'arm-elevation-right',
    [{ ...overshoot, targetDeg: 50, targetMaxDeg: 65 }]);
  assert.equal(back.decision, 'regress'); assert.deepEqual(back.to, { targetDeg: 45, targetMaxDeg: 60 });
  const floor = evaluate(plans({ 'arm-elevation-right': { params: { targetDeg: 40 } } }), 'arm-elevation-right',
    [{ ...overshoot, targetDeg: 40, targetMaxDeg: 55 }]);
  assert.equal(floor.decision, 'clinician_review'); assert.equal(floor.to, null);
  const tolerated = evaluate(plans(), 'arm-elevation-right', [session([52, 52, 52, 52, 52, 52, 52, 70])]);
  assert.equal(tolerated.decision, 'hold', 'one overshoot is within the limit of 2'); assert.match(tolerated.reasons.join(' '), /above 60°/);
});

test('a pain flare steps back even after good sessions', () => {
  const all = plans({ 'arm-elevation-right': { params: { targetDeg: 50 } } });
  const sessions = [{ ...good(), targetDeg: 50, targetMaxDeg: 65 }, { ...good(), targetDeg: 50, targetMaxDeg: 65 }];
  assert.equal(evaluate(all, 'arm-elevation-right', sessions, { pain: [{ level: 3, phase: 'start' }, { level: 4, phase: 'end' }] }).decision, 'progress');
  const high = evaluate(all, 'arm-elevation-right', sessions, { pain: [{ level: 2, phase: 'start' }, { level: 7, phase: 'during' }] });
  assert.equal(high.decision, 'regress'); assert.match(high.reasons.join(' '), /Pain reached 7\/10/);
  const rise = evaluate(all, 'arm-elevation-right', sessions, { pain: [{ level: 1, phase: 'start' }, { level: 3, phase: 'end' }] });
  assert.equal(rise.decision, 'regress'); assert.match(rise.reasons.join(' '), /rose 2 points/);
});

test('past the envelope the clinician decides', () => {
  const all = plans({ 'arm-elevation-right': { params: { targetDeg: 90 } } });
  const p = evaluate(all, 'arm-elevation-right', [1, 2].map(() => ({ ...session(Array(8).fill(95)), targetDeg: 90, targetMaxDeg: 105 })));
  assert.equal(p.decision, 'clinician_review'); assert.match(p.reasons.join(' '), /past the envelope \(90°\)/);
});

test('simulated sessions and sessions from before the level started do not count', () => {
  assert.equal(evaluate(plans(), 'arm-elevation-right', [good(), { ...good(), simulated: true }]).decision, 'hold');
  assert.equal(evaluate(plans(), 'arm-elevation-right', [good(), { ...good(), simulated: true }], { includeSimulated: true }).decision, 'progress');
  assert.equal(evaluate(plans(), 'arm-elevation-right', [good(), good(), { ...session([]), calibrated: false }]).decision, 'progress',
    'a session that never calibrated is a setup failure, not a bad session');
  const early = { ...good(), endedAt: '2026-09-25T23:00:00Z' };
  assert.equal(evaluate(plans(), 'arm-elevation-right', [good(), early]).decision, 'hold');
});

test('server: auto-progression inside the envelope, overshoot step-back, and clinician approval when auto-apply is off', {timeout:30000}, async () => {
  const dir = mkdtempSync(join(tmpdir(), 'progapi-')), port = 18775, base = `http://127.0.0.1:${port}`;
  const child = spawn(process.execPath, ['server.ts'], {cwd:import.meta.dirname, stdio:['ignore','pipe','pipe'],
    env:{...process.env, KINESTHETIC_PORT:String(port), KINESTHETIC_RECORDINGS_DIRECTORY:join(dir, 'rec'),
      KINESTHETIC_PLANS_DIRECTORY:join(dir, 'plans'), KINESTHETIC_PROPOSALS_DIRECTORY:join(dir, 'proposals'), KINESTHETIC_SOCIAL_DIRECTORY:join(dir, 'social')}});
  const post = (path: string, body?: unknown) => fetch(base + path, {method:'POST', body: body ? JSON.stringify(body) : undefined});
  try {
    await once(child.stdout, 'data');
    const producer = new WebSocket(`ws://127.0.0.1:${port}/pose?role=producer`); await once(producer, 'open');
    const poseSession = randomUUID(); let seq = 0, t = 0;
    const send = (arm: number) => producer.send(JSON.stringify({schemaVersion:'kinesthetic.session.v1', type:'pose.frame', sessionId:poseSession,
      sourceId:'camera', sequence:seq++, payload:{frameID:seq, source:'camera', subjectDetected:true, observedAtMonotonicMs:(t += 33), ...frame(t, arm)}}));
    const run = async (peaks: number[]) => {
      await post('/exercise/start', {prescribedReps: peaks.length, exercise: 'shoulder-raise.v1'});
      for (const peak of peaks) {
        for (let i = 0; i < 30; i++) send(5);
        for (let i = 0; i <= 18; i++) send(5 + (peak - 5) * i / 18); for (let i = 0; i < 15; i++) send(peak);
        for (let i = 18; i >= 0; i--) send(5 + (peak - 5) * i / 18); for (let i = 0; i < 15; i++) send(5);
      }
      await new Promise(r => setTimeout(r, 150));
      return (await (await post('/exercise/stop')).json()).progression;
    };
    const active = async () => (await (await fetch(base + '/api/plans/active')).json());

    assert.equal((await run([52, 52])).decision, 'hold');
    const up = await run([53, 53]);
    assert.equal(up.decision, 'progress'); assert.equal(up.status, 'applied'); assert.equal(up.appliedPlanVersion, 2);
    let plan = await active();
    assert.equal(plan.version, 2); assert.equal(plan.origin, 'auto-progression'); assert.equal(plan.proposalId, up.id);
    assert.match(plan.approvedBy, /within PM&R physician \(demo\)'s envelope/); assert.equal(plan.activities[0].params.targetDeg, 50);

    const down = await run([58, 72, 74, 76]);
    assert.equal(down.decision, 'regress'); assert.equal(down.status, 'applied');
    plan = await active(); assert.equal(plan.version, 3); assert.equal(plan.activities[0].params.targetDeg, 45);

    const off = await post('/api/plans', {rationale:'Review every level change myself.', changes:{'arm-elevation-right':{progression:{autoApply:false}}}});
    assert.equal(off.status, 201);
    await run([52, 52]); const waiting = await run([52, 52]);
    assert.equal(waiting.decision, 'progress'); assert.equal(waiting.status, 'pending');
    assert.equal((await active()).version, 4, 'nothing applied without the clinician');
    const pending = (await (await fetch(base + '/api/proposals')).json()).filter((p: any) => p.status === 'pending');
    assert.deepEqual(pending.map((p: any) => p.id), [waiting.id]);
    const approved = await (await post(`/api/proposals/${waiting.id}/approve`, {approvedBy:'Dr. Demo'})).json();
    assert.equal(approved.plan.version, 5); assert.equal(approved.plan.origin, 'clinician'); assert.equal(approved.plan.activities[0].params.targetDeg, 50);
    assert.equal(approved.proposal.status, 'approved');
    assert.equal((await post(`/api/proposals/${waiting.id}/approve`)).status, 409, 'a proposal applies once');
    producer.close();
  } finally { child.kill(); await once(child, 'exit'); }
});
