module FSharp.Graphics.CanvasTests.Tests

open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Primitives
open FSharp.Graphics.Canvas

let mutable private passed = 0
let private failures = ResizeArray<string>()
let private check (name: string) (ok: bool) =
    if ok then passed <- passed + 1
    else
        failures.Add name
        printfn "  FAIL  %s" name

let private canvas = Rect.create 0.0f 0.0f 1000.0f 700.0f
let private colour r g b : Color = { R = r; G = g; B = b; A = 255uy }
let private rect (x: float32) (y: float32) (w: float32) (h: float32) (fill: Color) : PrimitiveShape =
    Rectangle { Bounds = Rect.create x y w h; Radius = CornerRadius.zero; Fill = Solid fill; Stroke = None; Shadow = None }
let private list (cs: DrawCommand list) : ResizeArray<DrawCommand> = ResizeArray<DrawCommand>(cs)
let private draw (s: PrimitiveShape) : DrawCommand list = DrawList.fromShape s
let private covers (r: Rect) (inner: Rect) = r.X <= inner.X && r.Y <= inner.Y && r.X + r.W >= inner.X + inner.W && r.Y + r.H >= inner.Y + inner.H

/// the one region of a change that has exactly one
let private asRegion (c: Damage.Change) : Rect option =
    match c with
    | Damage.Regions [| r |] -> Some r
    | _ -> None

