namespace FSharp.Graphics.Diagrams

open System
open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Primitives
open FSharp.Graphics.Scene
open FSharp.Graphics.Charts

/// A layout drawn with the theme of the charts, and asked where a point is.
module Diagram =
    /// The length of an arrow head, and half its width.
    [<Literal>]
    let ArrowLength = 9.0f

    /// A box for this label: as wide as the text needs (an estimate: the average letter is 0.58 of the text size wide), with room around it.
    let fit (th: Theme) (id: int) (label: string) : Box =
        { Id = id; W = max 56.0f (20.0f + float32 label.Length * th.TextPx * 0.58f); H = th.TextPx * 2.4f }

    /// The box that holds the point (the one placed last of those that do, which is the one that is drawn on top).
    let boxAt (layout: Layout) (x: float32) (y: float32) : int option =
        layout.Boxes
        |> Array.tryFindBack (fun b -> x >= b.Rect.X && x <= b.Rect.X + b.Rect.W && y >= b.Rect.Y && y <= b.Rect.Y + b.Rect.H)
        |> Option.map (fun b -> b.Id)

    /// The head of an arrow: a triangle with its tip at `tip`, pointing along `dir` (a unit vector).
    let head (tip: Vec2) (dir: Vec2) (length: float32) : Vec2[] =
        let bx = tip.X - dir.X * length
        let by = tip.Y - dir.Y * length
        let hw = length * 0.45f
        [| tip; Vec2.create (bx - dir.Y * hw) (by + dir.X * hw); Vec2.create (bx + dir.Y * hw) (by - dir.X * hw) |]

    /// A route as a line with an arrow at its end.  The line stops where the head starts.
    let private arrow (color: Color) (width: float32) (pts: Vec2[]) : Scene =
        let n = pts.Length
        if n < 2 then Empty
        else
            let tip = pts.[n - 1]
            let prev = pts.[n - 2]
            let dx = tip.X - prev.X
            let dy = tip.Y - prev.Y
            let len = MathF.Sqrt(dx * dx + dy * dy)
            if len < 1.0e-3f then Empty
            else
                let dir = Vec2.create (dx / len) (dy / len)
                let hl = min ArrowLength (len * 0.8f)
                let stem = Array.append (Array.sub pts 0 (n - 1)) [| Vec2.create (tip.X - dir.X * hl * 0.9f) (tip.Y - dir.Y * hl * 0.9f) |]
                Group [ Parts.polygon (Geometry.ribbon stem width) color
                        Parts.polygon (head tip dir hl) color ]

    /// The diagram: the links under the boxes, each box with its label.  `fill` is the colour of a box (by id), `label` its text; the whole is one picture for assistive technology.
    let scene (th: Theme) (layout: Layout) (label: int -> string) (fill: int -> Color) : Scene =
        let edge = Color.withAlpha 0.85f th.Muted
        let links = Group [ for r in layout.Routes -> arrow edge 1.5f r.Points ]
        let boxes =
            Group [ for b in layout.Boxes do
                        let r = b.Rect
                        yield Shape(Shapes.roundedRect (Rect.create (r.X - 1.0f) (r.Y - 1.0f) (r.W + 2.0f) (r.H + 2.0f)) (CornerRadius.all 7.0f) (Solid th.Axis))
                        yield Shape(Shapes.roundedRect r (CornerRadius.all 6.0f) (Solid (fill b.Id)))
                        yield Parts.text th th.TextPx th.Text TextAlign.Center r.X (r.Y + (r.H - th.TextPx * 1.6f) * 0.5f) r.W (label b.Id) ]
        Parts.describe "diagram" "Diagram" (string layout.Boxes.Length + " boxes, " + string layout.Routes.Length + " links") (Group [ links; boxes ])
