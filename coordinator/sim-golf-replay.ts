// Simulated end-to-end golf input from recorded data, for testing without a camera or live AirPod.
// Pose: loops recorded address frames (hands together, both arms visible) into the pose relay.
// Motion: replays a real recorded AirPod swing into the golf relay, preceded by a held still period
// (the recorded attitude repeated) so Unity can auto-calibrate. Timing between the two recordings is
// not synchronized; this checks the transport, readiness, calibration, swing gate and launch path.
import { readFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import { once } from 'node:events';
import { setTimeout as delay } from 'node:timers/promises';
import { WebSocket } from 'ws';

const repo = new URL('..', import.meta.url).pathname;
const poseFile = process.env.SIM_POSE ?? `${repo}artifacts/pose-tests/v2/p1.json`;
const imuFile = process.env.SIM_IMU ?? `${repo}local-data/golf/patient-1790252289340.jsonl`;
const addressFrames = (process.env.SIM_ADDRESS_FRAMES ?? '0-14').split('-').map(Number);
const swingFrom = Number(process.env.SIM_SWING_FROM ?? 17.14), swingTo = Number(process.env.SIM_SWING_TO ?? 23);
const holdSeconds = Number(process.env.SIM_HOLD ?? 6), leadSeconds = Number(process.env.SIM_LEAD ?? 4);
const player = process.env.SIM_PLAYER ?? 'patient';

const capture = JSON.parse(await readFile(poseFile, 'utf8'));
const address = capture.frames.filter((f: any) => f.frameID >= addressFrames[0] && f.frameID <= addressFrames[1]);
if (!address.length) throw Error('No address frames selected');
const rows = (await readFile(imuFile, 'utf8')).trim().split('\n').map(l => JSON.parse(l));
const t0 = rows[0].sensorTime;
const swing = rows.filter(r => r.sensorTime - t0 >= swingFrom && r.sensorTime - t0 <= swingTo);
if (!swing.length) throw Error('No motion samples in the selected window');

const pose = new WebSocket('ws://127.0.0.1:8766/pose?role=producer');
await once(pose, 'open');
const poseSession = randomUUID();
let poseSeq = 0, running = true;
pose.on('message', () => {}); // capture.start requests from Unity are acknowledged implicitly
const poseLoop = (async () => {
  const started = performance.now();
  while (running && pose.readyState === WebSocket.OPEN) {
    const i = poseSeq % (address.length * 2 - 2 || 1);
    const frame = address[i < address.length ? i : address.length * 2 - 2 - i];
    pose.send(JSON.stringify({schemaVersion:'kinesthetic.session.v1',type:'pose.frame',sessionId:poseSession,
      sourceId:'simulated-recording',sequence:poseSeq,captureMonotonicMs:null,planVersion:null,calibrationId:null,
      payload:{...frame,frameID:poseSeq,source:'recorded-video'}}));
    poseSeq++;
    await delay(Math.max(0, started + poseSeq * 33.3 - performance.now()));
  }
})();
console.log(`Pose: looping ${address.length} recorded address frames at 30 fps (session ${poseSession}).`);
console.log(`Waiting ${leadSeconds}s for Unity to see the pose before motion starts…`);
await delay(leadSeconds * 1000);

const motion = new WebSocket(`ws://127.0.0.1:8767/golf?role=producer&player=${player}`);
await once(motion, 'open');
const motionSession = randomUUID();
let seq = 0, clock = 1000;
const send = (q: number[], rate: number[]) =>
  motion.send(JSON.stringify({type:'club.motion',playerId:player,sourceId:swing[0].sourceId,sessionId:motionSession,
    sequence:seq++,sensorTime:clock,quaternion:q,rotationRate:rate}));
console.log(`Motion: holding recorded address attitude still for ${holdSeconds}s…`);
for (let t = 0; t < holdSeconds; t += 0.02) { send(swing[0].quaternion, [0.01, -0.01, 0.005]); clock += 0.02; await delay(20); }
console.log(`Motion: replaying real recorded swing (${swing.length} samples, ${swingFrom}–${swingTo}s of the recording).`);
for (let i = 0; i < swing.length; i++) {
  const dt = i ? Math.min(0.1, swing[i].sensorTime - swing[i - 1].sensorTime) : 0.02;
  clock += dt; send(swing[i].quaternion, swing[i].rotationRate); await delay(dt * 1000);
}
console.log('Motion: swing complete; holding still for 3s.');
for (let t = 0; t < 3; t += 0.02) { send(swing.at(-1).quaternion, [0.01, 0, 0]); clock += 0.02; await delay(20); }
running = false; await poseLoop; pose.close(); motion.close();
console.log(`Done: ${poseSeq} pose packets, ${seq} motion packets.`);
