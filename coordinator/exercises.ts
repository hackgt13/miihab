// The clinical catalog over the measurement kinds in exercise/registry.ts. A kind says how a movement
// is measured; this says what a clinician is choosing when they build a plan from a patient's goal
// (e.g. golf with a friend): the movement, which goal components it trains, the sensor, the cue, and
// defaults for a new prescription and its progression envelope. Kinds that measure one movement with
// different sensors share a `movement`. Only IMU kinds are offered: camera (MediaPipe) measurement is
// off for now, so the camera kinds in the registry are not in this catalog.

export type Assistance = 'assisted' | 'active' | 'resisted';

export interface LibraryExercise {
  kind: string;                    // exercise/registry.ts id
  movement: string;                // kinds with the same movement are alternative sensors for it
  label: string;
  sensor: 'AirPod on the handle';
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
    components: ['shoulder elevation', 'scapular control'],
    cue: 'Sit tall, straight arm, lift the handle and pause at the top. Stop at the line.',
    defaults: { targetDeg: 45, ceilingMarginDeg: 15, prescribedReps: 8, holdMs: 400, sets: 1, loadKg: 0, assistance: 'active' },
    limits: { sets: [1, 5], loadKg: [0, 5] },
  },
  'elbow-flexion.v1': {
    kind: 'elbow-flexion.v1', movement: 'biceps_curl', label: 'Seated biceps curl', sensor: 'AirPod on the handle',
    components: ['elbow flexion', 'grip'],
    cue: 'Elbow stays by your side; curl up slowly and lower with control.',
    defaults: { targetDeg: 90, ceilingMarginDeg: 20, prescribedReps: 10, holdMs: 0, sets: 1, loadKg: 0.5, assistance: 'active' },
    limits: { sets: [1, 5], loadKg: [0, 10] },
  },
};
