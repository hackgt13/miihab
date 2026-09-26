// Rep qualities: what a therapist watches once the arm is getting there at all.
//
// The rep engine (kind.ts) decides whether a rep COUNTS — the clinician's rule. A quality decides how
// WELL a counted rep was made, and says so while it is being made. They are the things a PT calls out
// across the room: "hold it at the top", "slower on the way down", "smooth, no jerking", "you're
// tiring, your last few were shallower". None of them can invalidate a rep, and none of them knows
// which joint is moving: every quality reads the same angle-over-time trace plus the plan's band, so it
// applies unchanged to a shoulder raise, a curl, a trunk rotation or whatever comes next.
//
// Composition: an exercise is bound to a list of qualities (LIBRARY[kind].qualities, else all of
// them). Each quality has a config with defaults and limits, and a plan may override any config key as
// an ordinary prescription param (holdTargetMs, lowerMs, …), bounded like every other param. A new
// quality is one object in QUALITIES; nothing else changes.
//
// Determinism: a quality is driven by the trace in order and holds only state derived from it, so a
// recorded session re-scores identically — the same promise the engine makes.

import type { ResolvedParams } from './kind.ts';
import { LIBRARY } from '../exercises.ts';

export interface TraceSample { tMs: number; angleDeg: number }

/** One rep as it unfolds: the valid samples since it started, and the landmarks of its arc. */
export interface RepTrace {
  rep: number;
  startMs: number;
  endMs: number;                 // the latest sample; final once the rep completes
  samples: TraceSample[];
  peakDeg: number;
  peakMs: number;                // first sample at the peak
  topMs: number;                 // last sample within hysteresis of the peak: where the descent began
  reachedMs: number | null;      // first sample at or above the target
  aboveMs: number | null;        // last sample at or above target − hysteresis
  leftMs: number | null;         // the last sample still up before the lowering began; null until it has
}

export type RepPhase = 'raise' | 'hold' | 'lower';

/**
 * Where in its arc the rep is right now. Shared so the qualities agree on it. A rep that has not reached
 * the target is still raising, whatever it is doing: a dip on the way up is a hitch, not a lowering, and
 * a rep that turns back short of the target is the engine's to reject.
 */
export function phaseOf(trace: RepTrace, _p: ResolvedParams): RepPhase {
  if (trace.reachedMs == null) return 'raise';
  // Once the arm has left the band it is lowering, and a bounce back up is a hitch in that lowering rather
  // than a second hold — a rep ends at rest, and the engine sees to that.
  return trace.leftMs == null ? 'hold' : 'lower';
}

export function beginTrace(rep: number, tMs: number, angleDeg: number): RepTrace {
  return { rep, startMs: tMs, endMs: tMs, samples: [{ tMs, angleDeg }], peakDeg: angleDeg, peakMs: tMs, topMs: tMs, reachedMs: null, aboveMs: null, leftMs: null };
}
export function stepTrace(trace: RepTrace, tMs: number, angleDeg: number, p: ResolvedParams) {
  trace.samples.push({ tMs, angleDeg }); trace.endMs = tMs;
  if (angleDeg > trace.peakDeg) { trace.peakDeg = angleDeg; trace.peakMs = tMs; }
  if (angleDeg >= trace.peakDeg - p.hysteresisDeg) trace.topMs = tMs;
  if (trace.reachedMs == null && angleDeg >= p.targetDeg) trace.reachedMs = tMs;
  if (angleDeg >= p.targetDeg - p.hysteresisDeg) trace.aboveMs = tMs;
  else if (trace.reachedMs != null && trace.leftMs == null) trace.leftMs = trace.aboveMs;
}
const angleAt = (trace: RepTrace, tMs: number) => trace.samples.find(s => s.tMs === tMs)?.angleDeg ?? trace.peakDeg;

/** Every verdict says whether the rep passed this quality and how well, 0..1. The rest is its own. */
export interface RepVerdict { ok: boolean; score: number; [key: string]: unknown }

