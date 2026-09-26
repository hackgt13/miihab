// The exercise catalog. One place that knows which exercises exist, so the server no longer
// hardcodes a constructor and the plan schema can key prescriptions on an exercise id.
// Registering an exercise is one import and one array entry.

import { RepSession, type ExerciseKind, type RepParams } from './kind.ts';
import { bindQualities } from './quality.ts';
import { shoulderRaise } from './shoulder-raise.ts';
import { seatedTrunkRotation } from './seated-trunk-rotation.ts';
import { armElevation, elbowFlexion } from './arm-elevation.ts';
import { IMU_LIBRARY } from './imu-library.ts';
import { TWO_IMU } from './two-imu.ts';

export const EXERCISES: readonly ExerciseKind<any>[] = [shoulderRaise, seatedTrunkRotation, armElevation, elbowFlexion, ...IMU_LIBRARY, ...TWO_IMU];
// New rehab prescriptions are measured by the AirPod. The camera kinds stay registered so plans and
// sessions recorded with them still read, but camera measurement is off (see server.ts).
export const DEFAULT_EXERCISE = armElevation.id;

/** Plan schema v1 names the exercise with a free string; v2 will key on the id directly. */
const LEGACY_PLAN_TYPE: Readonly<Record<string, string>> = { seated_shoulder_raise: shoulderRaise.id };

export function exerciseKind(id: string | undefined | null): ExerciseKind<any> {
  const found = EXERCISES.find(e => e.id === id);
  if (!found) throw Error(`Unknown exercise "${id}". Known: ${EXERCISES.map(e => e.id).join(', ')}`);
  return found;
}
export function exerciseKindForPlanType(type: string | undefined | null): ExerciseKind<any> {
  return exerciseKind(LEGACY_PLAN_TYPE[String(type)] ?? type ?? DEFAULT_EXERCISE);
}
/**
 * A measured session, judged on the exercise's rep qualities. `qualityParams` are the prescription's
 * params: any that name a quality config key (holdTargetMs, lowerMs, …) tune that quality.
 */
export function createSession(id: string | undefined | null, params: RepParams, qualityParams: Record<string, unknown> = {}) {
  const kind = exerciseKind(id);
  return new RepSession(kind, params, bindQualities(kind.id, qualityParams));
}

export { RepSession } from './kind.ts';
export type { ExerciseKind, RepParams } from './kind.ts';
export { QUALITIES, bindQualities, qualityIdsFor, qualityLimits } from './quality.ts';
