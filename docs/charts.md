# Charts

`FSharp.Graphics.Charts` draws charts of a size that a screen cannot show point by point: a line of ten million samples, a density map of five million points, two hundred thousand candles.
A frame costs the pixels of the chart and not the points of the data. The scenes it makes are ordinary scenes of the scene graph (rectangles, polygons, text), drawn by whichever host draws
the rest (`FSharp.Graphics.Canvas` and `runtime/gfx.js`); the data structures under them are plain arrays and numbers with no scene in them, so they run (and are tested) under .NET and as WebAssembly GC.

The demo is `demo/FSharp.Graphics.Browser.Charts` (page and test: `demos/charts` of fsharp-wasm-runtime).

## The idea

A screen is about a thousand pixel columns wide. Whatever the data, a line in a plot that wide needs, for each column, the smallest and the largest sample the column covers: a line through
all of them covers exactly the band between those two. Finding them by reading the samples is O(n) per frame; the library answers in O(log n) per column.

| Structure | Answers | Cost of a question | Cost to make |
|---|---|---|---|
| `Pyramid` | the smallest and largest of samples *l*…*r* | O(log n) | one pass over the samples; an appended sample costs O(1) on average |
| `DensityGrid` | the number of points in a rectangle of cells | four reads | one pass over the points |

A pyramid keeps the minimum and maximum of every aligned block of 2, 4, 8 … samples; a run of samples is the join of at most 2 log n blocks, found bottom up as in an iterative segment tree. Only
complete blocks exist, so a stream can be drawn while it grows (`Append`), and `Clear` starts a recording again. A density grid is a grid of counts with its summed-area table, over the extent
of the data: the count of any rectangle of cells is `S(c1,r1) - S(c0,r1) - S(c1,r0) + S(c0,r0)`, so a screen of cells costs the same at every zoom (zooming in further than a cell shows the cells).

An extreme is never lost: a spike one sample wide among ten million is in the range of its pixel column, so it is on the screen at every zoom (tested, and checked in the browser).

## Parts

```fsharp
open FSharp.Graphics.Charts

// the samples: x = X0 + i * Dx
let series = { Name = "sensor"; Color = theme.Palette.[0]; Width = 1.6f; Fill = false
               Data = Pyramid.OfArray samples; X0 = 0.0; Dx = 1.0 }

// what the plot shows: x from 0 to n, y fitted to the samples in view
let frame = Frame.create width height 76.0f 62.0f 20.0f 36.0f (0.0, float n) (Line.yRange [ series ] (0.0, float n) 0.08)

let scene =
    Group [ Parts.axes theme frame (fun step v -> Format.tick step v) (fun step v -> Format.tick step v)
            Line.scene frame series
            Parts.legend theme 76.0f 12.0f [ ("sensor", series.Color) ] ]
```

| Module | What it is |
|---|---|
| `Linear`, `Domain`, `Ticks`, `Format` | the map between data and pixels, zoom and pan with limits (the value under the pointer stays under it), "nice" ticks (1, 2, 5 times a power of ten), numbers as text (`1.5M`, `10 000 000`) |
| `Pyramid`, `Span` | the minimum and maximum pyramid (`Range`, `Columns`, `Append`, `AppendMany`, `Clear`, `OfArray`) |
| `DensityGrid`, `Heat` | the density grid (`Build`, `Sum`, `Window`) and the colour levels of a raster with the runs of equal level (one rectangle each) |
| `Geometry` | the polygons: `ribbon` (a line `width` pixels wide along points, mitered up to three half widths, then cut), `band` (the top edge through the largest samples and back through the smallest), `area` |
| `Line` | a line or area: a band when a column holds 1.5 samples or more, a ribbon through the samples (with a dot on each when they are far apart) below that |
| `Candles` | open, high, low, close: zoomed out a column is the open of its first bar, the close of its last, the highest high and the lowest low (two pyramids), zoomed in each bar |
| `Bars`, `Heatmap` | bars over named categories; the density map as rectangles |
| `Parts` | axes and grid, legend, crosshair with a card, `describe` (the chart as an image with a name and a description for assistive technology) |
| `Theme`, `Frame` | the colours and the font; the plot rectangle on whole pixels with its two scales |

