namespace FSharp.Graphics.Diagrams

open System
open System.Collections.Generic
open FSharp.Math

/// A box to place: its id and its size.
type Box = { Id: int; W: float32; H: float32 }

/// A link from one box to another (by id).  A link to itself, to a box that is not there, and a second link between the same two boxes are left out.
type Link = { From: int; To: int }

/// The direction the layers run in.
type Direction =
    /// The first layer on top, each next one below it.
    | TopDown
    /// The first layer at the left, each next one to its right.
    | LeftRight

/// Room between the boxes: between two layers, between two boxes of a layer, and around the diagram.
type Spacing = { Layer: float32; Box: float32; Margin: float32 }

module Spacing =
    let standard : Spacing = { Layer = 56.0f; Box = 24.0f; Margin = 16.0f }

/// A box where it was placed.
type Placed = { Id: int; Layer: int; Rect: Rect }

/// A link as a line from the box it leaves to the box it enters: its last segment is where an arrow goes.  `Reversed`: the link ran against the layers (it closed a cycle), and the line
/// enters the box above it (or at its left) rather than leaving it.
type Route = { From: int; To: int; Points: Vec2[]; Reversed: bool }

type Layout = { Boxes: Placed[]; Routes: Route[]; Size: Vec2 }

/// The layered layout of a directed graph (Sugiyama): the links that close a cycle are turned round, every box gets the layer after the last box that links to it, a link that spans layers
/// is cut by a point in each layer it crosses, the order within the layers is found by sweeps of the barycentre (the order with the fewest crossings seen is kept), and the places in
/// a layer are the ones nearest to the middle of the neighbours (least squares) that keep the boxes apart.  A box with several links has them leave and enter at different points of its side.
module Layered =
    /// The sweeps of the order: each is one pass down and one up, and the search ends sooner when no link crosses another.
    [<Literal>]
    let Sweeps = 12

    /// The passes of the places.
    [<Literal>]
    let Passes = 8

    /// The most that a box lets its links spread along its side, in pixels between two of them.
    [<Literal>]
    let PortStep = 14.0f

    /// The links that can be drawn, as pairs of indices into `boxes` (a link of a box to itself, of an unknown box, and a link seen before are dropped).
    let private pairs (index: Dictionary<int, int>) (links: Link[]) : (int * int)[] =
        let seen = HashSet<int64>()
        [| for l in links do
             match index.TryGetValue l.From, index.TryGetValue l.To with
             | (true, a), (true, b) when a <> b && seen.Add(int64 a * 4294967296L + int64 b) -> yield (a, b)
             | _ -> () |]

    /// The links that close a cycle are reversed: a depth-first walk from every box in turn marks the link to a box that is still on the way.  The result has the pair as it goes with the
    /// layers, and whether it was turned.
    let private acyclic (n: int) (edges: (int * int)[]) : (int * int * bool)[] =
        let out = Array.init n (fun _ -> ResizeArray<int>())
        edges |> Array.iteri (fun e (a, _) -> out.[a].Add e)
        // 0 not seen, 1 on the way, 2 done
        let state = Array.zeroCreate<int> n
        let back = HashSet<int>()
        let stack = Stack<int * int>()
        for root in 0 .. n - 1 do
            if state.[root] = 0 then
                state.[root] <- 1
                stack.Push((root, 0))
                while stack.Count > 0 do
                    let (v, k) = stack.Pop()
                    if k < out.[v].Count then
                        stack.Push((v, k + 1))
                        let e = out.[v].[k]
                        let w = snd edges.[e]
                        if state.[w] = 1 then back.Add e |> ignore
                        elif state.[w] = 0 then
                            state.[w] <- 1
                            stack.Push((w, 0))
                    else state.[v] <- 2
        edges |> Array.mapi (fun e (a, b) -> if back.Contains e then (b, a, true) else (a, b, false))

    /// The layer of every box: the longest way to it from a box that nothing links to, and then a box that nothing links to is moved next to the nearest box it links to (it would
    /// otherwise be pulled to the first layer by one long link).
    let private layers (n: int) (dag: (int * int * bool)[]) : int[] =
        let out = Array.init n (fun _ -> ResizeArray<int>())
        let indeg = Array.zeroCreate<int> n
        for (a, b, _) in dag do
            out.[a].Add b
            indeg.[b] <- indeg.[b] + 1
        let layer = Array.zeroCreate<int> n
        let order = ResizeArray<int>()
        let queue = Queue<int>()
        let left = Array.copy indeg
        for v in 0 .. n - 1 do if left.[v] = 0 then queue.Enqueue v
        while queue.Count > 0 do
            let v = queue.Dequeue()
            order.Add v
            for w in out.[v] do
                if layer.[v] + 1 > layer.[w] then layer.[w] <- layer.[v] + 1
                left.[w] <- left.[w] - 1
                if left.[w] = 0 then queue.Enqueue w
        for i in order.Count - 1 .. -1 .. 0 do
            let v = order.[i]
            if indeg.[v] = 0 && out.[v].Count > 0 then
                let mutable nearest = Int32.MaxValue
                for w in out.[v] do nearest <- min nearest layer.[w]
                layer.[v] <- nearest - 1
        layer

    /// The number of pairs of links that cross between two neighbouring layers: for the links in the order of their upper end, the pairs whose lower ends are the other way round.
    let private crossings (links: (int * int)[]) (lowerSize: int) : int =
        let sorted = links |> Array.sortWith (fun (a, b) (c, d) -> if a <> c then compare a c else compare b d)
        let tree = Array.zeroCreate<int> (lowerSize + 1)
        let mutable total = 0
        let mutable placed = 0
        for (_, b) in sorted do
            // how many of the links before it end at or after b: the ones that cross it
            let mutable i = b + 1
            let mutable atOrBefore = 0
            while i > 0 do
                atOrBefore <- atOrBefore + tree.[i]
                i <- i - (i &&& -i)
            total <- total + (placed - atOrBefore)
            let mutable j = b + 1
            while j <= lowerSize do
                tree.[j] <- tree.[j] + 1
                j <- j + (j &&& -j)
            placed <- placed + 1
        total

    /// The least squares fit of `want` that is not closer than `gap` between neighbours (the pool-adjacent-violators solution of ordered regression): the places of a layer.
    let private apart (want: float32[]) (gap: float32[]) : float32[] =
        let m = want.Length
        if m = 0 then [||]
        else
            // shift by the distance the boxes must keep, so that the places only have to grow: p.[i] = q.[i] + shift.[i]
            let shift = Array.zeroCreate<float> m
            for i in 1 .. m - 1 do shift.[i] <- shift.[i - 1] + float gap.[i - 1]
            let sum = Array.zeroCreate<float> m
            let cnt = Array.zeroCreate<int> m
            let mutable top = 0
            for i in 0 .. m - 1 do
                sum.[top] <- float want.[i] - shift.[i]
                cnt.[top] <- 1
                top <- top + 1
                while top > 1 && sum.[top - 2] / float cnt.[top - 2] > sum.[top - 1] / float cnt.[top - 1] do
                    sum.[top - 2] <- sum.[top - 2] + sum.[top - 1]
                    cnt.[top - 2] <- cnt.[top - 2] + cnt.[top - 1]
                    top <- top - 1
            let out = Array.zeroCreate<float32> m
            let mutable i = 0
            for b in 0 .. top - 1 do
                let level = sum.[b] / float cnt.[b]
                for _ in 1 .. cnt.[b] do
                    out.[i] <- float32 (level + shift.[i])
                    i <- i + 1
            out

    let private place (direction: Direction) (spacing: Spacing) (boxes: Box[]) (links: Link[]) : Layout =
        let n = boxes.Length
        let index = Dictionary<int, int>()
        boxes |> Array.iteri (fun i b -> index.[b.Id] <- i)
        let edges = pairs index links
        let dag = acyclic n edges
        let layer0 = layers n dag
        let top = Array.max layer0
        // the vertices: the boxes, then a point for every layer that a link crosses
        let layerOf = ResizeArray<int>(layer0)
        let chains = Array.zeroCreate<int[]> dag.Length
        dag |> Array.iteri (fun e (a, b, _) ->
            let chain = ResizeArray<int>()
            chain.Add a
            for l in layer0.[a] + 1 .. layer0.[b] - 1 do
                layerOf.Add l
                chain.Add(layerOf.Count - 1)
            chain.Add b
            chains.[e] <- chain.ToArray())
        let m = layerOf.Count
        // the links between neighbouring layers: (upper vertex, lower vertex)
        let segments = ResizeArray<int * int>()
        let seenSegment = HashSet<int64>()
        for chain in chains do
            for k in 0 .. chain.Length - 2 do
                // two links can share a segment (a link and the one against it): it is one line, drawn once
                if seenSegment.Add(int64 chain.[k] * 4294967296L + int64 chain.[k + 1]) then segments.Add((chain.[k], chain.[k + 1]))
        let up = Array.init m (fun _ -> ResizeArray<int>())
        let down = Array.init m (fun _ -> ResizeArray<int>())
        for (u, v) in segments do
            down.[u].Add v
            up.[v].Add u
        // the order of the vertices in their layer
        let rows = Array.init (top + 1) (fun _ -> ResizeArray<int>())
        for v in 0 .. m - 1 do rows.[layerOf.[v]].Add v
        let pos = Array.zeroCreate<int> m
        let number () = for r in rows do (for i in 0 .. r.Count - 1 do pos.[r.[i]] <- i)
        number ()
        let below = Array.init (top + 1) (fun _ -> ResizeArray<int * int>())
        for (u, v) in segments do below.[layerOf.[u]].Add((u, v))
        let total () =
            let mutable c = 0
            for l in 0 .. top - 1 do
                c <- c + crossings (below.[l].ToArray() |> Array.map (fun (u, v) -> (pos.[u], pos.[v]))) rows.[l + 1].Count
            c
        let barycentre (neighbours: ResizeArray<int>[]) (v: int) : float =
            let ns = neighbours.[v]
            if ns.Count = 0 then float pos.[v] else (ns |> Seq.sumBy (fun w -> float pos.[w])) / float ns.Count
        let sweep (l: int) (neighbours: ResizeArray<int>[]) =
            let keyed = rows.[l] |> Seq.map (fun v -> (barycentre neighbours v, pos.[v], v)) |> Array.ofSeq
            let sorted = keyed |> Array.sortWith (fun (k1, p1, _) (k2, p2, _) -> if k1 <> k2 then compare k1 k2 else compare p1 p2)
            rows.[l].Clear()
            for (_, _, v) in sorted do rows.[l].Add v
            number ()
        let mutable best = total ()
        let mutable bestRows = rows |> Array.map (fun r -> r.ToArray())
        let mutable sweeps = 0
        while best > 0 && sweeps < Sweeps do
            for l in 1 .. top do sweep l up
            for l in top - 1 .. -1 .. 0 do sweep l down
            let c = total ()
            if c < best then
                best <- c
                bestRows <- rows |> Array.map (fun r -> r.ToArray())
            sweeps <- sweeps + 1
        for l in 0 .. top do
            rows.[l].Clear()
            rows.[l].AddRange bestRows.[l]
        number ()
        // the size of a vertex across the layers and along them (a point of a link has none, but boxes keep half a gap from it)
        let isBox (v: int) = v < n
        let across (v: int) = if not (isBox v) then 0.0f elif direction = TopDown then boxes.[v].W else boxes.[v].H
        let along (v: int) = if not (isBox v) then 0.0f elif direction = TopDown then boxes.[v].H else boxes.[v].W
        let gapBetween (a: int) (b: int) = (across a + across b) * 0.5f + (if isBox a || isBox b then spacing.Box else spacing.Box * 0.5f)
        // the places across: the boxes packed first, then pulled towards the middle of their neighbours, a pass down and a pass up in turn
        let centre = Array.zeroCreate<float32> m
        for r in rows do
            let vs = r.ToArray()
            let packed = apart (Array.zeroCreate vs.Length) (Array.init (max 0 (vs.Length - 1)) (fun i -> gapBetween vs.[i] vs.[i + 1]))
            vs |> Array.iteri (fun i v -> centre.[v] <- packed.[i])
        let settle (l: int) (neighbours: ResizeArray<int>[]) =
            let vs = rows.[l].ToArray()
            let want = vs |> Array.map (fun v -> let ns = neighbours.[v] in if ns.Count = 0 then centre.[v] else (ns |> Seq.sumBy (fun w -> centre.[w])) / float32 ns.Count)
            let placed = apart want (Array.init (max 0 (vs.Length - 1)) (fun i -> gapBetween vs.[i] vs.[i + 1]))
            vs |> Array.iteri (fun i v -> centre.[v] <- placed.[i])
        for _ in 1 .. Passes do
            for l in 1 .. top do settle l up
            for l in top - 1 .. -1 .. 0 do settle l down
        // the places along: the layers one after the other, each as thick as its thickest box
        let thickness = Array.zeroCreate<float32> (top + 1)
        for v in 0 .. n - 1 do thickness.[layerOf.[v]] <- max thickness.[layerOf.[v]] (along v)
        let start = Array.zeroCreate<float32> (top + 1)
        let mutable at = spacing.Margin
        for l in 0 .. top do
            start.[l] <- at
            at <- at + thickness.[l] + spacing.Layer
        let lowest = rows |> Array.map (fun r -> r |> Seq.fold (fun acc v -> min acc (centre.[v] - across v * 0.5f)) infinityf) |> Array.min
        let highest = rows |> Array.map (fun r -> r |> Seq.fold (fun acc v -> max acc (centre.[v] + across v * 0.5f)) -infinityf) |> Array.max
        let shiftAcross = spacing.Margin - lowest
        let acrossSize = highest - lowest + 2.0f * spacing.Margin
        let alongSize = at - spacing.Layer + spacing.Margin
        // a point as (across, along)
        let mid (v: int) : float32 * float32 = (centre.[v] + shiftAcross, start.[layerOf.[v]] + thickness.[layerOf.[v]] * 0.5f)
        let toXY (c: float32, a: float32) : Vec2 = if direction = TopDown then Vec2.create c a else Vec2.create a c
        let rectOf (v: int) : Rect =
            let (c, a) = mid v
            let w = boxes.[v].W
            let h = boxes.[v].H
            if direction = TopDown then Rect.create (c - w * 0.5f) (a - h * 0.5f) w h else Rect.create (a - w * 0.5f) (c - h * 0.5f) w h
        // where a link meets a side of a box: spread along the side in the order of the vertices at the other end
        let leaving = Dictionary<int64, float32>()
        let entering = Dictionary<int64, float32>()
        let key (u: int) (v: int) = int64 u * 4294967296L + int64 v
        let ports (table: Dictionary<int64, float32>) (neighbours: ResizeArray<int>[]) (keyOf: int -> int -> int64) =
            for v in 0 .. m - 1 do
                let ns = neighbours.[v] |> Seq.sortBy (fun w -> pos.[w]) |> Array.ofSeq
                let step = if isBox v then min PortStep (across v * 0.8f / float32 (max 1 ns.Length)) else 0.0f
                ns |> Array.iteri (fun k w -> table.[keyOf v w] <- (float32 k - float32 (ns.Length - 1) * 0.5f) * step)
        ports leaving down key
        ports entering up (fun v u -> key u v)
        let boxesPlaced = Array.init n (fun v -> ({ Id = boxes.[v].Id; Layer = layer0.[v]; Rect = rectOf v } : Placed))
        let routes =
            dag |> Array.mapi (fun e (a, b, reversed) ->
                let chain = chains.[e]
                let pts = ResizeArray<Vec2>()
                for k in 0 .. chain.Length - 1 do
                    let v = chain.[k]
                    let (c, a0) = mid v
                    if isBox v then
                        // leaving a box: the side that faces the next layer; entering one: the side that faces the previous
                        let half = along v * 0.5f
                        if k < chain.Length - 1 then
                            pts.Add(toXY (c + leaving.[key v chain.[k + 1]], a0 + half))
                        else
                            pts.Add(toXY (c + entering.[key chain.[k - 1] v], a0 - half))
                    else pts.Add(toXY (c, a0))
                let points = if reversed then Array.rev (pts.ToArray()) else pts.ToArray()
                let (f, t) = if reversed then (b, a) else (a, b)
                ({ From = boxes.[f].Id; To = boxes.[t].Id; Points = points; Reversed = reversed } : Route))
        let size = if direction = TopDown then Vec2.create acrossSize alongSize else Vec2.create alongSize acrossSize
        { Boxes = boxesPlaced; Routes = routes; Size = size }

    /// The layout of `boxes` and the `links` between them.
    let layout (direction: Direction) (spacing: Spacing) (boxes: Box[]) (links: Link[]) : Layout =
        if boxes.Length = 0 then { Boxes = [||]; Routes = [||]; Size = Vec2.create (2.0f * spacing.Margin) (2.0f * spacing.Margin) }
        else place direction spacing boxes links
