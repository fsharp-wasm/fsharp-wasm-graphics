namespace FSharp.Graphics.Charts

open System
open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Primitives
open FSharp.Graphics.Scene

/// Points as markers, coloured by index or by value; past `MaxMarkers` inside the view a density map takes their place.
module Scatter =
    /// The most markers one scene holds: a marker is a shape, and a scene of a hundred thousand of them is more than a frame can be.
    [<Literal>]
    let MaxMarkers = 20000

    let private bounds (f: Frame) =
        (min f.X.D0 f.X.D1, max f.X.D0 f.X.D1, min f.Y.D0 f.Y.D1, max f.Y.D0 f.Y.D1)

    /// How many of the first `n` points lie inside the view (a point with a NaN does not).
    let visible (f: Frame) (xs: float32[]) (ys: float32[]) (n: int) : int =
        let (xl, xh, yl, yh) = bounds f
        let mutable k = 0
        for i in 0 .. n - 1 do
            let x = float xs.[i]
            let y = float ys.[i]
            if x >= xl && x <= xh && y >= yl && y <= yh then k <- k + 1
        k

    /// The colour of point i: one colour for all, or a colour map over a value of each.
    let uniform (c: Color) : int -> Color = fun _ -> c
    let byValue (m: Colormap) (lo: float) (hi: float) (values: float32[]) : int -> Color = fun i -> Colormap.scaled m lo hi (float values.[i])

    /// A marker of `radius` pixels for each of the first `n` points inside the view, at most `MaxMarkers` (the first ones: use `auto` when there can be more).
    let scene (f: Frame) (xs: float32[]) (ys: float32[]) (n: int) (radius: float32) (color: int -> Color) : Scene =
        let (xl, xh, yl, yh) = bounds f
        let out = ResizeArray<Scene>()
        let mutable i = 0
        while i < n && out.Count < MaxMarkers do
            let x = float xs.[i]
            let y = float ys.[i]
            if x >= xl && x <= xh && y >= yl && y <= yh then
                out.Add(Parts.dot (Linear.map f.X x) (Linear.map f.Y y) radius (color i))
            i <- i + 1
        Group (List.ofArray (out.ToArray()))

    /// Markers while at most `MaxMarkers` points are in view, else a density map of all of them (a grid of 256 x 256 cells over their extent, in cells of 4 pixels) on the colour map.
    let auto (f: Frame) (xs: float32[]) (ys: float32[]) (n: int) (radius: float32) (color: int -> Color) (m: Colormap) : Scene =
        if visible f xs ys n <= MaxMarkers then scene f xs ys n radius color
        else
            let grid = DensityGrid.Build(xs, ys, n, 256, 256)
            Group (Heatmap.scene f grid 4 (Colormap.ramp m 12))

/// The area between a lower and an upper curve over the same x: a confidence band, a range, a standard deviation either side of a mean.
module Bands =
    /// One polygon: the upper edge from left to right, the lower back.  `xs` rise; more points than twice the plot's width are thinned to a regular stride (a band is smooth at that scale).
    let scene (f: Frame) (xs: float32[]) (lower: float32[]) (upper: float32[]) (n: int) (color: Color) : Scene =
        let p = f.Plot
        let stride = max 1 (n / max 1 (2 * int p.W))
        let idx = ResizeArray<int>()
        let mutable i = 0
        while i < n do
            let x = xs.[i]
            if x - x = 0.0f && lower.[i] - lower.[i] = 0.0f && upper.[i] - upper.[i] = 0.0f then idx.Add i
            i <- i + stride
        let k = idx.Count
        if k < 2 then Empty
        else
            let pts = Array.zeroCreate<Vec2> (2 * k)
            for j in 0 .. k - 1 do
                let at = idx.[j]
                pts.[j] <- Vec2.create (Linear.map f.X (float xs.[at])) (Linear.map f.Y (float upper.[at]))
                pts.[2 * k - 1 - j] <- Vec2.create (Linear.map f.X (float xs.[at])) (Linear.map f.Y (float lower.[at]))
            Scene.Clip(p, Parts.polygon pts color)

