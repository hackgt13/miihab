// The live patient, read from the RehabMii coordinator.
//
// Everything else in src/data is seed: four invented patients with invented sessions, which is what this
// portal was built against. This module is the one that is real. It reads the records the coordinator
// actually holds - the plan the clinician approved, the exercise sessions the headset recorded, the
// consistency calendar the board draws - and maps them into the same `PatientData` shape the seed
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
//   · scheduling                  - the program is a daily home program; there is no appointment book
//   · more than one patient       - the coordinator is one machine, one patient, `patientId: null`
import type {
  PatientData, PatientReport, PatientStatus, PlanVersion, RepEvent, ReplayMoment,
  SessionEvent, SessionHandoff, SessionSummary, VisitReply,
} from './types'

/** The one real patient. Seeded patients keep their own ids, so the two can stand side by side. */
export const LIVE_PATIENT_ID = 'live'

// ── The coordinator's own records ─────────────────────────────────────────
// Only the fields this portal reads. `summary` is coordinator/exercise/kind.ts, `plan` is
// coordinator/plans.ts (schema rehabmii.plan.v2), `dashboard` is coordinator/dashboard.ts.

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
  /** Head travel per rep from the headset (coordinator/head-lean.ts): a sign of trunk lean, with approximate angles. */
  headLean?: { available: boolean; repsOverThreshold?: number; repsMeasured?: number; maxCm?: number;
    perRep?: { rep: number; cm: number | null; approxDeg: number | null }[] }
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
  if (firstBad) moments.push({ t: at(firstBad), label: `Rep ${firstBad.rep} rejected - ${firstBad.reason ?? 'not counted'}`, repIndex: firstBad.rep })
  const best = reps.filter(r => r.valid).sort((a, b) => b.peakDeg - a.peakDeg)[0]
  if (best) moments.push({ t: at(best), label: `Best rep - ${Math.round(best.peakDeg)}°`, repIndex: best.rep })
  return moments.sort((a, b) => a.t - b.t)
}

/**
 * The patient's own word, from the question every visit ends on ("is there anything you want me to know?").
 * It is a quick reply rather than a scale, so the severity here is this portal reading the coordinator's
 * four kinds onto its own 0–10 - hurt and too-hard are what a clinician needs to see, and the rest are
 * context. A free-text message sits between them: it says something, and only the words say what.
 *
 * Replies are about the program, not about one session, so the last fortnight of them ride along with the
 * latest session rather than being pinned to a moment inside it - which is why `t` is 0 and the portal
 * says "reported at the visit" instead of a timestamp inside the recording.
 */
const SEVERITY: Record<VisitReply['kind'], number> = { hurt: 8, hard: 6, message: 5, easy: 2, fine: 0 }

function patientReports(replies: VisitReply[], now: Date): PatientReport[] {
  const since = now.getTime() - 14 * 86_400_000
  return replies
    .filter(r => Date.parse(r.at) >= since)
    .map(r => ({ t: 0, text: r.text, severity: SEVERITY[r.kind] ?? 5 }))
}

/** What the measurement itself is unsure about, in the words of what actually happened. */
/** Trunk lean from the headset when no camera measured the trunk: the mean, and the mean over the last third of reps. */
function headTrunk(summary: CoordinatorSummary | undefined): { meanDeg: number; finalDeg: number } | null {
  const degs = (summary?.headLean?.available ? summary.headLean.perRep ?? [] : [])
    .map(r => r.approxDeg).filter((d): d is number => d != null)
  if (!degs.length) return null
  const mean = (v: number[]) => Math.round((v.reduce((a, b) => a + b, 0) / v.length) * 10) / 10
  return { meanDeg: mean(degs), finalDeg: mean(degs.slice(-Math.max(1, Math.ceil(degs.length / 3)))) }
}

interface CoachEventRecord { at: string; source: SessionEvent['source']; kind: SessionEvent['kind']; content: string; tool?: string }

