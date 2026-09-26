// Versioned, immutable care plans. Approving a change writes a new version; earlier versions never change.
// A session pins the version it started with, so a mid-session approval cannot alter it.
//
// Schema v2 prescribes a LIST of activities rather than one exercise. v1 could only ever say
// `type: 'seated_shoulder_raise'` — a string literal — so a clinician could not prescribe golf, or
// trunk rotation, or two things in one plan. That made an "inbox of what to do today" impossible to
// express and a cross-activity dashboard impossible to build.
//
// A plan starts from the patient's goal (e.g. golf with a friend). Each measured prescription carries a
// safe band (params.targetDeg up to the ceiling params.targetMaxDeg), a dose, and a progression envelope:
// the limits inside which the data may move the target without the clinician (see progression.ts).
//
// Migration is read-side and lossless-in-practice: v1 files on disk are never rewritten (they are
// approved clinical records), they are upgraded in memory on read. v2 files written before goals,
// prescription ids, ceilings and envelopes existed gain their defaults on read the same way. Every plan
// also carries a derived `exercise` view in the v1 shape so existing readers — Unity's rehab studio and
// coach — keep working while they move over. That view is computed, never persisted.
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { exerciseKind, exerciseKindForPlanType, DEFAULT_EXERCISE } from './exercise/registry.ts';
import { requireActivity } from './activities.ts';
import { LIBRARY } from './exercises.ts';

export const PLAN_SCHEMA = 'kinesthetic.plan.v2';
export const LEGACY_PLAN_SCHEMA = 'kinesthetic.plan.v1';

/** The v1 exercise shape. Retained as the compatibility view, not as storage. */
export interface ExercisePlan {
  type: 'seated_shoulder_raise';
  side: 'left' | 'right';
  targetDeg: number;
  targetMaxDeg: number | null;
  prescribedReps: number;
  holdMs: number;
  maxTrunkDeviationDeg: number;
}

export interface Progression {
  stepDeg: number;            // how far one level moves the target
  minTargetDeg: number;       // envelope floor: a step back never goes below this
  maxTargetDeg: number;       // envelope ceiling: past this the clinician decides
  sessionsToProgress: number; // consecutive good sessions at a level before moving up
  inBandRatio: number;        // share of prescribed reps that must land in the band for a good session
  maxOvershoots: number;      // more reps than this above the safe ceiling in a session → step back
  autoApply: boolean;         // apply level changes inside the envelope without waiting for the clinician
}

/**
 * One prescribed thing. `activityId` matches the Unity ActivityDefinition and the `activityId` on an
 * ActivitySummary, so a prescription and its completed sessions join on one key — that join is what
 * the inbox and the adherence trend are. `id` names this prescription within the plan (two measured
 * exercises can share the rehab studio); progression and measured sessions key on it.
 *
 * `exerciseKind` is null when an activity measures nothing clinical (golf is play, not a rep count).
 * Keeping the two fields separate is deliberate: the activity is where the patient goes, the kind is
 * what gets measured there, and golf has the first without the second.
 */
export interface ActivityPrescription {
  id: string;
  activityId: string;
  exerciseKind: string | null;
  order: number;
  targetCount: number;
  params: Record<string, number | string>;
  note: string;
  progression: Progression | null;   // measured prescriptions only
}

export interface Plan {
  schema: typeof PLAN_SCHEMA;
  version: number;
  approvedBy: string;
  approvedAt: string;
  rationale: string;
  basedOnExerciseIds: string[];
  origin: 'clinician' | 'auto-progression';
  proposalId: string | null;
  goal: { text: string; components: string[] };
  activities: ActivityPrescription[];
  coachingNote: string;
  /** Derived v1 view of the first measured activity. Computed on read; absent from the stored file. */
  exercise: ExercisePlan;
}

/** A stored plan is a Plan minus the derived view. */
type StoredPlan = Omit<Plan, 'exercise'>;

export const REHAB_ACTIVITY = 'rehab.studio';
export const GOLF_ACTIVITY = 'golf.adaptive';

/** Bounds for activities that measure nothing clinical, where no ExerciseKind supplies limits. */
const UNMEASURED_LIMITS = { targetCount: [1, 200] } as const;
/** v1 spelled the compensation bound after the shoulder; RepParams spells it generically. */
const LEGACY_KEYS: Readonly<Record<string, string>> = { maxTrunkDeviationDeg: 'maxCompensationDeg', maxSafeDeg: 'targetMaxDeg' };
const STRING_PARAMS: Readonly<Record<string, readonly string[]>> = { side: ['left', 'right'], assistance: ['assisted', 'active', 'resisted'], imuSource: ['club', 'wrist'] };
const DEFAULT_GOAL = { text: 'Play golf again with their best friend', components: ['shoulder elevation', 'elbow flexion', 'grip'] };

