// Limb elevation from an IMU the patient holds or wears. IMU only: no camera, no MediaPipe.
//
// Magnitude: how far the device has tilted from its calibrated resting attitude, measured against
// gravity — the angle between the world vertical expressed in the device frame now and at rest. That
// is yaw-free by construction (turning about the vertical leaves it unchanged, and AirPod yaw drifts),
// and it needs no guess about which device axis lies along the limb. The quaternion convention was
// checked against the gyro on recorded AirPod data: this form satisfies dg/dt = g × ω (residual ~9%
// during swings); the conjugate is ~4× worse.
//
// Compensation (trunk lean) is not measured: one IMU on the hand cannot see the trunk, and camera pose is
// off for now. The summary reports it as unknown rather than zero. Estimating it from the IMU is future work.
//
// Posture assumptions, per activity-library.md: for elevation THE ELBOW MUST STAY STRAIGHT; for a curl
// THE UPPER ARM MUST STAY STILL. Device tilt only stands in for the segment while that holds.

import {
  RepSession, angle, unit,
  type ExerciseKind, type Observation, type ObservationInput, type Reference,
  type RepParams, type ResolvedParams, type Vec,
} from './kind.ts';

/** Below this angular speed the device counts as still; calibration only uses still frames. */
const STILL_RAD_S = 0.35;

/** The world's vertical axis in the device frame, for a CoreMotion attitude quaternion [x, y, z, w]. */
export function verticalInDevice([x, y, z, w]: readonly number[]): Vec {
  return unit([2 * (x * z - w * y), 2 * (y * z + w * x), w * w - x * x - y * y + z * z]);
}

/** Everything an IMU tilt exercise shares; each exercise supplies identity, defaults and limits. */
function imuTilt(spec: Pick<ExerciseKind<'trunk_compensation'>, 'id' | 'algorithmVersion' | 'defaults' | 'limits' | 'measurementNote'>):
  ExerciseKind<'trunk_compensation'> {
  return {
    ...spec,
    requires: ['imu'],
    compensationReason: 'trunk_compensation',
    compensationKey: 'trunkDeviation',
    landmarks: () => [],   // no pose

    observe(input: ObservationInput, _p: ResolvedParams, reference: Reference | null): Observation | null {
      const imu = input.imu!;
      const vertical = verticalInDevice(imu.quaternion);
      if (!reference) {
        // A moving handle is not a resting reference.
        if (Math.hypot(...imu.rotationRate) > STILL_RAD_S) return null;
        return { primaryDeg: 0, compensationDeg: null, scaleM: 1, axes: [vertical] };
      }
      return {
        primaryDeg: angle(vertical, reference.axes[0]),
        compensationDeg: null,
        scaleM: 1,   // no segment length is involved, so the plausibility check stays inert
        axes: [vertical],
      };
    },

    calibration: (r: Reference) => ({ restingVertical: r.axes[0] }),
  };
}

export const armElevation = imuTilt({
  id: 'arm-elevation.v1',
  algorithmVersion: 'arm-elevation.v2',
  // AirPods stream ~25 Hz; allow a little more than one dropped packet before calling tracking lost.
  defaults: { trackingGapMs: 400 },
  limits: { targetDeg: [35, 160], targetMaxDeg: [40, 170], prescribedReps: [1, 30], holdMs: [0, 3000], maxCompensationDeg: [3, 45] },
  measurementNote: 'Arm elevation from an IMU on the hand or wrist: tilt from the calibrated rest attitude against gravity, yaw-free but plane-blind, and valid only while the elbow stays straight. Agreement with a goniometer is about +/-5 degrees once mounting and sensor-to-segment alignment error are included.',
});

export const elbowFlexion = imuTilt({
  id: 'elbow-flexion.v1',
  algorithmVersion: 'elbow-flexion.v1',
  defaults: { trackingGapMs: 400, restMaxDeg: 20 },
  limits: { targetDeg: [30, 140], targetMaxDeg: [40, 150], prescribedReps: [1, 30], holdMs: [0, 2000], maxCompensationDeg: [3, 45] },
  measurementNote: 'Elbow flexion from an IMU in the hand (a curl): tilt from the calibrated rest attitude against gravity, valid only while the upper arm stays still at the side. Not a goniometer reading.',
});

export class ArmElevationSession extends RepSession<'trunk_compensation'> {
  constructor(params: RepParams) { super(armElevation, params); }
}
export class ElbowFlexionSession extends RepSession<'trunk_compensation'> {
  constructor(params: RepParams) { super(elbowFlexion, params); }
}
