// The exercise catalog. One place that knows which exercises exist, so the server no longer
// hardcodes a constructor and the plan schema can key prescriptions on an exercise id.
// Registering an exercise is one import and one array entry.

import { RepSession, type ExerciseKind, type RepParams } from './kind.ts';
import { shoulderRaise } from './shoulder-raise.ts';
import { seatedTrunkRotation } from './seated-trunk-rotation.ts';
import { armElevation, elbowFlexion } from './arm-elevation.ts';

export const EXERCISES: readonly ExerciseKind<any>[] = [shoulderRaise, seatedTrunkRotation, armElevation, elbowFlexion];
export const DEFAULT_EXERCISE = shoulderRaise.id;

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
export function createSession(id: string | undefined | null, params: RepParams) {
  return new RepSession(exerciseKind(id), params);
}

export { RepSession } from './kind.ts';
export type { ExerciseKind, RepParams } from './kind.ts';
