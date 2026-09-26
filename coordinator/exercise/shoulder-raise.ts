// Seated shoulder raise. Everything exercise-independent lives in kind.ts; this file is the
// geometry plus a back-compatible facade.
//
// Angles come from raw world landmarks (never from avatar smoothing or game assistance).
// These are engineering thresholds for the rehearsed setup, not clinical standards.

import {
  RepSession, angle, len, mid, sub,
  type ExerciseKind, type Observation, type ObservationInput, type Reference, type RepParams,
  type ResolvedParams, type Sample as RepSample, type SharedInvalidReason, type Side, type Vec,
} from './kind.ts';

export const ALGORITHM_VERSION = 'shoulder-raise.v1';

const IDX = { shoulder: { left: 11, right: 12 }, elbow: { left: 13, right: 14 }, hips: [23, 24], shoulders: [11, 12] } as const;

export const shoulderRaise: ExerciseKind<'trunk_compensation'> = {
  id: 'shoulder-raise.v1',
  algorithmVersion: ALGORITHM_VERSION,
  requires: ['pose'],
  compensationReason: 'trunk_compensation',
  compensationKey: 'trunkDeviation',
  defaults: {},
  limits: { targetDeg: [35, 160], targetMaxDeg: [40, 170], prescribedReps: [1, 30], holdMs: [0, 3000], maxCompensationDeg: [3, 45] },
  measurementNote: 'Estimated shoulder elevation from monocular pose; not a goniometer reading. Symptoms are patient-reported.',

  landmarks: (p: ResolvedParams) => [IDX.shoulder[p.side], IDX.elbow[p.side], ...IDX.hips, ...IDX.shoulders],

  observe(input: ObservationInput, p: ResolvedParams, reference: Reference | null): Observation | null {
    const w = input.pose!.worldLandmarks;
    const shoulder = w[IDX.shoulder[p.side]], elbow = w[IDX.elbow[p.side]];
    const upper = sub(elbow, shoulder), upperArmM = len(upper);
    if (upperArmM < 0.08) return null;
    // Occlusion can collapse a segment while confidence stays high; reject implausible lengths.
    if (reference && (upperArmM < reference.scaleM * 0.6 || upperArmM > reference.scaleM * 1.5)) return null;
    const torso = sub(mid(w[23], w[24]), mid(w[11], w[12]));
    if (len(torso) < 0.1) return null;
    const down = reference ? reference.axes[0] : torso;   // uncalibrated: this frame's torso is "down"
    return {
      primaryDeg: angle(upper, down),
      compensationDeg: reference ? angle(torso, reference.axes[0]) : null,
      scaleM: upperArmM,
      axes: [torso],
    };
  },

  calibration: (reference: Reference) => ({ torsoAxis: reference.axes[0], upperArmM: reference.scaleM }),
};

// ── Back-compatible facade ────────────────────────────────────────────────────────────────────
// Preserves the pre-extraction surface exactly: ExerciseConfig's maxTrunkDeviationDeg spelling,
// the flat calibration.complete event, reps[].trunkMaxDeg, samples[].trunkDeg, and
// summary().exercise. Callers in server.ts, measure-capture.ts and the tests are unchanged.
// The generic engine uses the neutral names; the legacy spellings are mapped here, and the
// rename reaches the plan schema and the portal in the plan-v2 phase, not before.

export type { Frame, Point, Side, Vec } from './kind.ts';

export interface ExerciseConfig extends Omit<RepParams, 'maxCompensationDeg'> {
  maxTrunkDeviationDeg?: number;
}
export type InvalidReason = SharedInvalidReason | 'trunk_compensation';
export type ExerciseEvent =
  | { type: 'calibration.complete'; tMs: number; torsoAxis: [number, number, number]; upperArmM: number }
  | { type: 'rep.started'; tMs: number; rep: number }
  | { type: 'target.reached'; tMs: number; rep: number; angleDeg: number }
  | { type: 'rep.completed'; tMs: number; rep: number; valid: boolean; reason: InvalidReason | null;
      peakDeg: number; durationMs: number; trunkMaxDeg: number; aboveTargetMax: boolean; startMs: number }
  | { type: 'tracking.lost'; tMs: number; rep: number | null }
  | { type: 'tracking.recovered'; tMs: number };

export interface Sample { tMs: number; valid: boolean; angleDeg: number | null; trunkDeg: number | null }

export class ShoulderRaiseSession {
  private readonly session: RepSession<'trunk_compensation'>;
  readonly config: Required<Omit<ExerciseConfig, 'targetMaxDeg' | 'prescribedReps' | 'planVersion'>> &
    Pick<ExerciseConfig, 'targetMaxDeg' | 'prescribedReps' | 'planVersion'>;
  readonly events: ExerciseEvent[] = [];
  readonly samples: Sample[] = [];

  constructor(config: ExerciseConfig) {
    const { maxTrunkDeviationDeg, ...rest } = config;
    this.session = new RepSession(shoulderRaise,
      maxTrunkDeviationDeg != null ? { ...rest, maxCompensationDeg: maxTrunkDeviationDeg } : rest);
    const { maxCompensationDeg, ...params } = this.session.params;
    this.config = { ...params, maxTrunkDeviationDeg: maxCompensationDeg } as ShoulderRaiseSession['config'];
  }

  get phase() { return this.session.phase; }
  get currentRep() { return this.session.currentRep; }

  /** Measure one frame without changing state. Null when required landmarks are not trustworthy. */
  measure(frame: Frame): { angleDeg: number; trunkDeg: number | null; torso: Vec; upperArmM: number } | null {
    const o = this.session.observe(frame);
    return o && { angleDeg: o.primaryDeg, trunkDeg: o.compensationDeg, torso: o.axes[0], upperArmM: o.scaleM };
  }

  push(frame: Frame): ExerciseEvent[] {
    const out = this.session.push(frame).map(e => {
      if (e.type === 'calibration.complete')
        return { type: e.type, tMs: e.tMs, torsoAxis: e.reference.torsoAxis as Vec, upperArmM: e.reference.upperArmM as number };
      if (e.type === 'rep.completed') {
        const { compensationMaxDeg, ...rest } = e;
        return { ...rest, trunkMaxDeg: compensationMaxDeg };
      }
      return e;
    }) as ExerciseEvent[];
    this.events.push(...out);
    const s = this.session.samples.at(-1)! as RepSample;
    this.samples.push({ tMs: s.tMs, valid: s.valid, angleDeg: s.angleDeg, trunkDeg: s.compensationDeg });
    return out;
  }

  summary() {
    const { exerciseKind, params, reps, ...rest } = this.session.summary() as Record<string, any>;
    return {
      ...rest,
      exercise: 'seated_shoulder_raise',
      config: this.config,
      reps: (reps as any[]).map(({ compensationMaxDeg, ...r }) => ({ ...r, trunkMaxDeg: compensationMaxDeg })),
    } as Record<string, any> & { attempted: number; valid: number; completed: number; calibrated: boolean;
      medianValidPeakDeg: number | null; invalidReasons: Record<string, number>; trackingLossEvents: number;
      reps: { rep: number; startMs: number; endMs: number; valid: boolean; reason: InvalidReason | null;
        peakDeg: number; trunkMaxDeg: number; durationMs: number }[] };
  }
}
