/// The module that becomes ChartDemo.wasm: four charts of FSharp.Graphics.Charts on one canvas.  The exports are what runtime/gfx.js (`mount`) calls, plus the selection:
///   start()                         bind the canvas, make the first chart, announce the charts ("chart:<id>", title, what it covers)
///   onResize(w, h, ratio)           CSS size of the canvas and the device pixel ratio
///   onPointer(kind, x, y, button)   kind 0 down, 1 up, 2 move, 3 leave: a drag pans, a double press shows everything again
///   onWheel(dx, dy)                 zoom about the pointer
///   onKey(kind)                     kind 0 down, 1 up; ← → pan, ↑ ↓ zoom, Home shows everything, End follows a stream
///   frame(nowMs)                    build, resolve, draw — only when something changed, or while a stream runs
///   chartCount() / select(i) / selected()
/// and the kernels the benchmark times (`bench...`): the pyramid against a pass over every sample, one frame's scene, the density map.
module FSharp.Graphics.Browser.ChartDemo

open System
open FSharp.Math
open FSharp.Wasm.Graph
open FSharp.Graphics.Core
open FSharp.Graphics.Scene
open FSharp.Graphics.Canvas
open FSharp.Graphics.Charts
open FSharp.Graphics.Charts.Live
open FSharp.Graphics.Diagrams

let private theme = Theme.dark (FontId 0UL)

let private two (v: float) : string = Format.fixedPoint 2 v
let private ms (v: float) : string = Format.fixedPoint (if v < 10.0 then 1 else 0) v

/// What a tab of the demo is.  The demo owns the viewport (what part of the data is shown) and the pointer; a chart owns its data and says how it is drawn and read.
type IChart =
    abstract Id: string
    abstract Title: string
    abstract Covers: string
    /// Data arrives while it is shown: a frame is due every time.
    abstract Live: bool
    /// Make the data (once) and say what it cost.
    abstract Prepare: unit -> string
    /// What has arrived in this many milliseconds.
    abstract Step: float -> unit
    abstract XLimits: float * float
    /// The y range of a chart that is zoomed on both axes; None: y follows what is in view.
    abstract YLimits: (float * float) option
    abstract FitY: float * float -> float * float
    abstract XLabel: float * float -> string
    abstract YLabel: float * float -> string
    abstract Legend: unit -> (string * Color) list
    /// The plot; `moving` while the viewport is being changed (a drag, a wheel), when a chart may draw coarser and finish when it stops.
    abstract Plot: Frame * bool -> Scene
    /// What is under the pointer: rings to draw and lines for the card.
    abstract Probe: Frame * float * float -> (float32 * float32 * Color) list * (string * Color) list
    /// What the chart shows, in words (also what a screen reader is told).
    abstract Describe: Frame -> string

let private samplesPerPixel (f: Frame) (dx: float) : float = Linear.perPixel f.X / dx

/// A line of ten million samples.
type SeriesChart() =
    let n = 10000000
    let mutable series : LineSeries option = None
    interface IChart with
        member _.Id = "series"
        member _.Title = "Ten million samples"
        member _.Covers = "A line of 10 000 000 samples with single-sample spikes: every zoom shows the spikes, because a frame reads the minimum and maximum pyramid and not the samples."
        member _.Live = false
        member _.Prepare() =
            let t0 = Gfx.now ()
            let ys = ChartData.signal 1u n
            let t1 = Gfx.now ()
            let p = Pyramid.OfArray ys
            let t2 = Gfx.now ()
            series <- Some { Name = "sensor"; Color = theme.Palette.[0]; Width = 1.6f; Fill = false; Data = p; X0 = 0.0; Dx = 1.0 }
            Format.grouped (float n) + " samples made in " + ms (t1 - t0) + " ms, pyramid built in " + ms (t2 - t1) + " ms"
        member _.Step(_) = ()
        member _.XLimits = (0.0, float n)
        member _.YLimits = None
        member _.FitY(x0, x1) = Line.yRange [ series.Value ] (x0, x1) 0.08
        member _.XLabel(step, v) = Format.tick step v
        member _.YLabel(step, v) = Format.tick step v
        member _.Legend() = [ ("sensor", theme.Palette.[0]) ]
        member _.Plot(f, _) = Line.scene f series.Value
        member _.Probe(f, x, _) =
            let s = series.Value
            match Line.nearest s x with
            | None -> ([], [])
            | Some (i, v) ->
                let half = Linear.perPixel f.X * 0.5
                let col = s.Data.Range(int (Math.Floor(x - half)), int (Math.Floor(x + half)) + 1)
                ( [ (Linear.map f.X (float i), Linear.map f.Y (float v), s.Color) ],
                  [ ("sample " + Format.grouped (float i), theme.Muted)
                    ("value " + two (float v), theme.Text)
                    ("this pixel " + two (float col.Lo) + " … " + two (float col.Hi), theme.Muted) ] )
        member _.Describe(f) =
            let per = samplesPerPixel f 1.0
            "Line of " + Format.grouped (float n) + " samples, showing " + Format.grouped f.X.D0 + " to " + Format.grouped f.X.D1 + " (" + Format.fixedPoint (if per < 10.0 then 2 else 0) per + " samples per pixel)"

