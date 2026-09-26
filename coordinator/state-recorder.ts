// Records or replays the Mac's golf.state stream, to test the headset client without a second Unity instance.
// Record:  node state-recorder.ts record out.jsonl [seconds]
// Replay:  node state-recorder.ts replay in.jsonl   (acts as the host; loops once)
import { createWriteStream, readFileSync } from 'node:fs';
import { once } from 'node:events';
import { setTimeout as delay } from 'node:timers/promises';
import { WebSocket } from 'ws';

const [mode, file, seconds = '12'] = process.argv.slice(2);
if (mode === 'record') {
  const ws = new WebSocket('ws://127.0.0.1:8767/state?role=client'); await once(ws, 'open');
  const out = createWriteStream(file); const start = Date.now(); let n = 0;
  ws.on('message', m => { out.write(JSON.stringify({ t: Date.now() - start, state: JSON.parse(m.toString()) }) + '\n'); n++; });
  await delay(Number(seconds) * 1000); ws.close(); out.end(); console.log(`recorded ${n} states`);
} else if (mode === 'replay') {
  const rows = readFileSync(file, 'utf8').trim().split('\n').map(l => JSON.parse(l)).filter(r => r.state.type === 'golf.state');
  const ws = new WebSocket('ws://127.0.0.1:8767/state?role=host'); await once(ws, 'open');
  const start = performance.now();
  for (const r of rows) { await delay(Math.max(0, start + r.t - performance.now())); ws.send(JSON.stringify(r.state)); }
  console.log(`replayed ${rows.length} states`); await delay(500); ws.close();
} else throw Error('mode must be record or replay');
