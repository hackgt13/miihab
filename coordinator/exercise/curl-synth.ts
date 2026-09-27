// A simulated biceps curl, as the two AirPods would report it: for the trajectory tests, and for the studio's
// curl-path preview when no real two-AirPod set exists yet.
//
// Each AirPod's attitude is built the way CoreMotion reports one — device to world, [x, y, z, w] — from three
// parts: a heading that drifts (AirPod yaw is arbitrary and wanders, which is why nothing may depend on it), the
// segment's tilt in and out of the curl's plane, and a fixed mount (the AirPod never sits square on the arm).
// The analysis must recover the tilts through all three.
//
// Anything this writes is simulated and says so.
//
//   node exercise/curl-synth.ts --payload    the studio's live curl path, partway through a demo set

import { writeFileSync, mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import type { MotionSample } from './trajectory.ts';

type Quat = [number, number, number, number];

const qmul = (a: Quat, b: Quat): Quat => [
  a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
  a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0],
  a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3],
  a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2],
];
const axisAngle = (x: number, y: number, z: number, deg: number): Quat => {
  const l = Math.hypot(x, y, z) || 1, h = deg * Math.PI / 360, s = Math.sin(h) / l;
  return [x * s, y * s, z * s, Math.cos(h)];
};
const minJerk = (s: number) => s * s * s * (10 - 15 * s + 6 * s * s);

/** A small deterministic generator, so a test's noise is the same every run. */
function rng(seed: number) {
  let s = seed >>> 0 || 1;
  return () => { s ^= s << 13; s ^= s >>> 17; s ^= s << 5; return ((s >>> 0) / 4294967296); };
}

export interface RepPlan {
  peak: number;          // elbow angle at the top, degrees
  swing?: number;        // upper arm swung forward at the top, degrees (the cheat)
  lateral?: number;      // forearm drifting out of the plane at the top, degrees
  hitch?: boolean;       // a catch on the way down
  raiseS?: number; holdS?: number; lowerS?: number; restS?: number;
}

export interface SynthOptions {
  reps: RepPlan[];
  hz?: number;
  noiseDeg?: number;     // sensor jitter
  headingDriftDegPerS?: number;
  seed?: number;
  startMs?: number;
}

/** Both AirPods' samples for a set of curls. The forearm is `imu`, the upper arm `ref`. */
export function synthCurl(o: SynthOptions): MotionSample[] {
  const hz = o.hz ?? 25, dt = 1 / hz, noise = o.noiseDeg ?? .5, drift = o.headingDriftDegPerS ?? 2;
  const random = rng(o.seed ?? 7), jitter = () => (random() - .5) * 2 * noise;
  // Mounts: each AirPod sits at its own odd angle on the arm, fixed for the set.
  const foreMount = qmul(axisAngle(0, 0, 1, 37), axisAngle(1, 1, 0, 12));
  const upperMount = qmul(axisAngle(0, 0, 1, -58), axisAngle(1, 0, 1, 9));
  const out: MotionSample[] = [];
  let t = 0, headingF = 20, headingU = 140;
  let lastF: Quat | null = null, lastU: Quat | null = null;

  const emit = (elbow: number, swing: number, lateral: number) => {
    headingF += drift * dt; headingU -= drift * .7 * dt;
    const foreTilt = elbow + swing;   // the forearm's tilt from hanging is the elbow angle plus the upper arm's
    // device → world: heading about world vertical, then the segment's tilt, then the mount.
    const f = qmul(qmul(axisAngle(0, 0, 1, headingF), qmul(axisAngle(0, 1, 0, foreTilt + jitter()), axisAngle(1, 0, 0, lateral + jitter()))), foreMount);
    const u = qmul(qmul(axisAngle(0, 0, 1, headingU), axisAngle(0, 1, 0, swing + jitter())), upperMount);
    const rate = (q: Quat, last: Quat | null) => {
      if (!last) return [0, 0, 0];
      const d = qmul([-last[0], -last[1], -last[2], last[3]], q);
      return [2 * d[0] / dt, 2 * d[1] / dt, 2 * d[2] / dt];
    };
    const tMs = (o.startMs ?? 0) + t * 1000;
    out.push({ role: 'imu', tMs, quaternion: f, rotationRate: rate(f, lastF) });
    out.push({ role: 'ref', tMs: tMs + 6, quaternion: u, rotationRate: rate(u, lastU) });
    lastF = f; lastU = u; t += dt;
  };
  const hold = (s: number, e = 0, sw = 0, lat = 0) => { for (let i = 0; i < Math.round(s * hz); i++) emit(e, sw, lat); };

  hold(1.5);   // still at rest: what calibration looks for
  for (const r of o.reps) {
    const up = r.raiseS ?? 1.8, top = r.holdS ?? .5, down = r.lowerS ?? 2.4, rest = r.restS ?? 1;
    const sw = r.swing ?? 0, lat = r.lateral ?? 0;
    for (let i = 1; i <= Math.round(up * hz); i++) { const s = minJerk(i / (up * hz)); emit(r.peak * s, sw * s, lat * s); }
    hold(top, r.peak, sw, lat);
    for (let i = 1; i <= Math.round(down * hz); i++) {
      const x = i / (down * hz);
      // A hitch: the lowering catches two-thirds of the way down, bounces up a little, then carries on.
      const s = r.hitch && x > .55 && x < .75 ? 1 - minJerk(.55) + .08 * Math.sin((x - .55) / .2 * Math.PI) : 1 - minJerk(x);
      emit(r.peak * s, sw * s, lat * s);
    }
    hold(rest);
  }
  return out;
}

