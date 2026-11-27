# Dependencies

What the libraries of this repository (and of GlyphAura, which they use) depend on, measured on 2026-11-27 with

```bash
grep -rn --include=*.fsproj PackageReference .        # here and in fsharp-glyph-aura, outside lib/
```

## The packages are free of third-party code

Every packable library of this repository (`FSharp.Math`, `FSharp.Graphics.Core`, `Primitives`, `Scene`, `SceneDsl`, `Render`, `Tea`, `Widgets`,
`Interact`, `Charts`, `Icons`, `Canvas`, `Host`) and `GlyphAura` have **no `PackageReference` of their own**. What a package brings along is
`FSharp.Core` and the packages of the platform itself, by the same version: `FSharp.Wasm.Icons` (fsharp-wasm-core) for `Icons`, `GlyphAura`
(fsharp-glyph-aura) for `Host`, and the other libraries of this repository.

## Where third-party packages are, and why they stay there

| project | package | what for | in a package? |
|---|---|---|---|
| `FSharp.Graphics.Host.Skia` | SkiaSharp 2.88.8 | a raster backend on the desktop, behind the `Host` interfaces | no (not packable) |
| `FSharp.Graphics.DesktopRunner` (demo) | SkiaSharp 2.88.8 and its native assets, Silk.NET.Windowing and Input 2.22.0 | a desktop window for the demos | no |
| `FSharp.Graphics.Tests`, `FSharp.Math.Tests` | FsCheck 3.3.2 | property tests | no |
| `GlyphAura.Tests` | xunit 2.9.3, FsCheck.Xunit 2.16.6, Microsoft.NET.Test.Sdk 17.14.1, coverlet.collector 6.0.4 | tests | no |
| `GlyphAura.Demo` | SkiaSharp 3.119.2 | a demo that draws shaped text | no |

So nothing has to be reduced: the code that a program compiled by fsharp-wasm takes in, and the packages that a .NET program restores, have no
dependency that is not ours. A third-party package that would enter a library is a finding to discuss first (AGENTS.md: do not write it twice, and
put code in the lowest layer all its users can reach).

## The .NET base library

The WebAssembly build has only what `FSharp.Wasm.Core` provides (the `System.*` surface written in F#). The widget editor
(fsharp-wasm-ide, `tests/Dogfood/run.sh widget-editor` of the compiler) compiles the whole `FSharp.Graphics` stack as one module in the compiler's CI,
so a library that reached for a .NET API outside that surface stops the build there. `FSharp.Graphics.Host` is the one library that is .NET only
(`System.Buffers.Binary`, the PNG and font readers): the WebAssembly build never references it.