export interface RepJudge<Live, Verdict extends RepVerdict> {
  /** After each valid sample is appended to the trace. What the patient sees live. */
  step(trace: RepTrace): Live;
  /** Once the rep is over. What the record keeps. */
  finish(trace: RepTrace): Verdict;
}

/** What the set reducer sees per rep, alongside the trace: the engine's verdict on it. */
export interface RepOutcome { valid: boolean; peakDeg: number; durationMs: number }

export interface RepQuality<
  Config extends Record<string, number> = Record<string, number>,
  Live = unknown, Verdict extends RepVerdict = RepVerdict, Set = unknown,
> {
  readonly id: string;
  readonly defaults: Config;
  readonly limits: Readonly<{ [K in keyof Config & string]: readonly [number, number] }>;
  /** False when the prescription gives this quality nothing to judge (a hold on a plan with no hold). Default true. */
  applies?(params: ResolvedParams, config: Config): boolean;
  /** Absent for a set-level quality that has nothing to say per rep. */
  begin?(params: ResolvedParams, config: Config): RepJudge<Live, Verdict>;
  /** The set, once it is over. `verdicts[i]` is this quality's verdict on `traces[i]` (empty without begin). */
  set(verdicts: Verdict[], traces: RepTrace[], outcomes: RepOutcome[], params: ResolvedParams, config: Config): Set;
}

const clamp01 = (x: number) => Math.max(0, Math.min(1, x));
const round = (x: number, places = 0) => { const k = 10 ** places; return Math.round(x * k) / k; };
const mean = (xs: number[]) => xs.length ? xs.reduce((s, x) => s + x, 0) / xs.length : null;
const median = (xs: number[]) => {
  if (!xs.length) return null;
  const s = [...xs].sort((a, b) => a - b), m = s.length >> 1;
  return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
};

// ── Hold in range ───────────────────────────────────────────────────────────────────────────────
// Time under tension at end range: the isometric pause at the top is where the weakest part of the
// arc gets trained, and "hold for two seconds" is the most common thing a PT adds to a raise. Counted
// only while the arm is inside the band — at or above the target and under the ceiling — so drifting
// over the line does not keep the timer running. The engine's holdMs is the floor a rep needs to count;
// this is the target the patient is asked to reach for, and never less than that floor — an endurance
// hold prescribed at five seconds is not asked for two. A plan with no hold at all (holdMs 0, a march
// or a curl) has no hold to judge.

export interface HoldLive { holding: boolean; heldMs: number; targetMs: number; fraction: number; met: boolean }
export interface HoldVerdict extends RepVerdict { longestMs: number; targetMs: number }
export interface HoldSet { targetMs: number; bestMs: number | null; metReps: number; reps: number }

const holdTarget = (p: ResolvedParams, c: { holdTargetMs: number }) => Math.max(c.holdTargetMs, p.holdMs);

export const hold: RepQuality<{ holdTargetMs: number }, HoldLive, HoldVerdict, HoldSet> = {
  id: 'hold',
  defaults: { holdTargetMs: 2000 },
  limits: { holdTargetMs: [500, 10000] },
  applies: p => p.holdMs > 0,
  begin(p, c) {
    const ceiling = p.targetMaxDeg ?? Infinity, targetMs = holdTarget(p, c);
    let inBand = false, runMs = 0, longestMs = 0, lastT: number | null = null;
    return {
      step(trace) {
        const s = trace.samples.at(-1)!;
        const dt = lastT == null ? 0 : Math.min(s.tMs - lastT, p.trackingGapMs); lastT = s.tMs;
        const enter = s.angleDeg >= p.targetDeg && s.angleDeg <= ceiling;
        const leave = s.angleDeg < p.targetDeg - p.hysteresisDeg || s.angleDeg > ceiling + p.hysteresisDeg;
        if (!inBand && enter) { inBand = true; runMs = 0; }
        else if (inBand && leave) { inBand = false; runMs = 0; }
        else if (inBand) { runMs += dt; longestMs = Math.max(longestMs, runMs); }
        return { holding: inBand, heldMs: Math.round(runMs), targetMs,
          fraction: clamp01(runMs / targetMs), met: longestMs >= targetMs };
      },
      finish: () => ({ ok: longestMs >= targetMs, score: clamp01(longestMs / targetMs),
        longestMs: Math.round(longestMs), targetMs }),
    };
  },
  set: (verdicts, _traces, _outcomes, p, c) => ({
    targetMs: holdTarget(p, c),
    bestMs: verdicts.length ? Math.max(...verdicts.map(v => v.longestMs)) : null,
    metReps: verdicts.filter(v => v.ok).length,
    reps: verdicts.length,
  }),
};

