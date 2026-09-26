// Deterministic seated shoulder-raise measurement from MediaPipe pose frames.
//
// The engine moved to exercise/kind.ts (generic rep measurement) and exercise/shoulder-raise.ts
// (this exercise's geometry). This module stays as the published entry point so existing callers
// — server.ts, measure-capture.ts, synthetic-pose.ts and the tests — keep importing one name.

export { ALGORITHM_VERSION, ShoulderRaiseSession } from './exercise/shoulder-raise.ts';
export type {
  ExerciseConfig, ExerciseEvent, Frame, InvalidReason, Point, Sample, Side, Vec,
} from './exercise/shoulder-raise.ts';
