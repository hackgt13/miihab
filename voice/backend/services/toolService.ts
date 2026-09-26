import type { PatientService } from './patientService.ts';
import type { AnalyticsService } from './analyticsService.ts';
import type { CoordinatorService } from './coordinatorService.ts';
import type { EmitEvent, ToolParams } from '../types.ts';

// ── Tool definitions ── registered with the ElevenLabs agent at creation time.
// type "client" means the backend executes them, not ElevenLabs servers.

export const TOOL_DEFINITIONS = [
  {
    type: 'client',
    name: 'get_patient_profile',
    description:
      "Retrieve the current patient's full profile: personal info, condition, goals, " +
      'precautions, and recent session history. Call at the start of every session.',
    parameters: { type: 'object', properties: {}, required: [] },
  },
  {
    type: 'client',
    name: 'get_patient_analytics',
    description:
      'Get progress analytics: pain trend, exercise progression, session frequency, ' +
      'streak, and milestone count. Call after get_patient_profile at session start.',
    parameters: { type: 'object', properties: {}, required: [] },
  },
  {
    type: 'client',
    name: 'update_patient_info',
    description:
      "Update the patient's profile whenever they share personal info, a new goal, " +
      'a change in condition, or a medical precaution.',
    parameters: {
      type: 'object',
      properties: {
        name:        { type: 'string',  description: "Patient's full name" },
        age:         { type: 'integer', description: "Patient's age in years" },
        condition:   { type: 'string',  description: 'Primary diagnosis or condition being treated' },
        goals:       { type: 'array', items: { type: 'string', description: 'A rehabilitation goal' }, description: 'Rehabilitation goals' },
        precautions: { type: 'array', items: { type: 'string', description: 'A medical precaution or contraindication' }, description: 'Medical precautions or contraindications' },
      },
      required: [],
    },
  },
  {
    type: 'client',
    name: 'log_pain_level',
    description:
      "Log a pain level (0-10). Call at session start (phase='start'), " +
      "after exercises (phase='during'), and at session end (phase='end').",
    parameters: {
      type: 'object',
      properties: {
        level:   { type: 'number', description: 'Pain level 0–10' },
        context: { type: 'string', description: "What was happening, e.g. 'knee flexion at 90 degrees'" },
        phase:   { type: 'string', enum: ['start', 'during', 'end'], description: 'Session phase' },
      },
      required: ['level'],
    },
  },
  {
    type: 'client',
    name: 'get_exercise_results',
    description:
      'Get what the camera measured this session: reps attempted and valid against the plan, ' +
      'range reached, and why any reps did not count (e.g. leaning the trunk). Also says whether ' +
      'an exercise is running right now. These are the only rep counts to use — never estimate reps yourself.',
    parameters: { type: 'object', properties: {}, required: [] },
  },
  {
    type: 'client',
    name: 'request_plan_review',
    description:
      "Ask the patient's physician to review the care plan. You cannot change the plan (reps, range, " +
      'hold, side) yourself. Use this when pain, fatigue, or ease suggests the plan may need changing.',
    parameters: {
      type: 'object',
      properties: {
        reason:   { type: 'string', description: "What you observed, in the patient's words where possible" },
        category: { type: 'string', enum: ['pain', 'too_hard', 'too_easy', 'fatigue', 'other'], description: 'Why a review is needed' },
      },
      required: ['reason', 'category'],
    },
  },
  {
    type: 'client',
    name: 'close_session',
    description:
      'Close the session at the end: links the measured exercises to this conversation and returns the summary. ' +
      'Call once, after the end-of-session pain check.',
    parameters: {
      type: 'object',
      properties: {
        session_notes: { type: 'string', description: 'Brief notes: how the patient felt, anything they reported' },
      },
      required: [],
    },
  },
  {
    type: 'client',
    name: 'add_milestone',
    description:
      'Record a patient achievement. Call for measured range gains, pain-free sets, ' +
      'attendance streaks, or a functional win the patient reports (e.g. reaching a shelf from the chair).',
    parameters: {
      type: 'object',
      properties: {
        description: { type: 'string', description: "Clear description, e.g. 'Reached the target height on every rep without leaning'" },
        category: {
          type: 'string',
          enum: ['range_of_motion', 'strength', 'endurance', 'pain_reduction', 'functional', 'adherence', 'other'],
          description: 'Milestone category',
        },
      },
      required: ['description', 'category'],
    },
  },
] as const;

// ── ToolService ── dispatches tool calls from the ElevenLabs agent ────────────

export class ToolService {
  private patientService: PatientService;
  private analyticsService: AnalyticsService;
  private coordinator: CoordinatorService;
  private patientId: string;
  private sessionId: string;
  private sessionStartedAt: string;
  private emitEvent: EmitEvent;

  constructor(
    patientService: PatientService,
    analyticsService: AnalyticsService,
    coordinator: CoordinatorService,
    patientId: string,
    session: { id: string; startedAt: string },
    emitEvent: EmitEvent,
  ) {
    this.patientService = patientService;
    this.analyticsService = analyticsService;
    this.coordinator = coordinator;
    this.patientId = patientId;
    this.sessionId = session.id;
    this.sessionStartedAt = session.startedAt;
    this.emitEvent = emitEvent;
  }

