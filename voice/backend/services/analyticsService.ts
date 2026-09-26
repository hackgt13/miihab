import type { PatientService } from './patientService.ts';
import type {
  PainLog,
  PTSession,
  ExerciseLog,
  PatientAnalytics,
  PainTrend,
  SessionSummary,
} from '../types.ts';

export class AnalyticsService {
  private patientService: PatientService;

  constructor(patientService: PatientService) {
    this.patientService = patientService;
  }

  async getPatientAnalytics(patientId: string): Promise<PatientAnalytics> {
    const [sessions, painLogs, exercises, milestones] = await Promise.all([
      this.patientService.getAllSessions(patientId),
      this.patientService.getAllPainLogs(patientId),
      this.patientService.getPatientExercises(patientId, 100),
      this.patientService.getRecentMilestones(patientId, 50),
    ]);

    return {
      pain_trend: computePainTrend(painLogs),
      exercise_progression: computeExerciseProgression(exercises),
      session_frequency_per_week: computeSessionFrequency(sessions),
      streak_days: computeStreak(sessions),
      total_sessions: sessions.length,
      total_milestones: milestones.length,
    };
  }

  async computeSessionSummary(sessionId: string): Promise<SessionSummary> {
    const [exercises, painAvg, painLogs] = await Promise.all([
      this.patientService.getSessionExercises(sessionId),
      this.patientService.getSessionPainAverage(sessionId),
      this.patientService.getSessionPainLogs(sessionId),
    ]);

    const startPain = painLogs.find(p => p.phase === 'start')?.level ?? null;
    const endPain = [...painLogs].reverse().find(p => p.phase === 'end')?.level ?? null;
    const painDelta =
      startPain !== null && endPain !== null
        ? round2(endPain - startPain)
        : null;

    return {
      exercises,
      exercise_count: exercises.length,
      pain_avg: painAvg,
      pain_delta: painDelta,
      start_pain: startPain,
      end_pain: endPain,
    };
  }
}

// ── pure computation helpers ──────────────────────────────────────────────────

function computePainTrend(painLogs: PainLog[]): PainTrend {
  if (painLogs.length < 2) {
    return { trend: 'insufficient_data', slope: 0, recent_avg: null, early_avg: null, data_points: painLogs.length };
  }

  const levels = painLogs.map(l => Number(l.level));
  const n = levels.length;
  const xMean = (n - 1) / 2;
  const yMean = levels.reduce((a, b) => a + b, 0) / n;
  const numer = levels.reduce((sum, y, i) => sum + (i - xMean) * (y - yMean), 0);
  const denom = levels.reduce((sum, _, i) => sum + (i - xMean) ** 2, 0);
  const slope = denom ? numer / denom : 0;

  const mid = Math.floor(n / 2);
  const earlyAvg = mid > 0 ? round2(levels.slice(0, mid).reduce((a, b) => a + b, 0) / mid) : null;
  const recentAvg = n - mid > 0 ? round2(levels.slice(mid).reduce((a, b) => a + b, 0) / (n - mid)) : null;
  const trend = slope < -0.1 ? 'improving' : slope > 0.1 ? 'worsening' : 'stable';

  return { trend, slope: round3(slope), recent_avg: recentAvg, early_avg: earlyAvg, data_points: n };
}

function computeExerciseProgression(exercises: ExerciseLog[]): PatientAnalytics['exercise_progression'] {
  const byName = new Map<string, ExerciseLog[]>();
  for (const ex of exercises) {
    const name = ex.exercise_name ?? 'unknown';
    const arr = byName.get(name) ?? [];
    arr.push(ex);
    byName.set(name, arr);
  }

  const result: PatientAnalytics['exercise_progression'] = {};
  for (const [name, logs] of byName) {
    if (logs.length < 2) continue;
    const newest = logs[0];
    const oldest = logs[logs.length - 1];
    const newVol = (newest?.sets ?? 1) * (newest?.reps ?? 1);
    const oldVol = (oldest?.sets ?? 1) * (oldest?.reps ?? 1);
    result[name] = {
      sessions: logs.length,
      volume_change_pct: oldVol ? round1((newVol - oldVol) / oldVol * 100) : 0,
      latest_sets: newest?.sets,
      latest_reps: newest?.reps,
    };
  }
  return result;
}

function computeSessionFrequency(sessions: PTSession[]): number | null {
  if (sessions.length < 2) return null;
  const cutoff = new Date(Date.now() - 30 * 86_400_000);
  const recent = sessions.filter(s => s.started_at && new Date(s.started_at) > cutoff);
  return round2(recent.length / 4.3);
}

function computeStreak(sessions: PTSession[]): number {
  if (sessions.length === 0) return 0;

  const dates = new Set(
    sessions
      .filter(s => s.started_at)
      .map(s => new Date(s.started_at!).toISOString().slice(0, 10)),
  );
  if (dates.size === 0) return 0;

  const latest = [...dates].sort().at(-1)!;
  const daysSince = Math.floor((Date.now() - new Date(latest).getTime()) / 86_400_000);
  if (daysSince > 2) return 0;

  let count = 0;
  let check = new Date(latest);
  while (dates.has(check.toISOString().slice(0, 10))) {
    count++;
    check = new Date(check.getTime() - 86_400_000);
  }
  return count;
}

function round1(n: number) { return Math.round(n * 10) / 10; }
function round2(n: number) { return Math.round(n * 100) / 100; }
function round3(n: number) { return Math.round(n * 1000) / 1000; }
