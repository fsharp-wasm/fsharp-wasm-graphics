#!/usr/bin/env bash
# pack.sh — NuGet packages of the library projects (the ones with IsPackable=true): dist/nupkg/<project>.<version>.nupkg
#
#   bash build/pack.sh [version]        # default: 0.1.0-preview (Directory.Build.props)
#
# Nothing is published; a release uploads what this makes.  Each package carries its sources under fable/ as well — a program
# compiled by fsharp-wasm compiles the libraries it uses together with its own code.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/dist/nupkg"
VERSION_ARG=()
[[ $# -gt 0 ]] && VERSION_ARG=("-p:Version=$1")
rm -rf "$OUT"; mkdir -p "$OUT"
# order = dependency order, so that every package finds the ones below it
for p in FSharp.Math FSharp.Graphics.Core FSharp.Graphics.Primitives FSharp.Graphics.Scene FSharp.Graphics.SceneDsl \
         FSharp.Graphics.Render FSharp.Graphics.Tea FSharp.Graphics.Widgets FSharp.Graphics.Canvas FSharp.Graphics.Host; do
  dotnet pack "$ROOT/src/$p/$p.fsproj" -c Release "${VERSION_ARG[@]}" -o "$OUT" --nologo -v q
done
ls -1 "$OUT"
