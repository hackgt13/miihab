// Which AirPod is which. The rule (AGENTS.md, "Sensors"): the patient's two pairs are Mac 1's and Mac 2's, read raw
// from the relay's /motion, and which one measures what is decided here, from what is live and what moves.
//
//   one IMU     the exercise takes whichever patient stream is live. Two live: the most recent one. Pinned (a group
//               session, where Mac 2 is someone else): that stream and no other, live or not.
//   two IMUs    both patient streams must be live; the patient is told where each AirPod goes and asked to move
//               the one on the measured limb. The stream that moves while the other stays still is `imu`, the other
//               is `ref`. Nothing is assumed from which app or which Mac a pair came through.
//
// The assignment locks at calibration (the engine's rest reference is per stream); after that a stream that goes
// quiet is the engine's tracking loss, never a reassignment. Once a two-AirPod set has told the pairs apart, the
// coordinator remembers it for the next set worn the same way (`pinned`), so the patient is asked once, not per set.

import type { Mac } from '../motion-fuse.ts';

export type Channel = Mac;
export type Mode = 'one' | 'two';
export type Phase = 'waiting' | 'identify' | 'ready';

export interface SensorState {
  mode: Mode;
  phase: Phase;
  /** Which channel is the measured limb, and (two IMUs) which the neighbouring segment. Null until assigned. */
  imu: Channel | null;
  ref: Channel | null;
  /** Channels with a sample inside freshMs. */
  live: Channel[];
  /** Whether the assignment is fixed for the rest of the set. */
  locked: boolean;
  /** What to tell the patient right now. */
  instruction: string;
}

export interface AssignOptions {
  mode: Mode;
  /** Catalog phrases for where each AirPod goes: 'AirPod on the wrist', 'AirPod on the chest'. */
  wear: { imu: string; ref?: string };
  /** A sample older than this is not a live stream. AirPods stream ~25 Hz; 1.5 s covers a few dropped packets. */
  freshMs?: number;
  /** Angular speed a moving AirPod exceeds, and one a resting AirPod stays under (rad/s). */
  moveRadS?: number;
  stillRadS?: number;
  /** How much recent motion decides "moving" and "still". */
  windowMs?: number;
  minSamples?: number;
  /** Skip identification: a group session (one IMU: Mac 1), a development override, or a replay that knows its
   *  streams. A pinned stream is never handed over, even while it is not live. */
  pinned?: { imu: Channel; ref?: Channel };
}

const CHANNELS: readonly Channel[] = ['mac1', 'mac2'];

/** 'AirPod on the wrist' → 'wrist'; 'AirPods in your ears' → 'ears'. */
export const placeOf = (wear: string) => {
  const m = /\b(?:on|in)\s+(?:the|your)\s+(.+)$/i.exec(wear);
  return (m ? m[1] : wear).trim();
};

export class ImuAssigner {
  readonly mode: Mode;
  private readonly wear: { imu: string; ref?: string };
  private readonly freshMs: number;
  private readonly moveRadS: number;
  private readonly stillRadS: number;
  private readonly windowMs: number;
  private readonly minSamples: number;
  private readonly last: Partial<Record<Channel, number>> = {};
  private readonly speeds: Record<Channel, { t: number; v: number }[]> = { mac1: [], mac2: [] };
  private imu: Channel | null = null;
  private ref: Channel | null = null;
  private readonly pinned: boolean;
  private locked = false;
  private now = -Infinity;
  private snapshot = '';

  constructor(o: AssignOptions) {
    this.mode = o.mode; this.wear = o.wear;
    this.freshMs = o.freshMs ?? 1500; this.moveRadS = o.moveRadS ?? 1.0; this.stillRadS = o.stillRadS ?? 0.35;
    this.windowMs = o.windowMs ?? 600; this.minSamples = o.minSamples ?? 5;
    this.pinned = !!o.pinned;
    if (o.pinned) { this.imu = o.pinned.imu; if (o.mode === 'two') this.ref = o.pinned.ref ?? (o.pinned.imu === 'mac1' ? 'mac2' : 'mac1'); }
    this.snapshot = JSON.stringify(this.state);
  }

