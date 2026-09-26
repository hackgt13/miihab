// Limb elevation from an IMU the patient holds or wears. IMU only: no camera, no MediaPipe.
//
// Magnitude: how far the device has tilted from its calibrated resting attitude, measured against
// gravity. The kernel, and why that is yaw-free and plane-blind, is in imu-tilt.ts.
//
// Posture assumptions, per activity-library.md: for elevation THE ELBOW MUST STAY STRAIGHT; for a curl
// THE UPPER ARM MUST STAY STILL. Device tilt only stands in for the segment while that holds.

import { RepSession, type RepParams } from './kind.ts';
import { imuTilt } from './imu-tilt.ts';

export { verticalInDevice } from './imu-tilt.ts';

export const armElevation = imuTilt({
  id: 'arm-elevation.v1',
  algorithmVersion: 'arm-elevation.v2',
  defaults: {},
  limits: { targetDeg: [35, 160], targetMaxDeg: [40, 170], prescribedReps: [1, 30], holdMs: [0, 3000], maxCompensationDeg: [3, 45] },
  measurementNote: 'Arm elevation from an IMU on the hand or wrist: tilt from the calibrated rest attitude against gravity, yaw-free but plane-blind, and valid only while the elbow stays straight. Agreement with a goniometer is about +/-5 degrees once mounting and sensor-to-segment alignment error are included.',
});

export const elbowFlexion = imuTilt({
  id: 'elbow-flexion.v1',
  algorithmVersion: 'elbow-flexion.v1',
  defaults: { restMaxDeg: 20 },
  limits: { targetDeg: [30, 140], targetMaxDeg: [40, 150], prescribedReps: [1, 30], holdMs: [0, 2000], maxCompensationDeg: [3, 45] },
  measurementNote: 'Elbow flexion from an IMU in the hand (a curl): tilt from the calibrated rest attitude against gravity, valid only while the upper arm stays still at the side. Not a goniometer reading.',
});

export class ArmElevationSession extends RepSession<'trunk_compensation'> {
  constructor(params: RepParams) { super(armElevation, params); }
}
export class ElbowFlexionSession extends RepSession<'trunk_compensation'> {
  constructor(params: RepParams) { super(elbowFlexion, params); }
}
