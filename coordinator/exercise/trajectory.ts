// A biceps curl's path through space, rebuilt from its two AirPods, and scored against the ideal curl.
//
// What the data can honestly give. Each AirPod reads the world vertical in its own frame (verticalInDevice), so
// each tells how its segment is tilted against gravity — never which way it faces, since AirPod yaw is arbitrary
// and drifts, and two pairs never share a heading. A curl moves both segments in one plane (the upper arm's small
// swing is in the same plane as the forearm's lift), so the two tilts can be laid into one body frame:
//
//   x  forward, the way the hand lifts         y  up         z  out of the curl's plane (sideways drift)
//
// The curl's plane is found from the data, not from how the AirPod was strapped: the forearm's rotation axis is
// the direction its vertical turns about over the set. In-plane tilt is the signed rotation about that axis;
// out-of-plane is how far the vertical leaves the plane — and only the part gravity can see: a forearm drifting
// sideways by φ at in-plane angle θ moves its vertical by asin(cos θ · sin φ), all of it hanging and none of it
// horizontal, where sideways is the same as a turn of heading and no AirPod can tell. `lateral` is that
// observable part, never a guess at the rest, and it is measured against the set's own plane: a curl done
// consistently a little across the body defines the plane, so what shows is a rep that left the usual one. Segment lengths are assumptions (an adult arm, stated
// in `meta`), so positions are in metres of a model arm, and every score below is a ratio that does not depend
// on them being exact.
//
// The ideal curl: the upper arm hangs still and the wrist sweeps a circle about the elbow, in the plane. Scores:
//   path accuracy   how close the wrist stays to that circle, frame by frame (Gaussian, sigma 4 cm)
//   range           peak elbow angle against the plan's target
//   smoothness      how well each lift and each lower fits a minimum-jerk curve (R²) — the classic model of a
//                   smooth, practised human movement (Flash & Hogan 1985); hitches and jerks lower it
//   stability       how still the upper arm stayed (a forward swing turns a curl into a front raise)
//   consistency     how alike the reps' peaks were across the set
//
// Presentation and review only. The rep engine (two-imu.ts) decides what counts; this never changes a count.

import { cross, dot, len, unit, type Vec } from './kind.ts';
import { verticalInDevice, signedTiltAbout } from './imu-tilt.ts';

export interface MotionSample {
  role: 'imu' | 'ref';           // imu: the forearm (wrist AirPod), ref: the upper arm
  tMs: number;
  quaternion: readonly number[];
  rotationRate?: readonly number[];
}

export interface TrajectoryOptions {
  upperArmM?: number;
  forearmM?: number;
  targetDeg?: number;            // the plan's elbow target; range is scored against it
}

export interface TrajectoryPoint {
  t: number;                     // seconds from the first paired frame
  elbow: number;                 // elbow angle, degrees (forearm tilt minus upper-arm tilt)
  upper: number;                 // upper-arm swing in the plane, degrees (0 = hanging)
  lateral: number;               // forearm out of the plane, degrees
  speed: number;                 // elbow angular speed, degrees per second
  wrist: Vec;                    // metres, shoulder at the origin
  elbowPos: Vec;
  deviationCm: number;           // distance from the ideal circle
  rep: number;                   // 1-based rep this frame belongs to, 0 between reps
}

export interface RepScore {
  rep: number;
  start: number; end: number;    // seconds
  peakElbowDeg: number;
  raiseS: number; lowerS: number;
  meanDeviationCm: number; maxDeviationCm: number;
  elbowDriftDeg: number;         // largest upper-arm swing during the rep
  lateralDeg: number;            // largest out-of-plane drift of the forearm
  pathAccuracy: number;          // 0–100
  range: number;                 // 0–100
  smoothness: number;            // 0–100
  stability: number;             // 0–100
  score: number;                 // 0–100
}

export interface Trajectory {
  points: TrajectoryPoint[];
  ideal: Vec[];                  // the ideal wrist circle, from hanging to the furthest the set went
  reps: RepScore[];
  set: { reps: number; pathAccuracy: number; range: number; smoothness: number; stability: number; consistency: number; score: number } | null;
  meta: { upperArmM: number; forearmM: number; targetDeg: number; frames: number; hz: number; sigmaCm: number };
}

