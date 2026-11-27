/// The tests of FSharp.Graphics.Charts.Live: a column of a document as the data of a chart.
module FSharp.Graphics.ChartsLiveTests.Tests

open System
open FSharp.Math
open FSharp.Wasm.Graph
open FSharp.Graphics.Core
open FSharp.Graphics.Scene
open FSharp.Graphics.Charts
open FSharp.Graphics.Charts.Live

let mutable private failures = 0
let mutable private count = 0
/// the number of the first check that failed (0: none): the name is printed by the .NET run, and a WebAssembly host cannot read it, so it is given this number
let mutable private firstFailure = 0

let private check (name: string) (ok: bool) =
    count <- count + 1
    if not ok then
        failures <- failures + 1
        if firstFailure = 0 then firstFailure <- count
        printfn "  FAIL  %s" name

/// A deterministic stream of floats in [0, 1): the same numbers under .NET and in WebAssembly.
let private stream (seed: int) : unit -> float =
    let mutable s = uint32 seed
    fun () ->
        s <- s * 1664525u + 1013904223u
        float (s >>> 8) / 16777216.0

let private numbers (seed: int) (n: int) : float[] =
    let next = stream seed
    Array.init n (fun _ -> next () * 200.0 - 100.0)

let rec private shapes (s: Scene) : int =
    match s with
    | Shape _ -> 1
    | Group cs -> cs |> List.sumBy shapes
    | Clip(_, c) | Transform(_, c) | Opacity(_, c) | Semantic(_, c) -> shapes c
    | _ -> 0

let private frame = Frame.create 400.0f 200.0f 40.0f 10.0f 10.0f 20.0f (0.0, 100.0) (-100.0, 100.0)
let private panned = Frame.create 400.0f 200.0f 40.0f 10.0f 10.0f 20.0f (20.0, 60.0) (-100.0, 100.0)
let private look (p: Pyramid) : LineSeries = { Name = "y"; Color = Color.hex 0x3B82F6u; Width = 2.0f; Fill = false; Data = p; X0 = 0.0; Dx = 1.0 }

/// A document with one node that has a column `y`.
let private series () : Doc * int =
    let d = Doc(1)
    let node = ref 0
    d.Edit("make", fun tx -> node.Value <- tx.Create "series") |> ignore
    (d, node.Value)

let private append (d: Doc) (node: int) (xs: float[]) : Flushed = (d.Edit("append", fun tx -> tx.Append(node, "y", xs))).Flushed

let private brute (xs: float[]) (l: int) (r: int) : Span =
    let mutable lo = infinityf
    let mutable hi = -infinityf
    for i in max 0 l .. min xs.Length r - 1 do
        let v = float32 xs.[i]
        if v < lo then lo <- v
        if v > hi then hi <- v
    { Lo = lo; Hi = hi }

let private samplesTests () =
    let (d, node) = series ()
    let s = Live.samples d node "y"
    check "samples: nothing in the column, nothing in the pyramid" (s.Peek.Count = 0)
    let all = numbers 1 5000
    let f1 = append d node all.[0 .. 9]
    check "samples: ten appended, ten in the pyramid, and the engine ran the one calc that reads the column" (s.Peek.Count = 10 && f1.Reached = 1 && f1.Ran = 1)
    let mutable pos = 10
    for chunk in [ 1000; 5; 2000; 1985 ] do
        append d node all.[pos .. pos + chunk - 1] |> ignore
        pos <- pos + chunk
    check "samples: past the first capacity the pyramid grows and keeps every sample" (s.Peek.Count = 5000 && pos = 5000)
    let same = Seq.forall (fun (l, r) -> let a = s.Peek.Pyramid.Range(l, r) in let b = brute all l r in a.Lo = b.Lo && a.Hi = b.Hi) [ (0, 5000); (0, 1); (1023, 1025); (4000, 5000); (17, 4999); (2048, 4096) ]
    check "samples: the range of runs of the grown pyramid is the range of a loop over the samples" same

