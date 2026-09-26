// Peer friend graph for rehab users: identities, follow edges, and invite codes.
//
// Deliberately narrow. A peer is a display name and a Mii, never a diagnosis, an
// age or a clinical measurement. Two patients are never comparable — six weeks
// post-stroke against six months — so nothing here exposes one person's numbers
// to another. Activity is shareable; measurement is not.
//
// Discovery is by invite code, a mutual yes to an introduction, or following
// someone you shared a group session with (groups.ts). There is no search, so a
// person can never appear in someone's world without an explicit exchange.
import { mkdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { randomUUID } from 'node:crypto';

export interface Person {
  id: string;
  displayName: string;
  /// Index into the bundled Mii set. Avatars are pseudonymous by design.
  mii: number;
  /// Seeded demo peers are flagged so the UI can label them, matching the
  /// project's existing habit of marking simulated data honestly.
  sample?: boolean;
  lastActiveAt?: string;
}
export interface Graph {
  schema: 'kinesthetic.friends.v1';
  me: string;
  people: Record<string, Person>;
  /// follows[a] contains everyone a follows. Following is one-way; a thread opens
  /// when either side follows, so encouragement never needs mutual approval.
  follows: Record<string, string[]>;
  invites: Record<string, string>;   // code -> issuer id
}

const AVATARS = 8;
const NAME_LIMIT = 24;

/// Every name someone types comes through here. People write "maya" or "de la
/// cruz" on a controller keyboard; the room shows Maya and De La Cruz. Only the
/// first letter of each word changes, so "Tomás", "O'Neil" and "Inês" survive.
export function properName(raw: unknown): string {
  return String(raw ?? '').trim().slice(0, NAME_LIMIT)
    .replace(/(^|[\s'-])(\p{L})/gu, (_, before, letter) => before + letter.toLocaleUpperCase());
}

export class FriendStore {
  private file: string;
  private graph: Graph;

  // Node 24 runs this file by stripping types only, so no parameter properties.
  constructor(dir: string) {
    mkdirSync(dir, { recursive: true });
    this.file = resolve(dir, 'friends.json');
    this.graph = existsSync(this.file)
      ? JSON.parse(readFileSync(this.file, 'utf8'))
      : FriendStore.seed();
    this.save();
  }

  /// A first run needs someone to be. Sample peers are labelled, never invented
  /// encouragement — they exist so the UI has shape before a real friend joins.
  private static seed(): Graph {
    const me = 'me';
    // Staggered activity so a fresh install has something to show: one peer
    // around today, one back after a gap. Labelled `sample` like everything
    // else here — shape for the UI, never invented encouragement.
    const now = Date.now();
    const daysAgo = (n: number) => new Date(now - n * 86400000).toISOString();
    const people: Record<string, Person> = {
      me: { id: me, displayName: 'You', mii: 0 },
      'sample-maya': { id: 'sample-maya', displayName: 'Maya', mii: 3, sample: true, lastActiveAt: daysAgo(3) },
      'sample-arun': { id: 'sample-arun', displayName: 'Arun', mii: 5, sample: true, lastActiveAt: daysAgo(0) },
    };
    return {
      schema: 'kinesthetic.friends.v1', me, people,
      follows: { me: ['sample-maya', 'sample-arun'], 'sample-maya': [me], 'sample-arun': [me] },
      invites: {},
    };
  }

  private save() { writeFileSync(this.file, JSON.stringify(this.graph, null, 2)); }

  me(): Person { return this.graph.people[this.graph.me]; }
  person(id: string): Person | undefined { return this.graph.people[id]; }
  has(id: string): boolean { return id in this.graph.people; }

  /// Everyone reachable from me in either direction, with the edge state.
  list(): Array<Person & { following: boolean; followsMe: boolean }> {
    const me = this.graph.me;
    const mine = new Set(this.graph.follows[me] ?? []);
    const theirs = new Set(
      Object.entries(this.graph.follows)
        .filter(([, list]) => list.includes(me)).map(([id]) => id));
    return [...new Set([...mine, ...theirs])]
      .map(id => this.graph.people[id])
      .filter(Boolean)
      .map(p => ({ ...p, following: mine.has(p.id), followsMe: theirs.has(p.id) }));
  }

  setName(displayName: string): Person {
    const name = properName(displayName);
    if (!name) throw Object.assign(new Error('A display name is required'), { status: 400 });
    this.graph.people[this.graph.me].displayName = name;
    this.save();
    return this.me();
  }

  follow(id: string, on: boolean): void {
    if (id === this.graph.me) throw Object.assign(new Error('Cannot follow yourself'), { status: 400 });
    if (!this.has(id)) throw Object.assign(new Error('Unknown person'), { status: 404 });
    const me = this.graph.me;
    const list = new Set(this.graph.follows[me] ?? []);
    on ? list.add(id) : list.delete(id);
    this.graph.follows[me] = [...list];
    this.save();
  }

  /// Six characters, no vowels, so a spoken code cannot become a word.
  invite(): string {
    const alphabet = 'BCDFGHJKLMNPQRSTVWXZ23456789';
    let code = '';
    for (let i = 0; i < 6; i++) code += alphabet[Math.floor(Math.random() * alphabet.length)];
    this.graph.invites[code] = this.graph.me;
    this.save();
    return code;
  }

  /// Redeeming creates the other person and follows both ways: an invite is an
  /// explicit two-sided act, so it needs no separate approval step.
  accept(code: string, displayName?: string): Person {
    const key = String(code ?? '').trim().toUpperCase();
    const issuer = this.graph.invites[key];
    if (!issuer) throw Object.assign(new Error('That code is not valid'), { status: 404 });
    delete this.graph.invites[key];

    const id = `peer-${randomUUID().slice(0, 8)}`;
    const name = properName(displayName) || 'A friend';
    this.graph.people[id] = {
      id, displayName: name, mii: Math.floor(Math.random() * AVATARS),
      lastActiveAt: new Date().toISOString(),
    };
    const me = this.graph.me;
    this.graph.follows[me] = [...new Set([...(this.graph.follows[me] ?? []), id])];
    this.graph.follows[id] = [...new Set([...(this.graph.follows[id] ?? []), me])];
    this.save();
    return this.graph.people[id];
  }

  /// Someone met in a group session, followed from its member list. The two of
  /// you already stood in the same room under the names you chose, so this is an
  /// explicit exchange too — only the name and the Mii that room showed come
  /// across, and following stays one-way like every other follow here.
  meet(person: Pick<Person, 'id' | 'displayName' | 'mii' | 'sample'>): Person {
    const me = this.graph.me;
    if (person.id === me) throw Object.assign(new Error('Cannot follow yourself'), { status: 400 });
    if (!/^[\w-]{1,64}$/.test(person.id)) throw Object.assign(new Error('Unknown person'), { status: 404 });
    this.graph.people[person.id] ??= {
      id: person.id,
      displayName: properName(person.displayName) || 'A friend',
      mii: Math.abs(Math.trunc(person.mii ?? 0)) % AVATARS,
      ...(person.sample ? { sample: true } : {}),
      lastActiveAt: new Date().toISOString(),
    };
    this.graph.follows[me] = [...new Set([...(this.graph.follows[me] ?? []), person.id])];
    this.save();
    return this.graph.people[person.id];
  }

  touch(id: string): void {
    const person = this.graph.people[id];
    if (!person) return;
    person.lastActiveAt = new Date().toISOString();
    this.save();
  }
}
