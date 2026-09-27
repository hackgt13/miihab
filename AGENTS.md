# Agent notes — RehabMii

## Quest is the target, not the Mac

"Works on the Mac" is unfinished. Play mode and Quest Link are not proof; only a device build is. Hardware validation remains unproven end to end.

- **Mac is authority; Quest only renders.** Sensors (camera, AirPods IMU, mic) are Mac-attached. `coordinator/server.ts` (8766) + `golf-relay.ts` (8767) run there as separate Node processes; Unity is a client of both, never a host. The headset gets resolved `golf.state`, never raw sensor data.
- **AirPods IMU is Mac-only.** `CMHeadphoneMotionManager` (`native/ClubMotionBridge.swift`, ~25 Hz), unreachable from Horizon OS. Chain: AirPods → Mac → relay → Unity (Mac) → `golf.state` → Quest.
- **Scene index 0 is what a build boots, and it is always a menu.** `MainMenu.unity` on the Mac (**Kinesthetic → Menu → Configure Mac build scenes** repairs the list), `QuestMenu.unity` (the plaza) on the headset. **Kinesthetic → Quest → Build combined headset app** is the only headset build: one APK, `com.kinesthetic.rehabmii`, plaza first, every activity behind a door. The golf-only and bowling-only APKs are gone; if a headset still has `com.kinesthetic.questgolf` or `questbowling`, uninstall it (`scripts/demo.sh` flags it).
- **Perf is a separate budget.** Quest: 2 eyes, 72–90 Hz, mobile GPU, Vulkan. Mac: 1 view, Metal. Measure on device first. Foveated rendering, SpaceWarp, dynamic resolution all off — first knobs to reach for.

### Android traps

Dormant only because `QuestSceneSetup` sets `game.enabled = false`; all return together once code runs on-headset.

- `streamingAssetsPath` is a `jar:` URL inside the APK — `File.Exists`/`ReadAllText` always fail. Use a `Resources` ScriptableObject (`QuestHostConfig`) or `UnityWebRequest`.
- `Microphone` needs manifest `RECORD_AUDIO` + runtime `Permission.RequestUserPermission`. No custom AndroidManifest yet.
- `UnityWebRequest` to `127.0.0.1` hits the *headset*, not the Mac. Plain HTTP may need `usesCleartextTraffic`.
- IL2CPP strips code: reflection-based JSON works in-editor, returns null on device. No `link.xml` — suspect first if the headset connects but renders nothing.
- Screen-space UI Toolkit (`m_RenderMode: 0`) does not render in stereo. No controller or gaze input — keyboard and pointer only.

## Sensors