let private chartTests () =
    let (d, node) = series ()
    let data = numbers 2 3000
    let s = Live.samples d node "y"
    let h = Live.histogram d node "y" -100.0 100.0 20
    let view = Live.viewport d frame
    let lineScene = Live.line d view s look
    let barScene = Live.bars d view h (Color.hex 0x22C55Eu)
    let chart = Live.stack d [ lineScene; barScene ]
    check "chart: an empty column makes a chart with nothing in it" (shapes chart.Peek = 0)
    let f = append d node data
    check "chart: an append runs the calc of the samples, of the histogram, both scenes and the stack, once each" (f.Reached = 5 && f.Ran = 5)
    let whole = Pyramid.OfArray (Array.map float32 data)
    let fresh = Group [ Line.scene frame (look whole)
                        (let hh = Histogram(-100.0, 100.0, 20) in (for v in data do hh.Add v); HistogramBars.scene frame hh (Color.hex 0x22C55Eu)) ]
    check "chart: the chart that followed the appends is the chart made from the whole column" (shapes chart.Peek = shapes fresh && h.Peek.Total = 3000)
    let p = Live.show d view panned
    check "chart: a pan runs the scenes and the stack, and not the pyramid or the histogram (the cut-off the engine counts)" (p.Reached = 3 && p.Ran = 3)
    check "chart: after the pan the scene is the one of the new frame" (shapes lineScene.Peek = shapes (Line.scene panned (look whole)))
    let again = Live.show d view panned
    check "chart: the same frame again is no change: nothing runs" (again.Ran = 0)
    // a second chart on the same column follows it by itself
    let s2 = Live.samples d node "y"
    append d node (numbers 3 10) |> ignore
    check "chart: two charts on one column follow it, each with a state of its own" (s.Peek.Count = 3010 && s2.Peek.Count = 3010 && not (obj.ReferenceEquals(s.Peek, s2.Peek)))

let private historyTests () =
    let (d, node) = series ()
    let tl = Timeline(d)
    let s = Live.samples d node "y"
    let h = Live.histogram d node "y" -100.0 100.0 10
    let first = numbers 4 100
    tl.Do("first", fun tx -> tx.Append(node, "y", first)) |> ignore
    tl.Do("second", fun tx -> tx.Append(node, "y", numbers 5 50)) |> ignore
    check "history: two appends, 150 samples counted" (s.Peek.Count = 150 && h.Peek.Total = 150)
    tl.Undo() |> ignore
    check "history: the undo of an append takes the samples back out of the pyramid and the histogram" (s.Peek.Count = 100 && h.Peek.Total = 100 && s.Peek.Values.[99] = float32 first.[99])
    tl.Redo() |> ignore
    check "history: the redo puts them back" (s.Peek.Count = 150 && h.Peek.Total = 150)
    // a write over a sample moves it from one bin to another, and the undo moves it back
    let before = h.Peek.Counts |> Array.copy
    tl.Do("fix", fun tx -> tx.Write(node, "y", 7, [| 99.5 |])) |> ignore
    let after = h.Peek.Counts |> Array.copy
    check "history: a write over a sample is in the pyramid, and the histogram is the one of the data now" (s.Peek.Values.[7] = 99.5f && h.Peek.Total = 150 && h.Peek.Counts.[9] = before.[9] + (if before = after then 0 else 1))
    tl.Undo() |> ignore
    check "history: the undo of the write gives the old histogram back" (h.Peek.Counts = before && s.Peek.Values.[7] = float32 first.[7])

let private remoteTests () =
    let a = Doc(1)
    let b = Doc(2)
    let node = ref 0
    let made = a.Edit("make", fun tx -> node.Value <- tx.Create "series")
    b.Apply made.Patch |> ignore
    let s = Live.samples b node.Value "y"
    let sent = a.Edit("append", fun tx -> tx.Append(node.Value, "y", numbers 6 40))
    let got = b.Apply sent.Patch
    check "remote: a patch from another document moves the chart of this one, one calc" (s.Peek.Count = 40 && got.Flushed.Ran = 1)
    let back = b.Apply sent.Inverse
    check "remote: and so does the inverse of it" (s.Peek.Count = 0 && back.Flushed.Ran = 1)