// ── Tempo ───────────────────────────────────────────────────────────────────────────────────────
// Rep speed, split by phase, because the phases mean different things. The raise is the concentric
// half; the lowering is the eccentric half, and letting gravity take it is the most common way a rep
// looks complete while training nothing — and a sign the arm is being dropped to get past a painful
// arc. "Two up, three down" is the prescription; fast is what a PT corrects, slow is rarely a problem.
//
// The engine only watches a rep between the rest band and the target, so the seconds it can see are a
// slice of each phase. Tempo therefore measures the SPEED across that slice, in degrees per second,
// and reports the time the whole arc would take at it — rest to target on the way up, peak to rest on
// the way down — which is the number the prescription is written in. Live guidance compares the speed
// so far against the tempo's, from the moment the arm starts down off its peak, so "slower" can be
// said while there is still arc left to slow down in. (With a low target the visible arc is short and
// the nudge comes late; the verdict at the end of the rep is exact either way.)

export type TempoLabel = 'fast' | 'good' | 'slow';
export interface TempoLive { phase: RepPhase; elapsedMs: number; targetMs: number | null; pace: number | null; guidance: 'slower' | null }
export interface TempoVerdict extends RepVerdict {
  raiseMs: number; lowerMs: number; raise: TempoLabel; lower: TempoLabel; targetRaiseMs: number; targetLowerMs: number;
}
export interface TempoSet {
  raiseMs: number | null; lowerMs: number | null; targetRaiseMs: number; targetLowerMs: number;
  controlledLowers: number; fastLowers: number; fastRaises: number; reps: number;
}

const FAST_BELOW = .6, SLOW_ABOVE = 2, PACE_NUDGE = 1.6, NUDGE_AFTER_MS = 160;
const tempoLabel = (measuredMs: number, targetMs: number): TempoLabel =>
  measuredMs < targetMs * FAST_BELOW ? 'fast' : measuredMs > targetMs * SLOW_ABOVE ? 'slow' : 'good';
/** 1 at the tempo, 0 at a third or three times of it. */
const tempoScore = (measuredMs: number, targetMs: number) =>
  measuredMs <= 0 ? 0 : clamp01(1 - Math.abs(Math.log(measuredMs / targetMs)) / Math.log(3));

/** The whole-arc time implied by moving `deg` degrees in `ms` milliseconds, over an arc of `arcDeg`. */
const arcMs = (deg: number, ms: number, arcDeg: number) => deg > 0 && ms > 0 ? arcDeg * ms / deg : 0;
/** The raise as observed: from the rep's first sample to reaching the target (or the peak of a rep that never did). */
function raiseSlice(t: RepTrace) {
  const endMs = t.reachedMs ?? t.peakMs;
  return { deg: angleAt(t, endMs) - t.samples[0].angleDeg, ms: endMs - t.startMs };
}
/** The lowering as observed: from the last sample still up (or the last one near the peak) to the rep's end. */
function lowerSlice(t: RepTrace) {
  const startMs = t.leftMs ?? t.topMs;
  return { deg: angleAt(t, startMs) - t.samples.at(-1)!.angleDeg, ms: t.endMs - startMs };
}

