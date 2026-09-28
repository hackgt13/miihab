// What a game session says about how the patient moved, for the clinician.
//
// Golf and bowling record every AirPod sample the relay receives (golf-relay.ts → local-data/golf, /bowling);
// the activity record (activity.ts) says when each game ran. This reads the patient's motion inside that
// window, splits it into individual movements (a swing, a roll), and reports practice volume and how the
// movements compare with each other — never the game's own score, distance or power, which depend on game
// settings (golf's power is capped by the rehab level) and are not measures of the person.
//
// Limits a reader should know, and the summary repeats them in `notes`:
// - AirPods report at ~25–45 Hz, so a fast swing peaks between samples. Peak speed is a within-patient
//   trend under the same sensor placement, not an absolute angular velocity.
// - Excursion is how far the sensor rotated. On a club that is club rotation, not a single joint's range.
// - With a second AirPod on the wrist, forearm ÷ club rotation estimates how much of the club's turn the arm
//   made; the rest came from the wrist. An index for trend, not a joint angle.
import { createReadStream } from 'node:fs';
import { readdir, stat } from 'node:fs/promises';
import { resolve } from 'node:path';
import { createInterface } from 'node:readline';

export const GAME_MOVEMENT_SCHEMA = 'kinesthetic.game-movement.v1';

export interface MotionRow { t: number; q: number[]; speed: number }   // t: relay receive time, ms since epoch
export interface Movement { startMs: number; endMs: number; peakDegPerSec: number; excursionDeg: number; durationMs: number; forearmShare: number | null }

export const SEGMENT = {
  startRadPerSec: 1.5,   // a movement begins when rotation passes this
  endRadPerSec: 0.6,     // and ends when it settles below this
  minPeakRadPerSec: 2.5, // weaker bursts are fidgeting or handling the sensor, not a swing or roll
  minDurationMs: 150,
  mergeGapMs: 450,       // a pause at the top of a backswing is part of the same swing
  maxSampleGapMs: 250,   // a longer silence is lost tracking, which also ends a movement
  stillRadPerSec: 0.2,   // measured out to here, so a slow movement's gentle start and finish count too
};

const DEG = 180 / Math.PI;
/** Rotation between two unit quaternions [x, y, z, w], in degrees. Frame-independent. */
export const angleBetween = (a: number[], b: number[]) =>
  2 * Math.acos(Math.min(1, Math.abs(a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3]))) * DEG;

function median(values: number[]) {
  if (!values.length) return null;
  const s = [...values].sort((a, b) => a - b), m = s.length >> 1;
  return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
}
/** Coefficient of variation, %: lower is steadier. Needs a few movements to mean anything. */
function variation(values: number[]) {
  if (values.length < 3) return null;
  const mean = values.reduce((a, b) => a + b, 0) / values.length;
  const sd = Math.sqrt(values.reduce((a, b) => a + (b - mean) ** 2, 0) / (values.length - 1));
  return mean > 0 ? (sd / mean) * 100 : null;
}
const round = (v: number | null, digits = 0) => v == null ? null : Math.round(v * 10 ** digits) / 10 ** digits;

