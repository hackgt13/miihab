// The cross-activity session record. One shape for every activity, written by one writer.
//
// Before this existed, golf appended per-shot rows to a jsonl in Unity's persistentDataPath — a
// directory that differs between the Editor and a built Player, and that the coordinator cannot
// read — while the exercise engine wrote per-session summaries server-side. Nothing could compare
// them, and the clinician portal could only ever see one activity.
//
// Deliberately NOT in this envelope: anything scored. A stroke count and a rep count are not
// comparable numbers, so activity-specific figures live in `payload`, behind a versioned
// discriminant, and nothing upstream parses them. What IS here is what generalises: dose,
// adherence, tracking quality, and a normalised failure vocabulary.

export const ACTIVITY_SUMMARY_SCHEMA = 'kinesthetic.activity.v1';

/** Failure modes that mean the same thing for every activity. Exercise-specific reasons stay in payload. */
export const SHARED_FLAGS = ['tracking_lost', 'interrupted', 'out_of_view', 'not_completed'] as const;
export type SharedFlag = typeof SHARED_FLAGS[number];

export interface Dose { prescribed: number | null; attempted: number; valid: number }
export interface Metric { name: string; value: number | null; unit: string }

/** Golf is a two-subject record; a rehab session is one. The envelope carries both without special cases. */
export interface ActivitySubject {
  subjectId: string;
  role: 'patient' | 'companion';
  dose: Dose;
  primaryMetric: Metric | null;
}

export interface ActivitySummary {
  schema: typeof ACTIVITY_SUMMARY_SCHEMA;
  activitySessionId: string;
  activityId: string;
  exerciseKinds: string[];          // [] when the activity measured nothing clinical
  venueId: string | null;
  patientId: string | null;         // a real patient entity arrives with plan schema v2
  planVersion: number | null;
  startedAt: string;
  endedAt: string;
  durationMs: number;
  completed: boolean;               // did the activity reach its own terminal condition
  subjects: ActivitySubject[];
  trackingQuality: { validFrameRatio: number | null; lossEvents: number };
  flags: SharedFlag[];
  payload: { kind: string; schemaVersion: string; data: unknown };
}

const str = (v: unknown, field: string, max = 200) => {
  if (typeof v !== 'string' || !v.trim()) throw Error(`${field} must be a non-empty string`);
  if (v.length > max) throw Error(`${field} must be at most ${max} characters`);
  return v;
};
const int = (v: unknown, field: string, lo = 0, hi = 1e6) => {
  const n = Number(v);
  if (!Number.isFinite(n) || n < lo || n > hi) throw Error(`${field} must be a number in [${lo}, ${hi}]`);
  return Math.round(n);
};
const nullableNumber = (v: unknown, field: string) => {
  if (v == null) return null;
  const n = Number(v);
  if (!Number.isFinite(n)) throw Error(`${field} must be a number or null`);
  return n;
};
const iso = (v: unknown, field: string) => {
  const s = str(v, field, 40);
  if (Number.isNaN(Date.parse(s))) throw Error(`${field} must be an ISO timestamp`);
  return s;
};
const dose = (v: any, field: string): Dose => {
  if (!v || typeof v !== 'object') throw Error(`${field} is required`);
  const attempted = int(v.attempted, `${field}.attempted`), valid = int(v.valid, `${field}.valid`);
  if (valid > attempted) throw Error(`${field}.valid cannot exceed attempted`);
  const prescribed = v.prescribed == null ? null : int(v.prescribed, `${field}.prescribed`);
  return { prescribed, attempted, valid };
};

/**
 * Validate and normalise an envelope from an untrusted client. Unity posts these over loopback,
 * but the coordinator is the thing that has to still be readable in six months, so it decides
 * what a session record may contain rather than storing whatever arrives.
 */
