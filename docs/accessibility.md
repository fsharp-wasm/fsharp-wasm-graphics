# Accessibility of the widget toolkit

Status: **under way (2026-11-17)**. Measured first, then built; this page says what is measured, what exists, and what is next, so
that the work can be taken up at any point.

## Why this is a structural problem, not a list of bugs

The toolkit draws pixels. A program built on it (the widget editor, the gallery) is **one `<canvas>`**: to assistive technology
it is a single unnamed picture. A scene knows rectangles, glyphs and handlers; it does not know that a rectangle with a label
*is a button*. Keyboard handling is per application (the editor reacts to some keys for some widgets), the focus ring is drawn
by some widgets and not by others, and the colours were never checked against a contrast ratio.

## Measured (2026-11-17, before any change; the colours, focus rings, target sizes and the keyboard in the toolkit are fixed since, see below)

Method: [`tools/a11y/baseline.mjs`](../tools/a11y/baseline.mjs) — axe-core 4.10.2 with the WCAG 2.0 / 2.1 / 2.2 A and AA rules,
Chromium's accessibility tree, Tab stops. Raw results: `tools/a11y/baseline-2026-11-17-*.jsonl`.

| what | result |
|---|---|
| axe-core on the widget editor | **0 violations** — and that is the problem: a canvas with an `aria-label` passes every rule |
| what a screen reader finds in the editor | **1 node**: the canvas |
| interactive controls drawn by the editor | 25 or more (3 in the top bar, 10 in the palette, the elements on the surface, about 8 in the inspector) — **0 exposed** |
| keyboard: add an element, select an element, use the top bar | **not reachable** (a button takes focus only from a pointer press; Enter and Space do nothing; Tab cycles the inspector's fields only) — fails WCAG 2.1.1 |
| keys in the browser | the editor matches `Left`, `Right`; the browser sends `ArrowLeft`, `ArrowRight` — the arrow keys do nothing |
| contrast of the theme (WCAG 1.4.3 text 4.5 : 1, 1.4.11 controls 3 : 1) | **8 of 19** token pairs fail in the dark theme, **12 of 19** in the light theme (white on the accent colour 3.68 : 1, on its hover colour 2.54 : 1; field borders 1.6 – 2.0 : 1; slider and toggle tracks 1.1 – 1.4 : 1) |
| visible focus (2.4.7) | Button, TextField and Dropdown draw one; CheckBox, Toggle and Slider do not |
| target size (2.5.8, 24 × 24) | CheckBox rows 18 px, the slider band 20 px |
| the organisation's site (29 pages) | 2 problems: colour contrast of the code highlighting in two docs pages, no `lang` on the CMS page |

## Design

1. **Semantics in the scene** (done): `Scene.Semantic(Semantics, child)` — a widget describes itself (role, name, value, state,
   range, `Act`: what a screen reader may ask of it). Transparent for layout, drawing and hit testing. `SemanticTree.collect`
   turns a resolved scene into the elements with their places in window space; `SemanticTree.walk` gives the same order for
   an authored scene (key handling needs no layout).
2. **Overlay in the browser** (next): the canvas host sends the tree to `runtime/gfx.js` (`a11yBegin`, `a11yNode(...)`,
   `a11yEnd`, plain numbers and strings like the drawing imports), which keeps a DOM of ARIA elements over the canvas, one per
   element, at the place it is drawn. A text box is a real `<input>` (caret, selection, IME, the platform's own editing). Actions come back
   through one export, `onA11y(index, action, value)`. The DOM focus and the toolkit's focus follow each other.
3. **Keyboard in the toolkit** (next): `Ui.root` handles Tab / Shift+Tab over the focus order of the semantic tree, Enter and
   Space on the focused element, arrows on sliders, Escape; and maps the browser's key names (`ArrowLeft`, `" "`) to the
   toolkit's. Applications stop re-implementing it.
4. **Theme** (next): tokens that meet AA — computed values are in the table below — and a test that fails when a pair does
   not. New tokens `Control` (boundaries of controls, 3 : 1), `Focus` (the ring), `Indicator` (non-text accent).
5. **Widgets** (next): every basic widget gets its semantics, a focus ring, and a target of at least 24 × 24 px; `TextField`
   gets `Label` and `OnChange`, `ProgressBar` and `ScrollView` a name (breaking, documented in the changelog).
6. **The editor** (after): regions (toolbar, elements, design surface, inspector), the design surface as a group of
   selectable elements (Enter selects, arrows move, Delete removes), announcements of what changed.

### Theme tokens that meet WCAG 2.2 AA (computed, not yet applied)

| token | dark | light | needed |
|---|---|---|---|
| `Accent` (button fill, white label) | `#2563EB` 5.17 : 1 | `#2563EB` | 4.5 : 1 with `OnAccent` |
| `AccentHover` (darker, not lighter) | `#1D4ED8` 6.70 : 1 | `#1D4ED8` | 4.5 : 1 |
| `AccentPressed` | `#1E40AF` 8.72 : 1 | `#1E40AF` | 4.5 : 1 |
| `Danger` (fill, white label) | `#DC2626` 4.83 : 1 | `#DC2626` | 4.5 : 1 |
| `Control` (borders, tracks of controls) | `#8393AD` 4.0 – 5.7 : 1 | `#5B6B82` 4.4 – 5.4 : 1 | 3 : 1 against every surface |
| `Focus` (the ring) | `#93C5FD` ≥ 7.0 : 1 | `#1D4ED8` ≥ 5.4 : 1 | 3 : 1 |
| `Indicator` (checked box, toggle on, slider value) | `#3B82F6` 4.0 – 4.9 : 1 | `#2563EB` 4.2 – 5.2 : 1 | 3 : 1 |
| `TextMuted` | `#94A3B8` (passes) | `#475569` 6.2 – 7.6 : 1 | 4.5 : 1 |
| `Success` | `#22C55E` (passes) | `#15803D` | 3 : 1 |

## What is done

* **Semantics in the scene**: `Role`, `Live`, `SemanticAction`, `Semantics` and the `Semantic` scene node (handled by `Resolve`,
  `Hit`, `Layout`, the combinators, the optimizer, the canvas emitter and the render cache); `SemanticTree.collect` / `walk` /
  `focusOrder`.
* **Theme**: new tokens `Control`, `Focus`, `Indicator`; `Accent` and its states darker so that white labels have 4.5 : 1;
  light theme `TextMuted` and `Success` fixed. Every pair a widget draws is checked by a test, in both themes.
* **Widgets** (`Label`, `Button`, `CheckBox`, `Toggle`, `ProgressBar`, `Slider`, `TextField`, `ScrollView`, `Dropdown`): each
  reports its role, name, state and range; each draws a focus ring (inside its box, so focusing never moves the layout); check
  boxes, toggles and sliders are at least 24 px high (2.5.8). `viewNamed` variants take the accessible name; the old `view`
  functions keep working and name the widget after its label, placeholder or id.
* **Tabs and the grid**: `Role.TabList`, `Role.Tab` (only the selected tab is in the order of Tab; the arrows, Home and End move to the others), `Role.TabPanel` (`Tabs.panel`) and `Role.Grid`, whose value is the selected cell as a sentence
  ("Account 4010, Q3: 1,204.00") and whose description lists its keys. The cells themselves are not in the semantic tree: a table of a million rows cannot be, and a screen reader is told the one cell that
  is selected; the DOM overlay for tables is part of the accessibility layer that is not finished.
* **Keyboard in the toolkit** (`Ui.root`): Tab and Shift+Tab over the focusable widgets in scene order (a disabled one is skipped),
  Enter and Space on button, check box, switch and combo box, the arrows, Home and End on a slider, a scroll region and a
  combo box, Escape on an open combo box; the browser's key names (`ArrowLeft`, `" "`) map to the toolkit's. An application
  receives the keys the toolkit does not handle, under the toolkit's names.
* **Tests** (`tests/FSharp.Graphics.WidgetTests`, 88 checks, in CI): the contrast of every pair, the semantics of every widget,
  the place and size of the elements, the keyboard. A mutation check (the old accent colours back in) fails with exactly the
  measured ratios, 3.68 : 1 and 2.54 : 1.

The host tests of the split projects (78) and the pixel goldens (18) are unchanged.

## What is next

1. **The overlay in the browser** (`runtime/gfx.js`): the tree reaches the page, so that a screen reader finds it.
2. **The editor** adopts the toolkit: regions, the design surface as selectable elements, `viewNamed` everywhere, its own Tab
   handling removed (the toolkit's replaces it), announcements of what changed. The editor pins an older revision of this
   library; moving it is part of this step.
3. End-to-end: axe-core and the accessibility tree on the editor page, Tab stops, and a keyboard-only session, in CI.

## Not covered by any automatic check — to be done by hand

Screen reader runs (NVDA, VoiceOver, Orca), zoom to 400 % and text spacing (1.4.4, 1.4.10, 1.4.12), reduced motion
(2.3.3), forced colours, touch targets on a phone. An automated tool finds a minority of the problems; the overlay makes the
rest *testable*, it does not replace testing.