/// Two streams that grow while they are shown; the pyramids are extended one sample at a time.
type StreamChart() =
    let capacity = 2000000
    let rate = 100000.0
    // the pyramids are made when the chart is first shown (24 MB each)
    let mutable a = { Name = "A"; Color = theme.Palette.[1]; Width = 1.6f; Fill = true; Data = Pyramid(1); X0 = 0.0; Dx = 1.0 }
    let mutable b = { Name = "B"; Color = theme.Palette.[2]; Width = 1.6f; Fill = false; Data = Pyramid(1); X0 = 0.0; Dx = 1.0 }
    let srcA = ChartData.Source(3u, 40000.0, 14.0)
    let srcB = ChartData.Source(5u, 9000.0, 6.0)
    let mutable carry = 0.0
    let mutable total = 0
    let add (k: int) =
        for _ in 1 .. k do
            // a full recording starts again
            if a.Data.Count >= capacity then
                a.Data.Clear()
                b.Data.Clear()
            a.Data.Append(srcA.Next())
            b.Data.Append(srcB.Next())
        total <- total + k
    interface IChart with
        member _.Id = "stream"
        member _.Title = "A stream"
        member _.Covers = "Two series of 100 000 samples a second, appended while the chart is drawn: the pyramid grows one sample at a time and the view follows the end. Drag or zoom to stop following, End to follow again."
        member _.Live = true
        member _.Prepare() =
            let t0 = Gfx.now ()
            a <- { a with Data = Pyramid(capacity) }
            b <- { b with Data = Pyramid(capacity) }
            add 300000
            "300 000 samples per series made and added one by one in " + ms (Gfx.now () - t0) + " ms; 100 000 a second arrive now"
        member _.Step(dt) =
            carry <- carry + dt * rate / 1000.0
            let k = int carry
            carry <- carry - float k
            add (min k 50000)
        member _.XLimits = (0.0, float (max 1000 a.Data.Count))
        member _.YLimits = None
        member _.FitY(x0, x1) = Line.yRange [ a; b ] (x0, x1) 0.1
        member _.XLabel(step, v) = Format.fixedPoint (Format.decimalsFor (step / rate)) (v / rate) + " s"
        member _.YLabel(step, v) = Format.tick step v
        member _.Legend() = [ ("A", a.Color); ("B", b.Color) ]
        member _.Plot(f, _) = Group [ Line.scene f a; Line.scene f b ]
        member _.Probe(f, x, _) =
            match Line.nearest a x, Line.nearest b x with
            | Some (i, va), Some (_, vb) ->
                ( [ (Linear.map f.X (float i), Linear.map f.Y (float va), a.Color); (Linear.map f.X (float i), Linear.map f.Y (float vb), b.Color) ],
                  [ ("t " + Format.fixedPoint 4 (float i / rate) + " s", theme.Muted); ("A " + two (float va), a.Color); ("B " + two (float vb), b.Color) ] )
            | _ -> ([], [])
        member _.Describe(f) =
            "Two live series, " + Format.grouped (float a.Data.Count) + " samples each so far, showing " + Format.fixedPoint 2 (f.X.D0 / rate) + " to " + Format.fixedPoint 2 (f.X.D1 / rate) + " seconds"

/// A density map of five million points.
type ScatterChart() =
    let n = 5000000
    let colors = Heatmap.ramp 24
    let mutable grid : DensityGrid option = None
    let mutable cached : (Linear * Linear * Rect * int * Scene) option = None
    interface IChart with
        member _.Id = "scatter"
        member _.Title = "Five million points"
        member _.Covers = "A density map of 5 000 000 points on a grid of 1 024 by 1 024 cells with a summed-area table: a frame reads four numbers per cell of the screen, whatever the zoom, so panning and zooming cost the same as the full view."
        member _.Live = false
        member _.Prepare() =
            let t0 = Gfx.now ()
            let xs = Array.zeroCreate<float32> n
            let ys = Array.zeroCreate<float32> n
            ChartData.cloud 11u n xs ys
            let t1 = Gfx.now ()
            grid <- Some (DensityGrid.Build(xs, ys, n, 1024, 1024))
            Format.grouped (float n) + " points made in " + ms (t1 - t0) + " ms, grid built in " + ms (Gfx.now () - t1) + " ms"
        member _.Step(_) = ()
        member _.XLimits = (float grid.Value.X0, float grid.Value.X1)
        member _.YLimits = Some (float grid.Value.Y0, float grid.Value.Y1)
        member _.FitY(x0, x1) = (x0, x1)
        member _.XLabel(step, v) = Format.fixedPoint (Format.decimalsFor step) v
        member _.YLabel(step, v) = Format.fixedPoint (Format.decimalsFor step) v
        member _.Legend() = [ ("few", colors.[3]); ("some", colors.[12]); ("many", colors.[22]) ]
        member _.Plot(f, moving) =
            // 14 000 rectangles are what 3-pixel cells make of this map: more than a canvas draws while it is dragged.  It is drawn in cells of 6 pixels then, and sharp when it stops;
            // a hover changes neither the view nor the cell, and takes the scene of the frame before
            let cell = if moving then 6 else 3
            match cached with
            | Some (x, y, p, c, scene) when x = f.X && y = f.Y && p = f.Plot && c = cell -> scene
            | _ ->
                let scene = Group (Heatmap.scene f grid.Value cell colors)
                cached <- Some (f.X, f.Y, f.Plot, cell, scene)
                scene
        member _.Probe(f, x, y) =
            let g = grid.Value
            let c = int ((x - float g.X0) / float (g.X1 - g.X0) * float g.Cols)
            let r = int ((y - float g.Y0) / float (g.Y1 - g.Y0) * float g.Rows)
            ( [],
              [ ("x " + two x + "   y " + two y, theme.Muted)
                (Format.grouped (float (g.Sum(c, c + 1, r, r + 1))) + " points in this cell", theme.Text) ] )
        member _.Describe(f) =
            "Density map of " + Format.grouped (float n) + " points, showing x from " + two f.X.D0 + " to " + two f.X.D1 + " and y from " + two f.Y.D0 + " to " + two f.Y.D1

