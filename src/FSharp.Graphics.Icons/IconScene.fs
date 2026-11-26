namespace FSharp.Graphics.Icons

open FSharp.Wasm.Icons
open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Primitives
open FSharp.Graphics.Scene

/// The shapes of an icon, by a name that the scene's own `Shape` and `Path` do not hide.
type IconShape = FSharp.Wasm.Icons.Shape

/// An icon as a scene: every shape of it is stroked with round ends and joins, as the SVG of the same icon is, in one colour.
module IconScene =

    /// A shape as the commands of a path, in the units of the icon's grid.  A circle, an ellipse and a rounded rectangle are arcs and lines; the rest is already lines.
    let commands (shape: IconShape) : Command list =
        let arcs (cx: float) (cy: float) (rx: float) (ry: float) =
            [ MoveTo (cx - rx, cy); ArcTo (rx, ry, 0.0, true, false, cx + rx, cy); ArcTo (rx, ry, 0.0, true, false, cx - rx, cy); Close ]
        let points (ps: (float * float) list) : Command list =
            match ps with
            | (x, y) :: rest -> MoveTo (x, y) :: (rest |> List.map LineTo)
            | [] -> []
        match shape with
        | IconShape.Path d -> PathData.parse d
        | IconShape.Circle (cx, cy, r) -> arcs cx cy r r
        | IconShape.Ellipse (cx, cy, rx, ry) -> arcs cx cy rx ry
        | IconShape.Line (x1, y1, x2, y2) -> [ MoveTo (x1, y1); LineTo (x2, y2) ]
        | IconShape.Polyline ps -> points ps
        | IconShape.Polygon ps -> points ps @ [ Close ]
        | IconShape.Rect (x, y, w, h, radius) ->
            let r = max 0.0 (min radius (min w h / 2.0))
            if r = 0.0 then [ MoveTo (x, y); LineTo (x + w, y); LineTo (x + w, y + h); LineTo (x, y + h); Close ]
            else
                [ MoveTo (x + r, y); LineTo (x + w - r, y); ArcTo (r, r, 0.0, false, true, x + w, y + r)
                  LineTo (x + w, y + h - r); ArcTo (r, r, 0.0, false, true, x + w - r, y + h)
                  LineTo (x + r, y + h); ArcTo (r, r, 0.0, false, true, x, y + h - r)
                  LineTo (x, y + r); ArcTo (r, r, 0.0, false, true, x + r, y); Close ]

    /// How many segments a curve gets per unit of the grid at a size: enough that no segment is longer than about two pixels.
    let private stepsPerUnit (scale: float) : float = max 0.5 (scale / 2.0)

    /// The strokes of an icon, in grid units: one polyline per subpath of every shape.
    let polylines (scale: float) (icon: Icon) : Polyline list =
        icon.Shapes |> List.collect (fun s -> PathData.flatten (stepsPerUnit scale) (commands s))

    /// A segment of a stroke: a capsule of the thickness.  A segment too short to have a direction (the dot of an icon: `h.01`) is a dot.
    let private segment (thickness: float32) (paint: Paint) (a: Vec2) (b: Vec2) : Scene =
        let d = Vec2.sub b a
        let b' = if Vec2.dot d d < 1e-6f then Vec2.add a (Vec2.create 1e-3f 0.0f) else b
        Scene.Shape(PrimitiveShape.Path(LineSdf(a, b', thickness), paint, None, None))

    let private stroke (scale: float32) (paint: Paint) (p: Polyline) : Scene list =
        let pts = p.Points |> Array.map (fun q -> Vec2.scale scale q)
        let n = pts.Length
        let thickness = scale * float32 FSharp.Wasm.Icons.Icons.strokeWidth
        match n with
        | 0 -> []
        | 1 -> [ segment thickness paint pts.[0] pts.[0] ]
        | _ ->
            [ for i in 0 .. n - 2 -> segment thickness paint pts.[i] pts.[i + 1]
              if p.Closed then yield segment thickness paint pts.[n - 1] pts.[0] ]

    /// The icon at `size` pixels, in `color`, with its corner at the origin of the scene it is in.
    let view (size: float32) (color: Color) (icon: Icon) : Scene =
        let scale = size / float32 FSharp.Wasm.Icons.Icons.grid
        let paint = Solid color
        Group (icon |> polylines (float scale) |> List.collect (stroke scale paint))
