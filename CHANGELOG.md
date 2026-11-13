# Changelog

All notable changes to this repository are documented here (newest first).

## [Unreleased]

### Added
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
