// The two-IMU kernel: one sensor on the segment that moves, one on the segment it moves against.
//
// imu-tilt.ts reads a single segment's inclination and says so plainly: "Compensation is not measured:
// one IMU cannot see the rest of the body." That is the gap this closes. A shoulder raise and a
// shoulder raise achieved by leaning look identical to one sensor on the wrist; with a second on the
// trunk they do not, because the trunk's own tilt is visible and is exactly the compensation the
// clinician is watching for.
//
// What stays out of here: every exercise-specific decision. A movement brings a profile — where the
// two sensors sit, which axis its angle is read about, which way counts as the movement, how still is
// still — and the kernel brings the mathematics. Adding a movement adds a profile, never a branch in
// this file. That is the same promise imu-tilt makes for one sensor, kept for two.
//
// Yaw is not used, anywhere. AirPod yaw drifts, and two devices drift independently, so a full relative
// quaternion would carry the sum of both errors. Every measure here is built from each device's own
// vertical — gravity, which does not drift — and the relative angle is the difference of two
// gravity-referenced tilts rather than a rotation between two attitudes.
//
// The output is the same Observation the one-sensor kernel produces, so the rep engine, the qualities
// and the trajectory evaluator work unchanged. The only addition is `metrics`, an open bag a profile
// may fill with whatever else it measures, for the movements that need more than one angle.

import {
  angle, cross, dot, len, reject, unit,
  type ExerciseKind, type ImuSample, type Observation, type ObservationInput, type Reference,
  type ResolvedParams, type Vec,
} from './kind.ts';
import { verticalInDevice, signedTiltAbout } from './imu-tilt.ts';

/** Which sensor a profile is talking about. `moving` drives the angle; `base` is what it moves against. */
export type Placement = 'moving' | 'base';

/**
 * How a profile reads its angle from the two segments.
 *
 *   'between'    The angle between the two segments' inclinations, unsigned. Needs no guess about how
 *                either device sits, so it suits anything strapped or held. Says how far, never which way.
 *   'difference' Each segment's signed tilt about its own axis, subtracted. Separates flexion from
 *                extension, and only valid where both device frames are fixed to the body.
 */
export type PairMeasure =
  | { kind: 'between' }
  | { kind: 'difference'; movingAxis: Vec; baseAxis: Vec; sign: (p: ResolvedParams) => 1 | -1 };

export interface ImuPairProfile {
  /** Where each sensor is worn, for the brief and for the summary. Not read by the mathematics. */
  readonly placements: Readonly<Record<Placement, string>>;
  readonly measure: PairMeasure;
  /**
   * How much of the base segment's own movement is compensation rather than the exercise. A raise
   * done by leaning shows here; a small sway does not.
   */
  readonly compensation?: { readonly axis?: Vec };
  /** Anything else this movement measures, from the same two attitudes. Optional and open-ended. */
  readonly metrics?: (moving: Segment, base: Segment, p: ResolvedParams) => Record<string, number>;
}

/** One sensor's state at an instant: its vertical now, and where that vertical rested. */
export interface Segment { vertical: Vec; rest: Vec; sample: ImuSample }

export type ImuPairSpec =
  Pick<ExerciseKind<'trunk_compensation'>, 'id' | 'algorithmVersion' | 'defaults' | 'limits' | 'measurementNote'>
  & { profile: ImuPairProfile };

/// A device is a resting reference only while it is still. Both must be: calibrating against a moving
/// trunk fixes the wrong zero, and every angle after it is wrong by that much.
const STILL_RAD_S = 0.35;

/// Neither sample may be older than this against the other, or the angle between them is the angle
/// between two different moments. At ~25 Hz a pair inside 60ms is adjacent samples.
export const PAIR_WINDOW_MS = 60;

/**
 * Holds the newest sample from each sensor and hands back a pair only when both are present and close
 * enough in time to be about the same instant.
 *
 * This is where two independent streams become one observation, and it is deliberately not inside
 * `observe`: the kernel should be given a coherent pair, not asked to remember what arrived when. It
 * also means the pairing is testable without an exercise, which is where the timing bugs live.
 */
export class PairSync {
  private latest = new Map<Placement, ImuSample>();
  // Node 24 runs this file by stripping types only, so no parameter properties.
  private readonly windowMs: number;
  constructor(windowMs: number = PAIR_WINDOW_MS) { this.windowMs = windowMs; }

