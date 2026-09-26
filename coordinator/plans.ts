// Versioned, immutable care plans. Approving a change writes a new version; earlier versions never change.
// A session pins the version it started with, so a mid-session approval cannot alter it.
//
// Schema v2 prescribes a LIST of activities rather than one exercise. v1 could only ever say
// `type: 'seated_shoulder_raise'` — a string literal — so a clinician could not prescribe golf, or
// trunk rotation, or two things in one plan. That made an "inbox of what to do today" impossible to
// express and a cross-activity dashboard impossible to build.
//
// Migration is read-side and lossless-in-practice: v1 files on disk are never rewritten (they are
// approved clinical records), they are upgraded in memory on read. Every plan also carries a derived
// `exercise` view in the v1 shape so existing readers — server.ts, the clinician portal — keep
// working untouched while they move over. That view is computed, never persisted: what lands on
// disk is clean v2.
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { exerciseKind, exerciseKindForPlanType, DEFAULT_EXERCISE } from './exercise/registry.ts';
import { requireActivity } from './activities.ts';

export const PLAN_SCHEMA = 'kinesthetic.plan.v2';
export const LEGACY_PLAN_SCHEMA = 'kinesthetic.plan.v1';

/** The v1 exercise shape. Retained as the compatibility view, not as storage. */
export interface ExercisePlan {
  type: 'seated_shoulder_raise';
  side: 'left' | 'right';
  targetDeg: number;
  prescribedReps: number;
  holdMs: number;
  maxTrunkDeviationDeg: number;
}

/**
 * One prescribed thing. `activityId` matches the Unity ActivityDefinition and the `activityId` on an
 * ActivitySummary, so a prescription and its completed sessions join on one key — that join is what
 * the inbox and the adherence trend are.
 *
 * `exerciseKind` is null when an activity measures nothing clinical (golf is play, not a rep count).
 * Keeping the two fields separate is deliberate: the activity is where the patient goes, the kind is
 * what gets measured there, and golf has the first without the second.
 */
export interface ActivityPrescription {
  activityId: string;
  exerciseKind: string | null;
  order: number;
  targetCount: number;
  params: Record<string, number | string>;
  note: string;
}

export interface Plan {
  schema: typeof PLAN_SCHEMA;
  version: number;
  approvedBy: string;
  approvedAt: string;
  rationale: string;
  basedOnExerciseIds: string[];
  activities: ActivityPrescription[];
  coachingNote: string;
  /** Derived v1 view of the first measured activity. Computed on read; absent from the stored file. */
  exercise: ExercisePlan;
}

/** A stored plan is a Plan minus the derived view. */
type StoredPlan = Omit<Plan, 'exercise'>;

export const REHAB_ACTIVITY = 'rehab.studio';

/** Bounds for activities that measure nothing clinical, where no ExerciseKind supplies limits. */
const UNMEASURED_LIMITS = { targetCount: [1, 200] } as const;
/** v1 spelled the compensation bound after the shoulder; RepParams spells it generically. */
const LEGACY_KEYS: Readonly<Record<string, string>> = { maxTrunkDeviationDeg: 'maxCompensationDeg' };

export const SEED_PLAN: Omit<StoredPlan, 'approvedAt'> = {
  schema: PLAN_SCHEMA, version: 1, approvedBy: 'PM&R physician (demo)',
  rationale: 'Initial home program after outpatient assessment (synthetic case).', basedOnExerciseIds: [],
  activities: [{
    activityId: REHAB_ACTIVITY, exerciseKind: DEFAULT_EXERCISE, order: 0, targetCount: 8,
    params: { side: 'right', targetDeg: 80, holdMs: 400, maxCompensationDeg: 12 },
    note: 'Sit tall, keep the chest facing forward, pause briefly at the top.',
  }],
  coachingNote: 'Sit tall, keep the chest facing forward, pause briefly at the top.',
};