/// Two hundred thousand candles.
type CandleChart() =
    let n = 200000
    let mutable series : CandleSeries option = None
    interface IChart with
        member _.Id = "candles"
        member _.Title = "Two hundred thousand candles"
        member _.Covers = "OHLC candles: zoomed out, a pixel column stands for hundreds of bars (the open of the first, the close of the last, the highest high and the lowest low, the last two from pyramids); zoomed in, each bar is its own candle."
        member _.Live = false
        member _.Prepare() =
            let t0 = Gfx.now ()
            let o = Array.zeroCreate<float32> n
            let c = Array.zeroCreate<float32> n
            let h = Array.zeroCreate<float32> n
            let l = Array.zeroCreate<float32> n
            ChartData.candles 17u n o c h l
            let t1 = Gfx.now ()
            series <- Some { Open = o; Close = c; High = Pyramid.OfArray h; Low = Pyramid.OfArray l; X0 = 0.0; Dx = 1.0 }
            Format.grouped (float n) + " bars made in " + ms (t1 - t0) + " ms, pyramids built in " + ms (Gfx.now () - t1) + " ms"
        member _.Step(_) = ()
        member _.XLimits = (0.0, float n)
        member _.YLimits = None
        member _.FitY(x0, x1) =
            let s = series.Value
            let r = s.Low.Range(int (Math.Floor x0), int (Math.Ceiling x1) + 1)
            let q = s.High.Range(int (Math.Floor x0), int (Math.Ceiling x1) + 1)
            if Span.isEmpty r then (0.0, 1.0)
            else
                let lo = float r.Lo
                let hi = float q.Hi
                let m = (hi - lo) * 0.08 + 0.01
                (lo - m, hi + m)
        member _.XLabel(step, v) = Format.tick step v
        member _.YLabel(step, v) = Format.fixedPoint (Format.decimalsFor step) v
        member _.Legend() = [ ("up", theme.Up); ("down", theme.Down) ]
        member _.Plot(f, _) = Candles.scene theme f series.Value
        member _.Probe(f, x, _) =
            let s = series.Value
            let i = int (Math.Floor x)
            if i < 0 || i >= n then ([], [])
            else
                let c = if s.Close.[i] >= s.Open.[i] then theme.Up else theme.Down
                ( [],
                  [ ("bar " + Format.grouped (float i), theme.Muted)
                    ("open  " + two (float s.Open.[i]), c)
                    ("high  " + two (float s.High.[i]), c)
                    ("low   " + two (float s.Low.[i]), c)
                    ("close " + two (float s.Close.[i]), c) ] )
        member _.Describe(f) =
            "Candlestick chart of " + Format.grouped (float n) + " bars, showing " + Format.grouped f.X.D0 + " to " + Format.grouped f.X.D1

/// Points coloured by a third value, on a colour map: markers while few are in view, a density map of all of them when many are (Scatter.auto).
type ScienceScatterChart() =
    let n = 400000
    let map = Colormap.viridis
    let mutable xs : float32[] = [||]
    let mutable ys : float32[] = [||]
    let mutable zs : float32[] = [||]
    let zLo = 0.0
    let zHi = 12.0
    interface IChart with
        member _.Id = "science-scatter"
        member _.Title = "Scatter by value"
        member _.Covers = "400 000 points coloured by their distance from the origin on viridis: with more than 20 000 in view a density map of all of them is drawn, and zooming in turns it into one marker per point, coloured by the value (the science charts: Scatter, Colormap, ColorBar)."
        member _.Live = false
        member _.Prepare() =
            let t0 = Gfx.now ()
            xs <- Array.zeroCreate n
            ys <- Array.zeroCreate n
            ChartData.cloud 21u n xs ys
            zs <- Array.init n (fun i -> MathF.Sqrt(xs.[i] * xs.[i] + ys.[i] * ys.[i]))
            Format.grouped (float n) + " points made in " + ms (Gfx.now () - t0) + " ms"
        member _.Step(_) = ()
        member _.XLimits = (-11.0, 11.0)
        member _.YLimits = Some (-7.0, 7.0)
        member _.FitY(x0, x1) = (x0, x1)
        member _.XLabel(step, v) = Format.fixedPoint (Format.decimalsFor step) v
        member _.YLabel(step, v) = Format.fixedPoint (Format.decimalsFor step) v
        member _.Legend() = [ ("few", Colormap.at map 0.15); ("many", Colormap.at map 0.9) ]
        member _.Plot(f, _) =
            let p = f.Plot
            // the colour bar says what a marker's colour means (the distance); the density map beyond 20 000 points in view is coloured by how many there are, which is the legend's business
            let markers = Scatter.visible f xs ys n <= Scatter.MaxMarkers
            Group [ Scatter.auto f xs ys n 2.0f (Scatter.byValue map zLo zHi zs) map
                    (if markers then ColorBar.scene theme map (zLo, zHi) (p.X + p.W - 22.0f) (p.Y + 12.0f) 10.0f (min 120.0f (p.H * 0.4f)) (fun step v -> Format.tick step v) else Empty) ]
        member _.Probe(_, x, y) = ([], [ ("x " + two x + "   y " + two y, theme.Muted); ("distance " + two (Math.Sqrt(x * x + y * y)), theme.Text) ])
        member _.Describe(f) =
            "Scatter plot of " + Format.grouped (float n) + " points coloured by their distance from the origin, showing x from " + two f.X.D0 + " to " + two f.X.D1