const conj = (q: Quat): Quat => [-q[0], -q[1], -q[2], q[3]];
/** v rotated by q (device → world); with conj(q), world → device. */
function rotate(q: Quat, v: [number, number, number]): [number, number, number] {
  const r = qmul(qmul(q, [v[0], v[1], v[2], 0]), conj(q));
  return [r[0], r[1], r[2]];
}

export interface ReachSample { role: 'imu' | 'ref'; tMs: number; sensorTime: number; quaternion: Quat; rotationRate: number[]; userAcceleration: number[] }

/**
 * The calibration swing: a straight arm raised to `peak` degrees in front and lowered, with each AirPod `…DistM` from
 * the shoulder. Rates and accelerations are the exact derivatives of the swing, turned into each AirPod's own frame
 * through its mount, in g — what CMDeviceMotion reports — plus a little noise.
 */
export function synthReach(o: { upperDistM: number; foreDistM: number; peak?: number; upS?: number; downS?: number; hz?: number;
  rateNoise?: number; accelNoise?: number; seed?: number; startMs?: number }): ReachSample[] {
  const hz = o.hz ?? 25, peak = (o.peak ?? 165) * Math.PI / 180, up = o.upS ?? 2.2, down = o.downS ?? 2.4;
  const random = rng(o.seed ?? 5), noise = (k: number) => (random() - .5) * 2 * k;
  const mounts: Record<'imu' | 'ref', Quat> = {
    imu: qmul(axisAngle(0, 0, 1, 37), axisAngle(1, 1, 0, 12)),
    ref: qmul(axisAngle(0, 0, 1, -58), axisAngle(1, 0, 1, 9)),
  };
  // θ(t), θ'(t), θ''(t) for rest, a minimum-jerk raise, a short hold, a minimum-jerk lower, rest.
  const legs: [number, number, number][] = [[1, 0, 0], [up, 0, peak], [.3, peak, peak], [down, peak, 0], [1, 0, 0]];
  const out: ReachSample[] = [];
  let t0 = 0;
  for (const [T, a, b] of legs) {
    for (let i = 0; i < Math.round(T * hz); i++) {
      const u = i / (T * hz), d = b - a;
      const th = a + d * (u * u * u * (10 - 15 * u + 6 * u * u));
      const th1 = d * (30 * u * u - 60 * u ** 3 + 30 * u ** 4) / T;
      const th2 = d * (60 * u - 180 * u * u + 120 * u ** 3) / (T * T);
      const t = t0 + i / hz;
      for (const role of ['ref', 'imu'] as const) {
        const dist = role === 'ref' ? o.upperDistM : o.foreDistM;
        const q = qmul(axisAngle(0, 1, 0, th * 180 / Math.PI), mounts[role]);
        // The AirPod's point on the arm, swinging about the world Y axis through the shoulder.
        const acc: [number, number, number] = [
          dist * (th2 * -Math.cos(th) + th1 * th1 * Math.sin(th)), 0, dist * (th2 * Math.sin(th) + th1 * th1 * Math.cos(th))];
        const a = rotate(conj(q), acc), w = rotate(conj(q), [0, th1, 0]);
        out.push({ role, tMs: (o.startMs ?? 0) + t * 1000, sensorTime: t, quaternion: q,
          rotationRate: w.map(x => x + noise(o.rateNoise ?? .02)),
          userAcceleration: a.map(x => x / 9.80665 + noise(o.accelNoise ?? .01)) });
      }
    }
    t0 += T;
  }
  return out;
}

/** A demo set with a story: good reps, then a swung one, a hitch, and a tiring, rushed finish. */
export const DEMO_SET: RepPlan[] = [
  { peak: 118 }, { peak: 121 }, { peak: 119 },
  { peak: 124, swing: 22 },
  { peak: 116, hitch: true },
  { peak: 104, swing: 8 },
  { peak: 96 }, { peak: 88, raiseS: .9, lowerS: 1.1 },
];

// Write the studio's live payload as it would be partway up the rep after the swung one, for the Unity preview
// (Kinesthetic → Rehab → Show a simulated curl path). Simulated, like everything this file makes.
if (import.meta.url === `file://${process.argv[1]}` && process.argv.includes('--payload')) {
  const { CurlTrace } = await import('./curl-trace.ts');
  const all = synthCurl({ reps: DEMO_SET, seed: 11 });
  let payload = null;
  for (let ms = 23800; ms < 40000 && !payload; ms += 200) {
    const trace = new CurlTrace(110, false);
    for (const x of all.filter(x => x.tMs < ms)) trace.push(x.role, x.tMs, { quaternion: [...x.quaternion], rotationRate: [...(x.rotationRate ?? [])] });
    const p = trace.next();
    if (p && p.reps.length === 4 && p.arm && p.arm.elbowDeg > 75) payload = p;
  }
  const out = resolve(import.meta.dirname, '../../unity/KinestheticUnity/Temp/curl-payload.json');
  mkdirSync(resolve(out, '..'), { recursive: true });
  writeFileSync(out, JSON.stringify({ ...payload, simulated: true }));
  console.log(`Wrote a simulated mid-set curl path to ${out}`);
}