function limitsOf(exerciseKindId: string | null) {
  return exerciseKindId ? exerciseKind(exerciseKindId).limits : UNMEASURED_LIMITS;
}

function bounded(key: string, value: unknown, limits: Readonly<Record<string, readonly [number, number]>>) {
  const range = limits[key];
  const n = Number(value);
  if (!Number.isFinite(n)) throw Error(`${key} must be a number.`);
  if (range) {
    const [lo, hi] = range;
    if (n < lo || n > hi) throw Error(`${key} must be between ${lo} and ${hi}.`);
  }
  return n;
}

/** Validate one prescription in isolation, so a bad entry names itself rather than the whole plan. */
function normaliseActivity(input: any, order: number): ActivityPrescription {
  const activityId = String(input?.activityId ?? '').trim();
  if (!activityId) throw Error(`activities[${order}].activityId is required.`);
  // The catalog is the authority on what may be prescribed, so a typo or a retired activity fails
  // here rather than becoming a permanent approved plan pointing at nothing.
  const activity = requireActivity(activityId);
  if (!activity.prescribable) throw Error(`activities[${order}]: ${activity.id} is not prescribable.`);
  // Explicit null means "measures nothing clinical" (golf). A named kind resolves through the
  // registry, which throws on an unknown id rather than silently prescribing nothing.
  const rawKind = input?.exerciseKind;
  const namedKind = rawKind == null ? '' : String(rawKind).trim();
  const kindId = rawKind === null ? null
    : namedKind ? exerciseKind(namedKind).id
    : activityId === REHAB_ACTIVITY ? DEFAULT_EXERCISE
    : null;
  const limits = limitsOf(kindId);
  const params: Record<string, number | string> = {};
  for (const [rawKey, value] of Object.entries(input?.params ?? {})) {
    if (value == null) continue;
    const key = LEGACY_KEYS[rawKey] ?? rawKey;
    if (key === 'side') { params.side = value === 'left' ? 'left' : 'right'; continue; }
    params[key] = bounded(key, value, limits);
  }
  const countRange = limits.prescribedReps ?? UNMEASURED_LIMITS.targetCount;
  const targetCount = bounded('targetCount', input?.targetCount, { targetCount: countRange });
  return {
    activityId: activityId.slice(0, 64),
    exerciseKind: kindId,
    order,
    targetCount: Math.round(targetCount),
    params,
    note: String(input?.note ?? '').slice(0, 500),
  };
}

/** The v1 shape, rebuilt from the first activity that measures something. */
function legacyView(activities: ActivityPrescription[]): ExercisePlan {
  const first = activities.find(a => a.exerciseKind) ?? activities[0];
  const p = first?.params ?? {};
  return {
    type: 'seated_shoulder_raise',
    side: p.side === 'left' ? 'left' : 'right',
    targetDeg: Number(p.targetDeg ?? 80),
    prescribedReps: first?.targetCount ?? 8,
    holdMs: Number(p.holdMs ?? 400),
    maxTrunkDeviationDeg: Number(p.maxCompensationDeg ?? 12),
  };
}

const withView = (plan: StoredPlan): Plan => ({ ...plan, exercise: legacyView(plan.activities) });

/** v1 on disk stays v1 on disk. This is the read-side upgrade. */
function upgrade(raw: any): StoredPlan {
  if (raw?.schema === PLAN_SCHEMA) {
    return { ...raw, activities: (raw.activities ?? []).map(normaliseActivity) };
  }
  if (raw?.schema !== LEGACY_PLAN_SCHEMA) throw Error(`Unknown plan schema "${raw?.schema}".`);
  const e = raw.exercise ?? {};
  return {
    schema: PLAN_SCHEMA, version: raw.version, approvedBy: raw.approvedBy, approvedAt: raw.approvedAt,
    rationale: raw.rationale, basedOnExerciseIds: raw.basedOnExerciseIds ?? [],
    coachingNote: raw.coachingNote ?? '',
    activities: [normaliseActivity({
      activityId: REHAB_ACTIVITY,
      exerciseKind: exerciseKindForPlanType(e.type).id,
      targetCount: e.prescribedReps,
      params: { side: e.side, targetDeg: e.targetDeg, holdMs: e.holdMs, maxCompensationDeg: e.maxTrunkDeviationDeg },
      note: raw.coachingNote ?? '',
    }, 0)],
  };
}

