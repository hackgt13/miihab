// Two-AirPod movements: the ones a second sensor turns from "a tilt, assuming the posture held" into a measured joint
// angle. Chosen because everyone already knows them — a biceps curl, a squat — and because each is the weakest kind
// of single-IMU reading made strong: the posture it rests on is now checked, or the angle it needs is now actually
// there. The kernel and the two roles are in imu-tilt.ts. The arm raise is not here on purpose: it is the one-AirPod
// movement (imu-library.ts), the one that plays where a second pair belongs to someone else.
//
// Which physical AirPod is which is not known here and not assumed: at the start of a set the patient moves the one
// on the limb, and the pair that moves while the other rests becomes `imu` (exercise/imu-assign.ts).

import { imuTilt } from './imu-tilt.ts';

const REPS: readonly [number, number] = [1, 30];

export const bicepsCurl = imuTilt({
  id: 'biceps-curl.v1', algorithmVersion: 'biceps-curl.v1',
  second: { role: 'certify', reason: 'upper_arm_swing', key: 'upperArmSwing' },
  // Swinging the upper arm forward is how a curl is cheated; past about 15 degrees it is a front raise.
  defaults: { restMaxDeg: 20, maxCompensationDeg: 15 },
  limits: { targetDeg: [30, 140], targetMaxDeg: [40, 150], prescribedReps: REPS, holdMs: [0, 2000], maxCompensationDeg: [5, 30] },
  measurementNote: 'Biceps curl from an AirPod on the wrist, with a second on the upper arm above the elbow. Forearm tilt from hanging rest against gravity; a rep counts only while the upper arm stayed within maxCompensationDeg of the side, so the forearm tilt is the elbow angle to within that bound.',
});

export const squat = imuTilt({
  id: 'squat.v1', algorithmVersion: 'squat.v1',
  second: { role: 'sum' },
  // Standing still is the rest; a rep begins once the knees are clearly bending.
  defaults: { restMaxDeg: 15, hysteresisDeg: 5, holdMs: 300, minRepMs: 1200 },
  limits: { targetDeg: [25, 110], targetMaxDeg: [30, 120], prescribedReps: REPS, holdMs: [0, 3000], maxCompensationDeg: [3, 45] },
  measurementNote: 'Squat knee angle from two AirPods on one leg, on the shin above the ankle and on the thigh above the knee: the sum of shin and thigh tilt from standing, which is the knee angle while both stay in the sagittal plane — they lean opposite ways in any squat. Knees caving inward (valgus) is not seen and slightly inflates the reading.',
});

export const TWO_IMU = [bicepsCurl, squat] as const;
