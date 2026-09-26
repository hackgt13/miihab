// Straight-arm shoulder elevation from a wrist-mounted IMU, with the camera policing the cheat.
//
// agents/activity-plan.md calls this the primary activity, and states the division this file
// implements: "IMU owns how high. Camera owns which direction, and did you cheat."
//
// Why the IMU and not pose for magnitude: inclination depends only on gravity, so it is yaw-free and
// essentially exact, where a monocular pose estimate of the same angle is neither. Why the camera for
// compensation: inclination cannot tell flexion from abduction (they differ by yaw, and yaw drifts),
// and it cannot see the patient leaning to buy height. One sensor each, no fusion beyond the shared
// clock in hostclock.ts.
//
// Posture assumption, per activity-library.md: THE ELBOW MUST STAY STRAIGHT. Wrist inclination only
// stands in for whole-arm inclination while that holds, and this kind cannot check it — the camera
// compensation rules are where that belongs once they carry a joint-level rule list.

import {
  RepSession, angle, len, mid, sub, unit,
  type ExerciseKind, type Observation, type ObservationInput, type Reference,
  type RepParams, type ResolvedParams, type Vec,
} from './kind.ts';

export const ALGORITHM_VERSION = 'arm-elevation.v1';

const SHOULDERS = [11, 12] as const, HIPS = [23, 24] as const;

/**
 * The device axis whose tilt is tracked. Any fixed axis yields a valid "how far from rest" angle,
 * because everything here is measured against the calibrated resting attitude rather than against an
 * absolute frame — so a wrong guess costs sensitivity, never correctness. Which axis best tracks a
 * straight arm still needs confirming against a real AirPod recording.
 */
const DEVICE_AXIS: Vec = [0, 0, 1];

/** Rotate v by unit quaternion q (x, y, z, w). */
function rotate(q: readonly number[], v: Vec): Vec {
  const [x, y, z, w] = q;
  const tx = 2 * (y * v[2] - z * v[1]);
  const ty = 2 * (z * v[0] - x * v[2]);
  const tz = 2 * (x * v[1] - y * v[0]);
  return [
    v[0] + w * tx + (y * tz - z * ty),
    v[1] + w * ty + (z * tx - x * tz),
    v[2] + w * tz + (x * ty - y * tx),
  ];
}

export const armElevation: ExerciseKind<'trunk_compensation'> = {
  id: 'arm-elevation.v1',
  algorithmVersion: ALGORITHM_VERSION,
  // Both channels are required. The Mac holds the camera and the AirPods, so the camera is there
  // anyway, and requiring it keeps the reference axes consistent across every calibration frame.
  requires: ['imu', 'pose'],
  compensationReason: 'trunk_compensation',
  compensationKey: 'trunkDeviation',
  defaults: {},
  limits: { targetDeg: [35, 175], prescribedReps: [1, 30], holdMs: [0, 3000], maxCompensationDeg: [3, 45] },
  measurementNote: 'Arm elevation from a wrist IMU relative to a calibrated resting attitude; yaw-free but plane-blind, and valid only while the elbow stays straight. Agreement with a goniometer is about +/-5 degrees once mounting and sensor-to-segment alignment error are included, not the ~1 degree raw inclination alone suggests.',

  // Only the compensation needs landmarks; magnitude comes from the IMU.
  landmarks: () => [...SHOULDERS, ...HIPS],

  observe(input: ObservationInput, p: ResolvedParams, reference: Reference | null): Observation | null {
    const imu = input.imu!, w = input.pose!.worldLandmarks;
    const axisNow = unit(rotate(imu.quaternion, DEVICE_AXIS));
    const torso = sub(mid(w[HIPS[0]], w[HIPS[1]]), mid(w[SHOULDERS[0]], w[SHOULDERS[1]]));
    if (len(torso) < 0.1) return null;

    // No resting attitude yet, so elevation is zero by definition and calibration can proceed.
    if (!reference) return { primaryDeg: 0, compensationDeg: null, scaleM: 1, axes: [axisNow, torso] };

    return {
      primaryDeg: angle(axisNow, reference.axes[0]),
      // The camera's job: how far the trunk has moved from where it was at rest.
      compensationDeg: angle(torso, reference.axes[1]),
      scaleM: 1,   // no segment length is involved, so the plausibility check stays inert
      axes: [axisNow, torso],
    };
  },

  calibration: (r: Reference) => ({ restingDeviceAxis: r.axes[0], torsoAxis: r.axes[1] }),
};

export class ArmElevationSession extends RepSession<'trunk_compensation'> {
  constructor(params: RepParams) { super(armElevation, params); }
}
