# Kinesthetic — Physician Portal Design System

## Philosophy

Clinical precision over decoration. Every element earns its place.
The interface should feel like a well-calibrated instrument — not a consumer app.
Data is the hero. The UI recedes.

The texture reference: a pharmaceutical tablet has a matte, slightly gritty surface.
Grain, restraint, and one purposeful color against a dark base — that is the register.

---

## Color

### Base
| Role | Hex | Usage |
|---|---|---|
| Background | `#232B2F` | Page background, main content area |
| Surface | `#2A3337` | Sidebar, panels, cards |
| Raised | `#323C41` | Hover states, selected rows, elevated cards |
| Border | `#3D484E` | All dividers, card edges, input borders |

### Text
| Role | Hex | Usage |
|---|---|---|
| Text | `#E4E9EB` | Headings, primary content |
| Muted | `#A3B0B6` | Labels, captions, secondary info, placeholders |

### Primary — Soft Teal
| Role | Hex | Usage |
|---|---|---|
| Primary | `#6FB8C4` | Buttons, active borders, links, focus rings |
| Primary hover | `#86C5CF` | Hover on primary elements |

One voice. Used for interactive elements and active states only.

### Accent
| Role | Hex | Usage |
|---|---|---|
| Accent | `#9CC2B5` | Sage. Small highlights, icons. Never on buttons. |

### Status
| State | Hex | Usage |
|---|---|---|
| Success | `#7CC49A` | On track |
| Warning | `#E3A86B` | Monitor / watch |
| Danger | `#E08585` | Needs review / alert |

Status colors appear only as dots and inline badges. Never decorative.

---

## Texture

Global grain overlay: SVG `feTurbulence` at 3.5% opacity, `mix-blend-mode: overlay`.
On the dark charcoal base this adds depth and a matte, instrument-panel quality.
Do not animate it. It is ambient.

---

## Typography

- **Font**: `system-ui, -apple-system, sans-serif`
- **Headings**: Bold, `tracking-tight`, `#E4E9EB`
- **Labels**: 10px, uppercase, `tracking-[0.18em]`, `#A3B0B6`
- **Numbers / data**: `font-mono` — all measurements, counts, percentages
- **Body**: `text-sm` (14px) throughout the portal

---

## Spacing & Shape

- Border radius: `rounded-sm` on inputs and buttons. `rounded-none` on cards and panels.
- Card padding: `p-5` standard, `p-6` for main panels.
- Sidebar width: `192px`.
- Sidebar patient row height: compact — `py-2`, single line name + truncated condition.

---

## Components

### Button
| Variant | Fill | Text |
|---|---|---|
| `primary` | `#6FB8C4` | `#232B2F` |
| `outline` | transparent | `#E4E9EB`, teal border/text on hover |
| `ghost` | transparent | `#A3B0B6`, raised bg on hover |
| `accent` | `#9CC2B5` | `#232B2F`, transitions to primary on hover |

### Input
Surface bg (`#2A3337`), border `#3D484E`, primary border on focus, danger on error.

### Sidebar patient row
Compact: `py-2`, name (`text-xs font-medium`) + truncated condition (`text-[10px]`).
Active: `bg-raised`, left border `#6FB8C4`.
Status dot: right-aligned, 6×6px.

### Stat card
Surface bg, border, label (muted, uppercase, 10px), value (`font-mono`, large, text-primary).

### Status badge (patient header)
Colored border at 33% opacity, 24% tint background, matching dot + label.

---

## Layout

- **Sidebar**: 192px, `bg-surface`, fixed left
- **Main**: `flex-1`, `bg-background`
- **Pre-auth pages**: full-bleed `bg-background`, centered content

---

## Motion

- `transition-colors duration-150` on interactive elements
- `animate-pulse` on live stream indicators only
- No entrance animations

---

## What to avoid

- Rounded corners larger than `rounded-sm`
- Drop shadows
- Gradient fills on buttons
- More than one primary element per view
- Status colors outside status indicators
- Any color not in this palette
