import { createServer } from 'node:http';
import { createReadStream, createWriteStream, mkdirSync } from 'node:fs';
import { stat } from 'node:fs/promises';
import { dirname, extname, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { WebSocketServer, WebSocket } from 'ws';
import { randomUUID } from 'node:crypto';
import { writeFile, readdir, readFile } from 'node:fs/promises';
import { PlanStore } from './plans.ts';
import { loadReplay } from './replay.ts';
import { ShoulderRaiseSession, type ExerciseConfig } from './measurement.ts';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const captureRoot = resolve(root, 'spikes/pose-capture');
const recordings = resolve(process.env.KINESTHETIC_RECORDINGS_DIRECTORY ?? resolve(root, 'local-data/sessions'));
mkdirSync(recordings, { recursive: true });
const port = Number(process.env.KINESTHETIC_PORT ?? 8766);
const plans = new PlanStore(resolve(process.env.KINESTHETIC_PLANS_DIRECTORY ?? resolve(root, 'local-data/plans')));
const portalRoot = resolve(root, 'coordinator/portal');
const historyFixture = resolve(root, 'coordinator/fixtures/history.json');
const allowedOrigins = new Set([`http://localhost:${port}`, `http://127.0.0.1:${port}`, 'http://localhost:8765', 'http://127.0.0.1:8765']);
const mime: Record<string,string> = { '.html':'text/html', '.js':'text/javascript', '.mjs':'text/javascript', '.css':'text/css', '.wasm':'application/wasm', '.task':'application/octet-stream' };
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
let exercise: ShoulderRaiseSession | null = null;
let exerciseId = '';
let exercisePoseSession: string | null = null;
let exerciseSource: string | null = null;   // producer sourceId, e.g. camera vs synthetic simulator
// Kept apart from the pose recording: Unity's replay loader accepts only pose.frame lines there.
let exerciseLog: ReturnType<typeof createWriteStream> | null = null;
function exerciseBroadcast(message: any) {
  const text = JSON.stringify({schemaVersion:'kinesthetic.session.v1', exerciseId, ...message});
  for (const ws of exerciseViewers) if (ws.readyState === WebSocket.OPEN && ws.bufferedAmount < 128*1024) ws.send(text);
}
async function readJson(request: import('node:http').IncomingMessage) {
  let body = ''; for await (const chunk of request) { body += chunk; if (body.length > 16384) throw Error('Body too large'); }
  return body ? JSON.parse(body) : {};
}
async function finishExercise() {
  if (!exercise) return null;
  const summary = {exerciseId, poseSessionId: exercisePoseSession, poseSource: exerciseSource,
    simulated: /synthetic|fixture|simulat/i.test(exerciseSource ?? ''), endedAt: new Date().toISOString(), ...exercise.summary()};
  await writeFile(resolve(recordings, `exercise-${exerciseId}.summary.json`), JSON.stringify(summary, null, 2));
  exerciseBroadcast({type:'exercise.summary', payload:summary});
  exerciseLog?.end(); exerciseLog = null;
  exercise = null; return summary;
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
      const body = await readJson(request) as Partial<ExerciseConfig>;
      // The session pins an approved plan version; its thresholds come from that plan. Explicit fields
      // in the request are development overrides and are recorded as such in the summary config.
      const plan = body.planVersion != null ? plans.get(Number(body.planVersion)) : plans.active();
      if (!plan) throw Error(`Plan v${body.planVersion} does not exist`);
      await finishExercise();
      exercise = new ShoulderRaiseSession({side: (body.side ?? plan.exercise.side) === 'left' ? 'left' : 'right',
        targetDeg: Number(body.targetDeg ?? plan.exercise.targetDeg),
        prescribedReps: Number(body.prescribedReps ?? plan.exercise.prescribedReps),
        holdMs: Number(body.holdMs ?? plan.exercise.holdMs),
        maxTrunkDeviationDeg: Number(body.maxTrunkDeviationDeg ?? plan.exercise.maxTrunkDeviationDeg),
        planVersion: plan.version});
      exerciseId = randomUUID(); exercisePoseSession = null; exerciseSource = null;
      exerciseLog = createWriteStream(resolve(recordings, `exercise-${exerciseId}.jsonl`));
      exerciseBroadcast({type:'exercise.started', payload:{config: exercise.config}});
      response.writeHead(200, {'Content-Type':'application/json'}).end(JSON.stringify({exerciseId, config: exercise.config}));
    } catch (error) { response.writeHead(400, {'Content-Type':'application/json'}).end(JSON.stringify({error:String((error as Error).message)})); }
    return;
  }
  if (url.pathname.startsWith('/api/')) {
    if (request.headers.origin && !allowedOrigins.has(request.headers.origin)) { response.writeHead(403).end(); return; }
    const json = (status: number, value: unknown) => response.writeHead(status, {'Content-Type':'application/json','Cache-Control':'no-store'}).end(JSON.stringify(value));
    try {
      if (request.method === 'GET' && url.pathname === '/api/plans') return json(200, plans.list());
      if (request.method === 'GET' && url.pathname === '/api/plans/active') return json(200, plans.active());
      if (request.method === 'POST' && url.pathname === '/api/plans') return json(201, plans.approve(await readJson(request)));
      if (request.method === 'GET' && url.pathname === '/api/history') return json(200, JSON.parse(await readFile(historyFixture, 'utf8')));
      const replay = url.pathname.match(/^\/api\/sessions\/([0-9a-f-]{36})\/replay$/i);
      if (request.method === 'GET' && replay) return json(200, await loadReplay(recordings, replay[1]));
      if (request.method === 'GET' && url.pathname === '/api/sessions') {
        const files = (await readdir(recordings)).filter(f => /^exercise-.*\.summary\.json$/.test(f));
        const sessions = await Promise.all(files.map(async f => JSON.parse(await readFile(resolve(recordings, f), 'utf8'))));
        sessions.sort((a, b) => String(b.endedAt).localeCompare(String(a.endedAt)));
        return json(200, sessions.slice(0, 50));
      }
      return json(404, {error:'Not found'});
    } catch (error) { return json(400, {error:(error as Error).message}); }
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
      latest = {...data, receivedSessionMs:lastReceived-sessionStarted};
      recording?.write(JSON.stringify(latest)+'\n'); broadcast(latest);
      if (exercise) {
        exercisePoseSession ??= sessionId; exerciseSource ??= String(data.sourceId ?? 'unknown');
        const events = exercise.push(f);
        const sample = exercise.samples.at(-1)!;
        exerciseBroadcast({type:'exercise.sample', payload:{...sample, phase:exercise.phase, rep:exercise.currentRep}});
        for (const event of events) { exerciseBroadcast({type:'exercise.event', payload:event}); exerciseLog?.write(JSON.stringify({type:'exercise.event', exerciseId, poseSessionId:sessionId, payload:event})+'\n'); }
      }
    } catch { ws.close(1008, 'Invalid pose data'); }
  });
  ws.on('close', () => { if (producer === ws) { producer = null; unavailable('Pose source disconnected'); recording?.end(); recording=null; sessionId=''; sequence=-1; } });
  ws.on('error', () => ws.close());
});
server.listen(port, '127.0.0.1', () => console.log(`Kinesthetic local pose bridge: http://localhost:${port}`));
function shutdown() { recording?.end(); for (const ws of sockets.clients) ws.close(); sockets.close(); server.close(); }
process.on('SIGINT', shutdown); process.on('SIGTERM', shutdown);