function uncertainty(summary: CoordinatorSummary): string[] {
  const notes: string[] = []
  const rejected = Object.entries(summary.invalidReasons ?? {})
  for (const [reason, count] of rejected) notes.push(`${count} rep${count === 1 ? '' : 's'} rejected - ${reason.replace(/_/g, ' ')}`)
  if (number(summary.trackingLossEvents) > 0) notes.push(`${summary.trackingLossEvents} tracking dropout${summary.trackingLossEvents === 1 ? '' : 's'}`)
  const usable = number(summary.validFrameRatio, 1)
  if (usable < 0.95) notes.push(`${Math.round((1 - usable) * 100)}% of frames unusable`)
  // A wrist AirPod cannot see the trunk at all, so its sessions come back with no compensation figure.
  // Zero would read as "sat perfectly still", which is a measurement nobody made.
  if (summary.trunkDeviation?.meanDeg == null)
    notes.push(headTrunk(summary) ? 'Trunk lean estimated from the headset (head travel per rep), not measured at the trunk'
      : 'Trunk deviation not measured by this sensor')
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

/** How recent a patient's word still counts as news a physician has not acted on. */
const RELAY_WINDOW_DAYS = 7

/** The replies a physician is meant to see rather than skim past, newest first. */
export const concerning = (replies: VisitReply[] = [], now = new Date()): VisitReply[] =>
  replies.filter(r => (r.kind === 'hurt' || r.kind === 'hard' || r.kind === 'message')
    && Date.parse(r.at) >= now.getTime() - RELAY_WINDOW_DAYS * 86_400_000)

/**
 * A status for the sidebar, from what the data supports and nothing more.
 *
 * Two things a physician wants flagged, in order. Pain first: the patient said something hurt, which is the
 * only thing on this page they said in their own words and the only one that can mean stop. Then silence -
 * a week without a session is the other thing the records can honestly say.
 */
function statusOf(sessions: SessionSummary[], daysSinceLast: number, replies: VisitReply[]): PatientStatus {
  const said = concerning(replies)
  if (said.some(r => r.kind === 'hurt')) return 'alert'
  if (sessions.length === 0) return 'watch'
  if (daysSinceLast >= 7) return 'alert'
  if (said.length > 0 || daysSinceLast >= 3) return 'watch'
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
 * one endpoint, because these are four different records with four different lifetimes - the plan changes
 * at a visit, the sessions change when the headset finishes one - and the coordinator has no combined
 * view that is anybody's source of truth.
 */
export async function fetchLivePatient(): Promise<PatientData> {
  const [dashboard, summaries, plans, activities, people, replies] = await Promise.all([
    get<CoordinatorDashboard>('/api/dashboard'),
    get<CoordinatorSummary[]>('/api/sessions'),
    get<CoordinatorPlan[]>('/api/plans'),
    get<CoordinatorActivity[]>('/api/activity-sessions'),
    get<{ me?: { displayName?: string } }>('/api/friends').catch(() => ({ me: undefined })),
    get<VisitReply[]>('/api/visit/replies').catch(() => [] as VisitReply[]),
  ])
  // What Alex said in the latest conversation, and the studio reports he coached from (coordinator/coach-log.ts).
  const coachEvents = await get<CoachEventRecord[]>('/api/coach/events').catch(() => [] as CoachEventRecord[])
  const coachStart = coachEvents.length ? Date.parse(coachEvents[0].at) : 0

  const real = summaries.filter(isReal).sort((a, b) => a.endedAt.localeCompare(b.endedAt))
  const latest = real[real.length - 1]
  const today = new Date()

  const sessions: SessionSummary[] = real.map((s, i) => ({
    session: i + 1,
    date: s.endedAt,
    medianPeakDeg: tenth(s.medianValidPeakDeg),
    trunkMeanDeg: tenth(s.trunkDeviation?.meanDeg ?? headTrunk(s)?.meanDeg),
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
    trunkDeviation: latest?.trunkDeviation?.meanDeg == null && headTrunk(latest) ? {
      meanDeg: headTrunk(latest)!.meanDeg, finalRepsDeg: headTrunk(latest)!.finalDeg,
    } : {
      meanDeg: tenth(latest?.trunkDeviation?.meanDeg),
      finalRepsDeg: finalReps.length
        ? Math.round((finalReps.reduce((sum, r) => sum + r.trunkDeg, 0) / finalReps.length) * 10) / 10
        : 0,
    },
    patientReports: patientReports(replies, today),
    replayMoments: latest ? replayMoments(latest) : [],
    uncertainty: latest ? uncertainty(latest) : ['No measured session yet'],
    repEvents: reps,
    coachLog: coachEvents.map(e => ({ t: Math.max(0, (Date.parse(e.at) - coachStart) / 1000), source: e.source, kind: e.kind,
      content: e.content, ...(e.tool ? { tool: e.tool } : {}) })),
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
      // "You" is what friends.ts calls the patient until they set a name on the headset - a placeholder,
      // not a name, and a clinician's sidebar is the wrong place to render it.
      name: (people.me?.displayName ?? '').trim().replace(/^You$/, '') || 'This headset',
      condition: dashboard.goal || plans[plans.length - 1]?.goal?.text || 'Home rehab program',
      status: statusOf(sessions, daysSinceLast, replies),
      urgency: -1,               // the live patient sorts above the seeded ones
      // The sidebar line leads with the patient's own word when there is one: a physician scanning the
      // list should not have to open a patient to find out they reported pain.
      flagDetail: [
        concerning(replies)[0] && `“${concerning(replies)[0].text}”`,
        `Day ${dashboard.programDay} of ${dashboard.programTotalDays}`,
        `${sessions.length} session${sessions.length === 1 ? '' : 's'}`,
      ].filter(Boolean).join(' · '),
      lastSession: latest ? saidWhen(daysSinceLast) : 'None yet',
      sessionCount: sessions.length,
      weeksActive: Math.max(1, Math.ceil(dashboard.programDay / 7)),
      // A daily home program has no appointment book: the next session is today while today still has
      // work outstanding on it, and tomorrow once it does not.
      nextScheduled: dayKey(dashboard.today.some(t => !t.done) ? today : new Date(today.getTime() + 86_400_000)),
      activePlanVersion: plans[plans.length - 1]?.version ?? 1,
      // The coordinator keeps no chart: a headset knows its patient by display name, not by MRN or
      // diagnosis code. The banner says "not on file" for these rather than showing an invented record.
      dob: '',
      sex: '',
      mrn: 'Not on file',
      icd10: 'Not on file',
      referringPhysician: 'Not on file',
      dateOfInjury: 'Not on file',
    },
    sessions,
    latestHandoff: handoff,
    schedule,
    plans: plans.map((plan, i) => planVersion(plan, plans[i - 1])).reverse(),   // newest first
    replies: [...replies].sort((a, b) => b.at.localeCompare(a.at)),
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
