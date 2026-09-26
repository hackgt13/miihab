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

export function movementActivity(x: LibraryExercise) {
  return {
    id: movementActivityId(x.kind),
    displayName: x.label,
    tagline: `${x.sensor}. ${x.posture}`,
    // The same card as golf's or the studio's: the tag says where the tracker goes, the caption how to wear it.
    card: { tag: x.sensor.toUpperCase(), caption: WEAR[x.sensor] },
    wear: WEAR[x.sensor],
    body: x.body,
    category: 'therapy',
    scene: 'Rehab',
    questScene: 'QuestRehab',
    venue: 'studio',
    exerciseKinds: [x.kind],
    requires: ['imu'],
    subjects: 1,
    prescribable: false,
    group: 'movement',
    loadingMessage: 'Opening the studio…',
    help: {
      title: `Today we will practice: ${x.label.toLowerCase()}.`,
      steps: [
        { step: 'Get set', copy: `${WEAR[x.sensor]} ${x.posture} Hold still until Ready.` },
        { step: 'Move', copy: x.cue },
        { step: 'Rest', copy: 'Return to rest between reps. Finish set stops early.' },
      ],
    },
  };
}

export const movementActivities = () => Object.values(LIBRARY).map(movementActivity);
