namespace FSharp.Graphics.Charts

open System
open FSharp.Math

/// The polygons of a chart, from numbers in pixels.  The scene graph draws closed shapes, so a line is the band along it and a dense line is the band between the smallest and the
/// largest sample of every pixel column.
module Geometry =

    /// The unit direction of each segment of a polyline.  A segment of no length takes the direction of its neighbour, so a repeated point does not break the corner after it.
    let private directions (pts: Vec2[]) : Vec2[] =
        let n = pts.Length - 1
        let d = Array.zeroCreate<Vec2> n
        let ok = Array.zeroCreate<bool> n
        for i in 0 .. n - 1 do
            let dx = pts.[i + 1].X - pts.[i].X
            let dy = pts.[i + 1].Y - pts.[i].Y
            let len = MathF.Sqrt(dx * dx + dy * dy)
            if len > 1.0e-6f then
                d.[i] <- Vec2.create (dx / len) (dy / len)
                ok.[i] <- true
        // fill the gaps from the previous segment, then from the next one
        for i in 1 .. n - 1 do
            if not ok.[i] && ok.[i - 1] then (d.[i] <- d.[i - 1]; ok.[i] <- true)
        for i in n - 2 .. -1 .. 0 do
            if not ok.[i] && ok.[i + 1] then (d.[i] <- d.[i + 1]; ok.[i] <- true)
        for i in 0 .. n - 1 do
            if not ok.[i] then d.[i] <- Vec2.create 1.0f 0.0f
        d

    /// A closed polygon: a line `width` pixels wide along `pts`.  Corners are mitered up to three times the half width, beyond that the corner is cut at that length, so a sharp turn
    /// does not shoot a spike through the plot.  Fewer than two points: nothing.
    let ribbon (pts: Vec2[]) (width: float32) : Vec2[] =
        let n = pts.Length
        if n < 2 then [||]
        else
            let half = width * 0.5f
            let dirs = directions pts
            let left = Array.zeroCreate<Vec2> n
            let right = Array.zeroCreate<Vec2> n
            for i in 0 .. n - 1 do
                let a = dirs.[max 0 (i - 1)]
                let b = dirs.[min (n - 2) i]
                // the normals of the two segments; their sum points along the miter
                let mx = -a.Y - b.Y
                let my = a.X + b.X
                let ml = MathF.Sqrt(mx * mx + my * my)
                let (ux, uy) = if ml < 1.0e-4f then (-b.Y, b.X) else (mx / ml, my / ml)
                let cosHalf = ux * -b.Y + uy * b.X
                let k = half * (if cosHalf < 0.34f then 3.0f else 1.0f / cosHalf)
                left.[i] <- Vec2.create (pts.[i].X + ux * k) (pts.[i].Y + uy * k)
                right.[i] <- Vec2.create (pts.[i].X - ux * k) (pts.[i].Y - uy * k)
            Array.init (2 * n) (fun i -> if i < n then left.[i] else right.[2 * n - 1 - i])

    /// The band of a dense line: the top edge through `top.[first..last]`, back along `bottom` (pixel y of the smallest sample).  Column c is at x = x0 + (c + 0.5) * colWidth.
    let band (x0: float32) (colWidth: float32) (first: int) (last: int) (top: float32[]) (bottom: float32[]) : Vec2[] =
        let n = last - first + 1
        if n < 1 then [||]
        else
            let x c = x0 + (float32 c + 0.5f) * colWidth
            Array.init (2 * n) (fun i ->
                if i < n then Vec2.create (x (first + i)) top.[first + i]
                else (let c = last - (i - n) in Vec2.create (x c) bottom.[c]))

    /// The area under a line: the points, closed down to `baseline` (pixel y).
    let area (pts: Vec2[]) (baseline: float32) : Vec2[] =
        if pts.Length < 2 then [||]
        else Array.init (pts.Length + 2) (fun i ->
                if i < pts.Length then pts.[i]
                elif i = pts.Length then Vec2.create pts.[pts.Length - 1].X baseline
                else Vec2.create pts.[0].X baseline)
