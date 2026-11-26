/// The tests of FSharp.Graphics.Icons: SVG path data read to commands, curves and arcs flattened to segments, shapes as paths, and every icon of the set as a scene.
module FSharp.Graphics.IconsTests.Tests

open System
open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Scene
open FSharp.Graphics.Icons
open FSharp.Wasm.Icons

let mutable private failures = 0
let mutable private count = 0
/// the number (in order of running) of each check that failed: what a host that cannot read the text of `printfn` reports
let private failedAt = ResizeArray<int>()

let private check (name: string) (ok: bool) =
    count <- count + 1
    if not ok then
        failures <- failures + 1
        failedAt.Add count
        printfn "  FAIL  %s" name

/// How close is close: sin, cos and atan of a program compiled by fsharp-wasm are accurate to about 1e-7 (fsharp-wasm docs/STATUS.md, known gaps), against 1e-16 under .NET.  An icon is
/// drawn on 24 units that are at most a few hundred pixels: a millionth of a unit is a thousandth of a pixel, and is what a point of an arc is asked to be on its circle to.
let private tolerance = 1e-5
let private near (a: float) (b: float) : bool = Math.Abs(a - b) < tolerance
let private nearf (a: float32) (b: float) : bool = Math.Abs(float a - b) < 1e-4

/// the points of the first polyline of a path, flattened at `density` steps per unit of length
let private pointsAt (density: float) (d: string) : (float * float) list =
    match PathData.flatten density (PathData.parse d) with
    | p :: _ -> p.Points |> Array.toList |> List.map (fun q -> float q.X, float q.Y)
    | [] -> []

let private points (d: string) : (float * float) list = pointsAt 4.0 d

