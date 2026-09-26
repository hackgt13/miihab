// Deterministic seated shoulder-raise measurement from MediaPipe pose frames.
// Angles come from raw world landmarks (never from avatar smoothing or game assistance).
// These are engineering thresholds for the rehearsed setup, not clinical standards.

export const ALGORITHM_VERSION = 'shoulder-raise.v1';

export type Side = 'left' | 'right';
export interface Point { x: number; y: number; z: number; visibility?: number }
export interface Frame { sourceMediaTimeMs: number; imageLandmarks: Point[]; worldLandmarks: Point[] }

export interface ExerciseConfig {
  side: Side;
  targetDeg: number;            // clinician-approved band starts here
  targetMaxDeg?: number;        // optional upper bound; exceeding it is flagged, not counted as extra credit
  restMaxDeg?: number;          // arm counts as "down" below this
  hysteresisDeg?: number;
  holdMs?: number;              // time inside the target band to count as reached
  maxTrunkDeviationDeg?: number;
  minRepMs?: number;
  trackingGapMs?: number;
  minVisibility?: number;
  calibrationMs?: number;       // valid resting frames used to fix the torso reference
  prescribedReps?: number;
  planVersion?: number | null;
}

export type InvalidReason = 'tracking_lost' | 'did_not_reach_target' | 'trunk_compensation' | 'too_fast';
export type ExerciseEvent =
  | { type: 'calibration.complete'; tMs: number; torsoAxis: [number, number, number]; upperArmM: number }
  | { type: 'rep.started'; tMs: number; rep: number }
  | { type: 'target.reached'; tMs: number; rep: number; angleDeg: number }
  | { type: 'rep.completed'; tMs: number; rep: number; valid: boolean; reason: InvalidReason | null;
      peakDeg: number; durationMs: number; trunkMaxDeg: number; aboveTargetMax: boolean; startMs: number }
  | { type: 'tracking.lost'; tMs: number; rep: number | null }
  | { type: 'tracking.recovered'; tMs: number };

export interface Sample { tMs: number; valid: boolean; angleDeg: number | null; trunkDeg: number | null }

type Vec = [number, number, number];
const sub = (a: Point, b: Point): Vec => [a.x - b.x, a.y - b.y, a.z - b.z];
const mid = (a: Point, b: Point): Point => ({ x: (a.x + b.x) / 2, y: (a.y + b.y) / 2, z: (a.z + b.z) / 2 });
const len = (v: Vec) => Math.hypot(v[0], v[1], v[2]);
const angle = (a: Vec, b: Vec) => {
  const d = (a[0] * b[0] + a[1] * b[1] + a[2] * b[2]) / (len(a) * len(b));
  return Math.acos(Math.max(-1, Math.min(1, d))) * 180 / Math.PI;
};
const median = (xs: number[]) => {
  if (!xs.length) return null;
  const s = [...xs].sort((a, b) => a - b), m = s.length >> 1;
  return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
};

const IDX = { shoulder: { left: 11, right: 12 }, elbow: { left: 13, right: 14 }, hips: [23, 24], shoulders: [11, 12] };

export class ShoulderRaiseSession {
  readonly config: Required<Omit<ExerciseConfig, 'targetMaxDeg' | 'prescribedReps' | 'planVersion'>> &
    Pick<ExerciseConfig, 'targetMaxDeg' | 'prescribedReps' | 'planVersion'>;
  readonly events: ExerciseEvent[] = [];
  readonly samples: Sample[] = [];
  private torsoDown: Vec | null = null;          // calibrated shoulder-mid → hip-mid direction
  private upperArmM: number | null = null;
  private calib: { start: number; torso: Vec[]; arm: number[] } | null = null;
  private state: 'calibrating' | 'rest' | 'rep' | 'paused' = 'calibrating';
  private repCount = 0;
  private rep: { start: number; peak: number; trunkMax: number; heldMs: number; reached: boolean; lost: boolean; lastT: number } | null = null;
  private lastValidT = -Infinity;
  private lost = false;

  get phase() { return this.state; }
  get currentRep() { return this.state === 'rep' ? this.repCount : null; }

