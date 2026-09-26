// The two things worth asserting here: that the score ranks the way a person
// would expect, and that an introduction never reveals anyone before both sides
// agree. The second is the one that matters — a scoring bug is a weak
// suggestion, an exposure bug is a patient's business handed to a stranger.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import { jaccard, bandProximity, phaseProximity, similarity, rank, mentors, reason, weeksSince, type Profile } from './matching.ts';
import { IntroductionStore, LocalDirectory } from './introductions.ts';

const person = (over: Partial<Profile> & { personId: string }): Profile => ({
  goalComponents: [], exerciseKinds: [], ageBand: null, programWeek: 0, ...over,
});

const golfer = person({ personId: 'golfer', goalComponents: ['shoulder elevation', 'grip'],
  exerciseKinds: ['shoulder-raise.v1'], ageBand: '50-59', programWeek: 3 });
const alsoGolf = person({ personId: 'also-golf', goalComponents: ['shoulder elevation', 'grip'],
  exerciseKinds: ['shoulder-raise.v1'], ageBand: '50-59', programWeek: 3 });
const gardener = person({ personId: 'gardener', goalComponents: ['trunk rotation'],
  exerciseKinds: ['trunk-rotation.v1'], ageBand: '20-29', programWeek: 20 });

test('an empty set matches nothing rather than dividing by zero', () => {
  assert.deepEqual(jaccard([], ['a']), { score: 0, shared: [] });
  assert.deepEqual(jaccard(['a'], []), { score: 0, shared: [] });
});

test('overlap is case and whitespace insensitive, because goals are typed by hand', () => {
  const { score, shared } = jaccard([' Shoulder Elevation '], ['shoulder elevation']);
  assert.equal(score, 1);
  assert.deepEqual(shared, ['shoulder elevation']);
});

test('an unstated age is no signal, not a mismatch', () => {
  assert.equal(bandProximity(null, '50-59'), 0);
  assert.equal(bandProximity('50-59', '50-59'), 1);
  // Adjacent bands are near-neighbours; the band edge is not a cliff.
  assert.ok(bandProximity('50-59', '60-69') > bandProximity('50-59', '70+'));
});

test('someone at the same stage scores above someone far along', () => {
  assert.equal(phaseProximity(3, 3), 1);
  assert.ok(phaseProximity(3, 4) > phaseProximity(3, 10));
  assert.equal(phaseProximity(1, 40), 0);
});

test('two people with the same goal outrank a stranger, and the reason says why', () => {
  const near = similarity(golfer, alsoGolf);
  const far = similarity(golfer, gardener);
  assert.ok(near.score > far.score);
  // The top term is the one the card will explain with.
  assert.equal(near.terms[0].name, 'goal');
  assert.match(reason(near, alsoGolf), /working toward/);
});

test('no shared anything is not a match at all', () => {
  const none = person({ personId: 'none', goalComponents: ['x'], exerciseKinds: ['y'] });
  assert.deepEqual(rank(none, [person({ personId: 'other' })]), []);
});

test('ranking skips yourself and anyone already answered', () => {
  // A weaker but real match: same exercise, different goal. The gardener shares
  // nothing with the golfer and so is absent from both lists by score alone.
  const partial = person({ personId: 'partial', goalComponents: ['walking'],
    exerciseKinds: ['shoulder-raise.v1'], ageBand: '50-59', programWeek: 4 });
  const all = [golfer, alsoGolf, partial, gardener];
  assert.deepEqual(rank(golfer, all).map(m => m.personId), ['also-golf', 'partial']);
  assert.deepEqual(rank(golfer, all, new Set(['also-golf'])).map(m => m.personId), ['partial']);
});

test('a mentor is someone well ahead on the same work, not someone alongside', () => {
  const ahead = person({ personId: 'ahead', goalComponents: ['shoulder elevation'],
    exerciseKinds: ['shoulder-raise.v1'], programWeek: 24 });
  assert.deepEqual(mentors(golfer, [alsoGolf, ahead]).map(m => m.personId), ['ahead']);
  // Nobody far enough along is an empty list, never a weak substitute.
  assert.deepEqual(mentors(golfer, [alsoGolf]), []);
});

test('weeks are whole and a missing date is week zero', () => {
  assert.equal(weeksSince(null), 0);
  assert.equal(weeksSince('nonsense'), 0);
  assert.equal(weeksSince(new Date(Date.now() - 15 * 86400000).toISOString()), 2);
});

function store() {
  const dir = mkdtempSync(resolve(tmpdir(), 'kinesthetic-intro-'));
  const directory = new LocalDirectory(dir);
  directory.write([golfer, alsoGolf, gardener]);
  return { store: new IntroductionStore(dir), directory };
}

test('an open introduction carries a reason and a stage, and never a name', async () => {
  const { store: s, directory } = store();
  const [made] = await s.suggest(golfer, directory, new Set(), 'peer', 1);
  assert.equal(made.pair.includes('also-golf'), true);
  assert.equal(made.joinedAt, null);
  // Both sides pending, and what each reads is about the other.
  assert.deepEqual(Object.values(made.answers), ['pending', 'pending']);
  assert.equal(typeof made.reasons['golfer'], 'string');
  assert.equal(typeof made.reasons['also-golf'], 'string');
  assert.ok(!made.reasons['golfer'].includes('also-golf'));
});

test('one yes is not enough; it takes both to join', async () => {
  const { store: s, directory } = store();
  const [made] = await s.suggest(golfer, directory, new Set(), 'peer', 1);
  assert.equal(IntroductionStore.joined(s.answer(made.id, 'golfer', 'yes')), false);
  assert.equal(IntroductionStore.joined(s.answer(made.id, 'also-golf', 'yes')), true);
});

test('a decline ends it, and that pair is never suggested again', async () => {
  const { store: s, directory } = store();
  const [made] = await s.suggest(golfer, directory, new Set(), 'peer', 1);
  s.answer(made.id, 'golfer', 'no');
  const again = await s.suggest(golfer, directory, new Set(), 'peer', 3);
  assert.equal(again.some(i => i.pair.includes('also-golf')), false);
});

test('a stranger cannot answer someone else\'s introduction', async () => {
  const { store: s, directory } = store();
  const [made] = await s.suggest(golfer, directory, new Set(), 'peer', 1);
  assert.throws(() => s.answer(made.id, 'gardener', 'yes'), /Not yours/);
  assert.throws(() => s.answer('no-such-id', 'golfer', 'yes'), /No such introduction/);
});

test('suggesting twice does not introduce the same pair twice', async () => {
  const { store: s, directory } = store();
  const first = await s.suggest(golfer, directory, new Set(), 'peer', 3);
  const second = await s.suggest(golfer, directory, new Set(), 'peer', 3);
  assert.ok(first.length > 0);
  assert.deepEqual(second, []);
});

test('an empty directory suggests nothing instead of failing', async () => {
  const dir = mkdtempSync(resolve(tmpdir(), 'kinesthetic-intro-'));
  const empty = new LocalDirectory(dir);
  assert.deepEqual(await new IntroductionStore(dir).suggest(golfer, empty, new Set()), []);
});