export const tempo: RepQuality<{ raiseMs: number; lowerMs: number }, TempoLive, TempoVerdict, TempoSet> = {
  id: 'tempo',
  defaults: { raiseMs: 2000, lowerMs: 3000 },
  limits: { raiseMs: [500, 10000], lowerMs: [500, 10000] },
  begin(p, c) {
    return {
      step(trace) {
        const phase = phaseOf(trace, p), a = trace.samples.at(-1)!.angleDeg;
        // At the top and not yet coming down: nothing to pace.
        if (phase === 'hold' && a > trace.peakDeg - p.hysteresisDeg)
          return { phase, elapsedMs: trace.endMs - trace.reachedMs!, targetMs: null, pace: null, guidance: null };
        // So far: the raise up to this sample, or the descent since the peak (still in the band or past it).
        const slice = phase === 'raise'
          ? { deg: a - trace.samples[0].angleDeg, ms: trace.endMs - trace.startMs }
          : { deg: angleAt(trace, trace.topMs) - a, ms: trace.endMs - trace.topMs };
        const targetMs = phase === 'raise' ? c.raiseMs : c.lowerMs;
        const arcDeg = phase === 'raise' ? p.targetDeg : trace.peakDeg;
        // Speed so far against the tempo's speed over the whole arc.
        const pace = slice.ms > 0 && slice.deg > 0 ? (slice.deg / slice.ms) / (arcDeg / targetMs) : null;
        return { phase, elapsedMs: slice.ms, targetMs, pace: pace == null ? null : round(pace, 2),
          guidance: pace != null && pace > PACE_NUDGE && slice.ms >= NUDGE_AFTER_MS ? 'slower' : null };
      },
      finish(trace) {
        const up = raiseSlice(trace), down = lowerSlice(trace);
        const raiseMs = Math.round(arcMs(up.deg, up.ms, p.targetDeg)), lowerMs = Math.round(arcMs(down.deg, down.ms, trace.peakDeg));
        const raise = tempoLabel(raiseMs, c.raiseMs), lower = tempoLabel(lowerMs, c.lowerMs);
        return { ok: raise !== 'fast' && lower !== 'fast', score: round((tempoScore(raiseMs, c.raiseMs) + tempoScore(lowerMs, c.lowerMs)) / 2, 2),
          raiseMs, lowerMs, raise, lower, targetRaiseMs: c.raiseMs, targetLowerMs: c.lowerMs };
      },
    };
  },
  set: (verdicts, _traces, _outcomes, _p, c) => ({
    raiseMs: median(verdicts.map(v => v.raiseMs)), lowerMs: median(verdicts.map(v => v.lowerMs)),
    targetRaiseMs: c.raiseMs, targetLowerMs: c.lowerMs,
    controlledLowers: verdicts.filter(v => v.lower !== 'fast').length,
    fastLowers: verdicts.filter(v => v.lower === 'fast').length,
    fastRaises: verdicts.filter(v => v.raise === 'fast').length,
    reps: verdicts.length,
  }),
};

// ── Control ─────────────────────────────────────────────────────────────────────────────────────
// Smoothness. A raise that stalls and dips before it continues, or a lowering that catches and
// bounces, is a hitch: momentum standing in for strength, or the arm flinching through a painful arc.
// Either is worth the therapist's attention, and neither shows in a peak angle. A hitch is a reversal
// of more than hitchDeg against the direction of the phase that then resumes — a dip that keeps going
// is the rep turning back, which is the engine's business; the hold at the top is left to `hold`.

export interface ControlLive { hitches: number; steady: boolean }
export interface ControlVerdict extends RepVerdict { hitches: number; raiseHitches: number; lowerHitches: number }
export interface ControlSet { steadyReps: number; hitches: number; reps: number }

