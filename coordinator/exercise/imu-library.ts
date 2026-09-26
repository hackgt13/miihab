// The single-IMU activities from agents/activity-library.md, as measurement specs over the imu-tilt kernel.
// Each entry is the library's `mount / measure / posture / rest / target` row; the clinical half (label, cue,
// dose defaults) is in exercises.ts, the normative decision in norms.ts. Adding one is an entry in each.
//
// Only the movements one AirPod measures well are here. Left out on purpose, with the reason:
//   - PNF diagonals: the plane IS the exercise, and the IMU is plane-blind; this would just be arm elevation.
//   - Ankle dorsi/plantarflexion: small ROM on an awkward mount, inside the sensor's mounting error.
//   - Sit-to-stand: its output is timing across stand and sit, and trunk tilt alone cannot tell them apart;
//     the relay carries attitude and rotation rate, not acceleration.
//   - Head steadiness and posture hold: continuous metrics, not reps; they need a hold engine, not this one.
//   - Shoulder rotation at the side, trunk and hip rotation: yaw, which the AirPod cannot measure.
//
// POSTURE IS LOAD-BEARING. Every note below states the assumption under which device tilt stands in for the
// segment. The camera is meant to police it; while camera measurement is off, nothing does.

import { imuTilt, type TiltMeasure } from './imu-tilt.ts';

const REPS: readonly [number, number] = [1, 30];
const COMPENSATION: readonly [number, number] = [3, 45];

// ── In-ear ──────────────────────────────────────────────────────────────────────────────────────────────
// AirPods in the ears are fixed to the head, so the device axes ARE head axes and the signed reading is valid.
// Assumed CMHeadphoneMotionManager frame: +x toward the right ear, +y forward, +z up when upright. Under that
// frame, chin-down turns the vertical positively about +x, and the right ear toward the right shoulder turns
// it negatively about +y. NOT yet checked on a worn pair: if flexion reads as extension, flip NOD here.
const NOD = 1, EAR_RIGHT = -1;
const HEAD_NOD: TiltMeasure = { kind: 'axis', axis: [1, 0, 0], sign: () => NOD };
const HEAD_EXTEND: TiltMeasure = { kind: 'axis', axis: [1, 0, 0], sign: () => -NOD as 1 | -1 };
const HEAD_SIDE: TiltMeasure = { kind: 'axis', axis: [0, 1, 0], sign: p => (p.side === 'right' ? EAR_RIGHT : -EAR_RIGHT) as 1 | -1 };
const NECK_REST = { restMaxDeg: 10, hysteresisDeg: 3, holdMs: 500 };

export const neckFlexion = imuTilt({
  id: 'neck-flexion.v1', algorithmVersion: 'neck-flexion.v1', measure: HEAD_NOD, defaults: NECK_REST,
  limits: { targetDeg: [20, 60], targetMaxDeg: [25, 70], prescribedReps: REPS, holdMs: [0, 5000], maxCompensationDeg: COMPENSATION },
  measurementNote: 'Neck flexion (chin toward chest) from AirPods in the ears: head pitch from the calibrated upright rest against gravity. Signed, so extension does not count. Valid only while the trunk stays still; leaning the trunk forward reads as neck flexion.',
});

export const neckExtension = imuTilt({
  id: 'neck-extension.v1', algorithmVersion: 'neck-extension.v1', measure: HEAD_EXTEND, defaults: NECK_REST,
  limits: { targetDeg: [20, 65], targetMaxDeg: [25, 75], prescribedReps: REPS, holdMs: [0, 5000], maxCompensationDeg: COMPENSATION },
  measurementNote: 'Neck extension (looking up) from AirPods in the ears: head pitch from the calibrated upright rest against gravity. Signed, so flexion does not count. Valid only while the trunk stays still; leaning back reads as neck extension.',
});

export const neckLateralFlexion = imuTilt({
  id: 'neck-lateral-flexion.v1', algorithmVersion: 'neck-lateral-flexion.v1', measure: HEAD_SIDE, defaults: NECK_REST,
  limits: { targetDeg: [15, 45], targetMaxDeg: [20, 50], prescribedReps: REPS, holdMs: [0, 5000], maxCompensationDeg: COMPENSATION },
  measurementNote: 'Neck lateral flexion (ear toward the prescribed side\'s shoulder) from AirPods in the ears: head roll from the calibrated upright rest against gravity. Signed by side. Valid only while the shoulders stay level; hiking the shoulder to meet the ear is not seen.',
});

