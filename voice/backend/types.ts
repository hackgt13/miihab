export interface Patient {
  id: string;
  name?: string;
  age?: number;
  condition?: string;
  goals: string[];
  precautions: string[];
  created_at?: string;
}

export interface PTSession {
  id: string;
  patient_id: string;
  started_at?: string;
  ended_at?: string;
  pain_start?: number;
  pain_end?: number;
  notes?: string;
  /** Coordinator exercise ids measured during this conversation — the join to the measured record. */
  exercise_ids?: string[];
  plan_version?: number | null;
}

export interface PainLog {
  id: string;
  patient_id: string;
  session_id?: string;
  level: number;
  context?: string;
  phase?: "start" | "during" | "end";
  logged_at?: string;
}

export interface Milestone {
  id: string;
  patient_id: string;
  description: string;
  category: string;
  logged_at?: string;
}

/** The physician-approved plan, owned by the coordinator (coordinator/plans.ts). */
export interface CarePlan {
  version: number;
  approvedBy: string;
  approvedAt: string;
  rationale: string;
  origin: "clinician" | "auto-progression";
  goal: { text: string; components: string[] };
  /**
   * What the patient is prescribed. Measured prescriptions have a band: reps count from params.targetDeg;
   * above params.targetMaxDeg is an overshoot to avoid. Golf is prescribed as play (exerciseKind null).
   */
  activities: {
    id: string;
    activityId: string;                 // e.g. rehab.studio, golf.adaptive
    exerciseKind: string | null;        // e.g. arm-elevation.v1 (AirPod), shoulder-raise.v1 (camera)
    targetCount: number;                // reps, or holes for golf
    params: Record<string, number | string>;
    note: string;
  }[];
  coachingNote: string;
}

/** One measured exercise from the coordinator's measurement engine. Never produced by the LLM. */
export interface MeasuredExercise {
  exercise_id: string;
  ended_at: string;
  exercise: string;
  side: "left" | "right";
  plan_version: number | null;
  target_deg: number | null;
  prescribed: number | null;
  attempted: number;
  valid: number;
  completed: number;
  median_peak_deg: number | null;
  invalid_reasons: Record<string, number>;
  tracking_loss_events: number;
  simulated: boolean;
}

export interface PlanReviewRequest {
  id: string;
  patient_id: string;
  session_id?: string;
  plan_version?: number | null;
  reason: string;
  category: string;
  status?: "open" | "reviewed";
  created_at?: string;
}

export interface SessionSummary {
  measured: MeasuredExercise[];
  pain_avg: number | null;
  pain_delta: number | null;
  start_pain: number | null;
  end_pain: number | null;
}

export interface PainTrend {
  trend: "improving" | "worsening" | "stable" | "insufficient_data";
  slope: number;
  recent_avg: number | null;
  early_avg: number | null;
  data_points: number;
}

export interface PatientAnalytics {
  pain_trend: PainTrend;
  /** From the coordinator's measured results; null when the coordinator is unreachable. */
  measured_progress: {
    sessions: number;
    latest: MeasuredExercise | null;
    first_median_peak_deg: number | null;
    latest_median_peak_deg: number | null;
  } | null;
  session_frequency_per_week: number | null;
  streak_days: number;
  total_sessions: number;
  total_milestones: number;
}

/** Sends a structured event to the connected Unity client. */
export type EmitEvent = (event: Record<string, unknown>) => void;

/** Parameters accepted by tool handlers. */
export type ToolParams = Record<string, unknown>;
