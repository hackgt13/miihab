// Exercise library: what the clinician can put in a plan. Each entry says what is measured, with which sensor,
// the safe limits a plan must stay within, and defaults for a new plan exercise. A patient's goal (e.g. golf)
// is broken into components; the clinician picks exercises that train those components.

export type Metric = 'shoulder_elevation_deg' | 'elbow_flexion_deg';
export type Sensor = 'imu' | 'pose';
export type Assistance = 'assisted' | 'active' | 'resisted';

export interface LibraryExercise {
  type: string;
  label: string;
  metric: Metric;
  sensors: Sensor[];
  components: string[];            // goal components this trains, e.g. 'shoulder elevation'
  restMaxDeg: number;              // below this the limb is at rest; a rep must return here
  limits: { targetDeg: [number, number]; maxSafeDeg: [number, number]; prescribedReps: [number, number];
            sets: [number, number]; holdMs: [number, number]; loadKg: [number, number] };
  defaults: { targetDeg: number; ceilingMarginDeg: number; prescribedReps: number; sets: number; holdMs: number;
              loadKg: number; assistance: Assistance; sensor: Sensor };
  cue: string;
}

export const LIBRARY: Record<string, LibraryExercise> = {
  seated_shoulder_raise: {
    type: 'seated_shoulder_raise', label: 'Seated shoulder raise', metric: 'shoulder_elevation_deg',
    sensors: ['imu', 'pose'], components: ['shoulder elevation', 'scapular control'], restMaxDeg: 30,
    // Above ~120° the shoulder of a wheelchair user is at impingement risk; plans cap below that.
    limits: { targetDeg: [35, 120], maxSafeDeg: [40, 130], prescribedReps: [1, 30], sets: [1, 5], holdMs: [0, 3000], loadKg: [0, 5] },
    defaults: { targetDeg: 45, ceilingMarginDeg: 15, prescribedReps: 8, sets: 1, holdMs: 400, loadKg: 0, assistance: 'active', sensor: 'imu' },
    cue: 'Sit tall, chest forward, lift with a straight arm and pause at the top.',
  },
  biceps_curl: {
    type: 'biceps_curl', label: 'Seated biceps curl', metric: 'elbow_flexion_deg',
    sensors: ['imu'], components: ['elbow flexion', 'grip'], restMaxDeg: 20,
    limits: { targetDeg: [30, 140], maxSafeDeg: [40, 150], prescribedReps: [1, 30], sets: [1, 5], holdMs: [0, 2000], loadKg: [0, 10] },
    defaults: { targetDeg: 90, ceilingMarginDeg: 20, prescribedReps: 10, sets: 1, holdMs: 0, loadKg: 0.5, assistance: 'active', sensor: 'imu' },
    cue: 'Elbow stays by your side; curl up slowly and lower with control.',
  },
};

export function libraryEntry(type: string): LibraryExercise {
  const entry = LIBRARY[type];
  if (!entry) throw Error(`Unknown exercise type "${type}". Known: ${Object.keys(LIBRARY).join(', ')}.`);
  return entry;
}
