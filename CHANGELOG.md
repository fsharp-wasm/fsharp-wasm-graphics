# Changelog

All notable changes to this repository are documented here (newest first).

## [Unreleased]

### Fixed
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
