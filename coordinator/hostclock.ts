// One time base for every channel, across both relay processes.
//
// activity-plan.md: "IMU owns how high. Camera owns which direction, and did you cheat.
// No fusion math beyond a shared clock." This is that shared clock.
//
// Before it, three timebases met only inside Unity: the pose bridge stamped with performance.now(),
// the golf relay with Date.now() plus CoreMotion sensorTime, and the client fused them using
// Stopwatch ticks at socket-read time. VirtualClubStrike's +/-120 ms window and ContactCueGate's
// 250 ms window were absorbing that skew, and nothing recorded could be re-scored offline because
// no two channels shared an axis.
//
// How it is comparable across processes: each process anchors once to the system wall clock at
// start, then advances on its own monotonic counter. Both anchors read the same system clock, so
// they agree to well under a millisecond on one machine, and later NTP steps cannot move a stamp
// afterwards because only the monotonic counter advances it.

const EPOCH_WALL_MS = Date.now();
const EPOCH_MONOTONIC = process.hrtime.bigint();

/** The wall-clock instant this process's monotonic axis is anchored to. */
export const HOST_EPOCH_UTC = new Date(EPOCH_WALL_MS).toISOString();

/**
 * Milliseconds on the shared host axis. Comparable between the pose bridge and the golf relay, and
 * monotonic within a process. Fractional: sub-millisecond ordering survives.
 */
export function hostMonotonicMs(): number {
  return EPOCH_WALL_MS + Number(process.hrtime.bigint() - EPOCH_MONOTONIC) / 1e6;
}

/** Stamp any inbound sample as it arrives. The stamp is the only cross-channel ordering key. */
export function stamped<T extends object>(sample: T): T & { hostMonotonicMs: number } {
  return { ...sample, hostMonotonicMs: hostMonotonicMs() };
}

export interface HostStamped { hostMonotonicMs: number }

/**
 * Interleave any number of separately recorded channels onto the shared axis.
 *
 * The two relays are separate processes, so they cannot safely append to one file; they each write
 * their own log and a reader merges them. That is equivalent for re-scoring and avoids cross-process
 * file contention. Lines without a stamp are dropped rather than guessed at — a sample whose time we
 * cannot place is worse than a missing one.
 */
export function interleave<T extends HostStamped>(...channels: T[][]): T[] {
  return channels.flat()
    .filter(s => s && Number.isFinite(s.hostMonotonicMs))
    .sort((a, b) => a.hostMonotonicMs - b.hostMonotonicMs);
}

/** Parse a recorded jsonl channel, skipping blank and malformed lines. */
export function parseChannel<T extends HostStamped>(text: string): T[] {
  const out: T[] = [];
  for (const line of text.split('\n')) {
    const trimmed = line.trim();
    if (!trimmed) continue;
    try { out.push(JSON.parse(trimmed) as T); } catch { /* a torn final line is expected mid-recording */ }
  }
  return out;
}
