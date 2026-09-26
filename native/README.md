# Kinesthetic motion apps

`zsh native/build.sh` builds two separate Mac apps from the shared sensor reader:

- **Kinesthetic Club Motion** sends `club.motion` on `/golf` for the golf club.
- **Kinesthetic Bowling Motion** sends `bowling.motion` on `/bowling-motion` for a wrist-mounted AirPod.

Starting either app pauses sensor capture in the other app. Each opens as a normal foreground window.
Bowling starts with `zsh scripts/start_bowling_session.sh`; it does not request the camera. Hold still facing
the pins to calibrate, then swing back and forward. Aim and power are game estimates from relative
orientation and angular speed. The native bridge has no simulated input mode.

## Golf

Native macOS AirPod sensor host for the adaptive golf scene. Uses the actual CMHeadphoneMotionManager acquisition pattern reviewed in `/Users/tazeemmahashin/Downloads/Aircade-main/Sources/Aircade/MotionModel.swift`, including sensorLocation, finite values and increasing hardware timestamps. It has no simulated input mode.

Build: `zsh native/build.sh` from the workspace on macOS 14 or newer with Xcode or Command Line Tools installed. The script discovers Swift and the selected macOS SDK through `xcrun`, targets the current Mac's architecture, and signs the local app. On CLT installations with duplicate SwiftBridging module maps, it uses a temporary compiler VFS overlay without changing installed toolchain files; that workaround needs Python 3 on PATH.

Run `node coordinator/golf-relay.ts`, then open `native/build/Kinesthetic Club Motion.app`. Pair supported AirPods to this Mac; the app now detects motion availability and starts streaming automatically. macOS may ask for Motion access. The first validation is whether the intended mounted earbud continues reporting when off-ear. The app shows the actual reporting Left/Right source; it cannot force macOS to select a particular bud. **Stop motion** pauses automatic detection; **Start motion** resumes it.

Unity's `AdaptiveGolf.unity` connects to this local relay on port 8767 and the existing pose relay on 8766. The capture page starts camera and Unity connection on load, and Unity assigns the active player automatically. Hold the mounted club still at address, then choose **Calibrate club** in the footer. A backswing and directional return through the reference orientation can commit one virtual shot. Readiness needs fresh camera and IMU input. Source changes, gaps and turn changes require calibration again.

This first host is local Mac only. It supports one paired AirPods motion stream at a time. A patient and friend with independent paired hosts still require the planned iPhone/LAN deployment. The relay schema and Unity player routing support their identities, but two independent physical hosts and Quest have not been demonstrated by this implementation.

The current club orientation display uses relative sensor rotation; mapping the actual mount axis to the rendered club requires a physical calibration test. Virtual shot power is a bounded mapping from angular speed, not a measurement of impact speed. No medication or rehabilitation progression decisions are made by this game.
