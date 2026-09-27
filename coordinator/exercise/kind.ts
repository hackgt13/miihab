// Generic rep measurement. Nothing in this file knows about any particular exercise.
// It owns frame validation, calibration against a resting reference, the rep state machine,
// the validity rules, and the summary reduction — the parts that were entangled with
// shoulder geometry in the original measurement.ts.
//
// An ExerciseKind contributes only geometry: which landmarks must be trustworthy, and how to
// turn one frame into a primary angle plus a compensation angle. Adding an exercise must not
// require editing this file; if it does, the interface is wrong.
//
// Rep qualities (quality.ts) ride alongside: they never decide whether a rep counts, only how well
// it was made, and the engine only tells them when a rep begins, what it measures, and when it ends.

import { QualityTrack, type QualityBinding, type RepVerdict } from './quality.ts';

export type Side = 'left' | 'right';
export interface Point { x: number; y: number; z: number; visibility?: number }
export interface Frame { sourceMediaTimeMs: number; imageLandmarks: Point[]; worldLandmarks: Point[] }
export type Vec = [number, number, number];
/** 'ref' is a second IMU on the neighbouring segment (the chest under a raised arm, the upper arm above a curl). */
export type ChannelId = 'pose' | 'imu' | 'ref';

/** A mounted IMU sample, already on the shared host axis (see hostclock.ts). */
export interface ImuSample {
  quaternion: [number, number, number, number];
  rotationRate: [number, number, number];
  hostMonotonicMs: number;
}

/**
 * One step of input. The engine holds the latest of each channel and hands both to the kind, so a
 * kind never does its own fusion — per activity-plan.md, "no fusion math beyond a shared clock".
 *
 * The division that shapes this: the IMU owns magnitude (inclination from vertical is yaw-free and
 * essentially exact), and the camera owns direction and compensation. So a wrist exercise reads
 * primaryDeg from `imu` and compensationDeg from `pose`, and either channel may be absent.
 */
export interface ObservationInput {
  tMs: number;                 // step time, on whichever axis the caller drives
  pose?: Frame | null;
  imu?: ImuSample | null;
  ref?: ImuSample | null;      // the second IMU, when the kind reads one
}

/** Reasons that mean the same thing for every exercise. A kind names its own compensation reason. */
export type SharedInvalidReason = 'tracking_lost' | 'did_not_reach_target' | 'too_fast';

export interface RepParams {
  side: Side;
  targetDeg: number;             // clinician-approved band starts here
  targetMaxDeg?: number;         // optional upper bound; exceeding it is flagged, not extra credit
  restMaxDeg?: number;           // movement counts as "returned" below this
  hysteresisDeg?: number;
  holdMs?: number;               // time inside the target band to count as reached
  maxCompensationDeg?: number;
  minRepMs?: number;
  trackingGapMs?: number;
  minVisibility?: number;
  calibrationMs?: number;        // valid resting frames used to fix the reference
  prescribedReps?: number;
  planVersion?: number | null;
}
export type ResolvedParams =
  Required<Omit<RepParams, 'targetMaxDeg' | 'prescribedReps' | 'planVersion'>> &
  Pick<RepParams, 'targetMaxDeg' | 'prescribedReps' | 'planVersion'>;

/** One frame reduced to the numbers the rep machine needs. */
export interface Observation {
  primaryDeg: number;              // drives rep detection
  compensationDeg: number | null;  // null until calibrated
  scaleM: number;                  // a segment length, for implausibility rejection
  axes: Vec[];                     // vectors medianed into the reference during calibration
}
export interface Reference { axes: Vec[]; scaleM: number }

export interface ExerciseKind<R extends string = string> {
  readonly id: string;                    // 'shoulder-raise.v1'
  readonly algorithmVersion: string;
  readonly requires: readonly ChannelId[];
  readonly compensationReason: R;         // e.g. 'trunk_compensation'
  readonly compensationKey: string;       // summary key, e.g. 'trunkDeviation'
  readonly defaults: Partial<RepParams>;
  readonly limits: Readonly<Record<string, readonly [number, number]>>;
  readonly measurementNote: string;
  /** Landmark indices that must be present, in-frame and confident. Empty when pose is not used. */
  landmarks(params: ResolvedParams): readonly number[];
  /** Null when the input is not trustworthy. Receives the reference once calibrated. */
  observe(input: ObservationInput, params: ResolvedParams, reference: Reference | null): Observation | null;
  /** Human-readable calibration payload for the event and the summary. */
  calibration(reference: Reference): Record<string, unknown>;
}