let private statsTests () =
    let (d, node) = series ()
    let s = Live.samples d node "y"
    let b = Live.box d s
    let data = numbers 7 501
    append d node data |> ignore
    let expected = Stats.box (Array.map float32 data) data.Length
    check "box: the box of the column is the box of the samples" (b.Peek.Count = 501 && b.Peek.Median = expected.Median && b.Peek.Q1 = expected.Q1 && b.Peek.Q3 = expected.Q3 && b.Peek.Outliers.Length = expected.Outliers.Length)
    append d node [| 1000.0 |] |> ignore
    check "box: a sample far out is an outlier of the next one" (b.Peek.Count = 502 && b.Peek.Outliers.Length = expected.Outliers.Length + 1 && b.Peek.Max = 1000.0)
    let plot = Live.boxPlot d (Live.viewport d frame) (Theme.dark (FontId 0UL)) "y" b (Color.hex 0xA78BFAu)
    check "box: the box plot is a scene of shapes" (shapes plot.Peek > 0)

let private scatterTests () =
    let d = Doc(1)
    let nodes = ref (0, 0)
    d.Edit("make", fun tx -> nodes.Value <- (tx.Create "x", tx.Create "y")) |> ignore
    let (nx, ny) = nodes.Value
    let xs = Live.samples d nx "v"
    let ys = Live.samples d ny "v"
    let view = Live.viewport d frame
    let sc = Live.scatter d view xs ys 2.0f (fun _ -> Color.hex 0xF59E0Bu) Colormap.viridis
    let px = Array.init 200 (fun i -> float i * 0.5)
    let py = Array.init 200 (fun i -> float (i % 50) - 25.0)
    d.Edit("points", fun tx -> tx.Append(nx, "v", px); tx.Append(ny, "v", py)) |> ignore
    check "scatter: two columns are the points of a scatter, a marker each while they are few" (shapes sc.Peek = 200)
    d.Edit("more x", fun tx -> tx.Append(nx, "v", [| 1.0; 2.0 |])) |> ignore
    check "scatter: a point needs both of its numbers: the longer column does not draw past the shorter" (shapes sc.Peek = 200)

let private fieldTests () =
    let (d, node) = series ()
    let cols = 9
    let rows = 7
    let bowl = [| for r in 0 .. rows - 1 do for c in 0 .. cols - 1 -> float ((c - 4) * (c - 4) + (r - 3) * (r - 3)) |]
    let f = Live.field d node "z"
    let view = Live.viewport d frame
    let map = Colormap.viridis
    let r = Live.raster d view f (0.0, 100.0) (-100.0, 100.0) map (0.0, 25.0)
    let c = Live.contours d view f (0.0, 100.0) (-100.0, 100.0) [| 4.0; 9.0 |] map (0.0, 25.0) 1.0f
    d.Edit("field", fun tx ->
        tx.Set(node, "cols", Num (float cols))
        tx.Set(node, "rows", Num (float rows))
        tx.Append(node, "z", bowl)) |> ignore
    check "field: a column with its size is a field, and the raster and the contours are scenes of it" (f.Peek.Cols = 9 && f.Peek.Rows = 7 && shapes r.Peek > 0 && shapes c.Peek > 0)
    let rasterBefore = shapes r.Peek
    d.Edit("flatten", fun tx -> tx.Write(node, "z", 0, Array.create (cols * rows) 3.0)) |> ignore
    check "field: a write over the field draws the new field (a flat one has no contour at these levels)" (shapes c.Peek = 0 && shapes r.Peek > 0 && shapes r.Peek <= rasterBefore)
    let (d2, empty) = series ()
    check "field: a column without a size is no field" ((Live.field d2 empty "z").Peek.Cols = 0)

let run () : int =
    samplesTests ()
    chartTests ()
    historyTests ()
    remoteTests ()
    statsTests ()
    scatterTests ()
    fieldTests ()
    printfn "%d checks, %d failed" count failures
    failures

#if FABLE_COMPILER
let chartslive_run () : int = run ()
/// how many checks the last run made (the host checks that it is not zero)
let chartslive_checks () : int = count
/// the number of the first check that failed, in the order the .NET run prints them (0: none)
let chartslive_first_failure () : int = firstFailure
#else
[<EntryPoint>]
let main _ = if run () = 0 then 0 else 1
#endif
