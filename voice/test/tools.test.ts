// Offline: a fake coordinator over HTTP and an in-memory patient store. No ElevenLabs or Supabase calls.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { once } from 'node:events';
import type { AddressInfo } from 'node:net';
import { CoordinatorService } from '../backend/services/coordinatorService.ts';
import { AnalyticsService } from '../backend/services/analyticsService.ts';
import { ToolService, TOOL_DEFINITIONS } from '../backend/services/toolService.ts';
import type { PatientService } from '../backend/services/patientService.ts';

const plan = { version: 2, approvedBy: 'Dr. Demo', approvedAt: '2026-09-26T06:00:00Z', rationale: 'Progress range', origin: 'clinician',
  goal: { text: 'Play golf again', components: ['shoulder elevation'] },
  activities: [{ id: 'arm-elevation-right', activityId: 'rehab.studio', exerciseKind: 'arm-elevation.v1', targetCount: 8,
    params: { side: 'right', targetDeg: 85, targetMaxDeg: 100, holdMs: 600 }, note: '' }],
  coachingNote: 'Sit tall.' };
const summary = (id: string, endedAt: string, valid: number, peak: number | null) => ({ exerciseId: id, endedAt,
  exercise: 'seated_shoulder_raise', side: 'right', planVersion: 2, config: { targetDeg: 85 }, prescribed: 8,
  attempted: valid + 1, valid, completed: valid, medianValidPeakDeg: peak, invalidReasons: { trunk_compensation: 1 },
  trackingLossEvents: 0, simulated: false });

async function fakeCoordinator(sessions: unknown[], replies: unknown[] = []) {
  const server = createServer(async (req, res) => {
    if (req.method === 'POST' && req.url === '/api/visit/replies') {
      let text = ''; for await (const chunk of req) text += chunk;
      replies.push(JSON.parse(text));
      res.writeHead(201, { 'Content-Type': 'application/json' }).end('{}'); return;
    }
    const body = req.url === '/api/plans/active' ? plan : req.url === '/api/sessions' ? sessions
      : req.url === '/exercise' ? { running: false } : null;
    res.writeHead(body ? 200 : 404, { 'Content-Type': 'application/json' }).end(JSON.stringify(body ?? {}));
  });
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  return { server, url: `http://127.0.0.1:${(server.address() as AddressInfo).port}` };
}

function memoryPatients(patient: Record<string, unknown> | null) {
  const writes: Record<string, unknown[]> = { pain: [], reviews: [], endSession: [] };
  const service = {
    getPatient: async () => patient,
    getRecentSessions: async () => [], getAllSessions: async () => [], getRecentPainLogs: async () => [],
    getAllPainLogs: async () => [], getRecentMilestones: async () => [], getSessionPainLogs: async () => [],
    getSessionPainAverage: async () => null,
    logPain: async (...a: unknown[]) => { writes.pain!.push(a); return { id: 'pain-1' }; },
    addPlanReviewRequest: async (...a: unknown[]) => { writes.reviews!.push(a); return { id: 'review-1' }; },
    endSession: async (...a: unknown[]) => { writes.endSession!.push(a); },
  };
  return { service: service as unknown as PatientService, writes };
}

function tools(patients: PatientService, coordinatorUrl: string, events: unknown[] = []) {
  const coordinator = new CoordinatorService(coordinatorUrl);
  return new ToolService(patients, new AnalyticsService(patients, coordinator), coordinator, 'patient-1',
    { id: 'session-1', startedAt: '2026-09-26T07:00:00Z' }, e => events.push(e));
}

test('the agent cannot log reps or change the plan; it can only ask for a review', () => {
  const names = TOOL_DEFINITIONS.map(t => t.name);
  assert.ok(!names.includes('log_exercise_session' as never));
  assert.ok(names.includes('get_exercise_results') && names.includes('request_plan_review') && names.includes('close_session'));
});

