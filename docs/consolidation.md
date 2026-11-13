# Duplicated code: inventory and the order to remove it

Measured on the tree of this repository and of `fsharp-glyph-aura` (similarity is difflib's quick ratio on the first
20 000 characters — an upper bound; "unique names" counts top-level names that occur nowhere in the other side).

## Done

| what | how |
|---|---|
| The F# base library for programs compiled by fsharp-wasm was copied per consumer (`Interop.fs` here, `Collections.props` + a second list in the compiler repository, an empty `fsharp-wasm-core`) | one library, [`fsharp-wasm-core`](https://github.com/fsharp-wasm/fsharp-wasm-core) (`FSharp.Wasm.Core`); `Directory.Build.props` names its directory once (`FSharpWasmCoreDir`), `FSharp.Math` imports its compile list, the browser host includes its `Interop.fs` |
| `Png.fs` in `Host` and in `GoldenTests` (same encoder, other module name and argument order) | `GoldenTests` references `Host` |

## Open — the monolith `src/FSharp.Graphics/` against the split projects

`src/FSharp.Graphics/` (16 files, 2 465 lines) is the first, single-assembly version. Only `tests/FSharp.Graphics.Tests`
references it; everything else (host, demos, the browser build) uses `Core`, `Primitives`, `Scene`, `SceneDsl`, `Render`,
`Tea`, `Widgets`. It is **not** a pure duplicate: 120 of its 369 top-level names exist nowhere in the split projects.

| monolith | split project | similarity |
|---|---|---|
| `Scene/Diff.fs` | `Render/Diff.fs` | 0.97 |
| `Layout/Layout.fs` (275 lines) | `Scene/Layout.fs` (186) | 0.79 |
| `Scene/Scene.fs` | `Scene/Scene.fs` | 0.79 |
| `Render/Encode.fs` (262) | `Render/Encode.fs` (193) | 0.74 |
| `Core/Paint.fs`, `Core/Types.fs`, `Core/ISdfShape.fs` | `Core/…`, `Primitives/ISdfShape.fs` | 0.66 / 0.56 / 0.40 |
| `Tea/Runtime.fs` (277) | `Tea/Runtime.fs` (114) | 0.48 |

What only the monolith has: `TextNode` with rich text, caret and selection (`TextInput`, `RichText`, `selectionRects`,
`caretPosition`), the layout result type (`LayoutResult`, alignment), the opcode decoder (`InputDecoder`), `Tea.Model`.
Before it can go, those have to be ported to the split projects or dropped on purpose — and the 1 400 lines of tests
(`Tests.fs`, `PropertyTests.fs`) move with them. `src/FSharp.Graphics.Wasm/` (a single-project bundle that links the sources)
and `build/build-wasm.sh` are the same story one level up: the script names paths that no longer exist
(`src/Fable.WasmGc.fsproj`, `tools/fsharp-wasm-cli`); `build/build-browser.sh` compiles the split projects directly.

## Open — GlyphAura (separate repository, MIT) against `Primitives` / `Core`

GlyphAura started as the text engine and carries its own copies of the drawing primitives:

| GlyphAura | here | similarity |
|---|---|---|
| `Shapes.fs` (192) | `Primitives/Shapes.fs` (240) | 0.88 |
| `TextNode.fs` (198) | monolith `Text/TextNode.fs` (270) | 0.81 |
| `DrawList.fs` (159) | `Primitives/DrawList.fs` (78) | 0.66 |
| `ShapeCache.fs` (122) | `Primitives/ShapeCache.fs` (30) | 0.32 |
| `Types.fs` (98) | `Core/Types.fs`, `Core/TextTypes.fs` | — |
| `PropertyTests.fs`, `Tests.fs` | monolith tests | 0.85 / 0.81 |

`Host` depends on GlyphAura (fonts, shaping), so GlyphAura cannot depend on this repository without a cycle between the two
repositories, and `Primitives` has to stay free of GlyphAura (it is compiled to WasmGC; GlyphAura uses SIMD). The direction
that works: the shared primitives live in one place that both reference — `Primitives` and `Core` — and GlyphAura keeps only
the shaping, `FontProvider`, `LayoutEngine` and the adapter. That is a change in two repositories and a decision about which
copy is canonical for each file; it is not done here.

## Open — golden tests

`GoldenTests/Rasterizer.fs` (461 lines) is a copy of the host's software rasterizer (`Host/Raster.fs`). `ARCHITECTURE.md` §14
says to switch to the host and regenerate the masters; today 5 of 18 golden images differ (13 pass).
