// The curl's trajectory while the set is still going, for the studio to draw beside the patient's arm.
//
// server.ts feeds every AirPod sample the assigner has named (imu = forearm, ref = upper arm) and asks, ten times a
// second, for what to broadcast on /exercise as `exercise.trajectory`. That is trajectory.ts run over the set so
// far: re-running it whole is a few thousand frames at most, and it lets the curl's plane and the rest pose settle
// as the set goes on rather than freezing whatever the first second happened to look like.
//
// The payload is shaped for drawing, not for review: the rep in progress at full rate, the last few finished
// reps thinned, the ideal arc, the arm right now, and the scores. Presentation only — the rep engine counts.
//
// Before the first rep, the arm is measured (arm-length.ts): the patient swings the straight arm up to the ceiling
// and back down, and each AirPod's acceleration against its rotation says how far it sits from the shoulder. While
// that runs, `calibrating` is true and server.ts keeps these samples from the rep engine, which would read the swing
// as a failed curl. It ends when the arm has gone up past 110° and come back down; a swing that does not fit, an
// AirPod app that sends no acceleration, or 25 seconds without a swing all fall back to an average adult arm, and
// the payload says which.

import { analyseCurl, type MotionSample, type RepScore, type TrajectoryPoint } from './trajectory.ts';
import { angle, type Vec } from './kind.ts';
import { verticalInDevice } from './imu-tilt.ts';
import { radiusFromSwing, armFromRadii, fitOk, type SwingSample } from './arm-length.ts';

const REACH_UP_DEG = 110, REACH_DOWN_DEG = 30, REACH_TIMEOUT_MS = 25000, NO_ACCEL_MS = 1000;
const DEFAULT_ARM = { upperArmM: 0.29, forearmM: 0.26 };

export interface Calibration {
  state: 'reach' | 'measured' | 'average';
  instruction: string;
  upperArmM: number;
  forearmM: number;
}

const RECENT_REPS = 3, RECENT_POINTS = 40, LIVE_POINTS = 160;

export interface TracePayload {
  calibration: Calibration;
  live: Vec[];                   // the wrist since the last rep ended: the rep being made, or the rest between
  recent: { rep: number; score: number; path: Vec[] }[];   // the last few finished reps, oldest first
  ideal: Vec[];
  arm: { elbow: Vec; wrist: Vec; elbowDeg: number; deviationCm: number } | null;
  reps: Pick<RepScore, 'rep' | 'peakElbowDeg' | 'pathAccuracy' | 'smoothness' | 'elbowDriftDeg' | 'score'>[];
  set: { reps: number; pathAccuracy: number; range: number; smoothness: number; stability: number; consistency: number; score: number } | null;
  meta: { upperArmM: number; forearmM: number; targetDeg: number };
}

const thin = (points: TrajectoryPoint[], n: number): Vec[] => {
  if (points.length <= n) return points.map(p => p.wrist);
  const step = (points.length - 1) / (n - 1);
  return Array.from({ length: n }, (_, i) => points[Math.round(i * step)].wrist);
};

export class CurlTrace {
  private samples: MotionSample[] = [];
  private targetDeg: number;
  private changed = false;
  private calibration: Calibration = { state: 'reach', instruction: 'Measure your arm: keep it straight, raise it up to the ceiling, then all the way down.', ...DEFAULT_ARM };
  private swing: Record<'imu' | 'ref', SwingSample[]> = { imu: [], ref: [] };
  private upperRest: Vec | null = null;
  private restSeen: Vec[] = [];
  private raised = false;
  private reachStartMs: number | null = null;
  private accelSeen = false;
  private held: MotionSample[] = [];   // what arrived during the reach step, in case it was not a reach after all

  constructor(targetDeg: number, measure = true) {
    this.targetDeg = targetDeg;
    if (!measure) this.calibration = { state: 'average', instruction: 'Using an average arm.', ...DEFAULT_ARM };
  }

  /**
   * True while the arm is being measured: these samples are the calibration swing, not a curl, and server.ts keeps
   * them from the rep engine. Only once acceleration is arriving — without it nothing can be measured, so an older
   * AirPod app never holds the engine back.
   */
  get calibrating() { return this.calibration.state === 'reach' && this.accelSeen; }
  private get reaching() { return this.calibration.state === 'reach'; }

  push(role: 'imu' | 'ref', tMs: number, sample: { quaternion?: number[]; rotationRate?: number[]; userAcceleration?: number[]; sensorTime?: number }) {
    if (!Array.isArray(sample.quaternion) || sample.quaternion.length !== 4) return;
    this.changed = true;
    if (this.reaching) { this.reach(role, tMs, sample); return; }
    this.samples.push({ role, tMs, quaternion: sample.quaternion, rotationRate: sample.rotationRate });
  }

