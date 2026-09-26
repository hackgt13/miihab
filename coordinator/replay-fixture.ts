// Transport verification only: label all packets as recorded even if originally captured by a camera.
import { readFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import { once } from 'node:events';
import { setTimeout as delay } from 'node:timers/promises';
import { WebSocket } from 'ws';

const file = process.argv[2];
if (!file) throw Error('Usage: node replay-fixture.ts /absolute/path/capture.json [repeat-count]');
const capture = JSON.parse(await readFile(file, 'utf8'));
if (capture.schemaVersion !== 'kinesthetic.pose-capture.v1' || !capture.frames?.length) throw Error('Invalid pose capture');
const repeats = Number(process.argv[3] ?? 1);
if (!Number.isInteger(repeats) || repeats < 1 || repeats > 10) throw Error('Repeat count must be 1–10');
const socket = new WebSocket('ws://127.0.0.1:8766/pose?role=producer');
await once(socket, 'open');
const sessionId = randomUUID();
console.log(`Recorded transport fixture: ${capture.frames.length} frames × ${repeats}; session ${sessionId}`);
let sequence = 0;
try {
  for (let repeat = 0; repeat < repeats; repeat++) {
    const started = performance.now(), first = capture.frames[0].sourceMediaTimeMs;
    for (const frame of capture.frames) {
      await delay(Math.max(0, started + frame.sourceMediaTimeMs - first - performance.now()));
      if (socket.readyState !== WebSocket.OPEN) throw Error('Pose bridge closed the fixture connection');
      socket.send(JSON.stringify({schemaVersion:'kinesthetic.session.v1',type:'pose.frame',sessionId,
        sourceId:'recorded-fixture',sequence:sequence++,captureMonotonicMs:null,planVersion:null,calibrationId:null,
        payload:{...frame,source:'recorded-video'}}));
    }
  }
} finally { socket.close(); }
console.log(`Completed ${sequence} recorded packets.`);
