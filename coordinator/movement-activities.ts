// Every movement in the exercise library, as its own activity in the gallery.
//
// A movement activity is derived, never authored: its name, setup, cue and help come from its LIBRARY entry
// (exercises.ts), and it opens the Movement Studio scene measuring that one kind. So adding a movement is a
// measurement spec plus a LIBRARY entry, then `npm run catalog`, which writes these into activities.json and
// Unity's copy of it. A test fails if the file and this derivation disagree.
//
// Not prescribable: a clinician prescribes a kind under rehab.studio, as before. Launching a movement runs the
// plan's prescription for that kind if there is one, and otherwise a practice set at the library's defaults
// (plans.ts, prescriptionForActivity).

import { LIBRARY, WEAR, type LibraryExercise } from './exercises.ts';

export const MOVEMENT_PREFIX = 'movement.';

/** movement.neck-flexion for neck-flexion.v1: the kind's id without its version, so a v2 keeps the tile. */
export const movementActivityId = (kind: string) => MOVEMENT_PREFIX + kind.replace(/\.v\d+$/, '');

/** "AirPod on the upper arm" → "upper arm": the place, for a tag naming two. */
const place = (sensor: string) => sensor.replace(/^AirPods? (on|in) (the |your )?/, '');
/** Where the tracker goes: the tag on the card, and the sentence that says how. */
const tagOf = (x: LibraryExercise) =>
  (x.reference ? `AirPods on ${place(x.sensor)} and ${place(x.reference)}` : x.sensor).toUpperCase();
const wearOf = (x: LibraryExercise) => x.wear ?? WEAR[x.sensor];

export function movementActivity(x: LibraryExercise) {
  return {
    id: movementActivityId(x.kind),
    displayName: x.label,
    tagline: x.posture,
    // The same card as golf's or the studio's: the tag says where the tracker goes, the caption how to wear it.
    card: { tag: tagOf(x), caption: wearOf(x) },
    wear: wearOf(x),
    body: x.body,
    category: 'therapy',
    scene: 'Rehab',
    questScene: 'QuestRehab',
    venue: 'studio',
    exerciseKinds: [x.kind],
    requires: x.reference ? ['imu', 'ref'] : ['imu'],
    subjects: 1,
    prescribable: false,
    group: 'movement',
    loadingMessage: 'Opening the studio…',
    help: {
      title: `Today we will practice: ${x.label.toLowerCase()}.`,
      steps: [
        { step: 'Get set', copy: `${wearOf(x)} ${x.posture} Hold still until Ready.` },
        { step: 'Move', copy: x.cue },
        { step: 'Rest', copy: 'Return to rest between reps. Finish set stops early.' },
      ],
    },
  };
}

/**
 * The gallery's movements: the ones everyone knows first (familiar 1), two-AirPod versions ahead of one-AirPod ones
 * at the same tier because they measure properly, then library order. A kind superseded by a better way to measure
 * the same movement has no tile of its own.
 */
export const movementActivities = () => Object.values(LIBRARY)
  .map((x, order) => ({ x, order }))
  .filter(({ x }) => !x.supersededBy)
  .sort((a, b) => a.x.familiar - b.x.familiar || Number(!!b.x.reference) - Number(!!a.x.reference) || a.order - b.order)
  .map(({ x }) => movementActivity(x));
