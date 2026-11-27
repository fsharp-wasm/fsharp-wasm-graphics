# Science charts, live charts and diagrams

What `FSharp.Graphics.Charts` has beyond the line, the candles, the bars and the density map (see [charts.md](charts.md)), and the two libraries that build on it:
`FSharp.Graphics.Charts.Live` (charts on the data graph of fsharp-wasm-core) and `FSharp.Graphics.Diagrams` (node-link diagrams).
Everything is a scene of the scene graph, so a chart is drawn by the canvas hosts the same way a widget is, picked and described for assistive technology the same way, and runs
under .NET and as WebAssembly GC.

## Science charts (`FSharp.Graphics.Charts`)

| module | what it draws | the data structure behind it |
|---|---|---|
| `Colormap` | viridis, grayscale and a diverging map (coolwarm); `Colormap.at`, `scaled`, `levels`, `ramp` | anchors at equal steps, blended in sRGB; NaN has a colour of its own (`Missing`) |
| `Axis.Log` | a logarithmic axis: `create`, `map`, `invert`, `ticks` (1, 2 and 5 of each decade over a few decades, the powers of ten over many), `label` | a record of two domain ends and two range ends |
| `Axis.Time` | a time axis in UTC: ticks at whole seconds, minutes, hours, days, months and years, labelled by what changes | civil dates by integer arithmetic (no `DateTime`, so it is the same in WebAssembly) |
| `Stats` | `quantile` (the type 7 of R and NumPy), `mean`, `stddev`, `box` (Tukey's whiskers at 1.5 IQR, outliers) | a sorted copy |
| `Histogram` | counts in equal bins, underflow and overflow apart; adding is O(1) | an array of counts: it follows a stream |
| `Scatter` | a marker per point while at most 20 000 are in view, a density map of all of them beyond | `DensityGrid` (a summed-area table) |
| `Bands`, `ErrorBars` | the area between two curves; symmetric error bars | the samples |
| `BoxPlot` | median, quartiles, whiskers and outliers of several samples side by side | `BoxStats` |
| `HistogramBars` | the bars of a `Histogram` | the counts |
| `Raster` | a field of values as coloured cells on a colour map; a row of cells of one colour is one rectangle (64 colours) | the field, downsampled to the pixels |
| `Contour` | iso-lines of a field (marching squares, the segments joined into lines by the edge they share) | the field |
| `ColorBar` | the colour map with its scale, to stand next to a raster or contours | the map |

Where there is a choice, the choice is the one that a plot of the same data in matplotlib makes (quantile type 7, whiskers at 1.5 IQR, viridis), so that a figure that is checked
against a reference is checked against the same numbers.

### What the tests say (`tests/FSharp.Graphics.ChartsTests`, `dotnet run`)

The colour maps against their anchors and the order of their lightness (viridis grows), the log ticks against the decades, the time ticks against calendar dates (leap years, the end of the
month, the turn of the year), the quantiles and the box against values worked out by hand, the histogram against a count of the samples, the raster of a field against its cells, the contours
against the field they come from (a line is closed or ends at the edge; a cell with a NaN corner has none) and the scenes (shapes per chart).

## Charts on the data graph (`FSharp.Graphics.Charts.Live`)

A column of a document (`FSharp.Wasm.Graph.Doc`) is the data of a chart, and the chart is a calc of the incremental engine:

```fsharp
let doc = Doc(1)
let node = ref 0
doc.Edit("make", fun tx -> node.Value <- tx.Create "series") |> ignore

let view    = Live.viewport doc frame                      // a source: the pan, the zoom, the size
let samples = Live.samples doc node.Value "y"              // the column as a pyramid that grows
let counts  = Live.histogram doc node.Value "y" -100.0 100.0 20
let chart   = Live.stack doc [ Live.line doc view samples look
                               Live.bars doc view counts green ]

doc.Edit("append", fun tx -> tx.Append(node.Value, "y", newSamples))   // chart.Value is the scene now
Live.show doc view otherFrame                                          // a pan: the scenes only
```

What the engine does, measured with its own count (`Flushed`, `tests/FSharp.Graphics.ChartsLiveTests`):

- an **append** of samples reaches the calcs that read the column (the pyramid, the histogram), the scenes that read those, and the stack: five calcs, each run once, and the pyramid and the
  histogram take the new samples only;
- a **pan or a zoom** (`Live.show`) reaches the scenes and the stack: three calcs, and the pyramid and the histogram are not reached (their cost does not depend on the viewport);
- the **same frame again** is no change: nothing runs;
- an **undo** of an append (`Timeline`), a patch from another document and the inverse of one move the chart the same way as an edit of its own; a column that was written over or truncated
  starts its state again and takes the column from the first number;
- two charts on one column follow it, each with a state of its own.

`Live.column` is the one function that follows a column (`create` a state, `take` what an append brought); `samples` (a `Pyramid` that grows, with its float32 samples), `histogram`, `box` and
`field` are four uses of it. A scene is `derived`: a calc that reads the viewport and the data, so what it reads is what makes it run. `Live.field` reads a column and the registers `cols` and
`rows` of its node as a field for `Live.raster` and `Live.contours`.

## Diagrams (`FSharp.Graphics.Diagrams`)

`Layered.layout direction spacing boxes links` is the layered layout of a directed graph (Sugiyama): the links that close a cycle are turned round (a depth-first walk), a box gets the layer
after the last box that links to it (a box nothing links to is moved next to its nearest successor), a link over layers is cut by a point in each layer, the order in the layers is found by
sweeps of the barycentre (the order with the fewest crossings seen is kept, counted with a Fenwick tree), and the places in a layer are the least squares fit to the middle of the neighbours
that keeps the boxes apart (the pool-adjacent-violators solution of ordered regression, optimal for each layer). A box with several links has them leave and enter at different points of its
side. `Diagram.scene` draws the result with the theme of the charts (the links under the boxes, an arrow head at the end of each link, one picture for assistive technology); `Diagram.boxAt`
says which box is under a point.

Links to the same box, to a box that is not there and a link repeated are left out; two boxes that link to each other are one line drawn once.

### What the tests say (`tests/FSharp.Graphics.DiagramsTests`, `dotnet run`)

Against the promises of the layout, not against a drawing: every link goes with the layers (a turned one against them), no two boxes overlap, everything is inside the size, a chain stands in a
line, a link enters the box it points to, a binary tree of 31 boxes given in a scrambled order is drawn without a crossing, a graph of 400 boxes and 900 links with cycles in it is sound and
every link that can be drawn has a line, left to right is top down turned, and the same graph is laid out the same way.

Time, 2026-11-27 (one thread, .NET 10, a Release build, while another build ran on the machine, so read them as an upper bound): 100 boxes and 200 links 27 ms, 500 and 1000 links 327 ms,
2000 and 4000 links 1.4 s.

## What it does not do yet

- No time axis for samples at irregular x (the pyramid wants regular spacing, as in [charts.md](charts.md)).
- `Live` redraws a scene from the data in view; it does not keep the scene between two frames of the same viewport (the engine already does not run it again).
- The layout is for graphs that fit a screen at some zoom; a graph of tens of thousands of boxes wants the clustering of a graph, not a layering.
- Links are straight lines between the points of the layers, not curves; boxes have one size each.
