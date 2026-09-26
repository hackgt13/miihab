// The live patient, read from the RehabMii coordinator.
//
// Everything else in src/data is seed: four invented patients with invented sessions, which is what this
// portal was built against. This module is the one that is real. It reads the records the coordinator
// actually holds — the plan the clinician approved, the exercise sessions the headset recorded, the
// consistency calendar the board draws — and maps them into the same `PatientData` shape the seed
// produces, so every component downstream keeps working without knowing where its numbers came from.
//
// Requests go through the `/api` proxy in vite.config.ts. The coordinator sends no CORS headers (it only
// rejects unknown origins), so a cross-origin fetch from :5173 would be blocked even though :5173 is on
// its allowlist. Same-origin through the dev proxy avoids the question; a built portal should be served
// by the coordinator itself, the way coordinator/portal already is.
//
// The shapes below mirror the coordinator's own JSON rather than re-describing it, the same way Unity's
// MenuDashboardModel mirrors /api/dashboard. When the coordinator's shape changes, this file changes with
// it and the mapping underneath is the only thing that has to be re-read.
//
// What is NOT here, because the coordinator does not hold it, and must not be faked into looking real:
//   · patient-reported symptoms   — nothing asks the patient how it felt, so `patientReports` is empty
//                                   and the trigger rule's third condition can never be met on live data
//   · the coach's event log       — no endpoint records what the coach said, so `coachLog` is empty
//   · scheduling                  — the program is a daily home program; there is no appointment book
//   · more than one patient       — the coordinator is one machine, one patient, `patientId: null`
import type {
  PatientData, PatientStatus, PlanVersion, RepEvent, ReplayMoment,
  SessionHandoff, SessionSummary,
} from './types'

/** The one real patient. Seeded patients keep their own ids, so the two can stand side by side. */
export const LIVE_PATIENT_ID = 'live'

// ── The coordinator's own records ─────────────────────────────────────────
// Only the fields this portal reads. `summary` is coordinator/exercise/kind.ts, `plan` is
// coordinator/plans.ts (schema kinesthetic.plan.v2), `dashboard` is coordinator/dashboard.ts.

interface CoordinatorRep {
  rep: number
  peakDeg: number
  compensationMaxDeg: number
  valid: boolean
  reason: string | null
  startMs: number
  durationMs: number
}

interface CoordinatorSummary {
  exerciseId: string
  prescriptionId?: string
  endedAt: string
  exerciseKind?: string
  side?: string
  planVersion?: number
  simulated?: boolean
  calibrated?: boolean
  seeded?: boolean
  attempted: number
  valid: number
  prescribed: number
  completed: boolean
  medianValidPeakDeg: number | null
  invalidReasons?: Record<string, number>
  trunkDeviation?: { meanDeg: number | null; maxDuringRepsDeg: number | null }
  trackingLossEvents?: number
  validFrameRatio?: number
  reps?: CoordinatorRep[]
  params?: Record<string, number | string | undefined>
}

interface CoordinatorActivity {
  activitySessionId: string
  activityId: string
  endedAt: string
  durationMs: number
}

interface CoordinatorPlanActivity {
  id: string
  activityId: string
  exerciseKind?: string | null
  targetCount: number
  order?: number
  note?: string
  params: Record<string, number | string | undefined>
}

interface CoordinatorPlan {
  version: number
  approvedAt: string
  approvedBy?: string
  rationale?: string
  goal?: { text?: string }
  coachingNote?: string
  activities: CoordinatorPlanActivity[]
}

interface CoordinatorDashboard {
  goal: string
  measured: boolean
  streakDays: number
  programDay: number
  programTotalDays: number
  today: { activityId: string; title: string; detail: string; done: boolean }[]
  calendar: { date: string; level: number }[]
}

// The coordinator's vocabulary for a movement, kept in step with coordinator/exercises.ts. Two kinds
// measure the same movement with different sensors and share a name; only one is ever prescribed.
const MOVEMENTS: Record<string, string> = {
  'arm-elevation.v1': 'Seated shoulder raise',
  'shoulder-raise.v1': 'Seated shoulder raise',
  'elbow-flexion.v1': 'Seated biceps curl',
  'trunk-rotation.v1': 'Seated trunk rotation',
}

