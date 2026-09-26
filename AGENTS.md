# Agent notes — RehabMii

## Quest is the target, not the Mac

"Works on the Mac" is unfinished. Play mode and Quest Link are not proof; only a device build is. Hardware validation remains unproven end to end.

- **Mac is authority; Quest only renders.** Sensors (camera, AirPods IMU, mic) are Mac-attached. `coordinator/server.ts` (8766) + `golf-relay.ts` (8767) run there as separate Node processes; Unity is a client of both, never a host. The headset gets resolved `golf.state`, never raw sensor data.
- **AirPods IMU is Mac-only.** `CMHeadphoneMotionManager` (`native/ClubMotionBridge.swift`, ~25 Hz), unreachable from Horizon OS. Chain: AirPods → Mac → relay → Unity (Mac) → `golf.state` → Quest.
- **Scene index 0 is what a build boots, and it is always a menu.** `MainMenu.unity` on the Mac, `QuestMenu.unity` (the plaza) on the headset; an activity scene is never the entry. The combined headset build orders its own list. For a manual Quest build, **Kinesthetic → Quest → Boot headset scene** first and **Boot menu scene** after.
- **Perf is a separate budget.** Quest: 2 eyes, 72–90 Hz, mobile GPU, Vulkan. Mac: 1 view, Metal. Measure on device first. Foveated rendering, SpaceWarp, dynamic resolution all off — first knobs to reach for.

### Android traps

Dormant only because `QuestSceneSetup` sets `game.enabled = false`; all return together once code runs on-headset.

- `streamingAssetsPath` is a `jar:` URL inside the APK — `File.Exists`/`ReadAllText` always fail. Use a `Resources` ScriptableObject (`QuestHostConfig`) or `UnityWebRequest`.
- `Microphone` needs manifest `RECORD_AUDIO` + runtime `Permission.RequestUserPermission`. No custom AndroidManifest yet.
- `UnityWebRequest` to `127.0.0.1` hits the *headset*, not the Mac. Plain HTTP may need `usesCleartextTraffic`.
- IL2CPP strips code: reflection-based JSON works in-editor, returns null on device. No `link.xml` — suspect first if the headset connects but renders nothing.
- Screen-space UI Toolkit (`m_RenderMode: 0`) does not render in stereo. No controller or gaze input — keyboard and pointer only.

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
