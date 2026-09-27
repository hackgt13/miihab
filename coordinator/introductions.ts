// Suggestion without exposure.
//
// friends.ts guarantees that nobody appears in your world without an explicit
// exchange, and discovery is the feature most likely to break it. So the server
// proposes, and neither side learns who the other is until both have said yes:
// an open introduction shows a reason and a stage ("also working toward golf,
// around week three") and never a name, a Mii or a number. On a mutual yes the
// two sides exchange identities and fall into the invite path friends.ts
// already has, which is why the friend graph barely changes to support this.
//
// A decline is remembered and never surfaces that pair again. It is deliberately
// not told to the other side: being turned down by a stranger you never knew
// existed is a cost worth designing out, and the declining side gets the same
// silence back.
//
// Where the candidates come from is left to a Directory. Today that is the
// people already on this machine; when a shared backend exists it is a query
// against it, and nothing else in this file changes.
import { mkdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { randomUUID } from 'node:crypto';
import { type Profile, type Match, rank, mentors, reason } from './matching.ts';

export type Answer = 'pending' | 'yes' | 'no';
export type Kind = 'peer' | 'mentor';

export interface Introduction {
  id: string;
  kind: Kind;
  /** Always exactly two ids, sorted, so a pair can only be introduced once. */
  pair: [string, string];
  answers: Record<string, Answer>;
  score: number;
  /** The line each side sees about the other, keyed by who is reading. */
  reasons: Record<string, string>;
  createdAt: string;
  /** Set once both said yes. The pair is then friends.ts's problem, not ours. */
  joinedAt: string | null;
}

/** Where candidate profiles come from. Local today, remote later. */
export interface Directory {
  profiles(): Promise<Profile[]>;
}

/** The people this machine already knows. A stand-in until a backend exists. */
export class LocalDirectory implements Directory {
  private file: string;
  constructor(dir: string) {
    mkdirSync(dir, { recursive: true });
    this.file = resolve(dir, 'profiles.json');
  }
  /// A first run has nobody to meet, so the Meet page would never show its one card. The server seeds a few
  /// labelled strangers, the way friends.ts seeds sample friends: shape for the UI, never a real person. Only
  /// when there is no directory at all, so a declined one stays declined.
  seedSamples(): void { if (!existsSync(this.file)) this.write(SAMPLE_PROFILES); }
  async profiles(): Promise<Profile[]> {
    if (!existsSync(this.file)) return [];
    try { return JSON.parse(readFileSync(this.file, 'utf8')) as Profile[]; } catch { return []; }
  }
  /** Used by the seed and by tests; a real directory would not expose this. */
  write(profiles: Profile[]): void {
    writeFileSync(this.file, JSON.stringify(profiles, null, 2));
  }
}

const SAMPLE_PROFILES: Profile[] = [
  { personId: 'sample-jo', displayName: 'Jo', goalComponents: ['shoulder elevation', 'grip'], exerciseKinds: ['arm-elevation.v1'], ageBand: null, programWeek: 3 },
  { personId: 'sample-dev', displayName: 'Dev', goalComponents: ['shoulder elevation', 'elbow flexion'], exerciseKinds: ['arm-elevation.v1'], ageBand: null, programWeek: 9 },
  { personId: 'sample-rosa', displayName: 'Rosa', goalComponents: ['grip'], exerciseKinds: [], ageBand: null, programWeek: 1 },
];

const pairKey = (a: string, b: string) => [a, b].sort().join('--');

export class IntroductionStore {
  private file: string;
  private introductions: Introduction[];

  constructor(dir: string) {
    mkdirSync(dir, { recursive: true });
    this.file = resolve(dir, 'introductions.json');
    this.introductions = existsSync(this.file)
      ? JSON.parse(readFileSync(this.file, 'utf8'))
      : [];
  }

  private save() { writeFileSync(this.file, JSON.stringify(this.introductions, null, 2)); }

  /** Everyone this person has already been shown, whatever they answered. */
  answered(me: string): Set<string> {
    const seen = new Set<string>();
    for (const i of this.introductions) {
      if (!i.pair.includes(me)) continue;
      seen.add(i.pair[0] === me ? i.pair[1] : i.pair[0]);
    }
    return seen;
  }

  find(id: string): Introduction | undefined {
    return this.introductions.find(i => i.id === id);
  }

  /** Open introductions this person has not answered yet, of one kind or all. */
  open(me: string, kind?: Kind): Introduction[] {
    return this.introductions.filter(i =>
      i.pair.includes(me) && i.answers[me] === 'pending' && !i.joinedAt &&
      (kind === undefined || i.kind === kind));
  }

  /**
   * Score the directory against one person and record the best few as open
   * introductions. Anyone already suggested, declined or befriended is skipped,
   * so calling this repeatedly does not resurface the same people.
   */
  async suggest(me: Profile, directory: Directory, exclude: Set<string>, kind: Kind = 'peer', limit = 3): Promise<Introduction[]> {
    const all = await directory.profiles();
    const byId = new Map(all.map(p => [p.personId, p]));
    const skip = new Set([...exclude, ...this.answered(me.personId)]);
    const matches: Match[] = kind === 'mentor'
      ? mentors(me, all, skip, limit)
      : rank(me, all, skip, limit);

    const made: Introduction[] = [];
    for (const match of matches) {
      const them = byId.get(match.personId);
      if (!them) continue;
      // Two people can only ever be introduced once, in one direction or the other.
      if (this.introductions.some(i => pairKey(...i.pair) === pairKey(me.personId, them.personId))) continue;

      const pair = [me.personId, them.personId].sort() as [string, string];
      const introduction: Introduction = {
        id: randomUUID(), kind, pair,
        answers: { [pair[0]]: 'pending', [pair[1]]: 'pending' },
        score: match.score,
        // Each side reads a line about the other, never about themselves.
        reasons: {
          [me.personId]: reason(match, them),
          [them.personId]: reason({ ...match, personId: me.personId }, me),
        },
        createdAt: new Date().toISOString(),
        joinedAt: null,
      };
      this.introductions.push(introduction);
      made.push(introduction);
    }
    if (made.length) this.save();
    return made;
  }

  /**
   * Record one side's answer. Returns the introduction, whose `joinedAt` is set
   * only once both have said yes — that transition is the caller's signal to
   * put the two into the friend graph.
   */
  answer(id: string, me: string, said: 'yes' | 'no'): Introduction {
    const introduction = this.find(id);
    if (!introduction) throw Object.assign(new Error('No such introduction'), { status: 404 });
    if (!introduction.pair.includes(me)) throw Object.assign(new Error('Not yours to answer'), { status: 403 });
    if (introduction.joinedAt) return introduction;

    introduction.answers[me] = said;
    const [a, b] = introduction.pair;
    if (introduction.answers[a] === 'yes' && introduction.answers[b] === 'yes') {
      introduction.joinedAt = new Date().toISOString();
    }
    this.save();
    return introduction;
  }

  /** True once both sides agreed, which is the only state that reveals anyone. */
  static joined(introduction: Introduction): boolean {
    return introduction.joinedAt !== null;
  }
}