test('an empty patient row (created with the session) is a first visit, and the plan comes along', async () => {
  const { server, url } = await fakeCoordinator([]);
  try {
    const profile = await tools(memoryPatients({ id: 'patient-1', goals: [], precautions: [] }).service, url)
      .dispatch('get_patient_profile', {}) as any;
    assert.equal(profile.is_new_patient, true);
    assert.equal(profile.care_plan.version, 2);
    const known = await tools(memoryPatients({ id: 'patient-1', name: 'Sam', goals: [], precautions: [] }).service, url)
      .dispatch('get_patient_profile', {}) as any;
    assert.equal(known.is_new_patient, false);
  } finally { server.close(); }
});

test('exercise results and the session close use measured data from this session only', async () => {
  const { server, url } = await fakeCoordinator([
    summary('ex-new', '2026-09-26T07:05:00Z', 6, 83.4),
    summary('ex-old', '2026-09-25T07:05:00Z', 4, 71),
  ]);
  try {
    const { service, writes } = memoryPatients({ id: 'patient-1', name: 'Sam', goals: [], precautions: [] });
    const events: any[] = [];
    const t = tools(service, url, events);
    const results = await t.dispatch('get_exercise_results', {}) as any;
    assert.deepEqual(results.completed_this_session.map((r: any) => [r.exercise_id, r.valid, r.median_peak_deg]), [['ex-new', 6, 83]]);
    const closed = await t.dispatch('close_session', { session_notes: 'Felt good' }) as any;
    assert.deepEqual(writes.endSession![0], ['session-1', { notes: 'Felt good', exerciseIds: ['ex-new'], planVersion: 2 }]);
    assert.equal(closed.summary.measured.length, 1);
    assert.equal(events.at(-1).type, 'session_summary');
    const analytics = await t.dispatch('get_patient_analytics', {}) as any;
    assert.equal(analytics.measured_progress.first_median_peak_deg, 71);
    assert.equal(analytics.measured_progress.latest_median_peak_deg, 83);
  } finally { server.close(); }
});

test('a plan review is filed against the current plan version and the plan itself is untouched', async () => {
  const replies: any[] = [];
  const { server, url } = await fakeCoordinator([], replies);
  try {
    const { service, writes } = memoryPatients({ id: 'patient-1', name: 'Sam', goals: [], precautions: [] });
    const result = await tools(service, url).dispatch('request_plan_review', { reason: 'Shoulder aches after 5 reps', category: 'pain' }) as any;
    assert.equal(result.status, 'sent_to_physician');
    assert.deepEqual(writes.reviews![0], ['patient-1', 'session-1', 2, 'Shoulder aches after 5 reps', 'pain']);
    // It also reaches the clinician portal, where the care team reads the patient's replies.
    assert.deepEqual(replies, [{ kind: 'hurt', text: 'Review requested by Alex: Shoulder aches after 5 reps', planVersion: 2 }]);
  } finally { server.close(); }
});

test('with the coordinator down the agent is told not to state numbers', async () => {
  const { service } = memoryPatients({ id: 'patient-1', name: 'Sam', goals: [], precautions: [] });
  const t = tools(service, 'http://127.0.0.1:9');
  assert.match((await t.dispatch('get_exercise_results', {}) as any).unavailable, /Do not state rep counts/);
  assert.ok((await t.dispatch('get_patient_profile', {}) as any).care_plan.unavailable);
  await assert.rejects(t.dispatch('log_pain_level', { level: 14 }), /0–10/);
});

test('update_patient_info writes only the fields it declares, with the types it declares', async () => {
  let written: Record<string, unknown> | null = null;
  const patients = { upsertPatient: async (_id: string, fields: Record<string, unknown>) => { written = fields; return { id: 'patient-1', ...fields }; } } as unknown as PatientService;
  const t = tools(patients, 'http://127.0.0.1:9');
  await t.dispatch('update_patient_info', { name: '  Marcus ', age: 34, goals: ['golf', 7, ''], id: 'someone-else', created_at: 'x', clinician_notes: 'overwritten' });
  assert.deepEqual(written, { name: 'Marcus', age: 34, goals: ['golf'] });
  written = null;
  const none = await t.dispatch('update_patient_info', { age: 'old', role: 'admin' }) as { status: string };
  assert.equal(none.status, 'unchanged'); assert.equal(written, null);
});