let tests () =
    // a panel and three buttons, the middle one is hovered in the second frame
    let panel = draw (rect 0.0f 0.0f 1000.0f 100.0f (colour 20uy 20uy 30uy))
    let button i hovered = draw (rect (20.0f + 120.0f * float32 i) 20.0f 100.0f 36.0f (if hovered then colour 90uy 90uy 255uy else colour 50uy 50uy 200uy))
    let frame hovered = list (panel @ button 0 false @ button 1 (hovered = 1) @ button 2 false)

    check "the same frame twice changes nothing" (Damage.between (frame 0) (frame 0) canvas = Damage.Nothing)
    check "an empty frame against an empty one changes nothing" (Damage.between (list []) (list []) canvas = Damage.Nothing)

    let hover = Damage.between (frame 0) (frame 1) canvas
    match asRegion hover with
    | Some r ->
        check "a button that is hovered changes the button and not much more" (covers r (Rect.create 140.0f 20.0f 100.0f 36.0f) && r.W < 130.0f && r.H < 60.0f)
        check "and the buttons beside it are outside the region" (not (Damage.touches r (Rect.create 20.0f 20.0f 100.0f 36.0f)) && not (Damage.touches r (Rect.create 260.0f 20.0f 100.0f 36.0f)))
    | None -> check "a button that is hovered is a region" false

    // two shapes that are the same are the same, though they are objects of their own
    let a = draw (rect 5.0f 5.0f 50.0f 50.0f (colour 1uy 2uy 3uy))
    let b = draw (rect 5.0f 5.0f 50.0f 50.0f (colour 1uy 2uy 3uy))
    check "equal commands made twice are the same" (List.forall2 Damage.same a b)
    check "a command that differs in its colour is not" (not (List.forall2 Damage.same a (draw (rect 5.0f 5.0f 50.0f 50.0f (colour 1uy 2uy 4uy)))))
    check "a command of another kind is not the same" (not (Damage.same (List.head a) PopClip))
    check "a push and its pop are not the same" (not (Damage.same PopTransform PopClip) && Damage.same PopTransform PopTransform)

    // two buttons changed that are far apart are two regions, and what lies between them is not drawn; two that touch are one
    let farApart = Damage.between (frame 0) (list (panel @ button 0 true @ button 1 false @ button 2 true)) canvas
    (match farApart with
     | Damage.Regions rs -> check "two changes far apart are two regions" (rs.Length = 2 && not (Damage.touches rs.[0] rs.[1]))
     | _ -> check "two changes far apart are regions" false)
    let neighbours = Damage.between (frame 0) (list (panel @ button 0 true @ button 1 true @ button 2 false)) canvas
    (match neighbours with
     | Damage.Regions rs -> check "two buttons side by side with a gap of a few pixels are one region" (rs.Length = 1)
     | _ -> check "two buttons side by side are a region" false)

    // two buttons in groups of their own (a transform each), both changed: the stretch between the changes closes the first group and opens the second, and still it is two regions
    let grouped (a: bool) (b: bool) =
        let group (dx: float32) (hovered: bool) = [ PushTransform (Mat3x2.translation dx 0.0f) ] @ draw (rect 20.0f 20.0f 100.0f 36.0f (if hovered then colour 90uy 90uy 255uy else colour 50uy 50uy 200uy)) @ [ PopTransform ]
        list (panel @ group 0.0f a @ group 500.0f b)
    (match Damage.between (grouped false false) (grouped true true) canvas with
     | Damage.Regions rs -> check "two changes in groups of their own are two regions" (rs.Length = 2 && covers rs.[0] (Rect.create 20.0f 20.0f 100.0f 36.0f) && covers rs.[1] (Rect.create 520.0f 20.0f 100.0f 36.0f))
     | _ -> check "two changes in groups of their own are regions" false)

    // a shape put in between: the region is where it is
    let extra = draw (rect 700.0f 300.0f 40.0f 40.0f (colour 200uy 0uy 0uy))
    let inserted = list (panel @ button 0 false @ button 1 false @ extra @ button 2 false)
    (match asRegion (Damage.between (frame 0) inserted canvas) with
     | Some r -> check "a shape put in between is the region, and only that" (covers r (Rect.create 700.0f 300.0f 40.0f 40.0f) && r.W < 60.0f && r.H < 60.0f)
     | None -> check "a shape put in between is a region" false)
    check "and taking it out again is the same region" (Damage.between inserted (frame 0) canvas = Damage.between (frame 0) inserted canvas)

    // under a transform that is the same in both frames: the region is where the transform puts it
    let moved (fill: Color) = list [ PushTransform (Mat3x2.translation 300.0f 300.0f) ] |> fun l -> (l.AddRange(draw (rect 0.0f 0.0f 50.0f 50.0f fill)); l.Add PopTransform; l)
    (match asRegion (Damage.between (moved (colour 1uy 1uy 1uy)) (moved (colour 9uy 9uy 9uy)) canvas) with
     | Some r -> check "a change inside a transform is where the transform puts it" (covers r (Rect.create 300.0f 300.0f 50.0f 50.0f) && r.X > 250.0f && r.Y > 250.0f)
     | None -> check "a change inside a transform is a region" false)

    // a transform that changes puts everything after it elsewhere: that is not a region
    let shifted (dx: float32) = list [ PushTransform (Mat3x2.translation dx 0.0f) ] |> fun l -> (l.AddRange(draw (rect 0.0f 0.0f 50.0f 50.0f (colour 1uy 1uy 1uy))); l.Add PopTransform; l)
    (match asRegion (Damage.between (shifted 10.0f) (shifted 20.0f) canvas) with
     | Some r -> check "a transform that changes is the region of what is drawn under it, before and now" (covers r (Rect.create 10.0f 0.0f 60.0f 50.0f) && r.W < 90.0f)
     | None -> check "a transform that changes is a region" false)
    check "a push without its pop in a frame, and a frame without it, are drawn whole" (Damage.between (list [ PushClip (Rect.create 0.0f 0.0f 5.0f 5.0f) ]) (list [ PushClip (Rect.create 0.0f 0.0f 9.0f 9.0f) ]) canvas = Damage.Everything)

    // a change inside a clip that is the same in both
    let clipped (fill: Color) = list [ PushClip (Rect.create 0.0f 0.0f 200.0f 200.0f) ] |> fun l -> (l.AddRange(draw (rect 10.0f 10.0f 30.0f 30.0f fill)); l.Add PopClip; l)
    (match asRegion (Damage.between (clipped (colour 1uy 1uy 1uy)) (clipped (colour 2uy 2uy 2uy)) canvas) with
     | Some r -> check "a change inside a clip is the region of the change" (covers r (Rect.create 10.0f 10.0f 30.0f 30.0f) && r.W < 60.0f)
     | None -> check "a change inside a clip is a region" false)
    check "a clip that changes with nothing drawn in it changes nothing" (Damage.between (list [ PushClip (Rect.create 0.0f 0.0f 10.0f 10.0f); PopClip ]) (list [ PushClip (Rect.create 0.0f 0.0f 20.0f 20.0f); PopClip ]) canvas = Damage.Nothing)

    // a blurred layer reaches beyond its shapes
    let blurred (sigma: float32) = list [ PushLayer { LayerParams.none with BlurSigma = sigma } ] |> fun l -> (l.AddRange(draw (rect 400.0f 300.0f 40.0f 40.0f (colour 1uy 1uy 1uy))); l.Add PopLayer; l)
    (match asRegion (Damage.between (blurred 4.0f) (blurred 6.0f) canvas) with
     | Some r -> check "a blurred layer is drawn with room for its blur (3 sigma)" (covers r (Rect.create 382.0f 282.0f 76.0f 76.0f))
     | None -> check "a blurred layer is a region" false)

    // a shadow reaches beyond its shape: blur, spread and offset
    let shadowed (blur: float32) =
        draw (Rectangle { Bounds = Rect.create 500.0f 400.0f 60.0f 30.0f; Radius = CornerRadius.zero; Fill = Solid (colour 5uy 5uy 5uy); Stroke = None
                          Shadow = Some { Offset = Vec2.create 0.0f 4.0f; Blur = blur; Spread = 0.0f; Color = colour 0uy 0uy 0uy; Inner = false } }) |> list
    (match asRegion (Damage.between (shadowed 8.0f) (shadowed 12.0f) canvas) with
     | Some r -> check "a shadow is drawn with room for its blur and its offset" (covers r (Rect.create 464.0f 372.0f 132.0f 90.0f))
     | None -> check "a shadow is a region" false)

    // text: the region holds the line, with its glyphs' advance
    let line (word: string) =
        let glyphs = word |> Seq.mapi (fun i ch -> { GlyphId = uint32 ch; X = 100.0f + 10.0f * float32 i; Y = 200.0f; AdvanceX = 10.0f }) |> Seq.toArray
        list [ DrawTextBatch(FontId 1UL, 16.0f, glyphs, colour 255uy 255uy 255uy) ]
    (match asRegion (Damage.between (line "Hello") (line "Help!") canvas) with
     | Some r -> check "a line of text is the region of its glyphs" (covers r (Rect.create 100.0f 185.0f 50.0f 20.0f) && r.W < 100.0f)
     | None -> check "a line of text is a region" false)

    // a change at the edge is cut to the canvas; one that covers most of it is the whole canvas
    let edge (fill: Color) = draw (rect 960.0f 660.0f 100.0f 100.0f fill) |> list
    (match asRegion (Damage.between (edge (colour 1uy 1uy 1uy)) (edge (colour 2uy 2uy 2uy)) canvas) with
     | Some r -> check "the region is cut to the canvas" (r.X + r.W <= 1000.0f && r.Y + r.H <= 700.0f)
     | None -> check "a change at the edge is a region" false)
    let whole (fill: Color) = draw (rect 0.0f 0.0f 1000.0f 700.0f fill) |> list
    check "a change over most of the canvas is a frame drawn whole" (Damage.between (whole (colour 1uy 1uy 1uy)) (whole (colour 2uy 2uy 2uy)) canvas = Damage.Everything)

    // what a region draws: the commands that reach it, and the state commands
    let st = Damage.initial
    check "a command outside the region does not reach it" (match Damage.reach st (List.head (draw (rect 500.0f 500.0f 10.0f 10.0f (colour 1uy 1uy 1uy)))) with
                                                            | ValueSome box -> not (Damage.touches (Rect.create 0.0f 0.0f 100.0f 100.0f) box)
                                                            | ValueNone -> false)
    check "a command that changes the state draws nothing" (Damage.reach st PopClip = ValueNone && Damage.reach st (PushTransform Mat3x2.identity) = ValueNone)

[<EntryPoint>]
let main _ =
    tests ()
    printfn "%d checks passed, %d failed" passed failures.Count
    failures.Count
