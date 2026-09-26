// Normative range-of-motion lookup, so a result reads "118 degrees, typical for your age band"
// instead of "118 degrees". agents/form-reference-data.md ranks this first by value per hour: it
// needs no sensor work and no model.
//
// HONESTY ABOUT THESE NUMBERS. The age-stratified study (n=6,635, markerless mocap) publishes its
// per-band tables in a supplementary appendix that is not in our reference notes, so the per-band
// values below are NOT transcribed from it. They are derived: anchored on a reported adult mean and
// adjusted by the trends the study does state in text --
//
//   - shoulder flexion declines about 15 degrees from the under-20 band to the over-60 band
//   - external rotation declines about 10 degrees from under-30 to over-60
//   - abduction is greater in females and in the young
//   - external rotation runs about 5 degrees greater in the right arm
//
// Every entry is therefore marked provisional, and `provenance` says so to whatever displays it.
// Replace this table with the appendix values before anyone treats a comparison as clinical fact.
// AAOS reference angles (flexion 180, abduction 180, external rotation 90) are idealised ceilings;
// adults over 50 measure significantly below them, so they must not be used as the target for an
// older patient or the result reads as failure to every older user.

export type Sex = 'female' | 'male' | 'unspecified';
export type Side = 'left' | 'right';

export const AGE_BANDS = [
  {id: '10-19', from: 10, to: 19}, {id: '20-29', from: 20, to: 29},
  {id: '30-39', from: 30, to: 39}, {id: '40-49', from: 40, to: 49},
  {id: '50-59', from: 50, to: 59}, {id: '60-69', from: 60, to: 69},
  {id: '70+', from: 70, to: 200},
] as const;
export type AgeBandId = typeof AGE_BANDS[number]['id'];

export interface NormEntry {
  movement: string;
  displayName: string;
  aaosReferenceDeg: number;      // the idealised ceiling, kept for context and never used as a target
  typicalByBand: Record<AgeBandId, number>;
  femaleOffsetDeg: number;       // applied for sex 'female'; 0 where the study reports no difference
  rightSideOffsetDeg: number;    // applied for the right side
  provisional: true;
  provenance: string;
}

const flexion: NormEntry = {
  movement: 'shoulder_flexion', displayName: 'Shoulder flexion', aaosReferenceDeg: 180,
  // ~15 degrees of decline across the span, spread evenly across the bands.
  typicalByBand: {'10-19': 168, '20-29': 166, '30-39': 163, '40-49': 160, '50-59': 157, '60-69': 153, '70+': 149},
  femaleOffsetDeg: 0, rightSideOffsetDeg: 0, provisional: true,
  provenance: 'Derived from the reported ~15 degree age decline; per-band appendix values not yet applied.',
};
const abduction: NormEntry = {
  movement: 'shoulder_abduction', displayName: 'Shoulder abduction', aaosReferenceDeg: 180,
  typicalByBand: {'10-19': 170, '20-29': 167, '30-39': 163, '40-49': 159, '50-59': 154, '60-69': 149, '70+': 144},
  femaleOffsetDeg: 3, rightSideOffsetDeg: 0, provisional: true,
  provenance: 'Derived from the reported age decline and greater abduction in females and the young.',
};
const externalRotation: NormEntry = {
  movement: 'shoulder_external_rotation', displayName: 'Shoulder external rotation', aaosReferenceDeg: 90,
  // ~10 degrees from under-30 to over-60.
  typicalByBand: {'10-19': 88, '20-29': 87, '30-39': 84, '40-49': 82, '50-59': 79, '60-69': 77, '70+': 75},
  femaleOffsetDeg: 0, rightSideOffsetDeg: 5, provisional: true,
  provenance: 'Derived from the reported ~10 degree age decline and ~5 degrees greater ER in the right arm.',
};

export const NORMS: readonly NormEntry[] = [flexion, abduction, externalRotation];

/** Which exercise measures which movement. An exercise with no normative equivalent maps to null. */
export const EXERCISE_MOVEMENT: Readonly<Record<string, string | null>> = {
  'arm-elevation.v1': 'shoulder_flexion',
  'shoulder-raise.v1': 'shoulder_flexion',
  'trunk-rotation.v1': null,          // no comparable normative table gathered yet
  // AAOS gives elbow flexion about 150 degrees, but agents/form-reference-data.md only surveyed
  // shoulder norms, so there is no age-banded table to compare against and inventing one would
  // repeat the mistake this file warns about. Explicitly none until an elbow table is gathered.
  'elbow-flexion.v1': null,
};

export function ageBandFor(years: number): AgeBandId | null {
  if (!Number.isFinite(years)) return null;
  const band = AGE_BANDS.find(b => years >= b.from && years <= b.to);
  return band ? band.id : null;
}

export interface Comparison {
  movement: string;
  displayName: string;
  ageBand: AgeBandId;
  typicalDeg: number;
  measuredDeg: number;
  differenceDeg: number;
  /** Fraction of the age-banded typical value. 1 means at the typical value. */
  ratioOfTypical: number;
  /** True when the measurement is within the stated single-IMU accuracy of typical. */
  withinMeasurementError: boolean;
  aaosReferenceDeg: number;
  provisional: true;
  provenance: string;
  note: string;
}

/**
 * Single-IMU shoulder ROM against a goniometer is about +/-5 degrees once soft-tissue mounting,
 * sensor-to-segment misalignment and calibration error are included -- not the ~1 degree that raw
 * inclination alone suggests. agents/form-reference-data.md is explicit: claim 5, not 1.
 */
export const MEASUREMENT_ACCURACY_DEG = 5;

export function compareToNorm(args: {
  movement: string; measuredDeg: number; ageYears: number; sex?: Sex; side?: Side;
}): Comparison | null {
  const entry = NORMS.find(n => n.movement === args.movement);
  const ageBand = ageBandFor(args.ageYears);
  if (!entry || !ageBand || !Number.isFinite(args.measuredDeg)) return null;
  const typicalDeg = entry.typicalByBand[ageBand]
    + (args.sex === 'female' ? entry.femaleOffsetDeg : 0)
    + (args.side === 'right' ? entry.rightSideOffsetDeg : 0);
  const differenceDeg = args.measuredDeg - typicalDeg;
  return {
    movement: entry.movement, displayName: entry.displayName, ageBand, typicalDeg,
    measuredDeg: args.measuredDeg,
    differenceDeg: Math.round(differenceDeg * 10) / 10,
    ratioOfTypical: Math.round((args.measuredDeg / typicalDeg) * 1000) / 1000,
    withinMeasurementError: Math.abs(differenceDeg) <= MEASUREMENT_ACCURACY_DEG,
    aaosReferenceDeg: entry.aaosReferenceDeg,
    provisional: true, provenance: entry.provenance,
    note: `Compared against the ${ageBand} band, not the AAOS reference of ${entry.aaosReferenceDeg} degrees. `
        + `Single-IMU agreement with a goniometer is about +/-${MEASUREMENT_ACCURACY_DEG} degrees.`,
  };
}

/** The comparison for a finished exercise session, or null when the exercise has no normative table. */
export function compareSession(args: {
  exerciseKind: string; medianValidPeakDeg: number | null; ageYears: number; sex?: Sex; side?: Side;
}): Comparison | null {
  const movement = EXERCISE_MOVEMENT[args.exerciseKind];
  if (!movement || args.medianValidPeakDeg == null) return null;
  return compareToNorm({movement, measuredDeg: args.medianValidPeakDeg,
    ageYears: args.ageYears, sex: args.sex, side: args.side});
}
