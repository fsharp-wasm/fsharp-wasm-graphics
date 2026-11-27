/// The tests of FSharp.Graphics.Diagrams: the layered layout, and the scene that draws it.
module FSharp.Graphics.DiagramsTests.Tests

open System
open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Scene
open FSharp.Graphics.Charts
open FSharp.Graphics.Diagrams

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

let rec private shapes (s: Scene) : int =
    match s with
    | Shape _ -> 1
    | Group cs -> cs |> List.sumBy shapes
    | Clip(_, c) | Transform(_, c) | Opacity(_, c) | Semantic(_, c) -> shapes c
    | _ -> 0

let private box (id: int) : Box = { Id = id; W = 60.0f; H = 30.0f }
let private link (a: int) (b: int) : Link = { From = a; To = b }
let private spacing = Spacing.standard

let private placed (l: Layout) (id: int) : Placed = l.Boxes |> Array.find (fun b -> b.Id = id)
let private cx (p: Placed) = p.Rect.X + p.Rect.W * 0.5f
let private cy (p: Placed) = p.Rect.Y + p.Rect.H * 0.5f

/// Every link goes with the layers (a turned one against them), and no two boxes overlap, and everything is inside the size.
let private sound (direction: Direction) (l: Layout) : bool =
    let layersOk =
        l.Routes |> Array.forall (fun r ->
            let a = (placed l r.From).Layer
            let b = (placed l r.To).Layer
            if r.Reversed then b < a else a < b)
    let overlap (p: Placed) (q: Placed) =
        p.Id <> q.Id && p.Rect.X < q.Rect.X + q.Rect.W && q.Rect.X < p.Rect.X + p.Rect.W && p.Rect.Y < q.Rect.Y + q.Rect.H && q.Rect.Y < p.Rect.Y + p.Rect.H
    let apartOk = l.Boxes |> Array.forall (fun p -> l.Boxes |> Array.forall (fun q -> not (overlap p q)))
    let inside = l.Boxes |> Array.forall (fun p -> p.Rect.X >= 0.0f && p.Rect.Y >= 0.0f && p.Rect.X + p.Rect.W <= l.Size.X + 0.01f && p.Rect.Y + p.Rect.H <= l.Size.Y + 0.01f)
    let progress =
        l.Boxes |> Array.forall (fun p -> l.Boxes |> Array.forall (fun q -> p.Layer >= q.Layer || (if direction = TopDown then cy p < cy q else cx p < cx q)))
    layersOk && apartOk && inside && progress

/// Pairs of lines that cross: the segments of the routes between the same two layers are compared by the side they are on at both ends (a pair of segments that share an end does not cross).
let private crossingsOf (direction: Direction) (l: Layout) : int =
    let along (v: Vec2) = if direction = TopDown then v.Y else v.X
    let across (v: Vec2) = if direction = TopDown then v.X else v.Y
    let segments =
        [| for r in l.Routes do
             for k in 0 .. r.Points.Length - 2 do
                 let a = r.Points.[k]
                 let b = r.Points.[k + 1]
                 if abs (along a - along b) > 0.5f then yield (if along a < along b then (a, b) else (b, a)) |]
    let mutable c = 0
    for i in 0 .. segments.Length - 1 do
        for j in i + 1 .. segments.Length - 1 do
            let (a1, b1) = segments.[i]
            let (a2, b2) = segments.[j]
            if abs (along a1 - along a2) < 0.5f && abs (along b1 - along b2) < 0.5f then
                let top = across a1 - across a2
                let bottom = across b1 - across b2
                if (top < -0.01f && bottom > 0.01f) || (top > 0.01f && bottom < -0.01f) then c <- c + 1
    c

let private layoutTests () =
    let chain = Layered.layout TopDown spacing [| box 1; box 2; box 3 |] [| link 1 2; link 2 3 |]
    check "layout: a chain is one box per layer, in the order of the links" ((placed chain 1).Layer = 0 && (placed chain 2).Layer = 1 && (placed chain 3).Layer = 2 && sound TopDown chain)
    check "layout: a chain stands in a line (the places across are the ones nearest the neighbours)" (abs (cx (placed chain 1) - cx (placed chain 2)) < 0.01f && abs (cx (placed chain 2) - cx (placed chain 3)) < 0.01f)
    check "layout: a link leaves the bottom of its box and enters the top of the next" (chain.Routes.Length = 2 && abs (chain.Routes.[0].Points.[0].Y - ((placed chain 1).Rect.Y + 30.0f)) < 0.01f && abs (chain.Routes.[0].Points.[1].Y - (placed chain 2).Rect.Y) < 0.01f)
    let empty = Layered.layout TopDown spacing [||] [||]
    check "layout: no boxes, no layout" (empty.Boxes.Length = 0 && empty.Routes.Length = 0)
    let single = Layered.layout TopDown spacing [| box 7 |] [||]
    check "layout: one box has the margin around it" (single.Boxes.Length = 1 && single.Boxes.[0].Rect.X = spacing.Margin && single.Boxes.[0].Rect.Y = spacing.Margin && single.Size.X = 60.0f + 2.0f * spacing.Margin)
    let odd = Layered.layout TopDown spacing [| box 1; box 2 |] [| link 1 1; link 1 9; link 1 2; link 1 2; link 8 2 |]
    check "layout: a link to itself, to a box that is not there, and a repeated link are left out" (odd.Routes.Length = 1)
    let cycle = Layered.layout TopDown spacing [| box 1; box 2; box 3 |] [| link 1 2; link 2 3; link 3 1 |]
    check "layout: a cycle is drawn with one link against the layers" ((cycle.Routes |> Array.filter (fun r -> r.Reversed)).Length = 1 && sound TopDown cycle && (cycle.Boxes |> Array.map (fun b -> b.Layer) |> Array.sort) = [| 0; 1; 2 |])
    let back = cycle.Routes |> Array.find (fun r -> r.Reversed)
    check "layout: the link against the layers still ends at the box it points to" (back.From = 3 && back.To = 1 && abs (back.Points.[back.Points.Length - 1].Y - (placed cycle 1).Rect.Y - 30.0f) < 0.01f)
    let long = Layered.layout TopDown spacing [| box 1; box 2; box 3 |] [| link 1 2; link 2 3; link 1 3 |]
    let skip = long.Routes |> Array.find (fun r -> r.From = 1 && r.To = 3)
    check "layout: a link over a layer passes it beside the box of that layer" (skip.Points.Length = 3 && abs (skip.Points.[1].X - cx (placed long 2)) > 30.0f && sound TopDown long)
    let across = Layered.layout LeftRight spacing [| box 1; box 2; box 3 |] [| link 1 2; link 1 3 |]
    check "layout: left to right puts the layers one after the other along x" (cx (placed across 1) < cx (placed across 2) && cx (placed across 2) = cx (placed across 3) && sound LeftRight across)
    let again = Layered.layout TopDown spacing [| box 1; box 2; box 3 |] [| link 1 2; link 2 3; link 1 3 |]
    check "layout: the same graph is laid out the same way" (again = long)

