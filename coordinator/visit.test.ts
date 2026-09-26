import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { PlanStore } from './plans.ts';
import { applyProgramUpdate, buildVisit, therapistFromEnv, VisitStore } from './visit.ts';
import { clean, seed } from './seed-visit.ts';

const dir = (p: string) => mkdtempSync(join(tmpdir(), p));

test('with one plan and no notes the board states the program, and claims no change', () => {
  const plans = new PlanStore(dir('visit-'));
  const v = buildVisit({ plans: plans.list(), notes: [] });
  assert.equal(v.schema, 'kinesthetic.visit.v1');
  assert.deepEqual(v.board.updates.map(u => [u.kind, u.heading, u.detail]), [
    ['program', 'Shoulder raises', '8 reps · right · to 45°'],
    ['program', 'Biceps curls', '10 reps · right · to 90° · 0.5 kg'],
    ['program', 'Golf', '9 holes with a friend'],
  ]);
  assert.match(v.board.attribution, /^From your licensed therapist · Alex, PT, DPT \(sample\)$/);
  assert.ok(!v.speech.some(l => /has changed in|have changed in|I've added/.test(l.text)), 'nothing changed, so nothing says it did');
});

test('a new plan version shows what the clinician changed, and each line reveals its own board item', () => {
  const plans = new PlanStore(dir('visit-'));
  plans.approve({ approvedBy: 'PT', rationale: 'Two good sessions in band.',
    changes: { 'arm-elevation-right': { params: { targetDeg: 50 } } }, remove: ['elbow-flexion-right'] } as any);
  const v = buildVisit({ plans: plans.list(), notes: [] });
  assert.deepEqual(v.board.updates.map(u => [u.kind, u.heading, u.detail]), [
    ['changed', 'Shoulder raises', 'target 45° → 50°, a little higher'],
    ['removed', 'Resting: Biceps curls', 'off your program for now'],
  ]);
  assert.match(v.speech[1].text, /2 things have changed/);
  // Speech walks the board in order: a line never reveals less than the one before it.
  const reveals = v.speech.map(l => l.reveal);
  assert.deepEqual(reveals, [...reveals].sort((a, b) => a - b));
  assert.equal(reveals.at(-1), v.board.updates.length);
  assert.ok(v.speech.every(l => l.audio === null), 'no voice yet: captions only');
});

test('therapist notes land on the board, newest three, and move the updated date', () => {
  const d = dir('visit-notes-');
  const notes = new VisitStore(d);
  for (const [i, text] of ['one', 'two', 'three', 'Ice after golf.'].entries()) notes.add({ text, author: 'Alex' }, new Date(2030, 0, i + 1));
  const reread = new VisitStore(d).list();
  assert.equal(reread.length, 4, 'notes persist');
  const v = buildVisit({ plans: new PlanStore(dir('visit-')).list(), notes: reread });
  assert.deepEqual(v.board.updates.filter(u => u.kind === 'note').map(u => u.detail), ['two', 'three', 'Ice after golf.']);
  assert.equal(v.board.updatedAt, reread[3].at);
  assert.ok(v.speech.some(l => l.text === 'Ice after golf.'));
});

test('a note is validated, and can be taken back', () => {
  const notes = new VisitStore(dir('visit-notes-'));
  assert.throws(() => notes.add({ text: '   ' }), /needs text/);
  assert.throws(() => notes.add({ text: 'x'.repeat(281) }), /at most 280/);
  const n = notes.add({ text: '  Keep   it slow.  ' });
  assert.equal(n.text, 'Keep it slow.');
  assert.equal(notes.remove(n.id), true);
  assert.equal(notes.remove(n.id), false);
});

test('a real therapist comes from the environment and is not labelled a sample', () => {
  assert.deepEqual(therapistFromEnv({}), { name: 'Alex', credentials: 'PT, DPT', sample: true });
  assert.deepEqual(therapistFromEnv({ KINESTHETIC_THERAPIST_NAME: 'Dr. Kim', KINESTHETIC_THERAPIST_CREDENTIALS: 'PT, DPT, NCS' }),
    { name: 'Dr. Kim', credentials: 'PT, DPT, NCS', sample: false });
});

test('a program update approves a plan version and leaves its note, or neither', () => {
  const plans = new PlanStore(dir('visit-'));
  const visit = new VisitStore(dir('visit-notes-'));
  assert.throws(() => applyProgramUpdate(plans, visit, { approvedBy: 'Alex', rationale: 'Too long a note.',
    changes: { 'arm-elevation-right': { targetCount: 10 } }, patientNote: 'x'.repeat(281) }), /at most 280/);
  assert.equal(plans.active().version, 1, 'a bad note stops the plan change too');
  assert.throws(() => applyProgramUpdate(plans, visit, { rationale: 'Unsigned change.', changes: {} }), /approvedBy/);
  const { plan, note } = applyProgramUpdate(plans, visit, { approvedBy: 'Alex', rationale: 'Ready for more reps.',
    origin: 'auto-progression', changes: { 'arm-elevation-right': { targetCount: 10 } }, patientNote: 'Ten now.' });
  assert.equal(plan.version, 2);
  assert.equal(plan.origin, 'clinician', 'a therapist update can never pass itself off as automatic');
  assert.equal(note?.planVersion, 2);
  const v = buildVisit({ plans: plans.list(), notes: visit.list() });
  assert.deepEqual(v.board.updates[0], { kind: 'changed', heading: 'Shoulder raises', detail: '8 → 10 reps',
    why: 'Ready for more reps.', automatic: false, planVersion: 2 });
  assert.match(v.speech[2].text, /8 → 10 reps\. Ready for more reps\./);
});

test('the board spans every version since the last visit, and says which changes were automatic', () => {
  const plans = new PlanStore(dir('visit-'));
  const visit = new VisitStore(dir('visit-notes-'));
  visit.markSeen(1);
  plans.approve({ origin: 'auto-progression', approvedBy: 'rules', rationale: 'Two good sessions.', changes: { 'arm-elevation-right': { params: { targetDeg: 50 } } } });
  plans.approve({ origin: 'auto-progression', approvedBy: 'rules', rationale: 'Two more good sessions.', changes: { 'arm-elevation-right': { params: { targetDeg: 55 } } } });
  plans.approve({ approvedBy: 'Alex', rationale: 'Heavier curls.', changes: { 'elbow-flexion-right': { params: { loadKg: 1 } } } });
  const v = buildVisit({ plans: plans.list(), notes: [], seen: visit.seen });
  assert.equal(v.since?.planVersion, 1);
  assert.deepEqual(v.board.updates.map(u => [u.heading, u.detail, u.automatic, u.why]), [
    ['Shoulder raises', 'target 45° → 55°, a little higher', true, 'Two more good sessions.'],
    ['Biceps curls', 'weight 0.5 → 1 kg', false, 'Heavier curls.'],
  ]);
  assert.match(v.speech[1].text, /since your last visit/);
  assert.match(v.speech[2].text, /on its own, inside the range I set/);
  visit.markSeen(4);
  visit.markSeen(2);
  assert.equal(visit.seen?.planVersion, 4, 'seen never moves backwards');
  const after = buildVisit({ plans: plans.list(), notes: [], seen: visit.seen });
  assert.ok(after.board.updates.every(u => u.kind === 'program'), 'nothing new since the last visit');
});

test('the seeder writes a fortnight of changes the visit can talk about, and takes all of it back out', () => {
  const plans = new PlanStore(dir('visit-'));
  const visit = new VisitStore(dir('visit-notes-'));
  plans.approve({ approvedBy: 'real PT', rationale: 'A real change before the demo.', changes: { 'arm-elevation-right': { targetCount: 9 } } });
  const { from, to } = seed(plans, visit);
  assert.deepEqual([from, to], [2, 4]);
  assert.throws(() => seed(plans, visit), /Already seeded/);
  const v = buildVisit({ plans: plans.list(), notes: visit.list(), seen: visit.seen });
  // The raise moved twice, by two versions: the automatic step and the therapist's review each get their own row.
  assert.deepEqual(v.board.updates.map(u => [u.kind, u.heading, u.detail, u.automatic ?? null]), [
    ['changed', 'Shoulder raises', 'target 45° → 50°, a little higher', true],
    ['changed', 'Shoulder raises', '9 → 11 reps', false],
    ['changed', 'Biceps curls', 'weight 0.5 → 1 kg', false],
    ['changed', 'Golf', '9 → 18 holes', false],
    ['note', 'Note', 'Two more raises now, same line. If the last two feel heavy, rest a minute before them.', null],
    ['note', 'Note', 'Ice your shoulder for ten minutes after golf.', null],
  ]);
  // Each version's reason is said once.
  const review = v.speech.filter(l => l.text.includes('Week-two review'));
  assert.equal(review.length, 1);
  const dates = plans.list().map(p => Date.parse(p.approvedAt));
  assert.deepEqual(dates, [...dates].sort((a, b) => a - b), 'backdated versions stay in order');
  const removed = clean(plans, visit);
  assert.deepEqual(removed.plans, [3, 4]);
  assert.equal(plans.active().version, 2, 'the real version before the seed is untouched');
  assert.equal(visit.list().length, 0);
  assert.equal(visit.seen, null);
});

test('the visit ends by asking, and what the patient relays reaches the care team with a fixed answer', () => {
  const plans = new PlanStore(dir('visit-'));
  const visit = new VisitStore(dir('visit-notes-'));
  const v = buildVisit({ plans: plans.list(), notes: [] });
  assert.equal(v.speech.at(-1)!.text, v.ask.line, 'the last thing said is the question');
  assert.deepEqual(v.ask.quickReplies.map(r => r.kind), ['fine', 'easy', 'hard', 'hurt']);

  const hurt = visit.addReply({ kind: 'hurt', planVersion: 1 });
  assert.equal(hurt.reply.text, 'Something hurt.');
  assert.match(hurt.acknowledgement, /Stop any exercise that hurts/);
  const typed = visit.addReply({ kind: 'message', text: '  My   elbow clicks on the curl. ' });
  assert.equal(typed.reply.text, 'My elbow clicks on the curl.');
  assert.throws(() => visit.addReply({ kind: 'message', text: ' ' }), /needs text/);
  assert.throws(() => visit.addReply({ kind: 'vibes' }), /kind must be/);
  assert.throws(() => visit.addReply({ kind: 'message', text: 'x'.repeat(501) }), /at most 500/);
  assert.deepEqual(visit.replies().map(r => r.kind), ['hurt', 'message']);
});
