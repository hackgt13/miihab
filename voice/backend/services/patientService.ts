import { createClient } from '@supabase/supabase-js';
import type { SupabaseClient } from '@supabase/supabase-js';
import { randomUUID } from 'node:crypto';
import type { Patient, PTSession, PainLog, Milestone, PlanReviewRequest } from '../types.ts';

export class PatientService {
  private db: SupabaseClient;

  constructor(url: string, serviceKey: string) {
    this.db = createClient(url, serviceKey);
  }

  // ── connectivity check ────────────────────────────────────────────────────

  async ping(): Promise<unknown> {
    const { error } = await this.db.from('patients').select('id').limit(1);
    return error ?? null;
  }

  // ── patients ──────────────────────────────────────────────────────────────

  async getPatient(patientId: string): Promise<Patient | null> {
    const { data } = await this.db
      .from('patients')
      .select('*')
      .eq('id', patientId)
      .maybeSingle();
    return (data as Patient | null) ?? null;
  }

  async upsertPatient(
    patientId: string,
    fields: Partial<Omit<Patient, 'id' | 'created_at'>>,
  ): Promise<Patient> {
    const payload = { ...filterDefined(fields), id: patientId };
    const { data, error } = await this.db
      .from('patients')
      .upsert(payload)
      .select()
      .single();
    if (error) throw error;
    return data as Patient;
  }

  // ── sessions ──────────────────────────────────────────────────────────────

  async createSession(patientId: string): Promise<{ id: string; startedAt: string }> {
    // Ensure patient row exists — Alex will populate the profile via get_patient_profile
    await this.db.from('patients').upsert({ id: patientId }, { onConflict: 'id', ignoreDuplicates: true });

    const sessionId = randomUUID();
    const startedAt = now();
    const { error } = await this.db.from('sessions').insert({
      id: sessionId,
      patient_id: patientId,
      started_at: startedAt,
    });
    if (error) throw error;
    return { id: sessionId, startedAt };
  }

  /** Stamps the end and links the coordinator's measured exercises for this conversation. */
  async endSession(
    sessionId: string,
    opts: { notes?: string; exerciseIds?: string[]; planVersion?: number | null } = {},
  ): Promise<void> {
    const payload: Record<string, unknown> = { ended_at: now() };
    if (opts.notes) payload['notes'] = opts.notes;
    if (opts.exerciseIds) payload['exercise_ids'] = opts.exerciseIds;
    if (opts.planVersion != null) payload['plan_version'] = opts.planVersion;
    const { error } = await this.db
      .from('sessions')
      .update(payload)
      .eq('id', sessionId);
    if (error) throw error;
  }

  async getRecentSessions(patientId: string, limit = 5): Promise<PTSession[]> {
    const { data } = await this.db
      .from('sessions')
      .select('*')
      .eq('patient_id', patientId)
      .order('started_at', { ascending: false })
      .limit(limit);
    return (data ?? []) as PTSession[];
  }

  async getAllSessions(patientId: string): Promise<PTSession[]> {
    const { data } = await this.db
      .from('sessions')
      .select('*')
      .eq('patient_id', patientId)
      .order('started_at', { ascending: false });
    return (data ?? []) as PTSession[];
  }

  // ── pain logs ─────────────────────────────────────────────────────────────

  async logPain(
    patientId: string,
    sessionId: string,
    level: number,
    context?: string,
    phase?: string,
  ): Promise<PainLog> {
    const record = {
      id: randomUUID(),
      patient_id: patientId,
      session_id: sessionId,
      level,
      context: context ?? null,
      phase: phase ?? null,
      logged_at: now(),
    };
    const { data, error } = await this.db
      .from('pain_logs')
      .insert(record)
      .select()
      .single();
    if (error) throw error;
    return data as PainLog;
  }

  async getRecentPainLogs(patientId: string, limit = 20): Promise<PainLog[]> {
    const { data } = await this.db
      .from('pain_logs')
      .select('*')
      .eq('patient_id', patientId)
      .order('logged_at', { ascending: false })
      .limit(limit);
    return (data ?? []) as PainLog[];
  }

  async getSessionPainLogs(sessionId: string): Promise<PainLog[]> {
    const { data } = await this.db
      .from('pain_logs')
      .select('level, phase, logged_at')
      .eq('session_id', sessionId)
      .order('logged_at', { ascending: true });
    return (data ?? []) as PainLog[];
  }

  async getAllPainLogs(patientId: string): Promise<PainLog[]> {
    const { data } = await this.db
      .from('pain_logs')
      .select('*')
      .eq('patient_id', patientId)
      .order('logged_at', { ascending: true });
    return (data ?? []) as PainLog[];
  }

  async getSessionPainAverage(sessionId: string): Promise<number | null> {
    const { data } = await this.db
      .from('pain_logs')
      .select('level')
      .eq('session_id', sessionId);
    if (!data || data.length === 0) return null;
    const sum = data.reduce((acc, r) => acc + Number(r.level), 0);
    return Math.round((sum / data.length) * 100) / 100;
  }

  // ── plan review requests ──────────────────────────────────────────────────
  // Alex cannot change the plan. It files a request; the physician decides in the portal.

  async addPlanReviewRequest(
    patientId: string,
    sessionId: string,
    planVersion: number | null,
    reason: string,
    category: string,
  ): Promise<PlanReviewRequest> {
    const record = {
      id: randomUUID(),
      patient_id: patientId,
      session_id: sessionId,
      plan_version: planVersion,
      reason,
      category,
      status: 'open',
      created_at: now(),
    };
    const { data, error } = await this.db
      .from('plan_review_requests')
      .insert(record)
      .select()
      .single();
    if (error) throw error;
    return data as PlanReviewRequest;
  }

  // ── milestones ────────────────────────────────────────────────────────────

  async addMilestone(
    patientId: string,
    description: string,
    category: string,
  ): Promise<Milestone> {
    const record = {
      id: randomUUID(),
      patient_id: patientId,
      description,
      category,
      logged_at: now(),
    };
    const { data, error } = await this.db
      .from('milestones')
      .insert(record)
      .select()
      .single();
    if (error) throw error;
    return data as Milestone;
  }

  async getRecentMilestones(patientId: string, limit = 10): Promise<Milestone[]> {
    const { data } = await this.db
      .from('milestones')
      .select('*')
      .eq('patient_id', patientId)
      .order('logged_at', { ascending: false })
      .limit(limit);
    return (data ?? []) as Milestone[];
  }
}

// ── helpers ───────────────────────────────────────────────────────────────────

function now(): string {
  return new Date().toISOString();
}

function filterDefined(obj: Record<string, unknown>): Record<string, unknown> {
  return Object.fromEntries(Object.entries(obj).filter(([, v]) => v !== undefined && v !== null));
}
