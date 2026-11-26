/// The tests of FSharp.Graphics.Charts: scales and ticks, the pyramid against a loop, the density grid against a count, the polygons, and the scenes that are made of them.
module FSharp.Graphics.ChartsTests.Tests

open System
open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Primitives
open FSharp.Graphics.Scene
open FSharp.Graphics.Charts

let mutable private failures = 0
let mutable private count = 0

let private check (name: string) (ok: bool) =
    count <- count + 1
    if not ok then
        failures <- failures + 1
        printfn "  FAIL  %s" name

let private near (a: float) (b: float) = abs (a - b) < 1.0e-6 * (1.0 + abs a + abs b)

/// A deterministic stream of floats in [0, 1): the same numbers under .NET and in WebAssembly.
let private stream (seed: int) : unit -> float32 =
    let mutable s = uint32 seed
    fun () ->
        s <- s * 1664525u + 1013904223u
        float32 (s >>> 8) / 16777216.0f

let private samples (seed: int) (n: int) : float32[] =
    let next = stream seed
    Array.init n (fun _ -> next () * 200.0f - 100.0f)

let private brute (ys: float32[]) (l: int) (r: int) : Span =
    let l = max 0 l
    let r = min ys.Length r
    let mutable lo = infinityf
    let mutable hi = -infinityf
    for i in l .. r - 1 do
        if ys.[i] < lo then lo <- ys.[i]
        if ys.[i] > hi then hi <- ys.[i]
    { Lo = lo; Hi = hi }

let private scaleTests () =
    let s = Linear.create 0.0 100.0 10.0f 210.0f
    check "a value maps to its pixel" (Linear.map s 50.0 = 110.0f)
    check "a pixel maps back to its value" (near (Linear.invert s 110.0f) 50.0)
    check "the round trip of 40 values" ([ for i in 0 .. 39 -> float i * 2.5 ] |> List.forall (fun v -> near (Linear.invert s (Linear.map s v)) v))
    check "a domain without extent maps to the middle of the range" (Linear.map (Linear.create 5.0 5.0 0.0f 10.0f) 5.0 = 5.0f)
    check "the domain per pixel" (near (Linear.perPixel s) 0.5)
    let flipped = Linear.create 0.0 1.0 300.0f 100.0f
    check "a range can run backwards (y grows upwards)" (Linear.map flipped 0.25 = 250.0f)
    // zoom and pan
    let limits = (0.0, 1000.0)
    let (a, b) = Domain.zoom limits 1.0 0.5 400.0 (200.0, 600.0)
    check "zoom in halves the span" (near (b - a) 200.0)
    check "zoom keeps the anchor where it was" (near ((400.0 - a) / (b - a)) 0.5)
    let (a2, b2) = Domain.zoom limits 1.0 1000.0 400.0 (200.0, 600.0)
    check "zoom out stops at the limits" (a2 = 0.0 && b2 = 1000.0)
    let (a3, b3) = Domain.zoom limits 10.0 0.001 400.0 (200.0, 600.0)
    check "zoom in stops at the narrowest span" (near (b3 - a3) 10.0)
    let (a4, b4) = Domain.pan limits -500.0 (200.0, 600.0)
    check "pan stops at the left limit and keeps the span" (a4 = 0.0 && near (b4 - a4) 400.0)
    let (a5, b5) = Domain.pan limits 5000.0 (200.0, 600.0)
    check "pan stops at the right limit" (b5 = 1000.0 && near (b5 - a5) 400.0)
    // ticks
    check "five ticks over 0..1 are 0.2 apart" (near (Ticks.step 0.0 1.0 5) 0.2)
    check "five ticks over 0..10 are 2 apart" (near (Ticks.step 0.0 10.0 5) 2.0)
    check "five ticks over 0..7 are 1 apart, over 0..8 2 apart" (near (Ticks.step 0.0 7.0 5) 1.0 && near (Ticks.step 0.0 8.0 5) 2.0)
    check "five ticks over 0..30 are 5 apart" (near (Ticks.step 0.0 30.0 5) 5.0)
    check "five ticks over 0..100 are 20 apart" (near (Ticks.step 0.0 100.0 5) 20.0)
    check "ticks are the multiples inside the range" (Ticks.values 0.3 1.0 0.2 = [| 0.4; 0.6; 0.8; 1.0 |] || (Ticks.values 0.3 1.0 0.2 |> Array.map (fun v -> Math.Round(v, 6))) = [| 0.4; 0.6; 0.8; 1.0 |])
    check "an empty range has no ticks" ((Ticks.values 1.0 0.0 0.2).Length = 0)
    check "the step of an empty or broken range is 1" (Ticks.step 3.0 3.0 5 = 1.0 && Ticks.step nan 3.0 5 = 1.0)
    // text of numbers
    check "fixed point, two digits" (Format.fixedPoint 2 3.14159 = "3.14")
    check "fixed point pads" (Format.fixedPoint 3 2.5 = "2.500")
    check "fixed point rounds up into the integer" (Format.fixedPoint 1 9.96 = "10.0")
    check "fixed point of a negative number" (Format.fixedPoint 1 -2.25 = "-2.3" || Format.fixedPoint 1 -2.25 = "-2.2")
    check "negative zero is zero" (Format.fixedPoint 1 -0.01 = "0.0")
    check "fixed point of no decimals" (Format.fixedPoint 0 41.5 = "42")
    check "decimals of a step" (Format.decimalsFor 0.25 = 2 && Format.decimalsFor 5.0 = 0 && Format.decimalsFor 0.1 = 1 && Format.decimalsFor 2.0 = 0)
    check "grouped thousands" (Format.grouped 10000000.0 = "10 000 000" && Format.grouped 999.0 = "999" && Format.grouped -1234.0 = "-1 234")
    check "a big tick is abbreviated by its size" (Format.tick 500000.0 1500000.0 = "1.5M" && Format.tick 500000.0 500000.0 = "500k" && Format.tick 500000.0 0.0 = "0")
    check "a small tick keeps its digits" (Format.tick 0.25 0.75 = "0.75" && Format.tick 5.0 15.0 = "15")