  /** A sample arrived on a channel. True when the state a UI shows has changed. */
  push(channel: Channel, rotationRate: readonly number[], tMs: number): boolean {
    this.now = Math.max(this.now, tMs);
    this.last[channel] = tMs;
    const w = this.speeds[channel];
    w.push({ t: tMs, v: Math.hypot(rotationRate[0] ?? 0, rotationRate[1] ?? 0, rotationRate[2] ?? 0) });
    while (w.length && w[0].t < tMs - this.windowMs) w.shift();
    this.assign();
    return this.changed();
  }

  /** Time passed with no sample; liveness may have lapsed. True when the shown state changed. */
  tick(tMs: number): boolean { this.now = Math.max(this.now, tMs); this.assign(); return this.changed(); }

  /** Fix the assignment for the rest of the set (the engine has calibrated against these streams). */
  lock(): boolean { if (this.phase !== 'ready') return false; this.locked = true; return this.changed(); }

  /** Which engine channel a relay channel feeds right now; null until the set is ready to be measured. */
  roleOf(channel: Channel): 'imu' | 'ref' | null {
    if (this.phase !== 'ready') return null;
    if (channel === this.imu) return 'imu';
    if (this.mode === 'two' && channel === this.ref) return 'ref';
    return null;
  }

  get live(): Channel[] { return CHANNELS.filter(c => this.last[c] != null && this.now - this.last[c]! <= this.freshMs); }

  get phase(): Phase {
    if (this.mode === 'one') return this.imu ? 'ready' : 'waiting';
    if (this.imu && this.ref) return 'ready';
    return this.live.length >= 2 ? 'identify' : 'waiting';
  }

  get state(): SensorState {
    return { mode: this.mode, phase: this.phase, imu: this.imu, ref: this.ref, live: this.live, locked: this.locked, instruction: this.instruction() };
  }

  private changed(): boolean { const s = JSON.stringify(this.state); if (s === this.snapshot) return false; this.snapshot = s; return true; }

  private assign() {
    if (this.locked || this.pinned) return;
    const live = this.live;
    if (this.mode === 'one') {
      // Take whatever is live. Until calibration locks it, a stream that dies hands over to one that is alive.
      if (this.imu && live.includes(this.imu)) return;
      this.imu = live.length ? live.reduce((a, b) => (this.last[a]! >= this.last[b]! ? a : b)) : null;
      return;
    }
    if (this.imu && this.ref) return;
    if (live.length < 2) return;
    // Identify: the one AirPod moving while the other rests is the limb.
    const moving = live.filter(c => this.moving(c)), still = live.filter(c => this.still(c));
    if (moving.length === 1 && still.length === 1) { this.imu = moving[0]; this.ref = still[0]; }
  }

  private moving(c: Channel): boolean {
    const w = this.speeds[c].filter(s => s.t >= this.now - this.windowMs);
    return w.length >= this.minSamples && w.filter(s => s.v > this.moveRadS).length >= w.length * 0.7;
  }
  private still(c: Channel): boolean {
    const w = this.speeds[c].filter(s => s.t >= this.now - this.windowMs);
    return w.length >= this.minSamples && w.every(s => s.v <= this.stillRadS);
  }

  private instruction(): string {
    const limb = placeOf(this.wear.imu), other = this.wear.ref ? placeOf(this.wear.ref) : null;
    const live = this.live;
    if (this.mode === 'one') {
      if (this.imu) return live.includes(this.imu) ? `Reading the AirPod on your ${limb}.` : `The AirPod on your ${limb} went quiet. Hold still while it reconnects.`;
      return `Put the AirPod on your ${limb} and hold still.`;
    }
    if (this.imu && this.ref) {
      if (live.length === 2) return this.locked ? `Reading both AirPods.` : `Got it. Now hold both still.`;
      const gone = live.includes(this.imu) ? other : limb;
      return `The AirPod on your ${gone} went quiet. Hold still while it reconnects.`;
    }
    if (live.length === 0) return `Put one AirPod on your ${limb} and one on your ${other}.`;
    if (live.length === 1) return `One AirPod is streaming. Connect the other, then hold still.`;
    return `Move the AirPod on your ${limb}. Keep the one on your ${other} still.`;
  }
}
