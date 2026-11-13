# Changelog

All notable changes to this repository are documented here (newest first).

## [Unreleased]

### Added
- **Browser host** (`demo/FSharp.Graphics.Browser`): the GUI builder compiled to WebAssembly GC and drawn onto one canvas
  through the functions of `runtime/gfx.js` (fsharp-wasm-runtime), which is also where its page lives. Exports `start`,
  `onResize`, `onPointer`, `onWheel`, `onKey`, `frame`; `bash build/build-browser.sh` builds it.
- `docs/consolidation.md`: the duplicated code (the monolith `src/FSharp.Graphics`, GlyphAura's copies of the primitives, the
  golden tests' rasterizer) with measured similarities and the order to remove it.
- Apache License 2.0 (`LICENSE`, `NOTICE`; the submodules keep their own).

### Changed
- `FSharp.Math` imports the compile list of `FSharp.Wasm.Core` (the portable `Dictionary`, `HashSet`, `Queue`, `Stack`, `Map`,
  `Set`) for the WasmGC build; `Directory.Build.props` names its directory once.
- Library code that the compiler could not translate was rewritten in a form that is equally plain on .NET: no
  `System.HashCode` in `Csg`, key-mapping tables of `Ui` as `match` functions, `Model.variantName`.
- The golden tests use the host's PNG encoder instead of their own copy.

### Removed
- `demo/FSharp.Graphics.Browser/Interop.fs` and `tests/FSharp.Graphics.GoldenTests/Png.fs` (copies).
