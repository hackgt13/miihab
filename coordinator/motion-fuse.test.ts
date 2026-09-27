// The games follow whichever AirPod pair is moving, and never see a jump when the pick changes.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DominantMotion } from './motion-fuse.ts';

const still = { quaternion: [0, 0, 0, 1], rotationRate: [0.02, 0, 0], sourceId: 'Right' };
const moving = (radS: number, q = [0, 0, 0, 1]) => ({ quaternion: q, rotationRate: [0, 0, radS], sourceId: 'Right' });
// A quarter turn about z, as another pair's frame would report the same pose.
const quarter = [0, 0, Math.SQRT1_2, Math.SQRT1_2];

test('bowling: the throwing wrist takes the stream from the resting pair', () => {
  const fuse = new DominantMotion();
  let t = 0;
  for (; t < 500; t += 40) { fuse.push('mac1', still, t); fuse.push('mac2', still, t); }
  assert.equal(fuse.picked, 'mac1');                     // first to speak, nothing to choose between
  let out = null;
  for (; t < 900; t += 40) { fuse.push('mac1', still, t); out = fuse.push('mac2', moving(8), t) ?? out; }
  assert.equal(fuse.picked, 'mac2');
  assert.equal(out?.mac, 'mac2');
});

test('golf: two pairs held together do not flicker, and the stronger reading wins', () => {
  const fuse = new DominantMotion();
  let switches = 0;
  for (let t = 0; t < 2000; t += 40) {
    const a = fuse.push('mac1', moving(10 + Math.sin(t)), t), b = fuse.push('mac2', moving(10.5 + Math.cos(t)), t);
    switches += Number(!!a?.switched) + Number(!!b?.switched);
  }
  assert.equal(switches, 1);                              // the first pick, and no bouncing after it
  const fuse2 = new DominantMotion();
  for (let t = 0; t < 1000; t += 40) { fuse2.push('mac1', moving(4), t); fuse2.push('mac2', moving(12), t); }
  assert.equal(fuse2.picked, 'mac2');
});

test('a switch carries orientation on instead of jumping to the other frame', () => {
  const fuse = new DominantMotion();
  let t = 0, last: number[] = [];
  for (; t < 400; t += 40) { last = fuse.push('mac1', moving(3), t)!.quaternion; fuse.push('mac2', { ...still, quaternion: quarter }, t); }
  let first = null;
  for (; t < 1200 && !first; t += 40) { fuse.push('mac1', still, t); first = fuse.push('mac2', moving(9, quarter), t); }
  assert.ok(first?.switched);
  first!.quaternion.forEach((v, i) => assert.ok(Math.abs(v - last[i]) < 1e-9));
});

test('a pair that goes quiet or leaves hands over at once', () => {
  const fuse = new DominantMotion();
  for (let t = 0; t < 400; t += 40) fuse.push('mac1', moving(5), t);
  fuse.drop('mac1');
  assert.equal(fuse.push('mac2', still, 440)?.mac, 'mac2');
  const stale = new DominantMotion();
  stale.push('mac1', moving(5), 0);
  assert.equal(stale.push('mac2', still, 2000)?.mac, 'mac2'); // mac1 silent for 2 s
});