let private parseTests () =
    check "an absolute move and a line" (PathData.parse "M1 2 L3 4" = [ MoveTo (1.0, 2.0); LineTo (3.0, 4.0) ])
    check "relative moves add to the pen" (PathData.parse "m1 2 l3 4 l1 1" = [ MoveTo (1.0, 2.0); LineTo (4.0, 6.0); LineTo (5.0, 7.0) ])
    check "h and v take one number, absolute and relative" (PathData.parse "M0 0 H5 V3 h-2 v-1" = [ MoveTo (0.0, 0.0); LineTo (5.0, 0.0); LineTo (5.0, 3.0); LineTo (3.0, 3.0); LineTo (3.0, 2.0) ])
    check "numbers after a move are lines, after a line more lines" (PathData.parse "M0 0 1 1 2 2" = [ MoveTo (0.0, 0.0); LineTo (1.0, 1.0); LineTo (2.0, 2.0) ])
    check "numbers after a relative move are relative lines" (PathData.parse "m1 1 2 2" = [ MoveTo (1.0, 1.0); LineTo (3.0, 3.0) ])
    check "separators are spaces, commas or none: a sign starts a number" (PathData.parse "M1,2L3-4" = [ MoveTo (1.0, 2.0); LineTo (3.0, -4.0) ])
    check "a number may start with the point, and a second point starts another number" (PathData.parse "M.5.25L1.5.5" = [ MoveTo (0.5, 0.25); LineTo (1.5, 0.5) ])
    check "decimals and exponents" (PathData.parse "M3.086 1e1L-2.5E-1 0" = [ MoveTo (3.086, 10.0); LineTo (-0.25, 0.0) ])
    check "close returns the pen to the start of the subpath" (PathData.parse "M2 3 L5 3 z l1 1" = [ MoveTo (2.0, 3.0); LineTo (5.0, 3.0); Close; LineTo (3.0, 4.0) ])
    check "a cubic, absolute and relative" (PathData.parse "M0 0 C1 1 2 1 3 0 c1 1 2 1 3 0" = [ MoveTo (0.0, 0.0); CubicTo (1.0, 1.0, 2.0, 1.0, 3.0, 0.0); CubicTo (4.0, 1.0, 5.0, 1.0, 6.0, 0.0) ])
    check "a smooth cubic reflects the last control about the pen" (PathData.parse "M0 0 C1 1 2 1 3 0 S5 -1 6 0" = [ MoveTo (0.0, 0.0); CubicTo (1.0, 1.0, 2.0, 1.0, 3.0, 0.0); CubicTo (4.0, -1.0, 5.0, -1.0, 6.0, 0.0) ])
    check "a smooth cubic after a line has the pen as its first control" (PathData.parse "M0 0 L2 0 S3 1 4 0" = [ MoveTo (0.0, 0.0); LineTo (2.0, 0.0); CubicTo (2.0, 0.0, 3.0, 1.0, 4.0, 0.0) ])
    check "a quadratic and a smooth quadratic" (PathData.parse "M0 0 Q1 1 2 0 T4 0" = [ MoveTo (0.0, 0.0); QuadTo (1.0, 1.0, 2.0, 0.0); QuadTo (3.0, -1.0, 4.0, 0.0) ])
    check "an arc with its flags apart" (PathData.parse "M0 0 a2 2 0 0 1 2 2" = [ MoveTo (0.0, 0.0); ArcTo (2.0, 2.0, 0.0, false, true, 2.0, 2.0) ])
    check "an arc with its flags written together with the next number" (PathData.parse "M0 0 a2 2 0 011 1" = [ MoveTo (0.0, 0.0); ArcTo (2.0, 2.0, 0.0, false, true, 1.0, 1.0) ])
    check "an arc with the long way round and the other direction" (PathData.parse "M0 0 A1 1 30 1 0 2 0" = [ MoveTo (0.0, 0.0); ArcTo (1.0, 1.0, 30.0, true, false, 2.0, 0.0) ])
    check "an arc repeated by numbers" (PathData.parse "M0 0 a1 1 0 0 1 1 1 1 1 0 0 1 1 -1" = [ MoveTo (0.0, 0.0); ArcTo (1.0, 1.0, 0.0, false, true, 1.0, 1.0); ArcTo (1.0, 1.0, 0.0, false, true, 2.0, 0.0) ])
    check "path data that ends in the middle of a command gives the commands before it" (PathData.parse "M0 0 L1 1 L2" = [ MoveTo (0.0, 0.0); LineTo (1.0, 1.0) ])
    check "something that is not path data gives nothing" (PathData.parse "hello" = [] && PathData.parse "" = [])

