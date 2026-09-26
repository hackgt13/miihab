// Drive the IMU path with no AirPod attached, by standing in for one at the relay's producer socket.
//
//   Replay a real recording (highest fidelity — these are genuine AirPod samples):
//     node sim-motion.ts replay ../local-data/golf/patient-1790408367513.jsonl [--speed 4] [--loop]
//
//   Synthesise a clean set of reps (deterministic, for checking rep counting and validity):
//     node sim-motion.ts reps [count] [--peak 52] [--channel club|wrist] [--short] [--fast]
//
//   Drive it by hand from the keyboard (arrow keys raise and lower, space swings):
//     node sim-motion.ts hand [--channel club|wrist]
//
// Why this exists: sim-rehab.ts streams synthetic *pose* into the bridge, but camera measurement is
// off (KINESTHETIC_CAMERA_MEASUREMENT), so the only live measurement path is the IMU and nothing
// could exercise it without hardware. Golf, bowling and the studio all read the relay, so all three
// become testable from a recording.
//
// The relay validates strictly (golf-relay.ts): sequence and sensorTime must both strictly increase,
// sourceId must be Left or Right, the quaternion must be near-unit and rates under 100 rad/s. Both
// modes resequence from zero onto a fresh session id rather than replaying the recorded ones, so a
// looped file is still a legal stream.

import { createReadStream } from 'node:fs';
import { createInterface } from 'node:readline';
import { randomUUID } from 'node:crypto';
import { once } from 'node:events';
import { setTimeout as delay } from 'node:timers/promises';
import { WebSocket } from 'ws';

type Channel = 'club' | 'wrist';
const CHANNELS: Record<Channel, { path: string; type: string }> = {
  // The studio reads 'club' unless the prescription sets imuSource: 'wrist'; golf reads 'club'; bowling 'wrist'.
  club: { path: '/golf', type: 'club.motion' },
  wrist: { path: '/bowling-motion', type: 'bowling.motion' },
};

const argv = process.argv.slice(2);
const mode = argv[0] ?? 'reps';
const flag = (name: string, fallback: number) => {
  const at = argv.indexOf(`--${name}`);
  return at >= 0 && argv[at + 1] != null ? Number(argv[at + 1]) : fallback;
};
const has = (name: string) => argv.includes(`--${name}`);
const channelArg = (() => {
  const at = argv.indexOf('--channel');
  const value = at >= 0 ? argv[at + 1] : 'club';
  if (value !== 'club' && value !== 'wrist') throw Error(`--channel must be club or wrist, got "${value}"`);
  return value as Channel;
})();

const player = process.env.KINESTHETIC_MOTION_PLAYER ?? 'patient';
const relay = process.env.KINESTHETIC_RELAY ?? 'ws://127.0.0.1:8767';
const { path, type } = CHANNELS[channelArg];
// The session id must mark itself as not a person moving. server.ts derives `simulated` from the source
// string it builds out of this id, progression.ts refuses to count simulated sessions, and the portal and
// the patient's summary both label them. Without the prefix a synthetic set is indistinguishable from a
// real one in the clinical record, which is the one outcome a simulator must never cause.
const sessionId = `simulated-${randomUUID()}`;

const socket = new WebSocket(`${relay}${path}?role=producer&player=${player}`);
socket.on('unexpected-response', (_req, res) => {
  console.error(`Relay refused the producer socket (${res.statusCode}). Another producer may hold "${player}" — ` +
    'disconnect the AirPod app, or pass a different KINESTHETIC_MOTION_PLAYER.');
  process.exit(1);
});
socket.on('error', error => { console.error(`Cannot reach the relay at ${relay}: ${(error as Error).message}\n` +
  'Start it with: node golf-relay.ts   (or scripts/start_demo_services.sh)'); process.exit(1); });
await once(socket, 'open');

let sequence = 0, sensorTime = 0;
/** One legal sample. `seconds` is the gap since the previous one; the relay needs both counters to rise. */
function send(quaternion: number[], rotationRate: number[], seconds: number, sourceId = 'Right') {
  sensorTime += Math.max(seconds, 1e-4);   // strictly increasing even if a recording repeats a stamp
  socket.send(JSON.stringify({ type, playerId: player, sourceId, sessionId,
    sequence: sequence++, sensorTime, quaternion, rotationRate }));
}

