import type { PatientService } from './patientService.ts';
import type { CoordinatorService } from './coordinatorService.ts';
import type {
  PainLog,
  PTSession,
  MeasuredExercise,
  PatientAnalytics,
  PainTrend,
  SessionSummary,
} from '../types.ts';

export class AnalyticsService {
  private patientService: PatientService;
  private coordinator: CoordinatorService;

  constructor(patientService: PatientService, coordinator: CoordinatorService) {
    this.patientService = patientService;
    this.coordinator = coordinator;
  }

  async getPatientAnalytics(patientId: string): Promise<PatientAnalytics> {
    const [sessions, painLogs, measured, milestones] = await Promise.all([
      this.patientService.getAllSessions(patientId),
      this.patientService.getAllPainLogs(patientId),
      this.coordinator.results({ limit: 50 }).catch(() => null),
      this.patientService.getRecentMilestones(patientId, 50),
    ]);

    return {
      pain_trend: computePainTrend(painLogs),
      measured_progress: measured && computeMeasuredProgress(measured),
      session_frequency_per_week: computeSessionFrequency(sessions),
      streak_days: computeStreak(sessions),
      total_sessions: sessions.length,
      total_milestones: milestones.length,
    };
  }

  /** Pain the patient reported in this conversation + what the coordinator measured since it started. */
  async computeSessionSummary(sessionId: string, startedAt: string): Promise<SessionSummary> {
    const [measured, painAvg, painLogs] = await Promise.all([
      this.coordinator.results({ since: startedAt }).catch(() => [] as MeasuredExercise[]),
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
      measured,
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

// Real (non-simulated) measured sessions only; `results` is newest first.
function computeMeasuredProgress(results: MeasuredExercise[]): NonNullable<PatientAnalytics['measured_progress']> {
  const real = results.filter(r => !r.simulated && r.attempted > 0);
  const peaks = real.map(r => r.median_peak_deg).filter((d): d is number => d != null);
  return {
    sessions: real.length,
    latest: real[0] ?? null,
    first_median_peak_deg: peaks.at(-1) ?? null,
    latest_median_peak_deg: peaks[0] ?? null,
  };
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

function round2(n: number) { return Math.round(n * 100) / 100; }
function round3(n: number) { return Math.round(n * 1000) / 1000; }
