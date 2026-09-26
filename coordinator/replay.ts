// Clinician replay data for one exercise session: the recorded pose (image landmarks, downsampled) plus the
// angle and trunk lean at every frame, computed by the same engine with the session's saved calibration,
// and the rep boundaries from the saved summary. Nothing here is re-scored; it only shows what was measured.
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { ShoulderRaiseSession, type Frame } from './measurement.ts';

export async function loadReplay(recordings: string, exerciseId: string) {
  if (!/^[0-9a-f-]{36}$/i.test(exerciseId)) throw Error('Invalid session id');
  const summary = JSON.parse(await readFile(resolve(recordings, `exercise-${exerciseId}.summary.json`), 'utf8'));
  if (!summary.poseSessionId) throw Error('This session has no linked pose recording');
  const lines = (await readFile(resolve(recordings, `${summary.poseSessionId}.jsonl`), 'utf8')).trim().split('\n');
  const frames: Frame[] = lines.map(l => JSON.parse(l)).filter(e => e.type === 'pose.frame').map(e => e.payload)
    .filter(f => f?.imageLandmarks?.length === 33 && f.worldLandmarks?.length === 33);

  const engine = new ShoulderRaiseSession(summary.config);
  if (summary.calibration) {                       // reuse the session's own reference, never a new one
    (engine as any).torsoDown = summary.calibration.torsoAxis;
    (engine as any).upperArmM = summary.calibration.upperArmM;
  }
  const reps = summary.reps ?? [];
  const start = (reps[0]?.startMs ?? frames[0]?.sourceMediaTimeMs ?? 0) - 1500;
  const end = (reps.at(-1)?.endMs ?? frames.at(-1)?.sourceMediaTimeMs ?? 0) + 1500;
  const side = summary.side === 'left' ? 'left' : 'right';
  const out: { t: number; p: number[][] | null; a: number | null; k: number | null }[] = [];
  let lastT = -Infinity;
  for (const f of frames) {
    const t = f.sourceMediaTimeMs;
    if (t < start || t > end || t - lastT < 66) continue;      // ~15 fps is plenty for review
    lastT = t;
    const m = engine.measure(f);
    out.push({ t: Math.round(t - start), a: m ? Math.round(m.angleDeg * 10) / 10 : null, k: m?.trunkDeg != null ? Math.round(m.trunkDeg * 10) / 10 : null,
      p: f.imageLandmarks.map(q => [Math.round(q.x * 1000) / 1000, Math.round(q.y * 1000) / 1000, Math.round((q.visibility ?? 0) * 100) / 100]) });
  }
  return {
    exerciseId, side, planVersion: summary.planVersion, targetDeg: summary.config?.targetDeg, maxTrunkDeviationDeg: summary.config?.maxTrunkDeviationDeg,
    simulated: summary.simulated ?? null, endedAt: summary.endedAt,
    reps: reps.map((r: any) => ({ ...r, startMs: r.startMs - start, endMs: r.endMs - start })),
    frames: out,
  };
}
