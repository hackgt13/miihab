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

test("the gallery's cards say what the catalog says, and cover every activity", () => {
  // Three places hold an activity's display copy: activities.json, the card in Gallery.uxml, and the
  // caption MainMenuController picks by card index. Nothing joined them, and bowling's card had
  // already drifted from its catalog tagline -- the menu described the activity one way while the
  // record the coordinator keeps described it another.
  //
  // Asserted here rather than fixed in Unity because the markup is the presentation layer's business;
  // what must not vary is what it says. If this fails, edit activities.json and make the card match,
  // not the other way round.
  const gallery = resolve(import.meta.dirname, '../unity/KinestheticUnity/Assets/Kinesthetic/Menu/Gallery.uxml');
  if (!existsSync(gallery)) return;   // coordinator can be checked out without the Unity project
  const markup = readFileSync(gallery, 'utf8');

  const text = (attributes: string) => /\btext="([^"]*)"/.exec(attributes)?.[1] ?? '';
  const cards = [...markup.matchAll(/<ui:Button name="([a-z-]+-card)"[\s\S]*?<\/ui:Button>/g)].map(([block, name]) => ({
    name,
    title: text(/<ui:Label([^>]*class="[^"]*\bcard-title\b[^"]*")/.exec(block)?.[1] ?? ''),
    description: text(/<ui:Label([^>]*class="[^"]*\bcard-description\b[^"]*")/.exec(block)?.[1] ?? ''),
  }));
  assert.ok(cards.length, 'found no activity cards in Gallery.uxml -- has the markup been restructured?');

  const byName = new Map(ACTIVITIES.map(a => [a.displayName, a]));
  for (const card of cards) {
    const activity = byName.get(card.title);
    assert.ok(activity, `the "${card.name}" card is titled "${card.title}", which is no activity's ` +
      `displayName. Known: ${[...byName.keys()].join(', ')}`);
    assert.equal(card.description, activity.tagline,
      `the "${card.name}" card describes ${activity.id} as "${card.description}", but the catalog's ` +
      `tagline is "${activity.tagline}". The catalog is the source of truth.`);
  }

  const shown = new Set(cards.map(c => c.title));
  const invisible = ACTIVITIES.filter(a => !shown.has(a.displayName));
  assert.deepEqual(invisible.map(a => a.id), [],
    `these activities exist and can be prescribed but have no card in the gallery, so nobody can ` +
    `reach them from the menu: ${invisible.map(a => a.id).join(', ')}.`);
});
