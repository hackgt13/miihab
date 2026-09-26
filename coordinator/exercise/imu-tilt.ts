// The single-IMU kernel, per activity-library.md: "one segment's inclination → a target band held for
// a duration". Every IMU exercise is this computation plus a spec; adding one must not add measurement code.
//
// Two ways to read the segment, both against gravity only, so both are yaw-free (AirPod yaw drifts):
//
//   'tilt'   The angle between the world vertical in the device frame now and at rest. Needs no guess about
//            how the device sits on the segment, so it suits anything strapped or held. Unsigned and
//            plane-blind: it says how far, never which way. The camera owns direction.
//   'axis'   The signed part of that tilt about one device axis. Only valid where the device frame is fixed to
//            the body — AirPods in the ears — so flexion and extension, or left and right, become separable.
//
// The quaternion convention was checked against the gyro on recorded AirPod data: verticalInDevice satisfies
// dg/dt = g × ω (residual ~9% during swings); the conjugate is ~4× worse.
//
// Compensation is not measured: one IMU cannot see the rest of the body, and camera pose is off for now.
// The summary reports it as unknown rather than zero.

import {
  angle, cross, dot, len, reject, unit,
  type ExerciseKind, type Observation, type ObservationInput, type Reference, type ResolvedParams, type Vec,
} from './kind.ts';

/** Below this angular speed the device counts as still; calibration only uses still frames. */
const STILL_RAD_S = 0.35;

/** The world's vertical axis in the device frame, for a CoreMotion attitude quaternion [x, y, z, w]. */
export function verticalInDevice([x, y, z, w]: readonly number[]): Vec {
  return unit([2 * (x * z - w * y), 2 * (y * z + w * x), w * w - x * x - y * y + z * z]);
}

/** Signed rotation of the vertical about device axis k from `from` to `to`, in degrees. Other rotation is ignored. */
export function signedTiltAbout(from: Vec, to: Vec, k: Vec): number {
  const a = reject(from, k), b = reject(to, k);
  if (len(a) < 1e-6 || len(b) < 1e-6) return 0;   // vertical along the axis: no rotation about it is defined
  return Math.atan2(dot(k, cross(a, b)), dot(a, b)) * 180 / Math.PI;
}

/**
 * How the primary angle is read. `axis` is a device axis; `sign` picks which way counts as the movement, and may
 * depend on the prescribed side (lateral flexion toward the left is the opposite rotation to toward the right).
 */
export type TiltMeasure =
  | { kind: 'tilt' }
  | { kind: 'axis'; axis: Vec; sign: (p: ResolvedParams) => 1 | -1 };

export type ImuTiltSpec = Pick<ExerciseKind<'trunk_compensation'>, 'id' | 'algorithmVersion' | 'defaults' | 'limits' | 'measurementNote'>
  & { measure?: TiltMeasure };

/** Build an exercise kind from a spec. Everything else — calibration, reps, validity, summary — is the rep engine's. */
export function imuTilt({ measure = { kind: 'tilt' }, ...spec }: ImuTiltSpec): ExerciseKind<'trunk_compensation'> {
  return {
    // AirPods stream ~25 Hz; allow a little more than one dropped packet before calling tracking lost.
    ...spec, defaults: { trackingGapMs: 400, ...spec.defaults },
    requires: ['imu'],
    compensationReason: 'trunk_compensation',
    compensationKey: 'trunkDeviation',
    landmarks: () => [],   // no pose

    observe(input: ObservationInput, p: ResolvedParams, reference: Reference | null): Observation | null {
      const imu = input.imu!;
      const vertical = verticalInDevice(imu.quaternion);
      // scaleM is 1 because no segment length is involved, so the plausibility check stays inert.
      if (!reference) {
        // A moving device is not a resting reference.
        if (Math.hypot(...imu.rotationRate) > STILL_RAD_S) return null;
        return { primaryDeg: 0, compensationDeg: null, scaleM: 1, axes: [vertical] };
      }
      const rest = reference.axes[0];
      const primaryDeg = measure.kind === 'tilt' ? angle(vertical, rest)
        : measure.sign(p) * signedTiltAbout(rest, vertical, measure.axis);
      return { primaryDeg, compensationDeg: null, scaleM: 1, axes: [vertical] };
    },

    calibration: (r: Reference) => ({ restingVertical: r.axes[0] }),
  };
}
