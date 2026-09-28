// Start and stop the servers the live tests spawn.
//
// A server that dies before listening (usually: another test run holds its port) must fail the test at
// once. Waiting on stdout alone never resolves, so the file hung past its timeout while its other
// children kept their ports and broke the next run too. The same goes for stopping: a child that has
// already exited never emits 'exit' again, so awaiting it hung the teardown.
import type { ChildProcess } from 'node:child_process';
import { once } from 'node:events';

export async function ready(child: ChildProcess) {
  let stderr = '';
  child.stderr?.on('data', d => stderr += d);
  const died = () => Error(`${child.spawnargs.at(-1)} exited (${child.exitCode ?? child.signalCode}) before listening\n${stderr}`);
  if (child.exitCode !== null || child.signalCode !== null) throw died();
  await Promise.race([once(child.stdout!, 'data'), once(child, 'exit').then(() => { throw died(); })]);
}

export async function stop(child: ChildProcess) {
  if (child.exitCode !== null || child.signalCode !== null) return;
  const exited = once(child, 'exit');
  child.kill('SIGTERM');
  await exited;
}

/** Resolves once `done()` is true, or throws after `ms`. For waits a fixed sleep made flaky under a busy test run. */
export async function until(done: () => boolean, ms = 3000, step = 10) {
  const deadline = Date.now() + ms;
  while (!done()) {
    if (Date.now() > deadline) throw Error(`condition not met within ${ms} ms`);
    await new Promise(r => setTimeout(r, step));
  }
}
