// The activity catalog's shape and its validation, apart from the file, so `npm run catalog` can check a
// regenerated catalog without first loading the one it is replacing (activities.ts loads activities.json on import).

import { EXERCISES } from './exercise/registry.ts';
import type { BodyModel } from './exercises.ts';

export const CATALOG_SCHEMA = 'kinesthetic.activities.v1';
/** 'ref': a second AirPod, for a two-IMU movement. */
export type ChannelId = 'pose' | 'imu' | 'ref';
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
  /** Null for an authored activity; 'movement' for one generated from the exercise library. */
  group: ActivityGroup | null;
  /** Its card in the gallery: every card is built from this, so none is weighted above another. Null keeps an
   *  activity out of the gallery (the therapist visit is reached from the plan it talks about). */
  card: { tag: string; caption: string } | null;
  /** Where the tracker goes, in the patient's words; null when the activity says so itself. */
  wear: string | null;
  /** How the measured joint is drawn (exercises.ts, BodyModel); null when nothing single is measured. */
  body: BodyModel | null;
}

const SEGMENTS = ['arm', 'forearm', 'head', 'trunk', 'thigh', 'shank', 'leg', 'knees'];
const HOLDS = ['upperArm', 'forearm', 'thigh', 'shank', 'legs'];
const isVec = (v: unknown) => Array.isArray(v) && v.length === 3 && v.every(Number.isFinite) && v.some(x => x !== 0);
function parseBody(b: any, where: string): BodyModel | null {
  if (b == null) return null;
  if (!SEGMENTS.includes(b.segment)) throw Error(`${where}: body.segment must be one of ${SEGMENTS.join(', ')}`);
  if (!isVec(b.rest) || !isVec(b.toward)) throw Error(`${where}: body.rest and body.toward must be non-zero [x, y, z]`);
  for (const [key, v] of Object.entries(b.hold ?? {}))
    if (!HOLDS.includes(key) || !isVec(v)) throw Error(`${where}: body.hold.${key} must be one of ${HOLDS.join(', ')} with a non-zero [x, y, z]`);
  return { segment: b.segment, rest: b.rest, toward: b.toward, ...(b.roll ? { roll: true } : {}), ...(b.hold ? { hold: b.hold } : {}) };
}

const CHANNELS: readonly ChannelId[] = ['pose', 'imu', 'ref'];

export function parseCatalog(raw: any, source: string): Activity[] {
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
    const card = a.card == null ? null : { tag: String(a.card.tag ?? '').trim(), caption: String(a.card.caption ?? '').trim() };
    if (card && (!card.tag || !card.caption)) throw Error(`${where}: card needs both tag and caption`);
    const body = parseBody(a.body, where);
    if (group === 'movement' && (!card || !body || !a.wear)) throw Error(`${where}: a movement needs a card, a body model and where to wear the tracker`);
    return {
      id, displayName: String(a.displayName), tagline: String(a.tagline ?? ''),
      loadingMessage: String(a.loadingMessage),
      music: a.music == null ? null : String(a.music),
      help: {title: String(help.title), steps: steps.map((s: any) => ({step: String(s.step), copy: String(s.copy)}))},
      category: a.category, scene: String(a.scene), questScene: a.questScene == null ? null : String(a.questScene), venue: String(a.venue),
      exerciseKinds, requires: requires as ChannelId[], subjects, prescribable: !!a.prescribable, navigation, group,
      card, wear: a.wear == null ? null : String(a.wear), body,
    };
  });
}