export function parseActivitySummary(input: unknown): ActivitySummary {
  const v = input as any;
  if (!v || typeof v !== 'object') throw Error('Body must be an object');
  if (v.schema !== ACTIVITY_SUMMARY_SCHEMA) throw Error(`schema must be "${ACTIVITY_SUMMARY_SCHEMA}"`);
  const subjects = Array.isArray(v.subjects) ? v.subjects : [];
  if (!subjects.length || subjects.length > 8) throw Error('subjects must hold between 1 and 8 entries');
  const payload = v.payload ?? {};
  const data = JSON.stringify(payload.data ?? null);
  if (data.length > 64 * 1024) throw Error('payload.data must serialise to at most 64 KiB');
  const startedAt = iso(v.startedAt, 'startedAt'), endedAt = iso(v.endedAt, 'endedAt');
  const flags = (Array.isArray(v.flags) ? v.flags : []).map((f: unknown) => {
    if (!SHARED_FLAGS.includes(f as SharedFlag)) throw Error(`flags may only contain ${SHARED_FLAGS.join(', ')}`);
    return f as SharedFlag;
  });
  const ratio = nullableNumber(v.trackingQuality?.validFrameRatio, 'trackingQuality.validFrameRatio');
  if (ratio != null && (ratio < 0 || ratio > 1)) throw Error('trackingQuality.validFrameRatio must be in [0,1]');
  return {
    schema: ACTIVITY_SUMMARY_SCHEMA,
    activitySessionId: str(v.activitySessionId, 'activitySessionId', 64),
    activityId: str(v.activityId, 'activityId', 64),
    exerciseKinds: (Array.isArray(v.exerciseKinds) ? v.exerciseKinds : []).slice(0, 8).map((e: unknown, i: number) => str(e, `exerciseKinds[${i}]`, 64)),
    venueId: v.venueId == null ? null : str(v.venueId, 'venueId', 64),
    patientId: v.patientId == null ? null : str(v.patientId, 'patientId', 64),
    planVersion: v.planVersion == null ? null : int(v.planVersion, 'planVersion', 1, 1e6),
    startedAt, endedAt,
    durationMs: int(v.durationMs, 'durationMs', 0, 24 * 3600 * 1000),
    completed: !!v.completed,
    subjects: subjects.map((s: any, i: number) => ({
      subjectId: str(s?.subjectId, `subjects[${i}].subjectId`, 64),
      role: s?.role === 'companion' ? 'companion' as const : 'patient' as const,
      dose: dose(s?.dose, `subjects[${i}].dose`),
      primaryMetric: s?.primaryMetric == null ? null : {
        name: str(s.primaryMetric.name, `subjects[${i}].primaryMetric.name`, 64),
        value: nullableNumber(s.primaryMetric.value, `subjects[${i}].primaryMetric.value`),
        unit: str(s.primaryMetric.unit, `subjects[${i}].primaryMetric.unit`, 24),
      },
    })),
    trackingQuality: { validFrameRatio: ratio, lossEvents: int(v.trackingQuality?.lossEvents ?? 0, 'trackingQuality.lossEvents') },
    flags,
    payload: {
      kind: str(payload.kind, 'payload.kind', 64),
      schemaVersion: str(payload.schemaVersion ?? '1', 'payload.schemaVersion', 24),
      data: JSON.parse(data),
    },
  };
}

/**
 * Build an envelope from a RepSession summary, so an exercise session and a golf round land in the
 * same shape. Proves the envelope generalises rather than being a golf-shaped record with a
 * clinical field bolted on.
 */
export function activitySummaryFromExercise(args: {
  activitySessionId: string; activityId: string; venueId?: string | null; patientId?: string | null;
  startedAt: string; endedAt: string; measured: Record<string, any>;
}): ActivitySummary {
  const m = args.measured;
  const started = Date.parse(args.startedAt), ended = Date.parse(args.endedAt);
  const flags: SharedFlag[] = [];
  if (m.trackingLossEvents > 0) flags.push('tracking_lost');
  if (m.prescribed != null && m.valid < m.prescribed) flags.push('not_completed');
  return parseActivitySummary({
    schema: ACTIVITY_SUMMARY_SCHEMA,
    activitySessionId: args.activitySessionId,
    activityId: args.activityId,
    exerciseKinds: [m.exerciseKind ?? m.algorithmVersion].filter(Boolean),
    venueId: args.venueId ?? null,
    patientId: args.patientId ?? null,
    planVersion: m.planVersion ?? null,
    startedAt: args.startedAt, endedAt: args.endedAt,
    durationMs: Math.max(0, ended - started),
    completed: m.prescribed != null ? m.valid >= m.prescribed : m.valid > 0,
    subjects: [{
      subjectId: 'patient', role: 'patient',
      dose: { prescribed: m.prescribed ?? null, attempted: m.attempted ?? 0, valid: m.valid ?? 0 },
      primaryMetric: { name: 'medianValidPeak', value: m.medianValidPeakDeg ?? null, unit: 'deg' },
    }],
    trackingQuality: { validFrameRatio: m.validFrameRatio ?? null, lossEvents: m.trackingLossEvents ?? 0 },
    flags,
    payload: { kind: String(m.exerciseKind ?? 'exercise'), schemaVersion: '1', data: m },
  });
}