const movementName = (kind?: string | null, side?: string): string => {
  const name = kind ? MOVEMENTS[kind] ?? kind : 'Activity'
  return side ? `${name} (${side === 'left' ? 'L' : 'R'})` : name
}

const number = (value: unknown, fallback = 0): number =>
  typeof value === 'number' && Number.isFinite(value) ? value : fallback

/** One decimal, because that is the precision the measurement has. /api/sessions hands over raw floats. */
const tenth = (value: unknown, fallback = 0): number => Math.round(number(value, fallback) * 10) / 10

async function get<T>(path: string): Promise<T> {
  const response = await fetch(path, { headers: { Accept: 'application/json' } })
  if (!response.ok) throw new Error(`${path} answered ${response.status}`)
  return (await response.json()) as T
}

// ── The patient's own work ────────────────────────────────────────────────

/**
 * A session counts as the patient's own work under the same test the coordinator's dashboard applies:
 * not simulated, calibrated, something attempted, and a measurement that came out of it. Anything else
 * is a setup failure or a rehearsal and does not belong in a clinician's trend line.
 */
const isReal = (s: CoordinatorSummary): boolean =>
  !s.simulated && s.calibrated !== false && s.attempted > 0 && s.medianValidPeakDeg != null

const repEvents = (summary: CoordinatorSummary): RepEvent[] =>
  (summary.reps ?? []).map(r => ({
    rep: r.rep,
    peakDeg: r.peakDeg,
    trunkDeg: r.compensationMaxDeg,
    valid: r.valid,
    reason: r.reason ?? undefined,
  }))

/**
 * Where a clinician would scrub to. Not a highlight reel: the first rejected rep, because that is where
 * the session started going wrong, and the best one, because that is what the patient can do. Times are
 * seconds from the first rep, which is what the session's own clock measures.
 */
function replayMoments(summary: CoordinatorSummary): ReplayMoment[] {
  const reps = summary.reps ?? []
  if (reps.length === 0) return []
  const zero = reps[0].startMs
  const at = (r: CoordinatorRep) => Math.max(0, Math.round((r.startMs - zero) / 1000))

  const moments: ReplayMoment[] = []
  const firstBad = reps.find(r => !r.valid)
  if (firstBad) moments.push({ t: at(firstBad), label: `Rep ${firstBad.rep} rejected — ${firstBad.reason ?? 'not counted'}`, repIndex: firstBad.rep })
  const best = reps.filter(r => r.valid).sort((a, b) => b.peakDeg - a.peakDeg)[0]
  if (best) moments.push({ t: at(best), label: `Best rep — ${Math.round(best.peakDeg)}°`, repIndex: best.rep })
  return moments.sort((a, b) => a.t - b.t)
}

/** What the measurement itself is unsure about, in the words of what actually happened. */
function uncertainty(summary: CoordinatorSummary): string[] {
  const notes: string[] = []
  const rejected = Object.entries(summary.invalidReasons ?? {})
  for (const [reason, count] of rejected) notes.push(`${count} rep${count === 1 ? '' : 's'} rejected — ${reason.replace(/_/g, ' ')}`)
  if (number(summary.trackingLossEvents) > 0) notes.push(`${summary.trackingLossEvents} tracking dropout${summary.trackingLossEvents === 1 ? '' : 's'}`)
  const usable = number(summary.validFrameRatio, 1)
  if (usable < 0.95) notes.push(`${Math.round((1 - usable) * 100)}% of frames unusable`)
  // A wrist AirPod cannot see the trunk at all, so its sessions come back with no compensation figure.
  // Zero would read as "sat perfectly still", which is a measurement nobody made.
  if (summary.trunkDeviation?.meanDeg == null) notes.push('Trunk deviation not measured by this sensor')
  if (summary.seeded) notes.push('Seeded demo session (scripts/seed_demo_history.py), not a sensor recording')
  return notes
}

// ── Plans ─────────────────────────────────────────────────────────────────