/// Error bars: a vertical line from y - e to y + e at each point, with a cap at both ends.
module ErrorBars =
    [<Literal>]
    let MaxBars = 4000

    /// `err.[i]` is the half length of the bar of point i (a negative or NaN error draws none).  At most `MaxBars` inside the view.
    let scene (f: Frame) (xs: float32[]) (ys: float32[]) (err: float32[]) (n: int) (color: Color) (cap: float32) : Scene =
        let xl = min f.X.D0 f.X.D1
        let xh = max f.X.D0 f.X.D1
        let out = ResizeArray<Scene>()
        let mutable i = 0
        while i < n && out.Count < MaxBars * 3 do
            let x = float xs.[i]
            let y = float ys.[i]
            let e = float err.[i]
            if x >= xl && x <= xh && y - y = 0.0 && e >= 0.0 && e - e = 0.0 then
                let px = MathF.Round(Linear.map f.X x)
                let top = Linear.map f.Y (y + e)
                let bottom = Linear.map f.Y (y - e)
                out.Add(Parts.box px (min top bottom) 1.0f (max 1.0f (abs (bottom - top))) color)
                out.Add(Parts.box (px - cap * 0.5f) (MathF.Round top) cap 1.0f color)
                out.Add(Parts.box (px - cap * 0.5f) (MathF.Round bottom) cap 1.0f color)
            i <- i + 1
        Scene.Clip(f.Plot, Group (List.ofArray (out.ToArray())))

/// Box plots over named groups: the box from the first to the third quartile with the median across it, the whiskers, and the outliers as dots.  The groups share the plot's width, as bars do.
module BoxPlot =
    let scene (th: Theme) (f: Frame) (names: string[]) (boxes: BoxStats[]) (colors: Color[]) : Scene =
        let p = f.Plot
        let n = max 1 boxes.Length
        let slot = p.W / float32 n
        let y (v: float) = MathF.Round(Linear.map f.Y v)
        Group [ for i in 0 .. boxes.Length - 1 do
                    let b = boxes.[i]
                    if b.Count > 0 then
                        let cx = MathF.Round(p.X + slot * (float32 i + 0.5f))
                        let half = MathF.Round(slot * 0.25f)
                        let c = colors.[i % colors.Length]
                        let q1 = y b.Q1
                        let q3 = y b.Q3
                        yield Parts.box cx (y b.HighWhisker) 1.0f (max 1.0f (q3 - y b.HighWhisker)) th.Muted
                        yield Parts.box cx q1 1.0f (max 1.0f (y b.LowWhisker - q1)) th.Muted
                        yield Parts.box (cx - half * 0.5f) (y b.HighWhisker) half 1.0f th.Muted
                        yield Parts.box (cx - half * 0.5f) (y b.LowWhisker) half 1.0f th.Muted
                        yield Parts.box (cx - half) q3 (half * 2.0f) (max 1.0f (q1 - q3)) (Color.withAlpha 0.55f c)
                        yield Parts.box (cx - half) (y b.Median) (half * 2.0f) 2.0f th.Text
                        for o in b.Outliers do yield Parts.dot cx (Linear.map f.Y o) 2.5f c
                    if i < names.Length then yield Parts.text th th.TextPx th.Muted TextAlign.Center (p.X + slot * float32 i) (p.Y + p.H + 6.0f) slot names.[i] ]

/// The bars of a histogram: one per bin from its lower edge to its upper, from zero up to its count.
module HistogramBars =
    /// The y range that shows every bin with a little room above.
    let yRange (h: Histogram) : float * float = (0.0, float (max 1 h.Max) * 1.08)

    let scene (f: Frame) (h: Histogram) (color: Color) : Scene =
        let zero = Linear.map f.Y 0.0
        Scene.Clip(f.Plot,
            Group [ for i in 0 .. h.Bins - 1 do
                        let c = h.Counts.[i]
                        if c > 0 then
                            let x0 = MathF.Round(Linear.map f.X (h.Edge i))
                            let x1 = MathF.Round(Linear.map f.X (h.Edge(i + 1)))
                            let top = Linear.map f.Y (float c)
                            yield Parts.box x0 top (max 1.0f (x1 - x0 - 1.0f)) (max 1.0f (zero - top)) color ])