  private reach(role: 'imu' | 'ref', tMs: number, s: { quaternion?: number[]; rotationRate?: number[]; userAcceleration?: number[]; sensorTime?: number }) {
    this.reachStartMs ??= tMs;
    this.held.push({ role, tMs, quaternion: s.quaternion!, rotationRate: s.rotationRate });
    const accel = Array.isArray(s.userAcceleration) && s.userAcceleration.length === 3 ? s.userAcceleration as Vec : null;
    if (accel) this.accelSeen = true;
    if (!this.accelSeen && tMs - this.reachStartMs > NO_ACCEL_MS)
      return this.settle(null, 'Your AirPod app does not send acceleration yet, so an average arm is used. Update the motion app to measure yours.');
    if (tMs - this.reachStartMs > REACH_TIMEOUT_MS) return this.settle(null, 'No arm swing seen, so an average arm is used.');
    if (accel && Array.isArray(s.rotationRate) && s.rotationRate.length === 3)
      this.swing[role].push({ t: Number.isFinite(s.sensorTime) ? s.sensorTime! : tMs / 1000, rate: s.rotationRate as Vec, accel });
    // Up and back down, read from the upper arm's tilt against its first still moments.
    if (role !== 'ref') return;
    const v = verticalInDevice(s.quaternion!);
    if (!this.upperRest) {
      const still = !s.rotationRate || Math.hypot(...s.rotationRate) < 0.35;
      if (still) this.restSeen.push(v); else this.restSeen = [];
      if (this.restSeen.length >= 8) {
        const m = this.restSeen.reduce((a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]] as Vec, [0, 0, 0] as Vec);
        const l = Math.hypot(...m); this.upperRest = [m[0] / l, m[1] / l, m[2] / l];
      }
      return;
    }
    const tilt = angle(this.upperRest, v);
    if (tilt > REACH_UP_DEG) this.raised = true;
    if (this.raised && tilt < REACH_DOWN_DEG) this.solve();
  }

  private solve() {
    const upper = radiusFromSwing(this.swing.ref), fore = radiusFromSwing(this.swing.imu);
    if (!upper || !fore || !fitOk(upper.fit) || !fitOk(fore.fit))
      return this.settle(null, 'That swing was hard to read, so an average arm is used. Keep the elbow straight next time.');
    const arm = armFromRadii(upper.radiusM, fore.radiusM);
    if (!arm) return this.settle(null, 'Those lengths do not look like an arm (is each AirPod a hand-width above its joint?), so an average arm is used.');
    this.settle(arm, '');
  }

  private settle(arm: { upperArmM: number; forearmM: number } | null, why: string) {
    const cm = (m: number) => Math.round(m * 100);
    this.calibration = arm
      ? { state: 'measured', instruction: `Arm measured: upper arm ${cm(arm.upperArmM)} cm, forearm ${cm(arm.forearmM)} cm.`, ...arm }
      : { state: 'average', instruction: why, ...DEFAULT_ARM };
    // A measured swing was a reach, not a curl; anything else that arrived may have been the first reps.
    if (!arm) this.samples.push(...this.held);
    this.held = []; this.swing = { imu: [], ref: [] };
    this.changed = true;
  }

  /** What to draw now, or null when nothing new has arrived since the last time it was asked. */
  next(force = false): TracePayload | null {
    if (!this.changed && !force) return null;
    this.changed = false;
    const blank = { live: [], recent: [], ideal: [], arm: null, reps: [], set: null,
      meta: { upperArmM: this.calibration.upperArmM, forearmM: this.calibration.forearmM, targetDeg: this.targetDeg } };
    if (this.reaching) return { calibration: this.calibration, ...blank };
    const t = analyseCurl(this.samples, { targetDeg: this.targetDeg, upperArmM: this.calibration.upperArmM, forearmM: this.calibration.forearmM });
    if (!t.points.length) return { calibration: this.calibration, ...blank };
    const lastEnd = t.reps.length ? t.reps[t.reps.length - 1].end : -Infinity;
    const live = t.points.filter(p => p.t > lastEnd).slice(-LIVE_POINTS).map(p => p.wrist);
    const recent = t.reps.slice(-RECENT_REPS).map(r => ({
      rep: r.rep, score: r.score, path: thin(t.points.filter(p => p.rep === r.rep), RECENT_POINTS),
    }));
    const now = t.points[t.points.length - 1];
    return {
      calibration: this.calibration,
      live, recent, ideal: t.ideal,
      arm: { elbow: now.elbowPos, wrist: now.wrist, elbowDeg: now.elbow, deviationCm: now.deviationCm },
      reps: t.reps.map(r => ({ rep: r.rep, peakElbowDeg: r.peakElbowDeg, pathAccuracy: r.pathAccuracy,
        smoothness: r.smoothness, elbowDriftDeg: r.elbowDriftDeg, score: r.score })),
      set: t.set,
      meta: { upperArmM: t.meta.upperArmM, forearmM: t.meta.forearmM, targetDeg: t.meta.targetDeg },
    };
  }
}
