import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { ACTIVITIES } from './activities.ts';
import { EXERCISES } from './exercise/registry.ts';
import { EXERCISE_MOVEMENT, NORMS } from './norms.ts';

// Seams between modules, not the modules themselves.
//
// Each of these is a cross-reference that nothing enforced, so adding one thing in one place and
// forgetting the other silently degraded behaviour somewhere far away. They are cheap to assert and
// they fail at the source rather than at a patient's session.
//
// The one that prompted this file: rehab.studio declared only shoulder-raise.v1 while the registry
// had grown three more kinds, so a session run with any of the others was rejected at save time --
// long after the run, with the reps already gone.

test('every exercise the engine implements is offered by some activity', () => {
  const declared = new Set(ACTIVITIES.flatMap(a => a.exerciseKinds));
  const orphans = EXERCISES.map(e => e.id).filter(id => !declared.has(id));
  assert.deepEqual(orphans, [],
    `these exercises can be prescribed and measured but no activity declares them, so their session ` +
    `records are rejected at save: ${orphans.join(', ')}. Add them to an activity in activities.json.`);
});

test('every exercise has an explicit normative decision, even if that decision is none', () => {
  const missing = EXERCISES.map(e => e.id).filter(id => !(id in EXERCISE_MOVEMENT));
  assert.deepEqual(missing, [],
    `these exercises fall through compareSession() and silently produce no normative comparison: ` +
    `${missing.join(', ')}. Add an entry to EXERCISE_MOVEMENT in norms.ts -- null is a valid, honest ` +
    `answer meaning "no normative table gathered", but absence is not.`);
});

test('every movement an exercise maps to actually has a normative table', () => {
  const tables = new Set(NORMS.map(n => n.movement));
  for (const [exercise, movement] of Object.entries(EXERCISE_MOVEMENT))
    if (movement != null)
      assert.ok(tables.has(movement), `${exercise} maps to "${movement}", which has no table in NORMS`);
});

test('every activity points at a Unity scene that exists on disk', () => {
  const assets = resolve(import.meta.dirname, '../unity/KinestheticUnity/Assets');
  if (!existsSync(assets)) return;   // coordinator can be checked out without the Unity project
  const scenes = new Set<string>();
  (function walk(dir: string) {
    for (const entry of readdirSync(dir)) {
      const path = join(dir, entry);
      if (statSync(path).isDirectory()) { if (entry !== 'Library') walk(path); }
      else if (entry.endsWith('.unity')) scenes.add(entry.slice(0, -'.unity'.length));
    }
  })(assets);
  for (const a of ACTIVITIES)
    assert.ok(scenes.has(a.scene),
      `${a.id} loads scene "${a.scene}", which does not exist. Known scenes: ${[...scenes].sort().join(', ')}`);
});

test("the gallery is the catalog: no authored cards, and every activity is reachable", () => {
  // Three places used to hold an activity's display copy: activities.json, a card in Gallery.uxml, and a
  // caption MainMenuController picked by card index. Nothing joined them, and bowling's card had drifted
  // from its catalog tagline. Now every card is built from the catalog (MainMenuController.BuildGallery),
  // so the check is that nobody has authored one back into the markup.
  const gallery = resolve(import.meta.dirname, '../unity/KinestheticUnity/Assets/Kinesthetic/Menu/Gallery.uxml');
  if (!existsSync(gallery)) return;   // coordinator can be checked out without the Unity project
  const markup = readFileSync(gallery, 'utf8');
  assert.ok(/name="cards"/.test(markup), 'Gallery.uxml has no "cards" container for the generated cards');
  assert.deepEqual([...markup.matchAll(/class="[^"]*\bactivity-card\b/g)].length, 0,
    'Gallery.uxml authors a card. Cards are generated from activities.json; put the copy in its "card" instead.');

  // An activity with no card is reached some other way: the therapist visit from the plan it talks about, whose
  // call to action is titled with the activity's displayName.
  const coaching = resolve(import.meta.dirname, '../unity/KinestheticUnity/Assets/Kinesthetic/Menu/Coaching.uxml');
  const text = (attributes: string) => /\btext="([^"]*)"/.exec(attributes)?.[1] ?? '';
  const calls = existsSync(coaching)
    ? [...readFileSync(coaching, 'utf8').matchAll(/<ui:Label([^>]*class="[^"]*\bcta-title\b[^"]*")/g)].map(m => text(m[1])) : [];
  const invisible = ACTIVITIES.filter(a => !a.card && !calls.includes(a.displayName));
  assert.deepEqual(invisible.map(a => a.id), [],
    `these activities have no gallery card and no other way in, so nobody can reach them from the menu: ${invisible.map(a => a.id).join(', ')}.`);
});