## Measured

2026-11-26, headless Chromium without a GPU (software canvas), one container; the numbers move by about 15 % from run to run. The page of the demo has the benchmark (`Run the benchmark`), and
`tests/e2e/charts.mjs` of fsharp-wasm-runtime runs it in CI and checks that the pyramid and a pass over every sample find the same columns exactly.

| What | Size | Time |
|---|---|---|
| Build the pyramid | 1 000 000 samples / 10 000 000 samples | 15 ms / 150 to 170 ms |
| Append to a growing pyramid, one sample at a time | 1 000 000 samples | 20 ms (50 million a second) |
| One frame of the line: the columns of a plot 1 000 pixels wide | over 10 000 000 samples | **123 to 150 µs** with the pyramid, 12.5 to 14.3 ms reading every sample (about 100 times) |
| The scene of the line (the polygons of the band) | 1 000 pixels wide | 0.19 to 0.30 ms |
| Build the density grid | 5 000 000 points on 1 024 × 1 024 cells | 60 ms |
| The scene of the density map | a plot 800 pixels wide, cells of 3 pixels | 2.8 to 3.4 ms, 13 900 rectangles |

What the page spends on a frame (the whole of it on the main thread: build, resolve, emit, compare with the last frame, the canvas calls; median and 95th percentile of the last 120 frames drawn):

| Chart | the pointer moves over it (crosshair and card) | wheel and drag |
|---|---|---|
| Ten million samples | 1.3 ms (1.8) | 0.8 ms (1.5) |
| A stream, 100 000 samples a second per series | 1.5 ms (2.1) every frame | 1.2 ms (1.5) |
| Two hundred thousand candles | 1.8 ms (3.3) | 0.9 ms (6.2) |
| Five million points | 8.3 ms (11.2) | 0.6 ms (15.4) |

Two things the numbers do not hide. A density map of 14 000 rectangles is more than a software canvas fills in one frame (about 80 ms), so a drag or a wheel draws it in cells of 6 pixels and the sharp
map follows when the view has stood still for 200 ms (the 95th percentile of 15 ms is that one frame); and a moving crosshair used to repaint all of it, because `Damage` merged regions that merely
cross (a vertical line and a horizontal one: their union is the plot): it merges the ones whose union wastes little now, and the hover cost 78 ms before and 19 ms after that change (8 ms with the map kept from
the frame before). A GPU canvas will be faster; this one is what CI can measure.

## What it does not do yet

- x is regular (`X0 + i * Dx`); samples at irregular x need a binary search from pixel to index, which the pyramid does not yet have a front for.
- No gaps for missing samples in a line (NaN is not handled by the pyramid). Logarithmic and time axes, histograms, scatter plots, rasters and contours are in [science-charts.md](science-charts.md).
- A ribbon can overlap itself at a sharp turn; the Canvas host fills it with the nonzero rule, which is right. The WebGPU host draws simple polygons only, so a ribbon with a hairpin may be drawn wrong
  there (the host counts what it cannot draw: `gpuStats().unsupported`).
- The legend does not toggle series, and there is no selection or brush yet.

## Tests

`tests/FSharp.Graphics.ChartsTests` (plain checks, `dotnet run`): the pyramid against a loop over the samples for every size around a power of two, built in one pass and by appending, with half of it
filled, cleared and filled again; a spike in a million samples; the columns against a loop; the density grid against a count of the points, the raster of the whole extent against the total; the
polygons against their definition (a right angle is mitered, a hairpin is cut, a repeated point does not break the line); and the scenes (a million samples are two polygons of at most 2 × 720
vertices). The browser test `tests/e2e/charts.mjs` of fsharp-wasm-runtime drives the demo: zoom keeps the point under the pointer, a drag moves the data by the distance of the pointer, the spike
is on the screen at the widest zoom, a stream grows and is followed, and the benchmark finds the same columns as a pass over every sample.
