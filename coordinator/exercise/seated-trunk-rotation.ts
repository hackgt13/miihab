// Seated trunk rotation. Written against the ExerciseKind interface only — the rep state machine,
// validity rules, calibration and summary all come from kind.ts unchanged.
//
// The movement is a rotation of the shoulder line about the torso axis, measured against a neutral
// shoulder line captured at rest. The compensation this exercise cares about is leaning: a patient
// who side-bends instead of rotating reaches the target without doing the movement.

import {
  RepSession, angle, cross, dot, len, mid, reject, sub, unit,
  type ExerciseKind, type Frame, type Observation, type Reference, type RepParams, type ResolvedParams,
} from './kind.ts';

export const ALGORITHM_VERSION = 'trunk-rotation.v1';

const SHOULDERS = [11, 12] as const, HIPS = [23, 24] as const;

export const seatedTrunkRotation: ExerciseKind<'trunk_lean'> = {
  id: 'trunk-rotation.v1',
  algorithmVersion: ALGORITHM_VERSION,
  requires: ['pose'],
  compensationReason: 'trunk_lean',
  compensationKey: 'trunkLean',
  // Rotation range is much smaller than shoulder elevation, so the rest band and hysteresis tighten.
  defaults: { restMaxDeg: 10, hysteresisDeg: 4, maxCompensationDeg: 10 },
  limits: { targetDeg: [15, 70], prescribedReps: [1, 30], holdMs: [0, 3000], maxCompensationDeg: [3, 30] },
  measurementNote: 'Estimated seated trunk rotation from monocular pose; depth-dependent and not a goniometer reading. Symptoms are patient-reported.',

  landmarks: () => [...SHOULDERS, ...HIPS],

  observe(frame: Frame, p: ResolvedParams, reference: Reference | null): Observation | null {
    const w = frame.worldLandmarks;
    const shoulderLine = sub(w[SHOULDERS[1]], w[SHOULDERS[0]]);
    const width = len(shoulderLine);
    if (width < 0.15) return null;
    // A rotation preserves shoulder width, so a collapsed line means the estimate is untrustworthy.
    if (reference && (width < reference.scaleM * 0.6 || width > reference.scaleM * 1.5)) return null;
    const torso = sub(mid(w[HIPS[0]], w[HIPS[1]]), mid(w[SHOULDERS[0]], w[SHOULDERS[1]]));
    if (len(torso) < 0.1) return null;

    // Before calibration there is no neutral to rotate away from, so rotation is zero by definition.
    // Calibration captures the neutral shoulder line as the second reference axis.
    if (!reference) return { primaryDeg: 0, compensationDeg: null, scaleM: width, axes: [torso, shoulderLine] };

    const axis = unit(reference.axes[0]);
    const neutral = reject(reference.axes[1], axis), current = reject(shoulderLine, axis);
    if (len(neutral) < 1e-6 || len(current) < 1e-6) return null;
    // Signed rotation about the calibrated torso axis. Rotating away from the prescribed side is
    // not partial credit, so the wrong direction reads as zero rather than as progress.
    const signed = Math.atan2(dot(cross(neutral, current), axis), dot(neutral, current)) * 180 / Math.PI;
    return {
      primaryDeg: Math.max(0, p.side === 'right' ? signed : -signed),
      compensationDeg: angle(torso, reference.axes[0]),
      scaleM: width,
      axes: [torso, shoulderLine],
    };
  },

  calibration: (r: Reference) => ({ torsoAxis: r.axes[0], neutralShoulderAxis: r.axes[1], shoulderWidthM: r.scaleM }),
};

/** Convenience constructor matching how server.ts builds a session. */
export class SeatedTrunkRotationSession extends RepSession<'trunk_lean'> {
  constructor(params: RepParams) { super(seatedTrunkRotation, params); }
}