let private flattenTests () =
    check "a line is its two points" (points "M0 0 L10 0" = [ (0.0, 0.0); (10.0, 0.0) ])
    check "a close makes a closed polyline" (match PathData.flatten 4.0 (PathData.parse "M0 0 L1 0 L1 1 Z") with [ p ] -> p.Closed && p.Points.Length = 3 | _ -> false)
    check "two subpaths are two polylines" (PathData.flatten 4.0 (PathData.parse "M0 0 L1 0 M5 5 L6 5") |> List.length = 2)
    check "a move with nothing after it is not a polyline" (PathData.flatten 4.0 (PathData.parse "M0 0 M5 5 L6 5") |> List.length = 1)
    let curve = points "M0 0 C0 10 10 10 10 0"
    check "a cubic ends where it was told to end, and starts at the pen" (curve.Head = (0.0, 0.0) && List.last curve = (10.0, 0.0))
    check "a cubic is sampled: more points the longer it is" (List.length curve >= 5 && List.length (pointsAt 0.2 "M0 0 C0 100 100 100 100 0") > List.length (pointsAt 0.2 "M0 0 C0 10 10 10 10 0"))
    check "and at least four segments however short" (List.length (pointsAt 0.01 "M0 0 C0 1 1 1 1 0") = 5)
    check "the middle of a symmetric cubic is on the axis of symmetry, at 3/4 of the height" (curve |> List.exists (fun (x, y) -> near x 5.0 && near y 7.5))
    let quad = points "M0 0 Q5 10 10 0"
    check "a quadratic passes through the point at its middle (5, 5)" (quad |> List.exists (fun (x, y) -> near x 5.0 && near y 5.0) && List.last quad = (10.0, 0.0))
    // a quarter of a circle of radius 1 from (0, 0) to (1, 1), in the positive direction: the centre is (0, 1)
    let quarter = points "M0 0 A1 1 0 0 1 1 1"
    check "an arc of a quarter turn ends where it was told to" (List.last quarter = (1.0, 1.0))
    check "every point of an arc is a radius from its centre" (quarter |> List.forall (fun (x, y) -> near (Math.Sqrt(x * x + (y - 1.0) * (y - 1.0))) 1.0))
    // the angle about the centre (0, 1) goes from -90 to 0 degrees, steadily: the points come in order along the arc, in the direction that was asked for
    let angles = quarter |> List.map (fun (x, y) -> Math.Atan2(y - 1.0, x))
    check "the points of an arc come in order, from -90 to 0 degrees about the centre" (near (List.head angles) (-Math.PI / 2.0) && Math.Abs(List.last angles) < 1e-6 && List.pairwise angles |> List.forall (fun (a, b) -> b > a))
    // the other direction has the other centre, (1, 0)
    let other = points "M0 0 A1 1 0 0 0 1 1"
    check "the other direction curves the other way: centre (1, 0)" (other |> List.forall (fun (x, y) -> near (Math.Sqrt((x - 1.0) * (x - 1.0) + y * y)) 1.0))
    // the long way, in the positive direction, from (0, 1) to (1, 0): three quarters of the circle about (0, 0)
    let large = points "M0 1 A1 1 0 1 1 1 0"
    check "the long way round is a three quarter turn about the other centre" (large |> List.forall (fun (x, y) -> near (Math.Sqrt(x * x + y * y)) 1.0) && List.length large > List.length quarter && large |> List.exists (fun (x, y) -> x < -0.99))
    check "radii too small are made just large enough: a half circle" (points "M0 0 A1 1 0 0 1 10 0" |> List.forall (fun (x, y) -> near (Math.Sqrt((x - 5.0) * (x - 5.0) + y * y)) 5.0))
    check "an arc with a radius of 0 is a line" (points "M0 0 A0 5 0 0 1 4 4" = [ (0.0, 0.0); (4.0, 4.0) ])
    check "an arc to where it is makes nothing" (points "M3 3 A2 2 0 0 1 3 3" |> List.length = 1 || points "M3 3 A2 2 0 0 1 3 3" = [])
    // an ellipse of radii 2 and 1 turned by 90 degrees has its long axis along y: x^2 + (y / 2)^2 = 1 about the origin
    let turned = points "M1 0 A2 1 90 0 1 -1 0"
    check "a turned ellipse has its points on it" (turned |> List.forall (fun (x, y) -> Math.Abs(x * x + y * y / 4.0 - 1.0) < 1e-3) && turned |> List.exists (fun (_, y) -> Math.Abs y > 1.9))