export const control: RepQuality<{ hitchDeg: number }, ControlLive, ControlVerdict, ControlSet> = {
  id: 'control',
  defaults: { hitchDeg: 4 },
  limits: { hitchDeg: [2, 15] },
  begin(p, c) {
    let raiseHitches = 0, lowerHitches = 0, was: RepPhase | null = null;
    // The high-water mark of the phase, and the turning point of a reversal in progress (null when none).
    let mark = 0, turn: number | null = null;
    return {
      step(trace) {
        const phase = phaseOf(trace, p), a = trace.samples.at(-1)!.angleDeg;
        if (phase !== was) { mark = a; turn = null; was = phase; }
        // Both phases are the same shape with the sign flipped: progress is up on the raise, down on the lowering.
        const sign = phase === 'raise' ? 1 : phase === 'lower' ? -1 : 0;
        if (sign) {
          const progress = a * sign, best = mark * sign;
          if (progress >= best) { mark = a; turn = null; }
          else if (turn == null) { if (progress < best - c.hitchDeg) turn = a; }
          else if (progress > turn * sign + c.hitchDeg) {   // came back after the reversal: that was a hitch
            if (phase === 'raise') raiseHitches++; else lowerHitches++;
            turn = null;
          } else if (progress < turn * sign) turn = a;      // the reversal deepens
        }
        const hitches = raiseHitches + lowerHitches;
        return { hitches, steady: hitches === 0 };
      },
      finish() {
        const hitches = raiseHitches + lowerHitches;
        return { ok: hitches === 0, score: clamp01(1 - hitches / 3), hitches, raiseHitches, lowerHitches };
      },
    };
  },
  set: verdicts => ({
    steadyReps: verdicts.filter(v => v.ok).length,
    hitches: verdicts.reduce((s, v) => s + v.hitches, 0),
    reps: verdicts.length,
  }),
};

// ── Consistency ─────────────────────────────────────────────────────────────────────────────────
// The set as a whole. A therapist ends a set when form breaks down, not when the count is reached:
// peaks that shrink across the set are fatigue, and reps that get quicker toward the end are the
// patient hurrying to finish. Set-level only, over the reps that counted, and silent below four of
// them — three reps do not make a trend.

export interface ConsistencySet {
  reps: number; peakSpreadDeg: number | null; fatigueDropDeg: number | null; fatigued: boolean; durationDriftRatio: number | null;
}

const MIN_REPS_FOR_TREND = 4;
export const consistency: RepQuality<{ fatigueDropDeg: number }, never, never, ConsistencySet> = {
  id: 'consistency',
  defaults: { fatigueDropDeg: 8 },
  limits: { fatigueDropDeg: [3, 30] },
  set(_verdicts, _traces, outcomes, _p, c) {
    const valid = outcomes.filter(o => o.valid);
    const peaks = valid.map(o => o.peakDeg), durations = valid.map(o => o.durationMs);
    const third = Math.floor(valid.length / 3);
    const trend = valid.length >= MIN_REPS_FOR_TREND && third > 0;
    const early = (xs: number[]) => mean(xs.slice(0, third))!, late = (xs: number[]) => mean(xs.slice(-third))!;
    const fatigueDropDeg = trend ? round(early(peaks) - late(peaks), 1) : null;
    return {
      reps: valid.length,
      peakSpreadDeg: peaks.length ? round(Math.max(...peaks) - Math.min(...peaks), 1) : null,
      fatigueDropDeg,
      fatigued: fatigueDropDeg != null && fatigueDropDeg >= c.fatigueDropDeg,
      durationDriftRatio: trend && early(durations) > 0 ? round(late(durations) / early(durations), 2) : null,
    };
  },
};

// ── Registry and binding ────────────────────────────────────────────────────────────────────────


