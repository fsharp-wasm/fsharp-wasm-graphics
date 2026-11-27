#!/usr/bin/env bash
# smoke-pack.sh — the packages as a consumer sees them: a new project that restores them from dist/nupkg (and nothing else of this repository) and runs.
#
#   bash build/smoke-pack.sh <version> [folder of the packages of fsharp-wasm-core]      # after `bash build/pack.sh <version>`
#
# It checks what a unit test cannot: that FSharp.Graphics.Icons restores with its dependencies (the other libraries of this repository and FSharp.Wasm.Icons of
# fsharp-wasm-core, by the same version), that FSharp.Graphics.Charts.Live restores with FSharp.Wasm.Graph and runs a chart on a document, that the dlls load, and that
# the sources are in the package where the compiler looks for them (fable/).  The packages of
# fsharp-wasm-core come from nuget.org once they are released; before that, name the folder `bash lib/fsharp-wasm-core/build/pack.sh <version>` made.  The packages
# cache is the temporary folder's own, so a package of the same version from an earlier run cannot be what is tested.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="${1:?usage: smoke-pack.sh <version> [folder of the packages of fsharp-wasm-core]}"
CORE_SOURCE="${2:+<add key=\"core\" value=\"$(cd "$2" && pwd)\"/>}"
DIR="$(mktemp -d)"; trap 'rm -rf "$DIR"' EXIT
export NUGET_PACKAGES="$DIR/packages"
cd "$DIR"
cat > nuget.config <<XML
<configuration><packageSources><clear/><add key="local" value="$ROOT/dist/nupkg"/>$CORE_SOURCE<add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
XML
cat > Smoke.fsproj <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.fs" />
    <PackageReference Include="FSharp.Graphics.Icons" Version="$VERSION" />
    <PackageReference Include="FSharp.Graphics.Charts.Live" Version="$VERSION" />
    <PackageReference Include="FSharp.Graphics.Diagrams" Version="$VERSION" />
  </ItemGroup>
</Project>
XML
cat > Program.fs <<'FS'
open FSharp.Math
let v = Vec2.Create(3.0f, 4.0f)
if v.Length <> 5.0f then (eprintfn "Vec2.Length is %f, not 5" v.Length; exit 1)
printfn "%s on a grid of %A" (System.Reflection.Assembly.Load("FSharp.Graphics.Icons").GetName().Name) FSharp.Wasm.Icons.Icons.grid
// a chart that follows a column of a document: three samples appended, three in the pyramid
let doc = FSharp.Wasm.Graph.Doc(1)
let node = ref 0
doc.Edit("make", fun tx -> node.Value <- tx.Create "series") |> ignore
let samples = FSharp.Graphics.Charts.Live.Live.samples doc node.Value "y"
doc.Edit("append", fun tx -> tx.Append(node.Value, "y", [| 1.0; 2.0; 3.0 |])) |> ignore
if samples.Peek.Count <> 3 then (eprintfn "the pyramid holds %d samples, not 3" samples.Peek.Count; exit 1)
// a diagram of two boxes and a link: two layers
let layout =
    FSharp.Graphics.Diagrams.Layered.layout FSharp.Graphics.Diagrams.Direction.TopDown FSharp.Graphics.Diagrams.Spacing.standard
        [| { Id = 1; W = 60.0f; H = 30.0f }; { Id = 2; W = 60.0f; H = 30.0f } |] [| { From = 1; To = 2 } |]
if layout.Routes.Length <> 1 then (eprintfn "the diagram has %d lines, not 1" layout.Routes.Length; exit 1)
FS
dotnet run -c Release --nologo -v q
for id in FSharp.Math FSharp.Graphics.Scene FSharp.Graphics.Icons FSharp.Graphics.Charts.Live FSharp.Graphics.Diagrams; do
  lower="$(echo "$id" | tr '[:upper:]' '[:lower:]')"
  ls "$NUGET_PACKAGES/$lower/$VERSION/fable/"*.fsproj > /dev/null || { echo "$id: no sources under fable/" >&2; exit 1; }
done
echo "smoke: the packages restore, load and carry their sources"
