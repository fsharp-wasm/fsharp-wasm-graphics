# Dashboards and SCADA

Written 2026-11-26.  What a dashboard needs, what the stack has, what is missing, and where each missing part goes.  A dashboard is a screen of instruments over live values; SCADA is the
same with alarms, a history and a picture of the plant (a mimic) that the operator acts on.  The design keeps the rule of the platform: the lowest layer all its users can reach, and
nothing written twice.

## What exists

| Need | What the stack has | Where |
|---|---|---|
| Trends of millions of samples, a live stream, candles, a heat map of points, bars | `FSharp.Graphics.Charts` (`Pyramid`, `DensityGrid`, `Line`, `Candles`, `Bars`, `Heatmap`, axes, legend, crosshair) | this repository, `docs/charts.md` |
| A table of a million rows, tabs, forms, buttons, sliders, a progress bar | `Grid`, `Tabs`, `Widgets` (`ProgressBar`, `Slider`, `Toggle`, `Dropdown`) | `src/FSharp.Graphics.Widgets` |
| Shapes with a stroke, a fill and a shadow, a scene that picks and routes events, animation | `Scene`, `Primitives`, `Hit`, `Animated` | `src/FSharp.Graphics.Scene`, `Primitives` |
| Values that change and what depends on them, derived tables, a history of edits | `FSharp.Wasm.Graph`, `Live`, the patches of the data graph | fsharp-wasm-core, `docs/data-graph.md` |
| Many programs that restart each other, a pool of workers | `FSharp.Wasm.Actors` (supervisor, pool) | fsharp-wasm-core |
| The same checks on every host (Node, Cloudflare, …), the JS side of a server | `fsharp-wasm-edge` | fsharp-wasm-edge |
| Accessible names and roles for what is drawn on a canvas | `Semantic`, `Parts.describe` | `docs/accessibility.md` |

## What is missing

1. **The value itself**: a tag with a quality (good, uncertain, bad), limits, and an alarm that has a life (raised, acknowledged, returned, cleared), a history that is kept small.
   Not a graphics matter: it is data and logic that a server, a test and a screen all need.  → `FSharp.Wasm.Telemetry` in fsharp-wasm-core (**written**, 173 tests under .NET and as WasmGC).
2. **Instruments**: gauge (an arc with zones), bar and tank (a level with limits), sparkline (a trend with no axes), lamp (a state), KPI card (a number, its change and a sparkline), a
   threshold band on a chart, an alarm list.  → `Instruments` in `FSharp.Graphics.Charts`, built on the scene (arcs are polygons, so there is no new primitive to draw).  **Written
   2026-11-26: the gauge, the level, the lamp, the sparkline and the key figure** (`Instruments.gauge`, `level`, `lamp`, `sparkline`, `kpi`; 14 checks under .NET and as WebAssembly
   GC).  The colour of a value is a `Tone` (normal, warning, alarm, fault) that the app takes from `Telemetry.Level` and the quality of the reading; a reading that is not a number
   draws no needle and no arc, says a dash and says "not trusted", and every instrument says its state in words to assistive technology.  Still to come: the threshold band on a chart
   and the alarm list.
3. **Diagrams**: a process mimic (symbols of a pump, a valve, a tank, a pipe whose colour and flow follow the values) and node-link diagrams (a flow chart, an org chart, a network).  The
   layout of a graph is data (layers, positions, edge routes): → `FSharp.Graphics.Diagrams`, with the layout in a pure module so that it is tested with no scene.
4. **A dashboard shell**: a grid of panels that the operator arranges (drag, resize), saved as data.  It builds on `Interact` (areas, drag sessions, drop zones) and on `Edit`; it does not
   repeat them.
5. **The way in**: protocols that deliver readings.  OPC UA, MQTT and Modbus are not written in F#; a gateway runs on an edge host (`fsharp-wasm-edge`), turns what it receives into `Sample`s
   and sends them as patches of the data graph (the same patches that carry edits), so a screen of ten thousand tags paints what changed.  A write to the plant (a set point, an
   acknowledgement) is a patch in the other direction, checked on the server.  → gateways in fsharp-wasm-edge, one adapter per protocol, behind one contract.
6. **A demo that is honest**: a simulated plant (a few tanks, pumps and a valve, with noise and a fault that happens) driving the instruments, the mimic, the alarm list and a trend, with
   the numbers of the measured frame times.  → `demos/` of fsharp-wasm-runtime.

## Order

1. `Telemetry` (done): the alarm, the historian.
2. `Instruments`: gauge, level, lamp, sparkline, key figure (**done**, each with a test on the scene it makes, no pixels); the band of limits on a chart and one page that shows them all (open).
3. `Diagrams`: the layout first (tested as data), then the mimic symbols, then the live demo.
4. The simulated plant and the alarm list, as the demo of 2 and 3 together.
5. The dashboard shell with arranging and saving.
6. The first gateway (MQTT, the simplest; then OPC UA).

Each step is a commit of its own, with its tests under .NET and as WasmGC; a fault the compiler shows on the way is fixed in the compiler first (the first one was `string` of a union case,
found by the tests of the alarm).

## Not decided

- Whether the mimic symbols are a set of our own (drawn as scenes) or an imported one (ISA-5.1 / ISO 14617 symbols): a set that is imported needs a licence that fits Apache-2.0 (see
  `AGENTS.md`, 10).  Our own is the default.
- How long a history is kept in the browser and where the rest lives: the historian's compression decides what is kept, a server decides for how long.
