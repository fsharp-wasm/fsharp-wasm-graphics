namespace FSharp.Graphics.Charts.Live

open FSharp.Math
open FSharp.Wasm.Graph
open FSharp.Graphics.Scene
open FSharp.Graphics.Charts

/// The samples of a column as a pyramid that grows (a `Pyramid` has a capacity and does not): the samples are what the column holds, as float32, and `Pyramid` answers the range of any
/// run of them in O(log n).  `Add` takes what an append brings; a column that was written over starts it again.
type Samples() =
    let mutable pyramid = Pyramid(1024)
    member _.Pyramid : Pyramid = pyramid
    member _.Count : int = pyramid.Count
    /// The samples (it holds `Count` of them and may be longer).
    member _.Values : float32[] = pyramid.Samples
    /// The numbers `data.[first .. last - 1]`.
    member _.Add(data: float[], first: int, last: int) : unit =
        let need = pyramid.Count + (last - first)
        if need > pyramid.Capacity then
            let bigger = Pyramid(max need (pyramid.Capacity * 2))
            bigger.AppendMany(pyramid.Samples, pyramid.Count)
            pyramid <- bigger
        for i in first .. last - 1 do pyramid.Append(float32 data.[i])

/// A field of values laid out in rows: `Values.[r * Cols + c]` (what a raster and its contours draw).
type Field = { Values: float32[]; Cols: int; Rows: int }

module Field =
    let empty : Field = { Values = [||]; Cols = 0; Rows = 0 }