/** Rows in time order → the movements in them. `forearm`, when given, is a wrist AirPod on the same clock. */
export function segmentMovements(rows: MotionRow[], forearm: MotionRow[] = []): Movement[] {
  const raw: { from: number; to: number }[] = [];
  let start = -1;
  for (let i = 0; i < rows.length; i++) {
    const gap = i > 0 && rows[i].t - rows[i - 1].t > SEGMENT.maxSampleGapMs;
    if (start >= 0 && (gap || rows[i].speed < SEGMENT.endRadPerSec)) { raw.push({ from: start, to: gap ? i - 1 : i }); start = -1; }
    // Start from the sample it rose from, unless that sample is on the far side of a gap.
    if (start < 0 && rows[i].speed >= SEGMENT.startRadPerSec) start = i > 0 && !gap ? i - 1 : i;
  }
  if (start >= 0) raw.push({ from: start, to: rows.length - 1 });
  // Merge a backswing and its downswing across a short pause (never across lost tracking).
  const merged: { from: number; to: number }[] = [];
  for (const r of raw) {
    const last = merged.at(-1);
    const contiguous = last && rows[r.from].t - rows[last.to].t <= SEGMENT.mergeGapMs &&
      rows.slice(last.to, r.from + 1).every((x, k, a) => k === 0 || x.t - a[k - 1].t <= SEGMENT.maxSampleGapMs);
    if (last && contiguous) last.to = r.to; else merged.push({ ...r });
  }
  const movements: Movement[] = [];
  let f = 0;
  for (let n = 0; n < merged.length; n++) {
    // Thresholds find a movement; its extent runs out to stillness, a gap, or the neighbouring movement.
    let { from, to } = merged[n];
    const floor = n > 0 ? merged[n - 1].to + 1 : 0, ceiling = n + 1 < merged.length ? merged[n + 1].from - 1 : rows.length - 1;
    while (from > floor && rows[from - 1].speed > SEGMENT.stillRadPerSec && rows[from].t - rows[from - 1].t <= SEGMENT.maxSampleGapMs) from--;
    if (from > floor && rows[from].t - rows[from - 1].t <= SEGMENT.maxSampleGapMs) from--;   // the still sample it left from
    while (to < ceiling && rows[to + 1].speed > SEGMENT.stillRadPerSec && rows[to + 1].t - rows[to].t <= SEGMENT.maxSampleGapMs) to++;
    if (to < ceiling && rows[to + 1].t - rows[to].t <= SEGMENT.maxSampleGapMs) to++;
    const span = rows.slice(from, to + 1);
    const peak = Math.max(...span.map(r => r.speed));
    const durationMs = rows[to].t - rows[from].t;
    if (peak < SEGMENT.minPeakRadPerSec || durationMs < SEGMENT.minDurationMs) continue;
    const excursion = Math.max(...span.map(r => angleBetween(rows[from].q, r.q)));
    // Forearm rotation over the same interval, only when the wrist stream covered it without gaps.
    let forearmShare: number | null = null;
    while (f < forearm.length && forearm[f].t < rows[from].t - 60) f++;
    const arm: MotionRow[] = [];
    for (let k = f; k < forearm.length && forearm[k].t <= rows[to].t + 60; k++) arm.push(forearm[k]);
    const covered = arm.length >= 3 && arm[0].t <= rows[from].t + 80 && arm.at(-1)!.t >= rows[to].t - 80 &&
      arm.every((x, k) => k === 0 || x.t - arm[k - 1].t <= SEGMENT.maxSampleGapMs);
    if (covered && excursion > 10) forearmShare = Math.min(1.5, Math.max(...arm.map(r => angleBetween(arm[0].q, r.q))) / excursion);
    movements.push({ startMs: rows[from].t, endMs: rows[to].t, peakDegPerSec: peak * DEG, excursionDeg: excursion, durationMs, forearmShare });
  }
  return movements;
}

export function summarizeMovements(movements: Movement[], rows: MotionRow[], window: { fromMs: number; toMs: number }) {
  const peaks = movements.map(m => m.peakDegPerSec), excursions = movements.map(m => m.excursionDeg);
  const third = Math.floor(movements.length / 3);
  const early = third >= 2 ? median(peaks.slice(0, third)) : null, late = third >= 2 ? median(peaks.slice(-third)) : null;
  const shares = movements.map(m => m.forearmShare).filter((v): v is number => v != null);
  // Tracking: the share of the window with the sensor reporting (no silence longer than maxSampleGapMs).
  let covered = 0;
  for (let i = 1; i < rows.length; i++) { const d = rows[i].t - rows[i - 1].t; if (d <= SEGMENT.maxSampleGapMs) covered += d; }
  const windowMs = Math.max(1, window.toMs - window.fromMs);
  return {
    movements: movements.length,
    activeSeconds: round(movements.reduce((a, m) => a + m.durationMs, 0) / 1000, 1),
    medianPeakDegPerSec: round(median(peaks)),
    medianExcursionDeg: round(median(excursions)),
    medianDurationMs: round(median(movements.map(m => m.durationMs))),
    speedVariationPct: round(variation(peaks)),
    excursionVariationPct: round(variation(excursions)),
    earlyToLateSpeedChangePct: early && late ? round(((late - early) / early) * 100) : null,
    forearmShareMedian: shares.length >= 3 ? round(median(shares), 2) : null,
    forearmShareMovements: shares.length,
    tracking: { sensorCoverage: round(Math.min(1, covered / windowMs), 2), sampleHz: round(rows.length / (windowMs / 1000), 1) },
    perMovement: movements.map(m => ({ atMs: m.startMs - window.fromMs, peakDegPerSec: round(m.peakDegPerSec), excursionDeg: round(m.excursionDeg),
      durationMs: m.durationMs, forearmShare: round(m.forearmShare, 2) })),
  };
}