  /** Accept a sample. Out-of-order samples for a sensor are dropped rather than rewinding it. */
  push(which: Placement, sample: ImuSample): void {
    const held = this.latest.get(which);
    if (held && sample.hostMonotonicMs <= held.hostMonotonicMs) return;
    this.latest.set(which, sample);
  }

  /**
   * The current pair, or null when one sensor is missing or stale. `tMs` is the newer of the two: a
   * pair is no fresher than its oldest half, but it happened at the later instant.
   */
  pair(): { moving: ImuSample; base: ImuSample; tMs: number; skewMs: number } | null {
    const moving = this.latest.get('moving'), base = this.latest.get('base');
    if (!moving || !base) return null;
    const skewMs = Math.abs(moving.hostMonotonicMs - base.hostMonotonicMs);
    if (skewMs > this.windowMs) return null;
    return { moving, base, tMs: Math.max(moving.hostMonotonicMs, base.hostMonotonicMs), skewMs };
  }

  /** Forget a sensor that has gone quiet, so a stale sample cannot pair with a live one later. */
  drop(which: Placement): void { this.latest.delete(which); }
  clear(): void { this.latest.clear(); }
}

/**
 * Build an exercise kind from a two-sensor profile. Calibration, reps, validity and the summary remain
 * the rep engine's, exactly as they are for one sensor.
 */
export function imuPair({ profile, ...spec }: ImuPairSpec): ExerciseKind<'trunk_compensation'> {
  const { measure } = profile;

  return {
    // Two streams drop packets independently, so a pair is missing more often than either sensor is.
    ...spec, defaults: { trackingGapMs: 500, ...spec.defaults },
    requires: ['imu'],
    compensationReason: 'trunk_compensation',
    compensationKey: 'trunkDeviation',
    landmarks: () => [],   // no pose

    observe(input: ObservationInput, p: ResolvedParams, reference: Reference | null): Observation | null {
      const moving = input.imu, base = input.imuBase;
      // A half-pair is not a worse reading, it is no reading. Saying so is what makes the engine
      // report tracking loss instead of quietly measuring against a sensor that stopped.
      if (!moving || !base) return null;

      const movingVertical = verticalInDevice(moving.quaternion);
      const baseVertical = verticalInDevice(base.quaternion);

      if (!reference) {
        if (Math.hypot(...moving.rotationRate) > STILL_RAD_S) return null;
        if (Math.hypot(...base.rotationRate) > STILL_RAD_S) return null;
        // Both rests, medianed independently by the engine. scaleM is 1: no segment length is involved.
        return { primaryDeg: 0, compensationDeg: 0, scaleM: 1, axes: [movingVertical, baseVertical] };
      }

      const m: Segment = { vertical: movingVertical, rest: reference.axes[0], sample: moving };
      const b: Segment = { vertical: baseVertical, rest: reference.axes[1] ?? reference.axes[0], sample: base };

      const primaryDeg = measure.kind === 'between'
        // How far the moving segment has travelled relative to the base, with the base's own travel
        // taken out: lean the trunk back and the raise does not grow.
        ? angle(m.vertical, m.rest) - angle(b.vertical, b.rest)
        : measure.sign(p) * (signedTiltAbout(m.rest, m.vertical, measure.movingAxis)
                           - signedTiltAbout(b.rest, b.vertical, measure.baseAxis));

      // The base segment's own movement, which one sensor could never see. This is the number the
      // engine already knows how to act on: past maxCompensationDeg the rep does not count.
      const compensationAxis = profile.compensation?.axis;
      const compensationDeg = Math.abs(compensationAxis
        ? signedTiltAbout(b.rest, b.vertical, compensationAxis)
        : angle(b.vertical, b.rest));

      const metrics = profile.metrics?.(m, b, p);
      return { primaryDeg, compensationDeg, scaleM: 1, axes: [m.vertical, b.vertical], ...(metrics ? { metrics } : {}) };
    },

    calibration: (r: Reference) => ({
      restingVertical: r.axes[0],
      restingVerticalBase: r.axes[1] ?? null,
      placements: profile.placements,
    }),
  };
}
