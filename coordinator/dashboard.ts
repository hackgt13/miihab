// What the Unity menu board shows (Menu/MenuDashboard.cs): today's prescription, consistency, and the
// reach trend — computed from the plan and the sessions actually recorded, in the shape the board's
// MenuDashboardModel already reads. Simulated sessions and sessions that never calibrated (a setup
// failure) are not the patient's work and are left out. `measured` is false until a real session exists;
// the board then shows the plan (prescription, program day) and empty progress.
import type { ActivityPrescription, Plan } from './plans.ts';
import { LIBRARY } from './exercises.ts';
import { buildVisit, type TherapistNote } from './visit.ts';

export const PROGRAM_DAYS = 84;        // a 12-week block
export const WEEK_SESSIONS_GOAL = 5;
const CALENDAR_WEEKS = 16;

const TITLES: Record<string, string> = { 'rehab.studio': 'Movement Studio', 'golf.adaptive': 'Golf' };

/** Local calendar day, YYYY-MM-DD, as the patient experiences it. */
export const dayKey = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const addDays = (d: Date, n: number) => { const x = new Date(d); x.setDate(x.getDate() + n); return x; };
const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate());

function taskDetail(a: ActivityPrescription): string {
  if (a.activityId === 'golf.adaptive') return `${a.targetCount} holes with a friend`;
  if (!a.exerciseKind) return a.note || `${a.targetCount} to do`;
  const name = (LIBRARY[a.exerciseKind]?.label ?? a.exerciseKind).replace(/^Seated /, '').toLowerCase();
  return `${a.targetCount} ${name}s · ${a.params.side ?? 'right'} · to ${a.params.targetDeg}°`;
}

/** Does this summary belong to that prescription? Summaries from before prescription ids name only the camera raise. */
const belongsTo = (s: any, a: ActivityPrescription) =>
  s.prescriptionId ? s.prescriptionId === a.id : s.exercise === 'seated_shoulder_raise' && a.id.startsWith('shoulder-raise');