let private pyramidTests () =
    // every size around a power of two, and the ones that are not
    for n in [ 1; 2; 3; 4; 5; 7; 8; 9; 15; 16; 17; 100; 1000; 1023; 1024; 1025 ] do
        let ys = samples n n
        let p = Pyramid.OfArray ys
        let q = Pyramid(n)
        for v in ys do q.Append v
        let next = stream (n + 7)
        let mutable ok = p.Count = n && q.Count = n
        for _ in 1 .. 300 do
            let l = int (next () * float32 (n + 4)) - 2
            let r = l + int (next () * float32 (n + 4))
            let b = brute ys l r
            let a1 = p.Range(l, r)
            let a2 = q.Range(l, r)
            let same (x: Span) = (x.Lo = b.Lo && x.Hi = b.Hi) || (Span.isEmpty x && Span.isEmpty b)
            if not (same a1 && same a2) then ok <- false
        check (sprintf "the range of any run of %d samples is the one a loop finds, built in one pass or by appending" n) ok
    // a pyramid that is cleared and filled again answers for the new samples only
    let again = Pyramid(64)
    for v in samples 21 64 do again.Append v
    again.Clear()
    let fresh = samples 22 40
    for v in fresh do again.Append v
    let ra = again.Range(0, 40)
    let rb = brute fresh 0 40
    check "a cleared pyramid forgets the old samples" (again.Count = 40 && ra.Lo = rb.Lo && ra.Hi = rb.Hi && (again.Range(0, 64)).Lo = rb.Lo)
    // a half-built pyramid never reads a block that is not complete
    let ys = samples 5 1000
    let half = Pyramid(1000)
    for i in 0 .. 636 do half.Append ys.[i]
    let mutable ok = true
    for l in 0 .. 40 .. 600 do
        let b = brute (Array.sub ys 0 637) l 700
        let a = half.Range(l, 700)
        if a.Lo <> b.Lo || a.Hi <> b.Hi then ok <- false
    check "a pyramid that is still being filled answers the same as a loop over what is there" ok
    // the point of the structure: an extreme is never lost, however far out the view is
    let big = Array.create 1000000 0.5f
    big.[777777] <- 99.0f
    big.[123456] <- -42.0f
    let pb = Pyramid.OfArray big
    let r = pb.Range(0, 1000000)
    check "one spike and one dip in a million samples are in the range of all of them" (r.Hi = 99.0f && r.Lo = -42.0f)
    let lo = Array.zeroCreate<float32> 1000
    let hi = Array.zeroCreate<float32> 1000
    pb.Columns(0.0, 1000.0, 1000, lo, hi)
    check "and in exactly one column each, the ones they are in" (hi.[777] = 99.0f && lo.[123] = -42.0f && (hi |> Array.filter (fun v -> v = 99.0f)).Length <= 2)
    // columns: the same as a loop, with the sample before each column
    let ys = samples 11 5000
    let p = Pyramid.OfArray ys
    let cols = 237
    let lo = Array.zeroCreate<float32> cols
    let hi = Array.zeroCreate<float32> cols
    let start = 130.5
    let step = 17.3
    p.Columns(start, step, cols, lo, hi)
    let mutable ok = true
    for c in 0 .. cols - 1 do
        let a = int (Math.Floor(start + float c * step))
        let e = int (Math.Floor(start + float (c + 1) * step))
        let b = brute ys (max 0 (a - 1)) (max e (a + 1))
        if lo.[c] <> b.Lo || hi.[c] <> b.Hi then ok <- false
    check "a column holds its samples and the one before" ok
    // columns outside the data are empty, and the data can end in the middle of the view
    let short = Pyramid.OfArray (samples 3 100)
    let lo = Array.zeroCreate<float32> 20
    let hi = Array.zeroCreate<float32> 20
    short.Columns(-50.0, 10.0, 20, lo, hi)
    check "columns before the first sample are empty" (hi.[0] < lo.[0] && hi.[4] < lo.[4])
    check "columns with samples are not" (hi.[5] >= lo.[5] && hi.[14] >= lo.[14])
    check "columns after the last sample are empty" (hi.[15] < lo.[15] && hi.[19] < lo.[19])
    // zoomed in beyond one sample per column: the columns show the segment between two samples, the hull of both
    short.Columns(10.0, 0.25, 20, lo, hi)
    let seg = brute (short.Samples) 9 11
    check "below one sample per column a column holds the segment it is on" (hi.[0] = seg.Hi && lo.[0] = seg.Lo && hi.[3] = seg.Hi && lo.[3] = seg.Lo)

