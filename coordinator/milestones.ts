// Something worth telling your friends, and only if you choose to.
//
// A milestone here is about turning up, never about how far anyone reached:
// friends.ts keeps one patient's measurements away from another, and a share
// that said "85° today" would hand every friend the comparison that rule exists
// to prevent. So the facts are the dashboard's consistency numbers — a streak,
// a best run, a full week — which the friend profile already shows.
//
// Each milestone has a stable id, so once it is shared or waved away it never
// comes back, and the next one is a genuinely new fact rather than the same one
// on a different day.
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

export interface Milestone {
  id: string;
  /** The plain fact, which is also the card's words when no model is configured. */
  fact: string;
}

const STREAK_LANDMARKS = [3, 7, 14, 21, 30, 45, 60, 90];

const dayKey = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

/** The milestone the dashboard shows today, if any. Streak landmarks first, then a new best, then a full week. */
export function milestoneFrom(d: { streakDays: number; bestStreakDays: number; weekSessionsDone: number; weekSessionsGoal: number },
                              now = new Date()): Milestone | null {
  // A run is named by the day it started, which does not move as it grows.
  const startOf = (days: number) => { const s = new Date(now); s.setHours(0, 0, 0, 0); s.setDate(s.getDate() - days + 1); return dayKey(s); };
  if (STREAK_LANDMARKS.includes(d.streakDays))
    return { id: `streak-${d.streakDays}-${startOf(d.streakDays)}`, fact: `${d.streakDays} days in a row` };
  if (d.streakDays >= 3 && d.streakDays === d.bestStreakDays)
    return { id: `best-${d.streakDays}-${startOf(d.streakDays)}`, fact: `longest run yet: ${d.streakDays} days in a row` };
  if (d.weekSessionsGoal > 0 && d.weekSessionsDone >= d.weekSessionsGoal) {
    const monday = new Date(now); monday.setHours(0, 0, 0, 0); monday.setDate(monday.getDate() - ((monday.getDay() + 6) % 7));
    return { id: `week-${dayKey(monday)}`, fact: `every session this week, ${d.weekSessionsDone} of ${d.weekSessionsGoal}` };
  }
  return null;
}

/** Which milestones have been shared or waved away. */
export class MilestoneStore {
  private file: string;
  private done: Record<string, { answer: 'shared' | 'dismissed'; at: string }>;

  constructor(dir: string) {
    mkdirSync(dir, { recursive: true });
    this.file = resolve(dir, 'milestones.json');
    try { this.done = existsSync(this.file) ? JSON.parse(readFileSync(this.file, 'utf8')) : {}; } catch { this.done = {}; }
  }

  answered(id: string): boolean { return id in this.done; }

  answer(id: string, answer: 'shared' | 'dismissed', now = new Date()): void {
    this.done[id] = { answer, at: now.toISOString() };
    writeFileSync(this.file, JSON.stringify(this.done, null, 2));
  }
}
