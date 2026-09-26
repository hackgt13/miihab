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
import { EXERCISES } from './exercise/registry.ts';

export const CATALOG_SCHEMA = 'kinesthetic.activities.v1';
export type ChannelId = 'pose' | 'imu';
/** 'movement': one exercise kind as its own gallery tile, generated from the library (movement-activities.ts). */
export type ActivityGroup = 'movement';

export interface Activity {
  id: string;
  displayName: string;
  tagline: string;
  category: 'therapy' | 'play';
  /** Unity scene name. The gallery loads this; nothing else should hardcode it. */
  scene: string;
  /** The headset's render-only copy of that scene, or null when the headset has no copy of it. */
  questScene: string | null;
  venue: string;
  /** Exercise kinds measured here. Empty means the activity measures nothing clinical. */
  exerciseKinds: string[];
  /** Hard channel requirements, used to gate the gallery. Not the in-activity readiness check. */
  requires: ChannelId[];
  /** Number of people participating in one session. */
  subjects: number;
  prescribable: boolean;
  /** Whether the shell or the activity owns pause, help and return controls. */
  navigation: 'shell' | 'activity';
  /** Curtain text while the scene loads. */
  loadingMessage: string;
  /** Resources path of the activity's background music, or null for none. */
  music: string | null;
  /** The three-step guide shown inside the activity. Lives here so a new activity brings its own. */
  help: { title: string; steps: { step: string; copy: string }[] };
  /** Null for an authored activity with its own gallery card. */
  group: ActivityGroup | null;
}

const CHANNELS: readonly ChannelId[] = ['pose', 'imu'];

function parse(raw: any, source: string): Activity[] {
  if (raw?.schema !== CATALOG_SCHEMA) throw Error(`${source}: schema must be "${CATALOG_SCHEMA}"`);
  if (!Array.isArray(raw.activities) || !raw.activities.length) throw Error(`${source}: activities must be a non-empty array`);
  const known = new Set(EXERCISES.map(e => e.id));
  const seen = new Set<string>();
  return raw.activities.map((a: any, i: number) => {
    const where = `${source}: activities[${i}]`;
    const id = String(a?.id ?? '').trim();
    if (!/^[a-z][a-z0-9]*(\.[a-z0-9-]+)+$/.test(id)) throw Error(`${where}: id must look like "family.name", got "${id}"`);
    if (seen.has(id)) throw Error(`${where}: duplicate id "${id}"`);
    seen.add(id);
    if (!String(a.displayName ?? '').trim()) throw Error(`${where}: displayName is required`);
    if (!String(a.scene ?? '').trim()) throw Error(`${where}: scene is required`);
    if (!String(a.venue ?? '').trim()) throw Error(`${where}: venue is required`);
    if (a.category !== 'therapy' && a.category !== 'play') throw Error(`${where}: category must be therapy or play`);
    const exerciseKinds = (Array.isArray(a.exerciseKinds) ? a.exerciseKinds : []).map(String);
    for (const kind of exerciseKinds)
      if (!known.has(kind)) throw Error(`${where}: unknown exercise kind "${kind}". Known: ${[...known].join(', ')}`);
    const requires = (Array.isArray(a.requires) ? a.requires : []).map(String);
    for (const channel of requires)
      if (!CHANNELS.includes(channel as ChannelId)) throw Error(`${where}: unknown channel "${channel}"`);
    const subjects = Number(a.subjects);
    if (!Number.isInteger(subjects) || subjects < 1 || subjects > 8) throw Error(`${where}: subjects must be 1-8`);
    const navigation = a.navigation ?? 'shell';
    if (navigation !== 'shell' && navigation !== 'activity') throw Error(`${where}: navigation must be shell or activity`);
    const help = a.help ?? {};
    const steps = Array.isArray(help.steps) ? help.steps : [];
    if (!String(help.title ?? '').trim()) throw Error(`${where}: help.title is required`);
    if (steps.length !== 3) throw Error(`${where}: help.steps must hold exactly 3 steps, got ${steps.length}`);
    for (const [j, s] of steps.entries())
      if (!String(s?.step ?? '').trim() || !String(s?.copy ?? '').trim())
        throw Error(`${where}: help.steps[${j}] needs both step and copy`);
    if (!String(a.loadingMessage ?? '').trim()) throw Error(`${where}: loadingMessage is required`);
    const group = a.group ?? null;
    if (group !== null && group !== 'movement') throw Error(`${where}: group must be movement or absent`);
    if (group === 'movement' && exerciseKinds.length !== 1) throw Error(`${where}: a movement measures exactly one exercise kind`);
    return {
      id, displayName: String(a.displayName), tagline: String(a.tagline ?? ''),
      loadingMessage: String(a.loadingMessage),
      music: a.music == null ? null : String(a.music),
      help: {title: String(help.title), steps: steps.map((s: any) => ({step: String(s.step), copy: String(s.copy)}))},
      category: a.category, scene: String(a.scene), questScene: a.questScene == null ? null : String(a.questScene), venue: String(a.venue),
      exerciseKinds, requires: requires as ChannelId[], subjects, prescribable: !!a.prescribable, navigation, group,
    };
  });
}

const catalogPath = resolve(import.meta.dirname, 'activities.json');
export const ACTIVITIES: readonly Activity[] = parse(JSON.parse(readFileSync(catalogPath, 'utf8')), 'activities.json');

/** Exposed so a test can check a candidate catalog without writing it to disk. */
export const parseCatalog = parse;

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
