// The whole demo loop with no AirPods: both pairs, played from the keyboard.
//
//   node coordinator/sim-demo.ts            (from the repo root, with the relay and coordinator running)
//
// It connects exactly as the real pairs will: Mac 1 from this Mac, Mac 2 with the pairing token, the way the second
// Mac arrives over Tailscale — so the relay, the games and the studio see the demo's own wiring, just with synthetic
// motion. The headset is real: wear it and look around; its head pose is its own. Each key is one moment of the demo:
//
//   w   studio, first set: wiggle the wrist pair while the chest pair rests (how the pairs are told apart, once)
//   r   studio: one arm raise on the wrist pair (Mac 2), the chest pair (Mac 1) still — up, hold, down
//   g   golf: one swing with both pairs held together in the grip (Mac 2 a little stronger, so it leads)
//   b   bowling: one throw on the wrist pair (Mac 2), the other pair resting
//   a   auto: repeat the last one every few seconds, hands free, until any key
//   q   quit
//
// Every session id starts `simulated-`, so nothing this sends can count as a real set (progression.ts refuses
// simulated sessions and the EHR labels them). Quit the Motion apps first: a real pair holds a slot.
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { randomUUID } from 'node:crypto';
import { once } from 'node:events';
import { setTimeout as delay } from 'node:timers/promises';
import { WebSocket } from 'ws';

const relay = process.env.KINESTHETIC_RELAY ?? 'ws://127.0.0.1:8767';
const http = relay.replace(/^ws/, 'http');
const token = (() => {
  try { return process.env.KINESTHETIC_PAIR_TOKEN ?? readFileSync(resolve(import.meta.dirname, '../local-data/pair-token.txt'), 'utf8').trim(); }
  catch { return ''; }
})();

const health = await fetch(http + '/').then(r => r.json()).catch(() => null);
if (!health) { console.error(`No relay at ${http}. Start it: zsh scripts/start_demo_services.sh`); process.exit(1); }
const held = Object.keys(health.macs ?? {});
if (held.length) {
  console.error(`A real AirPod pair already holds ${held.map(m => m.replace('mac', 'Mac ')).join(' and ')}. ` +
    'Quit Kinesthetic Club Motion / Bowling Motion (and on the other Mac), then run this again.');
  process.exit(1);
}
if (!token) console.warn('No pairing token (local-data/pair-token.txt): Mac 2 will connect as a local stream. Fine for the games.');

const HZ = 50, DT = 1 / HZ;
interface Pair { name: string; ws: WebSocket; type: string; player: string; session: string; seq: number; time: number; deg: number; sourceId: string }
async function pair(name: string, path: string, type: string, sourceId: string, paired: boolean): Promise<Pair> {
  const ws = new WebSocket(`${relay}${path}?role=producer&player=patient${paired && token ? `&token=${encodeURIComponent(token)}` : ''}`);
  ws.on('close', (code, why) => { console.error(`\n${name} closed by the relay (${code} ${why}).`); process.exit(1); });
  ws.on('error', e => { console.error(`${name}: ${e.message}`); process.exit(1); });
  await once(ws, 'open');
  return { name, ws, type, player: 'patient', session: `simulated-${randomUUID()}`, seq: 0, time: 0, deg: 0, sourceId };
}
// Mac 1 on the club app from this Mac; Mac 2 on the bowling app, paired, as the second Mac is.
const mac1 = await pair('Mac 1', '/golf', 'club.motion', 'Right', false);
const mac2 = await pair('Mac 2', '/bowling-motion', 'bowling.motion', 'Left', true);

/** One sample: the pair turned `deg` about its x axis (the studio's arm angle), moving at `rate` rad/s. */
function send(p: Pair, deg: number, rate: number) {
  p.time += DT; p.deg = deg;
  const t = deg * Math.PI / 360, jitter = () => (Math.random() - .5) * .02;   // a resting pair is never perfectly still
  p.ws.send(JSON.stringify({ type: p.type, playerId: p.player, sourceId: p.sourceId, sessionId: p.session,
    sequence: p.seq++, sensorTime: p.time, quaternion: [Math.sin(t), 0, 0, Math.cos(t)], rotationRate: [rate + jitter(), jitter(), jitter()] }));
}

