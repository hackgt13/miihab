import type { Frame, Point } from './measurement.ts';

// Synthetic seated body in MediaPipe world coordinates (metres, y down, hip-centred).
// armDeg: right upper arm elevation from hanging down (frontal plane). leanDeg: trunk lean sideways.
export function frame(tMs: number, armDeg: number, leanDeg = 0, opts: { hideElbow?: boolean; jitter?: number } = {}): Frame {
  const rad = (d: number) => d * Math.PI / 180, j = () => (Math.random() - .5) * (opts.jitter ?? 0);
  const world: Point[] = Array.from({ length: 33 }, () => ({ x: 0, y: 0, z: 0, visibility: .99 }));
  const image: Point[] = Array.from({ length: 33 }, () => ({ x: .5, y: .5, z: 0, visibility: .99 }));
  const lean = rad(leanDeg), up: [number, number] = [Math.sin(lean), -Math.cos(lean)];   // torso "up" direction (x,y)
  const across: [number, number] = [Math.cos(lean), Math.sin(lean)];
  const shoulderMid = { x: up[0] * .5, y: up[1] * .5 };
  const set = (i: number, x: number, y: number, z = 0) => { world[i] = { x: x + j(), y: y + j(), z: z + j(), visibility: .99 }; };
  set(23, .1, 0); set(24, -.1, 0);
  set(11, shoulderMid.x + across[0] * .18, shoulderMid.y + across[1] * .18);
  set(12, shoulderMid.x - across[0] * .18, shoulderMid.y - across[1] * .18);
  const a = rad(armDeg);   // rotate "down" (opposite of torso up in world frame, unaffected by lean) outward
  set(14, world[12].x - Math.sin(a) * .28, world[12].y + Math.cos(a) * .28);
  set(13, world[11].x, world[11].y + .28);
  // Forearms continue the upper arm (straight-arm raise); left arm rests.
  set(16, world[14].x - Math.sin(a) * .25, world[14].y + Math.cos(a) * .25);
  set(15, world[13].x, world[13].y + .25);
  // Head, hands and seated legs so avatar retargeting has a whole body (measurement ignores these).
  set(0, shoulderMid.x + up[0] * .22, shoulderMid.y + up[1] * .22, -.05);
  for (const [i, dx] of [[2, .03], [5, -.03], [7, .07], [8, -.07]] as const) set(i, shoulderMid.x + up[0] * .25 + dx, shoulderMid.y + up[1] * .25, -.03);
  for (const [i, hand] of [[17, 15], [19, 15], [21, 15], [18, 16], [20, 16], [22, 16]] as const)
    set(i, world[hand].x + (world[hand].x - world[hand === 15 ? 13 : 14].x) * .3, world[hand].y + (world[hand].y - world[hand === 15 ? 13 : 14].y) * .3);
  set(25, .1, 0, -.4); set(26, -.1, 0, -.4); set(27, .1, .42, -.42); set(28, -.1, .42, -.42);
  set(29, .1, .45, -.38); set(30, -.1, .45, -.38); set(31, .1, .46, -.5); set(32, -.1, .46, -.5);
  // Image coordinates: a plausible front view derived from world x/y so visibility/bounds checks see a real layout.
  for (let i = 0; i < 33; i++) image[i] = { x: .5 - world[i].x * .6, y: .45 + world[i].y * .6, z: world[i].z, visibility: .99 };
  if (opts.hideElbow) image[14] = { ...image[14], visibility: .1 };
  return { sourceMediaTimeMs: tMs, imageLandmarks: image, worldLandmarks: world };
}

