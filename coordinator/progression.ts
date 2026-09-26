// Progression: after each session, deterministic rules turn measured reps (and reported pain) into a proposal
// for the next dose of one plan exercise. Inside the clinician's envelope a proposal may apply itself; outside
// it, or when the clinician turned auto-apply off, it waits in the portal. The rules follow criteria-based
// graded progression: consistent in-band sessions move the target up one step; overshooting the safe ceiling
// or a pain flare steps it back. Nothing here is a clinical judgement; every change is logged with its evidence.
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { randomUUID } from 'node:crypto';
import type { Plan, PlanExercise } from './plans.ts';
import { libraryEntry } from './exercises.ts';

export const RULES_VERSION = 'progression.v1';
/** Pain-monitoring model: above this during or after exercise, or a rise of 2+ from the start, is a flare. */
export const PAIN_LIMIT = 5, PAIN_RISE = 2;

export interface SessionEvidence {
  exerciseId: string;          // the measured session
  planExerciseId: string;
  planVersion: number | null;
  endedAt: string;
  targetDeg: number;
  maxSafeDeg: number;
  prescribed: number;
  simulated: boolean;
  calibrated: boolean;         // false: setup or tracking failed; says nothing about the patient
  reps: { peakDeg: number; valid: boolean }[];
}
export interface PainReport { level: number; phase: 'start' | 'during' | 'end' }
export type Decision = 'progress' | 'regress' | 'hold' | 'clinician_review';
export interface Proposal {
  id: string;
  rulesVersion: string;
  createdAt: string;
  planVersion: number;
  exerciseId: string;
  exerciseLabel: string;
  decision: Decision;
  from: { targetDeg: number; maxSafeDeg: number };
  to: { targetDeg: number; maxSafeDeg: number } | null;
  reasons: string[];
  evidence: { exerciseId: string; endedAt: string; inBand: number; prescribed: number; overshoots: number; simulated: boolean }[];
  pain: { flare: boolean; max: number | null; rise: number | null } | null;
  status: 'applied' | 'pending' | 'approved' | 'dismissed' | 'superseded' | 'info';
  appliedPlanVersion: number | null;
}

/** Measured summary (coordinator/measurement.ts) → evidence. Older summaries lack the plan exercise id and ceiling. */
export function evidenceFromSummary(s: any): SessionEvidence {
  const targetDeg = Number(s.config?.targetDeg);
  return {
    exerciseId: String(s.exerciseId), planExerciseId: s.planExerciseId ?? `shoulder-raise-${s.side ?? 'right'}`,
    planVersion: s.planVersion ?? null, endedAt: String(s.endedAt), targetDeg,
    maxSafeDeg: Number(s.config?.maxSafeDeg ?? targetDeg + 15), prescribed: Number(s.prescribed ?? s.config?.prescribedReps ?? 0),
    simulated: Boolean(s.simulated), calibrated: s.calibrated !== false, reps: (s.reps ?? []).map((r: any) => ({ peakDeg: Number(r.peakDeg), valid: Boolean(r.valid) })),
  };
}

export function scoreSession(e: SessionEvidence) {
  const inBand = e.reps.filter(r => r.valid && r.peakDeg <= e.maxSafeDeg).length;
  const overshoots = e.reps.filter(r => r.peakDeg > e.maxSafeDeg).length;
  return { inBand, overshoots, ratio: e.prescribed ? inBand / e.prescribed : 0 };
}

export function painFlare(pain: PainReport[] | undefined) {
  if (!pain?.length) return null;
  const max = Math.max(...pain.map(p => p.level));
  const start = pain.find(p => p.phase === 'start')?.level, end = [...pain].reverse().find(p => p.phase === 'end')?.level;
  const rise = start != null && end != null ? end - start : null;
  return { flare: max > PAIN_LIMIT || (rise != null && rise >= PAIN_RISE), max, rise };
}

/** The oldest plan version from which this exercise has had its current band: the level started then. */
function levelSince(plans: Plan[], exercise: PlanExercise): string {
  let since = plans[plans.length - 1].approvedAt;
  for (let i = plans.length - 1; i >= 0; i--) {
    const e = plans[i].exercises.find(x => x.id === exercise.id);
    if (!e || e.targetDeg !== exercise.targetDeg || e.maxSafeDeg !== exercise.maxSafeDeg) break;
    since = plans[i].approvedAt;
  }
  return since;
}