let private shapeTests () =
    let flat (s: Shape) = IconScene.commands s |> PathData.flatten 8.0
    let circle = flat (Circle (12.0, 12.0, 4.0))
    check "a circle is one closed polyline of points a radius from the centre" (match circle with [ p ] -> p.Closed && p.Points |> Array.forall (fun q -> nearf (Vec2.distance q (Vec2.create 12.0f 12.0f)) 4.0 || Math.Abs(float (Vec2.distance q (Vec2.create 12.0f 12.0f)) - 4.0) < 1e-3) | _ -> false)
    check "an ellipse has the two radii" (match flat (Ellipse (0.0, 0.0, 6.0, 2.0)) with [ p ] -> p.Points |> Array.exists (fun q -> nearf q.X 6.0 || nearf q.X -6.0) && p.Points |> Array.forall (fun q -> Math.Abs(float q.Y) <= 2.0 + 1e-3) | _ -> false)
    check "a line is two points" (match flat (Line (1.0, 2.0, 3.0, 4.0)) with [ p ] -> p.Points.Length = 2 && not p.Closed | _ -> false)
    check "a polyline is open, a polygon is closed" (match flat (Polyline [ 0.0, 0.0; 1.0, 0.0; 1.0, 1.0 ]), flat (Polygon [ 0.0, 0.0; 1.0, 0.0; 1.0, 1.0 ]) with [ a ], [ b ] -> not a.Closed && b.Closed && a.Points.Length = 3 | _ -> false)
    check "a rectangle without a radius is its four corners" (match flat (Rect (1.0, 2.0, 10.0, 6.0, 0.0)) with [ p ] -> p.Closed && p.Points.Length = 4 | _ -> false)
    check "a rectangle with a radius stays inside its bounds and has round corners" (match flat (Rect (3.0, 3.0, 18.0, 18.0, 2.0)) with [ p ] -> p.Closed && p.Points.Length > 8 && p.Points |> Array.forall (fun q -> q.X >= 2.999f && q.X <= 21.001f && q.Y >= 2.999f && q.Y <= 21.001f) && not (p.Points |> Array.exists (fun q -> nearf q.X 3.0 && nearf q.Y 3.0)) | _ -> false)
    check "a radius larger than half the side is the half" (match flat (Rect (0.0, 0.0, 10.0, 4.0, 99.0)) with [ p ] -> p.Points |> Array.forall (fun q -> q.Y >= -0.001f && q.Y <= 4.001f) | _ -> false)
    check "an empty polyline has no commands" (IconScene.commands (Polyline []) = [])

/// every scene node of a scene, to count them
let rec private nodes (s: Scene) : int =
    match s with
    | Group xs -> xs |> List.sumBy nodes
    | Empty -> 0
    | _ -> 1

let private lucide : Icon list =
    [ Lucide.heading; Lucide.text; Lucide.image; Lucide.mousePointerClick; Lucide.quote; Lucide.minus ]

let private sceneTests () =
    let size = 24.0f
    let scene = IconScene.view size Color.White Lucide.minus
    check "an icon of one line is one segment" (nodes scene = 1)
    check "the icon heading has its three strokes" (nodes (IconScene.view size Color.White Lucide.heading) = 3)
    check "a larger icon has more segments for its curves" (nodes (IconScene.view 96.0f Color.White Lucide.image) > nodes (IconScene.view 16.0f Color.White Lucide.image))
    check "the polylines of an icon at the grid size are within the grid and its stroke" (lucide |> List.forall (fun i -> IconScene.polylines 1.0 i |> List.forall (fun p -> p.Points |> Array.forall (fun q -> q.X > -1.0f && q.X < 25.0f && q.Y > -1.0f && q.Y < 25.0f))))
    check "no coordinate is not a number" (lucide |> List.forall (fun i -> IconScene.polylines 2.0 i |> List.forall (fun p -> p.Points |> Array.forall (fun q -> not (Single.IsNaN q.X) && not (Single.IsNaN q.Y)))))
    check "every shape of the icons of the set gives at least one polyline" (lucide |> List.forall (fun i -> i.Shapes |> List.forall (fun s -> not (IconScene.commands s |> PathData.flatten 2.0 |> List.isEmpty))))

let run () : int =
    parseTests ()
    flattenTests ()
    shapeTests ()
    sceneTests ()
    printfn "%d checks, %d failed" count failures
    failures

#if FABLE_COMPILER
let icons_run () : int = run ()
/// how many checks the last run made (the host checks that it is not zero)
let icons_checks () : int = count
/// how many have failed so far, and which (the number of the check, from 1): a host that cannot read what the run prints
let icons_failures () : int = failures
let icons_failed (i: int) : int = failedAt.[i]
#else
[<EntryPoint>]
let main _ = if run () = 0 then 0 else 1
#endif
