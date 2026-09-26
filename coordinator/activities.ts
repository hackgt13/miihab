// The activity catalog. One declared list both halves of the app agree on.
//
// Why a JSON file rather than a Unity ScriptableObject: the coordinator needs the catalog too, to
// validate that a prescription points at a real activity and that a session record names one. A
// C#-only asset cannot serve the Node half. So JSON is the source and Unity reads a generated copy,
// the same way QuestSceneSetup.WriteHostConfig() generates QuestHostConfig.asset rather than anyone
// hand-editing it in the Inspector.
//
// What this replaces: the id 'golf.adaptive' existed only as a string literal inside a Unity C# file,
// 'rehab.studio' appeared in four unrelated places, and nothing checked that any of them agreed. A
// rename silently broke the join between a prescription and its completed sessions.
//
// It also gives bowling a name. BowlingGame.cs, its scene, HUD and swing detection were all built,
// and no layer of the app knew the activity existed.

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseCatalog, type Activity } from './activity-schema.ts';

export { CATALOG_SCHEMA, parseCatalog, type Activity, type ActivityGroup, type ChannelId } from './activity-schema.ts';

const catalogPath = resolve(import.meta.dirname, 'activities.json');
export const ACTIVITIES: readonly Activity[] = parseCatalog(JSON.parse(readFileSync(catalogPath, 'utf8')), 'activities.json');


export function activityById(id: string | null | undefined): Activity | null {
  return ACTIVITIES.find(a => a.id === id) ?? null;
}
/** Throws naming every known id, so a typo is diagnosable rather than silently accepted. */
export function requireActivity(id: string | null | undefined): Activity {
  const found = activityById(id);
  if (!found) throw Error(`Unknown activity "${id}". Known: ${ACTIVITIES.map(a => a.id).join(', ')}`);
  return found;
}
export const activityIds = () => ACTIVITIES.map(a => a.id);