/// A histogram of a column of a document that grows: the pyramid of the samples, the histogram and the scene are calcs of the data graph, so the samples that arrive cost themselves and the pixels.
type LiveHistogramChart() =
    let rate = 4000.0
    let rng = ChartData.Rng(31u)
    let mutable doc = Doc(1)
    let mutable node = 0
    let mutable view : Source<Frame> option = None
    let mutable bars : Calc<Scene> option = None
    let mutable counts : Calc<Histogram> option = None
    let mutable carry = 0.0
    let mutable lastEdit = ""
    let sample () : float =
        // two humps and a long tail: what a single mean and deviation would hide
        let r = rng.Next()
        if r < 0.55f then float (rng.Gauss() * 0.7f - 1.8f)
        elif r < 0.93f then float (rng.Gauss() * 1.0f + 1.6f)
        else float (abs (rng.Gauss()) * 3.0f + 2.0f)
    let add (k: int) =
        if k > 0 then
            let xs = Array.init k (fun _ -> sample ())
            let a = doc.Edit("samples", fun tx -> tx.Append(node, "x", xs))
            lastEdit <- string a.Flushed.Ran + " calcs ran for " + string k + " samples"
    interface IChart with
        member _.Id = "science-histogram"
        member _.Title = "A live histogram"
        member _.Covers = "A histogram of a column of a document that grows by 4 000 samples a second: the histogram, the bars and the chart are calcs of the data graph, so an appended sample is added to its bin and nothing is counted again, and a pan or a zoom runs the scene and never reaches the counts."
        member _.Live = true
        member _.Prepare() =
            let t0 = Gfx.now ()
            doc <- Doc(1)
            let made = ref 0
            doc.Edit("make", fun tx -> made.Value <- tx.Create "samples") |> ignore
            node <- made.Value
            let frame = Frame.create 800.0f 400.0f 40.0f 10.0f 10.0f 20.0f (-6.0, 8.0) (0.0, 1.0)
            let v = Live.viewport doc frame
            let h = Live.histogram doc node "x" -6.0 8.0 56
            view <- Some v
            counts <- Some h
            bars <- Some (Live.bars doc v h theme.Palette.[1])
            add 200000
            Format.grouped 200000.0 + " samples appended in one edit and counted in " + ms (Gfx.now () - t0) + " ms; " + Format.grouped rate + " arrive a second now"
        member _.Step(dt) =
            carry <- carry + dt * rate / 1000.0
            let k = int carry
            carry <- carry - float k
            add (min k 20000)
        member _.XLimits = (-6.0, 8.0)
        member _.YLimits = None
        member _.FitY(_, _) = HistogramBars.yRange counts.Value.Peek
        member _.XLabel(step, v) = Format.fixedPoint (Format.decimalsFor step) v
        member _.YLabel(step, v) = Format.tick step v
        member _.Legend() = [ ("samples", theme.Palette.[1]) ]
        member _.Plot(f, _) =
            Live.show doc view.Value f |> ignore
            bars.Value.Value
        member _.Probe(f, x, _) =
            let h = counts.Value.Peek
            let b = h.BinOf x
            if b < 0 || b >= h.Bins then ([], [])
            else ([], [ ("bin " + two (h.Edge b) + " to " + two (h.Edge(b + 1)), theme.Muted); (Format.grouped (float h.Counts.[b]) + " samples", theme.Text); (lastEdit, theme.Muted) ])
        member _.Describe(_) =
            "Histogram of " + Format.grouped (float counts.Value.Peek.Total) + " samples with two humps and a tail"

/// A field of values as coloured cells with its contour lines: the raster is downsampled to the pixels of the screen, whatever the size of the field.
type FieldChart() =
    let cols = 480
    let rows = 360
    let map = Colormap.viridis
    let mutable field : float32[] = [||]
    let mutable lo = 0.0
    let mutable hi = 1.0
    let levels = ref [||]
    interface IChart with
        member _.Id = "science-field"
        member _.Title = "A field with contours"
        member _.Covers = "A field of 480 by 360 values as coloured cells on viridis, with its contour lines (marching squares) and a colour bar: the raster is averaged down to the pixels of the screen, so the cost of a frame is the screen and not the field."
        member _.Live = false
        member _.Prepare() =
            let t0 = Gfx.now ()
            // two hills and a ridge
            field <- Array.init (cols * rows) (fun i ->
                let x = float (i % cols) / float (cols - 1) * 10.0
                let y = float (i / cols) / float (rows - 1) * 7.5
                let hill (cx: float) (cy: float) (w: float) (h: float) = h * Math.Exp(-((x - cx) * (x - cx) + (y - cy) * (y - cy)) / (2.0 * w * w))
                float32 (hill 3.0 3.0 1.1 8.0 + hill 7.0 4.5 1.6 5.0 + 1.5 * Math.Sin(x * 1.3) * Math.Cos(y * 1.7)))
            let (a, b) = Raster.range field
            lo <- a
            hi <- b
            levels.Value <- Contour.niceLevels lo hi 9
            Format.grouped (float (cols * rows)) + " values made in " + ms (Gfx.now () - t0) + " ms"
        member _.Step(_) = ()
        member _.XLimits = (0.0, 10.0)
        member _.YLimits = Some (0.0, 7.5)
        member _.FitY(x0, x1) = (x0, x1)
        member _.XLabel(step, v) = Format.fixedPoint (Format.decimalsFor step) v
        member _.YLabel(step, v) = Format.fixedPoint (Format.decimalsFor step) v
        member _.Legend() = [ ("low", Colormap.at map 0.0); ("high", Colormap.at map 1.0) ]
        member _.Plot(f, _) =
            let p = f.Plot
            Group [ Raster.scene f (0.0, 10.0) (0.0, 7.5) field cols rows map (lo, hi)
                    Contour.scene f (0.0, 10.0) (0.0, 7.5) field cols rows levels.Value (Colormap.reversed map) (lo, hi) 1.2f
                    ColorBar.scene theme map (lo, hi) (p.X + p.W - 22.0f) (p.Y + 12.0f) 10.0f (min 140.0f (p.H * 0.45f)) (fun step v -> Format.tick step v) ]
        member _.Probe(_, x, y) =
            let c = int (x / 10.0 * float (cols - 1) + 0.5)
            let r = int (y / 7.5 * float (rows - 1) + 0.5)
            if c < 0 || c >= cols || r < 0 || r >= rows then ([], [])
            else ([], [ ("x " + two x + "   y " + two y, theme.Muted); ("value " + two (float field.[r * cols + c]), theme.Text) ])
        member _.Describe(f) =
            "Field of " + Format.grouped (float (cols * rows)) + " values with contour lines, showing x from " + two f.X.D0 + " to " + two f.X.D1

