#!/usr/bin/env bash
# build-browser.sh — compile the scene gallery and the chart demo (each draws onto one <canvas>) to WasmGC.
#
#   bash build/build-browser.sh [--out dist/browser]
#
# Result: <out>/SceneGallery.wasm (the thirteen scenes of demo/FSharp.Graphics.Scenes, demo/FSharp.Graphics.Browser.Gallery) and <out>/ChartDemo.wasm (the four charts of
# FSharp.Graphics.Charts, demo/FSharp.Graphics.Browser.Charts).
# The page that runs it, and the glue it imports (runtime/gfx.js), are in fsharp-wasm-runtime (demos/graphics-gallery); that
# repository builds the project itself when it is checked out next to this one.  The widget editor, another module on the same
# canvas host (FSharp.Graphics.Canvas), lives in fsharp-wasm-ide.
#
# The compiler, first hit wins:
#   FSHARP_WASM_BIN   a release binary of fsharp-wasm (it carries FSharp.Wasm.Core beside it)
#   tools/fsharp-wasm the pinned compiler submodule:  git submodule update --init --recursive tools/fsharp-wasm tools/fsharp
#                     (needs FSHARP_COMPILER = the patched fsc.dll built from tools/fsharp, see the compiler's README)
# FSHARP_WASM_CORE_DIR points the build at another checkout of fsharp-wasm-core.  `wasm-tools` must be on the PATH.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/dist/browser"
while [[ $# -gt 0 ]]; do
  case $1 in
    --out) OUT="$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done
command -v wasm-tools >/dev/null || { echo "wasm-tools not found (cargo install wasm-tools)" >&2; exit 1; }

# project directory : module the compiler names after the last file of the project
HOSTS=("FSharp.Graphics.Browser.Gallery:SceneGallery" "FSharp.Graphics.Browser.Charts:ChartDemo")
mkdir -p "$OUT"

if [[ -z "${FSHARP_WASM_BIN:-}" ]]; then
  CLI="$ROOT/tools/fsharp-wasm/tools/FSharp.Wasm.Cli"
  [[ -d "$CLI" ]] || { echo "no compiler: set FSHARP_WASM_BIN, or init tools/fsharp-wasm" >&2; exit 1; }
fi

for host in "${HOSTS[@]}"; do
  PROJECT="$ROOT/demo/${host%%:*}"
  MODULE="${host##*:}"
  if [[ -n "${FSHARP_WASM_BIN:-}" ]]; then
    "$FSHARP_WASM_BIN" "$PROJECT" --outDir "$OUT" --noCache
  else
    dotnet run -c Release --project "$CLI" -- "$PROJECT" --outDir "$OUT" --noCache
  fi
  wasm-tools validate "$OUT/$MODULE.wasm" --features all
  echo "built: $OUT/$MODULE.wasm ($(wc -c < "$OUT/$MODULE.wasm") bytes)"
done
