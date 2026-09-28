import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { angleBetween, gameMovement, segmentMovements, type MotionRow } from './game-movement.ts';

// A sensor turning about one axis: `turns` lists [start ms, duration ms, total degrees]; still otherwise.
function track(turns: [number, number, number][], lengthMs: number, hz = 40, from = 0): MotionRow[] {
  const rows: MotionRow[] = [];
  let angle = 0;
  for (let t = 0; t <= lengthMs; t += 1000 / hz) {
    const turn = turns.find(([s, d]) => t >= s && t < s + d);
    // A smooth bell of angular speed whose integral over the turn is its total angle.
    const speedDeg = turn ? (turn[2] / turn[1]) * 1000 * (1 - Math.cos(2 * Math.PI * (t - turn[0]) / turn[1])) : 0;
    angle += speedDeg / hz;
    const half = angle * Math.PI / 360;
    rows.push({ t: from + t, q: [0, 0, Math.sin(half), Math.cos(half)], speed: speedDeg * Math.PI / 180 });
  }
  return rows;
}

test('each swing is one movement, with its rotation and duration; stillness and small fidgets are not movements', () => {
  const rows = track([[1000, 800, 120], [4000, 800, 60], [7000, 800, 8]], 9000);
  const movements = segmentMovements(rows);
  assert.equal(movements.length, 2, 'the 8° fidget is not a swing');
  assert.ok(Math.abs(movements[0].excursionDeg - 120) < 6, `first swing turned ${movements[0].excursionDeg}°`);
  assert.ok(Math.abs(movements[1].excursionDeg - 60) < 6);
  assert.ok(movements[0].peakDegPerSec > movements[1].peakDegPerSec, 'the bigger swing in the same time is faster');
  assert.ok(movements[0].durationMs > 400 && movements[0].durationMs < 900);
});

test('a backswing, a pause at the top and the downswing are one swing; lost tracking splits them', () => {
  const swing = track([[1000, 600, 90], [1900, 500, 90]], 4000);
  assert.equal(segmentMovements(swing).length, 1);
  const dropped = swing.filter(r => r.t < 1650 || r.t > 2100);   // the sensor went silent across the pause
  assert.equal(segmentMovements(dropped).length, 2);
});

test('a wrist AirPod on the same clock gives how much of the club turn the forearm made', () => {
  const club = track([[1000, 800, 120]], 3000);
  const forearm = track([[1000, 800, 90]], 3000, 25);
  const [m] = segmentMovements(club, forearm);
  assert.ok(m.forearmShare != null && Math.abs(m.forearmShare - 0.75) < 0.06, `forearm share ${m.forearmShare}`);
  const [none] = segmentMovements(club, forearm.filter(r => r.t < 1200));   // wrist stream stopped mid-swing
  assert.equal(none.forearmShare, null, 'no share from a wrist stream that did not cover the swing');
});

test('angle between orientations ignores the quaternion sign', () => {
  assert.ok(Math.abs(angleBetween([0, 0, 0, 1], [0, 0, 0, -1])) < 1e-6);
  assert.ok(Math.abs(angleBetween([0, 0, 0, 1], [0, 0, Math.sin(Math.PI / 4), Math.cos(Math.PI / 4)]) - 90) < 1e-6);
});

test('a golf session reads only the patient\'s club motion inside the round, and reports trend figures, never the score', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'game-movement-'));
  try {
    const start = Date.parse('2026-09-26T15:00:00Z');
    const golf = join(dir, 'golf'), bowling = join(dir, 'bowling');
    const write = (d: string, name: string, rows: MotionRow[], type: string) => {
      mkdirSync(d, { recursive: true });
      writeFileSync(join(d, name), rows.map((r, i) => JSON.stringify({ type, playerId: name.split('-')[0], sequence: i,
        quaternion: r.q, rotationRate: [0, 0, r.speed], receivedAt: Math.round(r.t) })).join('\n') + '\n');
    };
    // Six swings slowing down across the round, one swing after it ended, and the friend's swings.
    const swings: [number, number, number][] = [0, 1, 2, 3, 4, 5].map(i => [2000 + i * 3000, 700, 100 - i * 8]);
    write(golf, `patient-${start}.jsonl`, track([...swings, [25000, 700, 100]], 27000, 40, start), 'club.motion');
    write(golf, `friend-${start}.jsonl`, track([[3000, 700, 150]], 5000, 40, start), 'club.motion');
    write(bowling, `patient-${start}.jsonl`, track(swings.map(([s, d, a]) => [s, d, a * 0.5]), 27000, 25, start), 'bowling.motion');
    const summary = await gameMovement({ activityId: 'golf.adaptive', activitySessionId: 'round-1', completed: true,
      startedAt: new Date(start).toISOString(), endedAt: new Date(start + 20000).toISOString() }, { golf, bowling });
    assert.ok(summary);
    assert.equal(summary.movements, 6, 'the swing after the round ended is not counted, nor the friend\'s');
    assert.ok(summary.earlyToLateSpeedChangePct! < 0, 'later swings were slower');
    assert.equal(summary.forearmSensor, 'wrist');
    assert.ok(Math.abs(summary.forearmShareMedian! - 0.5) < 0.06);
    assert.ok(summary.tracking.sensorCoverage! > 0.9);
    assert.ok(!('strokes' in summary) && !('score' in summary));
    assert.equal(await gameMovement({ activityId: 'rehab.studio', startedAt: '', endedAt: '' }, { golf, bowling }), null);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test('the patient\'s motion is only the rows routed to the patient, from the pair that moved', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'gm-routing-'));
  try {
    const t0 = Date.now() - 60_000;   // files are only read if written after the window opens
    const row = (extra: object, t: number, speed: number) => JSON.stringify({ quaternion: [0, 0, 0, 1], rotationRate: [0, 0, speed], receivedAt: t, ...extra });
    // The second Mac's app says "patient", but in a group its rows went to the friend: none of them are the patient's.
    writeFileSync(join(dir, `patient-${t0}.jsonl`), [row({ playerId: 'friend' }, t0 + 10, 9), row({ playerId: 'friend' }, t0 + 20, 9)].join('\n'));
    // Solo, two pairs of the patient's in one folder: the one that moved is kept, the resting one is not mixed in.
    writeFileSync(join(dir, `patient-${t0 + 1}.jsonl`), [
      row({ type: 'motion.sample', mac: 'mac1' }, t0 + 30, 6), row({ type: 'motion.sample', mac: 'mac2' }, t0 + 31, 0.1),
      row({ type: 'motion.sample', mac: 'mac1' }, t0 + 40, 7), row({ type: 'motion.sample', mac: 'mac2' }, t0 + 41, 0.1)].join('\n'));
    const { readMotion } = await import('./game-movement.ts');
    const rows = await readMotion(dir, 'patient', t0, t0 + 1000);
    assert.deepEqual(rows.map(r => r.t), [t0 + 30, t0 + 40]);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
