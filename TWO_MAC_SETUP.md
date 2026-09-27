# Two Macs, two AirPod pairs

One Mac can read one AirPods motion stream, so a two-AirPod movement (biceps curl, squat) needs a second Mac
streaming its pair to the first Mac's relay. The arm raise is the one-AirPod movement and needs only Mac 1. Venue Wi-Fi blocks laptops from talking to each other, so
the two Macs meet over a Tailscale tunnel, and the Quest reaches the first Mac over its USB cable. No venue network
is in the path.

- **Mac 1 (receiver)** runs Unity, the coordinator, the relay and Club Motion for its own AirPods.
- **Mac 2 (sender)** runs a motion app (Club Motion or Bowling Motion, either works) for its AirPods and sends to Mac 1.
- Which pair measures the limb is decided per set by moving it, never by which Mac it is on (AGENTS.md, Sensors).

## Before you start

1. **Tailscale on both Macs, same account.** Install from https://tailscale.com/download/mac and sign in with the
   same login on each. Mac 1 is currently signed in as `8pxl@github`.
2. **Motion-capable AirPods on each Mac.** AirPods Pro, AirPods 3rd/4th generation or AirPods Max. Original and
   2nd-generation AirPods have no motion sensor.
3. **For each pair, in that Mac's Bluetooth settings:** turn off Automatic Ear Detection, and set Connect to This
   Mac to "When Last Connected to This Mac". Without this the stream stops the moment the AirPods leave the ear,
   and an iPhone on the same Apple ID will steal them.

## The motion apps are built from source

Both apps, **Kinesthetic Club Motion** and **Kinesthetic Bowling Motion**, compile from one file:
`native/ClubMotionBridge.swift`. A running app is whatever was last built, so **after any change to that file,
rebuild and reopen the apps, or the change is not running.** Nothing warns you: an old build simply leaves new
fields out, and the coordinator quietly falls back.

- `receive` (below) rebuilds **Bowling Motion** for Mac 2 every time, but only **opens** Club Motion as it was last
  built. Rebuild Club Motion yourself: `zsh native/build.sh club`, then quit and reopen it.
- `zsh native/build.sh bowling` rebuilds only Bowling Motion; `zsh native/build.sh` rebuilds both. Building one
  leaves a running copy of the other untouched.
- Mac 2 runs the zipped Bowling Motion from Mac 1. After a rebuild, send the new zip (AirDrop
  `native/build/Kinesthetic Bowling Motion.zip`) and reopen it there.

**Check what the running apps actually send** (on Mac 1, with the relay up):

```
cd coordinator && node -e "import('ws').then(({WebSocket})=>{const w=new WebSocket('ws://127.0.0.1:8767/motion?role=viewer');const seen={};w.on('message',b=>{const m=JSON.parse(b);if(m.type==='motion.sample')seen[m.mac]='userAcceleration' in m});setTimeout(()=>{console.log(seen);process.exit()},3000)})"
```

It prints each live pair (`mac1`, `mac2`) and whether its samples carry acceleration. `false` for a pair means
that Mac's app is an old build.

## Biceps curl: where the AirPods go, and measuring the arm

The curl is two-AirPod, so it needs both Macs and is solo only. Strap each AirPod where bone is close to the skin,
snug so it cannot slide; which way it faces does not matter.

- **Forearm (Mac 1 or Mac 2, either):** back of the forearm, **a hand-width above the wrist crease**. On the hand
  or right at the wrist, bending the wrist adds tilt the elbow did not make.
- **Upper arm:** **outside of the upper arm, a hand-width above the elbow**. Not on the front, where the biceps
  bulges and shifts it, and not high on the shoulder muscle.
- Put the other bud of each pair back in its case, so the strapped one is the bud that reports. Keep the palm
  facing up the whole rep: turning the forearm mid-rep reads partly as tilt.

