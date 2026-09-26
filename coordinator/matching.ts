// Introducing one patient to another.
//
// What this matches on, and what it deliberately does not: a profile here is
// built from the patient's own goal (plans.ts stores it in their words), the
// exercise kinds they have been prescribed, their age band and how far into a
// programme they are. There is no diagnosis field anywhere in this codebase and
// this does not add one — "shoulder work, wants to golf, week three" carries
// almost all of the matching value without a medical record in it.
//
// Nor does it compare anyone's numbers. friends.ts holds that two patients are
// never comparable, six weeks post-op against six months, and a score built out
// of range-of-motion would be exactly the comparison that rule forbids. Degrees,
// rep counts and adherence are absent by design; what two people have in common
// here is what they are working toward, not how far along they are.
//
// The score is a hand-weighted sum rather than anything learned. With this many
// patients a model could not be fit anyway, but the real reason is that an
// introduction has to say why it happened. "You are both working toward golf"
// is the entire value of the suggestion, so every score carries the terms that
// produced it and the UI renders them.
import { AGE_BANDS, type AgeBandId } from './norms.ts';

/** Everything a match is computed from. No measurements, by design. */
export interface Profile {
  personId: string;
  /// Revealed only once both sides have said yes. reason() never reads it, so
  /// it cannot leak into the anonymous card.
  displayName?: string;
  /** From plan.goal.components, lowercased: 'shoulder elevation', 'grip'. */
  goalComponents: string[];
  /** Exercise kind ids prescribed to them, e.g. 'shoulder-raise.v1'. */
  exerciseKinds: string[];
  /** Null when they have not said, which is common and must not exclude them. */
  ageBand: AgeBandId | null;
  /** Whole weeks since their programme was approved. */
  programWeek: number;
}

export interface Term { name: string; score: number; weight: number; shared: string[] }
export interface Match {
  personId: string;
  score: number;
  /** The terms that produced the score, strongest first, for the UI to explain with. */
  terms: Term[];
}

/// Hand-tuned, and meant to be tuned again against real pairs. Goal leads
/// because it is the thing two people can actually talk about; the body region
/// matters less than what they are trying to get back to.
const WEIGHTS = { goal: 0.45, kinds: 0.30, age: 0.15, phase: 0.10 };

/// Further apart than this and the two are not peers at all. The same number
/// draws both lines, so nobody can be offered as both.
export const MIN_WEEKS_AHEAD = 6;

const norm = (s: string) => s.trim().toLowerCase();

/** Overlap of two sets, 0 when either is empty rather than NaN. */
export function jaccard(a: string[], b: string[]): { score: number; shared: string[] } {
  const left = new Set(a.map(norm).filter(Boolean));
  const right = new Set(b.map(norm).filter(Boolean));
  if (left.size === 0 || right.size === 0) return { score: 0, shared: [] };
  const shared = [...left].filter(v => right.has(v));
  const union = new Set([...left, ...right]).size;
  return { score: shared.length / union, shared };
}

/// Adjacent bands are near-neighbours, not strangers: a 50-year-old and a
/// 60-year-old have more in common than the band edge suggests.
export function bandProximity(a: AgeBandId | null, b: AgeBandId | null): number {
  if (!a || !b) return 0;                       // unstated is not a mismatch, just no signal
  const index = (id: AgeBandId) => AGE_BANDS.findIndex(band => band.id === id);
  const apart = Math.abs(index(a) - index(b));
  return apart === 0 ? 1 : apart === 1 ? 0.6 : apart === 2 ? 0.25 : 0;
}

/// Someone three weeks ahead remembers week three. Someone six months ahead is
/// a different kind of relationship — see the mentor pairing, which wants the
/// gap rather than penalising it.
export function phaseProximity(a: number, b: number): number {
  const apart = Math.abs(a - b);
  return apart <= 1 ? 1 : apart <= 3 ? 0.7 : apart <= 6 ? 0.4 : apart <= 12 ? 0.15 : 0;
}

/**
 * Score one pair. Always returns terms, including the ones that scored zero, so
 * a caller can show why a weak match is weak.
 */
