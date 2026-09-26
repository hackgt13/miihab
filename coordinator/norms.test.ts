import test from 'node:test';
import assert from 'node:assert/strict';
import { AGE_BANDS, MEASUREMENT_ACCURACY_DEG, NORMS, ageBandFor, compareSession, compareToNorm } from './norms.ts';

test('age bands cover a plausible lifespan without gaps or overlap', () => {
  assert.equal(ageBandFor(17), '10-19');
  assert.equal(ageBandFor(64), '60-69');
  assert.equal(ageBandFor(95), '70+');
  assert.equal(ageBandFor(4), null, 'below the youngest band');
  assert.equal(ageBandFor(NaN), null);
  for (let i = 1; i < AGE_BANDS.length; i++)
    assert.equal(AGE_BANDS[i].from, AGE_BANDS[i - 1].to + 1, 'bands are contiguous');
});

test('typical values decline with age, so an older patient is not measured against the young', () => {
  for (const entry of NORMS) {
    const values = AGE_BANDS.map(b => entry.typicalByBand[b.id]);
    for (let i = 1; i < values.length; i++)
      assert.ok(values[i] <= values[i - 1], `${entry.movement} must not increase with age at band ${AGE_BANDS[i].id}`);
    assert.ok(values[0] <= entry.aaosReferenceDeg, 'no band exceeds the idealised AAOS ceiling');
  }
});

test('the same measurement reads differently against the right age band', () => {
  const young = compareToNorm({movement: 'shoulder_flexion', measuredDeg: 150, ageYears: 22})!;
  const older = compareToNorm({movement: 'shoulder_flexion', measuredDeg: 150, ageYears: 72})!;
  assert.ok(young.differenceDeg < older.differenceDeg, 'the same 150 degrees is further below typical when young');
  assert.ok(older.ratioOfTypical > young.ratioOfTypical);
  // The point of age-banding: 150 degrees must not read as failure to a 72-year-old.
  assert.ok(older.differenceDeg > -5, `a 72-year-old at 150 degrees is near typical, got ${older.differenceDeg}`);
  assert.equal(older.ageBand, '70+');
});

test('a result within single-IMU accuracy of typical is reported as such, and the claim is 5 not 1', () => {
  assert.equal(MEASUREMENT_ACCURACY_DEG, 5);
  const typical = compareToNorm({movement: 'shoulder_flexion', measuredDeg: 149, ageYears: 72})!;
  assert.equal(typical.withinMeasurementError, true);
  const short = compareToNorm({movement: 'shoulder_flexion', measuredDeg: 120, ageYears: 72})!;
  assert.equal(short.withinMeasurementError, false);
  assert.match(typical.note, /\+\/-5 degrees/);
});

test('every comparison declares that it is provisional and where its numbers came from', () => {
  const c = compareToNorm({movement: 'shoulder_abduction', measuredDeg: 160, ageYears: 35, sex: 'female'})!;
  assert.equal(c.provisional, true);
  assert.match(c.provenance, /appendix|Derived/);
  assert.match(c.note, /not the AAOS reference/);
  for (const entry of NORMS) assert.equal(entry.provisional, true);
});

test('sex and side offsets apply only where the study reports a difference', () => {
  const base = compareToNorm({movement: 'shoulder_abduction', measuredDeg: 160, ageYears: 35})!;
  const female = compareToNorm({movement: 'shoulder_abduction', measuredDeg: 160, ageYears: 35, sex: 'female'})!;
  assert.equal(female.typicalDeg - base.typicalDeg, 3, 'abduction is greater in females');

  const left = compareToNorm({movement: 'shoulder_external_rotation', measuredDeg: 80, ageYears: 35, side: 'left'})!;
  const right = compareToNorm({movement: 'shoulder_external_rotation', measuredDeg: 80, ageYears: 35, side: 'right'})!;
  assert.equal(right.typicalDeg - left.typicalDeg, 5, 'external rotation is greater in the right arm');

  const flexLeft = compareToNorm({movement: 'shoulder_flexion', measuredDeg: 160, ageYears: 35, side: 'left'})!;
  const flexRight = compareToNorm({movement: 'shoulder_flexion', measuredDeg: 160, ageYears: 35, side: 'right'})!;
  assert.equal(flexLeft.typicalDeg, flexRight.typicalDeg, 'no side difference is claimed for flexion');
});

test('a session maps through its exercise, and an exercise without a table says so', () => {
  const armed = compareSession({exerciseKind: 'arm-elevation.v1', medianValidPeakDeg: 142, ageYears: 45, side: 'right'});
  assert.ok(armed);
  assert.equal(armed!.movement, 'shoulder_flexion');
  assert.equal(armed!.measuredDeg, 142);

  assert.equal(compareSession({exerciseKind: 'trunk-rotation.v1', medianValidPeakDeg: 50, ageYears: 45}), null,
    'no normative table was gathered for trunk rotation, so no comparison is invented');
  assert.equal(compareSession({exerciseKind: 'arm-elevation.v1', medianValidPeakDeg: null, ageYears: 45}), null,
    'a session with no valid reps has nothing to compare');
  assert.equal(compareToNorm({movement: 'made_up', measuredDeg: 90, ageYears: 30}), null);
});
