import { createServer } from 'node:http';
import { createReadStream, createWriteStream, mkdirSync } from 'node:fs';
import { stat } from 'node:fs/promises';
import { dirname, extname, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { WebSocketServer, WebSocket } from 'ws';
import { randomUUID } from 'node:crypto';
import { writeFile, readdir, readFile } from 'node:fs/promises';
import { PlanStore, prescriptionForActivity } from './plans.ts';
import { FriendStore } from './friends.ts';
import { MessageStore, ENCOURAGEMENTS } from './messages.ts';
import { spotlight, recap, draft, introLine, milestoneLine, daysSince } from './social-ai.ts';
import { MilestoneStore, milestoneFrom } from './milestones.ts';
import { weeksSince, readable, type Profile, type FriendActivity } from './matching.ts';
import { IntroductionStore, LocalDirectory } from './introductions.ts';
import { GroupStore, PEER_ID } from './groups.ts';
import { hostMonotonicMs } from './hostclock.ts';
import { loadReplay } from './replay.ts';
import { createSession, exerciseKind, type RepParams, type RepSession } from './exercise/registry.ts';
import { ImuAssigner, type Channel, type Mode } from './exercise/imu-assign.ts';
import type { Frame, ImuSample, RepEvent } from './exercise/kind.ts';
import { activitySummaryFromExercise, parseActivitySummary } from './activity.ts';
import { NORMS, compareToNorm, type Sex, type Side } from './norms.ts';
import { requireActivity } from './activities.ts';
import { evaluate, evidenceFromSummary, ProposalStore, type PainReport } from './progression.ts';
import { LIBRARY } from './exercises.ts';
import { CurlTrace } from './exercise/curl-trace.ts';
import { buildDashboard, golfUnlock, planUpdate } from './dashboard.ts';
import { GAME_ACTIVITIES, gameMovement } from './game-movement.ts';
import { HeadLean } from './head-lean.ts';
import { CoachLog } from './coach-log.ts';
import { applyProgramUpdate, buildVisit, therapistFromEnv, VisitStore } from './visit.ts';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const captureRoot = resolve(root, 'spikes/pose-capture');
const recordings = resolve(process.env.KINESTHETIC_RECORDINGS_DIRECTORY ?? resolve(root, 'local-data/sessions'));
// The relay's motion recordings, which game-movement.ts reads for each golf round and bowling game.
const motionDirs = {
  golf: resolve(process.env.KINESTHETIC_GOLF_RECORDINGS ?? resolve(root, 'local-data/golf')),
  bowling: resolve(process.env.KINESTHETIC_BOWLING_RECORDINGS ?? resolve(root, 'local-data/bowling')),
};
mkdirSync(recordings, { recursive: true });
const port = Number(process.env.KINESTHETIC_PORT ?? 8766);
const plans = new PlanStore(resolve(process.env.KINESTHETIC_PLANS_DIRECTORY ?? resolve(root, 'local-data/plans')));
const proposals = new ProposalStore(resolve(process.env.KINESTHETIC_PROPOSALS_DIRECTORY ?? resolve(root, 'local-data/proposals')));
// Simulated input never moves a real patient's plan, except in a demo run that opts in.
const progressFromSimulated = process.env.KINESTHETIC_PROGRESS_SIMULATED === '1';
// Measurement is IMU-only for now. The camera (MediaPipe) kinds stay in the code, off unless opted in.
const cameraMeasurement = process.env.KINESTHETIC_CAMERA_MEASUREMENT === '1';
const socialDir = resolve(process.env.KINESTHETIC_SOCIAL_DIRECTORY ?? resolve(root, 'local-data/social'));
const friends = new FriendStore(socialDir);
const messages = new MessageStore(socialDir);
// Notes the therapist leaves for the patient's visit (visit.ts). Kept beside the plans: they are clinical.
// What Alex and the patient said, and the studio reports Alex coached from (coach-log.ts), for the EHR's coach feed.
const coachLog = new CoachLog(resolve(process.env.KINESTHETIC_COACH_DIRECTORY ?? resolve(root, 'local-data/coach')));
const visit = new VisitStore(resolve(process.env.KINESTHETIC_VISIT_DIRECTORY ?? resolve(root, 'local-data/visit')));
const therapist = therapistFromEnv();
const introductions = new IntroductionStore(socialDir);
// Local today. When a shared backend exists this is the only line that changes.
const directory = new LocalDirectory(socialDir);
directory.seedSamples();
const groups = new GroupStore(socialDir);
// The second Mac as a person in a group (groups.ts `peerPresent`). The relay knows when it is streaming; asked
// rather than told, like the relay asks this process about groups, so neither needs the other to start first.
// A short grace keeps an AirPod taken out for a moment from walking them out of the room.
const relayUrl = 'http://' + new URL(process.env.KINESTHETIC_MOTION_URL ?? 'ws://127.0.0.1:8767').host;   // the relay the motion comes from
const peer = {id: PEER_ID, displayName: process.env.KINESTHETIC_PEER_NAME || 'Guest', mii: 1};
let peerLiveAt = -Infinity;
const peerLive = () => Date.now() - peerLiveAt < 10000;
setInterval(async () => {
  try {
    const r = await fetch(relayUrl + '/', {signal: AbortSignal.timeout(800)});
    if (r.ok && (await r.json())?.secondMac?.live) peerLiveAt = Date.now();
  } catch {}
  if (peerLive()) groups.peerPresent(peer, friends.me().id); else groups.peerGone(peer.id);
}, 1000).unref();
const milestones = new MilestoneStore(socialDir);

/// This patient, as the matcher sees them: what they are working toward and
/// what they practise. Never a measurement — see the note at the top of
/// matching.ts for why a score built from degrees would be the wrong thing.
function myProfile(): Profile {
  const plan = plans.active();
  return {
    personId: friends.me().id,
    goalComponents: plan?.goal?.components ?? [],
    exerciseKinds: [...new Set((plan?.activities ?? [])
      .map(a => a.exerciseKind).filter((k): k is string => !!k))],
    ageBand: null,          // nobody is asked for this yet; null is "no signal"
    programWeek: weeksSince(plan?.approvedAt),
  };
}

/// Reads a bounded request body. Photos are the only binary upload here.
function readBytes(request: import('node:http').IncomingMessage, limit: number): Promise<Buffer> {
  return new Promise((done, fail) => {
    const chunks: Buffer[] = []; let size = 0;
    request.on('data', chunk => {
      size += chunk.length;
      if (size > limit) { fail(Object.assign(new Error('Photo is larger than 4 MB'), {status:413})); request.destroy(); return; }
      chunks.push(chunk);
    });
    request.on('end', () => done(Buffer.concat(chunks)));
    request.on('error', fail);
  });
}
const portalRoot = resolve(root, 'coordinator/portal');
// The clinician's EHR portal (ehr/, built with `npm run build` there): served here so it reads this coordinator's
// data same-origin, with no proxy and no CORS.
const ehrRoot = resolve(root, 'ehr/dist');
// 5173 is the wiirehab clinician web app's Vite dev server, which writes visit notes.
const allowedOrigins = new Set([`http://localhost:${port}`, `http://127.0.0.1:${port}`, 'http://localhost:8765', 'http://127.0.0.1:8765', 'http://localhost:5173', 'http://127.0.0.1:5173']);
const mime: Record<string,string> = { '.html':'text/html', '.js':'text/javascript', '.mjs':'text/javascript', '.css':'text/css', '.wasm':'application/wasm', '.task':'application/octet-stream',
  '.svg':'image/svg+xml', '.png':'image/png', '.json':'application/json', '.woff2':'font/woff2', '.ico':'image/x-icon' };
let producer: WebSocket | null = null;
let latest: any = null;
let lastReceived = 0;
let captureStatus = 'Camera not started';
let sessionId = '';
let sequence = -1;
let sessionStarted = 0;
let recording: ReturnType<typeof createWriteStream> | null = null;
const viewers = new Set<WebSocket>();
// Exercise measurement runs on the incoming pose stream. Its events use a separate /exercise
// socket so pose viewers (Unity) only ever receive pose.frame / pose.status messages.
const exerciseViewers = new Set<WebSocket>();
let exercise: RepSession | null = null;
let exerciseId = '';
let exercisePoseSession: string | null = null;
let exerciseSource: string | null = null;   // producer sourceId, e.g. camera vs synthetic simulator
let exerciseStartedAt = '';
let exercisePrescriptionId = '';            // plan activities[].id being measured, e.g. arm-elevation-right
let exerciseActivityId = 'rehab.studio';
let exercisePractice = false;               // a movement tile the plan does not prescribe: recorded, never progressed
// The latest camera frame, for exercises that read magnitude from the IMU and compensation from pose.
let lastPose: { frame: Frame; hostMs: number } | null = null;
let lastImuMs = -Infinity;
// Kept apart from the pose recording: Unity's replay loader accepts only pose.frame lines there.
let exerciseLog: ReturnType<typeof createWriteStream> | null = null;
function exerciseBroadcast(message: any) {
  const text = JSON.stringify({schemaVersion:'kinesthetic.session.v1', exerciseId, ...message});
  for (const ws of exerciseViewers) if (ws.readyState === WebSocket.OPEN && ws.bufferedAmount < 128*1024) ws.send(text);
}
// Engine output goes to /exercise viewers and the session log the same way for both sensors.
// The headset's head pose during an AirPod set (head-lean.ts): head travel per rep, a camera-free sign of leaning to
// compensate. Read from the relay's /head channel while a set runs; with no headset the summary says so.
const headUrl = process.env.KINESTHETIC_HEAD_URL ?? 'ws://127.0.0.1:8767/head?role=viewer';
let head: WebSocket | null = null, headLean: HeadLean | null = null;
function watchHead() {
  if (head) return;
  const ws = new WebSocket(headUrl); head = ws;
  ws.on('message', data => {
    if (!headLean) return;
    let p: any; try { p = JSON.parse(String(data)); } catch { return; }
    if (p.type === 'head.pose') headLean.push(Number(p.hostMonotonicMs), p.p);
  });
  ws.on('close', () => { if (head !== ws) return; head = null; if (headLean) setTimeout(watchHead, 1000); });
  ws.on('error', () => {});
}

function feed(events: RepEvent[], sourceSessionId: string | null) {
  if (!exercise) return;
  for (const event of events) if (event.type === 'rep.completed') headLean?.rep(event.rep, event.startMs, event.tMs);
  const sample = exercise.samples.at(-1);
  // `quality` is the rep qualities' live readout (hold timer, tempo pace, hitches) while a rep runs; null between reps.
  if (sample) exerciseBroadcast({type:'exercise.sample', payload:{...sample, phase:exercise.phase, rep:exercise.currentRep, quality:exercise.live}});
  for (const event of events) {
    exerciseBroadcast({type:'exercise.event', payload:event}); exerciseLog?.write(JSON.stringify({type:'exercise.event', exerciseId, sourceSessionId, payload:event})+'\n');
    // Calibrated against these streams: from here a quiet AirPod is tracking loss, never a different AirPod.
    if (event.type === 'calibration.complete' && assigner?.lock()) sensorsChanged();
  }
}
// The patient's two AirPod pairs (AGENTS.md, "Sensors"), raw from the relay's /motion: every sample tagged Mac 1
// (this Mac's pair) or Mac 2 (the second Mac's, over the tailnet). The games read the relay's one fused stream; the
// studio measures from both, and which pair is the measured limb — and, with two, which is the neighbouring
// segment — is decided by exercise/imu-assign.ts from what is live and what moves, then remembered.
const MOTION_URL = process.env.KINESTHETIC_RAW_MOTION_URL ?? 'ws://127.0.0.1:8767/motion?role=viewer';
let motion: WebSocket | null = null;
/// The last two-AirPod assignment, by how the pairs were worn. The next set worn the same way starts on it: the
/// patient tells the pairs apart once, not every set. Cleared only by a restart.
const rememberedPairs = new Map<string, {imu: Channel; ref: Channel}>();
const wearKey = (wear: {imu: string; ref?: string}) => `${wear.imu}|${wear.ref ?? ''}`;
let assignerWear: {imu: string; ref?: string} | null = null;
let assigner: ImuAssigner | null = null;
let assignerTicker: NodeJS.Timeout | null = null;
let lastRef: {sample: ImuSample; hostMs: number} | null = null;
// The last sample's host-clock stamp and when it arrived, so liveness lapses on the stream's own clock.
let lastMotionAt: {host: number; wall: number} | null = null;
const motionNow = () => lastMotionAt ? lastMotionAt.host + (Date.now() - lastMotionAt.wall) : hostMonotonicMs();
const sensorState = () => assigner?.state ?? null;
function sensorsChanged() { exerciseBroadcast({type:'exercise.sensors', payload: sensorState()}); }
/** How many people this set is for: the activity's own count, or two whenever the patient is in a group session. */
const peopleInSession = (activityId: string) => Math.max(requireActivity(activityId).subjects, groups.current(friends.me().id) ? 2 : 1);
class SensorRuleError extends Error {}
/** The set is over: no more assignment. The relay sockets stay up for the next set. */
function releaseSensors() {
  const s = assigner?.state;
  if (s?.mode === 'two' && s.locked && s.imu && s.ref && assignerWear) rememberedPairs.set(wearKey(assignerWear), {imu: s.imu, ref: s.ref});
  if (assignerTicker) { clearInterval(assignerTicker); assignerTicker = null; }
  assigner = null; lastRef = null; lastMotionAt = null;
}
function watchMotion() {
  if (motion) return;
  const ws = new WebSocket(MOTION_URL); motion = ws;
  ws.on('message', data => {
    if (!exercise || !assigner || motion !== ws) return;
    let p: any; try { p = JSON.parse(String(data)); } catch { return; }
    if (p.type !== 'motion.sample' || (p.mac !== 'mac1' && p.mac !== 'mac2')) return;
    const channel: Channel = p.mac;
    // Both relays stamp the shared host clock (hostclock.ts); older relays did not, so fall back to local time.
    const t = Number.isFinite(p.hostMonotonicMs) ? Number(p.hostMonotonicMs) : hostMonotonicMs();
    if (!lastMotionAt || t > lastMotionAt.host) lastMotionAt = {host: t, wall: Date.now()};
    if (assigner.push(channel, p.rotationRate, t)) sensorsChanged();
    const role = assigner.roleOf(channel);
    if (!role) return;   // not yet told which AirPod is which: nothing is measured
    exerciseLog?.write(JSON.stringify({type:'motion.sample', exerciseId, role, channel, payload:p})+'\n');
    curlTrace?.push(role, t, p);
    const sample: ImuSample = {quaternion: p.quaternion, rotationRate: p.rotationRate, hostMonotonicMs: t};
    if (role === 'ref') { lastRef = {sample, hostMs: t}; return; }
    exerciseSource ??= `airpod:${channel}:${p.sourceId}:${p.sessionId}`;   // no pose recording: replay stays camera-only
    if (t <= lastImuMs) return;
    // The relay only forwards samples, so a silent stream is seen here: step the engine with no IMU at the
    // moment the gap passed, which is tracking loss, before the sample that ends it.
    if (lastImuMs > -Infinity && t - lastImuMs > exercise.params.trackingGapMs)
      feed(exercise.pushFused({tMs: lastImuMs + exercise.params.trackingGapMs + 1, imu: null}), p.sessionId);
    lastImuMs = t;
    const pose = lastPose && Math.abs(t - lastPose.hostMs) < 250 ? lastPose.frame : null;
    // A second AirPod that has gone quiet is no second AirPod: the step fails its `ref` channel, which is tracking loss.
    const ref = lastRef && Math.abs(t - lastRef.hostMs) < exercise.params.trackingGapMs ? lastRef.sample : null;
    feed(exercise.pushFused({tMs: t, imu: sample, pose, ref}), p.sessionId);
  });
  // Reconnected while an exercise reads it; between sets the next start reconnects.
  ws.on('close', () => { if (motion !== ws) return; motion = null; if (exercise && assigner) setTimeout(watchMotion, 1000); });
  ws.on('error', () => {});
}
// A biceps curl's path, drawn live in the studio beside the patient's arm (exercise/curl-trace.ts): twice a second
// while the set runs, once more when it ends. Presentation only; the rep engine counts.
let curlTrace: CurlTrace | null = null;
let curlTraceTicker: ReturnType<typeof setInterval> | null = null;
function startCurlTrace(kindId: string, targetDeg: number) {
  endCurlTrace(false);
  if (kindId !== 'biceps-curl.v1') return;
  const trace = curlTrace = new CurlTrace(Number.isFinite(targetDeg) ? targetDeg : 110);
  curlTraceTicker = setInterval(() => {
    const payload = trace.next();
    if (payload) exerciseBroadcast({type:'exercise.trajectory', payload});
  }, 500);
  curlTraceTicker.unref();
}
function endCurlTrace(final = true) {
  if (curlTraceTicker) { clearInterval(curlTraceTicker); curlTraceTicker = null; }
  const payload = final ? curlTrace?.next(true) : null;
  if (payload) exerciseBroadcast({type:'exercise.trajectory', payload: {...payload, final: true}});
  curlTrace = null;
}
async function readJson(request: import('node:http').IncomingMessage) {
  let body = ''; for await (const chunk of request) { body += chunk; if (body.length > 16384) throw Error('Body too large'); }
  return body ? JSON.parse(body) : {};
}
async function dashboard() {
  const envelopes = await Promise.all((await readdir(recordings)).filter(f => /^session-.*\.json$/.test(f))
    .map(async f => JSON.parse(await readFile(resolve(recordings, f), 'utf8'))));
  return buildDashboard({plans: plans.list(), summaries: await readSummaries(), envelopes});
}

/// A model line is a nicety on top of a card that already works, so it gets a budget rather than the card waiting on it.
const within = <T,>(ms: number, work: Promise<T>) =>
  Promise.race([work, new Promise<null>(done => setTimeout(() => done(null), ms).unref())]);

/// Open introductions as the Meet card shows them. The warm line under the reason is written from what the two
/// share and nothing else, the same facts reason() used, so the model has nothing identifying to leak. A line
/// still being written past the budget lands in social-ai's cache for the next look; startup warms it first.
async function introductionCards(kind: 'peer' | 'mentor', budgetMs: number) {
  const me = friends.me().id, mine = myProfile();
  await introductions.suggest(mine, directory, new Set(friends.list().map(p => p.id)), kind);
  const profiles = await directory.profiles();
  return Promise.all(introductions.open(me, kind).map(async i => {
    const them = profiles.find(p => p.personId === (i.pair[0] === me ? i.pair[1] : i.pair[0]));
    const heading = i.reasons[me] ?? '';
    const warm = them && heading ? await within(budgetMs, introLine({
      kind: i.kind, heading,
      sharedGoals: mine.goalComponents.filter(g => them.goalComponents.includes(g)),
      sharedMovements: mine.exerciseKinds.filter(k => them.exerciseKinds.includes(k)).map(readable),
      theirProgramWeek: them.programWeek, yourProgramWeek: mine.programWeek,
    })) : null;
    // Deliberately no id, name or Mii for the other side.
    return {id: i.id, kind: i.kind, reason: heading, warm: warm ?? '', waitingOnThem: i.answers[me] === 'yes'};
  }));
}

async function readSummaries() {
  const files = (await readdir(recordings)).filter(f => /^exercise-.*\.summary\.json$/.test(f));
  return Promise.all(files.map(async f => JSON.parse(await readFile(resolve(recordings, f), 'utf8'))));
}
// After a session: rules propose the next dose; inside the clinician's envelope with auto-apply on, it applies.
async function progress(prescriptionId: string, pain?: PainReport[]) {
  const all = plans.list(), active = all[all.length - 1];
  let proposal = evaluate(all, prescriptionId, (await readSummaries()).map(evidenceFromSummary), {includeSimulated: progressFromSimulated, pain});
  const x = active.activities.find(a => a.id === prescriptionId)!;
  if ((proposal.decision === 'progress' || proposal.decision === 'regress') && x.progression?.autoApply && proposal.to) {
    const clinician = [...all].reverse().find(p => p.origin === 'clinician')?.approvedBy ?? active.approvedBy;
    const plan = plans.approve({origin:'auto-progression', proposalId: proposal.id, expectedActiveVersion: proposal.planVersion,
      approvedBy: `Auto-progression within ${clinician}'s envelope`, rationale: proposal.reasons.join(' '),
      basedOnExerciseIds: proposal.evidence.map(e => e.exerciseId), changes: {[prescriptionId]: {params: {targetDeg: proposal.to.targetDeg}}}});
    proposal = {...proposal, status: 'applied', appliedPlanVersion: plan.version};
  }
  proposals.save(proposal);
  if (proposal.status === 'pending' || proposal.status === 'applied') proposals.supersede(proposal.prescriptionId, proposal.id);
  return proposal;
}
async function finishExercise() {
  if (!exercise) return null;
  const measured = exercise.summary() as Record<string, any>;
  const summary = {exerciseId, prescriptionId: exercisePrescriptionId, poseSessionId: exercisePoseSession, poseSource: exerciseSource,
    simulated: /synthetic|fixture|simulat/i.test(exerciseSource ?? ''), endedAt: new Date().toISOString(),
    sensor: exercise.kind.requires.includes('imu') ? 'imu' : 'pose', sensors: assigner ? {imu: assigner.state.imu, ref: assigner.state.ref} : null,
    practice: exercisePractice, ...measured, config: measured.params,
    headLean: headLean?.summary() ?? {available: false, thresholdCm: 5}};
  headLean = null;
  await writeFile(resolve(recordings, `exercise-${exerciseId}.summary.json`), JSON.stringify(summary, null, 2));
  const envelope = activitySummaryFromExercise({activitySessionId: exerciseId, activityId: exerciseActivityId,
    venueId: requireActivity(exerciseActivityId).venue,
    startedAt: exerciseStartedAt || summary.endedAt, endedAt: summary.endedAt, measured});
  await writeFile(resolve(recordings, `session-${exerciseId}.json`), JSON.stringify(envelope, null, 2));
  endCurlTrace();
  exerciseBroadcast({type:'exercise.summary', payload:summary});
  exerciseLog?.end(); exerciseLog = null;
  exercise = null; releaseSensors();
  let progression = null;
  if (exercisePractice) return {...summary, progression};
  try { progression = await progress(exercisePrescriptionId); exerciseBroadcast({type:'exercise.progression', payload:progression}); }
  catch (error) { console.error('Progression failed:', (error as Error).message); }
  return {...summary, progression};
}

const server = createServer(async (request, response) => {
  const url = new URL(request.url ?? '/', 'http://localhost');
  if (request.method === 'POST' && url.pathname === '/capture/start') {
    if (request.headers.origin && !allowedOrigins.has(request.headers.origin)) { response.writeHead(403).end(); return; }
    const sourceConnected = producer?.readyState === WebSocket.OPEN;
    if (sourceConnected) producer!.send(JSON.stringify({type:'capture.start'}));
    response.writeHead(200, {'Content-Type':'application/json'}).end(JSON.stringify({sourceConnected})); return;
  }
  if (request.method === 'POST' && (url.pathname === '/exercise/start' || url.pathname === '/exercise/stop')) {
    if (request.headers.origin && !allowedOrigins.has(request.headers.origin)) { response.writeHead(403).end(); return; }
    try {
      if (url.pathname === '/exercise/stop') {
        const summary = await finishExercise();
        response.writeHead(summary ? 200 : 409, {'Content-Type':'application/json'}).end(JSON.stringify(summary ?? {error:'No exercise running'})); return;
      }
      const body = await readJson(request) as Partial<RepParams> & {maxTrunkDeviationDeg?: number; exercise?: string; prescriptionId?: string; activityId?: string; imuChannel?: string};
      // The session pins an approved plan version; its thresholds come from that plan. Explicit fields
      // in the request are development overrides and are recorded as such in the summary config.
      const plan = body.planVersion != null ? plans.get(Number(body.planVersion)) : plans.active();
      if (!plan) throw Error(`Plan v${body.planVersion} does not exist`);
      // Which prescription to measure: the one named, else what the launched activity runs (a movement tile its own
      // kind), else the first measured activity in the plan.
      const launched = body.prescriptionId ? null : prescriptionForActivity(plan, requireActivity(body.activityId ?? 'rehab.studio'));
      const x = launched ? launched.prescription : plan.activities.find(a => a.id === body.prescriptionId);
      if (!x?.exerciseKind) throw Error(body.prescriptionId ? `No measured prescription "${body.prescriptionId}" in plan v${plan.version}` : `Plan v${plan.version} prescribes nothing measured`);
      // `exercise` measures this prescription with another kind (e.g. by camera): a development override.
      // A movement tile measures with its own kind, which may be a better sensor setup than the plan's for the same movement.
      const kind = exerciseKind(body.exercise ?? launched?.measureWith ?? x.exerciseKind);
      if (kind.requires.includes('pose') && !cameraMeasurement)
        throw Error(`Camera (MediaPipe) measurement is off; measure "${x.id}" with the AirPod (change "Measured with" in the portal).`);
      // Sensors (AGENTS.md), one or two: solo both pairs are the patient's; in a group each person has one, so a
      // two-IMU movement cannot be measured there. Refused before anything is torn down.
      if (kind.requires.includes('ref') && groups.current(friends.me().id))
        throw new SensorRuleError(`"${LIBRARY[kind.id]?.label ?? kind.id}" needs two AirPods on one person. In a session with someone else each person has one AirPod: pick a one-AirPod movement.`);
      await finishExercise();
      const p = x.params, target = Number(body.targetDeg ?? p.targetDeg);
      const compensation = body.maxCompensationDeg ?? body.maxTrunkDeviationDeg ?? p.maxCompensationDeg;
      exercise = createSession(kind.id, {side: (body.side ?? p.side) === 'left' ? 'left' : 'right',
        targetDeg: target,
        targetMaxDeg: Number(body.targetMaxDeg ?? target + Number(p.targetMaxDeg) - Number(p.targetDeg)),
        prescribedReps: Number(body.prescribedReps ?? x.targetCount),
        holdMs: Number(body.holdMs ?? p.holdMs ?? 400),
        ...(compensation != null ? {maxCompensationDeg: Number(compensation)} : {}),
        planVersion: plan.version},
        // The prescription's params tune the qualities the exercise is coached on (holdTargetMs, lowerMs, …).
        {...p, ...body});
      exercisePrescriptionId = x.id; exerciseActivityId = x.activityId; exercisePractice = !!launched?.practice; lastImuMs = -Infinity;
      headLean = kind.requires.includes('imu') ? new HeadLean() : null;
      if (headLean) watchHead();
      if (kind.requires.includes('imu')) {
        // One IMU takes whatever is live; two are told apart by which one moves (exercise/imu-assign.ts), once: a set
        // worn the same way as one already told apart starts on that answer.
        const mode: Mode = kind.requires.includes('ref') ? 'two' : 'one', entry = LIBRARY[kind.id];
        const wear = {imu: entry?.sensor ?? 'AirPod on the wrist', ref: entry?.reference};
        // In a group Mac 2 is the other person, so a one-IMU set follows Mac 1 alone, as the games do (AGENTS.md).
        const pinned = body.imuChannel === 'mac1' || body.imuChannel === 'mac2' ? {imu: body.imuChannel as Channel}
          : mode === 'two' ? rememberedPairs.get(wearKey(wear))
          : groups.current(friends.me().id) ? {imu: 'mac1' as Channel} : undefined;
        assigner = new ImuAssigner({mode, wear, pinned}); assignerWear = wear;
        watchMotion();
        assignerTicker = setInterval(() => { if (assigner?.tick(motionNow())) sensorsChanged(); }, 500); assignerTicker.unref();
      }
      exerciseId = randomUUID(); exercisePoseSession = null; exerciseSource = null;
      exerciseStartedAt = new Date().toISOString();
      exerciseLog = createWriteStream(resolve(recordings, `exercise-${exerciseId}.jsonl`));
      const started = {exerciseId, prescriptionId: x.id, practice: exercisePractice, exerciseKind: kind.id, sensor: kind.requires.includes('imu') ? 'imu' : 'pose',
        sensors: sensorState(), config: exercise.params,
        qualities: exercise.qualities.configs};
      exerciseBroadcast({type:'exercise.started', payload: started});
      startCurlTrace(kind.id, Number(exercise.params.targetDeg));
      response.writeHead(200, {'Content-Type':'application/json'}).end(JSON.stringify(started));
    } catch (error) { response.writeHead(error instanceof SensorRuleError ? 409 : 400, {'Content-Type':'application/json'}).end(JSON.stringify({error:String((error as Error).message)})); }
    return;
  }
  if (request.method === 'POST' && url.pathname === '/activity/session') {
    try {
      const envelope = parseActivitySummary(await readJson(request));
      // The id names the file, so it must be a plain id (the games send a GUID), never a path.
      if (!/^[\w-]{1,64}$/.test(envelope.activitySessionId)) throw Error('activitySessionId must be letters, digits, - or _');
      await writeFile(resolve(recordings, `session-${envelope.activitySessionId}.json`), JSON.stringify(envelope, null, 2));
      response.writeHead(201, {'Content-Type':'application/json'}).end(JSON.stringify({stored: envelope.activitySessionId}));
    } catch (error) { response.writeHead(400, {'Content-Type':'application/json'}).end(JSON.stringify({error:String((error as Error).message)})); }
    return;
  }
  // Photos are served as bytes, so this sits ahead of the JSON API block.
  if (request.method === 'GET' && url.pathname.startsWith('/api/friends/photo/')) {
    const photo = messages.photo(decodeURIComponent(url.pathname.slice('/api/friends/photo/'.length)));
    if (!photo) { response.writeHead(404).end(); return; }
    response.writeHead(200, {'Content-Type':photo.contentType,'Cache-Control':'private, max-age=86400'}).end(photo.bytes);
    return;
  }
  if (url.pathname.startsWith('/api/')) {
    if (request.headers.origin && !allowedOrigins.has(request.headers.origin)) { response.writeHead(403).end(); return; }
    const json = (status: number, value: unknown) => response.writeHead(status, {'Content-Type':'application/json','Cache-Control':'no-store'}).end(JSON.stringify(value));
    try {
      if (request.method === 'GET' && url.pathname === '/api/plans') return json(200, plans.list());
      if (request.method === 'GET' && url.pathname === '/api/plans/active') return json(200, plans.active());
      // What launching an activity will measure, so the studio can brief the set before starting it.
      if (request.method === 'GET' && url.pathname === '/api/prescription') {
        const plan = plans.active(), activity = requireActivity(url.searchParams.get('activityId') ?? 'rehab.studio');
        const { prescription, practice } = prescriptionForActivity(plan, activity);
        const entry = prescription?.exerciseKind ? LIBRARY[prescription.exerciseKind] : undefined;
        return json(200, { planVersion: plan.version, activityId: activity.id, practice, prescription,
          label: entry?.label ?? null, sensor: entry?.sensor ?? null, posture: entry?.posture ?? null, cue: entry?.cue ?? null });
      }
      if (request.method === 'POST' && url.pathname === '/api/plans') {
        const {origin, proposalId, ...body} = await readJson(request);   // only progression may mark a version automatic
        return json(201, plans.approve(body));
      }
      if (request.method === 'GET' && url.pathname === '/api/exercises') return json(200, LIBRARY);
      // The motion relay's status (who is connected, how old each stream's last sample is) for the capture page,
      // which cannot read the relay directly across origins.
      if (request.method === 'GET' && url.pathname === '/api/motion-health') {
        try { return json(200, await (await fetch('http://127.0.0.1:8767/', {signal: AbortSignal.timeout(1000)})).json()); }
        catch { return json(503, {ready: false}); }
      }
      // Which AirPod is which for the running set, and what to tell the patient (exercise/imu-assign.ts).
      if (request.method === 'GET' && url.pathname === '/api/sensors') return json(200, sensorState() ?? {phase: 'idle'});
      if (request.method === 'GET' && url.pathname === '/api/golf-unlock') return json(200, golfUnlock(plans.active()));
      if (request.method === 'GET' && url.pathname === '/api/dashboard') {
        // With a plan change the patient has not seen yet, so the menu can say so before anything else.
        return json(200, {...await dashboard(),
          planUpdate: planUpdate({plans: plans.list(), notes: visit.list(), seen: visit.seen})});
      }
      // The therapist visit (Unity: Kinesthetic/Visit, coordinator/visit.ts): the whiteboard's program updates
      // since the patient last visited and what the therapist says about them. The therapist changes the
      // program through program-update (a plan version plus a note to the patient); Unity marks it seen.
      if (request.method === 'GET' && url.pathname === '/api/visit')
        return json(200, buildVisit({plans: plans.list(), notes: visit.list(), seen: visit.seen, therapist}));
      if (request.method === 'POST' && url.pathname === '/api/visit/program-update')
        return json(201, applyProgramUpdate(plans, visit, await readJson(request)));
      if (request.method === 'POST' && url.pathname === '/api/visit/seen') {
        const body = await readJson(request);
        const version = Number(body.planVersion);
        if (!plans.get(version)) return json(404, {error:`Plan v${body.planVersion} does not exist`});
        return json(200, visit.markSeen(version));
      }
      // What the patient relays at the end of a visit, for the clinician; the reply carries what Alex says back.
      if (request.method === 'GET' && url.pathname === '/api/visit/replies') return json(200, visit.replies());
      if (request.method === 'POST' && url.pathname === '/api/visit/replies') {
        const body = await readJson(request);
        return json(201, visit.addReply({kind: body.kind, text: body.text, planVersion: body.planVersion ?? plans.active().version}));
      }
      if (request.method === 'GET' && url.pathname === '/api/visit/notes') return json(200, visit.list());
      if (request.method === 'POST' && url.pathname === '/api/visit/notes') {
        const body = await readJson(request);   // text and author only: seeded and planVersion are not the client's to set
        return json(201, visit.add({text: body.text, author: body.author}));
      }
      const note = url.pathname.match(/^\/api\/visit\/notes\/([0-9a-f-]{36})$/i);
      if (request.method === 'DELETE' && note) return visit.remove(note[1]) ? json(200, {removed: note[1]}) : json(404, {error:'No such note'});
      if (request.method === 'GET' && url.pathname === '/api/proposals') return json(200, proposals.list().slice(0, 50));
      if (request.method === 'POST' && url.pathname === '/api/progression/evaluate') {
        const body = await readJson(request);
        return json(200, await progress(String(body.prescriptionId ?? ''), Array.isArray(body.pain) ? body.pain : undefined));
      }
      const decide = url.pathname.match(/^\/api\/proposals\/([0-9a-f-]{36})\/(approve|dismiss)$/i);
      if (request.method === 'POST' && decide) {
        const proposal = proposals.get(decide[1]);
        if (!proposal) return json(404, {error:'No such proposal'});
        if (proposal.status !== 'pending') return json(409, {error:`Proposal is ${proposal.status}`});
        if (decide[2] === 'dismiss') return json(200, proposals.save({...proposal, status:'dismissed'}));
        if (!proposal.to) return json(409, {error:'This proposal needs a plan change from the clinician; edit the plan instead.'});
        const body = await readJson(request);
        const plan = plans.approve({approvedBy: body.approvedBy, rationale: body.rationale ?? proposal.reasons.join(' '),
          expectedActiveVersion: proposal.planVersion, proposalId: proposal.id, basedOnExerciseIds: proposal.evidence.map(e => e.exerciseId),
          changes: {[proposal.prescriptionId]: {params: {targetDeg: proposal.to.targetDeg}}}});
        return json(200, {proposal: proposals.save({...proposal, status:'approved', appliedPlanVersion: plan.version}), plan});
      }
      if (url.pathname.startsWith('/api/friends')) {
        const me = friends.me().id;
        if (request.method === 'GET' && url.pathname === '/api/friends') {
          const people = friends.list();
          const unread = messages.unread(me, people.map(p => p.id));
          return json(200, {
            me: friends.me(),
            encouragements: ENCOURAGEMENTS,
            friends: people.map(p => ({...p, unread: unread[p.id] ?? 0})),
          });
        }
        if (request.method === 'POST' && url.pathname === '/api/friends/name')
          return json(200, friends.setName((await readJson(request) as {displayName?:string}).displayName ?? ''));
        if (request.method === 'POST' && url.pathname === '/api/friends/invite')
          return json(201, {code: friends.invite()});
        if (request.method === 'POST' && url.pathname === '/api/friends/accept') {
          const body = await readJson(request) as {code?:string; displayName?:string};
          return json(201, friends.accept(body.code ?? '', body.displayName));
        }
        if (request.method === 'POST' && url.pathname === '/api/friends/follow') {
          const body = await readJson(request) as {id?:string; following?:boolean};
          friends.follow(body.id ?? '', body.following !== false);
          return json(200, {ok:true});
        }
        if (request.method === 'GET' && url.pathname === '/api/friends/thread') {
          const other = url.searchParams.get('id') ?? '';
          if (!friends.has(other)) return json(404, {error:'Unknown person'});
          messages.markSeen(me, other);
          return json(200, {person: friends.person(other), messages: messages.thread(me, other)});
        }
        if (request.method === 'POST' && url.pathname === '/api/friends/message') {
          const body = await readJson(request) as {to?:string; kind?:string; text?:string; photoId?:string};
          if (!friends.has(body.to ?? '')) return json(404, {error:'Unknown person'});
          return json(201, messages.send(me, body.to!, body));
        }
        if (request.method === 'POST' && url.pathname === '/api/friends/photo') {
          const bytes = await readBytes(request, 4 * 1024 * 1024);
          return json(201, {photoId: messages.savePhoto(bytes, String(request.headers['content-type'] ?? ''))});
        }
        // Both AI routes answer 200 with empty values when the model is not
        // configured or declines, so the panel treats it as "no opinion today"
        // rather than an error it has to handle.
        // Discovery, on a double opt-in. An open introduction carries a reason and
        // nothing that identifies anyone; only a mutual yes exchanges names, and
        // from there it is the ordinary invite path.
        if (request.method === 'GET' && url.pathname === '/api/friends/introductions') {
          const kind = url.searchParams.get('kind') === 'mentor' ? 'mentor' : 'peer';
          return json(200, {introductions: await introductionCards(kind, 3000)});
        }
        if (request.method === 'POST' && url.pathname === '/api/friends/introductions/answer') {
          const body = await readJson(request) as {id?: string; yes?: boolean};
          const id = String(body.id ?? '');
          let answered = introductions.answer(id, me, body.yes ? 'yes' : 'no');
          // The local stand-in for the other side. Every profile in the local
          // directory is synthetic — there is no second coordinator to answer —
          // so a yes here completes the pair instead of waiting forever. With a
          // real backend this whole branch goes away and the wait is real; the
          // double opt-in in introductions.ts is untouched either way.
          if (body.yes && !IntroductionStore.joined(answered)) {
            const other = answered.pair[0] === me ? answered.pair[1] : answered.pair[0];
            const synthetic = (await directory.profiles()).some(p => p.personId === other);
            if (synthetic) answered = introductions.answer(id, other, 'yes');
          }
          if (!IntroductionStore.joined(answered)) return json(200, {joined: false});
          // Both said yes. Mint a code and redeem it, which is exactly what two
          // people who exchanged one by hand would have done.
          const other = answered.pair[0] === me ? answered.pair[1] : answered.pair[0];
          const profiles = await directory.profiles();
          const known = profiles.find(p => p.personId === other);
          const person = friends.has(other) ? friends.person(other)
            : friends.accept(friends.invite(), known?.displayName || 'A friend');
          return json(201, {joined: true, person});
        }
        // A friend's profile. Activity only: friends.ts holds that one patient's
        // measurements are not another's business, so nothing here carries a
        // degree, a rep count or how close to target they got — only whether they
        // turned up. `measured: false` is how the panel knows not to imply more.
        if (request.method === 'GET' && url.pathname === '/api/friends/profile') {
          const other = url.searchParams.get('id') ?? '';
          if (!friends.has(other)) return json(404, {error:'Unknown person'});
          const known = (await directory.profiles()).find(p => p.personId === other);
          const thread = messages.thread(me, other);
          const theirs = [...thread].reverse().find(m => m.from === other);
          return json(200, {
            person: friends.person(other),
            // Absent for someone whose coordinator we cannot reach, which today is
            // anyone not in the local directory.
            activity: known?.activity ?? null,
            lastActiveDays: daysSince(friends.person(other)?.lastActiveAt ?? theirs?.at),
            goalComponents: known?.goalComponents ?? [],
          });
        }
        if (request.method === 'GET' && url.pathname === '/api/friends/spotlight') {
          const people = friends.list();
          const unread = messages.unread(me, people.map(p => p.id));
          const candidates = people.map(p => {
            const thread = messages.thread(me, p.id);
            const fromThem = thread.filter(m => m.from === p.id);
            const theirs = fromThem[fromThem.length - 1];
            const mine = [...thread].reverse().find(m => m.from === me);
            // "Returned after three days" needs the quiet spell BEFORE they came
            // back, which a single lastActiveAt cannot express. The gap between
            // their last two messages is the one place that survives.
            const previous = fromThem[fromThem.length - 2];
            const gapDays = theirs && previous
              ? Math.max(0, Math.floor((Date.parse(theirs.at) - Date.parse(previous.at)) / 86400000))
              : null;
            // lastActiveAt only moves when someone joins, so a message of theirs
            // is often the fresher evidence. Take whichever is more recent.
            const stamped = daysSince(p.lastActiveAt);
            const wrote = daysSince(theirs?.at);
            const active = stamped === null ? wrote
              : wrote === null ? stamped
              : Math.min(stamped, wrote);
            return {
              id: p.id, displayName: p.displayName, sample: p.sample === true,
              unread: unread[p.id] ?? 0,
              daysSinceActive: active,
              daysSinceTheyWrote: wrote,
              daysSinceIWrote: daysSince(mine?.at),
              quietDaysBeforeTheyReturned: Number.isFinite(gapDays as number) ? gapDays : null,
            };
          });
          return json(200, await spotlight(candidates) ?? {choose: null, activity: []});
        }
        // Deliberately does not mark the thread seen: this is the line above a
        // conversation, not the act of reading it.
        if (request.method === 'GET' && url.pathname === '/api/friends/recap') {
          const other = url.searchParams.get('id') ?? '';
          if (!friends.has(other)) return json(404, {error:'Unknown person'});
          const line = await recap(
            friends.person(other)?.displayName ?? 'them',
            messages.thread(me, other).map(m => ({
              fromMe: m.from === me,
              kind: m.kind ? ENCOURAGEMENTS[m.kind] : null,
              text: m.text,
              photo: m.photoId != null,
              at: m.at,
            })));
          return json(200, {recap: line ?? ''});
        }
        // A first draft of a reply, for the patient's own composer. It is never sent from here.
        if (request.method === 'GET' && url.pathname === '/api/friends/draft') {
          const other = url.searchParams.get('id') ?? '';
          if (!friends.has(other)) return json(404, {error:'Unknown person'});
          const thread = messages.thread(me, other);
          const fromThem = thread.filter(m => m.from === other);
          const [previous, theirs] = [fromThem.at(-2), fromThem.at(-1)];
          const line = await draft(friends.person(other)?.displayName ?? 'them',
            thread.map(m => ({fromMe: m.from === me, kind: m.kind ? ENCOURAGEMENTS[m.kind] : null, text: m.text, photo: m.photoId != null, at: m.at})),
            {daysSinceTheyWrote: daysSince(theirs?.at), daysSinceIWrote: daysSince([...thread].reverse().find(m => m.from === me)?.at),
              quietDaysBeforeTheyReturned: theirs && previous ? Math.floor((Date.parse(theirs.at) - Date.parse(previous.at)) / 86400000) : null});
          return json(200, {draft: line ?? ''});
        }
        // A milestone of turning up (milestones.ts), worded by the model in the patient's voice. It reaches
        // friends only through answer with share: true, which is the patient pressing Share.
        if (request.method === 'GET' && url.pathname === '/api/friends/milestone') {
          const d = await dashboard(), found = milestoneFrom(d);
          if (!found || milestones.answered(found.id) || friends.list().length === 0) return json(200, {id: '', fact: '', line: ''});
          const line = await within(8000, milestoneLine({milestone: found.fact, goal: plans.active()?.goal?.text ?? null, programDay: d.programDay}));
          const fact = found.fact[0].toUpperCase() + found.fact.slice(1);
          return json(200, {id: found.id, fact, line: line || `${fact}.`});
        }
        if (request.method === 'POST' && url.pathname === '/api/friends/milestone/answer') {
          const body = await readJson(request) as {id?: string; share?: boolean; text?: string};
          const found = milestoneFrom(await dashboard());
          if (!found || found.id !== body.id) return json(409, {error:'That milestone is no longer current'});
          if (milestones.answered(found.id)) return json(200, {sharedWith: 0});
          let sharedWith = 0;
          if (body.share) for (const p of friends.list()) { messages.send(me, p.id, {text: String(body.text ?? found.fact)}); sharedWith++; }
          milestones.answer(found.id, body.share ? 'shared' : 'dismissed');
          return json(200, {sharedWith});
        }
        return json(404, {error:'Not found'});
      }
      // Group sessions (groups.ts). Everything here is about "me": the lobby for one
      // activity, the room I am in, and following someone I met there.
      if (url.pathname.startsWith('/api/groups')) {
        const me = friends.me();
        const friendIds = () => new Set(friends.list().map(p => p.id));
        const mine = () => {
          const group = groups.current(me.id);
          return group ? GroupStore.view(group, me.id, friendIds()) : null;
        };
        if (request.method === 'GET' && url.pathname === '/api/groups') {
          const activityId = url.searchParams.get('activity') ?? '';
          requireActivity(activityId);
          const recent = friends.list().filter(p => p.sample)
            .sort((a, b) => String(b.lastActiveAt).localeCompare(String(a.lastActiveAt)));
          groups.seed(activityId, recent);
          if (peerLive()) groups.peerHosts(peer, activityId, me.id);
          return json(200, {...groups.lobby(activityId, friendIds()), current: mine(), samples: groups.samples});
        }
        if (request.method === 'GET' && url.pathname === '/api/groups/current') return json(200, {group: mine()});
        if (request.method === 'POST' && url.pathname === '/api/groups') {
          const body = await readJson(request) as {activityId?: string; open?: boolean};
          requireActivity(body.activityId ?? '');
          groups.create(me, body.activityId!, body.open !== false);
          return json(201, {group: mine()});
        }
        if (request.method === 'POST' && url.pathname === '/api/groups/join') {
          const body = await readJson(request) as {id?: string};
          groups.join(me, String(body.id ?? ''), friendIds());
          return json(200, {group: mine()});
        }
        if (request.method === 'POST' && url.pathname === '/api/groups/leave') {
          groups.leave(me.id);
          return json(200, {group: null});
        }
        if (request.method === 'POST' && url.pathname === '/api/groups/message') {
          groups.send(me.id, await readJson(request) as {kind?: string; text?: string});
          return json(201, {group: mine()});
        }
        // Sample groups or real ones only (groups.ts `setSamples`). The G key on the Mac flips it (GroupPanel).
        if (request.method === 'GET' && url.pathname === '/api/groups/samples') return json(200, {on: groups.samples});
        if (request.method === 'POST' && url.pathname === '/api/groups/samples') {
          const body = await readJson(request) as {on?: boolean};
          groups.setSamples(body.on !== false);
          return json(200, {on: groups.samples, group: mine()});
        }
        // A fake partner, for a demo or a test (groups.ts `inject`): a labelled sample person joins your room.
        if (request.method === 'POST' && url.pathname === '/api/groups/inject') {
          groups.inject(me.id);
          return json(201, {group: mine()});
        }
        // Following someone from the room's list: only someone actually in the room with you.
        if (request.method === 'POST' && url.pathname === '/api/groups/befriend') {
          const body = await readJson(request) as {id?: string};
          const member = groups.member(me.id, String(body.id ?? ''));
          if (!member) return json(404, {error: 'They are not in your group'});
          const person = friends.meet(member);
          return json(201, {person, group: mine()});
        }
        return json(404, {error:'Not found'});
      }
      const replay = url.pathname.match(/^\/api\/sessions\/([0-9a-f-]{36})\/replay$/i);
      if (request.method === 'GET' && replay) return json(200, await loadReplay(recordings, replay[1]));
      // Two endpoints on purpose. /api/sessions stays the exercise-engine view the clinician portal
      // reads today; /api/activity-sessions is the cross-activity envelope view that golf also lands
      // in. Merging them duplicates every exercise session, since a finished session writes both.
      // The portal moves over when it gains a cross-activity table; until then these stay apart.
      // Normative ROM, so a result can read "142 degrees, typical for your age band". Every response
      // says it is provisional: the per-band appendix values have not been applied yet.
      if (request.method === 'GET' && url.pathname === '/api/norms') return json(200, NORMS);
      if (request.method === 'GET' && url.pathname === '/api/norms/compare') {
        const comparison = compareToNorm({
          movement: String(url.searchParams.get('movement') ?? ''),
          measuredDeg: Number(url.searchParams.get('measuredDeg')),
          ageYears: Number(url.searchParams.get('ageYears')),
          sex: (url.searchParams.get('sex') ?? undefined) as Sex | undefined,
          side: (url.searchParams.get('side') ?? undefined) as Side | undefined,
        });
        return comparison ? json(200, comparison)
          : json(404, {error: 'No normative table for that movement, age or measurement'});
      }
      if (request.method === 'GET' && url.pathname === '/api/sessions') {
        const sessions = await readSummaries();
        sessions.sort((a, b) => String(b.endedAt).localeCompare(String(a.endedAt)));
        return json(200, sessions.slice(0, 50));
      }
      // How the patient moved in each game: movements, typical speed and turn, consistency, and forearm ÷ club
      // rotation when a wrist AirPod was on. Computed once per finished session from the motion recordings and
      // kept next to its activity record; the recordings do not change after the session ends.
      // The coach's conversation, event by event: posted by the voice server as it happens, read by the EHR.
      if (request.method === 'POST' && url.pathname === '/api/coach/events') return json(201, coachLog.add(await readJson(request)));
      if (request.method === 'GET' && url.pathname === '/api/coach/events')
        return json(200, url.searchParams.get('scope') === 'recent' ? coachLog.recent(Number(url.searchParams.get('limit') ?? 100)) : coachLog.latestConversation());
      if (request.method === 'GET' && url.pathname === '/api/game-movement') {
        const files = (await readdir(recordings)).filter(f => /^session-.*\.json$/.test(f));
        const envelopes = (await Promise.all(files.map(async f => JSON.parse(await readFile(resolve(recordings, f), 'utf8')))))
          .filter(e => GAME_ACTIVITIES.includes(e.activityId) && Date.parse(e.endedAt) < Date.now() - 5000)
          .sort((a, b) => String(b.endedAt).localeCompare(String(a.endedAt))).slice(0, 20);
        const sessions = [];
        for (const e of envelopes) {
          // Session ids arrive from the game; only a plain id names a cache file.
          const cached = /^[\w-]{1,64}$/.test(String(e.activitySessionId)) ? resolve(recordings, `movement-${e.activitySessionId}.json`) : null;
          let movement;
          try { movement = JSON.parse(await readFile(cached ?? '', 'utf8')); }
          catch { movement = await gameMovement(e, motionDirs); if (cached) await writeFile(cached, JSON.stringify(movement)); }
          sessions.push({ ...movement, dose: e.subjects?.find((x: any) => x.role === 'patient')?.dose ?? null });
        }
        return json(200, sessions);
      }
      if (request.method === 'GET' && url.pathname === '/api/activity-sessions') {
        const files = (await readdir(recordings)).filter(f => /^session-.*\.json$/.test(f));
        const sessions = await Promise.all(files.map(async f => JSON.parse(await readFile(resolve(recordings, f), 'utf8'))));
        sessions.sort((a, b) => String(b.endedAt).localeCompare(String(a.endedAt)));
        return json(200, sessions.slice(0, 100));
      }
      return json(404, {error:'Not found'});
    } catch (error) { return json((error as {status?: number}).status ?? 400, {error:(error as Error).message}); }
  }
  if (request.method !== 'GET') { response.writeHead(405).end(); return; }
  if (url.pathname === '/portal' || url.pathname.startsWith('/portal/')) {
    const path = resolve(portalRoot, '.' + (url.pathname === '/portal' || url.pathname === '/portal/' ? '/index.html' : decodeURIComponent(url.pathname.slice('/portal'.length))));
    if (!path.startsWith(portalRoot + sep)) { response.writeHead(403).end(); return; }
    try { const info = await stat(path); if (!info.isFile()) throw Error();
      response.writeHead(200, {'Content-Type':mime[extname(path)] ?? 'application/octet-stream','Cache-Control':'no-store'}); createReadStream(path).pipe(response);
    } catch { response.writeHead(404).end(); }
    return;
  }
  if (url.pathname === '/ehr' || url.pathname.startsWith('/ehr/')) {
    const rel = decodeURIComponent(url.pathname.slice('/ehr'.length)) || '/';
    let path = resolve(ehrRoot, '.' + rel);
    if (!path.startsWith(ehrRoot + sep) && path !== ehrRoot) { response.writeHead(403).end(); return; }
    // A route inside the app (/ehr/portal/live) is the app's own: its index answers, and the router takes over.
    try { if (!(await stat(path)).isFile()) throw Error(); } catch { path = resolve(ehrRoot, 'index.html'); }
    try {
      await stat(path);
      response.writeHead(200, {'Content-Type':mime[extname(path)] ?? 'application/octet-stream',
        'Cache-Control': path.includes(`${sep}assets${sep}`) ? 'public, max-age=31536000, immutable' : 'no-store'});
      createReadStream(path).pipe(response);
    } catch { response.writeHead(404, {'Content-Type':'text/plain'}).end('The EHR portal is not built: run `npm install && npm run build` in ehr/.'); }
    return;
  }
  if (url.pathname === '/exercise') {
    response.writeHead(200, {'Content-Type':'application/json'}).end(JSON.stringify(exercise
      ? {running:true, exerciseId, phase:exercise.phase, summary:exercise.summary()} : {running:false})); return;
  }
  if (url.pathname === '/health') {
    response.writeHead(200, {'Content-Type':'application/json'}).end(JSON.stringify({
      ready:true, sourceConnected:Boolean(producer), viewers:viewers.size, captureStatus, sequence,
      frameAgeMs:lastReceived ? Math.round(performance.now()-lastReceived) : null,
      sessionId:sessionId || null,
    })); return;
  }
  try {
    const path = resolve(captureRoot, '.' + decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname));
    if (!path.startsWith(captureRoot + sep)) { response.writeHead(403).end(); return; }
    const info = await stat(path);
    if (!info.isFile()) { response.writeHead(404).end(); return; }
    response.writeHead(200, {'Content-Type':mime[extname(path)] ?? 'application/octet-stream', 'Cache-Control':'no-store'});
    createReadStream(path).pipe(response);
  } catch { response.writeHead(404).end(); }
});
const sockets = new WebSocketServer({ noServer:true, maxPayload:128*1024 });
server.on('upgrade', (request, socket, head) => {
  const url = new URL(request.url ?? '/', 'http://localhost');
  const role = url.searchParams.get('role');
  if (url.pathname === '/exercise' && role === 'viewer' && !(request.headers.origin && !allowedOrigins.has(request.headers.origin))) {
    sockets.handleUpgrade(request, socket, head, ws => { exerciseViewers.add(ws); ws.on('close', () => exerciseViewers.delete(ws)); ws.on('error', () => exerciseViewers.delete(ws)); });
    return;
  }
  if (url.pathname !== '/pose' || !['producer','viewer'].includes(role ?? '') ||
      (request.headers.origin && !allowedOrigins.has(request.headers.origin))) { socket.destroy(); return; }
  sockets.handleUpgrade(request, socket, head, ws => sockets.emit('connection', ws, request, role));
});
function broadcast(message: any) {
  const text = JSON.stringify(message);
  for (const ws of viewers) if (ws.readyState === WebSocket.OPEN && ws.bufferedAmount < 128*1024) ws.send(text);
}
function unavailable(reason: string) {
  latest = null; lastReceived = 0; captureStatus = reason;
  broadcast({schemaVersion:'kinesthetic.session.v1', type:'pose.status', sessionId,
    payload:{status:'unavailable', reason}});
}
sockets.on('connection', (ws, _request, role) => {
  if (role === 'viewer') {
    viewers.add(ws);
    if (latest && performance.now()-lastReceived <= 250) ws.send(JSON.stringify(latest));
    ws.on('close', () => viewers.delete(ws));
    ws.on('error', () => viewers.delete(ws));
    return;
  }
  if (producer) { ws.close(1008, 'A pose source is already connected.'); return; }
  producer = ws;
  ws.on('message', bytes => {
    try {
      const data = JSON.parse(bytes.toString());
      if (data.type === 'pose.status') { unavailable(String(data.payload?.reason ?? 'Source stopped')); return; }
      if (data.schemaVersion !== 'kinesthetic.session.v1' || data.type !== 'pose.frame' ||
          !/^[a-f0-9-]{36}$/i.test(data.sessionId) || !Number.isInteger(data.sequence) || data.sequence < 0) throw Error('Invalid pose envelope');
      const f = data.payload;
      if (!f || !Number.isFinite(f.observedAtMonotonicMs) || !Number.isFinite(f.sourceMediaTimeMs) ||
          !Array.isArray(f.imageLandmarks) || !Array.isArray(f.worldLandmarks) ||
          ![0,33].includes(f.imageLandmarks.length) || f.worldLandmarks.length !== f.imageLandmarks.length) throw Error('Invalid landmarks');
      if (sessionId !== data.sessionId) {
        recording?.end(); sessionId = data.sessionId; sequence = -1; sessionStarted = performance.now();
        recording = createWriteStream(resolve(recordings, sessionId + '.jsonl'), {flags:'a'});
        recording.on('error', error => { console.error('Recording failed:', error.message); unavailable('Recording failed'); });
      }
      if (data.sequence <= sequence) return;
      sequence = data.sequence; lastReceived = performance.now(); captureStatus = 'Camera streaming';
      latest = {...data, receivedSessionMs:lastReceived-sessionStarted, hostMonotonicMs:hostMonotonicMs()};
      recording?.write(JSON.stringify(latest)+'\n'); broadcast(latest);
      lastPose = {frame: f, hostMs: latest.hostMonotonicMs};
      // Camera-driven exercises step on each frame; IMU-driven ones step on motion and read this frame then.
      if (exercise && !exercise.kind.requires.includes('imu')) {
        exercisePoseSession ??= sessionId; exerciseSource ??= String(data.sourceId ?? 'unknown');
        feed(exercise.push(f), sessionId);
      }
    } catch { ws.close(1008, 'Invalid pose data'); }
  });
  ws.on('close', () => { if (producer === ws) { producer = null; unavailable('Pose source disconnected'); recording?.end(); recording=null; sessionId=''; sequence=-1; } });
  ws.on('error', () => ws.close());
});
server.listen(port, '127.0.0.1', () => console.log(`Kinesthetic local pose bridge: http://localhost:${port}`));
// Write the Meet card's lines before anyone opens it, so the first look is not the one that waits.
for (const kind of ['peer', 'mentor'] as const) introductionCards(kind, Infinity).catch(() => {});
// Shutdown must actually terminate (see golf-relay.ts): a peer that vanished without a closing handshake,
// or the outgoing motion-relay socket, would otherwise keep the process alive.
function shutdown() {
  recording?.end(); exercise = null; for (const ws of Object.values(motion)) ws?.terminate();
  for (const ws of sockets.clients) ws.terminate(); sockets.close();
  server.close(() => process.exit(0)); setTimeout(() => process.exit(0), 500).unref();
}
process.on('SIGINT', shutdown); process.on('SIGTERM', shutdown);
