# fsharp-wasm-graphics

<!-- status -->
[![CI](https://github.com/fsharp-wasm/fsharp-wasm-graphics/actions/workflows/ci.yml/badge.svg?branch=develop)](https://github.com/fsharp-wasm/fsharp-wasm-graphics/actions/workflows/ci.yml) · [Status of every repository](https://fsharp-wasm.github.io/status/)
<!-- /status -->

<!-- platform -->
> **Part of the [fsharp-wasm platform](https://fsharp-wasm.github.io/projects/)**: graphics and text. Builds on [core](https://github.com/fsharp-wasm/fsharp-wasm-core), [glyph-aura](https://github.com/fsharp-wasm/fsharp-glyph-aura). Used by [ide](https://github.com/fsharp-wasm/fsharp-wasm-ide), [backend](https://github.com/fsharp-wasm/fsharp-wasm-backend) (the canvas client), [runtime](https://github.com/fsharp-wasm/fsharp-wasm-runtime) (the scene gallery).
> [All projects](https://fsharp-wasm.github.io/projects/) · [Roadmap](https://fsharp-wasm.github.io/docs/roadmap/) · [Tests](https://fsharp-wasm.github.io/tests/) · [Benchmarks](https://fsharp-wasm.github.io/benchmarks/) · [Live demos](https://fsharp-wasm.github.io/demos/)

A retained-mode 2D graphics stack in F#: vectors and colours, an SDF primitive library, a scene graph with layout and text, a
widget library, a TEA runtime, and hosts that draw the result. It is written for [fsharp-wasm](https://github.com/fsharp-wasm/fsharp-wasm)
(F# → WebAssembly GC) and has no third-party dependency in its libraries: the base is `FSharp.Math` and
[`FSharp.Wasm.Core`](https://github.com/fsharp-wasm/fsharp-wasm-core), text shaping comes from
[GlyphAura](https://github.com/fsharp-wasm/fsharp-glyph-aura).

```text
Model ──View──▶ Scene ──Resolve──▶ positioned Scene ──Emit──▶ DrawCommand list ──▶ a host (canvas, software rasterizer, Skia)
```

| project | what it is |
|---|---|
| `FSharp.Math` | `Vec2/3/4`, `Rect`, `Mat3x2`, `Mat4x4`, `Color`, Bézier |
| `FSharp.Graphics.Core` | draw types: paint, stroke, shadow, text types, `ITextEngine` |
| `FSharp.Graphics.Primitives` | SDF shapes, the draw list, a shape cache |
| `FSharp.Graphics.Scene` | the `Scene` tree, layout, resolve, hit testing |
| `FSharp.Graphics.SceneDsl` | computation-expression builders for scenes |
| `FSharp.Graphics.Canvas` | the canvas host: `Gfx` imports of `runtime/gfx.js`, text engine, command sink, `CanvasHost` (the frame plumbing of a module that draws onto one canvas) |
| `FSharp.Graphics.Render` | the command encoder/decoder (protocol v2), diffing |
| `FSharp.Graphics.Tea` | the Elmish-style runtime and commands |
| `FSharp.Graphics.Widgets` | buttons, fields, sliders, toggles, a strip of tabs, a grid for tables far larger than the screen (it draws what is in the window), theme |
| `FSharp.Graphics.Interact` | where a drag lands: slots, the drop a pointer position means (before, after, into), the drag as a session (by the pointer, and by the keyboard: the places in reading order, an arrow key to the next), scrolling near an edge. Pure geometry on `FSharp.Math`: the canvas and the DOM share it |
| `FSharp.Graphics.Charts` | charts of a size that a screen cannot show point by point: a minimum and maximum pyramid (the range of any run of samples in O(log n), appended to one sample at a time), a summed-area table for density maps, scales and ticks, and the scenes of lines, bands, candles, bars and heat maps, with a crosshair and a legend. A frame costs the pixels of the chart, not the points of the data ([docs/charts.md](docs/charts.md)) |
| `FSharp.Graphics.Icons` | the icons of `FSharp.Wasm.Icons` (Lucide, typed shapes on a 24 x 24 grid) on a canvas: SVG path data read and flattened (`PathData`), an icon as round-ended strokes of one colour (`IconScene.view`), [docs/icons.md](docs/icons.md). Takes the core library as the submodule `lib/fsharp-wasm-core`. |
| `FSharp.Graphics.Host` | fonts, the software rasterizer, a PNG encoder (all own code) |
| `FSharp.Graphics.Host.Skia` | a Skia host (needs SkiaSharp; optional, not packed) |

`demo/` holds the demos, among them the scene gallery (`demo/FSharp.Graphics.Browser.Gallery`, the thirteen scenes of
`FSharp.Graphics.Scenes`). `bash build/build-browser.sh` compiles it to WasmGC; it draws onto one canvas through
`FSharp.Graphics.Canvas` and `runtime/gfx.js` of fsharp-wasm-runtime, whose pages run it live. The widget editor, which draws through
the same canvas host, is in [fsharp-wasm-ide](https://github.com/fsharp-wasm/fsharp-wasm-ide).
`docs/consolidation.md` lists what is still duplicated and the order to remove it.
`docs/performance.md` has what was measured of the canvas stack (what the builders cost, what a frame costs, why an idle page draws nothing) and what is next.

## Build, test, pack

```bash
git clone --recurse-submodules https://github.com/fsharp-wasm/fsharp-wasm-graphics.git
dotnet build FSharp.Graphics.slnx -c Release
dotnet run --project tests/FSharp.Math.Tests -c Release
dotnet run --project tests/FSharp.Graphics.HostTests -c Release
bash build/pack.sh            # → dist/nupkg/*.nupkg (the library projects; each also carries its sources for fsharp-wasm)
```

Each library is a NuGet package of the same name (the dll, its XML documentation, and the sources under `fable/`). Not on nuget.org yet (checked 2026-11-27). A package builds
on the packages of [fsharp-wasm-core](https://github.com/fsharp-wasm/fsharp-wasm-core) (`FSharp.Wasm.Icons` for `FSharp.Graphics.Icons`) by the same version, so a release is
tagged there first. `bash build/smoke-pack.sh <version> <folder of core's packages>` restores the packages in a new project and runs it; `.github/workflows/release.yml` does
that and the pack for a tag `vX.Y.Z` on `main`, pushes to nuget.org when the secret `NUGET_API_KEY` is set (and nuget.org has core at that version), and attaches the packages to a
GitHub release. Run by hand it is a dry run that publishes nothing.

## Contributing

Welcome. [CONTRIBUTING](https://github.com/fsharp-wasm/.github/blob/develop/CONTRIBUTING.md) is the organisation's guide and [AGENTS.md](AGENTS.md) says how the platform is worked on (the compiler comes first: a fault found here
is fixed in the compiler, not worked around here).

- **Run the tests:** `dotnet run --project tests/FSharp.Math.Tests -c Release` and `dotnet run --project tests/FSharp.Graphics.HostTests -c Release`.
- **A good first contribution:** A widget or a scene that draws wrongly, with the scene that shows it; golden pictures are in `golden/`. Issues labelled [`good first issue`](https://github.com/fsharp-wasm/fsharp-wasm-graphics/labels/good%20first%20issue) are the places to start.
- **A change** goes to `develop` as a pull request, with a test that fails without it. Open an issue first for anything larger than a fix.

## License

[Apache License 2.0](LICENSE); see [NOTICE](NOTICE) for the components that keep their own.