// ── Trajectory ──────────────────────────────────────────────────────────────────────────────────
// How closely the arc follows the shape it was prescribed, said as a percentage while it is being
// made. The others judge properties of the movement — did it pause, was it steady, was it paced. This
// one judges the whole path against a reference.
//
// The reference is generated, not recorded. The prescription already fixes both ends of it: rest to
// targetDeg over raiseMs and back over lowerMs. Flash & Hogan's minimum-jerk model says an unimpaired
// point-to-point movement follows the fifth-order polynomial that minimises jerk, which is the
// bell-shaped velocity profile a therapist is describing when they say "smooth, no jerking". So the
// curve costs one function and no capture session, and it re-derives from the plan rather than from
// whoever happened to record it.
//
// Live rather than after the fact on purpose. DTW and spectral arc length are the better measures of a
// finished rep, and they need a finished rep: neither can produce a number during one. Because the
// prescription fixes the duration, the reference is a known function of time since the rep began, so
// the live comparison needs no phase estimation — at 1400 ms in, the curve says what the angle should
// be, and the trace says what it is.
//
// Two things it deliberately does not do. It does not open at 100% and fall: a rep with too few samples
// to judge reports null and the corner stays blank. And it aggregates over the rep so far rather than
// reporting the instantaneous error, which at 50 Hz reads as noise and, in an app someone uses on their
// worst day, as failure.

/**
 * The prescribed angle at `tMs` after the rep began: minimum jerk up, the pause at the top, minimum
 * jerk down.
 *
 * The hold is part of the arc, not a detail. A raise is prescribed with one, and a reference that
 * descends while the arm is being held marks the best reps down and the rushed ones up — which is
 * exactly what it did before this took holdMs.
 */
export function referenceDeg(tMs: number, restDeg: number, targetDeg: number, raiseMs: number, holdMs: number, lowerMs: number): number {
  // The fifth-order polynomial with zero velocity and acceleration at both ends.
  const ease = (u: number) => { const c = clamp01(u); return c * c * c * (10 - 15 * c + 6 * c * c); };
  if (tMs <= 0) return restDeg;
  if (tMs < raiseMs) return restDeg + (targetDeg - restDeg) * ease(tMs / raiseMs);
  if (tMs < raiseMs + holdMs) return targetDeg;
  const down = (tMs - raiseMs - holdMs) / Math.max(1, lowerMs);
  return down >= 1 ? restDeg : targetDeg + (restDeg - targetDeg) * ease(down);
}

export interface TrajectoryLive {
  /** 0..100, or null before there is enough of the rep to say anything honest. */
  percent: number | null;
  deviationDeg: number;
}
export interface TrajectoryVerdict extends RepVerdict { meanDeviationDeg: number; percent: number }
export interface TrajectorySet { medianPercent: number | null; bestPercent: number | null }

export const trajectory: RepQuality<
  { toleranceDeg: number; raiseMs: number; holdTargetMs: number; lowerMs: number }, TrajectoryLive, TrajectoryVerdict, TrajectorySet
