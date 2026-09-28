// The patient's visit with their physical therapist (Unity: Kinesthetic/Visit). The therapist stands at a
// whiteboard and talks the patient through what changed in their program since they last visited.
//
// Two halves, read together as one reply from GET /api/visit:
//   board  — the whiteboard: program updates, "from your licensed therapist". Computed from the plan's
//            version history (what was actually approved) plus notes the therapist left for the patient.
//            Nothing here is invented: with no change since the last visit the board says what the program
//            is, not that something changed.
//   speech — the lines the therapist says, in order, each one revealing the board item it talks about.
//            Text only today. Each line carries `audio: null` so a voice (the ElevenLabs PT in voice/) can
//            fill it later without the scene changing shape: Unity plays audio when a line has it and
//            falls back to timed captions when it does not.
//
// How the program changes (the mechanism, not just the display):
//   - The therapist changes it: POST /api/visit/program-update approves a new plan version (plans.ts, the
//     same validated, immutable versioning the portal uses) and, in the same request, leaves the patient a
//     note about it. The note is checked before the plan is approved, so a bad note never leaves a plan
//     change without its explanation.
//   - The plan changes itself, inside the envelope the clinician set (progression.ts, origin
//     'auto-progression'). The visit says so rather than presenting it as the therapist's decision.
//   - The patient answers: the visit ends by asking whether there is anything to relay to the care team
//     (`ask` in the reply). A quick reply or a typed message goes to POST /api/visit/replies, the clinician
//     reads it in the portal, and the therapist's spoken acknowledgement comes back in the same response.
//     The acknowledgement is fixed text per kind, not generated: a patient reporting pain always hears the
//     same safety advice.
//   - The patient sees it: when a visit finishes, Unity marks the plan version seen (POST /api/visit/seen).
//     The next visit's board is everything approved since then, however many versions that spans, each
//     change with the reason of the version that made it.
//
// The therapist's identity is configuration, not data the patient can edit. Until a real clinician is
// attached it is labelled `sample`, matching the project's habit of marking demo people honestly.
import { mkdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { randomUUID } from 'node:crypto';
import type { ActivityPrescription, ApproveInput, Plan, PlanStore } from './plans.ts';
import { LIBRARY } from './exercises.ts';
import { activityById } from './activities.ts';

export const VISIT_SCHEMA = 'kinesthetic.visit.v1';
const NOTE_LIMIT = 280;
const NOTES_SHOWN = 3;
const AUTHOR_LIMIT = 60;
/** The whiteboard fits this many lines at its density; changes come first, then notes. */
export const BOARD_LIMIT = 6;

export interface Therapist { name: string; credentials: string; sample: boolean }

export interface TherapistNote {
  id: string; text: string; author: string; at: string;
  /** The plan version this note explains, when it came with a program update. */
  planVersion?: number | null;
  /** Written by the demo seeder (seed-visit.ts), so it can be told apart and removed. */
  seeded?: boolean;
}

/** One line on the whiteboard. `kind` lets the board mark a change apart from a standing instruction. */
export interface BoardUpdate {
  kind: 'added' | 'changed' | 'removed' | 'program' | 'note';
  heading: string; detail: string;
  /** Why it changed: the rationale of the plan version that made the change. */
  why?: string;
  /** True when progression moved it inside the clinician's envelope, not the clinician by hand. */
  automatic?: boolean;
  planVersion?: number;
}

/** One speech bubble. `reveal` is how many board updates are showing once this line starts. */
export interface SpeechLine { id: string; text: string; reveal: number; audio: string | null }

export interface Visit {
  schema: typeof VISIT_SCHEMA;
  therapist: Therapist;
  planVersion: number;
  /** The plan version the board is measured from: the last one the patient saw, or the one before this. */
  since: { planVersion: number; lastVisit: string | null } | null;
  board: { title: string; attribution: string; updatedAt: string; updates: BoardUpdate[] };
  speech: SpeechLine[];
  /** The question the visit ends on. Unity shows it once the speech is done. */
  ask: typeof ASK;
}

export const DEFAULT_THERAPIST: Therapist = { name: 'Alex', credentials: 'PT, DPT', sample: true };

export function therapistFromEnv(env: Record<string, string | undefined> = process.env): Therapist {
  const name = env.KINESTHETIC_THERAPIST_NAME?.trim();
  if (!name) return DEFAULT_THERAPIST;
  return { name, credentials: env.KINESTHETIC_THERAPIST_CREDENTIALS?.trim() || 'PT', sample: false };
}

/** What the patient relays to their care team at the end of a visit. */
export type ReplyKind = 'fine' | 'hurt' | 'easy' | 'hard' | 'message';
export interface PatientReply { id: string; kind: ReplyKind; text: string; planVersion: number | null; at: string; seeded?: boolean }

/** The question the visit ends on, and the quick replies a head or a pointer can pick without a keyboard. */
export const ASK = {
  prompt: 'Anything you want to relay to your coach?',
  line: "Before you go: is there anything you want me to know? Pick one below, or write me a message.",
  quickReplies: [
    { kind: 'fine', label: 'All good' },
    { kind: 'easy', label: 'Too easy' },
    { kind: 'hard', label: 'Too hard' },
    { kind: 'hurt', label: 'Something hurt' },
  ] as { kind: ReplyKind; label: string }[],
};
const REPLY_TEXT: Record<Exclude<ReplyKind, 'message'>, string> = {
  fine: 'Everything feels fine.', easy: 'The program feels too easy.', hard: 'The program feels too hard.', hurt: 'Something hurt.',
};
/** What the therapist says back. Fixed per kind, so pain always gets the same safety advice. */
export const ACKNOWLEDGEMENTS: Record<ReplyKind, string> = {
  fine: "Good to hear. Keep it steady, and I'll keep watching how it goes.",
  easy: "Noted. If it stays easy for a couple more sessions, I'll move you up a step.",
  hard: "Noted. We can ease off. I'll look at your last few sessions before the next one.",
  hurt: "Thanks for telling me. Stop any exercise that hurts. If the pain is sharp or doesn't settle, call the clinic rather than waiting for your next visit.",
  message: "Thanks, I've got that. I'll read it before your next session.",
};
const REPLY_LIMIT = 500;

interface VisitFile {
  schema: 'kinesthetic.visit-notes.v1';
  notes: TherapistNote[];
  seen: { planVersion: number; at: string; seeded?: boolean } | null;
  replies: PatientReply[];
}

/**
 * The visit's own records: the therapist's notes and which plan version the patient last saw. Read from
 * disk on every call, like PlanStore, so the seeder and a running coordinator never overwrite each other
 * with a stale copy.
 */
export class VisitStore {
  private file: string;

  constructor(dir: string) {
    mkdirSync(dir, { recursive: true });
    this.file = resolve(dir, 'visit-notes.json');
  }

  private read(): VisitFile {
    const raw = existsSync(this.file) ? JSON.parse(readFileSync(this.file, 'utf8')) : {};
    return { schema: 'kinesthetic.visit-notes.v1', notes: raw.notes ?? [], seen: raw.seen ?? null, replies: raw.replies ?? [] };
  }
  private write(data: VisitFile) { writeFileSync(this.file, JSON.stringify(data, null, 2)); }

  list(): TherapistNote[] { return this.read().notes; }
  get seen() { return this.read().seen; }

  /** Validates a note without storing it, so a program update can refuse before it approves anything. */
  static check(input: { text?: unknown; author?: unknown }) {
    const text = String(input.text ?? '').trim().replace(/\s+/g, ' ');
    if (!text) throw Error('A note needs text.');
    if (text.length > NOTE_LIMIT) throw Error(`A note is at most ${NOTE_LIMIT} characters.`);
    return { text, author: String(input.author ?? '').trim().slice(0, AUTHOR_LIMIT) };
  }

  add(input: { text?: unknown; author?: unknown; planVersion?: number | null; seeded?: boolean }, now = new Date()): TherapistNote {
    const note: TherapistNote = { id: randomUUID(), ...VisitStore.check(input), at: now.toISOString() };
    if (input.planVersion != null) note.planVersion = input.planVersion;
    if (input.seeded) note.seeded = true;
    const data = this.read();
    data.notes.push(note);
    this.write(data);
    return note;
  }

  remove(id: string): boolean {
    const data = this.read();
    const before = data.notes.length;
    data.notes = data.notes.filter(n => n.id !== id);
    if (data.notes.length === before) return false;
    this.write(data);
    return true;
  }

  /** The patient has seen the program as of this version. Never moves backwards, except for the seeder. */
  markSeen(planVersion: number, now = new Date(), seeded = false) {
    if (!Number.isInteger(planVersion) || planVersion < 1) throw Error('planVersion must be a plan version number.');
    const data = this.read();
    if (data.seen && data.seen.planVersion > planVersion && !seeded) return data.seen;
    data.seen = { planVersion, at: now.toISOString(), ...(seeded ? { seeded: true } : {}) };
    this.write(data);
    return data.seen;
  }

  replies(): PatientReply[] { return this.read().replies; }

  /**
   * The patient relays something to the care team. A quick reply's kind carries its own text; a message
   * needs the patient's words, and a quick reply may add some.
   */
  addReply(input: { kind?: unknown; text?: unknown; planVersion?: unknown }, now = new Date()) {
    const kind = String(input.kind ?? 'message') as ReplyKind;
    if (!Object.hasOwn(ACKNOWLEDGEMENTS, kind)) throw Error(`kind must be one of ${Object.keys(ACKNOWLEDGEMENTS).join(', ')}.`);
    const typed = String(input.text ?? '').trim().replace(/\s+/g, ' ');
    if (typed.length > REPLY_LIMIT) throw Error(`A message is at most ${REPLY_LIMIT} characters.`);
    if (kind === 'message' && !typed) throw Error('A message needs text.');
    const text = kind === 'message' ? typed : typed || REPLY_TEXT[kind];
    const version = input.planVersion == null ? null : Number(input.planVersion);
    const reply: PatientReply = { id: randomUUID(), kind, text, planVersion: Number.isInteger(version) ? version : null, at: now.toISOString() };
    const data = this.read();
    data.replies.push(reply);
    this.write(data);
    return { reply, acknowledgement: ACKNOWLEDGEMENTS[kind] };
  }

  /** Mark a note as the seeder's, dated when it says. */
  markSeeded(id: string, at: Date) {
    const data = this.read();
    for (const n of data.notes) if (n.id === id) { n.seeded = true; n.at = at.toISOString(); }
    this.write(data);
  }

  /** Take out what the demo seeder wrote. */
  clean(): number {
    const data = this.read();
    const before = data.notes.length;
    data.notes = data.notes.filter(n => !n.seeded);
    const removed = before - data.notes.length + (data.seen?.seeded ? 1 : 0);
    if (data.seen?.seeded) data.seen = null;
    this.write(data);
    return removed;
  }
}

export interface ProgramUpdateInput extends ApproveInput {
  /** What the patient reads on the whiteboard about this change. Optional, but checked before approval. */
  patientNote?: string;
}

/**
 * The therapist changes the program: a new approved plan version and, with it, a note to the patient.
 * Always a clinician decision: `origin` and `proposalId` from the request are ignored, as on /api/plans.
 */
export function applyProgramUpdate(plans: PlanStore, visit: VisitStore, input: ProgramUpdateInput) {
  const { patientNote, origin: _origin, proposalId: _proposal, ...change } = input;
  const note = patientNote == null || String(patientNote).trim() === '' ? null
    : VisitStore.check({ text: patientNote, author: change.approvedBy });
  if (!String(change.approvedBy ?? '').trim()) throw Error('approvedBy is required: a program update is signed.');
  const plan = plans.approve(change);
  return { plan, note: note ? visit.add({ ...note, planVersion: plan.version }) : null };
}

/** What a prescription is called on a whiteboard: "Shoulder raises", "Golf". */
export function prescriptionName(a: ActivityPrescription): string {
  if (a.exerciseKind) {
    const label = (LIBRARY[a.exerciseKind]?.label ?? a.exerciseKind).replace(/^Seated /, '');
    return label.charAt(0).toUpperCase() + label.slice(1) + 's';
  }
  return activityById(a.activityId)?.displayName ?? a.activityId;
}

/** The dose in a few words: "8 reps · right · to 45°", "9 holes with a friend". */
export function prescriptionDose(a: ActivityPrescription): string {
  if (a.activityId === 'golf.adaptive') return `${a.targetCount} holes with a friend`;
  if (!a.exerciseKind) return a.note || `${a.targetCount} to do`;
  const parts = [`${a.targetCount} reps`];
  if (a.params.side) parts.push(String(a.params.side));
  if (a.params.targetDeg != null) parts.push(`to ${a.params.targetDeg}°`);
  if (Number(a.params.loadKg) > 0) parts.push(`${a.params.loadKg} kg`);
  return parts.join(' · ');
}

/** Each thing that differs between two versions of one prescription, keyed so a change can be traced to its version. */
function prescriptionDiff(was: ActivityPrescription, a: ActivityPrescription): { key: string; text: string }[] {
  const parts: { key: string; text: string }[] = [];
  const t0 = was.params.targetDeg, t1 = a.params.targetDeg;
  if (t0 != null && t1 != null && Number(t0) !== Number(t1))
    parts.push({ key: 'target', text: `target ${t0}° → ${t1}°${Number(t1) > Number(t0) ? ', a little higher' : ', a step back'}` });
  if (was.targetCount !== a.targetCount) {
    const unit = a.activityId === 'golf.adaptive' ? 'holes' : a.exerciseKind ? 'reps' : 'to do';
    parts.push({ key: 'count', text: `${was.targetCount} → ${a.targetCount} ${unit}` });
  }
  if (was.params.side !== a.params.side && a.params.side) parts.push({ key: 'side', text: `now your ${a.params.side} side` });
  if (Number(was.params.loadKg ?? 0) !== Number(a.params.loadKg ?? 0) && a.params.loadKg != null)
    parts.push({ key: 'load', text: `weight ${was.params.loadKg ?? 0} → ${a.params.loadKg} kg` });
  if (was.note !== a.note && a.note) parts.push({ key: 'note', text: a.note });
  return parts;
}

/** The difference between two plan versions, as the patient would want it told. */
export function programChanges(before: Plan, after: Plan): BoardUpdate[] {
  const updates: BoardUpdate[] = [];
  const old = new Map(before.activities.map(a => [a.id, a]));
  for (const a of after.activities) {
    const was = old.get(a.id);
    if (!was) { updates.push({ kind: 'added', heading: `New: ${prescriptionName(a)}`, detail: prescriptionDose(a) }); continue; }
    const parts = prescriptionDiff(was, a);
    if (parts.length) updates.push({ kind: 'changed', heading: prescriptionName(a), detail: parts.map(p => p.text).join(' · ') });
  }
  const kept = new Set(after.activities.map(a => a.id));
  for (const a of before.activities)
    if (!kept.has(a.id)) updates.push({ kind: 'removed', heading: `Resting: ${prescriptionName(a)}`, detail: 'off your program for now' });
  return updates;
}

const sentence = (s: string) => /[.!?…]$/.test(s) ? s : s + '.';

/** What the therapist says about one board line, so each bubble talks about what just appeared. */
function say(u: BoardUpdate): string {
  const why = u.why ? ' ' + sentence(u.why) : '';
  switch (u.kind) {
    case 'added': return `I've added something: ${u.heading.replace(/^New: /, '').toLowerCase()}, ${u.detail}.${why}`;
    case 'removed': return `We're resting ${u.heading.replace(/^Resting: /, '').toLowerCase()} for now.${why}`;
    case 'changed': return u.automatic
      ? `${u.heading}: ${sentence(u.detail)} Your program moved this on its own, inside the range I set.${why}`
      : `${u.heading}: ${sentence(u.detail)}${why}`;
    case 'note': return sentence(u.detail);
    default: return `${u.heading}: ${u.detail}.`;
  }
}

/**
 * The changes from `baseline` to the active plan, cumulative (a target moved twice reads 45° → 55°), split
 * by the version that made each part so every row carries its own reason and says whether it was automatic.
 */
function changesSince(plans: Plan[], baseline: Plan): BoardUpdate[] {
  const active = plans[plans.length - 1];
  const steps = plans.filter(p => p.version > baseline.version);
  const pairs = steps.map((p, i) => [i === 0 ? baseline : steps[i - 1], p] as const);
  const find = (plan: Plan, id: string) => plan.activities.find(a => a.id === id);
  const tag = (u: BoardUpdate, version: Plan | undefined): BoardUpdate =>
    version ? { ...u, why: version.rationale, automatic: version.origin === 'auto-progression', planVersion: version.version } : u;
  // The latest version for which `changed` holds between it and the one before.
  const latest = (changed: (before: Plan, after: Plan) => boolean) => [...pairs].reverse().find(([b, a]) => changed(b, a))?.[1];

  const rows: BoardUpdate[] = [];
  for (const a of active.activities) {
    const was = find(baseline, a.id);
    if (!was) {
      rows.push(tag({ kind: 'added', heading: `New: ${prescriptionName(a)}`, detail: prescriptionDose(a) },
        latest((b, x) => !find(b, a.id) && !!find(x, a.id))));
      continue;
    }
    const groups = new Map<number, { plan: Plan | undefined; parts: string[] }>();
    for (const part of prescriptionDiff(was, a)) {
      const plan = latest((b, x) => {
        const pb = find(b, a.id), px = find(x, a.id);
        return !!pb && !!px && prescriptionDiff(pb, px).some(p => p.key === part.key);
      });
      const key = plan?.version ?? active.version;
      if (!groups.has(key)) groups.set(key, { plan, parts: [] });
      groups.get(key)!.parts.push(part.text);
    }
    for (const [, g] of [...groups].sort((x, y) => x[0] - y[0]))
      rows.push(tag({ kind: 'changed', heading: prescriptionName(a), detail: g.parts.join(' · ') }, g.plan));
  }
  for (const a of baseline.activities)
    if (!find(active, a.id))
      rows.push(tag({ kind: 'removed', heading: `Resting: ${prescriptionName(a)}`, detail: 'off your program for now' },
        latest((b, x) => !!find(b, a.id) && !find(x, a.id))));
  return rows;
}

export function buildVisit(args: { plans: Plan[]; notes: TherapistNote[]; seen?: { planVersion: number; at: string } | null; therapist?: Therapist }): Visit {
  const therapist = args.therapist ?? DEFAULT_THERAPIST;
  const active = args.plans[args.plans.length - 1];
  // Measured from what the patient last saw. Before any visit, from the version before this one.
  const seen = args.seen && args.plans.find(p => p.version === args.seen!.planVersion);
  const baseline = seen ?? (args.plans.length > 1 ? args.plans[args.plans.length - 2] : null);

  const changed = baseline && baseline.version < active.version ? changesSince(args.plans, baseline) : [];
  // Nothing changed: the board says what the program is instead.
  const program: BoardUpdate[] = changed.length ? [] : active.activities.map(a => ({ kind: 'program', heading: prescriptionName(a), detail: prescriptionDose(a) }));
  const notes: BoardUpdate[] = args.notes.slice(-NOTES_SHOWN).map(n => ({ kind: 'note', heading: 'Note', detail: n.text }));
  const room = Math.max(0, BOARD_LIMIT - notes.length);
  const updates = [...[...changed, ...program].slice(0, room), ...notes];
  const shownChanges = updates.filter(u => u.kind !== 'program' && u.kind !== 'note').length;

  const latestNote = args.notes.length ? args.notes[args.notes.length - 1].at : null;
  const updatedAt = latestNote && latestNote > active.approvedAt ? latestNote : active.approvedAt;

  const lines: Omit<SpeechLine, 'id' | 'audio'>[] = [];
  lines.push({ text: `Hi, I'm ${therapist.name}, your physical therapist. Good to see you.`, reveal: 0 });
  const since = seen ? 'since your last visit' : 'lately';
  lines.push({ text: shownChanges
    ? `I've been looking at your sessions, and ${shownChanges === 1 ? 'one thing has' : `${shownChanges} things have`} changed in your program ${since}.`
    : `I've been looking at your sessions. Nothing has changed ${since}, so here's your program as it stands.`, reveal: 0 });
  // Each version's reason is said once, on the first row it explains; the rows after it just say what moved.
  const explained = new Set<number>();
  updates.forEach((u, i) => {
    const repeat = u.planVersion != null && explained.has(u.planVersion);
    if (u.planVersion != null) explained.add(u.planVersion);
    lines.push({ text: say(repeat ? { ...u, why: undefined } : u), reveal: i + 1 });
  });
  if (active.coachingNote) lines.push({ text: sentence(active.coachingNote), reveal: updates.length });
  lines.push({ text: `That's everything on the board.`, reveal: updates.length });
  lines.push({ text: ASK.line, reveal: updates.length });

  return {
    schema: VISIT_SCHEMA, therapist, planVersion: active.version,
    since: baseline ? { planVersion: baseline.version, lastVisit: seen ? args.seen!.at : null } : null,
    board: {
      title: 'Program updates',
      attribution: `From your licensed therapist · ${therapist.name}, ${therapist.credentials}${therapist.sample ? ' (sample)' : ''}`,
      updatedAt, updates,
    },
    speech: lines.map((l, i) => ({ id: `line-${i}`, audio: null, ...l })),
    ask: ASK,
  };
}
