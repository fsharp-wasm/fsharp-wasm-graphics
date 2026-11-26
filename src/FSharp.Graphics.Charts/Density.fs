namespace FSharp.Graphics.Charts

open System

/// How many points lie in each cell of a fixed grid over the extent of the data, kept as a summed-area table: the points in any rectangle of cells are four reads.  A density map of ten
/// million points is therefore as cheap to draw as one of a hundred, at any zoom; building the grid reads the points once.  (Zooming in further than a cell shows the cells.)
type DensityGrid(x0: float32, x1: float32, y0: float32, y1: float32, cols: int, rows: int, sat: int[]) =
    let stride = cols + 1

    /// The extent the grid covers, and its size in cells.
    member _.X0 = x0
    member _.X1 = x1
    member _.Y0 = y0
    member _.Y1 = y1
    member _.Cols = cols
    member _.Rows = rows
    member _.Total : int = sat.[rows * stride + cols]

    /// The points in the cells [c0, c1) x [r0, r1) (clamped to the grid).
    member _.Sum(c0: int, c1: int, r0: int, r1: int) : int =
        let a = max 0 (min cols c0)
        let b = max 0 (min cols c1)
        let c = max 0 (min rows r0)
        let d = max 0 (min rows r1)
        if b <= a || d <= c then 0
        else sat.[d * stride + b] - sat.[c * stride + b] - sat.[d * stride + a] + sat.[c * stride + a]

    /// The density (points per cell of the grid) of an `outCols` x `outRows` raster over the rectangle [vx0, vx1] x [vy0, vy1], row 0 at the top (the larger y).  Returns the largest.
    /// The raster is written to `out`.
    member this.Window(vx0: float, vx1: float, vy0: float, vy1: float, outCols: int, outRows: int, out: float32[]) : float32 =
        let gw = float cols / float (x1 - x0)
        let gh = float rows / float (y1 - y0)
        // The cell edges of the raster in grid coordinates, computed once per edge: neighbours share an edge, so the raster partitions the grid (no cell counted twice or not at all).
        let ex = Array.init (outCols + 1) (fun i -> int (Math.Floor((vx0 + (vx1 - vx0) * float i / float outCols - float x0) * gw + 1.0e-6)))
        let ey = Array.init (outRows + 1) (fun j -> int (Math.Floor((vy1 - (vy1 - vy0) * float j / float outRows - float y0) * gh + 1.0e-6)))
        let mutable best = 0.0f
        for j in 0 .. outRows - 1 do
            // raster row 0 is the top: the highest y, so its edges run downwards
            let r1 = ey.[j]
            let r0 = min (r1 - 1) ey.[j + 1]
            for i in 0 .. outCols - 1 do
                let c0 = ex.[i]
                let c1 = max (c0 + 1) ex.[i + 1]
                let n = this.Sum(c0, c1, r0, r1)
                let d = if n = 0 then 0.0f else float32 n / float32 ((c1 - c0) * (r1 - r0))
                out.[j * outCols + i] <- d
                if d > best then best <- d
        best

    /// The grid of `n` points (xs.[i], ys.[i]) over their own extent.  Points that are not finite are not counted.
    static member Build(xs: float32[], ys: float32[], n: int, cols: int, rows: int) : DensityGrid =
        let mutable lx = infinityf
        let mutable hx = -infinityf
        let mutable ly = infinityf
        let mutable hy = -infinityf
        for i in 0 .. n - 1 do
            let x = xs.[i]
            let y = ys.[i]
            if x - x = 0.0f && y - y = 0.0f then
                if x < lx then lx <- x
                if x > hx then hx <- x
                if y < ly then ly <- y
                if y > hy then hy <- y
        if lx > hx then (lx <- 0.0f; hx <- 1.0f; ly <- 0.0f; hy <- 1.0f)
        if hx = lx then hx <- lx + 1.0f
        if hy = ly then hy <- ly + 1.0f
        let stride = cols + 1
        let sat : int[] = Array.zeroCreate ((rows + 1) * stride)
        let kx = float32 cols / (hx - lx)
        let ky = float32 rows / (hy - ly)
        for i in 0 .. n - 1 do
            let x = xs.[i]
            let y = ys.[i]
            if x - x = 0.0f && y - y = 0.0f then
                let c = min (cols - 1) (int ((x - lx) * kx))
                let r = min (rows - 1) (int ((y - ly) * ky))
                let at = (r + 1) * stride + c + 1
                sat.[at] <- sat.[at] + 1
        for r in 1 .. rows do
            for c in 1 .. cols do
                let at = r * stride + c
                sat.[at] <- sat.[at] + sat.[at - stride] + sat.[at - 1] - sat.[at - stride - 1]
        DensityGrid(lx, hx, ly, hy, cols, rows, sat)

/// Densities as a few levels of colour, and the runs of equal level that a row of them makes (one rectangle each: a map of 200 x 100 cells is a few thousand shapes, not twenty thousand).
module Heat =
    /// The level of a density for a map whose largest density is `maxD`: 0 is empty, 1..levels-1 the rest on a log scale, so that a few dense cells do not wash the others out.
    let level (levels: int) (maxD: float32) (d: float32) : int =
        if d <= 0.0f || maxD <= 0.0f then 0
        else
            let t = Math.Log(1.0 + float d) / Math.Log(1.0 + float maxD)
            max 1 (min (levels - 1) (1 + int (t * float (levels - 2))))

    /// The levels of a raster of densities.
    let levels (levels: int) (maxD: float32) (dens: float32[]) (n: int) (out: int[]) : unit =
        for i in 0 .. n - 1 do out.[i] <- level levels maxD dens.[i]

    /// `f row col length level` for every run of cells of one non-empty level in a row.
    let runs (lv: int[]) (cols: int) (rows: int) (f: int -> int -> int -> int -> unit) : unit =
        for r in 0 .. rows - 1 do
            let mutable c = 0
            while c < cols do
                let l = lv.[r * cols + c]
                if l = 0 then c <- c + 1
                else
                    let mutable e = c + 1
                    while e < cols && lv.[r * cols + e] = l do e <- e + 1
                    f r c (e - c) l
                    c <- e
