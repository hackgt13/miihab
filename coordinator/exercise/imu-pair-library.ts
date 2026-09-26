// Two-sensor movements, as profiles over the imu-pair kernel.
//
// Every entry here exists because the one-sensor version of the same movement carries a stated
// assumption it cannot check. imu-library.ts says so in each measurementNote: neck flexion is "valid
// only while the trunk stays still; leaning the trunk forward reads as neck flexion", and the curl
// assumes the "upper arm still at the side". A second sensor on the segment that is assumed still
// turns that assumption into a measurement — which is the whole reason to wear one.
//
// Adding a movement here is a profile: two placements, how the angle is read, and what counts as the
// base segment moving. No change to the kernel, and none to the engine, the qualities or the
// trajectory evaluator, which all read the same Observation a one-sensor kind produces.
//
// POSTURE IS STILL LOAD-BEARING, just less of it. Two sensors fix the relationship between two
// segments; they say nothing about the ones neither is on. A curl measured at forearm and upper arm
// is blind to the shoulder hiking, and the note on each entry says what it still cannot see.

import { imuPair, type ImuPairProfile } from './imu-pair.ts';
import type { ResolvedParams, Vec } from './kind.ts';

const REPS: readonly [number, number] = [1, 30];
const COMPENSATION: readonly [number, number] = [3, 45];

// Device frames, as imu-library.ts assumes them. In-ear: +x toward the right ear, +y forward, +z up.
// A strapped or held AirPod's frame is not fixed to the body, so anything worn that way reads
// unsigned ('between') rather than about a named axis.
const EAR_PITCH: Vec = [1, 0, 0];
const NOD = 1;

/// The trunk sensor's own forward-and-back tilt: what leaning looks like, whatever is being measured
/// above it. Shared by every profile whose base segment is the trunk.
const TRUNK_LEAN = { axis: [1, 0, 0] as Vec };

export const neckFlexionPaired = imuPair({
  id: 'neck-flexion.pair.v1', algorithmVersion: 'neck-flexion.pair.v1',
  defaults: { restMaxDeg: 10, hysteresisDeg: 3, holdMs: 500, maxCompensationDeg: 10 },
  limits: { targetDeg: [20, 60], targetMaxDeg: [25, 70], prescribedReps: REPS, holdMs: [0, 5000], maxCompensationDeg: COMPENSATION },
  measurementNote: 'Neck flexion from AirPods in the ears against a second AirPod on the sternum: head pitch minus trunk pitch, both from their calibrated upright rests against gravity. Leaning the trunk forward no longer reads as neck flexion — it reads as the compensation it is. Blind to the shoulders.',
  profile: {
    placements: { moving: 'AirPods in your ears', base: 'AirPod clipped at your sternum' },
    // Both frames are fixed to the body here, so the reading can be signed: flexion counts, extension does not.
    measure: { kind: 'difference', movingAxis: EAR_PITCH, baseAxis: EAR_PITCH, sign: () => NOD },
    compensation: TRUNK_LEAN,
  },
});

export const shoulderRaisePaired = imuPair({
  id: 'shoulder-raise.pair.v1', algorithmVersion: 'shoulder-raise.pair.v1',
  defaults: { restMaxDeg: 30, hysteresisDeg: 5, holdMs: 400, maxCompensationDeg: 12 },
  limits: { targetDeg: [20, 160], targetMaxDeg: [25, 175], prescribedReps: REPS, holdMs: [0, 5000], maxCompensationDeg: COMPENSATION },
  measurementNote: 'Arm elevation from an AirPod on the wrist against a second on the sternum: arm inclination minus trunk inclination, both from their calibrated rests against gravity. The lean that a single wrist sensor counts as extra reach is measured here and counted against the rep instead. Unsigned and plane-blind: it says how far, not which way, and the camera still owns direction.',
  profile: {
    // A strapped wrist AirPod's frame is not fixed to the arm, so the angle is unsigned.
    placements: { moving: 'AirPod on your wrist', base: 'AirPod clipped at your sternum' },
    measure: { kind: 'between' },
    compensation: TRUNK_LEAN,
    // How much of the reach was the trunk's: the number a therapist means by "you're leaning into it".
    metrics: (moving, base, _p: ResolvedParams) => ({
      trunkShareDeg: Math.abs(angleBetween(base.vertical, base.rest)),
    }),
  },
});

export const elbowFlexionPaired = imuPair({
  id: 'elbow-flexion.pair.v1', algorithmVersion: 'elbow-flexion.pair.v1',
  defaults: { restMaxDeg: 20, hysteresisDeg: 4, holdMs: 0, maxCompensationDeg: 15 },
  limits: { targetDeg: [30, 150], targetMaxDeg: [35, 160], prescribedReps: REPS, holdMs: [0, 5000], maxCompensationDeg: COMPENSATION },
  measurementNote: 'Elbow flexion from an AirPod in the handle against a second on the upper arm: forearm inclination minus upper-arm inclination. The upper arm drifting forward off the side — the usual way a curl is cheated — is measured here rather than assumed away. Blind to the shoulder hiking.',
  profile: {
    placements: { moving: 'AirPod in the handle', base: 'AirPod strapped to your upper arm' },
    measure: { kind: 'between' },
    // The base segment here is the upper arm, not the trunk: its own swing is the compensation.
    compensation: {},
  },
});

/** Local copy of the kernel's unsigned angle, so a profile's metrics need not import the whole module. */
function angleBetween(a: readonly number[], b: readonly number[]): number {
  const dot = a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
  const la = Math.hypot(...a), lb = Math.hypot(...b);
  if (la < 1e-9 || lb < 1e-9) return 0;
  return Math.acos(Math.max(-1, Math.min(1, dot / (la * lb)))) * 180 / Math.PI;
}

/** Every paired movement, for the registry. */
export const IMU_PAIR_KINDS = [neckFlexionPaired, shoulderRaisePaired, elbowFlexionPaired] as const;
