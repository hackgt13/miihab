import test from 'node:test';
import assert from 'node:assert/strict';
import { activityById, parseCatalog, requireActivity } from './activities.ts';

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
