// Two AirPod pairs, one motion stream for the games.
//
// The patient has two IMUs everywhere: Mac 1's AirPods (this Mac) and Mac 2's (the second Mac, over the tailnet).
// Every game reads one stream, and the one it wants is whichever pair is doing the moving:
//
//   golf     both pairs held together in the grip: the one reporting the bigger swing
//   bowling  one pair on the throwing wrist, the other resting: the wrist, the moment it throws
//
// Both are the same rule — follow the more active pair — so it lives here once, in the relay, and no game knows
// there are two. Activity is angular speed smoothed over a short window. A switch needs the other pair to be clearly
// more active (so two pairs held together do not flicker between each other) and a quiet or stale current pair
// gives way at once.
//
// Each pair reports orientation in its own reference frame, so switching would make the club or the arm jump. The
// output is therefore re-based at every switch: the new pair's orientation is carried on from where the old one
// left off, and from then on it follows the new pair's rotation. For two pairs held rigidly together that is exact;
// for a resting pair handing over to a throwing one it is continuous, which is what a game can use.

export type Mac = 'mac1' | 'mac2';
export const MACS: readonly Mac[] = ['mac1', 'mac2'];

export interface MotionIn {
  quaternion: number[];      // x, y, z, w
  rotationRate: number[];    // rad/s
  sourceId: string;          // 'Left' | 'Right' bud
}

export interface MotionOut extends MotionIn {
  mac: Mac;
  sequence: number;
  switched: boolean;         // this sample is the first from a newly picked pair
}

export interface FuseOptions {
  /** Smoothing time constant for activity (ms). */
  tauMs?: number;
  /** The other pair must beat the current one by this factor, plus `marginRadS`, to take over. */
  ratio?: number;
  marginRadS?: number;
  /** Shortest hold on a pick, so a burst of noise cannot bounce the output. */
  holdMs?: number;
  /** A pair with no sample for this long is gone. */
  staleMs?: number;
}

type Q = [number, number, number, number];
const mul = (a: Q, b: Q): Q => [
  a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
  a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0],
  a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3],
  a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2],
];
const inv = (q: Q): Q => { const n = q[0] ** 2 + q[1] ** 2 + q[2] ** 2 + q[3] ** 2 || 1; return [-q[0] / n, -q[1] / n, -q[2] / n, q[3] / n]; };
const unit = (q: Q): Q => { const n = Math.hypot(...q) || 1; return [q[0] / n, q[1] / n, q[2] / n, q[3] / n]; };

export class DominantMotion {
  private readonly tauMs: number;
  private readonly ratio: number;
  private readonly marginRadS: number;
  private readonly holdMs: number;
  private readonly staleMs: number;
  private readonly state = new Map<Mac, { activity: number; at: number; q: Q }>();
  private current: Mac | null = null;
  private pickedAt = -Infinity;
  /** Applied to the current pair's orientation so the output carries on from the last pick. */
  private offset: Q = [0, 0, 0, 1];
  private lastOut: Q | null = null;
  private sequence = 0;

  constructor(options: FuseOptions = {}) {
    this.tauMs = options.tauMs ?? 150;
    this.ratio = options.ratio ?? 1.35;
    this.marginRadS = options.marginRadS ?? 0.4;
    this.holdMs = options.holdMs ?? 250;
    this.staleMs = options.staleMs ?? 600;
  }

  /** Which pair the games are following now, if any. */
  get picked(): Mac | null { return this.current; }

  activity(mac: Mac, tMs: number): number {
    const s = this.state.get(mac);
    return s && tMs - s.at <= this.staleMs ? s.activity : 0;
  }

  /** A pair left: the games follow the other one from its next sample. */
  drop(mac: Mac): void {
    this.state.delete(mac);
    if (this.current === mac) this.current = null;
  }

  /** One sample from one pair. Returns the sample the games should see, or null when it is the other pair's turn. */
  push(mac: Mac, sample: MotionIn, tMs: number): MotionOut | null {
    const q = unit(sample.quaternion as Q);
    const speed = Math.hypot(...sample.rotationRate);
    const s = this.state.get(mac);
    if (!s || tMs - s.at > this.staleMs) this.state.set(mac, { activity: speed, at: tMs, q });
    else {
      const k = 1 - Math.exp(-Math.max(0, tMs - s.at) / this.tauMs);
      s.activity += (speed - s.activity) * k; s.at = tMs; s.q = q;
    }

    let switched = false;
    if (this.current !== mac) {
      const other = this.current, mine = this.activity(mac, tMs);
      const takeOver = other === null || !this.fresh(other, tMs)
        || tMs - this.pickedAt >= this.holdMs && mine > this.activity(other, tMs) * this.ratio + this.marginRadS;
      if (!takeOver) return null;
      // Carry the output on from where it was: last output = offset · q  ⇒  offset = last · q⁻¹.
      this.offset = this.lastOut ? unit(mul(this.lastOut, inv(q))) : [0, 0, 0, 1];
      this.current = mac; this.pickedAt = tMs; switched = true;
    }
    const out = unit(mul(this.offset, q));
    this.lastOut = out;
    return { quaternion: out, rotationRate: sample.rotationRate, sourceId: sample.sourceId, mac, sequence: this.sequence++, switched };
  }

  private fresh(mac: Mac, tMs: number) { const s = this.state.get(mac); return !!s && tMs - s.at <= this.staleMs; }
}