/// The data graph of a small document drawn as a node-link diagram; the viewport of the chart pans and zooms the diagram.
type GraphChart() =
    let mutable view : GraphView option = None
    let mutable size = Vec2.create 1.0f 1.0f
    interface IChart with
        member _.Id = "science-graph"
        member _.Title = "The data graph"
        member _.Covers = "The graph of a document (its nodes and the Refs of their fields) as a layered diagram: a box for every node, a line for every link, made again when a node, a link or a name changes and for nothing else. Drag to pan, wheel to zoom."
        member _.Live = false
        member _.Prepare() =
            let t0 = Gfx.now ()
            let doc = Doc(1)
            doc.Edit("model", fun tx ->
                let book = tx.Create "Ledger"
                tx.Set(book, "name", Atom.Text "General ledger")
                let accounts =
                    [ for name in [ "Cash"; "Receivables"; "Inventory"; "Payables"; "Equity"; "Revenue"; "Cost of sales"; "Salaries"; "Rent"; "Taxes" ] ->
                        let a = tx.Create "Account"
                        tx.Set(a, "name", Atom.Text name)
                        tx.Set(a, "ledger", Ref book)
                        a ]
                let periods = [ for m in 1 .. 4 -> (let p = tx.Create "Period" in tx.Set(p, "name", Atom.Text ("Q" + string m)); tx.Set(p, "ledger", Ref book); p) ]
                // every account is closed in every period, a period closes into the next one
                for p in periods do
                    for a in accounts |> List.take 5 do tx.Insert(p, "closes", 0, Ref a)
                for (p, q) in List.pairwise periods do tx.Set(p, "next", Ref q)
                let report = tx.Create "Report"
                tx.Set(report, "name", Atom.Text "Annual report")
                for p in periods do tx.Put(report, "periods", "p" + string (p / 1024), Ref p)
                tx.Set(report, "ledger", Ref book)) |> ignore
            let v = GraphDiagram.view doc theme Direction.LeftRight
            view <- Some v
            size <- v.Layout.Peek.Size
            string v.Layout.Peek.Boxes.Length + " nodes and " + string v.Layout.Peek.Routes.Length + " links laid out in " + ms (Gfx.now () - t0) + " ms"
        member _.Step(_) = ()
        member _.XLimits = (0.0, float size.X)
        member _.YLimits = Some (0.0, float size.Y)
        member _.FitY(x0, x1) = (x0, x1)
        member _.XLabel(_, _) = ""
        member _.YLabel(_, _) = ""
        member _.Legend() = []
        member _.Plot(f, _) =
            // the diagram is drawn in its own coordinates (y grows downwards); the frame says where those are on the screen
            let kx = (f.X.R1 - f.X.R0) / float32 (f.X.D1 - f.X.D0)
            let ky = (f.Y.R1 - f.Y.R0) / float32 (f.Y.D1 - f.Y.D0)
            let h = size.Y
            let m = { Mat3x2.identity with M11 = kx; M22 = -ky; M31 = f.X.R0 - float32 f.X.D0 * kx; M32 = f.Y.R0 + (h - float32 f.Y.D0) * ky }
            Scene.Clip(f.Plot, Scene.Transform(m, view.Value.Scene.Value))
        member _.Probe(f, x, y) =
            let l = view.Value.Layout.Peek
            match Diagram.boxAt l (float32 x) (size.Y - float32 y) with
            | Some id -> ([], [ ("node " + string (id / 1024), theme.Muted) ])
            | None -> ([], [])
        member _.Describe(_) =
            "Diagram of the data graph of a document: " + string view.Value.Layout.Peek.Boxes.Length + " nodes and " + string view.Value.Layout.Peek.Routes.Length + " links"

