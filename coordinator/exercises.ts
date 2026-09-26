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

export interface LibraryExercise {
  kind: string;                    // exercise/registry.ts id
  movement: string;                // kinds with the same movement are alternative sensors for it
  label: string;
  sensor: Sensor;
  posture: string;
  components: string[];
  cue: string;
  defaults: { targetDeg: number; ceilingMarginDeg: number; prescribedReps: number; holdMs: number; sets: number;
              loadKg: number; assistance: Assistance };
  /** Dose limits the measurement kind does not own. */
  limits: { sets: [number, number]; loadKg: [number, number] };
}

export const LIBRARY: Record<string, LibraryExercise> = {
  'arm-elevation.v1': {
    kind: 'arm-elevation.v1', movement: 'shoulder_raise', label: 'Seated shoulder raise', sensor: 'AirPod on the handle',
    posture: 'Elbow straight.',
    components: ['shoulder elevation', 'scapular control'],
    cue: 'Sit tall, straight arm, lift the handle and pause at the top. Stop at the line.',
    defaults: { targetDeg: 45, ceilingMarginDeg: 15, prescribedReps: 8, holdMs: 400, sets: 1, loadKg: 0, assistance: 'active' },
    limits: { sets: [1, 5], loadKg: [0, 5] },
    // The pause at the top is the point of a raise, and dropping the arm is the usual way to cheat it.
  },
  'elbow-flexion.v1': {
    kind: 'elbow-flexion.v1', movement: 'biceps_curl', label: 'Seated biceps curl', sensor: 'AirPod on the handle',
    posture: 'Upper arm still at the side.',
    components: ['elbow flexion', 'grip'],
    cue: 'Elbow stays by your side; curl up slowly and lower with control.',
    defaults: { targetDeg: 90, ceilingMarginDeg: 20, prescribedReps: 10, holdMs: 0, sets: 1, loadKg: 0.5, assistance: 'active' },
    limits: { sets: [1, 5], loadKg: [0, 10] },
    // A curl is judged on its lowering, not on a pause: no hold.
  },
  ...entries([
    // In-ear: put the AirPods in, that is the setup.
    { kind: 'neck-flexion.v1', movement: 'neck_flexion', label: 'Seated neck flexion', sensor: 'AirPods in your ears',
      posture: 'Sit tall; only the head moves.', components: ['cervical flexion'],
      cue: 'Sit tall and slowly bring your chin toward your chest. Pause, then return to looking ahead.',
      defaults: { targetDeg: 30, ceilingMarginDeg: 10, prescribedReps: 8, holdMs: 500 } },
    { kind: 'neck-extension.v1', movement: 'neck_extension', label: 'Seated neck extension', sensor: 'AirPods in your ears',
      posture: 'Sit tall; only the head moves.', components: ['cervical extension'],
      cue: 'Sit tall and slowly look up toward the ceiling. Pause, then return to looking ahead.',
      defaults: { targetDeg: 30, ceilingMarginDeg: 10, prescribedReps: 8, holdMs: 500 } },
    { kind: 'neck-lateral-flexion.v1', movement: 'neck_lateral_flexion', label: 'Seated neck side bend', sensor: 'AirPods in your ears',
      posture: 'Shoulders level and relaxed.', components: ['cervical lateral flexion'],
      cue: 'Keep your shoulders down and tip your ear toward your shoulder. Pause, then come back to centre.',
      defaults: { targetDeg: 20, ceilingMarginDeg: 10, prescribedReps: 8, holdMs: 500 } },
    // Wrist.
    { kind: 'shoulder-abduction.v1', movement: 'shoulder_abduction', label: 'Seated side arm raise', sensor: 'AirPod on the wrist',
      posture: 'Elbow straight, arm out to the side.', components: ['shoulder elevation', 'scapular control'],
      cue: 'Straight arm, lift it out to the side, thumb up. Pause at the line and lower slowly.',
      defaults: { targetDeg: 45, ceilingMarginDeg: 15, prescribedReps: 8, holdMs: 400 } },
    { kind: 'scaption.v1', movement: 'scaption', label: 'Seated scaption raise', sensor: 'AirPod on the wrist',
      posture: 'Elbow straight, arm halfway between forward and the side.', components: ['shoulder elevation', 'scapular control'],
      cue: 'Straight arm, thumb up, lift on a diagonal between forward and the side. Pause at the line.',
      defaults: { targetDeg: 45, ceilingMarginDeg: 15, prescribedReps: 8, holdMs: 400 } },
    { kind: 'arm-hold.v1', movement: 'arm_hold', label: 'Seated raise and hold', sensor: 'AirPod on the wrist',
      posture: 'Elbow straight.', components: ['shoulder endurance', 'scapular control'],
      cue: 'Raise your straight arm to the line and hold it there, steady, until the timer finishes.',
      defaults: { targetDeg: 45, ceilingMarginDeg: 15, prescribedReps: 3, holdMs: 5000 } },
    { kind: 'forearm-rotation.v1', movement: 'forearm_rotation', label: 'Seated forearm turn', sensor: 'AirPod on the wrist',
      posture: 'Elbow at your side, bent 90 degrees, forearm level.', components: ['forearm rotation', 'grip'],
      cue: 'Elbow at your side, thumb up. Turn your palm up or down, pause, and return to thumb up.',
      defaults: { targetDeg: 45, ceilingMarginDeg: 15, prescribedReps: 10, holdMs: 300 } },
    { kind: 'shoulder-external-rotation.v1', movement: 'shoulder_external_rotation', label: 'Shoulder rotation at 90/90', sensor: 'AirPod on the wrist',
      posture: 'Upper arm out at shoulder height, elbow bent 90 degrees.', components: ['shoulder external rotation'],
      cue: 'Arm out at shoulder height, elbow bent, hand forward. Rotate your hand up toward the ceiling, pause, and lower.',
      defaults: { targetDeg: 45, ceilingMarginDeg: 15, prescribedReps: 8, holdMs: 400 } },
    // Sternum.
    { kind: 'trunk-flexion.v1', movement: 'trunk_flexion', label: 'Seated forward bend', sensor: 'AirPod on the chest',
      posture: 'Feet flat, knees still.', components: ['trunk flexion'],
      cue: 'Sit tall, then bend forward slowly as if reaching for your toes. Pause and sit back up.',
      defaults: { targetDeg: 30, ceilingMarginDeg: 15, prescribedReps: 8, holdMs: 500 } },
    { kind: 'trunk-lateral-flexion.v1', movement: 'trunk_lateral_flexion', label: 'Seated side bend', sensor: 'AirPod on the chest',
      posture: 'Both hips on the seat; lean, do not reach.', components: ['trunk lateral flexion'],
      cue: 'Sit tall and lean to the side, keeping both hips on the seat. Pause and come back to centre.',
      defaults: { targetDeg: 15, ceilingMarginDeg: 10, prescribedReps: 8, holdMs: 500 } },
    { kind: 'trunk-extension.v1', movement: 'trunk_extension', label: 'Standing back bend', sensor: 'AirPod on the chest',
      posture: 'Hands on your hips, knees straight.', components: ['trunk extension'],
      cue: 'Hands on your hips, gently lean back. Pause and return to standing tall.',
      defaults: { targetDeg: 12, ceilingMarginDeg: 8, prescribedReps: 8, holdMs: 500 } },
    // Thigh and ankle.
    { kind: 'knee-extension.v1', movement: 'knee_extension', label: 'Seated knee straightening', sensor: 'AirPod on the ankle',
      posture: 'Thigh stays on the seat.', components: ['knee extension', 'quadriceps'],
      cue: 'Sit back, thigh on the seat. Straighten your knee to lift your foot, pause, and lower slowly.',
      defaults: { targetDeg: 45, ceilingMarginDeg: 15, prescribedReps: 10, holdMs: 500 } },
    { kind: 'straight-leg-raise.v1', movement: 'hip_flexion', label: 'Straight-leg raise', sensor: 'AirPod on the ankle',
      posture: 'Lying on your back, knee straight.', components: ['hip flexion', 'quadriceps'],
      cue: 'Lying on your back, keep the knee straight and lift the leg to the line. Pause and lower slowly.',
      defaults: { targetDeg: 30, ceilingMarginDeg: 15, prescribedReps: 10, holdMs: 500 } },
    { kind: 'hip-abduction.v1', movement: 'hip_abduction', label: 'Standing side leg raise', sensor: 'AirPod on the thigh',
      posture: 'Standing tall, holding a support; trunk upright.', components: ['hip abduction', 'balance'],
      cue: 'Hold a chair, stand tall, and lift your leg out to the side. Pause and lower slowly.',
      defaults: { targetDeg: 20, ceilingMarginDeg: 10, prescribedReps: 10, holdMs: 300 } },
    { kind: 'seated-march.v1', movement: 'seated_march', label: 'Seated march', sensor: 'AirPod on the thigh',
      posture: 'Sitting tall; lift the knee, not the trunk.', components: ['hip flexion', 'endurance'],
      cue: 'Sit tall and lift this knee up and down, as if marching. Steady rhythm.',
      defaults: { targetDeg: 15, ceilingMarginDeg: 15, prescribedReps: 20, holdMs: 0 } },
  ]),
};

/** Fill the dose defaults a new movement rarely changes, so an entry is only what makes it that movement. */
function entries(xs: (Omit<LibraryExercise, 'defaults' | 'limits'> & { defaults: Pick<LibraryExercise['defaults'],
  'targetDeg' | 'ceilingMarginDeg' | 'prescribedReps' | 'holdMs'> & Partial<LibraryExercise['defaults']> })[]) {
  return Object.fromEntries(xs.map(x => [x.kind, { ...x,
    defaults: { sets: 1, loadKg: 0, assistance: 'active' as Assistance, ...x.defaults },
    limits: { sets: [1, 5], loadKg: [0, 5] } }])) as Record<string, LibraryExercise>;
}
