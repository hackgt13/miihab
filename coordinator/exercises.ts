// The clinical catalog over the measurement kinds in exercise/registry.ts. A kind says how a movement
// is measured; this says what a clinician is choosing when they build a plan from a patient's goal
// (e.g. golf with a friend): the movement, which goal components it trains, the sensor, the cue, and
// defaults for a new prescription and its progression envelope. Kinds that measure one movement with
// different sensors share a `movement`. Only IMU kinds are offered: camera (MediaPipe) measurement is
// off for now, so the camera kinds in the registry are not in this catalog. `posture` is the assumption
// the single-IMU reading depends on (activity-library.md): say it to the patient, or the number is wrong.

export type Assistance = 'assisted' | 'active' | 'resisted';
export type Sensor = 'AirPod on the handle' | 'AirPod on the wrist' | 'AirPods in your ears' | 'AirPod on the chest'
  | 'AirPod on the thigh' | 'AirPod on the ankle';

/**
 * Which AirPod pair measures each mount. Two pairs, two jobs: the club AirPod (Club Motion app, the relay's /golf
 * stream) lives in a handle — it clips from the golf club into a dumbbell for curls; the strap AirPod (Bowling
 * Motion app, /bowling-motion) is worn on the body — the wrist for arm raises and bowling, or the chest, thigh,
 * ankle or ears. A prescription carries its source as params.imuSource, filled from here when not set.
 */
export type ImuSource = 'club' | 'wrist';
export const SOURCE_OF: Readonly<Record<Sensor, ImuSource>> = {
  'AirPod on the handle': 'club',
  'AirPod on the wrist': 'wrist',
  'AirPods in your ears': 'wrist',
  'AirPod on the chest': 'wrist',
  'AirPod on the thigh': 'wrist',
  'AirPod on the ankle': 'wrist',
};

/** Where each mount goes, said to the patient before anything else: the reading is only as good as the strap. */
export const WEAR: Readonly<Record<Sensor, string>> = {
  'AirPod on the handle': 'Hold the handle with the AirPod secured to it.',
  'AirPod on the wrist': 'Strap the AirPod to the back of this wrist.',
  'AirPods in your ears': 'Put both AirPods in your ears.',
  'AirPod on the chest': 'Strap the AirPod flat on your breastbone.',
  'AirPod on the thigh': 'Strap the AirPod to the front of this thigh, just above the knee.',
  'AirPod on the ankle': 'Strap the AirPod to the front of this shin, just above the ankle.',
};

/**
 * How the movement looks on a body, so the avatar, its mirror ghosts and the target band all draw the joint that
 * is actually measured. Anatomical, not a rig: directions are in the patient's own frame — x out to the working
 * side, y up, z forward — and Unity maps them onto whatever skeleton it has (PoseRig.ApplyMovement).
 *
 * The segment points along `rest` at 0 degrees and sweeps toward `toward` at 90, so the drawn angle IS the
 * measured one. With `roll` the segment stays along `rest` and turns about itself; `toward` is then the thumb.
 * `hold` places the other segments the posture needs (the upper arm out for 90/90, both legs straight to stand).
 */
export type BodySegment = 'arm' | 'forearm' | 'head' | 'trunk' | 'thigh' | 'shank' | 'leg';
export type BodyVec = [number, number, number];
export interface BodyModel {
  segment: BodySegment;
  rest: BodyVec;
  toward: BodyVec;
  roll?: boolean;
  hold?: Partial<Record<'upperArm' | 'forearm' | 'thigh' | 'shank' | 'legs', BodyVec>>;
}
const DOWN: BodyVec = [0, -1, 0], UP: BodyVec = [0, 1, 0], FORWARD: BodyVec = [0, 0, 1], BACK: BodyVec = [0, 0, -1], OUT: BodyVec = [1, 0, 0];
/** 30 degrees forward of the side: the plane a seated raise is drawn in. */
const SCAPULAR: BodyVec = [0.866, 0, 0.5];
const AT_SIDE: BodyVec = [0.12, -1, 0];
const STANDING = { legs: DOWN };