  async dispatch(toolName: string, params: ToolParams): Promise<unknown> {
    switch (toolName) {
      case 'get_patient_profile':   return this.getPatientProfile();
      case 'get_patient_analytics': return this.getPatientAnalytics();
      case 'update_patient_info':   return this.updatePatientInfo(params);
      case 'log_pain_level':        return this.logPainLevel(params);
      case 'get_exercise_results':  return this.getExerciseResults();
      case 'request_plan_review':   return this.requestPlanReview(params);
      case 'close_session':         return this.closeSession(params);
      case 'add_milestone':         return this.addMilestone(params);
      default: throw new Error(`Unknown tool: ${toolName}`);
    }
  }

  // ── handlers ─────────────────────────────────────────────────────────────

  private async carePlan() {
    return this.coordinator.activePlan().catch(() => null);
  }

  private async getPatientProfile() {
    const [patient, carePlan] = await Promise.all([
      this.patientService.getPatient(this.patientId),
      this.carePlan(),
    ]);
    const care_plan = carePlan ?? { unavailable: 'Plan service unreachable. Do not guess the prescription; keep to gentle check-ins.' };
    // createSession inserts an empty row so sessions can reference it: no name yet means a first visit.
    if (!patient?.name) {
      return {
        is_new_patient: true,
        patient_id: this.patientId,
        care_plan,
        message: 'No profile yet. Ask the patient for their name, condition, and goals.',
      };
    }
    const [recentSessions, recentPain, recentMilestones] = await Promise.all([
      this.patientService.getRecentSessions(this.patientId, 5),
      this.patientService.getRecentPainLogs(this.patientId, 10),
      this.patientService.getRecentMilestones(this.patientId, 5),
    ]);
    return { is_new_patient: false, patient, care_plan, recent_sessions: recentSessions, recent_pain_logs: recentPain, recent_milestones: recentMilestones };
  }

  private async getPatientAnalytics() {
    return this.analyticsService.getPatientAnalytics(this.patientId);
  }

  private async updatePatientInfo(params: ToolParams) {
    const updated = await this.patientService.upsertPatient(this.patientId, params as any);
    return { status: 'updated', patient: updated };
  }

  private async logPainLevel(params: ToolParams) {
    const level = Number(params['level']);
    if (!Number.isFinite(level) || level < 0 || level > 10) throw new Error('Pain level must be 0–10.');
    const context = params['context'] as string | undefined;
    const phase   = params['phase']   as string | undefined;

    const record = await this.patientService.logPain(this.patientId, this.sessionId, level, context, phase);
    const sessionAvg = await this.patientService.getSessionPainAverage(this.sessionId);

    return {
      status: 'logged',
      pain_id: record.id,
      session_pain_avg: sessionAvg,
      recommendation: level >= 8 ? 'stop_and_recommend_doctor' : level >= 7 ? 'stop_and_rest' : 'continue',
    };
  }

  private async getExerciseResults() {
    const [live, measured, plan] = await Promise.all([
      this.coordinator.live().catch(() => null),
      this.coordinator.results({ since: this.sessionStartedAt }).catch(() => null),
      this.carePlan(),
    ]);
    if (!live && !measured) return { unavailable: 'Measurement service unreachable. Do not state rep counts.' };
    return { running_now: live, completed_this_session: measured ?? [], plan: plan?.exercise ?? null };
  }

  private async requestPlanReview(params: ToolParams) {
    const plan = await this.carePlan();
    const reason   = String(params['reason'] ?? '').trim();
    const category = String(params['category'] ?? 'other');
    if (!reason) throw new Error('A reason is required.');
    const record = await this.patientService.addPlanReviewRequest(this.patientId, this.sessionId, plan?.version ?? null, reason, category);
    this.emitEvent({ type: 'plan_review_requested', data: { reason, category, plan_version: plan?.version ?? null } });
    return { status: 'sent_to_physician', request_id: record.id, note: 'The plan is unchanged until the physician approves a new version.' };
  }

  private async closeSession(params: ToolParams) {
    const sessionNotes = params['session_notes'] as string | undefined;
    const [measured, plan] = await Promise.all([
      this.coordinator.results({ since: this.sessionStartedAt }).catch(() => []),
      this.carePlan(),
    ]);
    await this.patientService.endSession(this.sessionId, {
      notes: sessionNotes,
      exerciseIds: measured.map(m => m.exercise_id),
      planVersion: plan?.version ?? null,
    });
    const summary = await this.analyticsService.computeSessionSummary(this.sessionId, this.sessionStartedAt);
    this.emitEvent({ type: 'session_summary', data: summary });
    return { status: 'closed', summary };
  }

  private async addMilestone(params: ToolParams) {
    const description = String(params['description']);
    const category    = String(params['category']);
    const record = await this.patientService.addMilestone(this.patientId, description, category);
    this.emitEvent({ type: 'milestone', data: { description, category } });
    return { status: 'logged', milestone_id: record.id };
  }
}
