// How far the patient's head moved away from where it sat at the start of the set, rep by rep, from the headset
// (golf-relay.ts /head: the head's offset from the seated eye point in the seat's frame, x right, y up, z forward).
//
// A flag for the clinician, not a measurement of the trunk: a seated patient who leans to reach carries the head
// with them, so head travel during a rep is a cheap, camera-free sign of trunk compensation. It is only a sign —
// a nod or a crane of the neck moves the head too — so it:
//   - uses head position, never head rotation;
//   - counts only inside a rep, when the patient faces forward to lift, not while they look around between reps;
//   - ignores small travel (under THRESHOLD_CM) and reports the distance, with an approximate lean angle about the
//     hips labelled as approximate;
//   - never changes which reps count. The exercise's own validity rules do that.
// With no headset the summary says so (available: false) rather than reporting zeros.

export const HEAD_LEAN = {
  thresholdCm: 5,      // head travel that counts as leaning, beyond the few centimetres a nod or neck movement makes
  pivotM: 0.8,         // hips to seated eyes: the lever the approximate angle is taken over
  baselineMs: 1500,    // how the patient sat: the first moments of the set, before the first rep
};

interface HeadSample { t: number; x: number; z: number }
interface RepWindow { rep: number; startMs: number; endMs: number }

const median = (v: number[]) => { const s = [...v].sort((a, b) => a - b), m = s.length >> 1; return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; };
const round1 = (v: number) => Math.round(v * 10) / 10;

export class HeadLean {
  private samples: HeadSample[] = [];
  private reps: RepWindow[] = [];
  private startedAt: number | null = null;

  /** A head pose from the relay, on the shared host clock. Only horizontal travel is kept: leaning moves the head
   * forward or sideways; sinking in the chair is not a reach. */
  push(t: number, p: number[]) {
    if (!Number.isFinite(t) || !Array.isArray(p) || p.length !== 3) return;
    this.startedAt ??= t;
    this.samples.push({ t, x: p[0], z: p[2] });
  }

  rep(rep: number, startMs: number, endMs: number) {
    if (Number.isFinite(startMs) && Number.isFinite(endMs) && endMs > startMs) this.reps.push({ rep, startMs, endMs });
  }

  summary() {
    if (this.samples.length < 10 || !this.reps.length) return { available: false as const, thresholdCm: HEAD_LEAN.thresholdCm };
    // How the patient sat: before the first rep began, within the first moments of the set.
    const firstRep = Math.min(...this.reps.map(r => r.startMs));
    const seated = this.samples.filter(s => s.t < firstRep && s.t - this.startedAt! <= HEAD_LEAN.baselineMs);
    const base = seated.length >= 3 ? seated : this.samples.slice(0, 3);
    const bx = median(base.map(s => s.x)), bz = median(base.map(s => s.z));
    const perRep = this.reps.map(({ rep, startMs, endMs }) => {
      const inRep = this.samples.filter(s => s.t >= startMs && s.t <= endMs);
      if (inRep.length < 3) return { rep, cm: null, approxDeg: null };
      const metres = Math.max(...inRep.map(s => Math.hypot(s.x - bx, s.z - bz)));
      const approxDeg = Math.asin(Math.min(1, metres / HEAD_LEAN.pivotM)) * 180 / Math.PI;
      return { rep, cm: round1(metres * 100), approxDeg: Math.round(approxDeg) };
    });
    const measured = perRep.filter(r => r.cm != null).map(r => r.cm!);
    if (!measured.length) return { available: false as const, thresholdCm: HEAD_LEAN.thresholdCm };
    return {
      available: true as const,
      thresholdCm: HEAD_LEAN.thresholdCm,
      repsMeasured: measured.length,
      repsOverThreshold: measured.filter(cm => cm >= HEAD_LEAN.thresholdCm).length,
      medianCm: round1(median(measured)),
      maxCm: round1(Math.max(...measured)),
      perRep,
      note: 'Head travel from the headset during each rep, from how the patient sat at the start of the set. A sign of leaning to compensate, not a trunk angle; the angle shown is approximate.',
    };
  }
}