let private treeTests () =
    // a binary tree whose boxes come in an order that crosses: the ids are scrambled, so that the first order of the sweeps is a bad one
    let depth = 5
    let n = (1 <<< depth) - 1
    let scramble i = (i * 17 + 5) % 997
    let boxes = Array.init n (fun i -> box (scramble i))
    let links = [| for i in 1 .. n - 1 -> link (scramble ((i - 1) / 2)) (scramble i) |]
    let l = Layered.layout TopDown spacing boxes links
    check "tree: a tree of 31 boxes is drawn in 5 layers, sound" (sound TopDown l && (l.Boxes |> Array.map (fun b -> b.Layer) |> Array.max) = 4 && l.Routes.Length = 30)
    check "tree: a tree has a drawing without crossings, and the sweeps find it" (crossingsOf TopDown l = 0)
    // a parent stands in the middle of its children
    let root = placed l (scramble 0)
    let kids = [ placed l (scramble 1); placed l (scramble 2) ]
    check "tree: the root is above the middle of its two children" (abs (cx root - (cx kids.[0] + cx kids.[1]) * 0.5f) < 1.0f)

let private randomTests () =
    let next = stream 42
    let n = 400
    let boxes = Array.init n (fun i -> { Id = i; W = float32 (30.0 + next () * 60.0); H = float32 (20.0 + next () * 20.0) })
    // a graph with cycles and long links
    let links = [| for _ in 1 .. 900 -> link (int (next () * float n)) (int (next () * float n)) |]
    let l = Layered.layout TopDown spacing boxes links
    check "random: 400 boxes and 900 links with cycles in them give a sound layout" (sound TopDown l)
    let drawn = links |> Array.filter (fun k -> k.From <> k.To) |> Array.map (fun k -> (k.From, k.To)) |> Array.distinct
    check "random: every link that can be drawn has a line" (l.Routes.Length = drawn.Length)
    let ends = l.Routes |> Array.forall (fun r ->
        let a = placed l r.From
        let b = placed l r.To
        let first = r.Points.[0]
        let last = r.Points.[r.Points.Length - 1]
        // the line starts on the top or bottom side of the box it leaves and ends on that of the box it enters, also when it runs against the layers
        let onSide (p: Placed) (v: Vec2) = abs (v.Y - p.Rect.Y) < 0.01f || abs (v.Y - (p.Rect.Y + p.Rect.H)) < 0.01f
        onSide a first && onSide b last)
    check "random: the lines begin and end on the boxes" ends

let private drawTests () =
    let th = Theme.dark (FontId 0UL)
    let l = Layered.layout TopDown spacing [| Diagram.fit th 1 "build"; Diagram.fit th 2 "test"; Diagram.fit th 3 "release" |] [| link 1 2; link 2 3; link 1 3 |]
    let scene = Diagram.scene th l (fun id -> "box " + string id) (fun id -> th.Palette.[id % th.Palette.Length])
    check "draw: a diagram is a rectangle and a head for each link, and two shapes for each box" (shapes scene >= 3 * 2 + 3 * 2)
    let b = placed l 2
    check "draw: the box at a point is the box that is there, and nothing else is anywhere else" (Diagram.boxAt l (cx b) (cy b) = Some 2 && Diagram.boxAt l -50.0f -50.0f = None)
    check "draw: a box is as wide as its text needs" ((Diagram.fit th 1 "a much longer label than the other one").W > (Diagram.fit th 1 "x").W && (Diagram.fit th 1 "x").W = 56.0f)

let run () : int =
    layoutTests ()
    treeTests ()
    randomTests ()
    drawTests ()
    printfn "%d checks, %d failed" count failures
    failures

#if FABLE_COMPILER
let diagrams_run () : int = run ()
/// how many checks the last run made (the host checks that it is not zero)
let diagrams_checks () : int = count
/// the number of the first check that failed, in the order the .NET run prints them (0: none)
let diagrams_first_failure () : int = firstFailure
#else
[<EntryPoint>]
let main _ = if run () = 0 then 0 else 1
#endif