export type RepEvent<R extends string = string> =
  | { type: 'calibration.complete'; tMs: number; reference: Record<string, unknown> }
  | { type: 'rep.started'; tMs: number; rep: number }
  | { type: 'target.reached'; tMs: number; rep: number; angleDeg: number }
  | { type: 'rep.completed'; tMs: number; rep: number; valid: boolean; reason: SharedInvalidReason | R | null;
      peakDeg: number; durationMs: number; compensationMaxDeg: number; aboveTargetMax: boolean; startMs: number;
      // How well it was made (quality.ts), keyed by quality id; score is null for a rep that did not count.
      quality: Record<string, RepVerdict>; score: number | null; streak: number }
  | { type: 'tracking.lost'; tMs: number; rep: number | null }
  | { type: 'tracking.recovered'; tMs: number };

export interface Sample { tMs: number; valid: boolean; angleDeg: number | null; compensationDeg: number | null }

export const sub = (a: Point, b: Point): Vec => [a.x - b.x, a.y - b.y, a.z - b.z];
export const mid = (a: Point, b: Point): Point => ({ x: (a.x + b.x) / 2, y: (a.y + b.y) / 2, z: (a.z + b.z) / 2 });
/** How far a posture may creep during calibration before the hold starts over, in degrees. */
export const CALIBRATION_DRIFT_DEG = 6;
export const len = (v: Vec) => Math.hypot(v[0], v[1], v[2]);
export const dot = (a: Vec, b: Vec) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
export const cross = (a: Vec, b: Vec): Vec =>
  [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
export const unit = (v: Vec): Vec => { const l = len(v) || 1; return [v[0] / l, v[1] / l, v[2] / l]; };
export const angle = (a: Vec, b: Vec) => {
  const d = dot(a, b) / (len(a) * len(b));
  return Math.acos(Math.max(-1, Math.min(1, d))) * 180 / Math.PI;
};
/** Component of v perpendicular to unit axis k. */
export const reject = (v: Vec, k: Vec): Vec => {
  const d = dot(v, k);
  return [v[0] - k[0] * d, v[1] - k[1] * d, v[2] - k[2] * d];
};
export const median = (xs: number[]) => {
  if (!xs.length) return null;
  const s = [...xs].sort((a, b) => a - b), m = s.length >> 1;
  return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
};

const BASE_DEFAULTS = {
  restMaxDeg: 30, hysteresisDeg: 5, holdMs: 250, maxCompensationDeg: 12, minRepMs: 700,
  trackingGapMs: 250, minVisibility: 0.5, calibrationMs: 800,
} as const;

/**
 * The rep engine. Drive it with push(frame) per pose frame; read summary() at the end.
 * Deterministic: identical frame sequences always produce identical summaries, which is what
 * makes a recorded session re-scorable when an algorithm version changes.
 */
export class RepSession<R extends string = string> {
  readonly kind: ExerciseKind<R>;
  readonly params: ResolvedParams;
  readonly events: RepEvent<R>[] = [];
  readonly samples: Sample[] = [];
  private reference: Reference | null = null;
  private calib: { start: number; axes: Vec[][]; scales: number[] } | null = null;
  private state: 'calibrating' | 'rest' | 'rep' | 'paused' = 'calibrating';
  private repCount = 0;
  private rep: { start: number; peak: number; compMax: number; heldMs: number; reached: boolean; lost: boolean; lastT: number } | null = null;
  private lastValidT = -Infinity;
  private lost = false;
  readonly qualities: QualityTrack;

  get phase() { return this.state; }
  get currentRep() { return this.state === 'rep' ? this.repCount : null; }
  get calibrated() { return !!this.reference; }
  get calibrationReference() { return this.reference; }
  /** The qualities' live readouts for the rep in progress (null between reps). */
  get live() { return this.qualities.current; }

  constructor(kind: ExerciseKind<R>, params: RepParams, qualities: QualityBinding[] = []) {
    this.kind = kind;
    if (!Number.isFinite(params.targetDeg) || params.targetDeg <= 0 || params.targetDeg >= 180)
      throw Error('targetDeg must be in (0,180)');
    this.params = { ...BASE_DEFAULTS, ...kind.defaults, ...params } as ResolvedParams;
    if (this.params.restMaxDeg + this.params.hysteresisDeg >= this.params.targetDeg)
      throw Error('targetDeg must exceed the rest band');
    this.qualities = new QualityTrack(this.params, qualities);
  }

  /** Validate every channel the kind declares, then hand the input over. No state change. */
  observeFused(input: ObservationInput): Observation | null {
    for (const channel of this.kind.requires) {
      if (channel === 'pose' && !this.poseUsable(input.pose)) return null;
      if (channel === 'imu' && !this.imuUsable(input.imu)) return null;
      if (channel === 'ref' && !this.imuUsable(input.ref)) return null;
    }
    // A channel the kind does not require may still be present and useful — a wrist exercise reads
    // compensation from pose when the camera can see the patient, and simply does without otherwise.
    const pose = this.poseUsable(input.pose) ? input.pose : null;
    const imu = this.imuUsable(input.imu) ? input.imu : null;
    const ref = this.imuUsable(input.ref) ? input.ref : null;
    return this.kind.observe({ tMs: input.tMs, pose, imu, ref }, this.params, this.reference);
  }

  private poseUsable(frame: Frame | null | undefined): boolean {
    if (!frame) return false;
    const img = frame.imageLandmarks, w = frame.worldLandmarks;
    if (!img || !w || img.length < 33 || w.length < 33) return false;
    for (const i of this.kind.landmarks(this.params)) {
      const p = img[i], q = w[i];
      if (!p || !q || !(p.visibility! >= this.params.minVisibility) ||
          p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1 ||
          ![q.x, q.y, q.z].every(Number.isFinite)) return false;
    }
    return true;
  }
  private imuUsable(sample: ImuSample | null | undefined): boolean {
    if (!sample) return false;
    if (!Array.isArray(sample.quaternion) || sample.quaternion.length !== 4) return false;
    if (!sample.quaternion.every(Number.isFinite)) return false;
    const norm = sample.quaternion.reduce((t, v) => t + v * v, 0);
    return norm > 0.5 && norm < 1.5;   // a collapsed or unnormalised attitude is not trustworthy
  }

  /** Pose-only convenience, so camera-driven callers stay unchanged. */
  observe(frame: Frame): Observation | null {
    return this.observeFused({ tMs: frame.sourceMediaTimeMs, pose: frame });
  }
  /** Pose-only convenience for a camera-driven caller. */
  push(frame: Frame): RepEvent<R>[] {
    return this.pushFused({ tMs: frame.sourceMediaTimeMs, pose: frame });
  }

  pushFused(input: ObservationInput): RepEvent<R>[] {
    const t = input.tMs, out: RepEvent<R>[] = [];
    const emit = (e: RepEvent<R>) => { out.push(e); this.events.push(e); };
    const m = this.observeFused(input);
    this.samples.push({ tMs: t, valid: !!m, angleDeg: m?.primaryDeg ?? null, compensationDeg: m?.compensationDeg ?? null });
    const c = this.params;

    if (!m) {
      if (!this.lost && t - this.lastValidT > c.trackingGapMs && this.state !== 'calibrating') {
        this.lost = true;
        emit({ type: 'tracking.lost', tMs: t, rep: this.rep ? this.repCount : null });
        if (this.rep) this.rep.lost = true;
        if (this.state === 'rest') this.state = 'paused';
      }
      if (this.state === 'calibrating' && this.calib && t - this.lastValidT > c.trackingGapMs) this.calib = null;
      return out;
    }
    if (this.lost) { this.lost = false; emit({ type: 'tracking.recovered', tMs: t }); }
    this.lastValidT = t;

    if (this.state === 'calibrating') {
      // Reference posture: the measured joint at rest. Held for calibrationMs of valid frames, and held: a posture
      // that creeps more than CALIBRATION_DRIFT_DEG from where the hold began starts the hold again, so a slowly
      // settling limb never becomes the reference.
      if (m.primaryDeg > c.restMaxDeg) { this.calib = null; return out; }
      if (this.calib && m.axes.some((axis, i) => angle(axis, this.calib!.axes[0][i]) > CALIBRATION_DRIFT_DEG)) this.calib = null;
      this.calib ??= { start: t, axes: [], scales: [] };
      this.calib.axes.push(m.axes); this.calib.scales.push(m.scaleM);
      if (t - this.calib.start >= c.calibrationMs && this.calib.axes.length >= 5) {
        const count = m.axes.length;
        const axes: Vec[] = [];
        for (let a = 0; a < count; a++)
          axes.push([0, 1, 2].map(k => median(this.calib!.axes.map(s => unit(s[a])[k]))!) as Vec);
        this.reference = { axes, scaleM: median(this.calib.scales)! };
        this.state = 'rest';
        emit({ type: 'calibration.complete', tMs: t, reference: this.kind.calibration(this.reference) });
      }
      return out;
    }

    const a = m.primaryDeg, comp = m.compensationDeg ?? 0;
    if (this.state === 'paused') {
      if (a < c.restMaxDeg) this.state = 'rest';
      return out;
    }
    if (this.state === 'rest') {
      if (a > c.restMaxDeg + c.hysteresisDeg) {
        this.repCount++; this.state = 'rep';
        this.rep = { start: t, peak: a, compMax: comp, heldMs: 0, reached: false, lost: false, lastT: t };
        this.qualities.begin(this.repCount, t, a);
        emit({ type: 'rep.started', tMs: t, rep: this.repCount });
      }
      return out;
    }
    // state === 'rep'
    const r = this.rep!;
    const dt = Math.min(t - r.lastT, c.trackingGapMs); r.lastT = t;
    r.peak = Math.max(r.peak, a); r.compMax = Math.max(r.compMax, comp);
    this.qualities.step(t, a);
    if (a >= c.targetDeg) {
      r.heldMs += dt;
      if (!r.reached && r.heldMs >= c.holdMs) {
        r.reached = true; emit({ type: 'target.reached', tMs: t, rep: this.repCount, angleDeg: a });
      }
    } else if (a < c.targetDeg - c.hysteresisDeg) r.heldMs = 0;
    if (a < c.restMaxDeg) {
      const durationMs = t - r.start;
      const reason: SharedInvalidReason | R | null =
        r.lost ? 'tracking_lost'
        : !r.reached ? 'did_not_reach_target'
        : r.compMax > c.maxCompensationDeg ? this.kind.compensationReason
        : durationMs < c.minRepMs ? 'too_fast'
        : null;
      const judged = this.qualities.finish({ valid: !reason, peakDeg: r.peak, durationMs });
      emit({ type: 'rep.completed', tMs: t, rep: this.repCount, valid: !reason, reason, peakDeg: r.peak, durationMs,
        compensationMaxDeg: r.compMax, aboveTargetMax: c.targetMaxDeg != null && r.peak > c.targetMaxDeg, startMs: r.start,
        quality: judged.quality, score: judged.score, streak: judged.streak });
      this.rep = null; this.state = 'rest';
    }
    return out;
  }

  /**
   * Canonical cross-exercise summary. The compensation block is written under the kind's own key
   * so each exercise keeps its established clinical vocabulary without the engine knowing it.
   */
  summary() {
    const reps = this.events.filter((e): e is Extract<RepEvent<R>, { type: 'rep.completed' }> => e.type === 'rep.completed');
    const valid = reps.filter(r => r.valid);
    const reasons: Record<string, number> = {};
    for (const r of reps) if (r.reason) reasons[r.reason] = (reasons[r.reason] ?? 0) + 1;
    const comp = this.samples.filter(s => s.valid && s.compensationDeg != null).map(s => s.compensationDeg!);
    const validFrames = this.samples.filter(s => s.valid).length;
    return {
      exerciseKind: this.kind.id,
      algorithmVersion: this.kind.algorithmVersion,
      side: this.params.side,
      planVersion: this.params.planVersion ?? null,
      params: this.params,
      calibrated: !!this.reference,
      calibration: this.reference ? this.kind.calibration(this.reference) : null,
      attempted: reps.length,
      valid: valid.length,
      // Reps that went above the safe ceiling (targetMaxDeg). Flagged, never extra credit.
      overshoots: this.params.targetMaxDeg != null ? reps.filter(r => r.aboveTargetMax).length : null,
      prescribed: this.params.prescribedReps ?? null,
      completed: this.params.prescribedReps != null ? Math.min(valid.length, this.params.prescribedReps) : valid.length,
      invalidReasons: reasons,
      medianValidPeakDeg: median(valid.map(r => r.peakDeg)),
      validPeaksDeg: valid.map(r => Math.round(r.peakDeg * 10) / 10),
      [this.kind.compensationKey]: {
        meanDeg: comp.length ? comp.reduce((s, x) => s + x, 0) / comp.length : null,
        maxDuringRepsDeg: reps.length ? Math.max(...reps.map(r => r.compensationMaxDeg)) : null,
      },
      trackingLossEvents: this.events.filter(e => e.type === 'tracking.lost').length,
      frames: this.samples.length,
      validFrameRatio: this.samples.length ? validFrames / this.samples.length : 0,
      reps: reps.map(r => ({ rep: r.rep, startMs: r.startMs, endMs: r.tMs, valid: r.valid, reason: r.reason,
        peakDeg: Math.round(r.peakDeg * 10) / 10,
        compensationMaxDeg: Math.round(r.compensationMaxDeg * 10) / 10,
        durationMs: r.durationMs, aboveTargetMax: r.aboveTargetMax, quality: r.quality, score: r.score })),
      // How well the counted reps were made, per quality (quality.ts). Never affects `valid`.
      quality: this.qualities.summary(),
      measurementNote: this.kind.measurementNote,
    } as Record<string, unknown>;
  }
}