let private densityTests () =
    let n = 20000
    let next = stream 9
    let xs = Array.init n (fun _ -> next () * 10.0f)
    let ys = Array.init n (fun i -> xs.[i] * 0.5f + next ())
    let g = DensityGrid.Build(xs, ys, n, 64, 32)
    check "the grid counts every point" (g.Total = n)
    check "the whole grid is the total" (g.Sum(0, 64, 0, 32) = n)
    check "a rectangle outside holds nothing" (g.Sum(100, 120, 0, 32) = 0 && g.Sum(5, 5, 0, 32) = 0)
    // a rectangle of cells against a count of the points in it
    let kx = 64.0f / (g.X1 - g.X0)
    let ky = 32.0f / (g.Y1 - g.Y0)
    let inCells (c0: int) (c1: int) (r0: int) (r1: int) =
        let mutable k = 0
        for i in 0 .. n - 1 do
            let c = min 63 (int ((xs.[i] - g.X0) * kx))
            let r = min 31 (int ((ys.[i] - g.Y0) * ky))
            if c >= c0 && c < c1 && r >= r0 && r < r1 then k <- k + 1
        k
    check "a rectangle of cells holds the points of those cells" (g.Sum(10, 40, 3, 20) = inCells 10 40 3 20 && g.Sum(0, 1, 0, 1) = inCells 0 1 0 1 && g.Sum(63, 64, 31, 32) = inCells 63 64 31 32)
    // the window: the whole extent keeps the count
    let out = Array.zeroCreate<float32> (16 * 8)
    let best = g.Window(float g.X0, float g.X1, float g.Y0, float g.Y1, 16, 8, out)
    let total = out |> Array.sumBy (fun d -> float d) 
    check "a raster of the whole extent holds the points (density times the 16 grid cells of a raster cell)" (abs (total * 16.0 - float n) < 1.0)
    check "its largest density is the largest of the raster" (best = Array.max out && best > 0.0f)
    // row 0 is the top: the points rise with x, so the top row is on the right and the bottom row on the left
    let rowSum (j: int) (from: int) (upto: int) = [ for i in from .. upto - 1 -> out.[j * 16 + i] ] |> List.sum
    check "row 0 is the highest y" (rowSum 0 8 16 > rowSum 0 0 8 && rowSum 7 0 8 > rowSum 7 8 16)
    // a window outside the data is empty
    let none = g.Window(1000.0, 1010.0, 1000.0, 1010.0, 4, 4, Array.zeroCreate 16)
    check "a window beside the data is empty" (none = 0.0f)
    // points that are not numbers are left out
    let g2 = DensityGrid.Build([| 1.0f; nanf; 2.0f; infinityf |], [| 1.0f; 1.0f; nanf; 3.0f |], 4, 4, 4)
    check "NaN and infinity are not counted" (g2.Total = 1)
    check "no points: a grid that is empty" ((DensityGrid.Build([||], [||], 0, 4, 4)).Total = 0)
    // levels and runs
    check "no density is level 0" (Heat.level 16 5.0f 0.0f = 0)
    check "any density is at least level 1" (Heat.level 16 1000.0f 0.0001f = 1)
    check "the largest density is the top level" (Heat.level 16 1000.0f 1000.0f = 15)
    check "levels do not fall as density rises" ([ for d in 0 .. 200 -> Heat.level 16 200.0f (float32 d) ] |> List.pairwise |> List.forall (fun (a, b) -> a <= b))
    let lv = [| 0; 2; 2; 2; 0; 3; 0; 0; 1; 1; 0; 5 |]
    let runs = ResizeArray<int * int * int * int>()
    Heat.runs lv 6 2 (fun r c len l -> runs.Add((r, c, len, l)))
    check "runs: equal levels side by side are one shape" (List.ofSeq runs = [ (0, 1, 3, 2); (0, 5, 1, 3); (1, 2, 2, 1); (1, 5, 1, 5) ])
    let ramp = Heatmap.ramp 24
    check "the ramp: nothing is transparent, the rest is not" (ramp.[0].A = 0uy && ramp.[1].A = 255uy && ramp.[23].A = 255uy)
    check "the ramp gets lighter" (int ramp.[23].R + int ramp.[23].G + int ramp.[23].B > int ramp.[1].R + int ramp.[1].G + int ramp.[1].B + 300)