/**
 * A plan version, with what it changed marked. `current` and `proposed` are both this version's own
 * numbers: PatientView reads plans[0] as the active plan and takes its chart reference lines off
 * `.current`, so `current` has to mean "what this plan asks for" and not "what the last one asked for".
 * `changed` is what carries the diff against the version before it.
 *
 * The setting names are a contract: PatientView reads "Trunk limit" and "Target ROM band" off the active
 * plan to draw the reference lines on its charts.
 */
function planVersion(plan: CoordinatorPlan, previous: CoordinatorPlan | undefined): PlanVersion {
  const measured = plan.activities.find(a => a.exerciseKind) ?? plan.activities[0]
  const was = previous?.activities.find(a => a.id === measured?.id)

  const target = number(measured?.params.targetDeg)
  const ceiling = number(measured?.params.targetMaxDeg)
  const trunk = number(measured?.params.maxCompensationDeg, 8)
  const wasTarget = was ? number(was.params.targetDeg) : target
  const wasCeiling = was ? number(was.params.targetMaxDeg) : ceiling
  const band = (low: number, high: number) => `${Math.round(low)} – ${Math.round(high)}°`

  return {
    version: plan.version,
    date: plan.approvedAt,
    exercise: movementName(measured?.exerciseKind, String(measured?.params.side ?? '')),
    approvedBy: plan.approvedBy,
    approvedAt: plan.approvedAt,
    notes: plan.rationale ?? plan.coachingNote,
    settings: [
      { setting: 'Target ROM band', current: band(target, ceiling), proposed: band(target, ceiling),
        changed: target !== wasTarget || ceiling !== wasCeiling },
      { setting: 'Reps prescribed', current: measured?.targetCount ?? 0, proposed: measured?.targetCount ?? 0,
        unit: 'reps', changed: (was?.targetCount ?? measured?.targetCount) !== measured?.targetCount },
      { setting: 'Trunk limit', current: trunk, proposed: trunk, unit: '°',
        changed: was ? number(was.params.maxCompensationDeg, 8) !== trunk : false },
    ],
  }
}

// ── The patient ───────────────────────────────────────────────────────────

/**
 * A status for the sidebar, from what the data supports and nothing more. The portal's own trigger rule
 * needs a patient-reported symptom to fire and nothing collects one, so this is about turning up and
 * moving: silence for a week is what a clinician wants flagged, and it is the one thing the records can
 * honestly say.
 */
function statusOf(sessions: SessionSummary[], daysSinceLast: number): PatientStatus {
  if (sessions.length === 0) return 'watch'
  if (daysSinceLast >= 7) return 'alert'
  if (daysSinceLast >= 3) return 'watch'
  return 'good'
}

const dayKey = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

function saidWhen(days: number): string {
  if (days <= 0) return 'Today'
  if (days === 1) return 'Yesterday'
  return `${days} days ago`
}

/**
 * Everything the portal shows about the live patient, in one read. Four requests in parallel rather than
 * one endpoint, because these are four different records with four different lifetimes — the plan changes
 * at a visit, the sessions change when the headset finishes one — and the coordinator has no combined
 * view that is anybody's source of truth.
 */