/// The demo: the selection, the viewport of each chart, the pointer, and the frame plumbing.
type Demo() =
    let host = CanvasHost(CanvasTextEngine())
    let charts : IChart[] = [| SeriesChart(); StreamChart(); ScatterChart(); CandleChart(); ScienceScatterChart(); LiveHistogramChart(); FieldChart(); GraphChart() |]
    let ready = Array.zeroCreate<bool> charts.Length
    let infos = Array.create charts.Length ""
    // the viewport of each chart: what part of the data is shown
    let vx0 = Array.zeroCreate<float> charts.Length
    let vx1 = Array.zeroCreate<float> charts.Length
    let vy0 = Array.zeroCreate<float> charts.Length
    let vy1 = Array.zeroCreate<float> charts.Length
    let follow = Array.create charts.Length false
    let mutable index = 0
    /// A chart is chosen and its data is being made: the frame after the choice shows the page's message, the one after that makes the data (the page paints in between).
    let mutable pending = -1
    let mutable waited = false
    // the pointer
    let mutable px = -1.0f
    let mutable py = -1.0f
    let mutable inside = false
    let mutable dragging = false
    let mutable lastPress = -1.0e9
    let mutable lastMs = -1.0
    // the cost of the frames that were drawn: a ring of the last 120
    let cost = Array.zeroCreate<float> 120
    let mutable costs = 0
    let mutable lastStats = -1.0e9
    /// When the viewport was last changed by the user, and whether the last frame drew it as moving.
    let mutable moved = -1.0e9
    let mutable clock = 0.0
    let mutable drewMoving = false
    let mutable built = false
    let mutable shown = ""
    // the margins around the plot
    let left = 76.0f
    let top = 62.0f
    let right = 20.0f
    let bottom = 36.0f

    /// The span of a stream that is shown first.
    let streamSpan = 400000.0

    let reset (i: int) =
        let c = charts.[i]
        let (lo, hi) = c.XLimits
        if c.Live then
            vx1.[i] <- hi
            vx0.[i] <- max lo (hi - streamSpan)
            follow.[i] <- true
        else
            vx0.[i] <- lo
            vx1.[i] <- hi
            follow.[i] <- false
        match c.YLimits with
        | Some (a, b) ->
            vy0.[i] <- a
            vy1.[i] <- b
        | None -> ()

    let ensure (i: int) =
        if not ready.[i] then
            infos.[i] <- charts.[i].Prepare()
            ready.[i] <- true
            reset i
        Gfx.announce "info" charts.[i].Id infos.[i]

    /// The frame of chart `i` at its viewport and the size of the canvas.
    let frameOf (i: int) : Frame =
        let c = charts.[i]
        let (x0, x1) = if c.Live && follow.[i] then (let (_, hi) = c.XLimits in (max 0.0 (hi - (vx1.[i] - vx0.[i])), hi)) else (vx0.[i], vx1.[i])
        let (y0, y1) = match c.YLimits with Some _ -> (vy0.[i], vy1.[i]) | None -> c.FitY(x0, x1)
        Frame.create host.Width host.Height left top right bottom (x0, x1) (y0, y1)

    /// Make the viewport of the chart what it shows now (a followed stream moves by itself).
    let settle (i: int) (f: Frame) =
        vx0.[i] <- f.X.D0
        vx1.[i] <- f.X.D1

    let limitsY (i: int) = match charts.[i].YLimits with Some l -> l | None -> (0.0, 1.0)

    let announceSelection () = Gfx.announce "selected" (string index) ""

    member _.Count = charts.Length
    member _.Index = index
    member _.Charts = charts

    member _.Start() =
        for c in charts do Gfx.announce ("chart:" + c.Id) c.Title c.Covers
        ensure 0
        announceSelection ()

    member _.Resize(w: float32, h: float32, r: float32) = host.Resize(w, h, r)

    member _.Select(i: int) =
        let next = max 0 (min (charts.Length - 1) i)
        if next <> index || pending >= 0 then
            pending <- next
            waited <- false
            Gfx.announce "busy" charts.[next].Id ""

    member _.Pointer(kind: int, x: float32, y: float32, now: float) =
        let f = frameOf index
        px <- x
        py <- y
        inside <- Frame.contains f x y
        if kind = 0 && inside then
            dragging <- true
            if now - lastPress < 350.0 then
                reset index
            lastPress <- now
        elif kind = 1 then dragging <- false
        elif kind = 3 then
            inside <- false
        host.Invalidate()

    /// A drag moves the data by the distance the pointer moved.
    member this.Move(x: float32, y: float32) =
        if dragging then
            let f = frameOf index
            let c = charts.[index]
            let dxd = float (px - x) * Linear.perPixel f.X
            let (a, b) = Domain.pan c.XLimits dxd (f.X.D0, f.X.D1)
            vx0.[index] <- a
            vx1.[index] <- b
            follow.[index] <- false
            moved <- clock
            match c.YLimits with
            | Some lim ->
                let dyd = float (y - py) * Linear.perPixel f.Y
                let (ya, yb) = Domain.pan lim dyd (f.Y.D0, f.Y.D1)
                vy0.[index] <- ya
                vy1.[index] <- yb
            | None -> ()
        px <- x
        py <- y
        inside <- Frame.contains (frameOf index) x y
        host.Invalidate()

    member _.Zoom(k: float, ax: float32, ay: float32) =
        let f = frameOf index
        let c = charts.[index]
        let (lo, hi) = c.XLimits
        let minSpan = if c.Live then 200.0 else 20.0
        let (a, b) = Domain.zoom (lo, hi) minSpan k (Linear.invert f.X ax) (f.X.D0, f.X.D1)
        vx0.[index] <- a
        vx1.[index] <- b
        moved <- clock
        if c.Live then follow.[index] <- (b >= hi - 1.0 && ax >= f.Plot.X + f.Plot.W - 4.0f)
        match c.YLimits with
        | Some lim ->
            let (ya, yb) = Domain.zoom lim 0.0001 k (Linear.invert f.Y ay) (f.Y.D0, f.Y.D1)
            vy0.[index] <- ya
            vy1.[index] <- yb
        | None -> ()
        host.Invalidate()

    member this.Wheel(dy: float) =
        let f = frameOf index
        // with the pointer outside the plot, the middle of it
        let ax = if inside then px else f.Plot.X + f.Plot.W * 0.5f
        let ay = if inside then py else f.Plot.Y + f.Plot.H * 0.5f
        this.Zoom(Math.Pow(0.8, dy), ax, ay)

    member this.Key(key: string) =
        let f = frameOf index
        let c = charts.[index]
        match key with
        | "ArrowLeft" | "ArrowRight" ->
            let d = (f.X.D1 - f.X.D0) * 0.1 * (if key = "ArrowLeft" then -1.0 else 1.0)
            let (a, b) = Domain.pan c.XLimits d (f.X.D0, f.X.D1)
            vx0.[index] <- a
            vx1.[index] <- b
            follow.[index] <- false
            moved <- clock
            host.Invalidate()
        | "ArrowUp" | "+" | "=" -> this.Zoom(0.8, f.Plot.X + f.Plot.W * 0.5f, f.Plot.Y + f.Plot.H * 0.5f)
        | "ArrowDown" | "-" -> this.Zoom(1.25, f.Plot.X + f.Plot.W * 0.5f, f.Plot.Y + f.Plot.H * 0.5f)
        | "Home" | "0" ->
            reset index
            host.Invalidate()
        | "End" when c.Live ->
            follow.[index] <- true
            host.Invalidate()
        | _ -> ()

    /// The scene of the chart that is shown, at this size.
    member private _.Build(size: Vec2) : Scene =
        built <- true
        let i = index
        let c = charts.[i]
        let f = frameOf i
        settle i f
        let w = size.X
        let legend = c.Legend()
        let probe = if inside && not dragging && Frame.contains f px py then Some (c.Probe(f, Linear.invert f.X px, Linear.invert f.Y py)) else None
        let description = c.Describe f
        let moving = clock - moved < 200.0
        drewMoving <- moving
        // the page shows what is in view; a stream changes it every frame, which is a text node and not a layout
        if description <> shown then
            shown <- description
            Gfx.announce "view" description ""
        Parts.describe ("chart-" + c.Id) c.Title description
            (Group [ Parts.box 0.0f 0.0f size.X size.Y theme.Background
                     Parts.text theme 17.0f theme.Text TextAlign.Left left 12.0f (w * 0.6f) c.Title
                     Parts.text theme 12.0f theme.Muted TextAlign.Left left 36.0f (w - left - right) description
                     Parts.legend theme (max left (w - right - Parts.legendWidth theme legend)) 12.0f legend
                     Parts.axes theme f (fun s v -> c.XLabel(s, v)) (fun s v -> c.YLabel(s, v))
                     c.Plot(f, moving)
                     (match probe with
                      | Some (marks, lines) -> Parts.crosshair theme f px py marks lines
                      | None -> Empty) ])

    member this.Frame(nowMs: float) =
        let t0 = Gfx.now ()
        clock <- nowMs
        // what was drawn coarse while the view moved is drawn sharp once it has stopped
        if drewMoving && nowMs - moved >= 200.0 then
            drewMoving <- false
            host.Invalidate()
        if pending >= 0 then
            // one frame for the page to say that the data is being made, then the data
            if waited then
                ensure pending
                index <- pending
                pending <- -1
                lastMs <- -1.0
                announceSelection ()
                host.Invalidate()
            else waited <- true
        else
            let c = charts.[index]
            if c.Live then
                if lastMs >= 0.0 then c.Step(min 100.0 (nowMs - lastMs))
                lastMs <- nowMs
                // a recording that started again: what was shown is gone
                if vx0.[index] >= snd c.XLimits then reset index
        built <- false
        host.Frame(nowMs, pending < 0 && charts.[index].Live, 0.0f, fun _ size -> this.Build size)
        if built then
            cost.[costs % cost.Length] <- Gfx.now () - t0
            costs <- costs + 1
            if nowMs - lastStats > 400.0 then
                lastStats <- nowMs
                let n = min costs cost.Length
                let sorted = Array.sub cost 0 n
                Array.sortInPlace sorted
                Gfx.announce "stats" (Format.fixedPoint 2 sorted.[n / 2]) (Format.fixedPoint 2 sorted.[min (n - 1) (n * 95 / 100)] + " " + string n)

