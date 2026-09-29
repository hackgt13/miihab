import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtemp, readdir, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { ACTIVITY_SUMMARY_SCHEMA, activitySummaryFromExercise, parseActivitySummary } from './activity.ts';
import { ready, stop } from './test-process.ts';

const golfRound = (over: Record<string, unknown> = {}) => ({
  schema: ACTIVITY_SUMMARY_SCHEMA,
  activitySessionId: 'golf-abc', activityId: 'golf.adaptive', exerciseKinds: [],
  venueId: 'resort-course', patientId: null, planVersion: null,
  startedAt: '2026-09-26T10:00:00.000Z', endedAt: '2026-09-26T10:12:30.000Z', durationMs: 750_000,
  completed: true,
  subjects: [
    {subjectId: 'patient', role: 'patient', dose: {prescribed: null, attempted: 9, valid: 7},
     primaryMetric: {name: 'strokes', value: 7, unit: 'strokes'}},
    {subjectId: 'friend', role: 'companion', dose: {prescribed: null, attempted: 6, valid: 6},
     primaryMetric: {name: 'strokes', value: 6, unit: 'strokes'}},
  ],
  trackingQuality: {validFrameRatio: 0.94, lossEvents: 1},
  flags: ['tracking_lost'],
  payload: {kind: 'golf.round', schemaVersion: '1', data: {holes: 1, club: 'Driver'}},
  ...over,
});

test('the envelope rejects records it could not be trusted to store', () => {
  assert.throws(() => parseActivitySummary({...golfRound(), schema: 'nope'}), /schema/);
  assert.throws(() => parseActivitySummary(golfRound({subjects: []})), /subjects/);
  assert.throws(() => parseActivitySummary(golfRound({flags: ['made_up_reason']})), /flags/);
  assert.throws(() => parseActivitySummary(golfRound({trackingQuality: {validFrameRatio: 4, lossEvents: 0}})), /\[0,1\]/);
  assert.throws(() => parseActivitySummary(golfRound({startedAt: 'yesterday'})), /ISO/);
  const bad = golfRound(); (bad.subjects as any)[0].dose = {prescribed: null, attempted: 2, valid: 5};
  assert.throws(() => parseActivitySummary(bad), /cannot exceed/);
  assert.throws(() => parseActivitySummary(golfRound({payload: {kind: 'x', schemaVersion: '1', data: {blob: 'y'.repeat(70_000)}}})), /64 KiB/);
});

test('an exercise session produces the same envelope shape as a golf round', () => {
  const e = activitySummaryFromExercise({
    activitySessionId: 'ex-1', activityId: 'rehab.studio', venueId: 'studio',
    startedAt: '2026-09-26T10:00:00.000Z', endedAt: '2026-09-26T10:04:00.000Z',
    measured: {exerciseKind: 'shoulder-raise.v1', planVersion: 3, attempted: 11, valid: 8, prescribed: 8,
      medianValidPeakDeg: 82.4, validFrameRatio: 0.96, trackingLossEvents: 2},
  });
  assert.equal(e.schema, ACTIVITY_SUMMARY_SCHEMA);
  assert.deepEqual(e.exerciseKinds, ['shoulder-raise.v1']);
  assert.equal(e.completed, true);
  assert.equal(e.durationMs, 240_000);
  assert.deepEqual(e.subjects[0].dose, {prescribed: 8, attempted: 11, valid: 8});
  assert.ok(e.flags.includes('tracking_lost'));
  assert.ok(!e.flags.includes('not_completed'), 'met the prescription, so not flagged incomplete');
});

test('an unmet prescription is flagged incomplete', () => {
  const e = activitySummaryFromExercise({
    activitySessionId: 'ex-2', activityId: 'rehab.studio',
    startedAt: '2026-09-26T10:00:00.000Z', endedAt: '2026-09-26T10:02:00.000Z',
    measured: {exerciseKind: 'shoulder-raise.v1', attempted: 4, valid: 3, prescribed: 8, trackingLossEvents: 0},
  });
  assert.equal(e.completed, false);
  assert.deepEqual(e.flags, ['not_completed']);
});

test('a set on simulated motion carries the simulated flag, and a game may send it', () => {
  const e = activitySummaryFromExercise({
    activitySessionId: 'ex-3', activityId: 'rehab.studio',
    startedAt: '2026-09-26T10:00:00.000Z', endedAt: '2026-09-26T10:02:00.000Z',
    measured: {exerciseKind: 'shoulder-raise.v1', attempted: 8, valid: 8, prescribed: 8, simulated: true},
  });
  assert.deepEqual(e.flags, ['simulated']);
  assert.deepEqual(parseActivitySummary(golfRound({flags: ['simulated']})).flags, ['simulated']);
});