// Day 1 for the demo patient: graded exposure starts low, with room to climb toward a full golf
// backswing; measured by the AirPod on the club handle. Golf itself is prescribed as play.
export const SEED_PLAN: Omit<StoredPlan, 'approvedAt'> = {
  schema: PLAN_SCHEMA, version: 1, approvedBy: 'PM&R physician (demo)',
  rationale: 'Initial home program after outpatient assessment (synthetic case).', basedOnExerciseIds: [],
  origin: 'clinician', proposalId: null, goal: DEFAULT_GOAL,
  activities: [
    { id: 'arm-elevation-right', activityId: REHAB_ACTIVITY, exerciseKind: 'arm-elevation.v1', order: 0, targetCount: 8,
      params: { side: 'right', targetDeg: 45, targetMaxDeg: 60, holdMs: 400 }, note: 'Stop at the line; higher is not better.',
      progression: { stepDeg: 5, minTargetDeg: 40, maxTargetDeg: 90, sessionsToProgress: 2, inBandRatio: .8, maxOvershoots: 2, autoApply: true } },
    { id: 'elbow-flexion-right', activityId: REHAB_ACTIVITY, exerciseKind: 'elbow-flexion.v1', order: 1, targetCount: 10,
      params: { side: 'right', targetDeg: 90, targetMaxDeg: 110, holdMs: 0, loadKg: 0.5 }, note: 'Elbow by your side.', progression: null },
    { id: GOLF_ACTIVITY, activityId: GOLF_ACTIVITY, exerciseKind: null, order: 2, targetCount: 9,
      params: {}, note: 'Nine holes with a friend.', progression: null },
  ],
  coachingNote: 'Slow and controlled. Stop at the line; higher is not better.',
};