> = {
  id: 'trajectory',
  // The tolerance is the width at which closeness reaches zero. A default near the band's own margin
  // rather than a number invented here: inside the prescribed corridor should read as on track.
  // holdTargetMs is the hold quality's key on purpose: it is the same prescribed pause, and the two
  // must agree about the shape of one rep. Naming it holdMs would collide with the engine's own param
  // — the floor a rep needs to count — and quietly rewrite rep detection.
  // holdTargetMs is the hold quality's key and its bounds, on purpose: it is the same prescribed
  // pause, and two qualities disagreeing about one rep's shape is a bug waiting to happen. Naming it
  // holdMs would be worse still — that is the engine's own param, the floor a rep needs to count.
  defaults: { toleranceDeg: 18, raiseMs: 2000, holdTargetMs: 2000, lowerMs: 3000 },
  limits: { toleranceDeg: [5, 45], raiseMs: [400, 8000], holdTargetMs: [500, 10000], lowerMs: [400, 12000] },

  begin(params, config) {
    // The arc starts where the rep started, not at restMaxDeg — that is the threshold for counting an
    // arm as returned, not the angle it rests at. Reading it as a position put the whole reference 30°
    // above the arm and cost every rep about 7° of phantom error.
    let restDeg = 0;
    // The pause the patient is asked for, never less than the floor a rep needs to count — the same
    // rule the hold quality applies, so the two agree about the shape of one rep. A plan with no hold
    // (a curl, a march) has no plateau in its arc either.
    const holdMs = (params.holdMs ?? 0) > 0 ? Math.max(config.holdTargetMs, params.holdMs ?? 0) : 0;
    // Smoothed over about a second: a figure recomputed per sample flickers, and a flickering
    // percentage is the first thing a person stops believing.
    const Smoothing = 1000;
    let smoothed: number | null = null, lastMs: number | null = null;
    let sum = 0, count = 0;

    const closeness = (trace: RepTrace) => {
      const last = trace.samples[trace.samples.length - 1];
      if (!last) return null;
      // Shape, not amplitude. Whether the arm got there is the engine's question and the band's; this
      // one asks how the path was travelled. So the arc is drawn to the height actually reached —
      // never below the target, so falling short still shows — and a rep taken honestly past the
      // target is not marked down for it.
      const amplitude = Math.max(params.targetDeg, trace.peakDeg);
      restDeg = trace.samples[0]?.angleDeg ?? 0;
      const since = last.tMs - trace.startMs;
      const want = referenceDeg(since, restDeg, amplitude, config.raiseMs, holdMs, config.lowerMs);
      // The plateau is the hold quality's business, and every rep matches the reference while it sits
      // there. Counting it averages the differences away: a rep snapped up in 400ms scored barely
      // below one taken over two seconds, because most of both was the pause.
      const travelling = since < config.raiseMs || since > config.raiseMs + holdMs;
      return { deviation: Math.abs(last.angleDeg - want), travelling };
    };

    return {
      step(trace) {
        const now = closeness(trace);
        if (!now) return { percent: null, deviationDeg: 0 };
        if (now.travelling) { sum += now.deviation; count++; }

        const fraction = clamp01(1 - now.deviation / config.toleranceDeg);
        const dt = lastMs == null ? Smoothing : Math.max(0, trace.endMs - lastMs);
        lastMs = trace.endMs;
        const weight = clamp01(dt / Smoothing);
        smoothed = smoothed == null ? fraction : smoothed + (fraction - smoothed) * weight;

        // Three samples is the least that is not just the first instant of the rep.
        const ready = trace.samples.length >= 3;
        return { percent: ready ? round(smoothed * 100) : null, deviationDeg: round(now.deviation, 1) };
      },
      finish() {
        const meanDeviation = count ? sum / count : 0;
        const score = clamp01(1 - meanDeviation / config.toleranceDeg);
        return { ok: score >= 0.5, score: round(score, 2), percent: round(score * 100),
          meanDeviationDeg: round(meanDeviation, 1) };
      },
    };
  },

  set(verdicts) {
    const percents = verdicts.map(v => v.percent);
    return { medianPercent: median(percents), bestPercent: percents.length ? Math.max(...percents) : null };
  },
};

export const QUALITIES: readonly RepQuality<any, any, any, any>[] = [hold, tempo, control, consistency, trajectory];

export interface QualityBinding { quality: RepQuality<any, any, any, any>; config: Record<string, number> }

export function quality(id: string): RepQuality<any, any, any, any> {
  const found = QUALITIES.find(q => q.id === id);
  if (!found) throw Error(`Unknown rep quality "${id}". Known: ${QUALITIES.map(q => q.id).join(', ')}`);
  return found;
}

/** Which qualities an exercise is judged on: the catalog's list, else every one. */
export function qualityIdsFor(kindId: string | null | undefined): readonly string[] {
  return (kindId && LIBRARY[kindId]?.qualities) ?? QUALITIES.map(q => q.id);
}

/** The config keys a plan may set for this exercise, with their bounds — merged into the plan's param limits. */
export function qualityLimits(kindId: string | null | undefined): Record<string, readonly [number, number]> {
  const out: Record<string, readonly [number, number]> = {};
  for (const id of qualityIdsFor(kindId)) Object.assign(out, quality(id).limits);
  return out;
}

