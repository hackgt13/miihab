# RehabMii

Kinesthetic's rehabilitation and adaptive-golf prototype: browser camera pose capture, a local Node relay, Unity patient/friend avatars, a Mac AirPod motion bridge, shoulder-exercise measurement, and a clinician plan-review portal.

## Prerequisites

- **Unity 6000.6.2f1** through Unity Hub. For Quest work, also install Android Build Support, Android SDK/NDK Tools, and OpenJDK for this editor version.
- **Node.js 24 or newer** and npm. The coordinator executes TypeScript directly with Node.
- **macOS 14 or newer** for the AirPod bridge; install Xcode or Command Line Tools (`xcode-select --install`). The build uses the selected SDK and the Mac's architecture. Python 3 is needed only for the duplicate-Swift-header workaround on affected CLT installations.
- A camera and supported paired AirPods for live sensor input. The keyboard golf scene can exercise the game without the AirPod bridge.

## First checkout

```bash
git clone git@github.com:hackgt13/rehabmii.git
cd rehabmii
cd coordinator
npm ci
cd ..
zsh native/build.sh
```

In Unity Hub, add `unity/KinestheticUnity` and open it with the editor version above. Allow Unity to restore the pinned packages and import the assets. Node dependencies, Unity `Library`, and the compiled native app are regenerated locally.

## Run the Mac demo

```bash
zsh scripts/start_demo_services.sh
```

This starts the pose/clinical coordinator on **8766**, the golf relay on **8767**, and the compiled AirPod app if available. Logs go to `local-data/logs/`.

1. Open `Assets/Kinesthetic/Golf/AdaptiveGolf.unity` and enter Play mode.
2. Open <http://localhost:8766/> for camera capture, or use the scene's **Open camera** button. Approve camera access and keep the active player's shoulders, elbows, wrists, and hips visible.
3. Pair the AirPods to this Mac, approve Motion access, and check that fresh motion reaches Unity.
4. Hold the mounted club still at address and use **Calibrate club** before a controlled swing. Turn/source changes require fresh calibration.
5. Open <http://localhost:8766/portal/> for the clinician view. Historical and simulator fixtures are labeled synthetic/simulated.

The launcher uses `nohup`; closing the launching Terminal does not stop the relays. Before restarting one, identify its listener with `lsof -nP -iTCP:8766 -sTCP:LISTEN` or the equivalent for port 8767 and stop that specific process. Native Motion and camera permissions belong to each teammate's Mac.

Other scenes:

| Scene | Purpose |
| --- | --- |
| `Assets/Kinesthetic/MotionProof.unity` | Articulated pose and recorded replay |
| `Assets/Kinesthetic/Golf/KeyboardGolf.unity` | Golf runtime with developer keyboard input |
| `Assets/Kinesthetic/Rehab/Rehab.unity` | Shoulder-exercise session and active care plan |
| `Assets/Kinesthetic/Golf/QuestGolf.unity` | Headset game-state client; the Mac remains the simulation authority |

## Quest setup on your own network

1. Connect the host Mac and Quest to the same LAN.
2. In the Unity editor on the host Mac, run **Kinesthetic → Quest → Write host config**. Inspect the generated `Assets/StreamingAssets/quest-host.json` and ensure `host` is the Mac's address on the Quest's network; VPN/extra adapters can affect automatic selection.
3. The menu also creates `local-data/pair-token.txt`. Start or restart the golf relay using `scripts/start_demo_services.sh` after this step so it reads that token and exposes the game-state channel to the LAN. Sensor channels remain local.
4. Build the Quest scene for Android/ARM64 using OpenXR. Generate the config on the host before building so the headset and relay use the same token.

`quest-host.example.json` documents the format. The real host config, its Unity `.meta`, and the pairing token are local generated files excluded from Git. Each teammate generates their own connection settings. The editor client uses the local relay; headset builds use the generated host config.

## Included art and optional editing sources

- The complete runtime course export, four textures, export report, and original `.meta` GUIDs are included under `unity/KinestheticUnity/Assets/Kinesthetic/Art/PurchasedGolf/`. Both golf scenes reference that export.
- Runtime patient/friend Miis, wheelchair, props, and Resort driver assets are included under the Unity `Art` directory. Resort club assets are Nintendo game assets extracted/uploaded by DogToon64; the original archive README is retained under `art/wii-sports-resort/Golf Clubs/README.txt`.
- `art/props/` contains the editable props. Export helpers are in `scripts/art/`. Use Blender's `--disable-autoexec` when running those scripts with source files.

Keep each asset's existing `.meta` alongside it to preserve Unity scene references. Preserve the source notes and attribution when editing or redistributing assets.

Teammates do **not** need to edit or download the original course to run the demo. If you want to change the Mii models or course in Blender, install GitHub CLI, sign in with an account that can read this private repository, and run:

```bash
gh auth login
python3 scripts/fetch_editing_sources.py
```

This downloads a pinned, checksum-verified archive from this repository's private `team-art-sources-v1` release. It includes the editable/export Mii Blender files, profiles, previews, source notes, supplied Golf Blender file/textures, and optional Wuhu OBJ/MTL/textures. The files are placed in ignored `art/mii/` and `art/course-reference/`. Wuhu is a reference asset and is not the playable course. Existing different files are preserved unless you pass `--overwrite`.

## Optional voice environment

The current demo does not require the local voice weights. For voice experiments on Apple Silicon, install Python 3.12 and `uv`, then:

```bash
cd coach
uv sync --frozen
cd ..
python3 scripts/fetch_voice_models.py
```

The downloader retrieves `kokoro-v1.0.onnx` and `voices-v1.0.bin` from the pinned upstream `model-files-v1.0` release, checks SHA-256, and stores them in ignored `coach/models/`. See [coach/README.md](coach/README.md). Voice samples are committed; a finished voice coach/evidence agent is still integration work.

## Verify your setup

```bash
cd coordinator
npm test
```

These checks cover pose transport, golf relay, exercise measurement, and care-plan behavior. Opening the scenes and testing actual devices remains necessary. See [native/README.md](native/README.md), [coordinator/README.md](coordinator/README.md), and [CLUB_SETUP.md](CLUB_SETUP.md).

## Current integration boundaries

The Mac prototype has implemented scenes and software checks. A combined real camera plus mounted-AirPod shot, two independently paired physical club hosts, the final native iPhone transport, and completed Quest hardware validation still need end-to-end proof. `local-data/` recordings, user history, generated logs, build output, and the archived original golf evaluation checkout are kept local. No API credential is needed for the current camera/relay/Unity demo.
