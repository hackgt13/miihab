// How long the patient's arm is, from one straight-arm swing up to the ceiling and back down.
//
// An AirPod measures how it turns (rotation rate ω) and how it is pushed (acceleration a, gravity removed), both in
// its own frame and from the same instant. Swing a straight arm about the shoulder and every point on it moves on a
// circle about the shoulder, so a point at r from the shoulder (r in the AirPod's frame, fixed, since the AirPod is
// strapped to the arm) accelerates at
//
//     a = α × r + ω × (ω × r)          (tangential + centripetal; α = dω/dt)
//
// which is linear in r. Every sample of the swing is three equations in the same three unknowns, so a least-squares
// fit over the swing gives r, and |r| is how far that AirPod sits from the shoulder. Only a rotation about a fixed
// point fits this well; the fit's R² says how well it did, and a poor fit is refused rather than believed.
//
// The shoulder is not a perfect hinge (the shoulder blade rides along in a full raise), so |r| is the distance to
// the swing's effective centre — which is what the curl's model arm wants anyway.
//
// From the two AirPods to the two segments, with each strapped a hand-width (HAND_M) above its own joint — the
// catalog's placement for the curl: forearm AirPod a hand-width above the wrist, upper-arm AirPod a hand-width above
// the elbow —
//     r_upper = upper arm − hand        r_fore = upper arm + forearm − hand
//  so upper arm = r_upper + hand and forearm = r_fore − r_upper, where the hand-width cancels.

import { cross, len, type Vec } from './kind.ts';

export const HAND_M = 0.08;
const G = 9.80665;
const MIN_RATE = 0.5;        // rad/s: slower than this, the centripetal term is too small to read over noise
const MIN_FIT = 0.5;

export interface SwingSample { t: number; rate: Vec; accel: Vec }   // seconds (sensor clock), rad/s, g

const sub = (a: Vec, b: Vec): Vec => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const scale = (a: Vec, k: number): Vec => [a[0] * k, a[1] * k, a[2] * k];

/** M such that M·r = α × r + ω × (ω × r). */
function matrix(alpha: Vec, w: Vec): number[][] {
  const e = (i: number): Vec => [i === 0 ? 1 : 0, i === 1 ? 1 : 0, i === 2 ? 1 : 0];
  const cols = [0, 1, 2].map(i => { const r = e(i); const t = cross(alpha, r), c = cross(w, cross(w, r)); return [t[0] + c[0], t[1] + c[1], t[2] + c[2]]; });
  return [0, 1, 2].map(row => cols.map(col => col[row]));
}

function solve3(A: number[][], b: number[]): Vec | null {
  const det = (m: number[][]) => m[0][0] * (m[1][1] * m[2][2] - m[1][2] * m[2][1]) - m[0][1] * (m[1][0] * m[2][2] - m[1][2] * m[2][0]) + m[0][2] * (m[1][0] * m[2][1] - m[1][1] * m[2][0]);
  const d = det(A);
  if (Math.abs(d) < 1e-9) return null;
  return [0, 1, 2].map(k => det(A.map((row, i) => row.map((v, j) => j === k ? b[i] : v))) / d) as Vec;
}

/** How far this AirPod sat from the centre it swung about, and how well a rigid swing explains its readings. */
export function radiusFromSwing(samples: SwingSample[]): { radiusM: number; fit: number } | null {
  const s = [...samples].sort((a, b) => a.t - b.t);
  if (s.length < 12) return null;
  // Rates smoothed over three samples before they are differentiated: α is noisier than anything else here.
  const w = s.map((_, i) => {
    const lo = Math.max(0, i - 1), hi = Math.min(s.length - 1, i + 1);
    let x: Vec = [0, 0, 0]; for (let j = lo; j <= hi; j++) x = [x[0] + s[j].rate[0], x[1] + s[j].rate[1], x[2] + s[j].rate[2]];
    return scale(x, 1 / (hi - lo + 1));
  });
  const A = [[0, 0, 0], [0, 0, 0], [0, 0, 0]], c = [0, 0, 0];
  const rows: { M: number[][]; b: Vec }[] = [];
  for (let i = 1; i < s.length - 1; i++) {
    const dt = s[i + 1].t - s[i - 1].t;
    if (!(dt > 0) || len(w[i]) < MIN_RATE) continue;
    const alpha = scale(sub(w[i + 1], w[i - 1]), 1 / dt);
    const M = matrix(alpha, w[i]), b = scale(s[i].accel, G);
    rows.push({ M, b });
    for (let r = 0; r < 3; r++) {
      c[r] += M[0][r] * b[0] + M[1][r] * b[1] + M[2][r] * b[2];
      for (let k = 0; k < 3; k++) A[r][k] += M[0][r] * M[0][k] + M[1][r] * M[1][k] + M[2][r] * M[2][k];
    }
  }
  if (rows.length < 10) return null;
  const r = solve3(A, c);
  if (!r) return null;
  const mean: Vec = scale(rows.reduce((m, x) => [m[0] + x.b[0], m[1] + x.b[1], m[2] + x.b[2]] as Vec, [0, 0, 0] as Vec), 1 / rows.length);
  let res = 0, tot = 0;
  for (const { M, b } of rows) {
    const p: Vec = [0, 1, 2].map(i => M[i][0] * r[0] + M[i][1] * r[1] + M[i][2] * r[2]) as Vec;
    res += len(sub(p, b)) ** 2; tot += len(sub(b, mean)) ** 2;
  }
  return { radiusM: len(r), fit: tot > 0 ? Math.max(0, 1 - res / tot) : 0 };
}

export interface ArmLengths { upperArmM: number; forearmM: number }

/** The two segments from the two AirPods' distances to the shoulder, or null when they are not an arm. */
export function armFromRadii(upperRadiusM: number, foreRadiusM: number): ArmLengths | null {
  const upperArmM = upperRadiusM + HAND_M, forearmM = foreRadiusM - upperRadiusM;
  // Adult upper arms run about 26–36 cm and forearms 22–30 cm; the bounds leave room either side and no more.
  if (upperArmM < 0.2 || upperArmM > 0.42 || forearmM < 0.16 || forearmM > 0.36) return null;
  return { upperArmM, forearmM };
}

export const fitOk = (fit: number) => fit >= MIN_FIT;