// ── Wrist ───────────────────────────────────────────────────────────────────────────────────────────────
// Unsigned tilt: how the AirPod sits in the strap is unknown, so only magnitude is trusted.

// 40, not arm-elevation's 35: the default rest band (30) plus hysteresis (5) must stay below the lowest target.
const ELEVATION = { targetDeg: [40, 160], targetMaxDeg: [40, 170], prescribedReps: REPS, holdMs: [0, 3000], maxCompensationDeg: COMPENSATION } as const;

export const shoulderAbduction = imuTilt({
  id: 'shoulder-abduction.v1', algorithmVersion: 'shoulder-abduction.v1', defaults: {}, limits: ELEVATION,
  measurementNote: 'Straight-arm shoulder abduction (out to the side) from an IMU on the wrist: tilt from the calibrated rest against gravity. Plane-blind: raising the arm forward reads the same. Valid only while the elbow stays straight.',
});

export const scaption = imuTilt({
  id: 'scaption.v1', algorithmVersion: 'scaption.v1', defaults: {}, limits: ELEVATION,
  measurementNote: 'Straight-arm elevation in the scapular plane (about 30-45 degrees forward of the side) from an IMU on the wrist: tilt from the calibrated rest against gravity. Plane-blind. Valid only while the elbow stays straight.',
});

export const armHold = imuTilt({
  id: 'arm-hold.v1', algorithmVersion: 'arm-hold.v1', defaults: { holdMs: 5000 },
  limits: { ...ELEVATION, holdMs: [1000, 60000] },
  measurementNote: 'Raise-and-hold: straight-arm elevation from an IMU on the wrist, held at or above the target for the prescribed time. A rep counts once the hold completes; dropping below the band restarts the clock. Plane-blind; valid only while the elbow stays straight.',
});

export const forearmRotation = imuTilt({
  id: 'forearm-rotation.v1', algorithmVersion: 'forearm-rotation.v1', defaults: { restMaxDeg: 15 },
  limits: { targetDeg: [30, 90], targetMaxDeg: [35, 100], prescribedReps: REPS, holdMs: [0, 3000], maxCompensationDeg: COMPENSATION },
  measurementNote: 'Forearm pronation or supination from an IMU on the wrist, FOREARM HORIZONTAL and elbow at the side, from thumb-up neutral: roll about the forearm against gravity. Unsigned, so either direction counts. With the forearm vertical the rotation is yaw and is not measured.',
});

export const shoulderExternalRotation = imuTilt({
  id: 'shoulder-external-rotation.v1', algorithmVersion: 'shoulder-external-rotation.v1', defaults: { restMaxDeg: 15 },
  limits: { targetDeg: [25, 100], targetMaxDeg: [30, 110], prescribedReps: REPS, holdMs: [0, 3000], maxCompensationDeg: COMPENSATION },
  measurementNote: 'Shoulder external rotation in 90/90 (upper arm out to the side at shoulder height, elbow bent 90) from an IMU on the wrist: forearm tilt from horizontal-forward rest against gravity. Unsigned, so internal rotation also counts. Valid only while the 90/90 position holds; at the side the same rotation is yaw and is not measured.',
});

// ── Sternum ─────────────────────────────────────────────────────────────────────────────────────────────

export const trunkFlexion = imuTilt({
  id: 'trunk-flexion.v1', algorithmVersion: 'trunk-flexion.v1', defaults: { restMaxDeg: 10 },
  limits: { targetDeg: [20, 90], targetMaxDeg: [25, 100], prescribedReps: REPS, holdMs: [0, 5000], maxCompensationDeg: COMPENSATION },
  measurementNote: 'Trunk forward flexion from an IMU on the sternum: chest tilt from the calibrated upright rest against gravity. Plane-blind, so a side lean also reads. Hip flexion and spinal flexion are not separated.',
});

