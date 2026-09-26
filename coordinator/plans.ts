// Versioned, immutable care plans. Approving a change writes a new version; earlier versions never change.
// A session pins the version it started with, so a mid-session approval cannot alter it.
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

export interface ExercisePlan {
  type: 'seated_shoulder_raise';
  side: 'left' | 'right';
  targetDeg: number;
  prescribedReps: number;
  holdMs: number;
  maxTrunkDeviationDeg: number;
}
export interface Plan {
  schema: 'kinesthetic.plan.v1';
  version: number;
  approvedBy: string;
  approvedAt: string;
  rationale: string;
  basedOnExerciseIds: string[];
  exercise: ExercisePlan;
  coachingNote: string;
}

const LIMITS = { targetDeg: [35, 160], prescribedReps: [1, 30], holdMs: [0, 3000], maxTrunkDeviationDeg: [3, 45] } as const;

export const SEED_PLAN: Omit<Plan, 'approvedAt'> = {
  schema: 'kinesthetic.plan.v1', version: 1, approvedBy: 'PM&R physician (demo)',
  rationale: 'Initial home program after outpatient assessment (synthetic case).', basedOnExerciseIds: [],
  exercise: { type: 'seated_shoulder_raise', side: 'right', targetDeg: 80, prescribedReps: 8, holdMs: 400, maxTrunkDeviationDeg: 12 },
  coachingNote: 'Sit tall, keep the chest facing forward, pause briefly at the top.',
};

export class PlanStore {
  readonly directory: string;
  constructor(directory: string) {
    this.directory = directory;
    mkdirSync(directory, { recursive: true });
    if (!this.list().length) this.write({ ...SEED_PLAN, approvedAt: new Date().toISOString() });
  }
  private write(plan: Plan) {
    const path = resolve(this.directory, `plan-v${plan.version}.json`);
    writeFileSync(path, JSON.stringify(plan, null, 2), { flag: 'wx' });   // wx: never overwrite an approved version
    return plan;
  }
  list(): Plan[] {
    return readdirSync(this.directory).filter(f => /^plan-v\d+\.json$/.test(f))
      .map(f => JSON.parse(readFileSync(resolve(this.directory, f), 'utf8')) as Plan)
      .sort((a, b) => a.version - b.version);
  }
  get(version: number) { return this.list().find(p => p.version === version) ?? null; }
  active(): Plan { const all = this.list(); return all[all.length - 1]; }

  /** Physician approval: validated changes on top of the active plan become the next immutable version. */
  approve(input: { approvedBy?: string; rationale?: string; basedOnExerciseIds?: string[]; coachingNote?: string;
                   exercise?: Partial<ExercisePlan>; expectedActiveVersion?: number }): Plan {
    const current = this.active();
    if (input.expectedActiveVersion != null && input.expectedActiveVersion !== current.version)
      throw Error(`Plan changed since you opened it (now v${current.version}); reload before approving.`);
    const rationale = String(input.rationale ?? '').trim();
    if (rationale.length < 5) throw Error('A rationale is required to approve a plan change.');
    const exercise = { ...current.exercise };
    for (const key of Object.keys(LIMITS) as (keyof typeof LIMITS)[]) {
      const value = input.exercise?.[key];
      if (value == null) continue;
      const n = Number(value), [lo, hi] = LIMITS[key];
      if (!Number.isFinite(n) || n < lo || n > hi) throw Error(`${key} must be between ${lo} and ${hi}.`);
      (exercise as any)[key] = key === 'prescribedReps' ? Math.round(n) : n;
    }
    if (input.exercise?.side) exercise.side = input.exercise.side === 'left' ? 'left' : 'right';
    return this.write({
      schema: 'kinesthetic.plan.v1', version: current.version + 1,
      approvedBy: String(input.approvedBy ?? 'PM&R physician (demo)').slice(0, 120),
      approvedAt: new Date().toISOString(), rationale: rationale.slice(0, 2000),
      basedOnExerciseIds: (input.basedOnExerciseIds ?? []).map(String).slice(0, 20),
      exercise, coachingNote: String(input.coachingNote ?? current.coachingNote).slice(0, 500),
    });
  }
}
