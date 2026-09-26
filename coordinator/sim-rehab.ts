// Streams a synthetic seated shoulder-raise session into the live pose bridge (no camera needed).
// Default script: rest, 5 good reps to 95°, one short rep (55°), one rep reached by leaning the trunk.
// Usage: node sim-rehab.ts [restSeconds]
import { randomUUID } from 'node:crypto';
import { once } from 'node:events';
import { setTimeout as delay } from 'node:timers/promises';
import { WebSocket } from 'ws';
import { frame } from './synthetic-pose.ts';

const rest = Number(process.argv[2] ?? 6);
const socket = new WebSocket('ws://127.0.0.1:8766/pose?role=producer');
await once(socket, 'open');
socket.on('message', () => {});
const sessionId = randomUUID(); let sequence = 0, t = 0; const started = performance.now();
async function send(arm: number, lean = 0) {
  const f = frame(t, arm, lean);
  socket.send(JSON.stringify({schemaVersion:'kinesthetic.session.v1', type:'pose.frame', sessionId, sourceId:'synthetic-rehab', sequence:sequence++,
    payload:{frameID:sequence, source:'recorded-video', subjectDetected:true, observedAtMonotonicMs:performance.now(), ...f}}));
  t += 33.3; await delay(Math.max(0, started + t - performance.now()));
}
const hold = async (arm: number, seconds: number, lean = 0) => { for (let i = 0; i < seconds * 30; i++) await send(arm, lean); };
const rep = async (peak: number, lean = 0) => {
  for (let i = 0; i <= 30; i++) await send(5 + (peak - 5) * i / 30, lean * i / 30);
  await hold(peak, .8, lean);
  for (let i = 30; i >= 0; i--) await send(5 + (peak - 5) * i / 30, lean * i / 30);
  await hold(5, 1);
};
console.log(`Synthetic rehab session ${sessionId}: resting ${rest}s`);
await hold(5, rest);
for (let i = 0; i < 5; i++) { await rep(95); console.log(`rep ${i + 1} (95°)`); }
await rep(55); console.log('short rep (55°)');
await rep(95, 22); console.log('leaning rep (95°, trunk 22°)');
for (let i = 0; i < 3; i++) await rep(95);
await hold(5, 2);
socket.close(); console.log('done');
