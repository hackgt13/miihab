import type { SessionSummary, SessionHandoff } from './types'

// ── Evaluated types (output of rule engine) ───────────────────────────────

export interface EvaluatedCondition {
  id: string
  label: string
  threshold: string
  observed: string
  displayLine: string   // full formatted line with ✓/✗
  met: boolean
}

export interface EvaluatedTrigger {
  fired: boolean
  ruleText: string
  summary: string
  conditions: EvaluatedCondition[]
}

// ── Rule definition ────────────────────────────────────────────────────────
//
//  This is the single source of truth shared by the engine (evaluation)
//  and the UI (rendering). Changing a threshold here changes it everywhere.

const PEAK_GAIN_THRESHOLD  = 5   // degrees, over last 4 sessions
const TRUNK_FINAL_LIMIT    = 8   // degrees, final reps
const STIFFNESS_THRESHOLD  = 4   // out of 10

export const RULE_TEXT =
  `Δ median peak ≥ ${PEAK_GAIN_THRESHOLD}° over last 4 sessions` +
  ` AND trunk deviation, final reps > ${TRUNK_FINAL_LIMIT}°` +
  ` AND patient-reported stiffness ≥ ${STIFFNESS_THRESHOLD}/10`

// ── Evaluator ─────────────────────────────────────────────────────────────

export function evaluateTrigger(
  sessions: SessionSummary[],
  handoff: SessionHandoff,
): EvaluatedTrigger {
  const sorted = [...sessions].sort((a, b) => a.session - b.session)
  const last4  = sorted.slice(-4)

  // Condition 1 — Median peak gain ≥ PEAK_GAIN_THRESHOLD° over last 4 sessions
  const firstPeak  = last4[0]?.medianPeakDeg ?? 0
  const latestPeak = last4[last4.length - 1]?.medianPeakDeg ?? 0
  const peakGain   = latestPeak - firstPeak
  const c1Met      = peakGain >= PEAK_GAIN_THRESHOLD
  const c1: EvaluatedCondition = {
    id: 'peak_gain',
    label: 'Median peak gain over last 4 sessions',
    threshold: `≥ ${PEAK_GAIN_THRESHOLD}°`,
    observed: `+${peakGain}° (${firstPeak}° → ${latestPeak}°)`,
    displayLine:
      `Median peak gain ≥ ${PEAK_GAIN_THRESHOLD}° over last 4 sessions: ` +
      `+${peakGain}° (${firstPeak}° → ${latestPeak}°) ${c1Met ? '✓' : '✗'}`,
    met: c1Met,
  }

  // Condition 2 — Trunk deviation, final reps > TRUNK_FINAL_LIMIT°
  const finalTrunk = handoff.trunkDeviation.finalRepsDeg
  const c2Met      = finalTrunk > TRUNK_FINAL_LIMIT
  const c2: EvaluatedCondition = {
    id: 'trunk_final',
    label: 'Trunk deviation, final reps',
    threshold: `> ${TRUNK_FINAL_LIMIT}°`,
    observed: `${finalTrunk}°`,
    displayLine:
      `Trunk deviation, final reps > ${TRUNK_FINAL_LIMIT}°: ${finalTrunk}° ${c2Met ? '✓' : '✗'}`,
    met: c2Met,
  }

  // Condition 3 — Patient-reported stiffness ≥ STIFFNESS_THRESHOLD/10
  const stiffReport = handoff.patientReports
    .filter(r => r.text.toLowerCase().includes('stiff'))
    .sort((a, b) => b.severity - a.severity)[0]
  const stiffSev = stiffReport?.severity ?? 0
  const c3Met    = stiffSev >= STIFFNESS_THRESHOLD
  const c3: EvaluatedCondition = {
    id: 'stiffness',
    label: 'Patient-reported stiffness',
    threshold: `≥ ${STIFFNESS_THRESHOLD}/10`,
    observed: stiffReport ? `${stiffSev}/10` : 'not reported',
    displayLine: stiffReport
      ? `Patient-reported stiffness ≥ ${STIFFNESS_THRESHOLD}/10: ${stiffSev}/10 ${c3Met ? '✓' : '✗'}`
      : `Patient-reported stiffness ≥ ${STIFFNESS_THRESHOLD}/10: not reported ✗`,
    met: c3Met,
  }

  const conditions = [c1, c2, c3]
  const fired = conditions.every(c => c.met)

  return {
    fired,
    ruleText: RULE_TEXT,
    summary: fired
      ? 'ROM has improved beyond threshold but trunk compensation is rising. Consider progression timing.'
      : 'Monitoring: not all trigger conditions met.',
    conditions,
  }
}

// ── Next-session projection ────────────────────────────────────────────────
// Derives a future date from today based on the median gap between sessions.

export function computeNextSession(sessions: SessionSummary[]): string {
  const sorted = [...sessions].sort((a, b) => a.session - b.session)
  if (sorted.length < 2) return 'Not scheduled'

  const timestamps = sorted.map(s => new Date(s.date).getTime())
  const gaps       = timestamps.slice(1).map((t, i) => t - timestamps[i])
  const sorted_gaps = [...gaps].sort((a, b) => a - b)
  const medGap      = sorted_gaps[Math.floor(sorted_gaps.length / 2)]

  return new Date(Date.now() + medGap).toISOString().slice(0, 10)
}