  constructor(config: ExerciseConfig) {
    if (!Number.isFinite(config.targetDeg) || config.targetDeg <= 0 || config.targetDeg >= 180) throw Error('targetDeg must be in (0,180)');
    this.config = {
      restMaxDeg: 30, hysteresisDeg: 5, holdMs: 250, maxTrunkDeviationDeg: 12, minRepMs: 700,
      trackingGapMs: 250, minVisibility: 0.5, calibrationMs: 800, ...config,
    };
    if (this.config.restMaxDeg + this.config.hysteresisDeg >= this.config.targetDeg) throw Error('targetDeg must exceed the rest band');
  }

  /** Measure one frame without changing state. Null when required landmarks are not trustworthy. */
  measure(frame: Frame): { angleDeg: number; trunkDeg: number | null; torso: Vec; upperArmM: number } | null {
    const img = frame.imageLandmarks, w = frame.worldLandmarks;
    if (!img || !w || img.length < 33 || w.length < 33) return null;
    const need = [IDX.shoulder[this.config.side], IDX.elbow[this.config.side], ...IDX.hips, ...IDX.shoulders];
    for (const i of need) {
      const p = img[i], q = w[i];
      if (!p || !q || !(p.visibility! >= this.config.minVisibility) || p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1 ||
          ![q.x, q.y, q.z].every(Number.isFinite)) return null;
    }
    const shoulder = w[IDX.shoulder[this.config.side]], elbow = w[IDX.elbow[this.config.side]];
    const upper = sub(elbow, shoulder), upperArmM = len(upper);
    if (upperArmM < 0.08) return null;
    // Occlusion can collapse a segment while confidence stays high; reject implausible lengths.
    if (this.upperArmM && (upperArmM < this.upperArmM * 0.6 || upperArmM > this.upperArmM * 1.5)) return null;
    const torso = sub(mid(w[23], w[24]), mid(w[11], w[12]));
    if (len(torso) < 0.1) return null;
    const down = this.torsoDown ?? torso;
    return { angleDeg: angle(upper, down), trunkDeg: this.torsoDown ? angle(torso, this.torsoDown) : null, torso, upperArmM };
  }

  push(frame: Frame): ExerciseEvent[] {
    const t = frame.sourceMediaTimeMs, out: ExerciseEvent[] = [];
    const emit = (e: ExerciseEvent) => { out.push(e); this.events.push(e); };
    const m = this.measure(frame);
    this.samples.push({ tMs: t, valid: !!m, angleDeg: m?.angleDeg ?? null, trunkDeg: m?.trunkDeg ?? null });
    const c = this.config;

    if (!m) {
      if (!this.lost && t - this.lastValidT > c.trackingGapMs && this.state !== 'calibrating') {
        this.lost = true;
        emit({ type: 'tracking.lost', tMs: t, rep: this.rep ? this.repCount : null });
        if (this.rep) { this.rep.lost = true; }
        if (this.state === 'rest') this.state = 'paused';
      }
      if (this.state === 'calibrating' && this.calib && t - this.lastValidT > c.trackingGapMs) this.calib = null;
      return out;
    }
    if (this.lost) { this.lost = false; emit({ type: 'tracking.recovered', tMs: t }); }
    this.lastValidT = t;

    if (this.state === 'calibrating') {
      // Reference posture: arm resting down, torso upright. Uses the uncalibrated torso as "down".
      if (m.angleDeg > c.restMaxDeg) { this.calib = null; return out; }
      this.calib ??= { start: t, torso: [], arm: [] };
      this.calib.torso.push(m.torso); this.calib.arm.push(m.upperArmM);
      if (t - this.calib.start >= c.calibrationMs && this.calib.torso.length >= 5) {
        const axis: Vec = [0, 1, 2].map(k => median(this.calib!.torso.map(v => v[k] / len(v)))!) as Vec;
        this.torsoDown = axis; this.upperArmM = median(this.calib.arm);
        this.state = 'rest';
        emit({ type: 'calibration.complete', tMs: t, torsoAxis: axis, upperArmM: this.upperArmM! });
      }
      return out;
    }

    const a = m.angleDeg, trunk = m.trunkDeg ?? 0;
    if (this.state === 'paused') {
      if (a < c.restMaxDeg) this.state = 'rest';
      return out;
    }
    if (this.state === 'rest') {
      if (a > c.restMaxDeg + c.hysteresisDeg) {
        this.repCount++; this.state = 'rep';
        this.rep = { start: t, peak: a, trunkMax: trunk, heldMs: 0, reached: false, lost: false, lastT: t };
        emit({ type: 'rep.started', tMs: t, rep: this.repCount });
      }
      return out;
    }
    // state === 'rep'
    const r = this.rep!;
    const dt = Math.min(t - r.lastT, c.trackingGapMs); r.lastT = t;
    r.peak = Math.max(r.peak, a); r.trunkMax = Math.max(r.trunkMax, trunk);
    if (a >= c.targetDeg) {
      r.heldMs += dt;
      if (!r.reached && r.heldMs >= c.holdMs) { r.reached = true; emit({ type: 'target.reached', tMs: t, rep: this.repCount, angleDeg: a }); }
    } else if (a < c.targetDeg - c.hysteresisDeg) r.heldMs = 0;
    if (a < c.restMaxDeg) {
      const durationMs = t - r.start;
      const reason: InvalidReason | null = r.lost ? 'tracking_lost' : !r.reached ? 'did_not_reach_target'
        : r.trunkMax > c.maxTrunkDeviationDeg ? 'trunk_compensation' : durationMs < c.minRepMs ? 'too_fast' : null;
      emit({ type: 'rep.completed', tMs: t, rep: this.repCount, valid: !reason, reason, peakDeg: r.peak, durationMs,
        trunkMaxDeg: r.trunkMax, aboveTargetMax: c.targetMaxDeg != null && r.peak > c.targetMaxDeg, startMs: r.start });
      this.rep = null; this.state = 'rest';
    }
    return out;
  }

