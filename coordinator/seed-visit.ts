// Give the therapist visit something to talk about: a fortnight of program changes and notes.
//
// A fresh machine has one plan version, so the visit's whiteboard can only list the program as it stands.
// This writes what a real fortnight would have left behind, through the same code paths the real ones
// take, so the visit reads it exactly as it would read a real clinic's:
//   - last visit: the patient saw the current plan version 12 days ago (VisitStore.markSeen);
//   - 8 days ago, progression moved the shoulder-raise target up one step inside its envelope
//     (PlanStore.approve, origin 'auto-progression', as server.ts does after a good session);
//   - 2 days ago, the therapist reviewed and changed the dose, with a note to the patient
//     (applyProgramUpdate, what POST /api/visit/program-update runs);
//   - and one standalone note.
//
// Everything written carries `"seeded": true`, so it is never mistaken for a real clinician's decision and
// `--clean` can take it out again. Plan versions are otherwise immutable; the seeder only ever removes its
// own, and only from the top of the history.
//
//     node seed-visit.ts            # from coordinator/, with the coordinator up or down
//     node seed-visit.ts --clean
import { readdirSync, readFileSync, unlinkSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { PlanStore, type Plan } from './plans.ts';
import { applyProgramUpdate, VisitStore } from './visit.ts';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const plansDir = resolve(process.env.KINESTHETIC_PLANS_DIRECTORY ?? resolve(root, 'local-data/plans'));
const visitDir = resolve(process.env.KINESTHETIC_VISIT_DIRECTORY ?? resolve(root, 'local-data/visit'));
const THERAPIST = 'Alex, PT, DPT (sample)';
const day = 86_400_000;

const planFile = (dir: string, version: number) => resolve(dir, `plan-v${version}.json`);
const seededVersions = (dir: string) => readdirSync(dir).filter(f => /^plan-v\d+\.json$/.test(f))
  .map(f => JSON.parse(readFileSync(resolve(dir, f), 'utf8')))
  .filter(p => p.seeded === true).map(p => Number(p.version)).sort((a, b) => a - b);

/** Mark a version the seeder wrote, and date it in the past — never before the version it follows. */
function stamp(plans: PlanStore, plan: Plan, daysAgo: number, previous: Plan) {
  const record = JSON.parse(readFileSync(planFile(plans.directory, plan.version), 'utf8'));
  const earliest = Date.parse(previous.approvedAt) + 60_000;
  record.approvedAt = new Date(Math.max(earliest, Date.now() - daysAgo * day)).toISOString();
  record.seeded = true;
  writeFileSync(planFile(plans.directory, plan.version), JSON.stringify(record, null, 2));
}

export function clean(plans: PlanStore, visit: VisitStore) {
  const seeded = seededVersions(plans.directory);
  const top = plans.active().version;
  // Only a run of seeded versions at the top comes out; anything approved since stays, and so do they.
  const removable = seeded.filter(v => seeded.filter(s => s >= v).length === top - v + 1);
  for (const v of removable) unlinkSync(planFile(plans.directory, v));
  return { plans: removable, visit: visit.clean() };
}

export function seed(plans: PlanStore, visit: VisitStore) {
  if (seededVersions(plans.directory).length) throw Error('Already seeded. Run with --clean first.');
  const start = plans.active();
  const has = (id: string) => start.activities.some(a => a.id === id);
  visit.markSeen(start.version, new Date(Date.now() - 12 * day), true);

  const raise = start.activities.find(a => a.exerciseKind === 'arm-elevation.v1');
  let previous = start;
  if (raise?.progression && raise.params.targetDeg != null) {
    const to = Math.min(Number(raise.params.targetDeg) + raise.progression.stepDeg, raise.progression.maxTargetDeg);
    const moved = plans.approve({ origin: 'auto-progression', approvedBy: 'Progression rules (inside the clinician envelope)',
      rationale: 'Two sessions in a row with 7 of 8 raises inside the band, and no pain reported.',
      expectedActiveVersion: start.version, changes: { [raise.id]: { params: { targetDeg: to } } } });
    stamp(plans, moved, 8, previous); previous = plans.active();
  }

  const changes: Record<string, any> = {};
  if (raise) changes[raise.id] = { targetCount: raise.targetCount + 2 };
  const curl = start.activities.find(a => a.exerciseKind === 'elbow-flexion.v1');
  if (curl) changes[curl.id] = { params: { loadKg: Math.min(Number(curl.params.loadKg ?? 0) + 0.5, 10) } };
  if (has('golf.adaptive')) changes['golf.adaptive'] = { targetCount: 18 };
  const { plan: reviewed, note } = applyProgramUpdate(plans, visit, {
    approvedBy: THERAPIST, expectedActiveVersion: previous.version, changes,
    rationale: 'Week-two review: your reach is steady at the new line, and the curls look easy at this weight.',
    coachingNote: 'Same slow, steady reps. The line is still the finish, not the start.',
    patientNote: 'Two more raises now, same line. If the last two feel heavy, rest a minute before them.',
  });
  stamp(plans, reviewed, 2, previous);
  if (note) visit.markSeeded(note.id, new Date(Date.now() - 2 * day));
  visit.add({ text: 'Ice your shoulder for ten minutes after golf.', author: THERAPIST, seeded: true }, new Date(Date.now() - day));
  return { from: start.version, to: plans.active().version };
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const plans = new PlanStore(plansDir);
  const visit = () => new VisitStore(visitDir);
  if (process.argv.includes('--clean')) {
    const removed = clean(plans, visit());
    console.log(`Removed seeded plan versions [${removed.plans.join(', ')}] and ${removed.visit} seeded visit record(s).`);
  } else {
    const { from, to } = seed(plans, visit());
    console.log(`Seeded plan v${from} → v${to} and visit notes. The next visit shows what changed since v${from}.`);
  }
}
