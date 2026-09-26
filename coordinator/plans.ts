// Versioned, immutable care plans. Approving a change writes a new version; earlier versions never change.
// A session pins the version it started with, so a mid-session approval cannot alter it.
//
// A plan starts from the patient's goal (e.g. golf with a friend), lists the exercises the clinician chose from
// the library, and gives each one a dose (range band, reps, load, assistance) and a progression envelope: the
// limits inside which the data may move the dose without the clinician. Outside it, a clinician must approve.
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { libraryEntry, type Assistance, type Sensor } from './exercises.ts';

export interface Progression {
  stepDeg: number;            // how far one level moves the target
  minTargetDeg: number;       // envelope floor: a step back never goes below this
  maxTargetDeg: number;       // envelope ceiling: past this the clinician decides
  sessionsToProgress: number; // consecutive good sessions at a level before moving up
  inBandRatio: number;        // share of prescribed reps that must land in the band for a good session
  maxOvershoots: number;      // more reps than this above the safe ceiling in a session → step back
  autoApply: boolean;         // apply level changes inside the envelope without waiting for the clinician
}
export interface PlanExercise {
  id: string;
  type: string;
  side: 'left' | 'right';
  sensor: Sensor;
  targetDeg: number;          // bottom of the band: the rep counts from here
  maxSafeDeg: number;         // top of the band: above this is an overshoot
  prescribedReps: number;
  sets: number;
  holdMs: number;
  loadKg: number;
  assistance: Assistance;
  maxTrunkDeviationDeg: number | null;   // camera only; an IMU on the hand cannot see the trunk
  progression: Progression;
}
export interface Plan {
  schema: 'kinesthetic.plan.v2';
  version: number;
  approvedBy: string;
  approvedAt: string;
  rationale: string;
  basedOnExerciseIds: string[];
  origin: 'clinician' | 'auto-progression';
  proposalId: string | null;
  goal: { text: string; components: string[] };
  exercises: PlanExercise[];
  coachingNote: string;
}
export type ExerciseChange = Partial<Omit<PlanExercise, 'id' | 'type' | 'progression'>> & { progression?: Partial<Progression> };
export interface ApproveInput {
  approvedBy?: string; rationale?: string; basedOnExerciseIds?: string[]; coachingNote?: string;
  goal?: { text?: string; components?: string[] };
  changes?: Record<string, ExerciseChange>;
  add?: (ExerciseChange & { type: string; id?: string })[];
  remove?: string[];
  expectedActiveVersion?: number;
  origin?: Plan['origin']; proposalId?: string | null;
}

// Day 1 for the demo patient: graded exposure starts low, with room to climb toward a full golf backswing.
export const SEED_PLAN: Omit<Plan, 'approvedAt'> = {
  schema: 'kinesthetic.plan.v2', version: 1, approvedBy: 'PM&R physician (demo)',
  rationale: 'Initial home program after outpatient assessment (synthetic case).', basedOnExerciseIds: [],
  origin: 'clinician', proposalId: null,
  goal: { text: 'Play golf again with their best friend', components: ['shoulder elevation', 'elbow flexion', 'grip'] },
  exercises: [
    newExercise('seated_shoulder_raise', { id: 'shoulder-raise-right', side: 'right', sensor: 'imu',
      progression: { stepDeg: 5, minTargetDeg: 40, maxTargetDeg: 90, sessionsToProgress: 2, inBandRatio: .8, maxOvershoots: 2, autoApply: true } }),
    newExercise('biceps_curl', { id: 'curl-right', side: 'right' }),
  ],
  coachingNote: 'Slow and controlled. Stop at the line; higher is not better.',
};

export function newExercise(type: string, input: ExerciseChange & { id?: string } = {}): PlanExercise {
  const lib = libraryEntry(type), d = lib.defaults;
  const targetDeg = input.targetDeg ?? d.targetDeg;
  const exercise: PlanExercise = {
    id: input.id ?? `${type.replace(/_/g, '-')}-${input.side ?? 'right'}`, type, side: input.side ?? 'right',
    sensor: input.sensor ?? d.sensor, targetDeg, maxSafeDeg: input.maxSafeDeg ?? targetDeg + d.ceilingMarginDeg,
    prescribedReps: input.prescribedReps ?? d.prescribedReps, sets: input.sets ?? d.sets, holdMs: input.holdMs ?? d.holdMs,
    loadKg: input.loadKg ?? d.loadKg, assistance: input.assistance ?? d.assistance,
    maxTrunkDeviationDeg: input.maxTrunkDeviationDeg ?? null,
    progression: { stepDeg: 5, minTargetDeg: lib.limits.targetDeg[0], maxTargetDeg: Math.min(lib.limits.targetDeg[1], targetDeg + 30),
      sessionsToProgress: 2, inBandRatio: .8, maxOvershoots: 2, autoApply: false, ...input.progression },
  };
  return validateExercise(exercise);
}

