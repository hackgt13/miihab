import test from 'node:test';
import assert from 'node:assert/strict';
import { ACTIVITIES, activityById, activityIds, parseCatalog, requireActivity } from './activities.ts';
import { EXERCISES } from './exercise/registry.ts';

const base = () => JSON.parse(JSON.stringify({
  schema: 'kinesthetic.activities.v1',
  activities: [{
    id: 'demo.thing', displayName: 'Demo', tagline: 't', category: 'play',
    scene: 'Demo', venue: 'somewhere', exerciseKinds: [], requires: ['pose'],
    subjects: 1, prescribable: false, loadingMessage: 'Loading…',
    help: {title: 'T', steps: [{step: 'a', copy: 'b'}, {step: 'c', copy: 'd'}, {step: 'e', copy: 'f'}]},
  }],
}));

test('the shipped catalog holds the three built activities', () => {
  assert.deepEqual(activityIds().sort(), ['bowling.adaptive', 'golf.adaptive', 'rehab.studio']);
  // Bowling was fully built -- scene, HUD, swing detection -- and no layer of the app knew it existed.
  assert.ok(activityById('bowling.adaptive'));
  assert.equal(activityById('golf.adaptive')!.subjects, 2, 'golf records two people');
  assert.equal(activityById('rehab.studio')!.subjects, 1);
});

test('bowling stays a solo IMU activity with its own pause and return controls', () => {
  const bowling = requireActivity('bowling.adaptive');
  assert.deepEqual(bowling.requires, ['imu']);
  assert.equal(bowling.subjects, 1);
  assert.equal(bowling.navigation, 'activity');
  assert.equal(requireActivity('golf.adaptive').navigation, 'shell');
  assert.equal(requireActivity('rehab.studio').navigation, 'shell');
  assert.deepEqual(requireActivity('rehab.studio').requires, ['imu']);
});

test('every activity carries its own copy, so a new one adds no branch in the shell', () => {
  for (const a of ACTIVITIES) {
    assert.ok(a.loadingMessage.trim(), `${a.id} needs loading copy`);
    assert.ok(a.help.title.trim(), `${a.id} needs a help title`);
    assert.equal(a.help.steps.length, 3, `${a.id} needs three help steps`);
    for (const s of a.help.steps) assert.ok(s.step.trim() && s.copy.trim());
    assert.ok(a.scene.trim() && a.venue.trim());
  }
});

test('only exercises the engine actually implements may be declared', () => {
  const known = new Set(EXERCISES.map(e => e.id));
  for (const a of ACTIVITIES) for (const k of a.exerciseKinds) assert.ok(known.has(k), `${a.id} declares unknown ${k}`);
  // Golf is play, not a rep count: it measures nothing clinical yet.
  assert.deepEqual(activityById('golf.adaptive')!.exerciseKinds, []);
  assert.ok(activityById('rehab.studio')!.exerciseKinds.includes('shoulder-raise.v1'));
});

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
