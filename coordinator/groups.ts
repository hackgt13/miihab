// Group therapy sessions: people doing the same activity at the same time, with
// a shared chat and a list of who is in the room.
//
// A group is a room, not a leaderboard. Members see a name, a Mii and whether
// they are friends — never anyone's reps, degrees or plan. The friends.ts rule
// holds unchanged: two patients at different stages are not comparable, so a
// shared session shares presence and encouragement, never measurement.
//
// Joining a room of strangers is itself the opt-in. Before joining, an open room
// shows only its activity, its size and how long it has been going; names and
// faces appear once you are inside, the same moment yours appears to them.
//
// Chat goes through the same gate as a pair's thread (messages.ts `compose`):
// the fixed encouragements and a short note, no photos. The room's own events
// — someone arriving, someone leaving — are the only lines it writes itself.
// Nothing here ever speaks for a person, sample or not.
//
// Everyone but "me" is local today. Sample rooms are seeded, flagged `sample`
// and labelled in the UI, exactly like friends.ts's sample peers; when a shared
// backend exists, `seed` goes away and the rest does not change.
import { mkdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { randomUUID } from 'node:crypto';
import { compose, type Encouragement } from './messages.ts';

export interface Member {
  id: string;
  displayName: string;
  mii: number;
  sample?: boolean;
  joinedAt: string;
}

export interface GroupMessage {
  id: string;
  from: string;
  /// The name they had in the room, kept so the history still reads after they leave.
  name: string;
  at: string;
  /// `joined` and `left` are the room's own lines; everything else is a person's.
  event: 'joined' | 'left' | null;
  kind: Encouragement | null;
  text: string;
}

export interface Group {
  id: string;
  activityId: string;
  title: string;
  hostId: string;
  /// Open rooms take strangers. A closed one is reachable only through a friend in it.
  open: boolean;
  sample?: boolean;
  createdAt: string;
  endedAt: string | null;
  members: Member[];
  messages: GroupMessage[];
}

interface State { schema: 'kinesthetic.groups.v1'; groups: Group[] }

type Who = Pick<Member, 'id' | 'displayName' | 'mii' | 'sample'>;

export const CAPACITY = 6;
const HISTORY = 200;

// Sample strangers for seeded rooms. Pseudonymous and labelled, like every
// sample peer — shape for the UI, never a person who says anything.
const STRANGERS: Array<Omit<Who, 'sample'>> = [
  { id: 'sample-june', displayName: 'June', mii: 1 },
  { id: 'sample-tomas', displayName: 'Tomás', mii: 2 },
  { id: 'sample-priya', displayName: 'Priya', mii: 4 },
  { id: 'sample-wen', displayName: 'Wen', mii: 6 },
  { id: 'sample-dele', displayName: 'Dele', mii: 7 },
  { id: 'sample-rosa', displayName: 'Rosa', mii: 3 },
  { id: 'sample-kofi', displayName: 'Kofi', mii: 5 },
  { id: 'sample-ines', displayName: 'Inês', mii: 0 },
];
const ROOM_NAMES = ['Morning circle', 'Steady pace', 'Back at it', 'Easy does it', 'Small steps', 'Good company'];

/// A stable small number from a string, so a seeded room looks the same on every run.
function hash(value: string): number {
  let h = 2166136261;
  for (const c of value) h = Math.imul(h ^ c.charCodeAt(0), 16777619);
  return h >>> 0;
}

export class GroupStore {
  private file: string;
  private state: State;
  private now: () => Date;

  constructor(dir: string, now: () => Date = () => new Date()) {
    mkdirSync(dir, { recursive: true });
    this.file = resolve(dir, 'groups.json');
    this.now = now;
    this.state = existsSync(this.file)
      ? JSON.parse(readFileSync(this.file, 'utf8'))
      : { schema: 'kinesthetic.groups.v1', groups: [] };
  }

  private save() { writeFileSync(this.file, JSON.stringify(this.state, null, 2)); }
  private stamp() { return this.now().toISOString(); }

  live(): Group[] { return this.state.groups.filter(g => !g.endedAt); }
  find(id: string): Group | undefined { return this.live().find(g => g.id === id); }
  /// The one room this person is in, if any. Joining another leaves this one.
  current(personId: string): Group | undefined {
    return this.live().find(g => g.members.some(m => m.id === personId));
  }

  /// Make sure an activity has somewhere to go on a machine with nobody else on it:
  /// one open room of sample strangers, and one room around the sample friend who
  /// was around most recently. Idempotent — a room already seeded stays as it is.
  seed(activityId: string, sampleFriends: Who[] = []): void {
    let changed = false;
    const live = this.live().filter(g => g.activityId === activityId && g.sample);
    const minutesAgo = (n: number) => new Date(this.now().getTime() - n * 60000).toISOString();

    if (!live.some(g => g.open)) {
      const h = hash(activityId);
      const count = 2 + (h % 3);
      const members = Array.from({ length: count }, (_, i) => STRANGERS[(h + i * 3) % STRANGERS.length])
        .map((s, i) => ({ ...s, sample: true, joinedAt: minutesAgo(18 - i * 4) }));
      this.state.groups.push({
        id: randomUUID(), activityId, title: ROOM_NAMES[h % ROOM_NAMES.length],
        hostId: members[0].id, open: true, sample: true,
        createdAt: minutesAgo(18), endedAt: null, members, messages: [],
      });
      changed = true;
    }

    const friend = sampleFriends.find(f => f.sample);
    if (friend && !live.some(g => !g.open) && !this.current(friend.id)) {
      const stranger = STRANGERS[hash(activityId + friend.id) % STRANGERS.length];
      this.state.groups.push({
        id: randomUUID(), activityId, title: `${friend.displayName}'s group`,
        hostId: friend.id, open: false, sample: true, createdAt: minutesAgo(9), endedAt: null,
        members: [
          { ...friend, sample: true, joinedAt: minutesAgo(9) },
          { ...stranger, sample: true, joinedAt: minutesAgo(6) },
        ],
        messages: [],
      });
      changed = true;
    }
    if (changed) this.save();
  }

  /// A fresh room with this person as its host.
  create(who: Who, activityId: string, open: boolean): Group {
    if (!/^[\w.-]{1,64}$/.test(activityId)) throw Object.assign(new Error('Unknown activity'), { status: 400 });
    this.leave(who.id, false);
    const group: Group = {
      id: randomUUID(), activityId, title: `${who.displayName}'s group`, hostId: who.id, open,
      createdAt: this.stamp(), endedAt: null, members: [], messages: [],
    };
    this.state.groups.push(group);
    this.enter(group, who);
    this.save();
    return group;
  }

  /// Joining is allowed into an open room, or a closed one that one of `friendIds` is in.
  join(who: Who, id: string, friendIds: Set<string> = new Set()): Group {
    const group = this.find(id);
    if (!group) throw Object.assign(new Error('That group has ended'), { status: 404 });
    if (group.members.some(m => m.id === who.id)) return group;
    if (!group.open && !group.members.some(m => friendIds.has(m.id)))
      throw Object.assign(new Error('That group is for friends of its members'), { status: 403 });
    if (group.members.length >= CAPACITY) throw Object.assign(new Error('That group is full'), { status: 409 });
    this.leave(who.id, false);
    this.enter(group, who);
    this.save();
    return group;
  }

  private enter(group: Group, who: Who) {
    group.members.push({
      id: who.id, displayName: who.displayName, mii: who.mii,
      ...(who.sample ? { sample: true } : {}), joinedAt: this.stamp(),
    });
    this.line(group, who, 'joined');
  }

  /// Leave whatever room this person is in. A room nobody real is left in ends;
  /// a seeded room keeps its sample members so it is still there next time.
  leave(personId: string, persist = true): Group | undefined {
    const group = this.current(personId);
    if (!group) return undefined;
    const leaving = group.members.find(m => m.id === personId)!;
    group.members = group.members.filter(m => m.id !== personId);
    this.line(group, leaving, 'left');
    if (group.members.length === 0 || (!group.sample && group.members.every(m => m.sample)))
      group.endedAt = this.stamp();
    if (persist) this.save();
    return group;
  }

  send(personId: string, body: { kind?: string; text?: string }): GroupMessage {
    const group = this.current(personId);
    if (!group) throw Object.assign(new Error('You are not in a group'), { status: 409 });
    const { kind, text } = compose(body, false);
    const name = group.members.find(m => m.id === personId)!.displayName;
    const message: GroupMessage = { id: randomUUID(), from: personId, name, at: this.stamp(), event: null, kind, text };
    this.push(group, message);
    this.save();
    return message;
  }

  private line(group: Group, who: Who, event: 'joined' | 'left') {
    this.push(group, { id: randomUUID(), from: who.id, name: who.displayName, at: this.stamp(), event, kind: null, text: '' });
  }

  private push(group: Group, message: GroupMessage) {
    group.messages.push(message);
    if (group.messages.length > HISTORY) group.messages.splice(0, group.messages.length - HISTORY);
  }

  /// A room as someone outside it sees it. `names` is the friends you could see
  /// in there anyway; strangers stay a count until you walk in.
  static summary(group: Group, friendIds: Set<string>, now: Date) {
    return {
      id: group.id, activityId: group.activityId, title: group.title, open: group.open,
      sample: group.sample === true,
      size: group.members.length, capacity: CAPACITY,
      minutes: Math.max(0, Math.round((now.getTime() - Date.parse(group.createdAt)) / 60000)),
      friends: group.members.filter(m => friendIds.has(m.id))
        .map(m => ({ id: m.id, displayName: m.displayName, mii: m.mii, sample: m.sample === true })),
    };
  }

  /// What the lobby needs: rooms with a friend in them, and open rooms without one.
  lobby(activityId: string, friendIds: Set<string>) {
    const now = this.now();
    const rooms = this.live().filter(g => g.activityId === activityId);
    const withFriends = rooms.filter(g => g.members.some(m => friendIds.has(m.id)));
    const open = rooms.filter(g => g.open && !withFriends.includes(g) && g.members.length < CAPACITY);
    return {
      friendsPlaying: withFriends.map(g => GroupStore.summary(g, friendIds, now)),
      open: open.map(g => GroupStore.summary(g, friendIds, now)),
    };
  }

  /// A room as a member sees it: everyone in it, and the chat.
  static view(group: Group, me: string, friendIds: Set<string>) {
    return {
      id: group.id, activityId: group.activityId, title: group.title, open: group.open,
      sample: group.sample === true, capacity: CAPACITY,
      members: group.members.map(m => ({
        id: m.id, displayName: m.displayName, mii: m.mii, sample: m.sample === true,
        isMe: m.id === me, host: m.id === group.hostId, friend: friendIds.has(m.id),
      })),
      // Empty strings rather than nulls: Unity's JsonUtility reads both as "".
      messages: group.messages.map(m => ({ ...m, event: m.event ?? '', kind: m.kind ?? '' })),
    };
  }

  /// Put a sample stranger into this person's room: a fake partner for a demo or a test, labelled like every
  /// sample, who says nothing and moves only as a simulator or a second Mac makes them.
  inject(personId: string): Member {
    const group = this.current(personId);
    if (!group) throw Object.assign(new Error('You are not in a group'), { status: 409 });
    if (group.members.length >= CAPACITY) throw Object.assign(new Error('That group is full'), { status: 409 });
    const here = new Set(group.members.map(m => m.id));
    const pick = STRANGERS.find(s => !here.has(s.id) && !this.current(s.id));
    if (!pick) throw Object.assign(new Error('No sample people left to add'), { status: 409 });
    this.enter(group, { ...pick, sample: true });
    this.save();
    return group.members[group.members.length - 1];
  }

  /// A member of this person's current room, for following them from the list.
  member(personId: string, otherId: string): Member | undefined {
    return this.current(personId)?.members.find(m => m.id === otherId);
  }
}