export function similarity(me: Profile, them: Profile): Match {
  const goal = jaccard(me.goalComponents, them.goalComponents);
  const kinds = jaccard(me.exerciseKinds, them.exerciseKinds);
  const age = bandProximity(me.ageBand, them.ageBand);
  const phase = phaseProximity(me.programWeek, them.programWeek);

  const terms: Term[] = [
    { name: 'goal', score: goal.score, weight: WEIGHTS.goal, shared: goal.shared },
    { name: 'exercises', score: kinds.score, weight: WEIGHTS.kinds, shared: kinds.shared },
    { name: 'age', score: age, weight: WEIGHTS.age, shared: [] },
    { name: 'stage', score: phase, weight: WEIGHTS.phase, shared: [] },
  ].sort((a, b) => b.score * b.weight - a.score * a.weight);

  const score = terms.reduce((total, t) => total + t.score * t.weight, 0);
  return { personId: them.personId, score, terms };
}

/**
 * Age and stage are modifiers, never grounds on their own. Without this, two
 * people who share nothing are still suggested to each other for both being in
 * week zero, which on a fresh cohort means everybody matches everybody.
 */
function substantive(match: Match): boolean {
  return match.terms.some(t => (t.name === 'goal' || t.name === 'exercises') && t.score > 0);
}

/**
 * The strongest matches for one person, best first. `exclude` carries everyone
 * already introduced, declined or befriended — a suggestion they have already
 * answered is worse than no suggestion.
 */
export function rank(me: Profile, others: Profile[], exclude: Set<string> = new Set(), limit = 5): Match[] {
  return others
    .filter(p => p.personId !== me.personId && !exclude.has(p.personId))
    // Someone far ahead is a mentor, not a peer; leave them for mentors().
    .filter(p => Math.abs(p.programWeek - me.programWeek) < MIN_WEEKS_AHEAD)
    .map(p => similarity(me, p))
    .filter(substantive)
    .sort((a, b) => b.score - a.score)
    .slice(0, limit);
}

/**
 * Pair someone early in a programme with someone well ahead of them. The gap is
 * the point, so this inverts phaseProximity instead of using it, and it is the
 * one place a difference between two patients is a feature rather than the
 * comparison friends.ts warns about.
 */
export function mentors(me: Profile, others: Profile[], exclude: Set<string> = new Set(), limit = 3): Match[] {
  return others
    .filter(p => p.personId !== me.personId && !exclude.has(p.personId))
    .filter(p => p.programWeek - me.programWeek >= MIN_WEEKS_AHEAD)
    .map(p => {
      // Same body of work, different distance along it.
      const kinds = jaccard(me.exerciseKinds, p.exerciseKinds);
      const goal = jaccard(me.goalComponents, p.goalComponents);
      const terms: Term[] = [
        { name: 'exercises', score: kinds.score, weight: 0.6, shared: kinds.shared },
        { name: 'goal', score: goal.score, weight: 0.4, shared: goal.shared },
      ].sort((a, b) => b.score * b.weight - a.score * a.weight);
      return { personId: p.personId, score: terms.reduce((t, x) => t + x.score * x.weight, 0), terms };
    })
    .filter(m => m.score > 0)
    .sort((a, b) => b.score - a.score)
    .slice(0, limit);
}

/**
 * One line saying why, for the anonymous card. Never names the person and never
 * quotes a number: at this stage the two sides have not agreed to meet.
 */
export function readable(id: string): string {
  return id.replace(/\.v\d+$/, '').replace(/[-_]/g, ' ').trim();
}

export function reason(match: Match, them: Profile): string {
  const top = match.terms[0];
  if (top?.shared.length) {
    const list = top.shared.slice(0, 2).map(readable).join(' and ');
    return top.name === 'goal'
      ? `Also working toward ${list}`
      : `Also working on ${list}`;
  }
  if (them.programWeek > 0) return `Around week ${them.programWeek} of their programme`;
  return 'Just starting out, like you';
}

/** Whole weeks since a plan was approved, floored at zero. */
export function weeksSince(iso: string | null | undefined): number {
  if (!iso) return 0;
  const then = Date.parse(iso);
  if (Number.isNaN(then)) return 0;
  return Math.max(0, Math.floor((Date.now() - then) / (7 * 86400000)));
}
