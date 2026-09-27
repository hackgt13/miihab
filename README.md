# MiiHab

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

1. Open `Assets/Kinesthetic/Menu/MainMenu.unity` and enter Play mode. Choose **Golf** or **Movement Studio**; use the arrow keys and Enter or click a card. **Menu** or Esc returns to activity selection. An active exercise session finishes before leaving the studio.
2. Golf opens camera capture and AirPod motion automatically. Allow camera access if asked. Keep both hands, elbows, and shoulders in view; rest the club at the mat. Play begins after calibration and a short countdown.
3. Pair the AirPods to this Mac, approve Motion access, and check that fresh motion reaches Unity.
4. Hold the mounted club still at address before each turn; calibration is automatic. Then make a controlled swing.
5. Open <http://localhost:8766/portal/> for the clinician view. Historical and simulator fixtures are labeled synthetic/simulated.

The launcher uses `nohup`; closing the launching Terminal does not stop the relays. Before restarting one, identify its listener with `lsof -nP -iTCP:8766 -sTCP:LISTEN` or the equivalent for port 8767 and stop that specific process. Native Motion and camera permissions belong to each teammate's Mac.

Other scenes:

| Scene | Purpose |
| --- | --- |
| `Assets/Kinesthetic/MotionProof.unity` | Articulated pose and recorded replay |
| `Assets/Kinesthetic/Golf/AdaptiveGolf.unity` | Direct entry to adaptive golf |
| `Assets/Kinesthetic/Golf/KeyboardGolf.unity` | Golf runtime with developer keyboard input |
| `Assets/Kinesthetic/Rehab/Rehab.unity` | Movement Studio: seated shoulder raises, prescribed targets, and live rep feedback |
| `Assets/Kinesthetic/Golf/QuestGolf.unity` | Headset game-state client; the Mac remains the simulation authority |

## Quest setup on your own network

1. Connect the host Mac and Quest to the same LAN.
2. In the Unity editor on the host Mac, run **Kinesthetic → Quest → Write host config**. Inspect the generated `Assets/Kinesthetic/Golf/Resources/Golf/QuestHostConfig.asset` and ensure `host` is the Mac's address on the Quest's network; VPN/extra adapters can affect automatic selection.
3. The menu also creates `local-data/pair-token.txt`. Start or restart the golf relay using `scripts/start_demo_services.sh` after this step so it reads that token and exposes the game-state channel to the LAN. Sensor channels remain local.
4. Run **Kinesthetic → Quest → Build combined headset app**. It regenerates the headset scenes, bakes the host config, and builds one APK, `local-data/builds/MiiHabQuest.apk`, with the plaza (`QuestMenu.unity`) at index 0: the headset boots into the menu, waits for the Mac, and follows it through a door into golf, bowling, the studio, the intro or the therapist visit. The build refuses if any catalog scene has no headset copy (**Kinesthetic → Quest → Verify headset scene coverage** runs the same check). Generate the config on the host before building so the headset and relay use the same token.
5. Install it with `adb install -r`. If the headset still carries an older single-game app (`com.kinesthetic.questgolf` or `com.kinesthetic.questbowling`), uninstall it: those boot straight into their game. `scripts/demo.sh` checks for both.

The host config is a `Resources` ScriptableObject, not `StreamingAssets`: on Android StreamingAssets lives inside the compressed APK, so a file read there always fails and the headset would silently fall back to loopback. The generated asset, its Unity `.meta`, and the pairing token are local files excluded from Git. Each teammate generates their own connection settings. The editor client uses the local relay; headset builds use the generated host config.

## Included art and optional editing sources

- The complete runtime course export, four textures, export report, and original `.meta` GUIDs are included under `unity/KinestheticUnity/Assets/Kinesthetic/Art/PurchasedGolf/`. Both golf scenes reference that export.
- Runtime patient/friend Miis, wheelchair, props, and Resort driver assets are included under the Unity `Art` directory. Resort club assets are Nintendo game assets extracted/uploaded by DogToon64; the original archive README is retained under `art/wii-sports-resort/Golf Clubs/README.txt`.
- `art/props/` contains the editable props. Export helpers are in `scripts/art/`. Use Blender's `--disable-autoexec` when running those scripts with source files.
- The Movement Studio room, meshes, materials, and wood texture are included in `Assets/Kinesthetic/Rehab/Studio/`. Its reusable prefab is `RehabStudio.prefab`; **Kinesthetic → Rehab → Create shoulder raise scene** rebuilds the scene and studio assets. The UI uses [Nunito](https://github.com/googlefonts/nunito), bundled with its SIL Open Font License in `Assets/Kinesthetic/Rehab/Fonts/OFL.txt`.
- The activity menu includes an original 34-second instrumental loop and selection sounds in `Assets/Kinesthetic/Menu/Audio/`. **Music: On/Off** saves the preference locally and also controls the calm golf loop in `Assets/Kinesthetic/Golf/Resources/GolfAudio/QuietFairway.wav` (generated by `scripts/art/compose_golf_audio.py`). `scripts/art/compose_menu_audio.py` regenerates the audio with NumPy; **Kinesthetic → Menu → Create main menu** rebuilds the menu scene and navigation prefab.

Keep each asset's existing `.meta` alongside it to preserve Unity scene references. Preserve the source notes and attribution when editing or redistributing assets.

Teammates do **not** need to edit or download the original course to run the demo. If you want to change the Mii models or course in Blender, install GitHub CLI, sign in with an account that can read this private repository, and run:

```bash
gh auth login
python3 scripts/fetch_editing_sources.py
```

This downloads a pinned, checksum-verified archive from this repository's private `team-art-sources-v1` release. It includes the editable/export Mii Blender files, profiles, previews, source notes, supplied Golf Blender file/textures, and optional Wuhu OBJ/MTL/textures. The files are placed in ignored `art/mii/` and `art/course-reference/`. Wuhu is a reference asset and is not the playable course. Existing different files are preserved unless you pass `--overwrite`.

## Voice

Voice uses ElevenLabs. No local speech-model download is required.

## Verify your setup

```bash
cd coordinator
npm test
```

These checks cover pose transport, golf relay, exercise measurement, and care-plan behavior. Opening the scenes and testing actual devices remains necessary. See [native/README.md](native/README.md), [coordinator/README.md](coordinator/README.md), and [CLUB_SETUP.md](CLUB_SETUP.md).

## Current integration boundaries

The Mac prototype has implemented scenes and software checks. A combined real camera plus mounted-AirPod shot, two independently paired physical club hosts, the final native iPhone transport, and completed Quest hardware validation still need end-to-end proof. `local-data/` recordings, user history, generated logs, build output, and the archived original golf evaluation checkout are kept local. No API credential is needed for the current camera/relay/Unity demo.
