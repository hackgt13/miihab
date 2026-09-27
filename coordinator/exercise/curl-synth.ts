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
    const trace = new CurlTrace(110);
    for (const x of all.filter(x => x.tMs < ms)) trace.push(x.role, x.tMs, { quaternion: [...x.quaternion], rotationRate: [...(x.rotationRate ?? [])] });
    const p = trace.next();
    if (p && p.reps.length === 4 && p.arm && p.arm.elbowDeg > 75) payload = p;
  }
  const out = resolve(import.meta.dirname, '../../unity/KinestheticUnity/Temp/curl-payload.json');
  mkdirSync(resolve(out, '..'), { recursive: true });
  writeFileSync(out, JSON.stringify({ ...payload, simulated: true }));
  console.log(`Wrote a simulated mid-set curl path to ${out}`);
}