  summary() {
    const reps = this.events.filter((e): e is Extract<ExerciseEvent, { type: 'rep.completed' }> => e.type === 'rep.completed');
    const valid = reps.filter(r => r.valid);
    const reasons: Record<string, number> = {};
    for (const r of reps) if (r.reason) reasons[r.reason] = (reasons[r.reason] ?? 0) + 1;
    const trunk = this.samples.filter(s => s.valid && s.trunkDeg != null).map(s => s.trunkDeg!);
    const validFrames = this.samples.filter(s => s.valid).length;
    return {
      algorithmVersion: ALGORITHM_VERSION,
      exercise: 'seated_shoulder_raise', side: this.config.side, planVersion: this.config.planVersion ?? null,
      config: this.config,
      calibrated: !!this.torsoDown, calibration: this.torsoDown ? { torsoAxis: this.torsoDown, upperArmM: this.upperArmM } : null,
      attempted: reps.length, valid: valid.length,
      prescribed: this.config.prescribedReps ?? null,
      completed: this.config.prescribedReps != null ? Math.min(valid.length, this.config.prescribedReps) : valid.length,
      invalidReasons: reasons,
      medianValidPeakDeg: median(valid.map(r => r.peakDeg)),
      validPeaksDeg: valid.map(r => Math.round(r.peakDeg * 10) / 10),
      trunkDeviation: { meanDeg: trunk.length ? trunk.reduce((s, x) => s + x, 0) / trunk.length : null,
        maxDuringRepsDeg: reps.length ? Math.max(...reps.map(r => r.trunkMaxDeg)) : null },
      trackingLossEvents: this.events.filter(e => e.type === 'tracking.lost').length,
      frames: this.samples.length, validFrameRatio: this.samples.length ? validFrames / this.samples.length : 0,
      reps: reps.map(r => ({ rep: r.rep, startMs: r.startMs, endMs: r.tMs, valid: r.valid, reason: r.reason,
        peakDeg: Math.round(r.peakDeg * 10) / 10, trunkMaxDeg: Math.round(r.trunkMaxDeg * 10) / 10, durationMs: r.durationMs })),
      measurementNote: 'Estimated shoulder elevation from monocular pose; not a goniometer reading. Symptoms are patient-reported.',
    };
  }
}
