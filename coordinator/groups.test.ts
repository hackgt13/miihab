// What matters in a shared room: strangers stay a count until you walk in, you
// are only ever in one room, the chat holds to the same fixed vocabulary as a
// pair's thread, and nothing the room writes speaks for a person.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import { GroupStore, CAPACITY, PEER_ID } from './groups.ts';
import { FriendStore } from './friends.ts';

const dir = () => mkdtempSync(resolve(tmpdir(), 'groups-'));
const me = { id: 'me', displayName: 'You', mii: 0 };
const maya = { id: 'sample-maya', displayName: 'Maya', mii: 3, sample: true };

test('seeding gives an activity one open room and one around a sample friend, once', () => {
  const store = new GroupStore(dir());
  store.seed('golf.adaptive', [maya]);
  store.seed('golf.adaptive', [maya]);
  const rooms = store.live().filter(g => g.activityId === 'golf.adaptive');
  assert.equal(rooms.length, 2);
  assert.ok(rooms.every(g => g.sample));
  assert.ok(rooms.some(g => g.open) && rooms.some(g => !g.open && g.hostId === maya.id));
  // Seeded rooms say nothing; only real events and real people write lines.
  assert.ok(rooms.every(g => g.messages.length === 0));
});

test('the lobby shows friends by name and strangers only as a count', () => {
  const store = new GroupStore(dir());
  store.seed('golf.adaptive', [maya]);
  const lobby = store.lobby('golf.adaptive', new Set([maya.id]));
  assert.equal(lobby.friendsPlaying.length, 1);
  assert.deepEqual(lobby.friendsPlaying[0].friends.map(f => f.id), [maya.id]);
  assert.equal(lobby.open.length, 1);
  assert.equal(lobby.open[0].friends.length, 0);
  assert.ok(lobby.open[0].size >= 2);
  assert.ok(!JSON.stringify(lobby.open).includes('displayName'));
});

test('a closed room is reachable only through a friend in it', () => {
  const store = new GroupStore(dir());
  store.seed('golf.adaptive', [maya]);
  const closed = store.live().find(g => !g.open)!;
  assert.throws(() => store.join(me, closed.id, new Set()), /friends of its members/);
  store.join(me, closed.id, new Set([maya.id]));
  assert.equal(store.current('me')?.id, closed.id);
});

test('one room at a time: joining another leaves the first, and says so in both', () => {
  const store = new GroupStore(dir());
  store.seed('golf.adaptive', [maya]);
  const [open, closed] = [store.live().find(g => g.open)!, store.live().find(g => !g.open)!];
  store.join(me, open.id);
  store.join(me, closed.id, new Set([maya.id]));
  assert.equal(store.current('me')?.id, closed.id);
  assert.ok(!store.find(open.id)!.members.some(m => m.id === 'me'));
  assert.deepEqual(store.find(open.id)!.messages.map(m => m.event), ['joined', 'left']);
  // The seeded room outlives a visitor.
  assert.equal(store.find(open.id)!.endedAt, null);
});

test('a room of my own ends when I leave it', () => {
  const store = new GroupStore(dir());
  const group = store.create(me, 'rehab.studio', true);
  store.leave('me');
  assert.equal(store.find(group.id), undefined);
});

test('chat takes encouragements and short notes, never photos or empties', () => {
  const store = new GroupStore(dir());
  assert.throws(() => store.send('me', { text: 'hi' }), /not in a group/);
  store.create(me, 'rehab.studio', true);
  assert.throws(() => store.send('me', {}), /encouragement or a note/);
  const cheer = store.send('me', { kind: 'with_you' });
  assert.equal(cheer.kind, 'with_you');
  const long = store.send('me', { text: 'x'.repeat(500), photoId: 'a.jpg' } as never);
  assert.equal(long.text.length, 240);
  assert.equal((long as unknown as { photoId?: string }).photoId, undefined);
  assert.equal(long.name, 'You');
});

test('a full room turns people away', () => {
  const store = new GroupStore(dir());
  const group = store.create({ id: 'host', displayName: 'Host', mii: 1 }, 'rehab.studio', true);
  for (let i = 1; i < CAPACITY; i++) store.join({ id: `p${i}`, displayName: `P${i}`, mii: i }, group.id);
  assert.throws(() => store.join(me, group.id), /full/);
});

test('following someone from the room puts only their room name and Mii in the graph', () => {
  const d = dir();
  const store = new GroupStore(d);
  const friends = new FriendStore(d);
  store.seed('bowling.adaptive');
  const room = store.live()[0];
  store.join(me, room.id);
  const stranger = room.members.find(m => m.id !== 'me')!;
  const person = friends.meet(store.member('me', stranger.id)!);
  assert.equal(person.displayName, stranger.displayName);
  assert.ok(friends.list().some(p => p.id === stranger.id && p.following && !p.followsMe));
  // Following again is a no-op, not a second person.
  friends.meet(stranger);
  assert.equal(friends.list().filter(p => p.id === stranger.id).length, 1);
  const view = GroupStore.view(store.current('me')!, 'me', new Set(friends.list().map(p => p.id)));
  assert.ok(view.members.find(m => m.id === stranger.id)!.friend);
  assert.ok(view.members.find(m => m.id === 'me')!.isMe);
});

