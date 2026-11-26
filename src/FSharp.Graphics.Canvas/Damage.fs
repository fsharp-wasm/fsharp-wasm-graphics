namespace FSharp.Graphics.Canvas

open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Primitives

/// What a frame changed on the canvas, so that the next one can redraw only that.  The list of commands of a frame is compared with the one before: what a button does when the pointer is
/// over it is a few commands, and the rest of the canvas is the same pixels.  Pure: no canvas here, so it is tested under .NET.
module Damage =

    /// The box of a shape, in its own coordinates.
    let geometryBounds (g: ShapeGeometry) : Rect =
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

    /// Do two commands draw the same thing?  A shape has no equality of its own (it is an interface, made anew every frame); its geometry has.
    let same (a: DrawCommand) (b: DrawCommand) : bool =
        match a, b with
        | DrawRect(b1, r1, p1), DrawRect(b2, r2, p2) -> b1 = b2 && r1 = r2 && p1 = p2
        | DrawCircle(c1, r1, p1), DrawCircle(c2, r2, p2) -> c1 = c2 && r1 = r2 && p1 = p2
        | DrawEllipse(c1, x1, y1, p1), DrawEllipse(c2, x2, y2, p2) -> c1 = c2 && x1 = x2 && y1 = y2 && p1 = p2
        | DrawPath(s1, p1), DrawPath(s2, p2) -> s1.Geometry = s2.Geometry && p1 = p2
        | DrawImage(i1, b1, o1), DrawImage(i2, b2, o2) -> i1 = i2 && b1 = b2 && o1 = o2
        | DrawTextBatch(f1, s1, g1, c1), DrawTextBatch(f2, s2, g2, c2) -> f1 = f2 && s1 = s2 && c1 = c2 && g1 = g2
        | DrawStroke(s1, k1), DrawStroke(s2, k2) -> s1.Geometry = s2.Geometry && k1 = k2
        | DrawShadow(s1, h1), DrawShadow(s2, h2) -> s1.Geometry = s2.Geometry && h1 = h2
        | PushClip r1, PushClip r2 -> r1 = r2
        | PushTransform m1, PushTransform m2 -> m1 = m2
        | PushLayer l1, PushLayer l2 -> l1.Alpha = l2.Alpha && l1.BlurSigma = l2.BlurSigma && l1.Blend = l2.Blend && l1.Filter = l2.Filter
        | PopClip, PopClip | PopTransform, PopTransform | PopLayer, PopLayer -> true
        | _ -> false

    /// How the commands at one point of a list draw: where the coordinates go (the canvas), and how far the layers around blur what is drawn in them.
    [<Struct>]
    type State = { Ctm: Mat3x2; Blur: float32 }

    let initial : State = { Ctm = Mat3x2.identity; Blur = 0.0f }

    /// The state after a command; `stack` keeps the states to come back to (a push saves, a pop restores).
    let advance (stack: ResizeArray<State>) (st: State) (c: DrawCommand) : State =
        match c with
        | PushTransform m -> stack.Add st; { st with Ctm = Mat3x2.multiply m st.Ctm }
        | PushLayer l -> stack.Add st; { st with Blur = st.Blur + l.BlurSigma }
        | PushClip _ -> stack.Add st; st
        | PopTransform | PopLayer | PopClip ->
            if stack.Count = 0 then st
            else
                let back = stack.[stack.Count - 1]
                stack.RemoveAt(stack.Count - 1)
                back
        | _ -> st

    let private grow (r: Rect) (d: float32) : Rect = Rect.create (r.X - d) (r.Y - d) (r.W + 2.0f * d) (r.H + 2.0f * d)

    /// The box that `r` covers on the canvas under `m`.
    let private onCanvas (m: Mat3x2) (r: Rect) : Rect =
        let p1 = Mat3x2.transformPoint m (Vec2.create r.X r.Y)
        let p2 = Mat3x2.transformPoint m (Vec2.create (r.X + r.W) r.Y)
        let p3 = Mat3x2.transformPoint m (Vec2.create r.X (r.Y + r.H))
        let p4 = Mat3x2.transformPoint m (Vec2.create (r.X + r.W) (r.Y + r.H))
        let x0 = min (min p1.X p2.X) (min p3.X p4.X)
        let y0 = min (min p1.Y p2.Y) (min p3.Y p4.Y)
        let x1 = max (max p1.X p2.X) (max p3.X p4.X)
        let y1 = max (max p1.Y p2.Y) (max p3.Y p4.Y)
        Rect.create x0 y0 (x1 - x0) (y1 - y0)

    /// The box a drawing command can touch, on the canvas, with room for what reaches beyond its shape: the edge of a stroke and its miters, a blurred shadow, a blurred layer, the antialiasing
    /// of an edge, the glyphs of a line of text (advance widths are known, the height is the size).  None for a command that draws nothing (the ones that change the state).
    let reach (st: State) (c: DrawCommand) : Rect voption =
        let local =
            match c with
            | DrawRect(b, _, _) -> ValueSome b
            | DrawCircle(ct, r, _) -> ValueSome (geometryBounds (GCircle(ct, r)))
            | DrawEllipse(ct, rx, ry, _) -> ValueSome (geometryBounds (GEllipse(ct, rx, ry)))
            | DrawPath(s, _) -> ValueSome (geometryBounds s.Geometry)
            | DrawImage(_, b, _) -> ValueSome b
            | DrawStroke(s, k) -> ValueSome (grow (geometryBounds s.Geometry) (k.Width * 0.5f * (max 1.0f k.MiterLimit)))
            | DrawShadow(s, h) ->
                let b = geometryBounds s.Geometry
                if h.Inner then ValueSome b
                else ValueSome (grow b (abs h.Spread + 3.0f * h.Blur + abs h.Offset.X + abs h.Offset.Y))
            | DrawTextBatch(_, size, glyphs, _) ->
                if glyphs.Length = 0 then ValueNone
                else
                    let mutable x0 = glyphs.[0].X
                    let mutable x1 = glyphs.[0].X + glyphs.[0].AdvanceX
                    let mutable y0 = glyphs.[0].Y
                    let mutable y1 = glyphs.[0].Y
                    for g in glyphs do
                        if g.X < x0 then x0 <- g.X
                        if g.X + g.AdvanceX > x1 then x1 <- g.X + g.AdvanceX
                        if g.Y < y0 then y0 <- g.Y
                        if g.Y > y1 then y1 <- g.Y
                    // ascent and descent of a line, with room for a glyph that leans over its advance
                    ValueSome (Rect.create (x0 - 0.25f * size) (y0 - 1.1f * size) (x1 - x0 + 0.5f * size) (y1 - y0 + 1.6f * size))
            | _ -> ValueNone
        match local with
        | ValueSome r -> ValueSome (onCanvas st.Ctm (grow r (3.0f * st.Blur + 1.0f)))
        | ValueNone -> ValueNone

    /// Do two boxes share a pixel?
    let touches (a: Rect) (b: Rect) : bool =
        a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H

    let private union (a: Rect) (b: Rect) : Rect =
        let x0 = min a.X b.X
        let y0 = min a.Y b.Y
        Rect.create x0 y0 (max (a.X + a.W) (b.X + b.W) - x0) (max (a.Y + a.H) (b.Y + b.H) - y0)

    /// How the canvas of the frame differs from the one before.
    [<Struct>]
    type Change =
        /// the same pixels: nothing is drawn
        | Nothing
        /// only here (on the canvas, CSS px: a few boxes that do not overlap), the rest is as it was
        | Regions of Rect array
        /// too much, or not to be told: draw the whole canvas
        | Everything

    /// Regions together larger than this share of the canvas are not worth the bookkeeping: the frame is drawn whole.
    let private wholeShare = 0.5f
    /// More boxes than this (after merging) are one box: every region costs a pass over the commands.
    let private mostRegions = 6

    let private area (r: Rect) : float32 = r.W * r.H

    /// Boxes whose union is hardly larger than the two together (a button and its label) are one box; two that are far apart stay two (the pointer moving from one button to another changes
    /// both, and what lies between is the same picture).  Boxes that merely overlap are not one box: the two lines of a crosshair cross, and their union is the whole plot, which is
    /// what a redraw must not be for a pointer that moves.  (Two regions that overlap are drawn one after the other, each whole inside its clip: the same pixels.)
    let private merge (boxes: ResizeArray<Rect>) : ResizeArray<Rect> =
        let boxes = ResizeArray<Rect>(boxes)
        let mutable again = true
        while again do
            again <- false
            let mutable i = 0
            while i < boxes.Count do
                let mutable j = i + 1
                while j < boxes.Count do
                    let u = union boxes.[i] boxes.[j]
                    if area u <= 1.25f * (area boxes.[i] + area boxes.[j]) then
                        boxes.[i] <- u
                        boxes.RemoveAt j
                        again <- true
                    else j <- j + 1
                i <- i + 1
        boxes

    /// At most `limit` boxes: while there are more, the two whose union wastes the least (the area it adds to the two) are made one.  A pointer that moves a crosshair and its card changes
    /// eight boxes; the three that they make are small, where one box around all of them is the plot.
    let private fewer (limit: int) (boxes: ResizeArray<Rect>) : ResizeArray<Rect> =
        while boxes.Count > limit do
            let mutable bi = 0
            let mutable bj = 1
            let mutable best = infinityf
            for i in 0 .. boxes.Count - 2 do
                for j in i + 1 .. boxes.Count - 1 do
                    let waste = area (union boxes.[i] boxes.[j]) - area boxes.[i] - area boxes.[j]
                    if waste < best then
                        best <- waste
                        bi <- i
                        bj <- j
            boxes.[bi] <- union boxes.[bi] boxes.[bj]
            boxes.RemoveAt bj
        boxes

    let private isState (c: DrawCommand) : bool =
        match c with
        | PushClip _ | PushTransform _ | PushLayer _ | PopClip | PopTransform | PopLayer -> true
        | _ -> false

    /// How many pushes a stretch of commands leaves open (negative: more pops than pushes), and how low that count goes on the way.
    let private depth (cs: ResizeArray<DrawCommand>) (from: int) (upTo: int) : struct (int * int) =
        let mutable d = 0
        let mutable lowest = 0
        for i in from .. upTo - 1 do
            match cs.[i] with
            | PushClip _ | PushTransform _ | PushLayer _ -> d <- d + 1
            | PopClip | PopTransform | PopLayer -> d <- d - 1
            | _ -> ()
            if d < lowest then lowest <- d
        struct (d, lowest)

    /// The change that a set of boxes is: each cut to the canvas, merged, and none at all or too much of it handled as such.
    let private regionsOf (boxes: ResizeArray<Rect>) (canvas: Rect) : Change =
        let cut = ResizeArray<Rect>()
        for r in boxes do
            let x0 = max canvas.X r.X
            let y0 = max canvas.Y r.Y
            let x1 = min (canvas.X + canvas.W) (r.X + r.W)
            let y1 = min (canvas.Y + canvas.H) (r.Y + r.H)
            if x1 > x0 && y1 > y0 then cut.Add(Rect.create x0 y0 (x1 - x0) (y1 - y0))
        if cut.Count = 0 then Nothing
        else
            let merged = if cut.Count > 64 then ResizeArray<Rect>([ Seq.reduce union cut ]) else merge cut
            let merged = fewer mostRegions merged
            let mutable total = 0.0f
            for r in merged do total <- total + area r
            if total > wholeShare * area canvas then Everything else Regions (merged.ToArray())

    /// Compare the commands of the frame before with the ones of this frame.  What is the same at the start and at the end is the same picture; what lies between is what changed.
    ///
    /// When the stretch between has as many commands in both frames, and what differs in it are drawing commands set against drawing commands, the structure (every push and pop) is the same
    /// and only those commands changed: their boxes, before and now, are the regions (two buttons far apart are two regions; what lies between them is not touched).
    ///
    /// Otherwise the stretch is taken as a whole, which holds only when its commands leave the state as they found it, every push meeting its pop: a changed push draws everything up to its pop, in
    /// the part that is the same, differently.  So the stretch is taken on to the pop of such a push (the same commands in both frames), and the box of everything in it, before and now, is the
    /// region.  When no pop can be found like that, or the two frames do not open the same pushes, the whole canvas is drawn.
    let between (before: ResizeArray<DrawCommand>) (after: ResizeArray<DrawCommand>) (canvas: Rect) : Change =
        let na = before.Count
        let nb = after.Count
        let mutable p = 0
        while p < na && p < nb && same before.[p] after.[p] do p <- p + 1
        if p = na && p = nb then Nothing
        else
            let mutable q = 0
            while q < na - p && q < nb - p && same before.[na - 1 - q] after.[nb - 1 - q] do q <- q + 1
            let stack = ResizeArray<State>()
            let mutable st = initial
            for i in 0 .. p - 1 do st <- advance stack st after.[i]
            let boxes = ResizeArray<Rect>()
            let mutable pairs = na - q - p = nb - q - p
            if pairs then
                for i in p .. na - q - 1 do
                    if not (same before.[i] after.[i]) && (isState before.[i] || isState after.[i]) then pairs <- false
            if pairs then
                for i in p .. na - q - 1 do
                    if not (same before.[i] after.[i]) then
                        (match reach st before.[i] with ValueSome r -> boxes.Add r | ValueNone -> ())
                        (match reach st after.[i] with ValueSome r -> boxes.Add r | ValueNone -> ())
                    st <- advance stack st after.[i]
                regionsOf boxes canvas
            else
                let struct (openedBefore, lowestBefore) = depth before p (na - q)
                let struct (openedAfter, lowestAfter) = depth after p (nb - q)
                // take the common end in, up to the pop that closes what the changed commands opened
                let mutable e = 0
                let mutable ok = lowestBefore >= 0 && lowestAfter >= 0 && openedBefore = openedAfter
                if ok && openedBefore > 0 then
                    let mutable open' = openedBefore
                    while open' > 0 && e < q do
                        (match before.[na - q + e] with
                         | PushClip _ | PushTransform _ | PushLayer _ -> open' <- open' + 1
                         | PopClip | PopTransform | PopLayer -> open' <- open' - 1
                         | _ -> ())
                        e <- e + 1
                    if open' <> 0 then ok <- false
                if not ok then Everything
                else
                    for (cs, upTo) in [ (before, na - q + e); (after, nb - q + e) ] do
                        let local = ResizeArray<State>(stack)
                        let mutable s = st
                        for i in p .. upTo - 1 do
                            match reach s cs.[i] with
                            | ValueSome r -> boxes.Add r
                            | ValueNone -> ()
                            s <- advance local s cs.[i]
                    regionsOf boxes canvas
