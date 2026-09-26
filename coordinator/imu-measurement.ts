// Deterministic rep measurement from one AirPod mounted on the handle the patient holds (Club Motion app).
// The angle is how far the handle has tilted from its calibrated rest attitude, measured against gravity, so
// turning the handle about the vertical does not count. With a straight arm the handle follows the arm (shoulder
// raise ≈ elevation); with the upper arm still it follows the forearm (curl ≈ elbow flexion). One IMU cannot
// see the trunk or other joints. These are engineering thresholds, not clinical standards.
import { angle, median, len, RepMachine, type ExerciseEvent, type Sample, type Side, type Vec } from './measurement.ts';

export const IMU_ALGORITHM_VERSION = 'imu-handle-tilt.v1';

export interface MotionSample {
  sensorTime: number;                                   // seconds, monotonic on the phone/Mac
  quaternion: [number, number, number, number];        // CoreMotion attitude [x, y, z, w]
  rotationRate: [number, number, number];              // rad/s, device frame
}
export interface ImuConfig {
  exercise: 'seated_shoulder_raise' | 'biceps_curl';
  side: Side;
  targetDeg: number;
  targetMaxDeg?: number;
  restMaxDeg?: number;
  hysteresisDeg?: number;
  holdMs?: number;
  minRepMs?: number;
  trackingGapMs?: number;       // AirPods stream ~25 Hz; a longer silence is tracking loss
  calibrationMs?: number;       // still, resting time that fixes the rest attitude
  stillRadS?: number;           // below this angular speed the handle counts as still
  prescribedReps?: number;
  planVersion?: number | null;
}

/**
 * The world's vertical axis expressed in the device frame. The quaternion convention was checked against the
 * gyro on recorded AirPod data: this form satisfies dg/dt = g × ω (residual ~9%); the other is 4× worse.
 */
export function verticalInDevice([x, y, z, w]: MotionSample['quaternion']): Vec {
  const v: Vec = [2 * (x * z - w * y), 2 * (y * z + w * x), w * w - x * x - y * y + z * z];
  const n = len(v);
  return [v[0] / n, v[1] / n, v[2] / n];
}

export class ImuRepSession {
  readonly config: Required<Omit<ImuConfig, 'targetMaxDeg' | 'prescribedReps' | 'planVersion'>> &
    Pick<ImuConfig, 'targetMaxDeg' | 'prescribedReps' | 'planVersion'>;
  readonly samples: Sample[] = [];
  private readonly reps: RepMachine;
  private restVertical: Vec | null = null;
  private calib: { start: number; v: Vec[] } | null = null;
  private t0: number | null = null;
  private lastT = -Infinity;
  private rejected = 0;

  get events() { return this.reps.events; }
  get phase() { return this.reps.state; }
  get currentRep() { return this.reps.currentRep; }

  constructor(config: ImuConfig) {
    if (!Number.isFinite(config.targetDeg) || config.targetDeg <= 0 || config.targetDeg >= 180) throw Error('targetDeg must be in (0,180)');
    this.config = { restMaxDeg: 30, hysteresisDeg: 5, holdMs: 250, minRepMs: 700, trackingGapMs: 400,
      calibrationMs: 800, stillRadS: 0.35, ...config };
    if (this.config.restMaxDeg + this.config.hysteresisDeg >= this.config.targetDeg) throw Error('targetDeg must exceed the rest band');
    this.reps = new RepMachine({ ...this.config, maxTrunkDeviationDeg: Infinity });
  }

  push(s: MotionSample): ExerciseEvent[] {
    const out: ExerciseEvent[] = [];
    const q = s.quaternion, norm = Math.hypot(...q);
    if (!Number.isFinite(s.sensorTime) || !q.every(Number.isFinite) || norm < .5 || norm > 1.5 || !s.rotationRate.every(Number.isFinite)) {
      this.rejected++; return out;
    }
    this.t0 ??= s.sensorTime;
    const t = (s.sensorTime - this.t0) * 1000;
    if (t <= this.lastT) return out;                      // stale or duplicate
    // Samples are pushed as they arrive; a gap means the stream stopped for that long.
    if (t - this.lastT > this.config.trackingGapMs && this.lastT > -Infinity) {
      if (this.reps.state === 'calibrating') this.calib = null;
      this.reps.missing(t, out);
    }
    this.lastT = t;
    const v = verticalInDevice(q), speed = Math.hypot(...s.rotationRate);
    const a = this.restVertical ? angle(this.restVertical, v) : null;
    this.samples.push({ tMs: t, valid: true, angleDeg: a, trunkDeg: null });
    this.reps.present(t, out);

    if (this.reps.state === 'calibrating') {
      // Rest attitude: arm down, handle still. Movement restarts the clock.
      if (speed > this.config.stillRadS) { this.calib = null; return out; }
      this.calib ??= { start: t, v: [] };
      this.calib.v.push(v);
      if (t - this.calib.start >= this.config.calibrationMs && this.calib.v.length >= 5) {
        const m: Vec = [0, 1, 2].map(k => median(this.calib!.v.map(x => x[k]))!) as Vec;
        const n = len(m);
        this.restVertical = [m[0] / n, m[1] / n, m[2] / n];
        this.reps.calibrated(out, { type: 'calibration.complete', tMs: t, sensor: 'imu', restVertical: this.restVertical });
      }
      return out;
    }
    this.reps.step(t, a!, 0, out);
    return out;
  }

  summary() {
    const measured = this.samples.filter(s => s.angleDeg != null).length;
    return {
      algorithmVersion: IMU_ALGORITHM_VERSION, sensor: 'imu' as const,
      exercise: this.config.exercise, side: this.config.side, planVersion: this.config.planVersion ?? null,
      config: this.config,
      calibrated: !!this.restVertical, calibration: this.restVertical ? { restVertical: this.restVertical } : null,
      ...this.reps.summary(),
      trunkDeviation: { meanDeg: null, maxDuringRepsDeg: null },
      frames: this.samples.length, validFrameRatio: this.samples.length ? measured / this.samples.length : 0,
      rejectedSamples: this.rejected,
      measurementNote: 'Estimated from the handle\'s tilt relative to the calibrated rest position (one AirPod IMU); assumes the handle moves with the limb segment. Not a goniometer reading. Symptoms are patient-reported.',
    };
  }
}
