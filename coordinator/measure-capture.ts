// Run the shoulder-raise engine over a saved capture (pose-capture JSON export or a coordinator session .jsonl).
// Usage: node measure-capture.ts <file> [--side right|left] [--target 80] [--prescribed 8] [--events]
import { readFile } from 'node:fs/promises';
import { ShoulderRaiseSession, type Frame } from './exercise/shoulder-raise.ts';

const args = process.argv.slice(2);
const file = args.find(a => !a.startsWith('--'));
if (!file) throw Error('Usage: node measure-capture.ts <capture.json|session.jsonl> [--side right] [--target 80] [--prescribed 8] [--events]');
const opt = (name: string) => { const i = args.indexOf('--' + name); return i >= 0 ? args[i + 1] : undefined; };

const text = await readFile(file, 'utf8');
const frames: Frame[] = file.endsWith('.jsonl')
  ? text.trim().split('\n').map(l => JSON.parse(l)).filter(e => e.type === 'pose.frame').map(e => e.payload)
  : JSON.parse(text).frames;

const session = new ShoulderRaiseSession({
  side: (opt('side') ?? 'right') as 'left' | 'right',
  targetDeg: Number(opt('target') ?? 80),
  prescribedReps: opt('prescribed') ? Number(opt('prescribed')) : undefined,
});
for (const f of frames) if (f?.imageLandmarks?.length === 33 && f.worldLandmarks?.length === 33) session.push(f);
if (args.includes('--events')) for (const e of session.events) console.log(JSON.stringify(e));
console.log(JSON.stringify(session.summary(), null, 2));
