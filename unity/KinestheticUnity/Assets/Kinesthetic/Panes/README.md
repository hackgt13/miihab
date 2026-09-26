# Panes

A pane is a rectangle of UI standing in a venue. **`Pane` is the only pane type in the project**
— one implementation, owned here. The menu board, gallery and friends panes that
`MainMenuSetup` builds by hand predate it; moving them onto `Pane` is the next consolidation.

`Kinesthetic.Shell.PaneCarousel` owns where panes stand. This folder owns what a pane is and
what is inside it. Nothing here moves a pane except a deliberate grip drag.

## Files

| File | What it is |
| --- | --- |
| `Pane.cs` | The surface. Sizes itself in panel pixels and scales down (1000 px per metre, 100 px per unit), works on its own copy of the PanelSettings, owns the content's lifetime, ticks it, draws the chrome, and projects world points onto itself (`TryProject`, `SendPointer`). |
| `IPaneContent.cs` | The content contract: `Bind(root)`, `Tick()`, `Dispose()`, `Title`, `PreferredSize` in metres. A texture case ends in an `Image`. |
| `PaneHost.cs` | Opens and closes panes through the ring's `Add`/`Remove`, focuses the faced one, turns `Ticking` off for the rest, routes the pointer, drags by the grip. |
| `WorldPanelPick.cs` | Ray → element, in paint order, so a modal shade blocks what is under it. Used because `panel.Pick` returns null on world-space panels. |
| `PanePointer.cs` | Ray and press only: mouse, head gaze (reusing `GazeReticle`), or `External` for a wrist. |
| `Content/AssetPane.cs` | A local image, page of text, or clip from `local-data/`. |
| `Content/GamePane.cs` | A live 3D stage: own camera → RenderTexture → `Image`, built 10 km out. Layer 30; 31 is the golf minimap's. |
| `Pane.uxml`, `Panes.uss` | Optional chrome (grip, title, close). Without a tree asset, content fills the whole panel. All colour is `var(--*)` from `Palette.uss`. |
| `../../Editor/PaneSandboxSetup.cs` | `Kinesthetic → Panes → Create pane sandbox`. Not in `EditorBuildSettings` on purpose. |

## Lifetime rules

- `SetContent(next)` disposes the previous content exactly once.
- Disabling the pane or its UIDocument keeps the content; it binds again when the tree returns.
- Destroying the pane disposes the last content and restores the shared PanelSettings reference.
- `Ticking = false` stops `Tick` without unbinding anything — what the host uses for panes nobody faces.

## Verified

`Kinesthetic → Menu → Verify navigation and panes (Play mode)` passes every pane check: scaled and
rotated projection, pointer coordinates matching picking, modal-shade occlusion, disabled
buttons, dispose-once on replace, keep-on-disable, rebind-on-reopen, dispose-on-destroy, and
never touching the shared PanelSettings. It then fails later, on the menu half: it looks for
`bowling-card` on the board, but the menu rework moved the activity cards into the separate
gallery pane (`Gallery.uxml`).

## Unverified

- The sandbox scene has not been run, so `PaneHost`, `PanePointer`, `AssetPane` and `GamePane`
  have not drawn anything yet.
- `AssetPane` paths are editor-correct only; a built player needs the coordinator on 8766.
- Quest: UI Toolkit has never rendered on the headset in this project.