export class PlanStore {
  readonly directory: string;
  constructor(directory: string) {
    this.directory = directory;
    mkdirSync(directory, { recursive: true });
    if (!this.list().length) this.write({ ...SEED_PLAN, approvedAt: new Date().toISOString() });
  }
  private write(plan: StoredPlan) {
    const path = resolve(this.directory, `plan-v${plan.version}.json`);
    writeFileSync(path, JSON.stringify(plan, null, 2), { flag: 'wx' });   // wx: never overwrite an approved version
    return withView(plan);
  }
  list(): Plan[] {
    return readdirSync(this.directory).filter(f => /^plan-v\d+\.json$/.test(f))
      .map(f => withView(upgrade(JSON.parse(readFileSync(resolve(this.directory, f), 'utf8')))))
      .sort((a, b) => a.version - b.version);
  }
  get(version: number) { return this.list().find(p => p.version === version) ?? null; }
  active(): Plan { const all = this.list(); return all[all.length - 1]; }

  /**
   * Physician approval: validated changes on top of the active plan become the next immutable version.
   *
   * Takes either `activities` (v2: replaces the prescription list, so golf can be added or an exercise
   * dropped) or `exercise` (v1: patches the first measured activity in place). Accepting both is what
   * lets the clinician portal keep posting the shape it posts today.
   */
  approve(input: { approvedBy?: string; rationale?: string; basedOnExerciseIds?: string[]; coachingNote?: string;
                   activities?: unknown[]; exercise?: Partial<ExercisePlan>; expectedActiveVersion?: number }): Plan {
    const current = this.active();
    if (input.expectedActiveVersion != null && input.expectedActiveVersion !== current.version)
      throw Error(`Plan changed since you opened it (now v${current.version}); reload before approving.`);
    const rationale = String(input.rationale ?? '').trim();
    if (rationale.length < 5) throw Error('A rationale is required to approve a plan change.');

    let activities: ActivityPrescription[];
    if (input.activities) {
      if (!Array.isArray(input.activities) || !input.activities.length)
        throw Error('activities must hold at least one prescription.');
      if (input.activities.length > 12) throw Error('activities must hold at most 12 prescriptions.');
      activities = input.activities.map(normaliseActivity);
    } else {
      activities = current.activities.map(a => ({ ...a, params: { ...a.params } }));
      const patch = input.exercise ?? {};
      const index = Math.max(0, activities.findIndex(a => a.exerciseKind));
      const target = activities[index];
      if (!target) throw Error('No measured activity to patch; send activities instead.');
      const merged: Record<string, unknown> = { ...target.params };
      for (const [key, value] of Object.entries(patch)) {
        if (value == null || key === 'type' || key === 'prescribedReps') continue;
        merged[LEGACY_KEYS[key] ?? key] = value;
      }
      activities[index] = normaliseActivity({
        ...target,
        params: merged,
        targetCount: patch.prescribedReps ?? target.targetCount,
      }, target.order);
    }
    activities = activities
      .map((a, i) => ({ ...a, order: i }))
      .sort((a, b) => a.order - b.order);

    return this.write({
      schema: PLAN_SCHEMA, version: current.version + 1,
      approvedBy: String(input.approvedBy ?? 'PM&R physician (demo)').slice(0, 120),
      approvedAt: new Date().toISOString(), rationale: rationale.slice(0, 2000),
      basedOnExerciseIds: (input.basedOnExerciseIds ?? []).map(String).slice(0, 20),
      activities,
      coachingNote: String(input.coachingNote ?? current.coachingNote).slice(0, 500),
    });
  }
}
