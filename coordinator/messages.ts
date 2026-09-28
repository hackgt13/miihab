// Encouraging messages and photos between rehab peers.
//
// Threads are append-only JSONL, one file per pair, so a message can never be
// silently edited after the fact. Photos are stored by content id and served
// back over the local bridge; nothing here reaches the network.
//
// Message bodies are short and optional: the quick encouragements carry most of
// the weight. A fixed set removes the moderation burden and makes it structurally
// impossible for one patient to give another medical advice through this channel.
import { mkdirSync, appendFileSync, readFileSync, writeFileSync, existsSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { randomUUID } from 'node:crypto';

export const ENCOURAGEMENTS = {
  nice_one: 'Nice one',
  welcome_back: 'Welcome back',
  that_looked_hard: 'That looked hard',
  with_you: 'With you',
  strong_finish: 'Strong finish',
} as const;
export type Encouragement = keyof typeof ENCOURAGEMENTS;

export interface Message {
  id: string;
  from: string;
  to: string;
  at: string;
  /// One of the fixed encouragements, or null for a plain note.
  kind: Encouragement | null;
  /// Free text is capped short: a note, never a consultation.
  text: string;
  photoId: string | null;
  seenAt?: string;
}

const TEXT_LIMIT = 240;
const PHOTO_LIMIT = 4 * 1024 * 1024;
const PHOTO_TYPES: Record<string, string> = {
  'image/jpeg': 'jpg', 'image/png': 'png', 'image/webp': 'webp',
};

/// What any message may carry, whoever it is to. A pair's thread and a group
/// session's chat hold the same fixed encouragements and the same short note,
/// so both go through this one gate.
export function compose(body: { kind?: string; text?: string; photoId?: string }, photos = true):
    { kind: Encouragement | null; text: string; photoId: string | null } {
  const kind = body.kind && Object.hasOwn(ENCOURAGEMENTS, body.kind) ? body.kind as Encouragement : null;
  const text = String(body.text ?? '').trim().slice(0, TEXT_LIMIT);
  const photoId = photos && body.photoId && /^[\w-]{1,64}\.\w{2,5}$/.test(body.photoId) ? body.photoId : null;
  if (!kind && !text && !photoId)
    throw Object.assign(new Error(photos ? 'A message needs an encouragement, a note or a photo'
      : 'A message needs an encouragement or a note'), { status: 400 });
  return { kind, text, photoId };
}

export class MessageStore {
  private dir: string;
  private threadDir: string;
  private photoDir: string;

  constructor(dir: string) {
    this.dir = dir;
    this.threadDir = resolve(dir, 'threads');
    this.photoDir = resolve(dir, 'photos');
    mkdirSync(this.threadDir, { recursive: true });
    mkdirSync(this.photoDir, { recursive: true });
  }

  /// One file per pair, with the ids sorted so either direction finds it.
  private threadFile(a: string, b: string): string {
    const safe = (s: string) => s.replace(/[^\w-]/g, '');
    return resolve(this.threadDir, [safe(a), safe(b)].sort().join('--') + '.jsonl');
  }

  thread(me: string, other: string): Message[] {
    const file = this.threadFile(me, other);
    if (!existsSync(file)) return [];
    return readFileSync(file, 'utf8').split('\n').filter(Boolean)
      .map(line => { try { return JSON.parse(line) as Message; } catch { return null; } })
      .filter((m): m is Message => m !== null);
  }

  send(from: string, to: string, body: { kind?: string; text?: string; photoId?: string }): Message {
    const message: Message = { id: randomUUID(), from, to, at: new Date().toISOString(), ...compose(body) };
    appendFileSync(this.threadFile(from, to), JSON.stringify(message) + '\n');
    return message;
  }

  /// Unread counts per peer, for the landing-page badge.
  unread(me: string, peers: string[]): Record<string, number> {
    const counts: Record<string, number> = {};
    for (const peer of peers) {
      counts[peer] = this.thread(me, peer).filter(m => m.to === me && !m.seenAt).length;
    }
    return counts;
  }

  /// Marking seen rewrites the file, which is why it is the only mutation here.
  markSeen(me: string, other: string): number {
    const file = this.threadFile(me, other);
    if (!existsSync(file)) return 0;
    const now = new Date().toISOString();
    let changed = 0;
    const lines = this.thread(me, other).map(m => {
      if (m.to === me && !m.seenAt) { m.seenAt = now; changed++; }
      return JSON.stringify(m);
    });
    if (changed) writeFileSync(file, lines.join('\n') + '\n');
    return changed;
  }

  savePhoto(bytes: Buffer, contentType: string): string {
    const extension = PHOTO_TYPES[contentType];
    if (!extension) throw Object.assign(new Error('Photos must be JPEG, PNG or WebP'), { status: 415 });
    if (bytes.length === 0 || bytes.length > PHOTO_LIMIT)
      throw Object.assign(new Error('Photo is empty or larger than 4 MB'), { status: 413 });
    const id = `${randomUUID()}.${extension}`;
    writeFileSync(resolve(this.photoDir, id), bytes);
    return id;
  }

  photo(id: string): { bytes: Buffer; contentType: string } | null {
    if (!/^[\w-]{1,64}\.\w{2,5}$/.test(id)) return null;
    const file = resolve(this.photoDir, id);
    if (!file.startsWith(this.photoDir) || !existsSync(file)) return null;
    const extension = id.split('.').pop()!;
    const contentType = Object.entries(PHOTO_TYPES).find(([, e]) => e === extension)?.[0];
    if (!contentType) return null;
    return { bytes: readFileSync(file), contentType };
  }
}