Three sources, and only three: the patient's two AirPod pairs — **Mac 1** (the relay Mac's own) and **Mac 2** (the second Mac's, over Tailscale with the pairing token; one Mac reads one `CMHeadphoneMotionManager` stream) — and the Quest, always on the head. Both pairs are the patient's, everywhere. Which Motion app or relay path a pair arrives on is transport; the relay files it as Mac 1 (loopback) or Mac 2 (paired), and nothing downstream sees an app, a path or a mount.

- **Games read one stream.** `/golf` and `/bowling-motion` viewers get the relay's fused `patient` stream: whichever pair is more active (`coordinator/motion-fuse.ts`), re-based at a switch so orientation never jumps. Golf: both pairs held together in the grip, the bigger swing wins. Bowling: one pair on the throwing wrist, the resting one loses. No game knows there are two.
- **The studio reads both, raw.** The coordinator reads `/motion` (every sample tagged `mac1`/`mac2`). One-IMU movements take whatever is live. Two-IMU movements tell the pairs apart once by which one moves while the other rests (`coordinator/exercise/imu-assign.ts`), lock at calibration, and the coordinator **remembers** the answer for the next set worn the same way — one and done, never a prompt per set.
- **No per-person pairs.** A second Mac is never a friend. `KINESTHETIC_REMOTE_MOTION=people` restores the older reading (second Mac = the other person in golf and groups, two-IMU movements refused there with 409) and exists only as a fallback; `=tagged` takes each app's picker at its word.
- **The headset is the head.** Head pose (`/head`) leans the torso and is the trunk-lean sensor going forward; an AirPod is never assigned to the head.
- **Connection is one and done.** `zsh scripts/start_tunnel.sh receive --watch` on Mac 1 (Tailscale up, relay on the tailnet with the persisted token in `local-data/pair-token.txt`, Quest over its cable). Mac 2 runs the zipped connector once; its Motion app keeps the address and token and reconnects on its own from then on. Tailscale addresses are stable, so nothing is re-entered.
- **Retired:** the `club`/`wrist` channel names in the coordinator, `KINESTHETIC_MOTION_URL`/`KINESTHETIC_WRIST_MOTION_URL` (now `KINESTHETIC_RAW_MOTION_URL`), `params.imuSource` and `SOURCE_OF`. `exercise.started` and `GET /api/sensors` carry `sensors: {mode, phase, imu, ref, live, locked, instruction}` with `imu`/`ref` as `mac1`/`mac2`; a UI shows `instruction` verbatim during `waiting` and `identify`.
- **Pinned by tests:** `motion-fuse.test.ts`, `exercise/imu-assign.test.ts`, the server tests in `exercise/two-imu.test.ts` and `exercise/imu-session.test.ts`, the relay tests in `golf-relay.test.ts` and `bowling-relay.test.ts`. Change the rule there first, then the code.

## Unity setup

- **6000.6.2f1**, changeset **770e33f6875c**, arm64 — both pinned in tracked `ProjectSettings/ProjectVersion.txt`. **Never accept a Hub upgrade prompt**: it rewrites that file and migrates assets one-way.
- **Install via the Hub GUI** → `Editor/6000.6.2f1-arm64`. `--headless install` creates `Editor/6000.6.2f1`, which the GUI ignores → two 10 GB copies. The bound one is in `UnityHub/projects-v1.json` (`eds:`).
- **Android Build Support + SDK/NDK/OpenJDK is required even for Mac-only work.** `Assets/Editor/AndroidCMakePin.cs` needs `UnityEditor.Android`; without it `CS0103` fails the whole editor assembly and every `Kinesthetic/*` menu silently disappears.
- Disk: ~10 GB editor + ~10 GB Android + ~2 GB `Library/`.
- Live log is `unity/KinestheticUnity/Logs/Editor.log`, not `~/Library/Logs/Unity/`. Grep `error CS`; bare `error`/`Exception` match package filenames.
- `Library/` is locked while the editor is open, so batchmode cannot run then.
- Batchmode: `-batchmode -quit -disable-assembly-updater -projectPath <p> -logFile <f>` (the flag stops the API updater rewriting source). Undo two side effects after: `LastSceneManagerSetup.txt` becomes `sceneSetups: []` (editor opens sceneless — reopen one), and `UniversalRenderPipelineGlobalSettings.asset` loses `m_RuntimeSettings` (`git checkout --` it).

## Repo

- **Scene wiring lives in `Assets/Editor/*SceneSetup.cs`, never the Inspector** — scenes are code-generated; hand-wiring is destroyed by the next `Create()`.
- **Never delete a `.meta`** — references are by GUID, not path.
- **Colour comes from the palette, never a literal** — `var(--token)` in USS, `Palette.*` in C#. `Assets/Kinesthetic/Palette.uss` and `Palette.cs` are two halves of one theme and move together. The five hues carry meaning and never swap jobs: cerulean is primary and measured, jungle is good, coral is target or trouble. No sixth hue — if nothing fits, take another step of the family whose meaning matches. `grep -rnE '#[0-9A-Fa-f]{6}' Assets/Kinesthetic --include='*.uss'` should only ever hit `Palette.uss`.
- **UI is a component and a variant, never a restyle** — buttons, labels, tags, chips, steps, readouts, rings and meters come from `Assets/Kinesthetic/UI/` (`<k:KButton tone="Primary" size="Large" />`). A screen chooses a component and a variant; its own USS sets layout only, never paint, and never selects a `.k-` class. Need a look that no variant gives? Add the variant in `UI/`, once. Components take colour only from the palette. `python3 scripts/check_ui.py` must pass. See `UI.md`.
- Prefer a real check: `Assets/Editor/` verification scripts and `scripts/unity_mcp.py` drive the running editor.
- `cd coordinator && npm test` — pose transport, golf relay, measurement, care plans.

## Git

- Commit and merge to main. ALWAYS flag conflicts first.
- DO NOT, under ANY CIRCUMSTANCES, add CLAUDE as an AUTHOR TO THE COMMITS
- USE SEMANTIC COMMITS
