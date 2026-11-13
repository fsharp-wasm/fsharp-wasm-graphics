#!/usr/bin/env bash
# build-browser.sh — compile the browser host (the GUI builder, drawn onto one <canvas>) to WasmGC.
#
#   bash build/build-browser.sh [--out dist/browser]
#
# Result: <out>/GraphicsEditor.wasm.  The page that runs it, and the glue it imports (runtime/gfx.js), are in
# fsharp-wasm-runtime (demos/graphics-editor); that repository builds this project itself when it is checked out next to
# this one.
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

PROJECT="$ROOT/demo/FSharp.Graphics.Browser"
mkdir -p "$OUT"

if [[ -n "${FSHARP_WASM_BIN:-}" ]]; then
  "$FSHARP_WASM_BIN" "$PROJECT" --outDir "$OUT" --noCache
else
  CLI="$ROOT/tools/fsharp-wasm/tools/FSharp.Wasm.Cli"
  [[ -d "$CLI" ]] || { echo "no compiler: set FSHARP_WASM_BIN, or init tools/fsharp-wasm" >&2; exit 1; }
  dotnet run -c Release --project "$CLI" -- "$PROJECT" --outDir "$OUT" --noCache
fi

wasm-tools validate "$OUT/GraphicsEditor.wasm" --features all
echo "built: $OUT/GraphicsEditor.wasm ($(wc -c < "$OUT/GraphicsEditor.wasm") bytes)"