test('a fake partner can be put into your room, labelled, and never twice the same person', () => {
  const store = new GroupStore(dir());
  assert.throws(() => store.inject('me'), /not in a group/);
  store.create(me, 'rehab.studio', true);
  const a = store.inject('me'), b = store.inject('me');
  assert.ok(a.sample && b.sample);
  assert.notEqual(a.id, b.id);
  assert.equal(store.current('me')!.members.length, 3);
  assert.deepEqual(store.current('me')!.messages.map(m => m.event), ['joined', 'joined', 'joined']);
});

test('real groups only: samples switched off leave no sample room, no sample person, and seed nothing', () => {
  const store = new GroupStore(dir());
  store.seed('golf.adaptive', [maya]);
  const mine = store.create(me, 'golf.adaptive', true);
  store.inject('me');
  assert.equal(store.current('me')!.members.length, 2);

  store.setSamples(false);
  assert.equal(store.samples, false);
  assert.ok(store.live().every(g => !g.sample), 'every sample room ended');
  assert.deepEqual(store.current('me')!.members.map(m => m.id), ['me'], 'the sample person left my room');
  assert.equal(store.current('me')!.messages.at(-1)!.event, 'left');
  store.seed('bowling.adaptive', [maya]);
  assert.equal(store.live().filter(g => g.activityId === 'bowling.adaptive').length, 0, 'nothing seeded');
  assert.deepEqual(store.lobby('golf.adaptive', new Set()).open.map(g => g.id), [mine.id]);
  assert.throws(() => store.inject('me'), /switched off/);

  store.setSamples(true);
  store.seed('bowling.adaptive', [maya]);
  assert.ok(store.live().some(g => g.activityId === 'bowling.adaptive' && g.sample));
  // It is remembered.
  assert.equal(new GroupStore(store['file'].replace(/\/groups\.json$/, '')).samples, true);
});

const peer = { id: PEER_ID, displayName: 'Sam', mii: 1 };

test('the second Mac comes into your room, and walks out of it when it goes quiet', () => {
  const store = new GroupStore(dir());
  store.setSamples(false);
  const mine = store.create(me, 'golf.adaptive', false);
  store.peerPresent(peer, me.id);
  assert.equal(store.current(peer.id)!.id, mine.id, 'even a closed room, even with samples off');
  assert.ok(!store.current(peer.id)!.members.find(m => m.id === peer.id)!.sample);
  store.peerPresent(peer, me.id);
  assert.equal(store.current(me.id)!.members.length, 2);
  store.peerGone(peer.id);
  assert.equal(store.current(peer.id), undefined);
  const room = store.current(me.id)!;
  assert.deepEqual(room.members.map(m => m.id), [me.id], 'your room goes on without them');
  assert.equal(room.messages.at(-1)!.event, 'left');
});

test('a full room makes a seat for the second Mac: a sample person stands down, never a real one', () => {
  const store = new GroupStore(dir());
  const mine = store.create(me, 'golf.adaptive', true);
  while (mine.members.length < CAPACITY) store.inject(me.id);
  const last = mine.members.at(-1)!.id;
  store.peerPresent(peer, me.id);
  const room = store.current(me.id)!;
  assert.equal(room.members.length, CAPACITY);
  assert.ok(room.members.some(m => m.id === peer.id), 'seated, as the relay plays them');
  assert.ok(!room.members.some(m => m.id === last), 'the latest sample to arrive gave up the seat');
  assert.ok(room.members.some(m => m.id === me.id));
});

test('with you in no room, the second Mac hosts an open one in the lobby you look at', () => {
  const store = new GroupStore(dir());
  store.setSamples(false);
  store.peerHosts(peer, 'golf.adaptive', me.id);
  store.peerHosts(peer, 'golf.adaptive', me.id);
  assert.equal(store.lobby('golf.adaptive', new Set()).open.length, 1, 'once, not a room per poll');
  // Alone, their room follows the lobby; the old one ends.
  store.peerHosts(peer, 'rehab.studio', me.id);
  assert.equal(store.lobby('golf.adaptive', new Set()).open.length, 0);
  // Joined, it stays put.
  store.join(me, store.lobby('rehab.studio', new Set()).open[0].id);
  store.peerHosts(peer, 'golf.adaptive', me.id);
  assert.equal(store.current(peer.id)!.activityId, 'rehab.studio');
  assert.equal(store.current(me.id)!.id, store.current(peer.id)!.id);
});