// Each moment is a function of time for each pair: angle in degrees. Rates come from the change, as a real IMU's do.
type Track = (tMs: number) => number;
const rest: Track = () => 0;
const smooth = (x: number) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);
const raise = (peak: number): Track => t => t < 2000 ? peak * smooth(t / 2000) : t < 4000 ? peak : peak * (1 - smooth((t - 4000) / 3000));
const wiggle: Track = t => 25 * Math.sin(t / 1000 * 2 * Math.PI * 1.5) * (t < 1600 ? 1 : 0) + (t < 1600 ? 25 : 0);
// A swing: take it back, pause at the top, through fast, finish high. Stronger means a faster downswing.
const swing = (gain: number): Track => t => t < 900 ? -70 * gain * smooth(t / 900) : t < 1100 ? -70 * gain
  : t < 1400 ? -70 * gain + 170 * gain * smooth((t - 1100) / 300) : 100 * gain * (1 - smooth((t - 1400) / 1200));
// A throw: arm back, then forward and up through release.
const throwArm: Track = t => t < 700 ? -45 * smooth(t / 700) : t < 1050 ? -45 + 125 * smooth((t - 700) / 350) : 80 * (1 - smooth((t - 1050) / 900));

const MOMENTS: Record<string, { label: string; ms: number; mac1: Track; mac2: Track }> = {
  w: { label: 'studio: wiggle the wrist pair (Mac 2), chest pair (Mac 1) still', ms: 2200, mac1: rest, mac2: wiggle },
  r: { label: 'studio: arm raise to 90° on the wrist pair (Mac 2)', ms: 7600, mac1: rest, mac2: raise(90) },
  g: { label: 'golf: swing, both pairs together in the grip', ms: 2800, mac1: swing(.9), mac2: swing(1) },
  b: { label: 'bowling: throw on the wrist pair (Mac 2)', ms: 2200, mac1: rest, mac2: throwArm },
};

let playing: { key: string; start: number } | null = null, auto = false, last = 'r', quit = false;
const stdin = process.stdin;
if (stdin.isTTY) stdin.setRawMode(true);
stdin.resume(); stdin.setEncoding('utf8');
stdin.on('data', (chunk: string) => {
  for (const key of chunk) {
    if (key === 'q' || key === '\u0003') { quit = true; return; }
    if (auto) { auto = false; console.log('\n  auto off'); }
    if (key === 'a') { auto = true; console.log(`\n  auto: "${MOMENTS[last].label}" every few seconds — any key stops`); continue; }
    if (MOMENTS[key]) { last = key; playing = { key, start: Date.now() }; console.log(`\n  ${MOMENTS[key].label}`); }
  }
});

console.log(`Both pairs streaming: Mac 1 (this Mac) and Mac 2 (${token ? 'paired, as the second Mac' : 'local'}).
  w  studio first set: wiggle wrist pair     r  studio: arm raise
  g  golf: swing                             b  bowling: throw
  a  auto-repeat the last one                q  quit
Rest a couple of seconds first: every game calibrates on a still pair.`);

let idleSince = Date.now(), ticks = 0, following: string | null = null;
while (!quit) {
  const now = Date.now();
  if (!playing && auto && now - idleSince > 3500) playing = { key: last, start: now };
  const m = playing && MOMENTS[playing.key];
  const t = playing ? now - playing.start : 0;
  for (const [p, track] of [[mac1, m ? m.mac1 : rest], [mac2, m ? m.mac2 : rest]] as [Pair, Track][]) {
    const deg = track(t), rate = (deg - p.deg) * Math.PI / 180 / DT;
    send(p, deg, rate);
  }
  if (m && t >= m.ms) { playing = null; idleSince = now; }
  // Asked in the background: a slow answer must never stall the 50 Hz stream.
  if (++ticks % 10 === 0) fetch(http + '/').then(r => r.json()).then(h => { following = h?.following ?? null; }).catch(() => {});
  if (ticks % 5 === 0) process.stdout.write(`\r  Mac 1 ${mac1.deg.toFixed(0).padStart(4)}°   Mac 2 ${mac2.deg.toFixed(0).padStart(4)}°   games follow: ${following ? following.replace('mac', 'Mac ') : '—'}   `);
  await delay(1000 * DT);
}
console.log('\n  stopped.');
mac1.ws.removeAllListeners('close'); mac2.ws.removeAllListeners('close');
mac1.ws.close(); mac2.ws.close();
if (stdin.isTTY) stdin.setRawMode(false);
process.exit(0);
