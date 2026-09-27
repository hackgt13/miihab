import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { ACTIVITIES, activityById, parseCatalog, requireActivity } from './activities.ts';
import { LIBRARY } from './exercises.ts';
import { movementActivityId } from './movement-activities.ts';
import { regenerate, UNITY_COPY } from './write-catalog.ts';

const base = () => JSON.parse(JSON.stringify({
  schema: 'kinesthetic.activities.v1',
  activities: [{
    id: 'demo.thing', displayName: 'Demo', tagline: 't', category: 'play',
    scene: 'Demo', venue: 'somewhere', exerciseKinds: [], requires: ['pose'],
    subjects: 1, prescribable: false, loadingMessage: 'Loading…',
    help: {title: 'T', steps: [{step: 'a', copy: 'b'}, {step: 'c', copy: 'd'}, {step: 'e', copy: 'f'}]},
  }],
}));

test('a malformed catalog fails at load rather than at the patient', () => {
  const bad = (mutate: (a: any) => void) => {
    const c = base(); mutate(c.activities[0]); return () => parseCatalog(c, 'test');
  };
  assert.throws(bad(a => a.id = 'NoDots'), /family\.name/);
  assert.throws(bad(a => a.exerciseKinds = ['no-such-exercise']), /unknown exercise kind/);
  assert.throws(bad(a => a.requires = ['telepathy']), /unknown channel/);
  assert.throws(bad(a => a.category = 'vibes'), /therapy or play/);
  assert.throws(bad(a => a.subjects = 0), /subjects/);
  assert.throws(bad(a => a.navigation = 'both'), /navigation/);
  assert.throws(bad(a => delete a.loadingMessage), /loadingMessage/);
  assert.throws(bad(a => a.help.steps = [{step: 'x', copy: 'y'}]), /exactly 3 steps/);
  assert.throws(bad(a => a.help.steps[1].copy = ''), /needs both step and copy/);
  const dupes = base(); dupes.activities.push(dupes.activities[0]);
  assert.throws(() => parseCatalog(dupes, 'test'), /duplicate id/);
  assert.throws(() => parseCatalog({schema: 'wrong', activities: []}, 'test'), /schema/);
});

test('an unknown id names the ones that exist instead of failing silently', () => {
  assert.equal(activityById('golf.typo'), null);
  assert.throws(() => requireActivity('golf.typo'), /Unknown activity "golf.typo"\. Known: /);
});

test('every library movement is a gallery activity, and the catalog is what the library derives: run `npm run catalog`', () => {
  const text = readFileSync(resolve(import.meta.dirname, 'activities.json'), 'utf8');
  assert.equal(text, regenerate(text), 'activities.json is stale against the exercise library. Run `npm run catalog`.');
  if (existsSync(UNITY_COPY)) assert.equal(readFileSync(UNITY_COPY, 'utf8'), text, "Unity's catalog copy is stale. Run `npm run catalog`.");
  for (const [kind, x] of Object.entries(LIBRARY)) {
    if (x.supersededBy) { assert.equal(activityById(movementActivityId(kind)), null, `${kind} is superseded and has no tile`); continue; }
    const a = requireActivity(movementActivityId(kind));
    assert.deepEqual([a.group, a.exerciseKinds, a.prescribable], ['movement', [kind], false]);
  }
  assert.ok(ACTIVITIES.filter(a => a.group === 'movement').every(a => LIBRARY[a.exerciseKinds[0]]), 'no tile outlives its library entry');
});

test('a movement measures exactly one kind', () => {
  const c = base(); Object.assign(c.activities[0], {group: 'movement', exerciseKinds: []});
  assert.throws(() => parseCatalog(c, 'test'), /exactly one exercise kind/);
  c.activities[0].group = 'shelf';
  assert.throws(() => parseCatalog(c, 'test'), /group must be movement/);
});

test('the one-AirPod arm raise leads the movements, on the gallery\'s first page (six cards) beside golf and bowling', () => {
  assert.equal(Object.values(LIBRARY).filter(x => x.lead).length, 1, 'one lead movement');
  const cards = ACTIVITIES.filter(a => a.card).map(a => a.id);
  assert.equal(cards.filter(id => id.startsWith('movement.'))[0], 'movement.arm-raise');
  assert.ok(cards.indexOf('movement.arm-raise') < 6, `arm raise is card ${cards.indexOf('movement.arm-raise') + 1}; the first page holds six`);
});