export interface LibraryExercise {
  kind: string;                    // exercise/registry.ts id
  movement: string;                // kinds with the same movement are alternative sensors for it
  label: string;
  sensor: Sensor;
  posture: string;
  body: BodyModel;
  components: string[];
  cue: string;
  defaults: { targetDeg: number; ceilingMarginDeg: number; prescribedReps: number; holdMs: number; sets: number;
              loadKg: number; assistance: Assistance };
  /** Dose limits the measurement kind does not own. */
  limits: { sets: [number, number]; loadKg: [number, number] };
  /** The rep qualities this movement is coached on (exercise/quality.ts ids); absent means all of them. Their configs are plan params. */
  qualities?: string[];
}

export const LIBRARY: Record<string, LibraryExercise> = {
  'arm-elevation.v1': {
    // Worn on the wrist strap (the same AirPod as bowling), so the club AirPod stays in the dumbbell for curls.
    kind: 'arm-elevation.v1', movement: 'shoulder_raise', label: 'Seated shoulder raise', sensor: 'AirPod on the wrist',
    posture: 'Elbow straight.', body: { segment: 'arm', rest: DOWN, toward: SCAPULAR },
    components: ['shoulder elevation', 'scapular control'],
    cue: 'Sit tall, straight arm, lift your arm and pause at the top. Stop at the line.',
    defaults: { targetDeg: 45, ceilingMarginDeg: 15, prescribedReps: 8, holdMs: 400, sets: 1, loadKg: 0, assistance: 'active' },
    limits: { sets: [1, 5], loadKg: [0, 5] },
    // The pause at the top is the point of a raise, and dropping the arm is the usual way to cheat it.
    // Trajectory is on the raise first: it is the movement with the clearest prescribed shape.
    qualities: ['hold', 'tempo', 'control', 'consistency', 'trajectory'],
  },
  'elbow-flexion.v1': {
    kind: 'elbow-flexion.v1', movement: 'biceps_curl', label: 'Seated biceps curl', sensor: 'AirPod on the handle',
    posture: 'Upper arm still at the side.', body: { segment: 'forearm', rest: DOWN, toward: FORWARD, hold: { upperArm: AT_SIDE } },
    components: ['elbow flexion', 'grip'],
    cue: 'Elbow stays by your side; curl up slowly and lower with control.',
    defaults: { targetDeg: 90, ceilingMarginDeg: 20, prescribedReps: 10, holdMs: 0, sets: 1, loadKg: 0.5, assistance: 'active' },
    limits: { sets: [1, 5], loadKg: [0, 10] },
    // A curl is judged on its lowering, not on a pause: no hold.
    qualities: ['tempo', 'control', 'consistency'],
  },
  ...entries([
    // In-ear: put the AirPods in, that is the setup.
    { kind: 'neck-flexion.v1', movement: 'neck_flexion', label: 'Seated neck flexion', sensor: 'AirPods in your ears',
      posture: 'Sit tall; only the head moves.', body: { segment: 'head', rest: UP, toward: FORWARD }, components: ['cervical flexion'],
      cue: 'Sit tall and slowly bring your chin toward your chest. Pause, then return to looking ahead.',
      defaults: { targetDeg: 30, ceilingMarginDeg: 10, prescribedReps: 8, holdMs: 500 } },
    { kind: 'neck-extension.v1', movement: 'neck_extension', label: 'Seated neck extension', sensor: 'AirPods in your ears',
      posture: 'Sit tall; only the head moves.', body: { segment: 'head', rest: UP, toward: BACK }, components: ['cervical extension'],
      cue: 'Sit tall and slowly look up toward the ceiling. Pause, then return to looking ahead.',
      defaults: { targetDeg: 30, ceilingMarginDeg: 10, prescribedReps: 8, holdMs: 500 } },
    { kind: 'neck-lateral-flexion.v1', movement: 'neck_lateral_flexion', label: 'Seated neck side bend', sensor: 'AirPods in your ears',
      posture: 'Shoulders level and relaxed.', body: { segment: 'head', rest: UP, toward: OUT }, components: ['cervical lateral flexion'],
      cue: 'Keep your shoulders down and tip your ear toward your shoulder. Pause, then come back to centre.',
      defaults: { targetDeg: 20, ceilingMarginDeg: 10, prescribedReps: 8, holdMs: 500 } },
    // Wrist.
    { kind: 'shoulder-abduction.v1', movement: 'shoulder_abduction', label: 'Seated side arm raise', sensor: 'AirPod on the wrist',
      posture: 'Elbow straight, arm out to the side.', body: { segment: 'arm', rest: DOWN, toward: OUT }, components: ['shoulder elevation', 'scapular control'],
      cue: 'Straight arm, lift it out to the side, thumb up. Pause at the line and lower slowly.',
      defaults: { targetDeg: 45, ceilingMarginDeg: 15, prescribedReps: 8, holdMs: 400 } },
    { kind: 'scaption.v1', movement: 'scaption', label: 'Seated scaption raise', sensor: 'AirPod on the wrist',
      posture: 'Elbow straight, arm halfway between forward and the side.', body: { segment: 'arm', rest: DOWN, toward: [0.707, 0, 0.707] }, components: ['shoulder elevation', 'scapular control'],
      cue: 'Straight arm, thumb up, lift on a diagonal between forward and the side. Pause at the line.',
      defaults: { targetDeg: 45, ceilingMarginDeg: 15, prescribedReps: 8, holdMs: 400 } },
    { kind: 'arm-hold.v1', movement: 'arm_hold', label: 'Seated raise and hold', sensor: 'AirPod on the wrist',
      posture: 'Elbow straight.', body: { segment: 'arm', rest: DOWN, toward: SCAPULAR }, components: ['shoulder endurance', 'scapular control'],
      cue: 'Raise your straight arm to the line and hold it there, steady, until the timer finishes.',
      defaults: { targetDeg: 45, ceilingMarginDeg: 15, prescribedReps: 3, holdMs: 5000 } },
    { kind: 'forearm-rotation.v1', movement: 'forearm_rotation', label: 'Seated forearm turn', sensor: 'AirPod on the wrist',
      posture: 'Elbow at your side, bent 90 degrees, forearm level.', body: { segment: 'forearm', rest: FORWARD, toward: UP, roll: true, hold: { upperArm: AT_SIDE } }, components: ['forearm rotation', 'grip'],
      cue: 'Elbow at your side, thumb up. Turn your palm up or down, pause, and return to thumb up.',
      defaults: { targetDeg: 45, ceilingMarginDeg: 15, prescribedReps: 10, holdMs: 300 } },
    { kind: 'shoulder-external-rotation.v1', movement: 'shoulder_external_rotation', label: 'Shoulder rotation at 90/90', sensor: 'AirPod on the wrist',
      posture: 'Upper arm out at shoulder height, elbow bent 90 degrees.', body: { segment: 'forearm', rest: FORWARD, toward: UP, hold: { upperArm: OUT } }, components: ['shoulder external rotation'],
      cue: 'Arm out at shoulder height, elbow bent, hand forward. Rotate your hand up toward the ceiling, pause, and lower.',
      defaults: { targetDeg: 45, ceilingMarginDeg: 15, prescribedReps: 8, holdMs: 400 } },
    // Sternum.
    { kind: 'trunk-flexion.v1', movement: 'trunk_flexion', label: 'Seated forward bend', sensor: 'AirPod on the chest',
      posture: 'Feet flat, knees still.', body: { segment: 'trunk', rest: UP, toward: FORWARD }, components: ['trunk flexion'],
      cue: 'Sit tall, then bend forward slowly as if reaching for your toes. Pause and sit back up.',
      defaults: { targetDeg: 30, ceilingMarginDeg: 15, prescribedReps: 8, holdMs: 500 } },
    { kind: 'trunk-lateral-flexion.v1', movement: 'trunk_lateral_flexion', label: 'Seated side bend', sensor: 'AirPod on the chest',
      posture: 'Both hips on the seat; lean, do not reach.', body: { segment: 'trunk', rest: UP, toward: OUT }, components: ['trunk lateral flexion'],
      cue: 'Sit tall and lean to the side, keeping both hips on the seat. Pause and come back to centre.',
      defaults: { targetDeg: 15, ceilingMarginDeg: 10, prescribedReps: 8, holdMs: 500 } },
    { kind: 'trunk-extension.v1', movement: 'trunk_extension', label: 'Standing back bend', sensor: 'AirPod on the chest',
      posture: 'Hands on your hips, knees straight.', body: { segment: 'trunk', rest: UP, toward: BACK, hold: STANDING }, components: ['trunk extension'],
      cue: 'Hands on your hips, gently lean back. Pause and return to standing tall.',
      defaults: { targetDeg: 12, ceilingMarginDeg: 8, prescribedReps: 8, holdMs: 500 } },
    // Thigh and ankle.
    { kind: 'knee-extension.v1', movement: 'knee_extension', label: 'Seated knee straightening', sensor: 'AirPod on the ankle',
      posture: 'Thigh stays on the seat.', body: { segment: 'shank', rest: DOWN, toward: FORWARD }, components: ['knee extension', 'quadriceps'],
      cue: 'Sit back, thigh on the seat. Straighten your knee to lift your foot, pause, and lower slowly.',
      defaults: { targetDeg: 45, ceilingMarginDeg: 15, prescribedReps: 10, holdMs: 500 } },
    // Done lying on the back, drawn upright: the avatar sits, and the hip angle between trunk and leg is the same.
    { kind: 'straight-leg-raise.v1', movement: 'hip_flexion', label: 'Straight-leg raise', sensor: 'AirPod on the ankle',
      posture: 'Lying on your back, knee straight.', body: { segment: 'leg', rest: DOWN, toward: FORWARD, hold: STANDING }, components: ['hip flexion', 'quadriceps'],
      cue: 'Lying on your back, keep the knee straight and lift the leg to the line. Pause and lower slowly.',
      defaults: { targetDeg: 30, ceilingMarginDeg: 15, prescribedReps: 10, holdMs: 500 } },
    { kind: 'hip-abduction.v1', movement: 'hip_abduction', label: 'Standing side leg raise', sensor: 'AirPod on the thigh',
      posture: 'Standing tall, holding a support; trunk upright.', body: { segment: 'leg', rest: DOWN, toward: OUT, hold: STANDING }, components: ['hip abduction', 'balance'],
      cue: 'Hold a chair, stand tall, and lift your leg out to the side. Pause and lower slowly.',
      defaults: { targetDeg: 20, ceilingMarginDeg: 10, prescribedReps: 10, holdMs: 300 } },
    { kind: 'seated-march.v1', movement: 'seated_march', label: 'Seated march', sensor: 'AirPod on the thigh',
      posture: 'Sitting tall; lift the knee, not the trunk.', body: { segment: 'thigh', rest: FORWARD, toward: UP, hold: { shank: DOWN } }, components: ['hip flexion', 'endurance'],
      cue: 'Sit tall and lift this knee up and down, as if marching. Steady rhythm.',
      defaults: { targetDeg: 15, ceilingMarginDeg: 15, prescribedReps: 20, holdMs: 0 },
      // A quick rhythm by design: no hold, and "slower on the way down" would coach against it.
      qualities: ['control', 'consistency'] },
  ]),
};

/** Fill the dose defaults a new movement rarely changes, so an entry is only what makes it that movement. */
function entries(xs: (Omit<LibraryExercise, 'defaults' | 'limits'> & { defaults: Pick<LibraryExercise['defaults'],
  'targetDeg' | 'ceilingMarginDeg' | 'prescribedReps' | 'holdMs'> & Partial<LibraryExercise['defaults']> })[]) {
  return Object.fromEntries(xs.map(x => [x.kind, { ...x,
    defaults: { sets: 1, loadKg: 0, assistance: 'active' as Assistance, ...x.defaults },
    limits: { sets: [1, 5], loadKg: [0, 5] } }])) as Record<string, LibraryExercise>;
}
