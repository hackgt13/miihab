# Kinesthetic Unity runtime

Unity 6000.6.2f1 / URP. Unity is the authoritative 3D runtime for the eventual rehabilitation and shared golf experience. The runtime includes pose tracking/replay, adaptive golf, shoulder exercises, and a Quest game-state client.

See [team setup](../README.md), [native motion setup](../native/README.md), and [physical club setup](../CLUB_SETUP.md).

## Reuse and new integration code

- **Pose inference:** actual Wheelgentic browser initialization/inference, in `../spikes/pose-capture/`. Unity receives observations; it does not reimplement MediaPipe.
- **Retargeting:** `PoseRig.cs` ports the finite-direction/aim approach from Wheelgentic `web/avatar.js` (around line 599) to Unity's `Quaternion.FromToRotation`. It drives separate upper-arm and forearm joints from the existing model's real child axes. Unity transport, timestamped replay, validity gates, and UI are new integration code.
- **Articulated mesh:** the supplied Mii Maker 4.1 character, customized with hair 23, head 2, eyes 39, mouth 5, and no glasses. The export preserves the original body weights and 22 anatomical/control bones; facial sprites are baked into small textures. [Optional editing sources](../README.md#included-art-and-optional-editing-sources). CesiumMan remains as the earlier integration asset with its [attribution](../third_party/CesiumMan/README.upstream.md) and [license](../third_party/CesiumMan/LICENSE.md).
- **Import/rendering:** glTFast 6.20.0, URP template packages pinned in the Unity package lock. Unity Pipeline 0.7.0-exp.1 provides the running editor's MCP control surface.
- **Blender props:** black and steel wheelchair, 10 lb dumbbell, and golf driver, with editable source and export notes in `../art/props/README.md`. The wheelchair is parented to the same actor root as the Mii, and the motion proof defaults to a three-quarter view that shows the seated pose and footrests.

The original Wheelgentic snapshot has no explicit application-code license. Preserve attribution and resolve redistribution terms before publishing; the local evaluation is user-authorized. The test avatar is a reusable integration asset, not final product art or a model of the patient's body shape.

## Measurement boundary

Raw MediaPipe coordinates feed angle arithmetic before any avatar scaling. Image bounds, visibility, finite world coordinates, and nonzero segments gate measurements. The initial Unity mapping is `(x, -y, z)` and still needs a human side/depth check. Untracked arms return to a reference pose; unseen legs use an authored seated pose. No smoothing, rig scale, exercise assistance, or model inference is allowed to fabricate clinical progress.

`LivePoseClient` receives on a background task and keeps one latest packet. Unity applies it on the main thread. Frames older than 250 ms become unavailable. Background execution stays enabled so the scene continues while the capture browser has focus.

`PoseReplay` reads original capture JSON or coordinator JSONL and exposes replay controls. `KinestheticSceneSetup.VerifyFile` tests real capture articulation/replay; `VerifyGeometry` checks known geometry and invalid tracking behavior through MCP.