/// A field of values on a regular grid, drawn as coloured cells.  `field.[r * cols + c]` is the value at node (c, r): the nodes lie `(x1 - x0) / (cols - 1)` apart from `x0` to `x1` and `(y1 - y0) / (rows - 1)`
/// from `y0` (row 0, the bottom) to `y1`; a cell is centred on its node, so a raster and the contours of the same field lie on each other.
module Raster =
    /// The number of colours of a raster: a value is rounded to one of them, and a row of cells of one colour is one rectangle.
    [<Literal>]
    let Levels = 64

    /// The field of `cols x rows` nodes averaged down to at most `maxCols x maxRows` (blocks of whole nodes; a block's mean is of its finite values, NaN if it has none).
    let downsample (field: float32[]) (cols: int) (rows: int) (maxCols: int) (maxRows: int) : float32[] * int * int =
        let bc = max 1 ((cols + maxCols - 1) / max 1 maxCols)
        let br = max 1 ((rows + maxRows - 1) / max 1 maxRows)
        if bc = 1 && br = 1 then (field, cols, rows)
        else
            let oc = (cols + bc - 1) / bc
            let orows = (rows + br - 1) / br
            let out = Array.zeroCreate<float32> (oc * orows)
            for r in 0 .. orows - 1 do
                for c in 0 .. oc - 1 do
                    let mutable sum = 0.0
                    let mutable k = 0
                    for rr in r * br .. min rows ((r + 1) * br) - 1 do
                        for cc in c * bc .. min cols ((c + 1) * bc) - 1 do
                            let v = field.[rr * cols + cc]
                            if v - v = 0.0f then (sum <- sum + float v; k <- k + 1)
                    out.[r * oc + c] <- if k = 0 then nanf else float32 (sum / float k)
            (out, oc, orows)

    /// The smallest and largest finite value of the field (0, 1 when there is none).
    let range (field: float32[]) : float * float =
        let mutable lo = infinity
        let mutable hi = -infinity
        for v in field do
            if v - v = 0.0f then
                if float v < lo then lo <- float v
                if float v > hi then hi <- float v
        if lo > hi then (0.0, 1.0) else (lo, hi)

    /// The cells, from `lo` (the first colour of the map) to `hi` (the last); NaN cells are left out.  Inside the plot.
    let scene (f: Frame) (x0: float, x1: float) (y0: float, y1: float) (field: float32[]) (cols: int) (rows: int) (m: Colormap) (lo: float, hi: float) : Scene =
        let dx = if cols > 1 then (x1 - x0) / float (cols - 1) else (x1 - x0)
        let dy = if rows > 1 then (y1 - y0) / float (rows - 1) else (y1 - y0)
        let colors = Colormap.ramp m (Levels + 1)
        let lv = Array.zeroCreate<int> (cols * rows)
        for i in 0 .. cols * rows - 1 do
            let v = float field.[i]
            lv.[i] <- if Double.IsNaN v then 0 else 1 + int (Math.Round(max 0.0 (min 1.0 (if hi = lo then 0.5 else (v - lo) / (hi - lo))) * float (Levels - 1)))
        let xEdge (c: int) = MathF.Round(Linear.map f.X (x0 + (float c - 0.5) * dx))
        let yEdge (r: int) = MathF.Round(Linear.map f.Y (y0 + (float r - 0.5) * dy))
        let out = ResizeArray<Scene>()
        Heat.runs lv cols rows (fun r c len l ->
            let xa = xEdge c
            let xb = xEdge (c + len)
            let ya = yEdge (r + 1)
            let yb = yEdge r
            out.Add(Parts.box (min xa xb) (min ya yb) (max 1.0f (abs (xb - xa))) (max 1.0f (abs (yb - ya))) colors.[l]))
        Scene.Clip(f.Plot, Group (List.ofArray (out.ToArray())))