export const trunkLateralFlexion = imuTilt({
  id: 'trunk-lateral-flexion.v1', algorithmVersion: 'trunk-lateral-flexion.v1', defaults: { restMaxDeg: 5, hysteresisDeg: 3 },
  limits: { targetDeg: [10, 40], targetMaxDeg: [15, 45], prescribedReps: REPS, holdMs: [0, 5000], maxCompensationDeg: COMPENSATION },
  measurementNote: 'Trunk lateral flexion toward the prescribed side from an IMU on the sternum: chest tilt from the calibrated upright rest against gravity. Unsigned and plane-blind: which side, and a forward lean, are not distinguished. Hip shift is not seen.',
});

export const trunkExtension = imuTilt({
  id: 'trunk-extension.v1', algorithmVersion: 'trunk-extension.v1', defaults: { restMaxDeg: 4, hysteresisDeg: 2, holdMs: 500 },
  limits: { targetDeg: [8, 30], targetMaxDeg: [10, 35], prescribedReps: REPS, holdMs: [0, 5000], maxCompensationDeg: COMPENSATION },
  measurementNote: 'Trunk extension (leaning back) from an IMU on the sternum: chest tilt from the calibrated upright rest against gravity. Unsigned and plane-blind, so a forward lean also reads. Knee and hip substitution is not seen.',
});

// ── Thigh and ankle ─────────────────────────────────────────────────────────────────────────────────────

export const kneeExtension = imuTilt({
  id: 'knee-extension.v1', algorithmVersion: 'knee-extension.v1', defaults: { restMaxDeg: 15 },
  limits: { targetDeg: [30, 90], targetMaxDeg: [35, 95], prescribedReps: REPS, holdMs: [0, 5000], maxCompensationDeg: COMPENSATION },
  measurementNote: 'Seated knee extension from an IMU on the ankle: shank tilt from hanging-vertical rest against gravity. The chair fixes the thigh, which is what makes shank tilt equal knee angle; lifting the thigh off the seat inflates it.',
});

export const straightLegRaise = imuTilt({
  id: 'straight-leg-raise.v1', algorithmVersion: 'straight-leg-raise.v1', defaults: { restMaxDeg: 10 },
  limits: { targetDeg: [20, 80], targetMaxDeg: [25, 90], prescribedReps: REPS, holdMs: [0, 5000], maxCompensationDeg: COMPENSATION },
  measurementNote: 'Straight-leg raise lying on the back from an IMU on the thigh or ankle: leg tilt from lying-flat rest against gravity. Valid only while the knee stays straight; pelvic tilt is not seen.',
});

export const hipAbduction = imuTilt({
  id: 'hip-abduction.v1', algorithmVersion: 'hip-abduction.v1', defaults: { restMaxDeg: 8, hysteresisDeg: 3 },
  limits: { targetDeg: [15, 45], targetMaxDeg: [20, 50], prescribedReps: REPS, holdMs: [0, 5000], maxCompensationDeg: COMPENSATION },
  measurementNote: 'Standing hip abduction from an IMU on the thigh: thigh tilt from standing rest against gravity. Plane-blind, so a forward kick also reads; the trunk side-lean cheat is not seen.',
});

export const seatedMarch = imuTilt({
  id: 'seated-march.v1', algorithmVersion: 'seated-march.v1', defaults: { restMaxDeg: 5, hysteresisDeg: 3, holdMs: 0, minRepMs: 400 },
  limits: { targetDeg: [10, 40], targetMaxDeg: [15, 50], prescribedReps: [1, 60], holdMs: [0, 2000], maxCompensationDeg: COMPENSATION },
  measurementNote: 'Seated marching, one leg: thigh tilt from resting-on-the-seat against gravity, from an IMU on that thigh. One IMU is one leg, so symmetry between legs is not measured.',
});

export const IMU_LIBRARY = [
  neckFlexion, neckExtension, neckLateralFlexion,
  shoulderAbduction, scaption, armHold, forearmRotation, shoulderExternalRotation,
  trunkFlexion, trunkLateralFlexion, trunkExtension,
  kneeExtension, straightLegRaise, hipAbduction, seatedMarch,
] as const;
