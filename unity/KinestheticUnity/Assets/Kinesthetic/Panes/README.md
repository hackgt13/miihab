# Panes

A pane is a rectangle of UI standing in a venue: chrome, a content area, and a pointer that works
the same from a mouse, from head gaze, or from a pose-tracked wrist.

**This half owns what a pane is and what is inside it. `Kinesthetic.Shell.PaneCarousel` owns where
panes stand.** That boundary is the carousel's own: *"the panes themselves, and everything inside
them, belong to `Kinesthetic.Panes`."* Nothing here touches a pane's transform except a deliberate
grip drag, and the carousel holds no opinion about content. There is no window type on either
side — one noun for one object.

## Files

| File | What it is |
| --- | --- |
| `IPaneContent.cs` | The content contract: `Bind(root)`, `Tick()`, `Dispose()`. One shape, not two — a texture case ends in an `Image`, the way `GolfHud` already wraps its minimap. Content never learns which pane it is in. |
| `Pane.cs` | One surface. UIDocument, its own `BoxCollider` sized to `WorldSize`, chrome, focus. Never disables its UIDocument, because that destroys the visual tree. |
| `PaneHost.cs` | Opens, closes, focuses, drags. Hands the whole list to `PaneCarousel.Adopt`. Ticks only the pane being faced. |
| `WorldPanelPick.cs` | Ray → element, in one place, because `panel.Pick` returns null on world-space panels. |
| `PanePointer.cs` | Ray and press, nothing else. Mouse, gaze (reusing `GazeReticle`), or `External` for a wrist. |
| `Content/AssetPane.cs` | A local image, page of text, or clip from `local-data/`. |
| `Content/GamePane.cs` | A live 3D stage: own camera → RenderTexture → `Image`, built 10 km out so venue cameras clip it — the trick `GolfHud` uses. Layer 30; 31 is the minimap's. |
| `Pane.uxml`, `Panes.uss` | Chrome. Zero hardcoded colour: every value is a `var(--*)` from `Palette.uss`. |
| `../../Editor/PaneSandboxSetup.cs` | `Kinesthetic → Panes → Create pane sandbox`. Deliberately not added to `EditorBuildSettings`. |

## Running it

**Kinesthetic → Panes → Create pane sandbox**, then Play. Three panes stand in the ring: a page, a
clip, and a stage with one shape that spins and one that follows the pointer. Drop files at
`local-data/panes/readme.txt` and `local-data/panes/clip.mp4`; a missing file reports itself in the
pane rather than failing silently.

## Unverified

- **Never run.** No pane has been drawn yet. Compilation is the only thing checked.
- **`WorldPanelPick` normalises against the pane's declared metres, then maps to panel coordinates
  with a y-flip.** `GazeDwell.Under` skips that step and compares metres against pixels, which only
  agrees for a 1:1 panel. One of the two is wrong. A pane that highlights its close button when you
  look at its corner will say which, and then `GazeDwell` should adopt this helper — a three-line
  change that deletes the duplicate.
- **Resolved:** the carousel's owner answered the `Adopt` index-reset question in code
  (`3dacf36`) with `Add`/`Remove` and an `Adopt` that holds its ground. `PaneHost` uses `Add` and
  `Remove`, so opening a pane never spins the person away from what they were reading.
- **`AssetPane` paths are editor-correct only.** `local-data/` resolves relative to
  `Application.dataPath`; a built player needs the coordinator on 8766 to serve the files instead.
- **Quest is unproven.** `QuestSceneSetup.cs:40` disables the UIDocument on device, so UI Toolkit
  has never rendered on the headset here. World-space panes are proven on the Mac only — which is
  where they are proven: `MainMenuSetup.cs:76-79` has been standing a world-space panel on the
  plaza all along.
