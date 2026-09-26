# The UI system

Read this before adding a button, a label, a status or a readout to any screen.

## The rule

**A component owns its whole appearance. A screen chooses which component and which variant, and
nothing else.** There is one button in this product, not six that were drawn to match. It looks the same
in the studio, on the course, at the lanes and on the menu board because it is the same one.

Variation is a closed set: `tone="Primary"`, `size="Large"` — an enum the component defines, resolving to
values written once in the component's own stylesheet. A screen cannot pass a colour, a radius, a font
size or a padding, because no component accepts one. Need a look no variant gives? Add the variant in
`UI/`, once, for everyone.

Colour follows the palette rule in `AGENTS.md`: every value in a component is a `var(--token)` from
`Palette.uss`, or `Palette.*` for the parts painted in code.

## Using a component

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:k="Kinesthetic.UI">
    <Style src="../Palette.uss" />
    <Style src="../UI/Base.uss" />
    <Style src="Studio.uss" />
    …
    <k:KButton name="start" text="Start session" tone="Primary" size="Large" class="studio-start" />
</ui:UXML>
```

In C#, components are ordinary elements: `root.Q<KButton>("start").clicked += Begin;`,
`new KChip { text = "AirPods connected", state = KChip.State.Good }`.

**`UI/Specimen.uxml` shows every component in every variant.** Open it in UI Builder. It is the visual
contract: if something looks wrong there it looks wrong everywhere, and it is fixed there once.

## How a component is sealed

1. **The UXML attribute surface is the variant API.** Components are `[UxmlElement]` controls whose
   `[UxmlAttribute]`s are enums, content (`string`) or counts (`int`, `float`, `bool`). There is no
   attribute a colour could go into.
2. **A component carries its own stylesheet.** Each one attaches its sheet from
   `UI/Resources/KinestheticUI/` in its constructor, so it looks the same whether or not the screen it
   lands in linked anything — including panes and panels this system does not own. No `PanelSettings`
   asset is involved.
3. **Whatever a component sets, the component wins.** Every component selector is at least two classes
   (`.k-button.k-button--primary`), which outranks a screen's `Button { }` (one type) or `.sheet Button`
   (a class and a type). And each component sets every property those older rules touch, so nothing
   leaks through. A screen's own class on a component can still add what the component leaves alone —
   margin, position, flex — which is exactly the layout a screen is allowed.
4. **It is checked.** See *Enforcement*.

## The components

Values below are what the stylesheets hold; the tables are the whole variant surface.

### `KButton` — `tone`, `size`, `shape`

Derives from `Button`, so `text`, `name`, `clicked` and `Q<Button>` all behave as before.

| `tone` | Fill | Ink | Edge | For |
|---|---|---|---|---|
| `Primary` | `cerulean-50` | `sand-00` | `cerulean-60`, lip `cerulean-70` | the one thing to press |
| `Secondary` (default) | `sand-00` | `prussian-50` | `cerulean-30` | everything else you can press |
| `Ghost` | none | `slate-70` | none | a control that should not draw the eye |
| `Quiet` | `sand-00-a24` | `prussian-70` | `sand-00-a40` | chrome standing over a venue |

| `size` | Pill height | Font | Round Ø |
|---|---|---|---|
| `Small` | 36px | 14px | 36px |
| `Medium` (default) | 46px | 16px | 48px |
| `Large` | 54px | 18px | 132px |

`shape="Pill"` (default) is fully rounded with a 4px lip that halves when pressed. `shape="Round"` is a
circle for a single glyph: aim arrows, a close cross.

**Hover and focus are one state for every tone**, so a person driving this with their head sees what a
person with a mouse sees. **World-space panels get no pointer events** — `panel.Pick` returns null there
(`GazeDwell.cs:17`) — so `:hover` never fires on the plaza board or in a pane. Whatever resolves the ray
sets `button.Hot` and `button.Pressed`; they draw exactly what `:hover` and `:active` draw. Disabled is
one look for every tone: `sand-40` fill, `slate-70` ink.

### `KEyebrow` — `backdrop`

The small capitals above a heading: 11px, tracking 2px, ExtraBold. `Light` (default) is `slate-50`;
`Dark` is `sand-30`, for glass over a venue. Write the copy in capitals — USS has no text-transform.

### `KTag` — `tone`

A small pill of metadata: 12px, radius 12px. Lettering on a tint pairs `<family>-60` on `<family>-10`,
the pairing `Palette.uss` documents as clearing 4.5:1.

| `tone` | |
|---|---|
| `Neutral` (default) | `slate-60` on `sand-20` |
| `Info` | `cerulean-60` on `cerulean-10` |
| `Good` | `jungle-60` on `jungle-10` |
| `Trouble` | `coral-60` on `coral-10` |

### `KChip` — `state`, `text`

A device or connection and whether it is there. The dot carries the state in the family whose meaning
matches: `Idle` `slate-30`, `Live` `cerulean-50`, `Good` `jungle-40`, `Trouble` `coral-40`. Fill and
lettering follow the same `-60` on `-10` pairing as tags. A chip is not pressable; put a `KButton` beside
it.

### `KSteps` / `KStep` — `activity`, `numbering`

Help copy is data. `<k:KSteps activity="golf.adaptive" />` reads that activity's `help.steps` from the
catalog (`coordinator/activities.json`, see [ACTIVITIES.md](ACTIVITIES.md)), so no screen's markup holds
a second copy of it.

A readiness checklist is not help text, so it is written out as `<k:KStep title="…" copy="…" />`
children, with `numbering="Progress"`: set `steps.Step(i).Done = true` and that badge turns to a
`jungle` check. `numbering="Number"` (default) always shows 1, 2, 3.

### `KReadout` — `value`, `caption`, `size`, `tone`

A measured figure and what it is.

| `size` | Figure | Caption | Read at |
|---|---|---|---|
| `Hero` | 78px | 16px | across a room |
| `Large` | 56px | 16px | two metres, the primary figure on a panel |
| `Regular` (default) | 42px | 15px | two metres, one of several |
| `Compact` | 28px | 12px | a scoreboard cell |

`tone` is `Ink` (default, `prussian-50`), `Good` (`jungle-60`) or `Target` (`coral-60`), and it is a
verdict: a figure turns `Good` when it passes the plan, never because it is large.

### `KArc` — `form`, `segments`, `fraction`, `tone`

A ring that fills. `Continuous` (default) is one sweep; `Segmented` draws one arc per rep
(`segments`, up to 24). The screen gives it a size; anything inside it — usually a `KReadout` — is
centred. The track is `Palette.Line`; the fill is `Progress` (default), `Good` or `Target`.

### `KMeter` — `form`, `count`, `filled`, `fraction`, `tone`

A straight measure. `Pips` (default) for a count a person can check — "4 of 5" — at 15px tall; `Bar`
for a continuous value at 8px. The screen sets the width. Same track and tones as `KArc`, so a ring and a
bar on one panel read as one system.

### `KText` — `size`, `tone`

Readable headings and instructions. Sizes: `Title` (36px), `Heading` (26px), `Body` (19px),
`Caption` (15px). Tones: `Ink`, `Soft`, `OnDark`, `Success`. Text wraps by default.

### `KSurface` — `tone`, `density`

Shared surfaces for compact HUDs and summaries. Tones: `Paper`, `Glass`, `Scrim`. Densities:
`Compact`, `Comfortable`, `Flush`. The screen positions the surface and its children; the component
owns its padding, background, edge and radius. A scrim's blocking behavior and visibility remain
with the screen. These variants and `KText` are also shown in `Specimen.uxml`.

## What a screen may do

A screen's own stylesheet keeps its own classes, and they set **layout only**: `position`, the insets,
`width`/`height` and their min/max, `flex-*`, `align-*`/`justify-*`, `margin`, `overflow`, `display`.

Never **paint**: colour, background, border, radius, font, font size, letter spacing, padding, scale,
opacity, transition. Padding counts as paint because it is part of how a component reads.

A screen positions components. It does not dress them.

## Enforcement

```bash
python3 scripts/check_ui.py
```

It fails on:

1. **Palette.** A `var(--x)` in `UI/` that `Palette.uss` does not define; a colour literal in `UI/`
   (only `rgba(0, 0, 0, 0)`, the absence of a colour, is allowed); a hex colour in any stylesheet but
   `Palette.uss`.
2. **Restyling a component.** A `.k-` selector in any stylesheet outside `UI/`.
3. **Leaking the variant API.** A component `[UxmlAttribute]` that is not an enum from its own file, a
   `string`, `int`, `float` or `bool`.
4. **Paint in a sealed screen.** Linking `UI/Base.uss` is what seals a screen: from then on every
   stylesheet it links outside `UI/` must be layout-only.

Screens that do not link `Base.uss` yet are legacy: counted, not failed, so the check holds from the
first commit while screens move over one at a time. **Baseline at landing: 8 legacy screen sheets
setting 1,052 paint declarations.** That number only goes down.

## Migrating a screen

One screen at a time, by whoever owns it:

1. Link `../UI/Base.uss` after `Palette.uss`, and delete the screen's own `Label { margin: 0 }` reset,
   `.hidden` and font declarations — `Base.uss` has them.
2. Swap each element for its component and variant. Keep the `name` so `Q<>` lookups still resolve;
   `Q<Button>` finds a `KButton`.
3. Strip paint from what remains until `scripts/check_ui.py` passes. If a look has no component or
   variant, add it to `UI/` rather than keeping it local.
4. On a world-space panel, drive `Hot`/`Pressed` from whatever resolves the ray.

## Known limits

- **Studio migration verified in Play mode.** The studio uses the shared components, including its
  connection state, repetition ring, angle meter and summary. Other screens remain on their legacy
  sheets until migrated; run the UI check and inspect each migration in the Game view.
- **The same px is not the same size on every screen.** Golf and Replay panels use a 1400×900 reference
  resolution; Bowling, Navigation and Rehab use 1600×900; the menu board is world-space. So a 16px
  button renders about 14% larger in Golf. The fix is in the `PanelSettings` assets, which this system
  deliberately does not touch.
- **Quest is unproven.** No UI Toolkit has rendered on the headset here yet (`QuestSceneSetup.cs:40`
  disables the UIDocument on device). `[UxmlElement]` uses generated code rather than reflection, which
  is the right side of the IL2CPP stripping trap in `AGENTS.md`, but that is reasoning, not a device
  test.

## The clinician portal is out of scope

`coordinator/portal/` is a separate design system on purpose: its own tokens, dark mode, system fonts
and a desktop reading distance. A clinician at a monitor and a patient two metres from a 1.85 m panel
do not want the same 78px figure. The two token vocabularies do not map (`--accent` `#2a78d6` against
cerulean `#2274A5`; roles like `--good`/`--bad` against hues with fixed meanings). If they ever need to
agree, that mapping is the work, not a shared stylesheet.
