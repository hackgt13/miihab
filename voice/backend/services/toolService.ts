import type { PatientService } from './patientService.ts';
import type { AnalyticsService } from './analyticsService.ts';
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
    name: 'log_exercise_session',
    description:
      'Log all exercises and close the session record. Call once at the end of the session.',
    parameters: {
      type: 'object',
      properties: {
        exercises: {
          type: 'array',
          description: 'Exercises performed this session',
          items: {
            type: 'object',
            properties: {
              exercise_name: { type: 'string',  description: 'Name of the exercise performed' },
              sets:          { type: 'integer', description: 'Number of sets completed' },
              reps:          { type: 'integer', description: 'Number of reps per set' },
              duration_sec:  { type: 'integer', description: 'Duration in seconds (timed exercises)' },
              pain_during:   { type: 'number',  description: 'Pain level 0–10 during this exercise' },
              notes:         { type: 'string',  description: 'Any notes about this exercise' },
            },
            required: ['exercise_name'],
          },
        },
        session_notes: { type: 'string', description: 'Overall session notes' },
      },
      required: ['exercises'],
    },
  },
  {
    type: 'client',
    name: 'add_milestone',
    description:
      'Record a patient achievement. Call for ROM gains, pain-free movement, ' +
      'attendance streaks, or any functional improvement the patient reports.',
    parameters: {
      type: 'object',
      properties: {
        description: { type: 'string', description: "Clear description, e.g. 'First full squat without knee pain'" },
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
  private patientId: string;
  private sessionId: string;
  private emitEvent: EmitEvent;

  constructor(
    patientService: PatientService,
    analyticsService: AnalyticsService,
    patientId: string,
    sessionId: string,
    emitEvent: EmitEvent,
  ) {
    this.patientService = patientService;
    this.analyticsService = analyticsService;
    this.patientId = patientId;
    this.sessionId = sessionId;
    this.emitEvent = emitEvent;
  }

  async dispatch(toolName: string, params: ToolParams): Promise<unknown> {
    switch (toolName) {
      case 'get_patient_profile':   return this.getPatientProfile();
      case 'get_patient_analytics': return this.getPatientAnalytics();
      case 'update_patient_info':   return this.updatePatientInfo(params);
      case 'log_pain_level':        return this.logPainLevel(params);
      case 'log_exercise_session':  return this.logExerciseSession(params);
      case 'add_milestone':         return this.addMilestone(params);
      default: throw new Error(`Unknown tool: ${toolName}`);
    }
  }

  // ── handlers ─────────────────────────────────────────────────────────────

  private async getPatientProfile() {
    const patient = await this.patientService.getPatient(this.patientId);
    if (!patient) {
      return {
        is_new_patient: true,
        patient_id: this.patientId,
        message: 'No profile found. Ask the patient for their name, condition, and goals.',
      };
    }
    const [recentSessions, recentPain, recentMilestones] = await Promise.all([
      this.patientService.getRecentSessions(this.patientId, 5),
      this.patientService.getRecentPainLogs(this.patientId, 10),
      this.patientService.getRecentMilestones(this.patientId, 5),
    ]);
    return { is_new_patient: false, patient, recent_sessions: recentSessions, recent_pain_logs: recentPain, recent_milestones: recentMilestones };
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
    const context = params['context'] as string | undefined;
    const phase   = params['phase']   as string | undefined;

    const record = await this.patientService.logPain(this.patientId, this.sessionId, level, context, phase);
    const sessionAvg = await this.patientService.getSessionPainAverage(this.sessionId);

    return {
      status: 'logged',
      pain_id: record.id,
      session_pain_avg: sessionAvg,
      recommendation: level >= 7 ? 'rest_and_reassess' : 'continue',
    };
  }

  private async logExerciseSession(params: ToolParams) {
    const exercises    = (params['exercises']     as ToolParams[]) ?? [];
    const sessionNotes = params['session_notes'] as string | undefined;

    const logged = await Promise.all(
      exercises.map(ex =>
        this.patientService.logExercise(
          this.sessionId,
          this.patientId,
          String(ex['exercise_name']),
          {
            sets:         ex['sets']         as number | undefined,
            reps:         ex['reps']         as number | undefined,
            duration_sec: ex['duration_sec'] as number | undefined,
            pain_during:  ex['pain_during']  as number | undefined,
            notes:        ex['notes']        as string | undefined,
          },
        ),
      ),
    );

    await this.patientService.endSession(this.sessionId, sessionNotes);
    const summary = await this.analyticsService.computeSessionSummary(this.sessionId);
    this.emitEvent({ type: 'session_summary', data: summary });

    return { status: 'logged', exercises_logged: logged.length, summary };
  }

  private async addMilestone(params: ToolParams) {
    const description = String(params['description']);
    const category    = String(params['category']);
    const record = await this.patientService.addMilestone(this.patientId, description, category);
    this.emitEvent({ type: 'milestone', data: { description, category } });
    return { status: 'logged', milestone_id: record.id };
  }
}