/** Bind the exercise's qualities, each configured from its defaults overridden by any matching params. */
export function bindQualities(kindId: string | null | undefined, params: Record<string, unknown> = {}): QualityBinding[] {
  return qualityIdsFor(kindId).map(id => {
    const q = quality(id), config: Record<string, number> = { ...q.defaults };
    for (const key of Object.keys(q.defaults)) {
      const value = Number(params[key]);
      if (params[key] != null && Number.isFinite(value)) config[key] = value;
    }
    return { quality: q, config };
  });
}

// ── The track: what the engine drives ───────────────────────────────────────────────────────────
// One per session. The engine tells it when a rep starts, hands it every valid sample while the rep
// runs, and asks for the verdicts when the rep ends; it keeps the streak and the running score.

export interface RepQualityResult { quality: Record<string, RepVerdict>; ok: boolean; score: number | null; streak: number }

export class QualityTrack {
  readonly bindings: QualityBinding[];
  private readonly params: ResolvedParams;
  private trace: RepTrace | null = null;
  private judges: { id: string; judge: RepJudge<unknown, RepVerdict> }[] = [];
  private readonly traces: RepTrace[] = [];
  private readonly outcomes: RepOutcome[] = [];
  private readonly verdicts: Record<string, RepVerdict[]> = {};
  private readonly repScores: number[] = [];
  private live: Record<string, unknown> = {};
  private streak = 0; private bestStreak = 0;

  constructor(params: ResolvedParams, bindings: QualityBinding[]) {
    this.params = params;
    this.bindings = bindings.filter(b => b.quality.applies?.(params, b.config) ?? true);
    for (const b of this.bindings) this.verdicts[b.quality.id] = [];
  }

  /** The qualities in play and their configs, for whoever renders them. */
  get configs() { return this.bindings.map(b => ({ id: b.quality.id, ...b.config })); }

  /** Live readouts for the rep in progress, keyed by quality id, plus the streak. Null between reps. */
  get current(): Record<string, unknown> | null {
    return this.trace ? { streak: this.streak, ...this.live } : null;
  }

  begin(rep: number, tMs: number, angleDeg: number) {
    this.trace = beginTrace(rep, tMs, angleDeg);
    this.judges = this.bindings.filter(b => b.quality.begin).map(b => ({ id: b.quality.id, judge: b.quality.begin!(this.params, b.config) }));
    this.live = {};
    for (const { id, judge } of this.judges) this.live[id] = judge.step(this.trace);
  }

  step(tMs: number, angleDeg: number) {
    if (!this.trace) return;
    stepTrace(this.trace, tMs, angleDeg, this.params);
    for (const { id, judge } of this.judges) this.live[id] = judge.step(this.trace);
  }

  finish(outcome: RepOutcome): RepQualityResult {
    const trace = this.trace!;
    const quality: Record<string, RepVerdict> = {};
    for (const { id, judge } of this.judges) { quality[id] = judge.finish(trace); this.verdicts[id].push(quality[id]); }
    this.traces.push(trace); this.outcomes.push(outcome);
    const scores = Object.values(quality).map(v => v.score);
    const ok = outcome.valid && Object.values(quality).every(v => v.ok);
    const score = outcome.valid && scores.length ? round(mean(scores)!, 2) : null;
    if (score != null) this.repScores.push(score);
    this.streak = ok ? this.streak + 1 : 0; this.bestStreak = Math.max(this.bestStreak, this.streak);
    this.trace = null; this.judges = []; this.live = {};
    return { quality, ok, score, streak: this.streak };
  }

  /** The set's verdict per quality, plus the streak and the mean form score over the reps that counted. */
  summary() {
    const out: Record<string, unknown> = {};
    for (const b of this.bindings)
      out[b.quality.id] = b.quality.set(this.verdicts[b.quality.id], this.traces, this.outcomes, this.params, b.config);
    return { ...out, streak: { best: this.bestStreak, last: this.streak },
      formScore: this.repScores.length ? round(mean(this.repScores)!, 2) : null };
  }
}
