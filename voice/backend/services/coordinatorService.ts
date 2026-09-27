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

  /** Put something in front of the care team in the clinician portal ("From the patient"): the same record a
   * patient's reply at the end of a therapist visit makes. */
  async tellCareTeam(kind: 'hurt' | 'hard' | 'easy' | 'message', text: string, planVersion: number | null): Promise<void> {
    const resp = await fetch(this.baseUrl + '/api/visit/replies', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: AbortSignal.timeout(2000),
      body: JSON.stringify({ kind, text: text.slice(0, 280), planVersion }),
    });
    if (!resp.ok) throw new Error(`Coordinator /api/visit/replies returned ${resp.status}`);
  }

  /** One event for the clinician's coach feed (coordinator/coach-log.ts). Never blocks or fails the conversation. */
  coachEvent(event: { conversation: string; source: 'coach' | 'patient' | 'engine'; kind: string; content: string; tool?: string }): void {
    fetch(this.baseUrl + '/api/coach/events', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: AbortSignal.timeout(2000),
      body: JSON.stringify(event),
    }).catch(() => {});
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