export function evaluate(plans: Plan[], exerciseId: string, sessions: SessionEvidence[],
                         opts: { includeSimulated?: boolean; pain?: PainReport[]; now?: string } = {}): Proposal {
  const plan = plans[plans.length - 1];
  const x = plan.exercises.find(e => e.id === exerciseId);
  if (!x) throw Error(`No exercise "${exerciseId}" in plan v${plan.version}.`);
  const p = x.progression, since = levelSince(plans, x);
  const atLevel = sessions
    .filter(s => s.planExerciseId === x.id && s.targetDeg === x.targetDeg && s.endedAt >= since && s.calibrated && (opts.includeSimulated || !s.simulated))
    .sort((a, b) => b.endedAt.localeCompare(a.endedAt));
  const scored = atLevel.map(s => ({ s, ...scoreSession(s) }));
  const pain = painFlare(opts.pain);
  const from = { targetDeg: x.targetDeg, maxSafeDeg: x.maxSafeDeg };
  const margin = x.maxSafeDeg - x.targetDeg;
  const band = (t: number) => ({ targetDeg: t, maxSafeDeg: t + margin });
  const reasons: string[] = [];
  let decision: Decision = 'hold', to: Proposal['to'] = null;

  const latest = scored[0];
  if (!latest) reasons.push(`No ${opts.includeSimulated ? '' : 'real '}sessions at ${x.targetDeg}° yet.`);
  else if (latest.overshoots > p.maxOvershoots || pain?.flare) {
    if (latest.overshoots > p.maxOvershoots) reasons.push(`${latest.overshoots} reps went above the ${x.maxSafeDeg}° safe ceiling (limit ${p.maxOvershoots}).`);
    if (pain?.flare) reasons.push(pain.max! > PAIN_LIMIT ? `Pain reached ${pain.max}/10.` : `Pain rose ${pain.rise} points during the session.`);
    const target = Math.max(p.minTargetDeg, x.targetDeg - p.stepDeg);
    if (target < x.targetDeg) { decision = 'regress'; to = band(target); reasons.push(`Step back to ${target}° to let the shoulder settle.`); }
    else { decision = 'clinician_review'; reasons.push(`Already at the envelope floor (${p.minTargetDeg}°); needs the clinician.`); }
  } else {
    const run = scored.slice(0, p.sessionsToProgress);
    const good = (r: typeof scored[number]) => r.ratio >= p.inBandRatio && r.overshoots === 0;
    const streak = run.findIndex(r => !good(r)); const goodCount = streak < 0 ? run.length : streak;
    if (goodCount >= p.sessionsToProgress) {
      reasons.push(`${goodCount} sessions in a row with ≥${Math.round(p.inBandRatio * 100)}% of reps in the ${x.targetDeg}–${x.maxSafeDeg}° band and no overshoots.`);
      const target = x.targetDeg + p.stepDeg;
      if (target <= p.maxTargetDeg) { decision = 'progress'; to = band(target); reasons.push(`Move up to ${target}°.`); }
      else { decision = 'clinician_review'; reasons.push(`Next level (${target}°) is past the envelope (${p.maxTargetDeg}°); needs the clinician.`); }
    } else {
      reasons.push(`${goodCount} of ${p.sessionsToProgress} good sessions at this level.`);
      if (!good(latest)) reasons.push(latest.overshoots
        ? `${latest.overshoots} rep${latest.overshoots > 1 ? 's' : ''} went above ${x.maxSafeDeg}°; stay under the line.`
        : `${latest.inBand}/${latest.s.prescribed} reps in the band; ${Math.ceil(p.inBandRatio * latest.s.prescribed)} needed.`);
    }
  }
  return {
    id: randomUUID(), rulesVersion: RULES_VERSION, createdAt: opts.now ?? new Date().toISOString(), planVersion: plan.version,
    exerciseId: x.id, exerciseLabel: libraryEntry(x.type).label, decision, from, to, reasons,
    evidence: scored.slice(0, Math.max(p.sessionsToProgress, 1)).map(r => ({ exerciseId: r.s.exerciseId, endedAt: r.s.endedAt,
      inBand: r.inBand, prescribed: r.s.prescribed, overshoots: r.overshoots, simulated: r.s.simulated })),
    pain, status: decision === 'hold' ? 'info' : 'pending', appliedPlanVersion: null,
  };
}

export class ProposalStore {
  readonly directory: string;
  constructor(directory: string) { this.directory = directory; mkdirSync(directory, { recursive: true }); }
  save(p: Proposal) { writeFileSync(resolve(this.directory, `${p.id}.json`), JSON.stringify(p, null, 2)); return p; }
  get(id: string): Proposal | null {
    if (!/^[0-9a-f-]{36}$/i.test(id)) return null;
    try { return JSON.parse(readFileSync(resolve(this.directory, `${id}.json`), 'utf8')); } catch { return null; }
  }
  list(): Proposal[] {
    return readdirSync(this.directory).filter(f => f.endsWith('.json'))
      .map(f => JSON.parse(readFileSync(resolve(this.directory, f), 'utf8')) as Proposal)
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  }
  /** A newer proposal for the same exercise replaces an older one still waiting. */
  supersede(exerciseId: string, exceptId: string) {
    for (const p of this.list()) if (p.exerciseId === exerciseId && p.status === 'pending' && p.id !== exceptId) this.save({ ...p, status: 'superseded' });
  }
}