/// A vertical colour bar: the colour map from `hi` (top) to `lo` (bottom), with ticks at its right.
module ColorBar =
    let scene (th: Theme) (m: Colormap) (lo: float, hi: float) (x: float32) (y: float32) (w: float32) (h: float32) (label: float -> float -> string) : Scene =
        let slices = max 2 (min 128 (int h))
        let sliceH = h / float32 slices
        let step = Ticks.step lo hi (max 2 (int (h / 40.0f)))
        let ticks = Ticks.values lo hi step
        Group [ for i in 0 .. slices - 1 do
                    let t = 1.0 - (float i + 0.5) / float slices
                    yield Parts.box x (MathF.Round(y + sliceH * float32 i)) w (sliceH + 0.5f) (Colormap.at m t)
                for v in ticks do
                    let ty = y + h * float32 (1.0 - (if hi = lo then 0.5 else (v - lo) / (hi - lo)))
                    yield Parts.box (x + w) (MathF.Round ty) 4.0f 1.0f th.Muted
                    yield Parts.text th th.TextPx th.Muted TextAlign.Left (x + w + 8.0f) (ty - th.TextPx * 0.8f) 60.0f (label step v) ]

/// Contour lines of a field (the grid of `Raster`): marching squares, the segments of each level chained into polylines.
module Contour =
    /// `levels` nice values between `lo` and `hi` (a step of 1, 2 or 5 times a power of ten), those inside the range only.
    let niceLevels (lo: float) (hi: float) (count: int) : float[] =
        if not (hi > lo) then [||] else Ticks.values lo hi (Ticks.step lo hi count)

    // an edge of the grid, by number: horizontal edge (c, r)-(c+1, r) is 2 * (r * cols + c), vertical edge (c, r)-(c, r+1) is that + 1
    [<Struct>]
    type private Seg = { A: int; B: int; Ax: float32; Ay: float32; Bx: float32; By: float32 }

    /// The segments of the level set of the field at `level`, as pairs of points in grid coordinates (node (c, r) is at (c, r)).  A cell with a NaN corner has none.
    let private segments (field: float32[]) (cols: int) (rows: int) (level: float32) : Seg[] =
        let out = ResizeArray<Seg>()
        let v (c: int) (r: int) = field.[r * cols + c]
        for r in 0 .. rows - 2 do
            for c in 0 .. cols - 2 do
                let v00 = v c r
                let v10 = v (c + 1) r
                let v11 = v (c + 1) (r + 1)
                let v01 = v c (r + 1)
                if v00 - v00 = 0.0f && v10 - v10 = 0.0f && v11 - v11 = 0.0f && v01 - v01 = 0.0f then
                    let code = (if v00 >= level then 1 else 0) + (if v10 >= level then 2 else 0) + (if v11 >= level then 4 else 0) + (if v01 >= level then 8 else 0)
                    if code <> 0 && code <> 15 then
                        let fc = float32 c
                        let fr = float32 r
                        let t (a: float32) (b: float32) = (level - a) / (b - a)
                        // the four edges of the cell: where the level crosses each, and the number of that edge
                        let bottomId = 2 * (r * cols + c)
                        let topId = 2 * ((r + 1) * cols + c)
                        let leftId = 2 * (r * cols + c) + 1
                        let rightId = 2 * (r * cols + c + 1) + 1
                        let bottom = (bottomId, fc + t v00 v10, fr)
                        let top = (topId, fc + t v01 v11, fr + 1.0f)
                        let left = (leftId, fc, fr + t v00 v01)
                        let right = (rightId, fc + 1.0f, fr + t v10 v11)
                        let add (ia, xa, ya) (ib, xb, yb) = out.Add { A = ia; B = ib; Ax = xa; Ay = ya; Bx = xb; By = yb }
                        let centreHigh = (v00 + v10 + v11 + v01) * 0.25f >= level
                        match code with
                        | 1 | 14 -> add left bottom
                        | 2 | 13 -> add bottom right
                        | 3 | 12 -> add left right
                        | 4 | 11 -> add right top
                        | 6 | 9 -> add bottom top
                        | 7 | 8 -> add left top
                        | 5 -> if centreHigh then (add bottom right; add left top) else (add left bottom; add right top)
                        | _ -> if centreHigh then (add left bottom; add right top) else (add bottom right; add left top)    // 10
        out.ToArray()

    /// The polylines of the level, in grid coordinates; a closed line ends where it began.
    let lines (field: float32[]) (cols: int) (rows: int) (level: float32) : Vec2[] list =
        let segs = segments field cols rows level
        // the segments at each edge (an edge is shared by two cells, so at most two)
        let at = System.Collections.Generic.Dictionary<int, ResizeArray<int>>()
        let note (edge: int) (i: int) =
            if not (at.ContainsKey edge) then at.[edge] <- ResizeArray<int>()
            at.[edge].Add i
        for i in 0 .. segs.Length - 1 do
            note segs.[i].A i
            note segs.[i].B i
        let used = Array.zeroCreate<bool> segs.Length
        // the segment other than `i` at `edge`, not yet used
        let next (edge: int) (i: int) : int =
            let mutable found = -1
            for j in at.[edge] do
                if j <> i && not used.[j] && found < 0 then found <- j
            found
        let result = ResizeArray<Vec2[]>()
        for seed in 0 .. segs.Length - 1 do
            if not used.[seed] then
                used.[seed] <- true
                let forward = ResizeArray<Vec2>()
                let backward = ResizeArray<Vec2>()
                forward.Add(Vec2.create segs.[seed].Ax segs.[seed].Ay)
                forward.Add(Vec2.create segs.[seed].Bx segs.[seed].By)
                // follow from the B end
                let mutable edge = segs.[seed].B
                let mutable cur = seed
                let mutable go = true
                while go do
                    let j = next edge cur
                    if j < 0 then go <- false
                    else
                        used.[j] <- true
                        let s = segs.[j]
                        if s.A = edge then (forward.Add(Vec2.create s.Bx s.By); edge <- s.B) else (forward.Add(Vec2.create s.Ax s.Ay); edge <- s.A)
                        cur <- j
                // follow from the A end (a closed line has been used up)
                edge <- segs.[seed].A
                cur <- seed
                go <- true
                while go do
                    let j = next edge cur
                    if j < 0 then go <- false
                    else
                        used.[j] <- true
                        let s = segs.[j]
                        if s.A = edge then (backward.Add(Vec2.create s.Bx s.By); edge <- s.B) else (backward.Add(Vec2.create s.Ax s.Ay); edge <- s.A)
                        cur <- j
                let all = Array.append (Array.rev (backward.ToArray())) (forward.ToArray())
                result.Add all
        List.ofArray (result.ToArray())

    /// The contours of the field at each level, as lines `width` pixels wide coloured by the level on the map from `lo` to `hi`.  Inside the plot.
    let scene (f: Frame) (x0: float, x1: float) (y0: float, y1: float) (field: float32[]) (cols: int) (rows: int) (levels: float[]) (m: Colormap) (lo: float, hi: float) (width: float32) : Scene =
        let kx = if cols > 1 then (x1 - x0) / float (cols - 1) else (x1 - x0)
        let ky = if rows > 1 then (y1 - y0) / float (rows - 1) else (y1 - y0)
        Scene.Clip(f.Plot,
            Group [ for level in levels do
                        let color = Colormap.scaled m lo hi level
                        for line in lines field cols rows (float32 level) do
                            if line.Length >= 2 then
                                let px = line |> Array.map (fun p -> Vec2.create (Linear.map f.X (x0 + float p.X * kx)) (Linear.map f.Y (y0 + float p.Y * ky)))
                                yield Parts.polygon (Geometry.ribbon px width) color ])
