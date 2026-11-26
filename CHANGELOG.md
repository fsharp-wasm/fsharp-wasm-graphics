# Changelog

All notable changes to this repository are documented here (newest first).

## [Unreleased]

### Added
- **`FSharp.Graphics.Icons`**, a library (packable): the icons of `FSharp.Wasm.Icons` (typed shapes on a 24 x 24 grid, the Lucide set) on a canvas. `PathData.parse` reads SVG path data to absolute commands (relative moves, `H`, `V`, the smooth curves and arcs with their flags written together, a number repeating the last command), `PathData.flatten` samples curves and arcs to straight segments (an arc by the centre form of the SVG notes: the radii scaled up when too small, the direction and the long way round), `IconScene.view size color icon` makes the strokes of every shape as round-ended segments of one colour: the same picture as the SVG text and the DOM view of the library, with no icon written twice. The library comes with the core as the submodule `lib/fsharp-wasm-core` (`FSharpWasmCoreRoot`). 51 checks under .NET and as WebAssembly GC.
- **`Keyboard`** in `FSharp.Graphics.Interact`: the same drag by the keyboard. `Keyboard.places` lists where a drop can go in reading order (before each slot, into each one that nests, after the last), `Keyboard.grab` picks something up (over the drop the pointer had, or the first place) and `Keyboard.step` moves it by an arrow key, stopping at the ends. It is a `Session` like the pointer's, so a view draws the same mark and `Drag.release` (Enter) and `Drag.cancel` (Escape) end it. `Drop.place` is the one place that makes a drop and its mark. 20 checks under .NET and as WebAssembly GC (63 of the library).
- **`Instruments`** in `FSharp.Graphics.Charts`: the instruments of a dashboard as scenes. `Instruments.gauge` (a dial of 270 degrees with zones, the value as an arc and a needle), `level` (a tank with marks for the limits), `lamp`, `sparkline` and `kpi` (the card of a key figure). The colour of a value is a `Tone` (`Normal`, `Warning`, `Alarm`, `Fault`); a reading that is not a number draws no needle and says a dash and "not trusted"; each instrument is named for assistive technology with its value and its state in words. 14 checks, the same under .NET and as WebAssembly GC (docs/dashboards.md).
- **`Properties`** and **`Chip`** of `FSharp.Graphics.Widgets`: the rows of an inspector, made once for the editors (the widget editor, the newsletter designer). A caption with the value as a chip (`field`), a number on a slider (`slider`) or with a step down and a step up (`number`), a row of presets, an angle (`angle`: a slider over the turn and the right angles), one of a few choices as a segmented row (`choice`), a colour of a palette as swatches (`color`, each a button with the name of its colour), a switch, a line of text, and a group under a heading (`section`). A number has a `Range` (limits, step, unit, decimals) and snaps to it. Each is a pure function of its value and a callback, built from the widgets that are there, so the keyboard and the screen reader work as on those (16 tests). `Label.viewSized` sets the size of the letters of a label.
- **`TextInput`**, typed text as an event of its own, `Ui.rootText` and `TextEdit.typed`: a host that has it sends what a person typed as the platform made it (layout, Shift, Caps Lock, AltGr, accents), and the text field takes it as it is. `Ui.root` does not hear it, so nothing that exists changes. A single character that is not a letter or a digit is itself in `TextEdit.apply`, for a host that still says it as a key (13 tests).
- **`FSharp.Graphics.Charts`**, a library (packable): charts of a size that a screen cannot show point by point (`docs/charts.md`). `Pyramid` keeps the minimum and maximum of every aligned block of samples, so the range of any run of them is O(log n) (built in one pass, appended to one sample at a time, cleared for a recording that starts again); `DensityGrid` is a grid of counts with its summed-area table, so the points in a rectangle of cells are four reads; `Linear`, `Domain` (zoom keeps the value under the pointer, pan, both with limits), `Ticks` and `Format`; `Geometry` (the ribbon along a line, the band between the smallest and largest sample of each pixel column, the area); and the scenes `Line` (band or ribbon by samples per pixel), `Candles`, `Bars`, `Heatmap` and `Parts` (axes, legend, crosshair with a card, the chart as an image for assistive technology). A frame costs the pixels of the chart: one frame of a line of ten million samples is 123 to 150 µs for the columns, against 12.5 to 14.3 ms reading every sample (2026-11-26, headless Chromium). 98 checks under .NET.
- `demo/FSharp.Graphics.Browser.Charts`: the demo module (`ChartDemo.wasm`): ten million samples with single-sample spikes, a stream of two series at 100 000 samples a second, a density map of five million points and two hundred thousand candles; zoom about the pointer, drag, a crosshair that reads the data, keys, and the kernels of the benchmark.

