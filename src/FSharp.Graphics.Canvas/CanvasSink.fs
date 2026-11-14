namespace FSharp.Graphics.Canvas

open System
open System.Text
open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Primitives

/// Interprets a draw command list on the canvas through `Gfx`.  The semantics follow
/// FSharp.Graphics.Host.Skia.SkiaSink; where Canvas 2D has no equivalent the approximation is noted:
///  - a layer applies its alpha, blend mode and blur to each draw inside it instead of compositing the group once
///  - layer colour matrices are ignored
///  - images draw as the same neutral grey as the other hosts
module CanvasSink =
    let private blendIndex (b: Blend) : int =
        match b with
        | Normal -> 0 | Multiply -> 1 | Screen -> 2 | Overlay -> 3 | Darken -> 4 | Lighten -> 5
        | ColorDodge -> 6 | ColorBurn -> 7 | HardLight -> 8 | SoftLight -> 9 | Difference -> 10 | Exclusion -> 11

    /// Grow (or shrink, if negative) a shape by `d` on every side: shadow spread.
    let private inflate (g: ShapeGeometry) (d: float32) : ShapeGeometry =
        match g with
        | GRect(b, r) ->
            let grow (x: float32) = if x > 0.0f then max (x + d) 0.0f else 0.0f
            GRect(Rect.create (b.X - d) (b.Y - d) (b.W + 2.0f * d) (b.H + 2.0f * d),
                  { TopLeft = grow r.TopLeft; TopRight = grow r.TopRight; BottomRight = grow r.BottomRight; BottomLeft = grow r.BottomLeft })
        | GCircle(c, r) -> GCircle(c, max (r + d) 0.0f)
        | GEllipse(c, rx, ry) -> GEllipse(c, max (rx + d) 0.0f, max (ry + d) 0.0f)
        | GLine(a, b, t) -> GLine(a, b, max (t + 2.0f * d) 0.0f)
        | GOpaque b -> GOpaque(Rect.create (b.X - d) (b.Y - d) (b.W + 2.0f * d) (b.H + 2.0f * d))
        | GPolygon _ -> g

    /// Append the outline of a shape to the current path.  A line is a capsule: a rounded rectangle laid along it.
    let private addGeometry (g: ShapeGeometry) : unit =
        match g with
        | GRect(b, r) -> Gfx.roundRect (float b.X) (float b.Y) (float b.W) (float b.H) (float r.TopLeft) (float r.TopRight) (float r.BottomRight) (float r.BottomLeft)
        | GCircle(c, r) -> Gfx.ellipse (float c.X) (float c.Y) (float r) (float r)
        | GEllipse(c, rx, ry) -> Gfx.ellipse (float c.X) (float c.Y) (float rx) (float ry)
        | GPolygon vs ->
            if vs.Length > 0 then
                Gfx.moveTo (float vs.[0].X) (float vs.[0].Y)
                for i in 1 .. vs.Length - 1 do Gfx.lineTo (float vs.[i].X) (float vs.[i].Y)
                Gfx.closePath ()
        | GLine(a, b, t) ->
            let dx = float (b.X - a.X)
            let dy = float (b.Y - a.Y)
            let len = sqrt (dx * dx + dy * dy)
            let t = float t
            if len < 1.0e-6 then Gfx.ellipse (float a.X) (float a.Y) (t * 0.5) (t * 0.5)
            else
                // the path is built in the line's own frame (x along the segment) and keeps those coordinates
                Gfx.save ()
                Gfx.transform (dx / len) (dy / len) (-dy / len) (dx / len) (float a.X) (float a.Y)
                Gfx.roundRect (-t * 0.5) (-t * 0.5) (len + t) t (t * 0.5) (t * 0.5) (t * 0.5) (t * 0.5)
                Gfx.restore ()
        | GOpaque b -> Gfx.roundRect (float b.X) (float b.Y) (float b.W) (float b.H) 0.0 0.0 0.0 0.0

    let private geometryBounds (g: ShapeGeometry) : Rect =
        match g with
        | GRect(b, _) | GOpaque b -> b
        | GCircle(c, r) -> Rect.create (c.X - r) (c.Y - r) (2.0f * r) (2.0f * r)
        | GEllipse(c, rx, ry) -> Rect.create (c.X - rx) (c.Y - ry) (2.0f * rx) (2.0f * ry)
        | GLine(a, b, t) ->
            let x0 = min a.X b.X - t * 0.5f
            let y0 = min a.Y b.Y - t * 0.5f
            Rect.create x0 y0 (max a.X b.X + t * 0.5f - x0) (max a.Y b.Y + t * 0.5f - y0)
        | GPolygon vs ->
            if vs.Length = 0 then Rect.zero
            else
                let mutable x0 = vs.[0].X
                let mutable y0 = vs.[0].Y
                let mutable x1 = x0
                let mutable y1 = y0
                for v in vs do
                    if v.X < x0 then x0 <- v.X
                    if v.Y < y0 then y0 <- v.Y
                    if v.X > x1 then x1 <- v.X
                    if v.Y > y1 then y1 <- v.Y
                Rect.create x0 y0 (x1 - x0) (y1 - y0)

    /// Make `p` the current fill style.
    let private setPaint (p: Paint) : unit =
        match p with
        | Solid c -> Gfx.solid (int c.R) (int c.G) (int c.B) (int c.A)
        | LinearGradient(a, b, stops) when stops.Length > 0 ->
            Gfx.gradLinear (float a.X) (float a.Y) (float b.X) (float b.Y)
            for s in stops do Gfx.gradStop (float s.Offset) (int s.Color.R) (int s.Color.G) (int s.Color.B) (int s.Color.A)
            Gfx.gradUse ()
        | RadialGradient(c, r, stops) when stops.Length > 0 ->
            Gfx.gradRadial (float c.X) (float c.Y) (float r)
            for s in stops do Gfx.gradStop (float s.Offset) (int s.Color.R) (int s.Color.G) (int s.Color.B) (int s.Color.A)
            Gfx.gradUse ()
        // images and empty gradients: neutral grey, like the other hosts
        | _ -> Gfx.solid 200 200 200 255

    let private fillGeometry (g: ShapeGeometry) (p: Paint) : unit =
        setPaint p
        Gfx.pathBegin ()
        addGeometry g
        Gfx.fill ()

    let private strokeGeometry (g: ShapeGeometry) (st: Stroke) : unit =
        // a stroke paint is a colour; a gradient stroke takes the colour of its first stop
        let c =
            match st.Paint with
            | Solid c -> c
            | LinearGradient(_, _, stops) | RadialGradient(_, _, stops) when stops.Length > 0 -> stops.[0].Color
            | _ -> { R = 200uy; G = 200uy; B = 200uy; A = 255uy }
        let cap = match st.Cap with Butt -> 0 | Round -> 1 | Square -> 2
        let join = match st.Join with Miter -> 0 | RoundJoin -> 1 | Bevel -> 2
        Gfx.strokeStyle (int c.R) (int c.G) (int c.B) (int c.A) (float st.Width) cap join (float st.MiterLimit)
        Gfx.pathBegin ()
        addGeometry g
        Gfx.stroke ()

    /// Drop shadow: the (spread-inflated) shape is filled off the canvas, its blurred shadow lands offset in place.
    /// Inner shadow: the blurred complement of the eroded shape, clipped to the shape.
    let private shadow (g: ShapeGeometry) (sh: Shadow) : unit =
        let blur = float (max sh.Blur 0.0f)
        if not sh.Inner then
            Gfx.shadowBegin (float sh.Offset.X) (float sh.Offset.Y) blur (int sh.Color.R) (int sh.Color.G) (int sh.Color.B) (int sh.Color.A)
            Gfx.solid 0 0 0 255
            Gfx.pathBegin ()
            addGeometry (inflate g sh.Spread)
            Gfx.fill ()
            Gfx.shadowEnd ()
        else
            Gfx.save ()
            Gfx.pathBegin ()
            addGeometry g
            Gfx.clipPath ()
            let b = geometryBounds g
            let sigma = max (sh.Blur * 0.5f) 0.01f
            let margin = 4.0f * sigma + abs sh.Spread + abs sh.Offset.X + abs sh.Offset.Y + 8.0f
            Gfx.shadowBegin (float sh.Offset.X) (float sh.Offset.Y) blur (int sh.Color.R) (int sh.Color.G) (int sh.Color.B) (int sh.Color.A)
            Gfx.solid 0 0 0 255
            Gfx.pathBegin ()
            Gfx.roundRect (float (b.X - margin)) (float (b.Y - margin)) (float (b.W + 2.0f * margin)) (float (b.H + 2.0f * margin)) 0.0 0.0 0.0 0.0
            addGeometry (inflate g (-sh.Spread))
            Gfx.fillEvenOdd ()
            Gfx.shadowEnd ()
            Gfx.restore ()

    /// One run per line: consecutive glyphs on the same baseline become one string drawn at the first glyph.
    let private text (size: float32) (color: Color) (glyphs: GlyphPosition array) : unit =
        let sb = StringBuilder()
        let mutable i = 0
        while i < glyphs.Length do
            let first = glyphs.[i]
            sb.Clear() |> ignore
            let mutable j = i
            while j < glyphs.Length && glyphs.[j].Y = first.Y do
                sb.Append(char glyphs.[j].GlyphId) |> ignore
                j <- j + 1
            Gfx.text (sb.ToString()) (float first.X) (float first.Y) (float size) 400 (int color.R) (int color.G) (int color.B) (int color.A)
            i <- j

    let run (commands: ResizeArray<DrawCommand>) : unit =
        for i in 0 .. commands.Count - 1 do
            match commands.[i] with
            | DrawRect(b, r, p) -> fillGeometry (GRect(b, r)) p
            | DrawCircle(c, r, p) -> fillGeometry (GCircle(c, r)) p
            | DrawEllipse(c, rx, ry, p) -> fillGeometry (GEllipse(c, rx, ry)) p
            | DrawPath(s, p) -> fillGeometry s.Geometry p
            | DrawStroke(s, st) -> strokeGeometry s.Geometry st
            | DrawShadow(s, sh) -> shadow s.Geometry sh
            | DrawTextBatch(_, size, glyphs, color) -> text size color glyphs
            | DrawImage(_, r, opacity) ->
                Gfx.solid 200 200 200 (int (255.0f * (min 1.0f (max 0.0f opacity))))
                Gfx.pathBegin ()
                Gfx.roundRect (float r.X) (float r.Y) (float r.W) (float r.H) 0.0 0.0 0.0 0.0
                Gfx.fill ()
            | PushClip r ->
                Gfx.save ()
                Gfx.clip (float r.X) (float r.Y) (float r.W) (float r.H)
            | PopClip -> Gfx.restore ()
            | PushTransform m ->
                Gfx.save ()
                Gfx.transform (float m.M11) (float m.M12) (float m.M21) (float m.M22) (float m.M31) (float m.M32)
            | PopTransform -> Gfx.restore ()
            | PushLayer lp ->
                Gfx.save ()
                Gfx.layer (float lp.Alpha) (blendIndex lp.Blend) (float lp.BlurSigma)
                if lp.Filter.Length = 20 then
                    Gfx.matrixBegin ()
                    for v in lp.Filter do Gfx.matrixValue (float v)
                    Gfx.matrixUse ()
            | PopLayer -> Gfx.restore ()
