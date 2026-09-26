import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readdirSync, statSync } from 'node:fs';
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

test('every activity names an exercise the engine actually implements', () => {
  const known = new Set(EXERCISES.map(e => e.id));
  for (const a of ACTIVITIES)
    for (const kind of a.exerciseKinds)
      assert.ok(known.has(kind), `${a.id} declares "${kind}", which the registry does not implement`);
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

test('activity ids are stable identifiers, not display text', () => {
  // These are written into approved plans and session records, which are immutable on disk, so a
  // rename is a migration rather than an edit.
  for (const a of ACTIVITIES) {
    assert.match(a.id, /^[a-z][a-z0-9]*(\.[a-z0-9-]+)+$/, `${a.id} is not a stable id`);
    assert.notEqual(a.id, a.displayName.toLowerCase(), `${a.id} looks derived from display text`);
  }
});
