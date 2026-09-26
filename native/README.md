# Kinesthetic Club Motion

Native macOS AirPod sensor host for the adaptive golf scene. Uses the actual CMHeadphoneMotionManager acquisition pattern reviewed in `/Users/tazeemmahashin/Downloads/Aircade-main/Sources/Aircade/MotionModel.swift`, including sensorLocation, finite values and increasing hardware timestamps. It has no simulated input mode.

Build: `zsh native/build.sh` from the workspace. This Mac has stale duplicate SwiftBridging module maps; the build uses a temporary compiler VFS overlay instead of changing system files.

Run `node coordinator/golf-relay.ts`, then open `native/build/Kinesthetic Club Motion.app`. Pair supported AirPods to this Mac; the app now detects motion availability and starts streaming automatically. macOS may ask for Motion access. The first validation is whether the intended mounted earbud continues reporting when off-ear. The app shows the actual reporting Left/Right source; it cannot force macOS to select a particular bud. **Stop motion** pauses automatic detection; **Start motion** resumes it.

Unity's `AdaptiveGolf.unity` connects to this local relay on port 8767 and the existing pose relay on 8766. The capture page starts camera and Unity connection on load, and Unity assigns the active player automatically. Hold the mounted club still at address, then choose **Calibrate club** in the footer. A backswing and directional return through the reference orientation can commit one virtual shot. Readiness needs fresh camera and IMU input. Source changes, gaps and turn changes require calibration again.

This first host is local Mac only. It supports one paired AirPods motion stream at a time. A patient and friend with independent paired hosts still require the planned iPhone/LAN deployment. The relay schema and Unity player routing support their identities, but two independent physical hosts and Quest have not been demonstrated by this implementation.

The current club orientation display uses relative sensor rotation; mapping the actual mount axis to the rendered club requires a physical calibration test. Virtual shot power is a bounded mapping from angular speed, not a measurement of impact speed. No medication or rehabilitation progression decisions are made by this game.