const SIGMA_M = 0.04;
const REP_START_DEG = 25, REP_END_DEG = 15, MIN_REP_S = 0.8;
const PAIR_MS = 150, MAX_POINTS = 1500;

const deg = (r: number) => r * 180 / Math.PI;
const rad = (d: number) => d * Math.PI / 180;
const clamp = (x: number, lo = 0, hi = 100) => Math.max(lo, Math.min(hi, x));
const mean = (xs: number[]) => xs.length ? xs.reduce((a, b) => a + b, 0) / xs.length : 0;
const round = (x: number, n = 1) => Math.round(x * 10 ** n) / 10 ** n;

/** A segment hanging down, turned forward by `inPlane` degrees and out of the plane by `outOfPlane`. */
function direction(inPlane: number, outOfPlane: number): Vec {
  const t = rad(inPlane), p = rad(outOfPlane);
  return [Math.sin(t) * Math.cos(p), -Math.cos(t) * Math.cos(p), Math.sin(p)];
}

/** The minimum-jerk profile from 0 to 1 over normalised time. */
const minJerk = (s: number) => s * s * s * (10 - 15 * s + 6 * s * s);

/** How well a stretch of angles fits a minimum-jerk move between its ends: R², clamped to 0–1. */
function minJerkFit(values: number[]): number {
  if (values.length < 4) return 1;
  const a = values[0], b = values[values.length - 1], n = values.length - 1;
  const m = mean(values);
  let res = 0, tot = 0;
  values.forEach((v, i) => { const f = a + (b - a) * minJerk(i / n); res += (v - f) ** 2; tot += (v - m) ** 2; });
  return tot < 1e-9 ? 1 : Math.max(0, 1 - res / tot);
}

/** Tilt of one segment against its rest, split into in-plane (about k) and out-of-plane. */
function tilts(rest: Vec, verticals: Vec[], k: Vec) {
  const restOut = Math.asin(Math.max(-1, Math.min(1, dot(rest, k))));
  return verticals.map(v => ({
    inPlane: signedTiltAbout(rest, v, k),
    outOfPlane: deg(Math.asin(Math.max(-1, Math.min(1, dot(v, k)))) - restOut),
  }));
}

/** The rotation axis a segment's vertical turned about over the set, in its own device frame. */
function planeAxis(rest: Vec, verticals: Vec[]): Vec | null {
  const sum: Vec = [0, 0, 0];
  for (const v of verticals) { const c = cross(rest, v); sum[0] += c[0]; sum[1] += c[1]; sum[2] += c[2]; }
  return len(sum) / Math.max(1, verticals.length) < 0.02 ? null : unit(sum);   // under ~1° on average: it did not move
}

/** The resting vertical: the stillest half-second in the first three, averaged. */
function restOf(verticals: Vec[], rates: number[], times: number[]): Vec {
  const t0 = times[0];
  let best = 0, bestScore = Infinity;
  const window = Math.max(3, times.findIndex(t => t - t0 >= 500));
  for (let i = 0; i + window < verticals.length && times[i] - t0 < 3000; i++) {
    let s = 0; for (let j = i; j < i + window; j++) s += rates[j];
    if (s < bestScore) { bestScore = s; best = i; }
  }
  const sum: Vec = [0, 0, 0];
  for (let j = best; j < Math.min(verticals.length, best + window); j++) { sum[0] += verticals[j][0]; sum[1] += verticals[j][1]; sum[2] += verticals[j][2]; }
  return unit(sum);
}

/** Pair each forearm frame with the upper-arm frame nearest it in time, within PAIR_MS. */
function pair(samples: MotionSample[]) {
  const imu = samples.filter(s => s.role === 'imu').sort((a, b) => a.tMs - b.tMs);
  const ref = samples.filter(s => s.role === 'ref').sort((a, b) => a.tMs - b.tMs);
  const pairs: { t: number; fore: MotionSample; upper: MotionSample }[] = [];
  let j = 0;
  for (const f of imu) {
    while (j + 1 < ref.length && Math.abs(ref[j + 1].tMs - f.tMs) <= Math.abs(ref[j].tMs - f.tMs)) j++;
    if (ref.length && Math.abs(ref[j].tMs - f.tMs) <= PAIR_MS) pairs.push({ t: f.tMs, fore: f, upper: ref[j] });
  }
  return pairs;
}

