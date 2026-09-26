// The patient's visit with their physical therapist (Unity: Kinesthetic/Visit). The therapist stands at a
// whiteboard and talks the patient through what changed in their program since the last plan version.
//
// Two halves, read together as one reply from GET /api/visit:
//   board  — the whiteboard: program updates, "from your licensed therapist". Computed from the plan's
//            version history (what the clinician actually changed) plus notes the therapist left for the
//            patient (POST /api/visit/notes). Nothing here is invented: with one plan version and no notes
//            the board says what the program is, not that something changed.
//   speech — the lines the therapist says, in order, each one revealing the board item it talks about.
//            Text only today. Each line carries `audio: null` so a voice (the ElevenLabs PT in voice/) can
//            fill it later without the scene changing shape: Unity plays audio when a line has it and
//            falls back to timed captions when it does not.
//
// The therapist's identity is configuration, not data the patient can edit. Until a real clinician is
// attached it is labelled `sample`, matching the project's habit of marking demo people honestly.
import { mkdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { randomUUID } from 'node:crypto';
import type { ActivityPrescription, Plan } from './plans.ts';
import { LIBRARY } from './exercises.ts';
import { activityById } from './activities.ts';

export const VISIT_SCHEMA = 'kinesthetic.visit.v1';
const NOTE_LIMIT = 280;
const NOTES_SHOWN = 3;
const AUTHOR_LIMIT = 60;

export interface Therapist { name: string; credentials: string; sample: boolean }

export interface TherapistNote { id: string; text: string; author: string; at: string }

/** One line on the whiteboard. `kind` lets the board mark a change apart from a standing instruction. */
export interface BoardUpdate { kind: 'added' | 'changed' | 'removed' | 'program' | 'note'; heading: string; detail: string }

/** One speech bubble. `reveal` is how many board updates are showing once this line starts. */
export interface SpeechLine { id: string; text: string; reveal: number; audio: string | null }

export interface Visit {
  schema: typeof VISIT_SCHEMA;
  therapist: Therapist;
  planVersion: number;
  board: { title: string; attribution: string; updatedAt: string; updates: BoardUpdate[] };
  speech: SpeechLine[];
}

export const DEFAULT_THERAPIST: Therapist = { name: 'Alex', credentials: 'PT, DPT', sample: true };

export function therapistFromEnv(env: Record<string, string | undefined> = process.env): Therapist {
  const name = env.KINESTHETIC_THERAPIST_NAME?.trim();
  if (!name) return DEFAULT_THERAPIST;
  return { name, credentials: env.KINESTHETIC_THERAPIST_CREDENTIALS?.trim() || 'PT', sample: false };
}

/** Notes the therapist leaves for the patient, newest last. A flat file, like the friend graph. */
export class VisitNoteStore {
  private file: string;
  private notes: TherapistNote[];

  constructor(dir: string) {
    mkdirSync(dir, { recursive: true });
    this.file = resolve(dir, 'visit-notes.json');
    this.notes = existsSync(this.file) ? JSON.parse(readFileSync(this.file, 'utf8')).notes ?? [] : [];
  }

  list(): TherapistNote[] { return this.notes.slice(); }

  add(input: { text?: unknown; author?: unknown }, now = new Date()): TherapistNote {
    const text = String(input.text ?? '').trim().replace(/\s+/g, ' ');
    if (!text) throw Error('A note needs text.');
    if (text.length > NOTE_LIMIT) throw Error(`A note is at most ${NOTE_LIMIT} characters.`);
    const author = String(input.author ?? '').trim().slice(0, AUTHOR_LIMIT);
    const note = { id: randomUUID(), text, author, at: now.toISOString() };
    this.notes.push(note);
    this.save();
    return note;
  }

  remove(id: string): boolean {
    const before = this.notes.length;
    this.notes = this.notes.filter(n => n.id !== id);
    if (this.notes.length === before) return false;
    this.save();
    return true;
  }

  private save() { writeFileSync(this.file, JSON.stringify({ schema: 'kinesthetic.visit-notes.v1', notes: this.notes }, null, 2)); }
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
  return parts.join(' · ');
}

/** The difference between two plan versions, as the patient would want it told. */
export function programChanges(before: Plan, after: Plan): BoardUpdate[] {
  const updates: BoardUpdate[] = [];
  const old = new Map(before.activities.map(a => [a.id, a]));
  for (const a of after.activities) {
    const was = old.get(a.id);
    if (!was) { updates.push({ kind: 'added', heading: `New: ${prescriptionName(a)}`, detail: prescriptionDose(a) }); continue; }
    const changes: string[] = [];
    const t0 = was.params.targetDeg, t1 = a.params.targetDeg;
    if (t0 != null && t1 != null && Number(t0) !== Number(t1))
      changes.push(`target ${t0}° → ${t1}°${Number(t1) > Number(t0) ? ', a little higher' : ', a step back'}`);
    if (was.targetCount !== a.targetCount) changes.push(`${was.targetCount} → ${a.targetCount} ${a.exerciseKind ? 'reps' : 'to do'}`);
    if (was.params.side !== a.params.side && a.params.side) changes.push(`now your ${a.params.side} side`);
    if (was.params.loadKg !== a.params.loadKg && a.params.loadKg != null) changes.push(`weight ${was.params.loadKg ?? 0} → ${a.params.loadKg} kg`);
    if (was.note !== a.note && a.note) changes.push(a.note);
    if (changes.length) updates.push({ kind: 'changed', heading: prescriptionName(a), detail: changes.join(' · ') });
  }
  const kept = new Set(after.activities.map(a => a.id));
  for (const a of before.activities)
    if (!kept.has(a.id)) updates.push({ kind: 'removed', heading: `Resting: ${prescriptionName(a)}`, detail: 'off your program for now' });
  return updates;
}

const sentence = (s: string) => /[.!?…]$/.test(s) ? s : s + '.';

/** What the therapist says about one board line, so each bubble talks about what just appeared. */
function say(u: BoardUpdate): string {
  switch (u.kind) {
    case 'added': return `I've added something: ${u.heading.replace(/^New: /, '').toLowerCase()}, ${u.detail}.`;
    case 'removed': return `We're resting ${u.heading.replace(/^Resting: /, '').toLowerCase()} for now.`;
    case 'changed': return `${u.heading}: ${sentence(u.detail)}`;
    case 'note': return sentence(u.detail);
    default: return `${u.heading}: ${u.detail}.`;
  }
}

export function buildVisit(args: { plans: Plan[]; notes: TherapistNote[]; therapist?: Therapist }): Visit {
  const therapist = args.therapist ?? DEFAULT_THERAPIST;
  const active = args.plans[args.plans.length - 1];
  const previous = args.plans.length > 1 ? args.plans[args.plans.length - 2] : null;

  const changed = previous ? programChanges(previous, active) : [];
  // Nothing changed (or this is the first plan): the board says what the program is instead.
  const program: BoardUpdate[] = changed.length ? [] : active.activities.map(a => ({ kind: 'program', heading: prescriptionName(a), detail: prescriptionDose(a) }));
  const notes: BoardUpdate[] = args.notes.slice(-NOTES_SHOWN).map(n => ({ kind: 'note', heading: 'Note', detail: n.text }));
  const updates = [...changed, ...program, ...notes];

  const latestNote = args.notes.length ? args.notes[args.notes.length - 1].at : null;
  const updatedAt = latestNote && latestNote > active.approvedAt ? latestNote : active.approvedAt;

  const lines: Omit<SpeechLine, 'id' | 'audio'>[] = [];
  lines.push({ text: `Hi, I'm ${therapist.name}, your physical therapist. Good to see you.`, reveal: 0 });
  lines.push({ text: changed.length
    ? `I've been looking at your sessions, and I've made ${changed.length === 1 ? 'one change' : `${changed.length} changes`} to your program.`
    : `I've been looking at your sessions. Here's your program as it stands.`, reveal: 0 });
  updates.forEach((u, i) => lines.push({ text: say(u), reveal: i + 1 }));
  if (active.coachingNote) lines.push({ text: sentence(active.coachingNote), reveal: updates.length });
  lines.push({ text: `That's everything on the board. I'll keep an eye on how it goes.`, reveal: updates.length });

  return {
    schema: VISIT_SCHEMA, therapist, planVersion: active.version,
    board: {
      title: 'Program updates',
      attribution: `From your licensed therapist · ${therapist.name}, ${therapist.credentials}${therapist.sample ? ' (sample)' : ''}`,
      updatedAt, updates,
    },
    speech: lines.map((l, i) => ({ id: `line-${i}`, audio: null, ...l })),
  };
}