**Measuring the arm (before the first rep).** The studio's curl panel (where the mirror usually stands) says
*MEASURE YOUR ARM*: keep the arm straight, raise it to the ceiling, then lower it all the way. From that swing each
AirPod's acceleration against its rotation gives its distance from the shoulder (`coordinator/exercise/arm-length.ts`),
and with both AirPods a hand-width above their joints, the upper arm and forearm lengths follow. The panel then shows
*Arm measured: upper arm … cm, forearm … cm* until the first rep.

It needs **acceleration from both apps**, which only builds made from 2026-09-27 on send (see the check above). Without
it the step is skipped within a second and an average arm is used; the panel says so under *Curl when you're ready*.
A swing that does not fit a straight arm, lengths that are not an arm, or 25 seconds without a swing also fall back
to the average arm, and the panel says which.

## Mac 1: receive

```
zsh scripts/start_tunnel.sh receive --watch
```

This does everything on Mac 1:

- brings the Tailscale tunnel up and prints Mac 1's tailnet address
- starts the coordinator and the relay, bound to the network with the pairing token
- opens Club Motion for Mac 1's AirPods
- builds Bowling Motion and zips it with a double-clickable connector for Mac 2
- wires the Quest over USB as soon as it is plugged in and authorized
- prints the exact command for Mac 2
- with `--watch`, stays up: re-ups the tunnel if it drops, re-wires a replugged Quest, and prints a line whenever
  Mac 1 or Mac 2 goes live or quiet. Ctrl-C to stop.

Leave it running for the session.

## Mac 2: send

The `receive` output ends with the command for Mac 2. It has this shape, with Mac 1's tailnet address and token:

```
zsh scripts/start_tunnel.sh send <mac1-tailnet-ip> <token>
```

For example, today:

```
zsh scripts/start_tunnel.sh send 100.95.181.27 fa29d58133e44aa8ab719db5fc8fa6eb
```

This brings the tunnel up on Mac 2, waits until Mac 1's relay is reachable, points whichever motion app is present
at Mac 1, opens it, and waits until Mac 2's motion is seen by Mac 1. Then pair Mac 2's AirPods and allow Motion
access when the app asks.

**Mac 2 without the repo:** AirDrop `native/build/Kinesthetic Bowling Motion.zip` from Mac 1. Unzip it and
double-click `Connect to <Mac 1>.command`. It does the same as `send`. If macOS refuses to open it, right-click it
and choose Open once.

The token is in `local-data/pair-token.txt` on Mac 1. The tailnet address is stable across Wi-Fi changes, but it
changes if Mac 1 is re-added to the tailnet; `receive` always prints the current one.

## Quest

Plug the Quest into Mac 1 over USB and accept the USB debugging prompt in the headset. `receive --watch` reverses
the relay, coordinator and voice ports to it, and the headset app tries its own loopback first, which reaches Mac 1
over the cable. The Quest does not need Wi-Fi for this.

## Is it working?

On Mac 1:

```
zsh scripts/start_tunnel.sh status
```

shows the tunnel state and peers, whether the Quest is wired, and every live pair with its age. The relay's own view:

```
curl http://127.0.0.1:8767/
```

What the studio is doing with the pairs during a set:

```
curl http://127.0.0.1:8766/api/sensors
```

## When it does not work

- **`send` sits on "waiting for this pair to reach the receiver".** The tunnel is fine; the app on Mac 2 has no
  AirPods motion, so it never connects. Read the app's status line on Mac 2: it says whether it is waiting for
  paired AirPods, missing Motion access (System Settings, Privacy and Security, Motion and Fitness), or reporting a bud.
- **A pair shows connected in Bluetooth but the relay sees nothing.** The AirPods have left the audio device list.
  Put them on for a second, or disconnect and reconnect them in the Bluetooth menu, and check Automatic Ear
  Detection is off. The apps retry every few seconds on their own.
- **The studio says "Hold still to calibrate" forever.** The coordinator was not restarted after a code change.
  `zsh scripts/start_demo_services.sh` restarts what is down; to force it, kill the node processes on 8766 and 8767
  and run it again.
- **Both Macs on a phone hotspot instead** also works and needs no Tailscale, but the Quest must then join the
  hotspot too, and the address changes between joins. `zsh scripts/start_two_airpods.sh receive` prints it.