let private pts (l: (float * float) list) : Vec2[] = l |> List.map (fun (x, y) -> Vec2.create (float32 x) (float32 y)) |> Array.ofList

let private geometryTests () =
    let r = Geometry.ribbon (pts [ (0.0, 10.0); (50.0, 10.0); (100.0, 10.0) ]) 4.0f
    check "a straight ribbon has two vertices per point" (r.Length = 6)
    check "its edges are half the width from the line" (r |> Array.forall (fun v -> abs (v.Y - 10.0f) = 2.0f))
    check "it covers the line's length" (r |> Array.map (fun v -> v.X) |> Array.min = 0.0f && (r |> Array.map (fun v -> v.X) |> Array.max) = 100.0f)
    check "fewer than two points: no ribbon" ((Geometry.ribbon [||] 2.0f).Length = 0 && (Geometry.ribbon (pts [ (1.0, 1.0) ]) 2.0f).Length = 0)
    let rep = Geometry.ribbon (pts [ (0.0, 0.0); (10.0, 0.0); (10.0, 0.0); (20.0, 0.0) ]) 2.0f
    check "a repeated point does not break the line" (rep |> Array.forall (fun v -> abs v.Y <= 1.0f + 1.0e-4f) && rep |> Array.forall (fun v -> not (Single.IsNaN v.X || Single.IsNaN v.Y)))
    // a corner of 90 degrees is mitered: the outer vertex is at the corner shifted by half in both axes
    let c = Geometry.ribbon (pts [ (0.0, 0.0); (10.0, 0.0); (10.0, 10.0) ]) 2.0f
    check "a right angle is mitered" (c |> Array.exists (fun v -> abs (v.X - 11.0f) < 1.0e-4f && abs (v.Y + 1.0f) < 1.0e-4f))
    // a hairpin is cut off and does not shoot out
    let hp = Geometry.ribbon (pts [ (0.0, 0.0); (100.0, 0.0); (0.5, 0.2) ]) 2.0f
    check "a hairpin turn is cut at three half widths" (hp |> Array.forall (fun v -> v.X < 100.0f + 3.1f && v.X > -3.1f))
    let band = Geometry.band 10.0f 1.0f 2 4 [| 0.0f; 0.0f; 5.0f; 6.0f; 7.0f |] [| 0.0f; 0.0f; 8.0f; 9.0f; 10.0f |]
    check "a band goes along the top and back along the bottom" (band.Length = 6 && band.[0] = Vec2.create 12.5f 5.0f && band.[2] = Vec2.create 14.5f 7.0f && band.[3] = Vec2.create 14.5f 10.0f && band.[5] = Vec2.create 12.5f 8.0f)
    check "a band of no column is nothing" ((Geometry.band 0.0f 1.0f 3 2 [||] [||]).Length = 0)
    let a = Geometry.area (pts [ (0.0, 5.0); (10.0, 3.0) ]) 20.0f
    check "an area is closed down to the baseline" (a.Length = 4 && a.[2] = Vec2.create 10.0f 20.0f && a.[3] = Vec2.create 0.0f 20.0f)