test('live: an activity posts its own session record and it appears alongside exercise sessions', {timeout:40000}, async () => {
  const directory = await mkdtemp(join(tmpdir(), 'kinesthetic-activity-'));
  const port = 18783;   // every suite has its own ports (18766-18782 are taken); keep every test isolated
  const child = spawn(process.execPath, ['server.ts'], {cwd: import.meta.dirname,
    env: {...process.env, KINESTHETIC_PORT: String(port), KINESTHETIC_RECORDINGS_DIRECTORY: directory,
          KINESTHETIC_PLANS_DIRECTORY: join(directory, 'plans')}, stdio: ['ignore', 'pipe', 'pipe']});
  try {
    await ready(child);
    const post = (body: unknown) => fetch(`http://127.0.0.1:${port}/activity/session`, {method: 'POST', body: JSON.stringify(body)});

    const created = await post(golfRound());
    assert.equal(created.status, 201);
    assert.deepEqual(await created.json(), {stored: 'golf-abc'});

    const rejected = await post(golfRound({flags: ['nonsense']}));
    assert.equal(rejected.status, 400, 'the coordinator owns the stored shape, not the client');
    // An id the catalog does not know must be refused, or a session joins to no prescription.
    const unknown = await post(golfRound({activityId: 'golf.typo'}));
    assert.equal(unknown.status, 400);
    assert.match((await unknown.json()).error, /Unknown activity/);

    const files = await readdir(directory);
    assert.ok(files.includes('session-golf-abc.json'));
    const saved = JSON.parse(await readFile(join(directory, 'session-golf-abc.json'), 'utf8'));
    assert.equal(saved.activityId, 'golf.adaptive');

    const envelopes = await (await fetch(`http://127.0.0.1:${port}/api/activity-sessions`)).json();
    assert.equal(envelopes.length, 1);
    assert.equal(envelopes[0].schema, ACTIVITY_SUMMARY_SCHEMA, 'golf is visible to the coordinator now');
    assert.equal(envelopes[0].activityId, 'golf.adaptive');

    // The exercise-engine view must not gain a golf row, or the clinician chart double-counts.
    const exercises = await (await fetch(`http://127.0.0.1:${port}/api/sessions`)).json();
    assert.deepEqual(exercises, [], 'golf is not an exercise-engine session');
  } finally {
    await stop(child);
    await rm(directory, {recursive: true, force: true});
  }
});

// The exact JSON Newtonsoft emits for the anonymous object in KinestheticGolf.CompleteRound.
// C# cannot be compiled or run from the test suite, so this pins the producer/validator contract:
// if the C# shape and this envelope ever disagree, this test is where it shows up.
const UNITY_GOLF_ROUND = `{
  "schema": "kinesthetic.activity.v1",
  "activitySessionId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
  "activityId": "golf.adaptive",
  "exerciseKinds": [],
  "venueId": "resort-course",
  "patientId": null,
  "planVersion": null,
  "startedAt": "2026-09-26T10:00:00.0000000Z",
  "endedAt": "2026-09-26T10:12:30.0000000Z",
  "durationMs": 750000,
  "completed": true,
  "subjects": [
    {"subjectId": "patient", "role": "patient",
     "dose": {"prescribed": null, "attempted": 9, "valid": 7},
     "primaryMetric": {"name": "strokes", "value": 7.0, "unit": "strokes"}},
    {"subjectId": "friend", "role": "companion",
     "dose": {"prescribed": null, "attempted": 6, "valid": 6},
     "primaryMetric": {"name": "strokes", "value": 6.0, "unit": "strokes"}}
  ],
  "trackingQuality": {"validFrameRatio": null, "lossEvents": 2},
  "flags": ["tracking_lost"],
  "payload": {"kind": "golf.round", "schemaVersion": "1",
    "data": {"strokes": [7, 6], "misses": [2, 0], "acceptedShots": 13, "club": "Driver"}}
}`;

test("Unity's golf round envelope satisfies the coordinator contract", () => {
  const e = parseActivitySummary(JSON.parse(UNITY_GOLF_ROUND));
  assert.equal(e.activityId, 'golf.adaptive');
  assert.deepEqual(e.exerciseKinds, [], 'golf measures nothing clinical yet');
  assert.equal(e.subjects.length, 2);
  assert.deepEqual(e.subjects.map(s => s.role), ['patient', 'companion']);
  assert.deepEqual(e.subjects.map(s => s.subjectId), ['patient', 'friend']);
  // Dose is the cross-activity figure: swings that qualified but missed still count as attempts.
  assert.deepEqual(e.subjects[0].dose, {prescribed: null, attempted: 9, valid: 7});
  assert.equal(e.trackingQuality.lossEvents, 2);
  assert.deepEqual(e.flags, ['tracking_lost']);
  assert.deepEqual((e.payload.data as any).strokes, [7, 6]);
});