export function buildDashboard(args: { plans: Plan[]; summaries: any[]; envelopes: any[]; now?: Date }) {
  const now = args.now ?? new Date(), today = startOfDay(now), todayKey = dayKey(today);
  const active = args.plans[args.plans.length - 1];
  // Exercise sessions that are the patient's own work; every activity record is checked against them.
  const excluded = new Set(args.summaries.filter(s => s.simulated || s.calibrated === false || !(s.attempted > 0)).map(s => String(s.exerciseId)));
  const summaries = args.summaries.filter(s => !excluded.has(String(s.exerciseId)));
  const envelopes = args.envelopes.filter(e => !excluded.has(String(e.activitySessionId)) && !e.flags?.includes('simulated')
    && Date.parse(e.endedAt) <= now.getTime());

  // Consistency: how many sessions ended on each local day.
  const perDay = new Map<string, number>();
  for (const e of envelopes) { const k = dayKey(new Date(e.endedAt)); perDay.set(k, (perDay.get(k) ?? 0) + 1); }

  let streakDays = 0;
  // A streak is still alive today until today ends, so it counts back from yesterday if today is empty.
  for (let d = perDay.has(todayKey) ? today : addDays(today, -1); perDay.has(dayKey(d)); d = addDays(d, -1)) streakDays++;
  const days = [...perDay.keys()].sort();
  let bestStreakDays = 0, run = 0, prev: Date | null = null;
  for (const k of days) {
    const d = new Date(`${k}T00:00:00`);
    run = prev && dayKey(addDays(prev, 1)) === k ? run + 1 : 1;
    bestStreakDays = Math.max(bestStreakDays, run); prev = d;
  }

  const monday = addDays(today, -((today.getDay() + 6) % 7));
  const weekSessionsDone = [...perDay.keys()].filter(k => k >= dayKey(monday) && k <= todayKey).length;

  // Program day counts from the first approved plan: the day the program started.
  const started = startOfDay(new Date(args.plans[0]?.approvedAt ?? now));
  const programDay = Math.max(1, Math.round((today.getTime() - started.getTime()) / 86_400_000) + 1);

  // 16 weeks, starting on a Sunday so each column is a clean week (same shape as the board's demo grid).
  let first = addDays(today, -(CALENDAR_WEEKS * 7 - 1)); first = addDays(first, -first.getDay());
  const calendar: { date: string; level: number }[] = [];
  for (let d = first; d <= today; d = addDays(d, 1)) calendar.push({ date: dayKey(d), level: Math.min(4, perDay.get(dayKey(d)) ?? 0) });

  const today_ = active.activities.map(a => {
    const doneToday = a.exerciseKind
      ? summaries.some(s => belongsTo(s, a) && dayKey(new Date(s.endedAt)) === todayKey && (s.valid ?? 0) >= a.targetCount)
      : envelopes.some(e => e.activityId === a.activityId && e.completed && dayKey(new Date(e.endedAt)) === todayKey);
    // A measured prescription is named for its movement, so two studio exercises are two different rows.
    const label = a.exerciseKind ? LIBRARY[a.exerciseKind]?.label.replace(/^Seated /, '') : undefined;
    const title = label ? label[0].toUpperCase() + label.slice(1) : TITLES[a.activityId];
    return { activityId: a.activityId, prescriptionId: a.id, title: title ?? a.activityId, detail: taskDetail(a), done: doneToday };
  });

  // Reach trend: the first measured prescription, its last eight real sessions.
  const primary = active.activities.find(a => a.exerciseKind);
  const measured = primary ? summaries.filter(s => belongsTo(s, primary) && s.medianValidPeakDeg != null)
    .sort((a, b) => String(a.endedAt).localeCompare(String(b.endedAt))) : [];
  const history = measured.slice(-8)
    .map(s => ({ label: new Date(s.endedAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric' }),
      attempted: s.attempted, valid: s.valid, medianPeakDeg: Math.round(s.medianValidPeakDeg * 10) / 10 }));
  // Week one: the median reach over the first seven days of measured work. "Since week one" on the board is
  // read against this, not against the oldest of the last eight — a busy day would otherwise make the
  // trend the difference between two sessions of the same afternoon.
  let baselinePeakDeg: number | null = null;
  if (measured.length) {
    const firstEnd = Date.parse(measured[0].endedAt) + 7 * 86_400_000;
    const week = measured.filter(s => Date.parse(s.endedAt) < firstEnd).map(s => s.medianValidPeakDeg).sort((a, b) => a - b);
    baselinePeakDeg = Math.round(week[Math.floor(week.length / 2)] * 10) / 10;
  }

  return {
    goal: active.goal.text,
    measured: envelopes.length > 0,
    streakDays, bestStreakDays,
    weekSessionsDone, weekSessionsGoal: WEEK_SESSIONS_GOAL,
    programDay, programTotalDays: PROGRAM_DAYS,
    targetDeg: primary ? Number(primary.params.targetDeg) : null,
    today: today_, history, baselinePeakDeg, calendar,
  };
}

/** Share of a full drive the patient's swing can reach at level 1 of their envelope. */
export const GOLF_POWER_FLOOR = 0.6;

/**
 * Rehab unlocks golf: the patient's swing power in the golf game follows where their measured reach sits
 * in the clinician's envelope. At the envelope floor (day 1) a full swing gives GOLF_POWER_FLOOR of a full
 * drive; each level up adds to it, and the envelope's top gives a full drive. A plan with nothing measured
 * and progressing leaves golf as it was.
 */
export function golfUnlock(plan: Plan) {
  const primary = plan.activities.find(a => a.exerciseKind && a.progression);
  if (!primary?.progression) return { swingPowerCap: 1, level: null, levels: null, targetDeg: null, message: null };
  const { minTargetDeg: lo, maxTargetDeg: hi, stepDeg } = primary.progression;
  const target = Number(primary.params.targetDeg);
  const fraction = hi > lo ? Math.min(1, Math.max(0, (target - lo) / (hi - lo))) : 1;
  const levels = Math.floor((hi - lo) / stepDeg) + 1, level = Math.min(levels, Math.floor((target - lo) / stepDeg) + 1);
  const swingPowerCap = Math.round((GOLF_POWER_FLOOR + (1 - GOLF_POWER_FLOOR) * fraction) * 100) / 100;
  return { swingPowerCap, level, levels, targetDeg: target,
    message: swingPowerCap >= 1 ? 'Full drive unlocked' : `Rehab level ${level} of ${levels} · ${Math.round(swingPowerCap * 100)}% of a full drive` };
}

/**
 * A plan change the patient has not seen yet: the active version is newer than the last one they saw (a finished
 * therapist visit marks it seen). What changed comes from the visit's own board, in its words and with its why,
 * so the menu's notice and the visit that explains it can never disagree. Null when there is nothing new.
 */
export function planUpdate(args: { plans: Plan[]; notes: TherapistNote[]; seen: { planVersion: number; at: string } | null }) {
  const active = args.plans[args.plans.length - 1];
  if (!active || (args.seen && args.seen.planVersion >= active.version)) return null;
  const visit = buildVisit({ plans: args.plans, notes: args.notes, seen: args.seen });
  const changes = visit.board.updates.filter(u => u.kind === 'added' || u.kind === 'changed' || u.kind === 'removed')
    .map(u => ({ kind: u.kind, heading: u.heading, detail: u.detail, why: u.why ?? null }));
  if (!changes.length) return null;
  return {
    planVersion: active.version, fromVersion: visit.since?.planVersion ?? null,
    origin: active.origin, approvedBy: active.approvedBy, approvedAt: active.approvedAt,
    headline: changes.length === 1 ? 'Your plan changed' : `${changes.length} changes to your plan`,
    changes: changes.slice(0, 3),
  };
}