/// the polygons of a scene
let rec private polygons (s: Scene) : Vec2[] list =
    match s with
    | Shape(Path(sdf, _, _, _)) -> (match sdf.Geometry with GPolygon vs -> [ vs ] | _ -> [])
    | Group cs -> cs |> List.collect polygons
    | Clip(_, c) | Transform(_, c) | Opacity(_, c) | Semantic(_, c) -> polygons c
    | _ -> []

let rec private shapes (s: Scene) : int =
    match s with
    | Shape _ -> 1
    | Group cs -> cs |> List.sumBy shapes
    | Clip(_, c) | Transform(_, c) | Opacity(_, c) | Semantic(_, c) -> shapes c
    | _ -> 0

let private sceneTests () =
    let th = Theme.dark (FontId 0UL)
    let n = 1000000
    let ys = Array.init n (fun i -> float32 (Math.Sin(float i * 0.0005)) * 50.0f + float32 (i % 7))
    let data = Pyramid.OfArray ys
    let series = { Name = "s"; Color = th.Palette.[0]; Width = 2.0f; Fill = true; Data = data; X0 = 0.0; Dx = 1.0 }
    let frame = Frame.create 800.0f 400.0f 60.0f 20.0f 20.0f 40.0f (0.0, float n) (-60.0, 70.0)
    check "the plot sits on whole pixels" (frame.Plot.X = 60.0f && frame.Plot.Y = 20.0f && frame.Plot.W = 720.0f && frame.Plot.H = 340.0f)
    check "x runs right, y runs up" (Linear.map frame.X 0.0 = 60.0f && Linear.map frame.X (float n) = 780.0f && Linear.map frame.Y -60.0 = 360.0f && Linear.map frame.Y 70.0 = 20.0f)
    let dense = Line.scene frame series
    let polys = polygons dense
    check "a million samples are a band of 720 columns: two polygons (the fill and the band)" (polys.Length = 2)
    check "the band has at most two vertices per column" (polys |> List.forall (fun p -> p.Length <= 2 * 720 + 2))
    // zoomed in to 40 samples the line is a ribbon through them, with a dot on each
    let zoomed = Frame.create 800.0f 400.0f 60.0f 20.0f 20.0f 40.0f (1000.0, 1040.0) (-60.0, 70.0)
    let sparse = Line.scene zoomed series
    let sp = polygons sparse
    check "a zoomed-in line is a ribbon through its samples" (sp.Length = 2 && sp |> List.exists (fun p -> p.Length = 2 * 43))
    check "with a dot on each sample" (shapes sparse - sp.Length >= 40)
    // outside the data
    let beyond = Frame.create 800.0f 400.0f 60.0f 20.0f 20.0f 40.0f (2.0e6, 3.0e6) (-60.0, 70.0)
    check "a view beside the data draws nothing" (polygons (Line.scene beyond series) |> List.isEmpty)
    // the y range follows the view
    let (y0, y1) = Line.yRange [ series ] (0.0, 100.0) 0.05
    let b = brute ys 0 102
    check "the y range of a view is the range of its samples with a margin" (near y0 (float b.Lo - 0.05 * float (b.Hi - b.Lo)) && near y1 (float b.Hi + 0.05 * float (b.Hi - b.Lo)))
    // candles
    let next = stream 4
    let m = 5000
    let close = Array.zeroCreate<float32> m
    let opn = Array.zeroCreate<float32> m
    let high = Array.zeroCreate<float32> m
    let low = Array.zeroCreate<float32> m
    let mutable price = 100.0f
    for i in 0 .. m - 1 do
        opn.[i] <- price
        price <- price + (next () - 0.5f) * 2.0f
        close.[i] <- price
        high.[i] <- max opn.[i] close.[i] + next ()
        low.[i] <- min opn.[i] close.[i] - next ()
    let cs = { Open = opn; Close = close; High = Pyramid.OfArray high; Low = Pyramid.OfArray low; X0 = 0.0; Dx = 1.0 }
    let cf = Frame.create 800.0f 400.0f 60.0f 20.0f 20.0f 40.0f (0.0, float m) (70.0, 130.0)
    let all = Candles.scene th cf cs
    check "5000 candles in 720 columns are at most two shapes per column" (shapes all <= 2 * 720 && shapes all >= 2 * 700)
    let few = Candles.scene th (Frame.create 800.0f 400.0f 60.0f 20.0f 20.0f 40.0f (100.0, 130.0) (70.0, 130.0)) cs
    check "30 candles are two shapes each" (shapes few = 2 * 31 || shapes few = 2 * 30 || shapes few = 2 * 32)
    // bars
    let bf = Frame.create 400.0f 300.0f 50.0f 10.0f 10.0f 30.0f (0.0, 1.0) (0.0, 100.0)
    let bars = Bars.scene th bf [| ("a", 10.0f); ("b", 50.0f); ("c", -20.0f) |] th.Palette
    check "a bar for each item" (shapes bars = 3)
    // heatmap of the points: as many shapes as runs, fewer than cells
    let xsP = Array.init 50000 (fun i -> float32 (Math.Sin(float i)) * 3.0f)
    let ysP = Array.init 50000 (fun i -> float32 (Math.Cos(float i * 1.7)) * float32 (i % 5))
    let grid = DensityGrid.Build(xsP, ysP, 50000, 256, 256)
    let hf = Frame.create 640.0f 360.0f 50.0f 10.0f 10.0f 30.0f (float grid.X0, float grid.X1) (float grid.Y0, float grid.Y1)
    let hm = Heatmap.scene hf grid 4 (Heatmap.ramp 24)
    check "a density map is shapes, fewer than its cells" (hm.Length > 100 && hm.Length < (580 / 4) * (320 / 4))
    // the crosshair and the legend and the description make scenes
    check "a crosshair has its lines, marks and card" (shapes (Parts.crosshair th frame 200.0f 100.0f [ (200.0f, 100.0f, th.Palette.[0]) ] [ ("x 5", th.Text) ]) >= 5)
    check "a legend has a swatch per item" (shapes (Parts.legend th 0.0f 0.0f [ ("a", th.Palette.[0]); ("b", th.Palette.[1]) ]) = 2)
    check "axes have a grid" (shapes (Parts.axes th frame (fun s v -> Format.tick s v) (fun s v -> Format.tick s v)) >= 10)
    match Parts.describe "c" "Chart" "A line" Empty with
    | Semantic(sem, _) -> check "the chart is an image with a name and a description" (sem.Role = Role.Image && sem.Name = "Chart" && sem.Description = "A line")
    | _ -> check "the chart is described" false