function validateExercise(e: PlanExercise): PlanExercise {
  const lib = libraryEntry(e.type), L = lib.limits;
  const num = (key: string, value: number, [lo, hi]: readonly [number, number]) => {
    if (!Number.isFinite(value) || value < lo || value > hi) throw Error(`${e.id}: ${key} must be between ${lo} and ${hi}.`);
  };
  if (!/^[a-z0-9-]{1,40}$/.test(e.id)) throw Error(`Exercise id "${e.id}" must be lowercase letters, digits and dashes.`);
  if (e.side !== 'left' && e.side !== 'right') throw Error(`${e.id}: side must be left or right.`);
  if (!lib.sensors.includes(e.sensor)) throw Error(`${e.id}: ${lib.label} can be measured with ${lib.sensors.join(' or ')}, not ${e.sensor}.`);
  if (!['assisted', 'active', 'resisted'].includes(e.assistance)) throw Error(`${e.id}: assistance must be assisted, active or resisted.`);
  num('targetDeg', e.targetDeg, L.targetDeg); num('maxSafeDeg', e.maxSafeDeg, L.maxSafeDeg);
  if (e.maxSafeDeg <= e.targetDeg) throw Error(`${e.id}: the safe ceiling must be above the target.`);
  num('prescribedReps', e.prescribedReps, L.prescribedReps); num('sets', e.sets, L.sets);
  num('holdMs', e.holdMs, L.holdMs); num('loadKg', e.loadKg, L.loadKg);
  e.prescribedReps = Math.round(e.prescribedReps); e.sets = Math.round(e.sets);
  if (e.maxTrunkDeviationDeg != null) {
    if (e.sensor !== 'pose') throw Error(`${e.id}: a trunk lean limit needs the camera; an IMU on the hand cannot see the trunk.`);
    num('maxTrunkDeviationDeg', e.maxTrunkDeviationDeg, [3, 45]);
  }
  const p = e.progression;
  num('progression.stepDeg', p.stepDeg, [1, 20]);
  num('progression.minTargetDeg', p.minTargetDeg, L.targetDeg); num('progression.maxTargetDeg', p.maxTargetDeg, L.targetDeg);
  if (!(p.minTargetDeg <= e.targetDeg && e.targetDeg <= p.maxTargetDeg))
    throw Error(`${e.id}: the target must sit inside the progression envelope (${p.minTargetDeg}–${p.maxTargetDeg}°).`);
  num('progression.sessionsToProgress', p.sessionsToProgress, [1, 10]); p.sessionsToProgress = Math.round(p.sessionsToProgress);
  num('progression.inBandRatio', p.inBandRatio, [.5, 1]); num('progression.maxOvershoots', p.maxOvershoots, [0, 10]);
  p.autoApply = Boolean(p.autoApply);
  return e;
}

/** Plans saved before v2 had one shoulder-raise exercise measured by camera. */
export function upgrade(plan: any): Plan {
  if (plan.schema === 'kinesthetic.plan.v2') return plan as Plan;
  const e = plan.exercise;
  return {
    schema: 'kinesthetic.plan.v2', version: plan.version, approvedBy: plan.approvedBy, approvedAt: plan.approvedAt,
    rationale: plan.rationale, basedOnExerciseIds: plan.basedOnExerciseIds ?? [], origin: 'clinician', proposalId: null,
    goal: { ...SEED_PLAN.goal },
    exercises: [newExercise('seated_shoulder_raise', { id: `shoulder-raise-${e.side}`, side: e.side, sensor: 'pose',
      targetDeg: e.targetDeg, maxSafeDeg: Math.min(130, e.targetDeg + 15), prescribedReps: e.prescribedReps, holdMs: e.holdMs,
      maxTrunkDeviationDeg: e.maxTrunkDeviationDeg,
      progression: { minTargetDeg: Math.min(40, e.targetDeg), maxTargetDeg: Math.max(90, e.targetDeg) } })],
    coachingNote: plan.coachingNote,
  };
}