export function analyseCurl(samples: MotionSample[], options: TrajectoryOptions = {}): Trajectory {
  const Lu = options.upperArmM ?? 0.29, Lf = options.forearmM ?? 0.26, target = options.targetDeg ?? 110;
  const pairs = pair(samples);
  const empty: Trajectory = { points: [], ideal: [], reps: [], set: null,
    meta: { upperArmM: Lu, forearmM: Lf, targetDeg: target, frames: 0, hz: 0, sigmaCm: SIGMA_M * 100 } };
  if (pairs.length < 10) return empty;

  const times = pairs.map(p => p.t);
  const rate = (s: MotionSample) => s.rotationRate ? Math.hypot(...s.rotationRate) : 0;
  const foreV = pairs.map(p => verticalInDevice(p.fore.quaternion));
  const upperV = pairs.map(p => verticalInDevice(p.upper.quaternion));
  const foreRest = restOf(foreV, pairs.map(p => rate(p.fore) + rate(p.upper)), times);
  const upperRest = restOf(upperV, pairs.map(p => rate(p.fore) + rate(p.upper)), times);

  const kf = planeAxis(foreRest, foreV);
  if (!kf) return { ...empty, meta: { ...empty.meta, frames: pairs.length } };
  const fore = tilts(foreRest, foreV, kf);
  // The upper arm's own axis, when it moved at all; signed so a swing that goes with the lift is positive.
  const ku = planeAxis(upperRest, upperV);
  let upper = ku ? tilts(upperRest, upperV, ku) : upperV.map(() => ({ inPlane: 0, outOfPlane: 0 }));
  if (ku) {
    const corr = upper.reduce((s, u, i) => s + u.inPlane * fore[i].inPlane, 0);
    if (corr < 0) upper = upper.map(u => ({ inPlane: -u.inPlane, outOfPlane: -u.outOfPlane }));
  }

  // Positions, the ideal circle's centre at the hanging elbow.
  const centreY = -Lu;
  const t0 = times[0];
  const frames = pairs.map((_, i) => {
    const elbowPos = direction(upper[i].inPlane, upper[i].outOfPlane).map(c => c * Lu) as Vec;
    const f = direction(fore[i].inPlane, fore[i].outOfPlane);
    const wrist: Vec = [elbowPos[0] + f[0] * Lf, elbowPos[1] + f[1] * Lf, elbowPos[2] + f[2] * Lf];
    const inPlaneR = Math.hypot(wrist[0], wrist[1] - centreY);
    const deviation = Math.hypot(inPlaneR - Lf, wrist[2]);
    return { t: (times[i] - t0) / 1000, elbow: fore[i].inPlane - upper[i].inPlane, upper: upper[i].inPlane,
      lateral: fore[i].outOfPlane, wrist, elbowPos, deviation };
  });

  // Reps from the elbow angle, with hysteresis.
  const spans: [number, number][] = [];
  let open = -1;
  frames.forEach((f, i) => {
    if (open < 0 && f.elbow > REP_START_DEG) { open = i; while (open > 0 && frames[open - 1].elbow > REP_END_DEG) open--; }
    else if (open >= 0 && f.elbow < REP_END_DEG) { if (f.t - frames[open].t >= MIN_REP_S) spans.push([open, i]); open = -1; }
  });

  const reps: RepScore[] = spans.map(([s, e], r) => {
    const slice = frames.slice(s, e + 1);
    let peak = s; for (let i = s; i <= e; i++) if (frames[i].elbow > frames[peak].elbow) peak = i;
    const devs = slice.map(f => f.deviation);
    const pathAccuracy = 100 * mean(devs.map(d => Math.exp(-((d / SIGMA_M) ** 2))));
    const peakElbow = frames[peak].elbow;
    const range = clamp(100 * peakElbow / target);
    function peakElbowAt(i: number) { return frames[i].elbow; }
    // The lift ends, and the lower begins, where the arm is within 5% of its peak: a hold at the top is neither.
    let top = s; while (top < peak && frames[top].elbow < .95 * peakElbowAt(peak)) top++;
    let leave = e; while (leave > peak && frames[leave].elbow < .95 * peakElbowAt(peak)) leave--;
    const smoothness = 100 * (minJerkFit(frames.slice(s, top + 1).map(f => f.elbow)) + minJerkFit(frames.slice(leave, e + 1).map(f => f.elbow))) / 2;
    const drift = Math.max(...slice.map(f => Math.abs(f.upper)));
    const lateral = Math.max(...slice.map(f => Math.abs(f.lateral)));
    const stability = clamp(100 * (1 - drift / 30));
    const score = 0.4 * pathAccuracy + 0.25 * range + 0.2 * smoothness + 0.15 * stability;
    return {
      rep: r + 1, start: round(frames[s].t, 2), end: round(frames[e].t, 2), peakElbowDeg: round(peakElbow),
      raiseS: round(frames[top].t - frames[s].t, 2), lowerS: round(frames[e].t - frames[leave].t, 2),
      meanDeviationCm: round(100 * mean(devs)), maxDeviationCm: round(100 * Math.max(...devs)),
      elbowDriftDeg: round(drift), lateralDeg: round(lateral),
      pathAccuracy: round(pathAccuracy), range: round(range), smoothness: round(smoothness),
      stability: round(stability), score: round(score),
    };
  });

  let set: Trajectory['set'] = null;
  if (reps.length) {
    const peaks = reps.map(r => r.peakElbowDeg), m = mean(peaks);
    const sd = Math.sqrt(mean(peaks.map(p => (p - m) ** 2)));
    const consistency = clamp(100 * (1 - sd / Math.max(1, m) * 4));   // a 25% spread of peaks is zero
    const avg = (k: keyof RepScore) => round(mean(reps.map(r => r[k] as number)));
    set = { reps: reps.length, pathAccuracy: avg('pathAccuracy'), range: avg('range'), smoothness: avg('smoothness'),
      stability: avg('stability'), consistency: round(consistency),
      score: round(0.9 * mean(reps.map(r => r.score)) + 0.1 * consistency) };
  }

  const repOf = new Int32Array(frames.length);
  spans.forEach(([s, e], r) => { for (let i = s; i <= e; i++) repOf[i] = r + 1; });
  const step = Math.max(1, Math.ceil(frames.length / MAX_POINTS));
  const points: TrajectoryPoint[] = [];
  for (let i = 0; i < frames.length; i += step) {
    const f = frames[i], g = frames[Math.min(frames.length - 1, i + 1)], h = frames[Math.max(0, i - 1)];
    const dt = g.t - h.t;
    points.push({ t: round(f.t, 3), elbow: round(f.elbow), upper: round(f.upper), lateral: round(f.lateral),
      speed: round(dt > 0 ? (g.elbow - h.elbow) / dt : 0),
      wrist: f.wrist.map(c => round(c, 4)) as Vec, elbowPos: f.elbowPos.map(c => round(c, 4)) as Vec,
      deviationCm: round(f.deviation * 100), rep: repOf[i] });
  }

  const reach = Math.max(target, ...frames.map(f => f.elbow));
  const ideal: Vec[] = [];
  for (let i = 0; i <= 48; i++) {
    const a = rad(reach * i / 48);
    ideal.push([round(Math.sin(a) * Lf, 4), round(centreY - Math.cos(a) * Lf, 4), 0]);
  }

  const hz = pairs.length > 1 ? (pairs.length - 1) / ((times[times.length - 1] - t0) / 1000) : 0;
  return { points, ideal, reps, set,
    meta: { upperArmM: Lu, forearmM: Lf, targetDeg: target, frames: pairs.length, hz: round(hz), sigmaCm: SIGMA_M * 100 } };
}
