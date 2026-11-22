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

    let private earliest (a: float32 voption) (b: float32 voption) : float32 voption =
        match a, b with
        | ValueSome x, ValueSome y -> ValueSome(min x y)
        | ValueSome _, ValueNone -> a
        | _ -> b

    /// When the clock next changes how the scene looks, in milliseconds from `timeMs` (the time since the scene's clock started):
    /// `ValueSome 0` while an animation that moves continuously runs (a frame is due every time), `ValueSome d` when only stepped ones run (`Easing.Steps`: the next step is `d` ms away
    /// and the picture stands still until then, as a caret that blinks), `ValueNone` when nothing depends on the clock.  A host that draws only what changed sleeps until then.
    let rec animationDelay (timeMs: float32) (s: Scene) : float32 voption =
        match s with
        | Empty | Shape _ | Text _ | Image _ | GlyphRun _ -> ValueNone
        | Animated(t, child) ->
            let own =
                if Timeline.isDone t timeMs then ValueNone
                else
                    match t.Easing with
                    | Steps(count, _) when count > 0 && t.DurationMs > 0.0f ->
                        let period = t.DurationMs / float32 count
                        let since = timeMs - t.DelayMs
                        if since < 0.0f then ValueSome(-since)
                        else ValueSome(period - (since - period * Scalar.floor (since / period)))
                    | _ -> ValueSome 0.0f
            earliest own (animationDelay timeMs child)
        | Group children | Stack(_, children) -> children |> List.fold (fun acc c -> earliest acc (animationDelay timeMs c)) ValueNone
        | Padding(_, child) | Clip(_, child)
        | Transform(_, child) | Opacity(_, child)
        | Reactive(_, child) | Semantic(_, child)
        | Blur(_, child) | ColorFilter(_, child)
        | BlendMode(_, child) -> animationDelay timeMs child
        | Mask(mask, child) -> earliest (animationDelay timeMs mask) (animationDelay timeMs child)

    /// True while the scene changes with the clock: an `Animated` node whose transition has not run to completion at `timeMs`.
    let isAnimating (timeMs: float32) (s: Scene) : bool = (animationDelay timeMs s).IsSome
