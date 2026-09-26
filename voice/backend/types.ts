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
}

export interface ExerciseLog {
  id: string;
  session_id: string;
  patient_id: string;
  exercise_name: string;
  sets?: number;
  reps?: number;
  duration_sec?: number;
  pain_during?: number;
  notes?: string;
  logged_at?: string;
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

export interface SessionSummary {
  exercises: ExerciseLog[];
  exercise_count: number;
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
  exercise_progression: Record<
    string,
    {
      sessions: number;
      volume_change_pct: number;
      latest_sets?: number;
      latest_reps?: number;
    }
  >;
  session_frequency_per_week: number | null;
  streak_days: number;
  total_sessions: number;
  total_milestones: number;
}

/** Sends a structured event to the connected Unity client. */
export type EmitEvent = (event: Record<string, unknown>) => void;

/** Parameters accepted by tool handlers. */
export type ToolParams = Record<string, unknown>;
