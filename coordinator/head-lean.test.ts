import { test } from 'node:test';
import assert from 'node:assert/strict';
import { HeadLean, HEAD_LEAN } from './head-lean.ts';

// A head held at `lean` metres forward during each rep window and back at `sat` between them, at 30 Hz.
function set(windows: [number, number, number][], sat: [number, number] = [0, 0], nod = 0) {
  const lean = new HeadLean();
  for (let t = 0; t <= 20000; t += 33) {
    const inRep = windows.find(([s, e]) => t >= s && t <= e);
    const forward = inRep ? inRep[2] : 0;
    lean.push(t, [sat[0], nod * Math.sin(t / 200), sat[1] + forward]);
  }
  windows.forEach(([s, e], i) => lean.rep(i + 1, s, e));
  return lean.summary();
}

test('a rep that leans the head forward past the threshold is flagged, with the distance and an approximate angle', () => {
  const s = set([[2000, 4000, .02], [6000, 8000, .09], [10000, 12000, .03]]);
  assert.equal(s.available, true);
  if (!s.available) return;
  assert.equal(s.repsMeasured, 3);
  assert.equal(s.repsOverThreshold, 1, 'only the 9 cm rep');
  assert.equal(s.maxCm, 9);
  const leaning = s.perRep.find(r => r.rep === 2)!;
  assert.ok(leaning.approxDeg! >= 6 && leaning.approxDeg! <= 7, `about 6.5 degrees over ${HEAD_LEAN.pivotM} m, got ${leaning.approxDeg}`);
});

test('travel is measured from how the patient sat, not from the seat: sitting a little forward all set is not a lean', () => {
  const s = set([[2000, 4000, 0], [6000, 8000, 0]], [0.03, 0.06]);
  assert.equal(s.available, true);
  if (s.available) assert.equal(s.repsOverThreshold, 0);
});

test('nodding (the head going up and down) is not leaning', () => {
  const s = set([[2000, 4000, 0], [6000, 8000, 0]], [0, 0], 0.06);
  if (s.available) assert.equal(s.maxCm, 0);
});

test('without a headset the summary says so instead of reporting zeros', () => {
  const lean = new HeadLean();
  lean.rep(1, 1000, 2000);
  assert.deepEqual(lean.summary(), { available: false, thresholdCm: HEAD_LEAN.thresholdCm });
});