/// Charts as calcs of the data graph.  Everything here takes the `Doc` whose engine runs the calcs: a column of the document is the data (`samples`, `histogram`, `field`), the view of the chart
/// is a source (`viewport`), and a scene is a calc that reads both, so
///
/// - an edit that appends samples runs the calc of the column (the new samples only) and the scenes that read it (the pixels of the chart), and nothing else;
/// - a pan, a zoom or a resize sets the viewport, and runs the scenes only: the pyramids and histograms are not reached;
/// - an undo, or a patch from another place, moves the chart the way an edit of its own does (`Truncate` or `Write` make a column start again, and the calc takes it from the start).
module Live =
    let private ignore2 (_: 'a) (_: 'a) : unit = ()

    /// A calc that is another value whenever it runs (what reads it runs too).
    let derived (doc: Doc) (initial: 'a) (f: unit -> 'a) : Calc<'a> = Calc<'a>(doc.Engine, Same.never, initial, ignore2, f)

    /// The view of a chart (its frame: size, plot, scales) as a source: set it and flush to pan, zoom or resize.
    let viewport (doc: Doc) (frame: Frame) : Source<Frame> = Source<Frame>(doc.Engine, frame, (fun a b -> a = b))

    /// Another frame for the chart; the flush runs what reads it (the scenes).  What the flush did is the answer.
    let show (doc: Doc) (view: Source<Frame>) (frame: Frame) : Flushed =
        view.Set frame |> ignore
        doc.Engine.Flush()

    /// A calc of a column that keeps a state up to date with what changed in it: `create` makes the state, and `take state data first last` brings in the numbers of an append
    /// (`data.[first .. last - 1]`).  When something that was there changed (a write, a truncate, an undo of an append) the state is made again and takes the whole column.
    let column (doc: Doc) (node: int) (field: string) (create: unit -> 's) (take: 's -> float[] -> int -> int -> unit) : Calc<'s> =
        let state = ref (create ())
        let epoch = ref -1
        let seen = ref 0
        derived doc state.Value (fun () ->
            let n = doc.Count(node, field)
            let e = doc.Epoch(node, field)
            let data = doc.Data(node, field)
            if e <> epoch.Value || n < seen.Value then
                state.Value <- create ()
                seen.Value <- 0
                epoch.Value <- e
            if n > seen.Value then take state.Value data seen.Value n
            seen.Value <- n
            state.Value)

    /// The samples of a column.
    let samples (doc: Doc) (node: int) (field: string) : Calc<Samples> =
        column doc node field Samples (fun s data a b -> s.Add(data, a, b))

    /// The histogram of a column over [lo, hi) in `bins` bins: an appended number is added to its bin and nothing is counted again.
    let histogram (doc: Doc) (node: int) (field: string) (lo: float) (hi: float) (bins: int) : Calc<Histogram> =
        column doc node field (fun () -> Histogram(lo, hi, bins)) (fun h data a b -> for i in a .. b - 1 do h.Add data.[i])

    /// The box of a column (median, quartiles, whiskers, outliers).  It is found again when the column changed, which sorts the samples.
    let box (doc: Doc) (samples: Calc<Samples>) : Calc<BoxStats> =
        let empty = Stats.box [||] 0
        derived doc empty (fun () -> let s = samples.Value in Stats.box s.Values s.Count)

    /// A column as a field of `cols` x `rows` values (the registers `cols` and `rows` of the node say how they are laid out; without them the field is empty).  It is read whole when it
    /// changed: a field is written over, not appended to.
    let field (doc: Doc) (node: int) (field: string) : Calc<Field> =
        let size name = match doc.Get(node, name) with Num v when v >= 1.0 -> int v | _ -> 0
        derived doc Field.empty (fun () ->
            let data = doc.Data(node, field)
            let n = doc.Count(node, field)
            let cols = size "cols"
            let rows = size "rows"
            if cols = 0 || rows = 0 || cols * rows > n then Field.empty
            else { Values = Array.init (cols * rows) (fun i -> float32 data.[i]); Cols = cols; Rows = rows })

    // ---- scenes: a calc that reads the viewport and the data of the chart ----

    /// A line of the samples of a column; `series` says how it looks and where its x starts (`fun p -> { Name = ..; Data = p; X0 = ..; Dx = .. ; .. }`).
    let line (doc: Doc) (view: Source<Frame>) (samples: Calc<Samples>) (series: Pyramid -> LineSeries) : Calc<Scene> =
        derived doc Empty (fun () -> Line.scene view.Value (series samples.Value.Pyramid))

    /// Markers, or a density map when there are too many of them in view, of the points (x.[i], y.[i]) of two columns.
    let scatter (doc: Doc) (view: Source<Frame>) (xs: Calc<Samples>) (ys: Calc<Samples>) (radius: float32) (color: int -> Color) (map: Colormap) : Calc<Scene> =
        derived doc Empty (fun () ->
            let x = xs.Value
            let y = ys.Value
            Scatter.auto view.Value x.Values y.Values (min x.Count y.Count) radius color map)

    /// The bars of a histogram.
    let bars (doc: Doc) (view: Source<Frame>) (h: Calc<Histogram>) (color: Color) : Calc<Scene> =
        derived doc Empty (fun () -> HistogramBars.scene view.Value h.Value color)

    /// The box plot of one column (`name` under it).
    let boxPlot (doc: Doc) (view: Source<Frame>) (theme: Theme) (name: string) (b: Calc<BoxStats>) (color: Color) : Calc<Scene> =
        derived doc Empty (fun () -> BoxPlot.scene theme view.Value [| name |] [| b.Value |] [| color |])

    /// A field as coloured cells over [x0, x1] x [y0, y1], on a colour map between `lo` and `hi`.
    let raster (doc: Doc) (view: Source<Frame>) (f: Calc<Field>) (xr: float * float) (yr: float * float) (map: Colormap) (range: float * float) : Calc<Scene> =
        derived doc Empty (fun () ->
            let v = f.Value
            if v.Cols = 0 then Empty else Raster.scene view.Value xr yr v.Values v.Cols v.Rows map range)

    /// The contour lines of a field at these levels, coloured by level on the map.
    let contours (doc: Doc) (view: Source<Frame>) (f: Calc<Field>) (xr: float * float) (yr: float * float) (levels: float[]) (map: Colormap) (range: float * float) (width: float32) : Calc<Scene> =
        derived doc Empty (fun () ->
            let v = f.Value
            if v.Cols = 0 then Empty else Contour.scene view.Value xr yr v.Values v.Cols v.Rows levels map range width)

    /// Scenes one over the other, the first at the bottom: the chart.  It reads each of them, so it is made again when one is.
    let stack (doc: Doc) (layers: Calc<Scene> list) : Calc<Scene> =
        derived doc Empty (fun () -> Group [ for l in layers -> l.Value ])
