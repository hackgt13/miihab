export type PatientStatus = 'alert' | 'watch' | 'good'
export type EventSource = 'engine' | 'coach' | 'patient'
export type EventKind = 'speak' | 'tool_call' | 'cue_template' | 'llm_reply' | 'measurement' | 'report'
export type AuditAction = 'note_signed' | 'plan_approved' | 'reply_flagged' | 'resource_drafted' | 'session_reviewed'

export interface Patient {
  id: string
  name: string
  condition: string
  status: PatientStatus
  urgency: number            // 0 = most urgent, used for sidebar sort
  flagDetail: string         // one-line reason shown under name in sidebar
  lastSession: string
  sessionCount: number
  weeksActive: number
  nextScheduled: string
  activePlanVersion: number
  dob: string
  sex: string
  mrn: string
  icd10: string
  referringPhysician: string
  dateOfInjury: string
  photoUrl?: string
}

export interface SessionEvent {
  t: number
  source: EventSource
  kind: EventKind
  content: string
  tool?: string
  args?: Record<string, unknown>
  flagged?: boolean
}

export interface RepEvent {
  rep: number
  peakDeg: number
  trunkDeg: number
  valid: boolean
  reason?: string
}

export interface ReplayMoment {
  t: number
  label: string
  repIndex?: number
}

export interface PatientReport {
  t: number
  text: string
  severity: number
}

export interface SessionSummary {
  session: number
  date: string
  medianPeakDeg: number
  trunkMeanDeg: number
  validReps: number
  prescribedReps: number
  completed: boolean
  synthetic?: boolean
}

export interface SessionHandoff {
  sessionId: string
  patientId: string
  planVersion: number
  exercise: string
  reps: { valid: number; attempted: number; prescribed: number }
  medianPeakDeg: number
  baselineMedianPeakDeg: number
  trunkDeviation: { meanDeg: number; finalRepsDeg: number }
  patientReports: PatientReport[]
  replayMoments: ReplayMoment[]
  uncertainty: string[]
  repEvents: RepEvent[]
  coachLog: SessionEvent[]
  durationSec: number
  timestamp: string
  synthetic?: boolean
}

export interface TriggerCondition {
  description: string
  threshold: string
  observed: string
  met: boolean
}

export interface Trigger {
  fired: boolean
  rule: string
  summary: string
  conditions: TriggerCondition[]
  values: Record<string, string>
}

export interface PlanSetting {
  setting: string
  current: number | string
  proposed: number | string
  unit?: string
  min?: number
  max?: number
  changed: boolean
}

export interface PlanVersion {
  version: number
  date: string
  exercise: string
  settings: PlanSetting[]
  approvedBy?: string
  approvedAt?: string
  notes?: string
}

export interface SOAPNote {
  id: string
  sessionId: string
  date: string
  patientId: string
  subjective: string
  objective: string
  assessment: string
  plan: string
  signedBy?: string
  signedAt?: string
  draft: boolean
}

export interface AuditEntry {
  id: string
  timestamp: string
  patientId: string
  action: AuditAction
  detail: string
  actor: string
}

export interface RTMRecord {
  patientId: string
  month: string          // YYYY-MM
  daysWithData: number
  daysScheduled: number
  reviewMinutes: number
  sessionCount: number
}

/**
 * What the patient relayed at the end of a visit - the answer to "is there anything you want me to know?",
 * stored by coordinator/visit.ts. A quick reply carries fixed text; `message` carries the patient's own.
 */
export interface VisitReply {
  id: string
  kind: 'fine' | 'easy' | 'hard' | 'hurt' | 'message'
  text: string
  at: string
  planVersion: number | null
}

export interface PatientData {
  patient: Patient
  sessions: SessionSummary[]
  latestHandoff: SessionHandoff
  schedule: { date: string; completed: boolean }[]
  plans: PlanVersion[]
  rtm: RTMRecord
  /** Live patients only: seeded ones have never been to a visit. Newest first. */
  replies?: VisitReply[]
}
