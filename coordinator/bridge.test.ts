import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { setTimeout as delay } from 'node:timers/promises';
import { WebSocket } from 'ws';

test('pose routing, source isolation, freshness, recording, and disconnect', {timeout:10000}, async () => {
  const directory = await mkdtemp(join(tmpdir(), 'kinesthetic-bridge-'));
  const port = 18766;
  const child = spawn(process.execPath, ['server.ts'], {cwd:import.meta.dirname,
    env:{...process.env, KINESTHETIC_PORT:String(port), KINESTHETIC_RECORDINGS_DIRECTORY:directory, KINESTHETIC_PLANS_DIRECTORY:join(directory,"plans"), KINESTHETIC_PROPOSALS_DIRECTORY:join(directory,"proposals")}, stdio:['ignore','pipe','pipe']});
  const connections: WebSocket[] = [];
  try {
    await Promise.race([once(child.stdout, 'data'), once(child, 'exit').then(() => { throw Error('Bridge did not start'); })]);
    async function connect(role: string) {
      const socket = new WebSocket(`ws://127.0.0.1:${port}/pose?role=${role}`);
      connections.push(socket); await once(socket, 'open'); return socket;
    }
    const viewer = await connect('viewer');
    const source = await connect('producer');
    const startCommand = once(source, 'message');
    const started = await (await fetch(`http://127.0.0.1:${port}/capture/start`, {method:'POST'})).json();
    assert.equal(started.sourceConnected, true);
    assert.equal(JSON.parse((await startCommand)[0].toString()).type, 'capture.start');
    assert.equal((await fetch(`http://127.0.0.1:${port}/capture/start`, {method:'POST',headers:{Origin:'https://untrusted.example'}})).status, 403);
    const sessionId = randomUUID();
    const landmark = {x:.5,y:.5,z:0,visibility:1};
    const packet = {schemaVersion:'kinesthetic.session.v1',type:'pose.frame',sessionId,sourceId:'test',sequence:0,
      payload:{frameID:0,source:'recorded-video',subjectDetected:true,sourceMediaTimeMs:0,observedAtMonotonicMs:1,
        imageLandmarks:Array(33).fill(landmark),worldLandmarks:Array(33).fill(landmark)}};
    let incoming = once(viewer, 'message'); source.send(JSON.stringify(packet));
    assert.equal(JSON.parse((await incoming)[0].toString()).sequence, 0);
    const rejected = new WebSocket(`ws://127.0.0.1:${port}/pose?role=producer`);
    connections.push(rejected);
    assert.equal((await once(rejected, 'close'))[0], 1008);
    const received: any[] = []; viewer.on('message', bytes => received.push(JSON.parse(bytes.toString())));
    source.send(JSON.stringify(packet)); // repeated sequence must not be recorded or broadcast
    incoming = once(viewer, 'message');
    source.send(JSON.stringify({...packet,sequence:1,payload:{...packet.payload,frameID:1,sourceMediaTimeMs:33}}));
    assert.equal(JSON.parse((await incoming)[0].toString()).sequence, 1);
    assert.deepEqual(received.map(p => p.sequence), [1]);
    await delay(300);
    const lateViewer = await connect('viewer');
    const late: any[] = []; lateViewer.on('message', bytes => late.push(JSON.parse(bytes.toString())));
    await delay(30); assert.equal(late.length, 0, 'Late viewer must not receive stale cached pose');
    incoming = once(viewer, 'message'); source.close();
    assert.equal(JSON.parse((await incoming)[0].toString()).type, 'pose.status');
    await delay(30);
    const records = (await readFile(join(directory, sessionId+'.jsonl'), 'utf8')).trim().split('\n').map(JSON.parse);
    assert.deepEqual(records.map(p => p.sequence), [0,1]);
    assert.ok(records.every(p => Number.isFinite(p.receivedSessionMs)));
    const health = await (await fetch(`http://127.0.0.1:${port}/health`)).json();
    assert.equal(health.sourceConnected, false);
    assert.equal(health.frameAgeMs, null);
    assert.equal(health.captureStatus, 'Pose source disconnected');
    assert.equal((await (await fetch(`http://127.0.0.1:${port}/capture/start`,{method:'POST'})).json()).sourceConnected,false);
    const invalid = await connect('producer');
    const closed = once(invalid, 'close'); invalid.send(JSON.stringify({...packet,sessionId:'../../bad'}));
    assert.equal((await closed)[0], 1008);
  } finally {
    for (const socket of connections) socket.terminate();
    const exited = once(child, 'exit'); child.kill('SIGTERM');
    await exited; await rm(directory, {recursive:true, force:true});
  }
});
