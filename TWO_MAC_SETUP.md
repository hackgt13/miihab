# Two Macs, two AirPod pairs

One Mac can read one AirPods motion stream, so a two-AirPod movement (arm raise, biceps curl, squat) needs a
second Mac streaming its pair to the first Mac's relay. Venue Wi-Fi blocks laptops from talking to each other, so
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
