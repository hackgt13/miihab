// The curl's trajectory while the set is still going, for the studio to draw beside the patient's arm.
//
// server.ts feeds every AirPod sample the assigner has named (imu = forearm, ref = upper arm) and asks, twice a
// second, for what to broadcast on /exercise as `exercise.trajectory`. That is trajectory.ts run over the set so
// far: re-running it whole is a few thousand frames at most, and it lets the curl's plane and the rest pose settle
// as the set goes on rather than freezing whatever the first second happened to look like.
//
// The payload is shaped for drawing, not for review: the rep in progress at full rate, the last few finished
// reps thinned, the ideal arc, the arm right now, and the scores. Presentation only — the rep engine counts.

import { analyseCurl, type MotionSample, type RepScore, type TrajectoryPoint } from './trajectory.ts';
import type { Vec } from './kind.ts';

const RECENT_REPS = 3, RECENT_POINTS = 40, LIVE_POINTS = 160;

export interface TracePayload {
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

  constructor(targetDeg: number) { this.targetDeg = targetDeg; }

  push(role: 'imu' | 'ref', tMs: number, sample: { quaternion?: number[]; rotationRate?: number[] }) {
    if (!Array.isArray(sample.quaternion) || sample.quaternion.length !== 4) return;
    this.samples.push({ role, tMs, quaternion: sample.quaternion, rotationRate: sample.rotationRate });
    this.changed = true;
  }

  /** What to draw now, or null when nothing new has arrived since the last time it was asked. */
  next(force = false): TracePayload | null {
    if (!this.changed && !force) return null;
    this.changed = false;
    const t = analyseCurl(this.samples, { targetDeg: this.targetDeg });
    if (!t.points.length) return null;
    const lastEnd = t.reps.length ? t.reps[t.reps.length - 1].end : -Infinity;
    const live = t.points.filter(p => p.t > lastEnd).slice(-LIVE_POINTS).map(p => p.wrist);
    const recent = t.reps.slice(-RECENT_REPS).map(r => ({
      rep: r.rep, score: r.score, path: thin(t.points.filter(p => p.rep === r.rep), RECENT_POINTS),
    }));
    const now = t.points[t.points.length - 1];
    return {
      live, recent, ideal: t.ideal,
      arm: { elbow: now.elbowPos, wrist: now.wrist, elbowDeg: now.elbow, deviationCm: now.deviationCm },
      reps: t.reps.map(r => ({ rep: r.rep, peakElbowDeg: r.peakElbowDeg, pathAccuracy: r.pathAccuracy,
        smoothness: r.smoothness, elbowDriftDeg: r.elbowDriftDeg, score: r.score })),
      set: t.set,
      meta: { upperArmM: t.meta.upperArmM, forearmM: t.meta.forearmM, targetDeg: t.meta.targetDeg },
    };
  }
}
