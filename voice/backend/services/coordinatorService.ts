/**
 * Read-only view of the clinical record the coordinator owns (coordinator/server.ts on the Mac):
 *   - the physician-approved care plan (immutable versions; only the portal approves a new one)
 *   - measured exercise results (reps, range, compensation) from the pose measurement engine
 *
 * Alex reads these so it can talk about real numbers. It never writes reps and never changes the plan.
 */

import type { CarePlan, MeasuredExercise } from '../types.ts';

export class CoordinatorService {
  private baseUrl: string;

  constructor(baseUrl: string) {
    this.baseUrl = baseUrl.replace(/\/+$/, '');
  }

  async activePlan(): Promise<CarePlan> {
    return this.get<CarePlan>('/api/plans/active');
  }

  /** Measured results, newest first. `since` keeps only exercises that ended at or after it. */
  async results(opts: { since?: string; limit?: number } = {}): Promise<MeasuredExercise[]> {
    const sessions = await this.get<any[]>('/api/sessions');
    return sessions
      .filter(s => !opts.since || String(s.endedAt) >= opts.since)
      .slice(0, opts.limit ?? 10)
      .map(toMeasured);
  }

  /** The exercise running right now, if any. */
  async live(): Promise<{ running: boolean; phase?: string; attempted?: number; valid?: number; prescribed?: number }> {
    const state = await this.get<any>('/exercise');
    if (!state.running) return { running: false };
    return { running: true, phase: state.phase, attempted: state.summary?.attempted,
      valid: state.summary?.valid, prescribed: state.summary?.prescribed };
  }

  private async get<T>(path: string): Promise<T> {
    const resp = await fetch(this.baseUrl + path, { signal: AbortSignal.timeout(2000) });
    if (!resp.ok) throw new Error(`Coordinator ${path} returned ${resp.status}`);
    return (await resp.json()) as T;
  }
}

function toMeasured(s: any): MeasuredExercise {
  return {
    exercise_id: String(s.exerciseId),
    ended_at: String(s.endedAt),
    exercise: String(s.exercise),
    side: s.side,
    plan_version: s.planVersion ?? null,
    target_deg: s.config?.targetDeg ?? null,
    prescribed: s.prescribed ?? null,
    attempted: s.attempted ?? 0,
    valid: s.valid ?? 0,
    completed: s.completed ?? 0,
    median_peak_deg: s.medianValidPeakDeg == null ? null : Math.round(s.medianValidPeakDeg),
    invalid_reasons: s.invalidReasons ?? {},
    tracking_loss_events: s.trackingLossEvents ?? 0,
    simulated: Boolean(s.simulated),
  };
}