export async function fetchLivePatient(): Promise<PatientData> {
  const [dashboard, summaries, plans, activities, people] = await Promise.all([
    get<CoordinatorDashboard>('/api/dashboard'),
    get<CoordinatorSummary[]>('/api/sessions'),
    get<CoordinatorPlan[]>('/api/plans'),
    get<CoordinatorActivity[]>('/api/activity-sessions'),
    get<{ me?: { displayName?: string } }>('/api/friends').catch(() => ({ me: undefined })),
  ])

  const real = summaries.filter(isReal).sort((a, b) => a.endedAt.localeCompare(b.endedAt))
  const latest = real[real.length - 1]
  const today = new Date()

  const sessions: SessionSummary[] = real.map((s, i) => ({
    session: i + 1,
    date: s.endedAt,
    medianPeakDeg: tenth(s.medianValidPeakDeg),
    trunkMeanDeg: tenth(s.trunkDeviation?.meanDeg),
    validReps: s.valid,
    prescribedReps: s.prescribed,
    completed: s.completed,
    synthetic: Boolean(s.seeded || s.simulated),
  }))

  const envelope = activities.find(a => a.activitySessionId === latest?.exerciseId)
  const reps = latest ? repEvents(latest) : []
  // The last third of a session is where fatigue shows, which is the number the trigger rule reads.
  const finalReps = reps.slice(-Math.max(1, Math.ceil(reps.length / 3)))

  const handoff: SessionHandoff = {
    sessionId: latest?.exerciseId ?? 'none',
    patientId: LIVE_PATIENT_ID,
    planVersion: latest?.planVersion ?? plans[plans.length - 1]?.version ?? 1,
    exercise: movementName(latest?.exerciseKind, latest?.side),
    reps: { valid: latest?.valid ?? 0, attempted: latest?.attempted ?? 0, prescribed: latest?.prescribed ?? 0 },
    medianPeakDeg: tenth(latest?.medianValidPeakDeg),
    baselineMedianPeakDeg: sessions[0]?.medianPeakDeg ?? 0,
    trunkDeviation: {
      meanDeg: tenth(latest?.trunkDeviation?.meanDeg),
      finalRepsDeg: finalReps.length
        ? Math.round((finalReps.reduce((sum, r) => sum + r.trunkDeg, 0) / finalReps.length) * 10) / 10
        : 0,
    },
    patientReports: [],          // nothing asks the patient how it felt — see the note at the top
    replayMoments: latest ? replayMoments(latest) : [],
    uncertainty: latest ? uncertainty(latest) : ['No measured session yet'],
    repEvents: reps,
    coachLog: [],                // no endpoint records what the coach said
    durationSec: Math.round(number(envelope?.durationMs) / 1000),
    timestamp: latest?.endedAt ?? new Date().toISOString(),
    synthetic: Boolean(latest?.seeded || latest?.simulated),
  }

  // The calendar the patient's own board draws, as the portal's adherence strip: three weeks is what
  // fits, and a day counts as done when a session ended on it.
  const schedule = dashboard.calendar.slice(-21).map(day => ({ date: day.date, completed: day.level > 0 }))

  const month = dayKey(today).slice(0, 7)
  const thisMonth = dashboard.calendar.filter(day => day.date.startsWith(month))
  const daysSinceLast = latest
    ? Math.round((today.getTime() - new Date(latest.endedAt).getTime()) / 86_400_000)
    : 99

  return {
    patient: {
      id: LIVE_PATIENT_ID,
      // "You" is what friends.ts calls the patient until they set a name on the headset — a placeholder,
      // not a name, and a clinician's sidebar is the wrong place to render it.
      name: (people.me?.displayName ?? '').trim().replace(/^You$/, '') || 'This headset',
      condition: dashboard.goal || plans[plans.length - 1]?.goal?.text || 'Home rehab program',
      status: statusOf(sessions, daysSinceLast),
      urgency: -1,               // the live patient sorts above the seeded ones
      flagDetail: `Day ${dashboard.programDay} of ${dashboard.programTotalDays} · ${sessions.length} session${sessions.length === 1 ? '' : 's'}`,
      lastSession: latest ? saidWhen(daysSinceLast) : 'None yet',
      sessionCount: sessions.length,
      weeksActive: Math.max(1, Math.ceil(dashboard.programDay / 7)),
      // A daily home program has no appointment book: the next session is today while today still has
      // work outstanding on it, and tomorrow once it does not.
      nextScheduled: dayKey(dashboard.today.some(t => !t.done) ? today : new Date(today.getTime() + 86_400_000)),
      activePlanVersion: plans[plans.length - 1]?.version ?? 1,
    },
    sessions,
    latestHandoff: handoff,
    schedule,
    plans: plans.map((plan, i) => planVersion(plan, plans[i - 1])).reverse(),   // newest first
    rtm: {
      patientId: LIVE_PATIENT_ID,
      month,
      daysWithData: thisMonth.filter(day => day.level > 0).length,
      daysScheduled: thisMonth.length,
      reviewMinutes: 0,          // the portal's own timer owns this; the coordinator never sees it
      sessionCount: sessions.length,
    },
  }
}
