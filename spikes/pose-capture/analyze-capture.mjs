// Usage: node analyze-capture.mjs /absolute/path/to/export.json
// Reads local data only; emits aggregate diagnostics, never raw landmarks/device IDs.
import { readFile } from 'node:fs/promises';
import { basename } from 'node:path';
import { createHash } from 'node:crypto';
import { assessFraming, EXERCISE_VIEWS, FRAMING_RULE } from './framing.mjs';

const input = process.argv[2];
if (!input) throw new Error('Provide the path to a Kinesthetic capture JSON.');
const bytes = await readFile(input);
const capture = JSON.parse(bytes);
if (capture.schemaVersion !== 'kinesthetic.pose-capture.v1' || !capture.frames?.length) {
  throw new Error('Expected a nonempty kinesthetic.pose-capture.v1 export.');
}
const frames = capture.frames;
const round = value => Number.isFinite(value) ? Math.round(value * 100) / 100 : null;
function distribution(values) {
  const sorted = values.filter(Number.isFinite).sort((a, b) => a - b);
  if (!sorted.length) return null;
  const percentile = p => {
    const at = (sorted.length - 1) * p;
    const lower = Math.floor(at), upper = Math.ceil(at);
    return sorted[lower] + (sorted[upper] - sorted[lower]) * (at - lower);
  };
  return { count: sorted.length, min: round(sorted[0]), median: round(percentile(.5)),
    p95: round(percentile(.95)), max: round(sorted.at(-1)) };
}
const differences = key => frames.slice(1).map((frame, i) => frame[key] - frames[i][key]);
const framing = frames.map(frame => assessFraming(frame.imageLandmarks));
const views = Object.fromEntries(EXERCISE_VIEWS.map(view => {
  let validFrames = 0, currentRun = 0, longestRun = 0;
  const missing = {};
  for (const frame of framing) {
    const state = frame[view.id];
    if (state.jointsInView) {
      validFrames++;
      longestRun = Math.max(longestRun, ++currentRun);
    } else {
      currentRun = 0;
      for (const item of state.missing) {
        const key = `${item.joint}: ${item.reason}`;
        missing[key] = (missing[key] ?? 0) + 1;
      }
    }
  }
  return [view.id, { label: view.label, jointsInViewFrames: validFrames,
    percent: round(validFrames / frames.length * 100), longestRunFrames: longestRun,
    missingJointFrameCounts: missing }];
}));
const sourceGaps = differences('sourceMediaTimeMs');
const observationGaps = differences('observedAtMonotonicMs');
const warmSpan = frames.at(-1).sourceMediaTimeMs - frames[1]?.sourceMediaTimeMs;
const result = {
  input: basename(input), sha256: createHash('sha256').update(bytes).digest('hex'),
  schemaVersion: capture.schemaVersion, sessionID: capture.sessionID,
  runtime: capture.runtime ?? 'MediaPipe Tasks Vision (browser)',
  source: { kind: capture.source?.kind, label: capture.source?.label,
    width: capture.width, height: capture.height, delegate: capture.delegate },
  frames: frames.length,
  subjectDetectedFrames: frames.filter(frame => frame.subjectDetected).length,
  contiguousFrameIDs: frames.every((frame, i) => frame.frameID === i),
  frameShapesConsistent: frames.every(frame => frame.imageLandmarks?.length === (frame.subjectDetected ? 33 : 0)
    && frame.worldLandmarks?.length === (frame.subjectDetected ? 33 : 0)),
  nonfiniteRequiredCoordinates: frames.reduce((n, frame) => n +
    ['imageLandmarks', 'worldLandmarks'].reduce((m, key) => m + frame[key].reduce((k, point) =>
      k + ['x', 'y', 'z'].filter(axis => !Number.isFinite(point[axis])).length, 0), 0), 0),
  sourceSpanMs: round(frames.at(-1).sourceMediaTimeMs - frames[0].sourceMediaTimeMs),
  sourceGapsMs: distribution(sourceGaps),
  observationGapsMs: distribution(observationGaps),
  nonIncreasingSourceTimes: sourceGaps.filter(value => value <= 0).length,
  nonIncreasingObservationTimes: observationGaps.filter(value => value <= 0).length,
  firstInferenceMs: round(frames[0].inferenceDurationMs),
  inferenceMs: distribution(frames.map(frame => frame.inferenceDurationMs)),
  inferenceAfterFirstMs: distribution(frames.slice(1).map(frame => frame.inferenceDurationMs)),
  // Counts inference samples across the source timeline, not end-to-end latency.
  samplesPerSecondAfterFirst: warmSpan > 0 && frames.length > 2 ? round((frames.length - 2) * 1000 / warmSpan) : null,
  framingRule: FRAMING_RULE,
  exerciseFraming: views,
  limitations: [
    'Detection and visibility scores are not accuracy measurements.',
    'Left/right are model labels; anatomical side requires visual verification against the source video.',
    'Framing pass does not validate 3D coordinates, calibration, ROM, or repetitions.',
    'Timing describes this inference run, not camera-to-display latency.',
  ],
};
process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