/**
 * The player's samples from one motion channel's recordings between two times. Recording files are named
 * `<player>-<connection start ms>.jsonl` and each covers one app connection, so only files that overlap the
 * window are read.
 */
export async function readMotion(dir: string, playerId: string, fromMs: number, toMs: number): Promise<MotionRow[]> {
  let names: string[];
  try { names = await readdir(dir); } catch { return []; }
  const files: string[] = [];
  for (const name of names) {
    const m = name.match(/^(.+)-(\d{13})\.jsonl$/);
    if (!m || m[1] !== playerId || Number(m[2]) > toMs) continue;
    const path = resolve(dir, name);
    if ((await stat(path)).mtimeMs >= fromMs) files.push(path);
  }
  // A file is named after the app's own picker, not after whom its samples went to: in a group session the second
  // Mac's "patient" app is routed to `friend` (golf-relay.ts), so each row is kept only if it went to this player.
  // Rows from the relay's two-pair era carry no playerId (raw /motion samples, always the patient's) and a `mac`.
  // Two pairs of the patient's can land in one folder; interleaved, their frames would read as wild rotations, so
  // the stream that moved most is kept — the same "follow the active pair" rule the games use (motion-fuse.ts).
  const streams = new Map<string, { rows: MotionRow[]; activity: number }>();
  for (const path of files) {
    for await (const line of createInterface({ input: createReadStream(path), crlfDelay: Infinity })) {
      let r: any; try { r = JSON.parse(line); } catch { continue; }
      const t = Number(r.receivedAt);
      if (!(t >= fromMs && t <= toMs) || !Array.isArray(r.quaternion) || !Array.isArray(r.rotationRate)) continue;
      if ((r.playerId ?? 'patient') !== playerId) continue;
      const [x, y, z] = r.rotationRate.map(Number);
      const key = typeof r.mac === 'string' ? r.mac : 'one pair';   // untagged rows: one pair, across reconnects
      const stream = streams.get(key) ?? { rows: [], activity: 0 };
      const speed = Math.hypot(x, y, z);
      stream.rows.push({ t, q: r.quaternion.map(Number), speed }); stream.activity += speed;
      streams.set(key, stream);
    }
  }
  let best: MotionRow[] = [], most = -1;
  for (const { rows, activity } of streams.values()) if (activity > most) { best = rows; most = activity; }
  return best.sort((a, b) => a.t - b.t);
}

/** Which sensor measures the patient in each game, and which one (if any) is the forearm reference. */
const SOURCES: Record<string, { primary: 'golf' | 'bowling'; primaryLabel: string; forearm: 'bowling' | null }> = {
  'golf.adaptive': { primary: 'golf', primaryLabel: 'club', forearm: 'bowling' },
  'bowling.adaptive': { primary: 'bowling', primaryLabel: 'wrist', forearm: null },
};
export const GAME_ACTIVITIES = Object.keys(SOURCES);

export async function gameMovement(envelope: any, dirs: { golf: string; bowling: string }, playerId = 'patient') {
  const source = SOURCES[envelope.activityId];
  if (!source) return null;
  const fromMs = Date.parse(envelope.startedAt), toMs = Date.parse(envelope.endedAt);
  const rows = await readMotion(dirs[source.primary], playerId, fromMs, toMs);
  const forearm = source.forearm ? await readMotion(dirs[source.forearm], playerId, fromMs, toMs) : [];
  const movements = segmentMovements(rows, forearm);
  return {
    schema: GAME_MOVEMENT_SCHEMA,
    activitySessionId: envelope.activitySessionId, activityId: envelope.activityId,
    startedAt: envelope.startedAt, endedAt: envelope.endedAt, completed: envelope.completed !== false,
    sensor: source.primaryLabel, forearmSensor: source.forearm && forearm.length ? 'wrist' : null,
    ...summarizeMovements(movements, rows, { fromMs, toMs }),
    notes: [
      'Peak speed is sampled at the AirPods rate; compare sessions with the same sensor placement rather than reading it as an absolute value.',
      `Excursion is ${source.primaryLabel} rotation, not a single joint's range of motion.`,
      ...(source.forearm ? ['Forearm ÷ club rotation needs a second AirPod on the wrist; it estimates how much of the turn came from the arm rather than the wrist.'] : []),
    ],
  };
}
