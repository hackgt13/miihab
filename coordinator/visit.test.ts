import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { PlanStore } from './plans.ts';
import { buildVisit, therapistFromEnv, VisitNoteStore } from './visit.ts';

const dir = (p: string) => mkdtempSync(join(tmpdir(), p));

test('with one plan and no notes the board states the program, and claims no change', () => {
  const plans = new PlanStore(dir('visit-'));
  const v = buildVisit({ plans: plans.list(), notes: [] });
  assert.equal(v.schema, 'kinesthetic.visit.v1');
  assert.deepEqual(v.board.updates.map(u => [u.kind, u.heading, u.detail]), [
    ['program', 'Shoulder raises', '8 reps · right · to 45°'],
    ['program', 'Biceps curls', '10 reps · right · to 90°'],
    ['program', 'Golf', '9 holes with a friend'],
  ]);
  assert.match(v.board.attribution, /^From your licensed therapist · Alex, PT, DPT \(sample\)$/);
  assert.ok(!v.speech.some(l => /change/.test(l.text)), 'nothing changed, so nothing says it did');
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
  assert.match(v.speech[1].text, /2 changes/);
  // Speech walks the board in order: a line never reveals less than the one before it.
  const reveals = v.speech.map(l => l.reveal);
  assert.deepEqual(reveals, [...reveals].sort((a, b) => a - b));
  assert.equal(reveals.at(-1), v.board.updates.length);
  assert.ok(v.speech.every(l => l.audio === null), 'no voice yet: captions only');
});

test('therapist notes land on the board, newest three, and move the updated date', () => {
  const d = dir('visit-notes-');
  const notes = new VisitNoteStore(d);
  for (const [i, text] of ['one', 'two', 'three', 'Ice after golf.'].entries()) notes.add({ text, author: 'Alex' }, new Date(2030, 0, i + 1));
  const reread = new VisitNoteStore(d).list();
  assert.equal(reread.length, 4, 'notes persist');
  const v = buildVisit({ plans: new PlanStore(dir('visit-')).list(), notes: reread });
  assert.deepEqual(v.board.updates.filter(u => u.kind === 'note').map(u => u.detail), ['two', 'three', 'Ice after golf.']);
  assert.equal(v.board.updatedAt, reread[3].at);
  assert.ok(v.speech.some(l => l.text === 'Ice after golf.'));
});

test('a note is validated, and can be taken back', () => {
  const notes = new VisitNoteStore(dir('visit-notes-'));
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
