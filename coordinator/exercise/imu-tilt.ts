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

/** Below this angular speed the device counts as still; calibration only uses still frames. About 9°/s: a held
 *  arm, not one drifting into place. */
const STILL_RAD_S = 0.15;

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

/**
 * A second AirPod, on the segment next to the measured one. Each AirPod reads yaw-free tilt from its own calibrated
 * rest, so the pair never needs a shared heading — two pairs of AirPods never agree on one, and both drift.
 *
 *   'certify'  The neighbouring segment is meant to stay put (the trunk under a raised arm, the upper arm above a
 *              curl). Its tilt is the rep's compensation: a rep that moved it past maxCompensationDeg does not count,
 *              so every counted rep is within that many degrees of the true joint angle. One IMU can only assume it.
 *   'sum'      The joint sits between two segments that tilt opposite ways from vertical (thigh forward and shin back
 *              in a squat), so the joint angle is the sum of the two tilts. One IMU cannot measure it at all.
 */
export type SecondImu = { role: 'certify'; reason: string; key: string } | { role: 'sum' };

export type ImuTiltSpec = Pick<ExerciseKind, 'id' | 'algorithmVersion' | 'defaults' | 'limits' | 'measurementNote'>
  & { measure?: TiltMeasure; second?: SecondImu };

const still = (imu: { rotationRate: readonly number[] }) => Math.hypot(...imu.rotationRate) <= STILL_RAD_S;

/** Build an exercise kind from a spec. Everything else — calibration, reps, validity, summary — is the rep engine's. */
export function imuTilt({ measure = { kind: 'tilt' }, second, ...spec }: ImuTiltSpec): ExerciseKind {
  return {
    // AirPods stream ~25 Hz; allow a little more than one dropped packet before calling tracking lost.
    ...spec, defaults: { trackingGapMs: 400, ...spec.defaults },
    requires: second ? ['imu', 'ref'] : ['imu'],
    compensationReason: second?.role === 'certify' ? second.reason : 'trunk_compensation',
    compensationKey: second?.role === 'certify' ? second.key : 'trunkDeviation',
    landmarks: () => [],   // no pose

    observe(input: ObservationInput, p: ResolvedParams, reference: Reference | null): Observation | null {
      const imu = input.imu!, ref = second ? input.ref! : null;
      const vertical = verticalInDevice(imu.quaternion);
      const refVertical = ref ? verticalInDevice(ref.quaternion) : null;
      const axes = refVertical ? [vertical, refVertical] : [vertical];
      // scaleM is 1 because no segment length is involved, so the plausibility check stays inert.
      if (!reference) {
        // A moving device is not a resting reference, and with two, both have to be still.
        if (!still(imu) || (ref && !still(ref))) return null;
        return { primaryDeg: 0, compensationDeg: null, scaleM: 1, axes };
      }
      const rest = reference.axes[0];
      const limb = measure.kind === 'tilt' ? angle(vertical, rest)
        : measure.sign(p) * signedTiltAbout(rest, vertical, measure.axis);
      const other = refVertical ? angle(refVertical, reference.axes[1]) : null;
      return {
        primaryDeg: second?.role === 'sum' ? limb + other! : limb,
        compensationDeg: second?.role === 'certify' ? other : null,
        scaleM: 1, axes,
      };
    },

    calibration: (r: Reference) => ({ restingVertical: r.axes[0], ...(r.axes[1] ? { referenceRestingVertical: r.axes[1] } : {}) }),
  };
}