let private demo = Demo()

[<CompiledName("start")>]
let start () : unit =
    Gfx.init "gfx"
    demo.Start()

[<CompiledName("onResize")>]
let onResize (w: float) (h: float) (ratio: float) : unit = demo.Resize(float32 w, float32 h, float32 ratio)

[<CompiledName("onPointer")>]
let onPointer (kind: int) (x: float) (y: float) (button: int) : unit =
    if kind = 2 then demo.Move(float32 x, float32 y) else demo.Pointer(kind, float32 x, float32 y, Gfx.now ())

[<CompiledName("onWheel")>]
let onWheel (dx: float) (dy: float) : unit = if dy <> 0.0 then demo.Wheel dy

[<CompiledName("onKey")>]
let onKey (kind: int) : unit =
    let key = Gfx.pendingKey ()
    if kind = 0 then demo.Key key

[<CompiledName("chartCount")>]
let chartCount () : int = demo.Count

[<CompiledName("selected")>]
let selected () : int = demo.Index

[<CompiledName("select")>]
let select (i: int) : unit = demo.Select i

[<CompiledName("frame")>]
let frame (nowMs: float) : unit = demo.Frame nowMs

// ---- what the benchmark times ------------------------------------------------------------------------------------------------------------------------------------------------------

/// What the benchmark kernels share: the samples of the last `benchBuild` and the pyramid of them, and the grid of the last `benchGrid`.
type private BenchState = { mutable Samples: float32[]; mutable Pyramid: Pyramid; mutable Grid: DensityGrid option }