let private instrumentTests () =
    let th = Theme.dark (FontId 0UL)
    let isEmpty (s: Scene) = match s with Empty -> true | _ -> false
    let rec shapes (s: Scene) : int =
        match s with
        | Shape _ -> 1
        | Group cs -> cs |> List.sumBy shapes
        | Semantic(_, c) | Transform(_, c) | Clip(_, c) -> shapes c
        | _ -> 0
    let semantics (s: Scene) = match s with Semantic(sem, _) -> Some sem | _ -> None

    check "instruments: a fraction is where a value is in its range, and 0 for what is not a number or a range that is none"
        (near (Instruments.fraction 0.0 100.0 50.0) 0.5 && Instruments.fraction 0.0 100.0 250.0 = 1.0 && Instruments.fraction 0.0 100.0 -5.0 = 0.0
         && Instruments.fraction 0.0 100.0 nan = 0.0 && Instruments.fraction 5.0 5.0 5.0 = 0.0)
    check "instruments: a value is shown with its unit, three digits at most, and a dash when it is not a number"
        (Instruments.show "\u00B0C" 75.0 = "75.0 \u00B0C" && Instruments.show "bar" 123.4 = "123 bar" && Instruments.show "" 5.0 = "5.00" && Instruments.show "bar" nan = "\u2014" && Instruments.show "bar" infinity = "\u2014")
    let ring = Instruments.arc 50.0f 50.0f 30.0f 40.0f 135.0f 405.0f
    check "instruments: a ring segment is a polygon between its two radii" (ring.Length >= 8 && ring |> Array.forall (fun p -> let d = MathF.Sqrt((p.X - 50.0f) * (p.X - 50.0f) + (p.Y - 50.0f) * (p.Y - 50.0f)) in d > 29.99f && d < 40.01f))

    let spec = { Min = 0.0; Max = 100.0; Value = 80.0; Zones = [ (0.0, 70.0, Normal); (70.0, 90.0, Warning); (90.0, 100.0, Alarm) ]; Label = "Temperature"; Unit = "\u00B0C"; Tone = Warning }
    let g = Instruments.gauge th "t1" 200.0f spec
    check "instruments: a gauge is a meter with its name, its range, its value and its state in words"
        (match semantics g with
         | Some s -> s.Role = Role.ProgressBar && s.Name = "Temperature" && s.Min = 0.0f && s.Max = 100.0f && s.Now = 80.0f && s.Value = "80.0 \u00B0C, warning"
         | None -> false)
    let bad = Instruments.gauge th "t1" 200.0f { spec with Value = nan; Tone = Normal }
    check "instruments: a reading that is not a number draws no needle and no arc, says a dash and says it is not trusted"
        (shapes bad < shapes g && (match semantics bad with Some s -> s.Value.Contains "\u2014" && s.Value.Contains "not trusted" | None -> false))
    check "instruments: a range of no width, a value outside the range and a gauge of no size do not fail"
        (shapes (Instruments.gauge th "t" 100.0f { spec with Min = 5.0; Max = 5.0 }) > 0 && shapes (Instruments.gauge th "t" 100.0f { spec with Value = 1.0e9 }) > 0 && (Instruments.gauge th "t" 0.0f spec |> shapes) > 0)

    let tank = { Min = 0.0; Max = 10.0; Value = 7.5; Marks = [ (9.0, Alarm); (8.0, Warning); (2.0, Warning) ]; Label = "Tank 1"; Unit = "m"; Tone = Normal }
    let t0 = Instruments.level th "l1" 60.0f 200.0f { tank with Marks = [] }
    let t3 = Instruments.level th "l1" 60.0f 200.0f tank
    check "instruments: a level has a line across it at each mark" (shapes t3 = shapes t0 + 3)
    check "instruments: a level is a meter: its value is 7.50 m" (match semantics t3 with Some s -> s.Name = "Tank 1" && s.Value = "7.50 m" && s.Now = 7.5f | None -> false)

    let off = Instruments.lamp th "p1" 12.0f Normal false "Pump 1"
    let on = Instruments.lamp th "p1" 12.0f Alarm true "Pump 1"
    check "instruments: a lamp says in words whether it is lit and in which tone"
        (match semantics off, semantics on with Some a, Some b -> a.Description = "off" && b.Description = "on, alarm" && b.Name = "Pump 1" | _ -> false)
    check "instruments: a lit lamp has the ring of light, a dark one has not" (shapes on > shapes off)

    check "instruments: a sparkline of fewer than two values is nothing, of a flat series is a line, of a series is a line and a dot"
        (isEmpty (Instruments.sparkline 100.0f 30.0f th.Up [| 1.0f |]) && shapes (Instruments.sparkline 100.0f 30.0f th.Up [| 3.0f; 3.0f; 3.0f |]) = 2 && shapes (Instruments.sparkline 100.0f 30.0f th.Up [| 1.0f; 4.0f; 2.0f; 8.0f |]) = 2)
    let card = Instruments.kpi th "k1" 220.0f 90.0f "Throughput" "1 204 / h" "+4.2 % since the hour" Normal [| 1.0f; 2.0f; 1.5f; 3.0f |]
    check "instruments: a key figure is named with its value, and its change and its state are its description"
        (match semantics card with Some s -> s.Name = "Throughput: 1 204 / h" && s.Description = "+4.2 % since the hour, normal" | None -> false)

let run () : int =
    scaleTests ()
    pyramidTests ()
    densityTests ()
    geometryTests ()
    sceneTests ()
    instrumentTests ()
    printfn "%d checks, %d failed" count failures
    failures

#if FABLE_COMPILER
let charts_run () : int = run ()
/// how many checks the last run made (the host checks that it is not zero)
let charts_checks () : int = count
#else
[<EntryPoint>]
let main _ = if run () = 0 then 0 else 1
#endif
