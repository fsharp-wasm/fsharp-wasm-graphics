# fsharp-wasm-graphics

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
| `FSharp.Graphics.Render` | the command encoder/decoder (protocol v2), diffing |
| `FSharp.Graphics.Tea` | the Elmish-style runtime and commands |
| `FSharp.Graphics.Widgets` | buttons, fields, sliders, toggles, theme |
| `FSharp.Graphics.Host` | fonts, the software rasterizer, a PNG encoder (all own code) |
| `FSharp.Graphics.Host.Skia` | a Skia host (needs SkiaSharp; optional, not packed) |

`demo/` holds the demos, among them the GUI builder and its browser host (`demo/FSharp.Graphics.Browser`) and the scene gallery
(`demo/FSharp.Graphics.Browser.Gallery`, the thirteen scenes of `FSharp.Graphics.Scenes`). `bash build/build-browser.sh` compiles both
to WasmGC; they draw onto one canvas through `runtime/gfx.js` of fsharp-wasm-runtime, whose pages run them live.
`docs/consolidation.md` lists what is still duplicated and the order to remove it.

## Build, test, pack

```bash
git clone --recurse-submodules https://github.com/fsharp-wasm/fsharp-wasm-graphics.git
dotnet build FSharp.Graphics.slnx -c Release
dotnet run --project tests/FSharp.Math.Tests -c Release
dotnet run --project tests/FSharp.Graphics.HostTests -c Release
bash build/pack.sh            # → dist/nupkg/*.nupkg (the library projects; each also carries its sources for fsharp-wasm)
```

## License

[Apache License 2.0](LICENSE); see [NOTICE](NOTICE) for the components that keep their own.