let private bench = { Samples = [||]; Pyramid = Pyramid(1); Grid = None }

/// Milliseconds to build the pyramid of `n` samples (the samples are made first and not counted).
[<CompiledName("benchBuild")>]
let benchBuild (n: int) : float =
    bench.Samples <- ChartData.signal 7u n
    let t0 = Gfx.now ()
    bench.Pyramid <- Pyramid.OfArray bench.Samples
    Gfx.now () - t0

/// Milliseconds to append `n` samples to an empty pyramid one at a time (a stream).
[<CompiledName("benchAppend")>]
let benchAppend (n: int) : float =
    let p = Pyramid(n)
    let src = bench.Samples
    let t0 = Gfx.now ()
    for i in 0 .. n - 1 do p.Append src.[i % src.Length]
    Gfx.now () - t0

/// Milliseconds for the columns of a plot `cols` wide over all the samples, by the pyramid: the work of one frame.
[<CompiledName("benchColumns")>]
let benchColumns (cols: int) (reps: int) : float =
    let lo = Array.zeroCreate<float32> cols
    let hi = Array.zeroCreate<float32> cols
    let n = bench.Pyramid.Count
    let t0 = Gfx.now ()
    for _ in 1 .. reps do bench.Pyramid.Columns(0.0, float n / float cols, cols, lo, hi)
    (Gfx.now () - t0) / float reps

/// The same columns found by reading every sample: what drawing the line without a pyramid has to do.
[<CompiledName("benchNaive")>]
let benchNaive (cols: int) (reps: int) : float =
    let lo = Array.zeroCreate<float32> cols
    let hi = Array.zeroCreate<float32> cols
    let ys = bench.Samples
    let n = ys.Length
    let step = float n / float cols
    let t0 = Gfx.now ()
    for _ in 1 .. reps do
        for c in 0 .. cols - 1 do
            let a = int (Math.Floor(float c * step))
            let b = int (Math.Floor(float (c + 1) * step))
            let mutable l = infinityf
            let mutable h = -infinityf
            for i in (if a > 0 then a - 1 else 0) .. (if b > a then b else a + 1) - 1 do
                let v = ys.[i]
                if v < l then l <- v
                if v > h then h <- v
            lo.[c] <- l
            hi.[c] <- h
    (Gfx.now () - t0) / float reps

/// The largest difference between the columns of the pyramid and the ones found by reading every sample: 0 when they are the same.
[<CompiledName("benchAgree")>]
let benchAgree (cols: int) : float =
    let lo = Array.zeroCreate<float32> cols
    let hi = Array.zeroCreate<float32> cols
    let n = bench.Pyramid.Count
    bench.Pyramid.Columns(0.0, float n / float cols, cols, lo, hi)
    let ys = bench.Samples
    let step = float n / float cols
    let mutable worst = 0.0f
    for c in 0 .. cols - 1 do
        let a = int (Math.Floor(float c * step))
        let b = int (Math.Floor(float (c + 1) * step))
        let mutable l = infinityf
        let mutable h = -infinityf
        for i in (if a > 0 then a - 1 else 0) .. (if b > a then b else a + 1) - 1 do
            if ys.[i] < l then l <- ys.[i]
            if ys.[i] > h then h <- ys.[i]
        worst <- max worst (max (abs (lo.[c] - l)) (abs (hi.[c] - h)))
    float worst

/// Milliseconds to build the scene of the whole line in a plot `w` wide (the polygon of the band, not the drawing of it), per frame.
[<CompiledName("benchScene")>]
let benchScene (w: int) (reps: int) : float =
    let n = bench.Pyramid.Count
    let series = { Name = "bench"; Color = theme.Palette.[0]; Width = 1.6f; Fill = true; Data = bench.Pyramid; X0 = 0.0; Dx = 1.0 }
    let f = Frame.create (float32 w + 96.0f) 420.0f 76.0f 20.0f 20.0f 36.0f (0.0, float n) (-200.0, 200.0)
    let t0 = Gfx.now ()
    let mutable keep = Scene.Empty
    for _ in 1 .. reps do keep <- Line.scene f series
    (Gfx.now () - t0) / float reps

/// Milliseconds to build the grid of `n` points (making them is not counted).
[<CompiledName("benchGrid")>]
let benchGrid (n: int) : float =
    let xs = Array.zeroCreate<float32> n
    let ys = Array.zeroCreate<float32> n
    ChartData.cloud 11u n xs ys
    let t0 = Gfx.now ()
    bench.Grid <- Some (DensityGrid.Build(xs, ys, n, 1024, 1024))
    Gfx.now () - t0

/// Milliseconds to build the scene of the density map in a plot `w` wide, per frame.
[<CompiledName("benchHeat")>]
let benchHeat (w: int) (reps: int) : float =
    let g = bench.Grid.Value
    let colors = Heatmap.ramp 24
    let f = Frame.create (float32 w + 96.0f) 420.0f 76.0f 20.0f 20.0f 36.0f (float g.X0, float g.X1) (float g.Y0, float g.Y1)
    let t0 = Gfx.now ()
    let mutable keep = []
    for _ in 1 .. reps do keep <- Heatmap.scene f g 3 colors
    (Gfx.now () - t0) / float reps

[<CompiledName("benchHeatShapes")>]
let benchHeatShapes (w: int) : int =
    let g = bench.Grid.Value
    let f = Frame.create (float32 w + 96.0f) 420.0f 76.0f 20.0f 20.0f 36.0f (float g.X0, float g.X1) (float g.Y0, float g.Y1)
    (Heatmap.scene f g 3 (Heatmap.ramp 24)).Length
