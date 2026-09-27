// The sensor rule (AGENTS.md, "Sensors"): one IMU takes whatever is live; two IMUs are told apart by which one moves.
import test from 'node:test';
import assert from 'node:assert/strict';
import { ImuAssigner, placeOf } from './imu-assign.ts';

const STILL = [0, 0, 0], MOVING = [1.4, 0.2, 0];

test('one IMU: whichever patient stream is live is the one measured, and it hands over until locked', () => {
  const a = new ImuAssigner({ mode: 'one', wear: { imu: 'AirPod on the wrist' } });
  assert.equal(a.state.phase, 'waiting'); assert.match(a.state.instruction, /Put the AirPod on your wrist/);
  assert.equal(a.roleOf('mac1'), null);
  // The second Mac's pair happens to be the live one: it is taken. Nothing about which Mac decided it.
  assert.equal(a.push('mac2', STILL, 1000), true);
  assert.deepEqual([a.state.phase, a.state.imu, a.roleOf('mac2'), a.roleOf('mac1')], ['ready', 'mac2', 'imu', null]);
  // A second live stream changes nothing while the first is alive.
  a.push('mac1', STILL, 1010); assert.equal(a.state.imu, 'mac2');
  // The chosen one dies before calibration: the other takes over.
  a.push('mac1', STILL, 2000);
  assert.equal(a.push('mac1', STILL, 2600), true); assert.equal(a.state.imu, 'mac1');
  // Locked at calibration: a silent stream is tracking loss, never a swap.
  a.lock();
  a.push('mac2', STILL, 4400); a.tick(4500);
  assert.deepEqual([a.state.imu, a.state.locked, a.state.live], ['mac1', true, ['mac2']]);
  assert.match(a.state.instruction, /went quiet/);
});

test('two IMUs: both must be live, then the one that moves while the other rests is the limb', () => {
  const a = new ImuAssigner({ mode: 'two', wear: { imu: 'AirPod on the wrist', ref: 'AirPod on the chest' } });
  assert.match(a.state.instruction, /one AirPod on your wrist and one on your chest/);
  a.push('mac1', STILL, 0);
  assert.equal(a.state.phase, 'waiting'); assert.match(a.state.instruction, /Connect the other/);
  a.push('mac2', STILL, 10);
  assert.equal(a.state.phase, 'identify'); assert.match(a.state.instruction, /Move the AirPod on your wrist. Keep the one on your chest still/);
  assert.equal(a.roleOf('mac1'), null, 'nothing is measured until the pairs are told apart');
  // Both moving (the patient shuffling): not an answer.
  let t = 20;
  for (let i = 0; i < 20; i++) { t += 40; a.push('mac1', MOVING, t); a.push('mac2', MOVING, t); }
  assert.equal(a.state.phase, 'identify');
  // A brief twitch is not an answer either: the window has to agree.
  a.push('mac1', STILL, t += 40); a.push('mac2', MOVING, t); a.push('mac1', STILL, t += 40); a.push('mac2', STILL, t);
  assert.equal(a.state.phase, 'identify');
  // The chest pair (here Mac 1's) is moved while the other rests: it is the limb — the catalog's mount
  // words never decided which channel is which.
  for (let i = 0; i < 20; i++) { t += 40; a.push('mac1', MOVING, t); a.push('mac2', STILL, t); }
  assert.deepEqual([a.state.phase, a.state.imu, a.state.ref], ['ready', 'mac1', 'mac2']);
  assert.deepEqual([a.roleOf('mac1'), a.roleOf('mac2')], ['imu', 'ref']);
  assert.match(a.state.instruction, /hold both still/);
  // Once assigned, the pairs may both move (a rep): the assignment is not revisited.
  for (let i = 0; i < 20; i++) { t += 40; a.push('mac1', STILL, t); a.push('mac2', MOVING, t); }
  assert.deepEqual([a.state.imu, a.state.ref], ['mac1', 'mac2']);
  a.lock(); a.tick(t + 5000);
  assert.deepEqual(a.state.live, []); assert.match(a.state.instruction, /went quiet/);
});

test('a pinned assignment skips identification (replays and development overrides)', () => {
  const a = new ImuAssigner({ mode: 'two', wear: { imu: 'AirPod on the shin', ref: 'AirPod on the thigh' }, pinned: { imu: 'mac2' } });
  assert.deepEqual([a.state.phase, a.state.imu, a.state.ref], ['ready', 'mac2', 'mac1']);
  assert.equal(a.roleOf('mac1'), 'ref');
});

test('placement words come from the catalog phrase', () => {
  assert.equal(placeOf('AirPod on the wrist'), 'wrist');
  assert.equal(placeOf('AirPods in your ears'), 'ears');
  assert.equal(placeOf('AirPod on the upper arm'), 'upper arm');
});