export class PlanStore {
  readonly directory: string;
  constructor(directory: string) {
    this.directory = directory;
    mkdirSync(directory, { recursive: true });
    if (!this.list().length) this.write({ ...structuredClone(SEED_PLAN), approvedAt: new Date().toISOString() });
  }
  private write(plan: Plan) {
    const path = resolve(this.directory, `plan-v${plan.version}.json`);
    writeFileSync(path, JSON.stringify(plan, null, 2), { flag: 'wx' });   // wx: never overwrite an approved version
    return plan;
  }
  list(): Plan[] {
    return readdirSync(this.directory).filter(f => /^plan-v\d+\.json$/.test(f))
      .map(f => upgrade(JSON.parse(readFileSync(resolve(this.directory, f), 'utf8'))))
      .sort((a, b) => a.version - b.version);
  }
  get(version: number) { return this.list().find(p => p.version === version) ?? null; }
  active(): Plan { const all = this.list(); return all[all.length - 1]; }

  /** A new version on top of the active plan: validated exercise changes, additions and removals. */
  approve(input: ApproveInput): Plan {
    const current = this.active();
    if (input.expectedActiveVersion != null && input.expectedActiveVersion !== current.version)
      throw Error(`Plan changed since you opened it (now v${current.version}); reload before approving.`);
    const rationale = String(input.rationale ?? '').trim();
    if (rationale.length < 5) throw Error('A rationale is required to approve a plan change.');

    const removed = new Set((input.remove ?? []).map(String));
    let exercises = current.exercises.filter(e => !removed.has(e.id)).map(e => structuredClone(e));
    for (const [id, change] of Object.entries(input.changes ?? {})) {
      const index = exercises.findIndex(e => e.id === id);
      if (index < 0) throw Error(`No exercise "${id}" in plan v${current.version}.`);
      const before = exercises[index];
      const next: PlanExercise = { ...before, ...pick(change), progression: { ...before.progression, ...change.progression } };
      // Moving the target keeps the ceiling's margin unless the ceiling is set explicitly.
      if (change.targetDeg != null && change.maxSafeDeg == null)
        next.maxSafeDeg = Math.min(libraryEntry(next.type).limits.maxSafeDeg[1], Number(change.targetDeg) + before.maxSafeDeg - before.targetDeg);
      coerce(next);
      exercises[index] = validateExercise(next);
    }
    for (const add of input.add ?? []) exercises.push(newExercise(add.type, coerce({ ...add } as any)));
    if (!exercises.length) throw Error('A plan needs at least one exercise.');
    const ids = exercises.map(e => e.id);
    if (new Set(ids).size !== ids.length) throw Error('Exercise ids must be unique.');

    return this.write({
      schema: 'kinesthetic.plan.v2', version: current.version + 1,
      approvedBy: String(input.approvedBy ?? 'PM&R physician (demo)').slice(0, 160),
      approvedAt: new Date().toISOString(), rationale: rationale.slice(0, 2000),
      basedOnExerciseIds: (input.basedOnExerciseIds ?? []).map(String).slice(0, 20),
      origin: input.origin === 'auto-progression' ? 'auto-progression' : 'clinician', proposalId: input.proposalId ?? null,
      goal: { text: String(input.goal?.text ?? current.goal.text).slice(0, 300),
              components: (input.goal?.components ?? current.goal.components).map(String).slice(0, 12) },
      exercises, coachingNote: String(input.coachingNote ?? current.coachingNote).slice(0, 500),
    });
  }
}

const EDITABLE = ['side', 'sensor', 'targetDeg', 'maxSafeDeg', 'prescribedReps', 'sets', 'holdMs', 'loadKg', 'assistance', 'maxTrunkDeviationDeg'] as const;
function pick(change: ExerciseChange) {
  return Object.fromEntries(EDITABLE.filter(k => change[k] !== undefined).map(k => [k, change[k]])) as Partial<PlanExercise>;
}
// Form and JSON input arrive as strings or numbers; numbers are compared after this.
function coerce<T extends Record<string, any>>(e: T): T {
  for (const k of ['targetDeg', 'maxSafeDeg', 'prescribedReps', 'sets', 'holdMs', 'loadKg'])
    if (e[k] != null) (e as any)[k] = Number(e[k]);
  if (e.maxTrunkDeviationDeg != null) (e as any).maxTrunkDeviationDeg = Number(e.maxTrunkDeviationDeg);
  if (e.progression) for (const k of ['stepDeg', 'minTargetDeg', 'maxTargetDeg', 'sessionsToProgress', 'inBandRatio', 'maxOvershoots'])
    if (e.progression[k] != null) e.progression[k] = Number(e.progression[k]);
  return e;
}