if (mode === 'replay') {
  const file = argv[1];
  if (!file) throw Error('Usage: node sim-motion.ts replay <recording.jsonl> [--speed N] [--loop]');
  const speed = flag('speed', 1);
  console.log(`Replaying ${file} → ${relay}${path} as "${player}" at ${speed}×${has('loop') ? ', looping' : ''}`);

  do {
    let previous: number | null = null, sent = 0;
    const lines = createInterface({ input: createReadStream(file), crlfDelay: Infinity });
    for await (const line of lines) {
      if (!line.trim()) continue;
      let row: any;
      try { row = JSON.parse(line); } catch { continue; }
      if (!Array.isArray(row.quaternion) || !Array.isArray(row.rotationRate)) continue;
      // Recorded gaps are the point of a replay: reconnects and dropouts are what the engine must survive.
      const gap = previous == null ? 0.02 : Math.max(row.sensorTime - previous, 1e-4);
      previous = row.sensorTime;
      if (sent) await delay((gap * 1000) / speed);
      send(row.quaternion, row.rotationRate, gap, row.sourceId === 'Left' ? 'Left' : 'Right');
      if (++sent % 500 === 0) process.stdout.write(`\r  ${sent} samples`);
    }
    console.log(`\r  ${sent} samples sent.`);
  } while (has('loop'));
} else if (mode === 'reps') {
  // A rotation about the device x-axis by theta puts the world vertical at [0, sin t, cos t] in the
  // device frame, so the angle from the resting vertical is exactly theta — see verticalInDevice() in
  // exercise/arm-elevation.ts. That makes the commanded angle the measured angle, with no fitting.
  const reps = Number(argv[1]) || 8;
  const peak = flag('peak', has('short') ? 30 : 52);   // --short lands inside the rest band: invalid reps
  const hz = 50, dt = 1 / hz;
  const riseMs = has('fast') ? 250 : 900;              // --fast trips the minRepMs floor
  const holdMs = 600, restMs = 900;

  const at = (deg: number, rateRadS: number) => {
    const t = (deg * Math.PI) / 180 / 2;
    send([Math.sin(t), 0, 0, Math.cos(t)], [rateRadS, 0, 0], dt);
  };
  const ramp = async (from: number, to: number, ms: number) => {
    const steps = Math.max(1, Math.round(ms / 1000 * hz));
    const rate = ((to - from) * Math.PI / 180) / (ms / 1000);
    for (let i = 1; i <= steps; i++) { at(from + (to - from) * (i / steps), rate); await delay(1000 * dt); }
  };
  const still = async (deg: number, ms: number) => {
    for (let i = 0; i < Math.round(ms / 1000 * hz); i++) { at(deg, 0); await delay(1000 * dt); }
  };

  console.log(`Synthesising ${reps} reps to ${peak}° → ${relay}${path} as "${player}"`);
  // Calibration only accepts frames under 0.35 rad/s and needs calibrationMs of them; two seconds is ample.
  console.log('  holding still to calibrate…');
  await still(0, 2000);
  for (let rep = 1; rep <= reps; rep++) {
    process.stdout.write(`\r  rep ${rep} of ${reps}`);
    await ramp(0, peak, riseMs);
    await still(peak, holdMs);
    await ramp(peak, 0, riseMs);
    await still(0, restMs);
  }
  console.log('\n  done.');
} else if (mode === 'hand') {
  // You are the sensor. The stream never stops, so the engine sees a continuous limb rather than jumps:
  // the keys move a *commanded* angle and the arm eases toward it at a human rate, which is what makes
  // the angular rate realistic enough for calibration stillness and the swing gate to behave.
  const hz = 50, dt = 1 / hz, maxDegPerSec = 70;
  let commanded = 0, actual = 0, swing: { until: number; peak: number } | null = null, samples = 0;
  const still0 = async (ms: number) => {
    for (let i = 0; i < Math.round(ms / 1000 * hz); i++) { send([0, 0, 0, 1], [0, 0, 0], dt); await delay(1000 * dt); }
  };

  const keys = `
  ↑ / k   raise 5°        ↓ / j   lower 5°
  0-9     jump to 0°…90°  r       return to rest
  space   swing (a rate spike: fires golf above 2 rad/s, bowling above 0.8)
  q       quit
`;
  console.log(`Hand-driven IMU → ${relay}${path} as "${player}"${keys}`);

  const stdin = process.stdin;
  if (stdin.isTTY) stdin.setRawMode(true);
  stdin.resume(); stdin.setEncoding('utf8');
  let quit = false, accepting = false;
  // A chunk can carry several keys at once (a pipe, or a fast typist), and an arrow is three bytes.
  // Reading a chunk as one key turned "5" then "0" into 50, which the digit branch then multiplied.
  stdin.on('data', (chunk: string) => {
    for (let i = 0; i < chunk.length; i++) {
      const key = chunk.startsWith('\u001b[', i) ? chunk.slice(i, i + 3) : chunk[i];
      if (key.length === 3) i += 2;
      if (key === 'q' || key === '\u0003') { quit = true; return; }
      if (!accepting) continue;                       // ignore keys pressed during the rest window
      if (key === '\u001b[A' || key === 'k') commanded += 5;
      else if (key === '\u001b[B' || key === 'j') commanded -= 5;
      else if (key === 'r') commanded = 0;
      else if (key === ' ') swing = { until: Date.now() + 700, peak: 0 };
      else if (key >= '0' && key <= '9') commanded = Number(key) * 10;
      commanded = Math.max(0, Math.min(180, commanded));
    }
  });

  // Stream two still seconds at zero before accepting a key. Calibration takes the first 800ms of
  // still frames as the resting reference, so a key pressed too early anchors "rest" to a raised arm
  // and every angle afterwards is measured from the wrong place -- with calibrated: true either way.
  process.stdout.write('  holding still at 0° so calibration anchors there… ');
  await still0(2000);
  accepting = true;
  console.log('ready.\n');

  while (!quit) {
    let rateRadS: number;
    if (swing) {
      // A swing is a back-then-through sweep, so the gate sees a departure from address and a return.
      const phase = 1 - (swing.until - Date.now()) / 700;
      if (phase >= 1) { swing = null; actual = commanded; rateRadS = 0; }
      else { const back = 60 * Math.sin(Math.PI * Math.min(phase * 1.6, 1));
        const next = commanded + back;
        rateRadS = ((next - actual) * Math.PI / 180) / dt; actual = next; }
    } else {
      const step = Math.sign(commanded - actual) * Math.min(Math.abs(commanded - actual), maxDegPerSec * dt);
      rateRadS = (step * Math.PI / 180) / dt; actual += step;
    }
    const t = (actual * Math.PI) / 180 / 2;
    send([Math.sin(t), 0, 0, Math.cos(t)], [rateRadS, 0, 0], dt);
    samples++;
    if (samples % 5 === 0) {
      const filled = Math.round(Math.min(actual, 120) / 120 * 32);
      process.stdout.write(`\r  ${'█'.repeat(filled)}${'·'.repeat(32 - filled)} ${actual.toFixed(0).padStart(3)}° ` +
        `→ ${commanded.toFixed(0).padStart(3)}°  ${Math.abs(rateRadS).toFixed(1).padStart(5)} rad/s  ${samples} samples  `);
    }
    await delay(1000 * dt);
  }
  console.log('\n  stopped.');
  if (stdin.isTTY) stdin.setRawMode(false);
  stdin.pause();
} else {
  console.error('Usage:\n  node sim-motion.ts replay <recording.jsonl> [--speed N] [--loop]\n' +
    '  node sim-motion.ts reps [count] [--peak 52] [--channel club|wrist] [--short] [--fast]\n' +
    '  node sim-motion.ts hand [--channel club|wrist]');
  process.exit(2);
}

// Let the relay flush before the socket drops, or the last samples never reach a viewer.
await delay(250);
socket.close();