### Changed
- **`Damage` no longer merges regions that merely overlap**: the two lines of a crosshair cross, and their bounding box is the plot, which a moving pointer repainted whole (78 ms a frame for a density map of 14 000 rectangles in headless Chromium, 19 ms after). Boxes are merged when their union is hardly larger than the two together, and when there are more than six the two that waste the least are merged until six are left (instead of one box around all of them). `CanvasTests` has a crosshair that moves and eight changes far apart (both fail without the change).

### Fixed
- **A text field could type letters, digits and the space only** (a browser says `"."`, `"?"`, `"@"` and the toolkit knew the US names `"Period"`, `"Number1"`): no punctuation, nothing a layout makes of Shift, no capital with Caps Lock; found 2026-11-26 in the widget editor.
- **A key before the pointer has been seen is no longer dropped** (`Hit.Dispatcher`): it goes to what is at the origin of the scene, where the root is, so a page that is typed into at once works (it reached nothing until the pointer had moved). The check that said a key "reaches nothing" before any pointer movement said so by mistake and now says what is meant.
- **A widget inside a `ScrollView` can be reached** (`ScrollView.viewNamed`): the transparent area the region answers for was drawn above its content, and a picked shape hides what is under it, so no press got through to a button, a check box or a field in the region. It is below the content now; where there is no content, the region still answers (a press focuses it). Checked: a button in a `ScrollView` is clicked and focused (92 widget checks).
- **Two keys or presses within one frame each meet the state the one before made** (`CanvasHost.Input`): the scene was built once per frame, so Tab, Tab (key repeat, a script, a quick tap) both acted on the state of the frame before and the focus moved once. A key or a press that follows an event that changed the model within the same frame builds the scene again first; the movement of the pointer does not (it comes a thousand times a second). The parameters of `Frame` are called `designW` and `buildScene` now (they hid the host's own state).
- Keys and the wheel go to the handlers of the **current** frame under the pointer (`Hit.Dispatcher`), not to the ones of the frame in which the pointer last moved. Those closed over the state of that frame, so a key (Tab, with the focus of the moment) acted on an old one; it showed as the keyboard focus jumping to the first control.

### Changed
- A `Semantic` node with `Role.None` hides everything inside it from the semantic tree (`SemanticTree.collect` and `walk`) and from the tab order: a picture of a widget, such as an element that is being designed, is not exposed as a live widget. `Role.None` was documented as "not exposed" and did nothing.

### Added
- **`Tabs`** (`FSharp.Graphics.Widgets`): a strip of tabs, closable or not, as a tab list (`Role.TabList`, `Role.Tab`, `Role.TabPanel` for what is under it); the arrows, Home and End move between tabs through `Ui.root`. 15 checks.
- **`Grid`** (`FSharp.Graphics.Widgets`): a grid for tables far larger than the screen. It draws what is in the window and asks for no more (a table of 1000 rows asks for the 40 cells it shows), knows nothing of what the rows and columns are (lines with a label, a depth and a caret; cells are functions), and keeps the arithmetic apart from the drawing: `hit`, `key`, `reveal` and `clamp` are plain functions, tested without a canvas. Selection, the keyboard (arrows, pages, Home, End, Enter, Space), the wheel (Shift for sideways), scroll bars, totals shaded, zeros quiet; `Role.Grid` with the selected cell as its value. 30 checks (151 widget checks in all).
- **`FSharp.Graphics.Interact`**, a library (packable): where a drag lands — `Slot`, `Drop.resolve` (a pointer position means before, after or into a slot; the innermost slot wins; above or below the list is before the first / after the last), `Session` and `Drag` (press, move beyond a threshold, release, cancel; a press that does not move is a click) and `Scroll.near` (the speed of an auto-scroll near an edge). Pure geometry on `FSharp.Math`, no scene graph and no DOM, so the widget editor (canvas) and the newsletter designer (DOM) use one code. 44 checks under .NET and as WasmGC.
- **`FSharp.Graphics.Canvas`**, a library (packable, the tenth package): the canvas host that was part of the browser demo — the `Gfx`
  imports of `runtime/gfx.js`, `CanvasTextEngine`, `Emit`, `CanvasSink` — plus `CanvasHost`, the frame plumbing every module that draws
  onto one canvas needs (size, input, build -> resolve -> emit -> draw, redraw only when something changed). The scene gallery and the
  widget editor of fsharp-wasm-ide are the two modules on it.
- **Scene gallery** (`demo/FSharp.Graphics.Browser.Gallery`, `SceneGallery.wasm`): the thirteen scenes of `FSharp.Graphics.Scenes` on one
  canvas. A second entry project that links the host files of `FSharp.Graphics.Browser` (surface, text engine, command sink). It
  names its scenes to the page (`Gfx.announce`), `select(i)` / `selected()` / `sceneCount()` are exported, ← → Home End switch scenes, a
  canvas narrower than 800 px shows the scene scaled to fit, static scenes are drawn only when something changes.
- The canvas host applies colour matrices (`Gfx.matrixBegin` / `matrixValue` / `matrixUse`); layers with a `ColorMatrix` (grayscale,
  sepia, invert) were drawn unfiltered. Layer alpha, blur and matrix apply to each drawn shape, not to the composed group.
- **Packages.** The nine library projects (`FSharp.Math`, `FSharp.Graphics.Core`, `Primitives`, `Scene`, `SceneDsl`, `Render`, `Tea`,
  `Widgets`, `Host`) are packable: `bash build/pack.sh` makes the `.nupkg` files (dll, XML docs and the sources under `fable/` for programs
  compiled by fsharp-wasm), the CI packs on every push and uploads them as an artifact. Nothing is published.
- `README.md`.
- **Browser host** (`demo/FSharp.Graphics.Browser`): the GUI builder compiled to WebAssembly GC and drawn onto one canvas
  through the functions of `runtime/gfx.js` (fsharp-wasm-runtime), which is also where its page lives. Exports `start`,
  `onResize`, `onPointer`, `onWheel`, `onKey`, `frame`; `bash build/build-browser.sh` builds it.
- `docs/consolidation.md`: the duplicated code (the monolith `src/FSharp.Graphics`, GlyphAura's copies of the primitives, the
  golden tests' rasterizer) with measured similarities and the order to remove it.
- Apache License 2.0 (`LICENSE`, `NOTICE`; the submodules keep their own).

### Removed
- The GUI builder (`demo/FSharp.Graphics.GuiBuilder`, its browser module `demo/FSharp.Graphics.Browser`) moved to
  [fsharp-wasm-ide](https://github.com/fsharp-wasm/fsharp-wasm-ide) as the widget editor; its scripted session is a test there. The
  desktop runner no longer lists it (it would be a dependency of an application on this library); `FSharp.Graphics.GuiBuilder.Shots`
  is now `FSharp.Graphics.Shots` and keeps the DSL, widget gallery and CSG showcases.

### Fixed
- Golden tests: the rasterizer applied alpha twice (`Color.withAlpha` multiplies; four call sites also passed `A / 255 * x`): a
  translucent fill came out too transparent, a blur lost 14 % of its coverage, a 1.5 px stroke of alpha 40 was drawn with alpha 6.
  Seven masters were refreshed (three had the bug baked in, four predate the gradient API change); 18 of 18 pass.
- Math tests no longer fail at random: `blend` with a fully transparent background has no colour, `lerp` overflows for operands near
  the Float32 range, the determinant law needs a tolerance that scales with the operands.

### Changed
- The libraries have no third-party dependency (only the own ones and FSharp.Core); the old Wasm project no longer takes `Fable.Core` from
  NuGet but the `Interop.fs` of FSharp.Wasm.Core. SkiaSharp and Silk.NET remain in the optional Skia host and in the desktop demo,
  xunit/FsCheck in the tests.
- CI: .NET 10 (it built with the 8.0 setup action), runnable by hand.
- `FSharp.Math` imports the compile list of `FSharp.Wasm.Core` (the portable `Dictionary`, `HashSet`, `Queue`, `Stack`, `Map`,
  `Set`) for the WasmGC build; `Directory.Build.props` names its directory once.
- Library code that the compiler could not translate was rewritten in a form that is equally plain on .NET: no
  `System.HashCode` in `Csg`, key-mapping tables of `Ui` as `match` functions, `Model.variantName`.
- The golden tests use the host's PNG encoder instead of their own copy.

### Removed
- `demo/FSharp.Graphics.Browser/Interop.fs` and `tests/FSharp.Graphics.GoldenTests/Png.fs` (copies).
