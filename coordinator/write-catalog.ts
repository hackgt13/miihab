// `npm run catalog`: regenerate the movement activities in activities.json from the exercise library, and write
// Unity's copy (what Kinesthetic → Activities → Sync catalog does from the editor). Authored entries are kept
// as they are; every entry with group "movement" is replaced by the derivation in movement-activities.ts.

import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { movementActivities } from './movement-activities.ts';
import { parseCatalog } from './activities.ts';

const source = resolve(import.meta.dirname, 'activities.json');
export const UNITY_COPY = resolve(import.meta.dirname, '../unity/KinestheticUnity/Assets/Kinesthetic/Activities/Resources/Activities/activities.json');

/** The catalog file's text with the movements regenerated. ASCII-escaped, as the file has always been. */
export function regenerate(text: string): string {
  const catalog = JSON.parse(text);
  catalog.activities = [...catalog.activities.filter((a: any) => a.group !== 'movement'), ...movementActivities()];
  parseCatalog(catalog, 'regenerated activities.json');   // never write a catalog the coordinator would refuse
  return JSON.stringify(catalog, null, 2).replace(/[\u007f-￿]/g, c => `\\u${c.charCodeAt(0).toString(16).padStart(4, '0')}`) + '\n';
}

if (import.meta.main) {
  const text = regenerate(readFileSync(source, 'utf8'));
  writeFileSync(source, text);
  writeFileSync(UNITY_COPY, text);
  console.log(`Wrote ${JSON.parse(text).activities.length} activities to activities.json and Unity's copy.`);
}
