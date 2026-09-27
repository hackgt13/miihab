// What was said and measured in the coach's conversations, for the clinician's EHR (ehr/ "Coach feed").
//
// The voice server (voice/) posts each event as it happens: Alex's lines, what the patient said, the studio's
// rep and set reports that reached Alex, and the tools Alex called. The EHR reads the latest conversation back
// so a physician can see what the coach actually told their patient, next to the measurements it was based on.
// One append-only file, the last MEMORY events in memory; nothing here is edited after it is written.

import { appendFileSync, mkdirSync, readFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import { resolve } from 'node:path';

export const COACH_SOURCES = ['coach', 'patient', 'engine'] as const;
export const COACH_KINDS = ['llm_reply', 'speak', 'measurement', 'report', 'tool_call', 'cue_template'] as const;
export type CoachSource = typeof COACH_SOURCES[number];
export type CoachKind = typeof COACH_KINDS[number];

export interface CoachEvent {
  id: string;
  at: string;                 // ISO time it happened
  conversation: string;       // the voice session it belongs to
  source: CoachSource;
  kind: CoachKind;
  content: string;
  tool?: string;
}

const MEMORY = 500, CONTENT_LIMIT = 800;

export class CoachLog {
  private readonly file: string;
  private events: CoachEvent[] = [];

  constructor(directory: string) {
    mkdirSync(directory, { recursive: true });
    this.file = resolve(directory, 'events.jsonl');
    try {
      this.events = readFileSync(this.file, 'utf8').split('\n').filter(Boolean).slice(-MEMORY)
        .flatMap(line => { try { return [JSON.parse(line) as CoachEvent]; } catch { return []; } });
    } catch { /* no log yet */ }
  }

  add(input: { conversation?: unknown; source?: unknown; kind?: unknown; content?: unknown; tool?: unknown }, now = new Date()): CoachEvent {
    const source = String(input.source ?? '') as CoachSource, kind = String(input.kind ?? '') as CoachKind;
    if (!COACH_SOURCES.includes(source)) throw Error(`source must be one of ${COACH_SOURCES.join(', ')}.`);
    if (!COACH_KINDS.includes(kind)) throw Error(`kind must be one of ${COACH_KINDS.join(', ')}.`);
    const content = String(input.content ?? '').trim().replace(/\s+/g, ' ');
    if (!content) throw Error('content is required.');
    const conversation = String(input.conversation ?? '').trim().slice(0, 80) || 'unknown';
    const event: CoachEvent = { id: randomUUID(), at: now.toISOString(), conversation, source, kind,
      content: content.slice(0, CONTENT_LIMIT), ...(input.tool ? { tool: String(input.tool).slice(0, 60) } : {}) };
    appendFileSync(this.file, JSON.stringify(event) + '\n');
    this.events.push(event);
    if (this.events.length > MEMORY) this.events.splice(0, this.events.length - MEMORY);
    return event;
  }

  /** The most recent conversation's events, oldest first; or the last `limit` events across conversations. */
  latestConversation(): CoachEvent[] {
    const last = this.events.at(-1)?.conversation;
    return last ? this.events.filter(e => e.conversation === last) : [];
  }

  recent(limit = 100): CoachEvent[] { return this.events.slice(-Math.max(1, Math.min(limit, MEMORY))); }
}
