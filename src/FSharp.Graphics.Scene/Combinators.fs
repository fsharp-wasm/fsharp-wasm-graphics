namespace FSharp.Graphics.Scene

open FSharp.Math

/// Additional scene query and traversal helpers
[<AutoOpen>]
module SceneCombinators =
    /// Recursively collect all leaf nodes from a scene tree
    let rec collectLeaves (s: Scene) : Scene list =
        match s with
        | Shape _ | Text _ | Image _ | GlyphRun _ -> [s]
        | Empty -> []
        | Group children | Stack(_, children) ->
            children |> List.collect collectLeaves
        | Padding(_, child) | Clip(_, child)
        | Transform(_, child) | Opacity(_, child)
        | Reactive(_, child) | Semantic(_, child) | Animated(_, child)
        | Blur(_, child) | ColorFilter(_, child)
        | BlendMode(_, child) ->
            collectLeaves child
        | Mask(mask, child) ->
            collectLeaves mask @ collectLeaves child

    /// Count all nodes in a scene tree
    let rec countNodes (s: Scene) : int =
        match s with
        | Empty -> 0
        | Shape _ | Text _ | Image _ | GlyphRun _ -> 1
        | Group children | Stack(_, children) ->
            1 + (children |> List.sumBy countNodes)
        | Padding(_, child) | Clip(_, child)
        | Transform(_, child) | Opacity(_, child)
        | Reactive(_, child) | Semantic(_, child) | Animated(_, child)
        | Blur(_, child) | ColorFilter(_, child)
        | BlendMode(_, child) ->
            1 + countNodes child
        | Mask(mask, child) ->
            1 + countNodes mask + countNodes child

    /// True while the scene changes with the clock: an `Animated` node whose transition has not run to completion at `timeMs` (milliseconds since the scene's clock started).
    /// A host that draws only what changed uses it to know that a frame is due although nothing else happened: a scene that stands still costs nothing.
    let rec isAnimating (timeMs: float32) (s: Scene) : bool =
        match s with
        | Empty | Shape _ | Text _ | Image _ | GlyphRun _ -> false
        | Animated(t, child) -> not (Timeline.isDone t timeMs) || isAnimating timeMs child
        | Group children | Stack(_, children) -> children |> List.exists (isAnimating timeMs)
        | Padding(_, child) | Clip(_, child)
        | Transform(_, child) | Opacity(_, child)
        | Reactive(_, child) | Semantic(_, child)
        | Blur(_, child) | ColorFilter(_, child)
        | BlendMode(_, child) -> isAnimating timeMs child
        | Mask(mask, child) -> isAnimating timeMs mask || isAnimating timeMs child