function limitsOf(exerciseKindId: string | null): Readonly<Record<string, readonly [number, number]>> {
  if (!exerciseKindId) return UNMEASURED_LIMITS;
  return { ...exerciseKind(exerciseKindId).limits, ...LIBRARY[exerciseKindId]?.limits };
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

function progressionOf(input: any, targetDeg: number, kindId: string, label: string): Progression {
  const [lo, hi] = exerciseKind(kindId).limits.targetDeg ?? [0, 180];
  const p: Progression = { stepDeg: 5, minTargetDeg: Math.min(lo + 5, targetDeg), maxTargetDeg: Math.min(hi, targetDeg + 30),
    sessionsToProgress: 2, inBandRatio: .8, maxOvershoots: 2, autoApply: false };
  for (const [key, value] of Object.entries(input ?? {})) {
    if (!(key in p) || value == null) continue;
    (p as any)[key] = key === 'autoApply' ? Boolean(value) : Number(value);
  }
  const check = (key: string, value: number, range: readonly [number, number]) => {
    if (!Number.isFinite(value) || value < range[0] || value > range[1]) throw Error(`${label}: progression.${key} must be between ${range[0]} and ${range[1]}.`);
  };
  check('stepDeg', p.stepDeg, [1, 20]); check('minTargetDeg', p.minTargetDeg, [lo, hi]); check('maxTargetDeg', p.maxTargetDeg, [lo, hi]);
  check('sessionsToProgress', p.sessionsToProgress, [1, 10]); check('inBandRatio', p.inBandRatio, [.5, 1]); check('maxOvershoots', p.maxOvershoots, [0, 10]);
  p.sessionsToProgress = Math.round(p.sessionsToProgress);
  if (!(p.minTargetDeg <= targetDeg && targetDeg <= p.maxTargetDeg))
    throw Error(`${label}: the target must sit inside the progression envelope (${p.minTargetDeg}–${p.maxTargetDeg}°).`);
  return p;
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
  const catalog = kindId ? LIBRARY[kindId] : undefined;
  const params: Record<string, number | string> = {};
  for (const [rawKey, value] of Object.entries(input?.params ?? {})) {
    if (value == null) continue;
    const key = LEGACY_KEYS[rawKey] ?? rawKey;
    if (STRING_PARAMS[key]) {
      if (!STRING_PARAMS[key].includes(String(value))) throw Error(`${key} must be ${STRING_PARAMS[key].join(' or ')}.`);
      params[key] = String(value); continue;
    }
    params[key] = bounded(key, value, limits);
  }
  const countRange = limits.prescribedReps ?? UNMEASURED_LIMITS.targetCount;
  const targetCount = bounded('targetCount', input?.targetCount ?? catalog?.defaults.prescribedReps, { targetCount: countRange });
  const id = String(input?.id ?? (kindId ? `${kindId.replace(/\.v\d+$/, '')}-${params.side ?? 'right'}` : activityId)).trim();
  if (!/^[a-z0-9.-]{1,64}$/.test(id)) throw Error(`Prescription id "${id}" must be lowercase letters, digits, dots and dashes.`);

  let progression: Progression | null = null;
  if (kindId) {
    // A measured prescription always has a band: target, and a safe ceiling above it.
    params.side ??= 'right';
    params.targetDeg ??= catalog?.defaults.targetDeg ?? 80;
    const target = Number(params.targetDeg);
    params.targetMaxDeg ??= Math.min(limits.targetMaxDeg?.[1] ?? 180, target + (catalog?.defaults.ceilingMarginDeg ?? 15));
    if (Number(params.targetMaxDeg) <= target) throw Error(`${id}: the safe ceiling (targetMaxDeg) must be above the target.`);
    progression = progressionOf(input?.progression, target, kindId, id);
  }
  return {
    id,
    activityId: activityId.slice(0, 64),
    exerciseKind: kindId,
    order,
    targetCount: Math.round(targetCount),
    params,
    note: String(input?.note ?? '').slice(0, 500),
    progression,
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
    targetMaxDeg: p.targetMaxDeg != null ? Number(p.targetMaxDeg) : null,
    prescribedReps: first?.targetCount ?? 8,
    holdMs: Number(p.holdMs ?? 400),
    maxTrunkDeviationDeg: Number(p.maxCompensationDeg ?? 12),
  };
}

const withView = (plan: StoredPlan): Plan => ({ ...plan, exercise: legacyView(plan.activities) });

/** v1 on disk stays v1 on disk. This is the read-side upgrade. */
function upgrade(raw: any): StoredPlan {
  const common = { origin: raw?.origin === 'auto-progression' ? 'auto-progression' as const : 'clinician' as const,
    proposalId: raw?.proposalId ?? null, goal: raw?.goal ?? DEFAULT_GOAL };
  if (raw?.schema === PLAN_SCHEMA) {
    return { ...raw, ...common, activities: (raw.activities ?? []).map(normaliseActivity) };
  }
  if (raw?.schema !== LEGACY_PLAN_SCHEMA) throw Error(`Unknown plan schema "${raw?.schema}".`);
  const e = raw.exercise ?? {};
  return {
    schema: PLAN_SCHEMA, version: raw.version, approvedBy: raw.approvedBy, approvedAt: raw.approvedAt,
    rationale: raw.rationale, basedOnExerciseIds: raw.basedOnExerciseIds ?? [], ...common,
    coachingNote: raw.coachingNote ?? '',
    activities: [normaliseActivity({
      activityId: REHAB_ACTIVITY,
      exerciseKind: exerciseKindForPlanType(e.type).id,
      targetCount: e.prescribedReps,
      params: { side: e.side, targetDeg: e.targetDeg, holdMs: e.holdMs, maxCompensationDeg: e.maxTrunkDeviationDeg },
      note: raw.coachingNote ?? '',
      progression: { minTargetDeg: Math.min(40, e.targetDeg), maxTargetDeg: Math.max(90, e.targetDeg) },
    }, 0)],
  };
}

/** A change to one prescription, by id. Params and progression merge over what the plan has. */
export interface PrescriptionChange {
  exerciseKind?: string | null; targetCount?: number; note?: string;
  params?: Record<string, number | string | null>; progression?: Partial<Progression>;
}
export interface ApproveInput {
  approvedBy?: string; rationale?: string; basedOnExerciseIds?: string[]; coachingNote?: string;
  goal?: { text?: string; components?: string[] };
  activities?: unknown[];                            // replaces the prescription list
  exercise?: Partial<Omit<ExercisePlan, 'targetMaxDeg'>> & { targetMaxDeg?: number };   // v1: patches the first measured activity
  changes?: Record<string, PrescriptionChange>;      // by prescription id
  add?: unknown[]; remove?: string[];
  expectedActiveVersion?: number;
  origin?: Plan['origin']; proposalId?: string | null;
}

export class PlanStore {
  readonly directory: string;
  constructor(directory: string) {
    this.directory = directory;
    mkdirSync(directory, { recursive: true });
    if (!this.list().length) this.write({ ...structuredClone(SEED_PLAN), approvedAt: new Date().toISOString() });
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
   * Physician (or progression) approval: validated changes on top of the active plan become the next
   * immutable version. Three ways to say what changed:
   *   `activities` replaces the prescription list (golf added, an exercise dropped);
   *   `changes` / `add` / `remove` edit prescriptions by id (the portal and progression.ts);
   *   `exercise` patches the first measured activity in the v1 shape.
   */
  approve(input: ApproveInput): Plan {
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
    } else if (input.exercise) {
      activities = current.activities.map(a => structuredClone(a));
      const patch = input.exercise;
      const index = Math.max(0, activities.findIndex(a => a.exerciseKind));
      const target = activities[index];
      if (!target) throw Error('No measured activity to patch; send activities instead.');
      const merged: Record<string, unknown> = { ...target.params };
      for (const [key, value] of Object.entries(patch)) {
        if (value == null || key === 'type' || key === 'prescribedReps') continue;
        merged[LEGACY_KEYS[key] ?? key] = value;
      }
      // A v1 client knows nothing of ceilings or envelopes: a new target keeps the ceiling's margin and
      // widens the envelope to include itself, since the clinician set it explicitly.
      let progression = target.progression;
      if (patch.targetDeg != null && target.params.targetDeg != null) {
        const t = Number(patch.targetDeg);
        if (patch.targetMaxDeg == null && target.params.targetMaxDeg != null)
          merged.targetMaxDeg = t + Number(target.params.targetMaxDeg) - Number(target.params.targetDeg);
        if (progression) progression = { ...progression, minTargetDeg: Math.min(progression.minTargetDeg, t), maxTargetDeg: Math.max(progression.maxTargetDeg, t) };
      }
      activities[index] = normaliseActivity({ ...target, params: merged, progression, targetCount: patch.prescribedReps ?? target.targetCount }, target.order);
    } else {
      const removed = new Set((input.remove ?? []).map(String));
      activities = current.activities.filter(a => !removed.has(a.id)).map(a => structuredClone(a));
      for (const [id, change] of Object.entries(input.changes ?? {})) {
        const index = activities.findIndex(a => a.id === id);
        if (index < 0) throw Error(`No prescription "${id}" in plan v${current.version}.`);
        const before = activities[index];
        const params: Record<string, unknown> = { ...before.params };
        for (const [key, value] of Object.entries(change.params ?? {})) {
          if (value === null) delete params[LEGACY_KEYS[key] ?? key]; else params[LEGACY_KEYS[key] ?? key] = value;
        }
        // Moving the target keeps the ceiling's margin unless the ceiling is set explicitly.
        if (change.params?.targetDeg != null && change.params.targetMaxDeg == null && change.params.maxSafeDeg == null && before.params.targetMaxDeg != null)
          params.targetMaxDeg = Number(change.params.targetDeg) + Number(before.params.targetMaxDeg) - Number(before.params.targetDeg);
        activities[index] = normaliseActivity({ ...before, ...(change.exerciseKind !== undefined ? { exerciseKind: change.exerciseKind } : {}),
          targetCount: change.targetCount ?? before.targetCount, note: change.note ?? before.note, params,
          progression: { ...before.progression, ...change.progression } }, before.order);
      }
      for (const add of input.add ?? []) activities.push(normaliseActivity(add, activities.length));
      if (!activities.length) throw Error('activities must hold at least one prescription.');
    }
    activities = activities.map((a, i) => ({ ...a, order: i })).sort((a, b) => a.order - b.order);
    const ids = activities.map(a => a.id);
    if (new Set(ids).size !== ids.length) throw Error('Prescription ids must be unique; give the second one an id.');

    return this.write({
      schema: PLAN_SCHEMA, version: current.version + 1,
      approvedBy: String(input.approvedBy ?? 'PM&R physician (demo)').slice(0, 160),
      approvedAt: new Date().toISOString(), rationale: rationale.slice(0, 2000),
      basedOnExerciseIds: (input.basedOnExerciseIds ?? []).map(String).slice(0, 20),
      origin: input.origin === 'auto-progression' ? 'auto-progression' : 'clinician', proposalId: input.proposalId ?? null,
      goal: { text: String(input.goal?.text ?? current.goal.text).slice(0, 300),
              components: (input.goal?.components ?? current.goal.components).map(String).slice(0, 12) },
      activities,
      coachingNote: String(input.coachingNote ?? current.coachingNote).slice(0, 500),
    });
  }
}
