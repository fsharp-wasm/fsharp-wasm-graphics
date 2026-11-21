/// The tests of FSharp.Graphics.Interact: where a pointer position drops, the drag session, scrolling near an edge.
module FSharp.Graphics.InteractTests.Tests

open FSharp.Math
open FSharp.Graphics.Interact

let mutable private failures = 0
let mutable private count = 0

let private check (name: string) (ok: bool) =
    count <- count + 1
    if not ok then
        failures <- failures + 1
        printfn "  FAIL  %s" name

let private pt (x: float) (y: float) : Vec2 = Vec2.create (float32 x) (float32 y)
let private slot (id: int) (x: float) (y: float) (w: float) (h: float) (nests: bool) : Slot =
    { Id = id; Bounds = Rect.create (float32 x) (float32 y) (float32 w) (float32 h); Flow = Down; Nests = nests }

/// three blocks of 40 px, 10 px apart, in a column 200 wide: ids 1, 2, 3 at y = 0, 50, 100
let private column : Slot list = [ slot 1 0.0 0.0 200.0 40.0 false; slot 2 0.0 50.0 200.0 40.0 false; slot 3 0.0 100.0 200.0 40.0 false ]

let private at (slots: Slot list) (x: float) (y: float) : (int * Position) option =
    match Drop.resolve slots (pt x y) with
    | Some d -> Some (d.Target, d.Position)
    | None -> None

let private dropTests () =
    check "no slots, no drop" (Drop.resolve [] (pt 10.0 10.0)).IsNone
    check "the upper half of a block is before it" (at column 50.0 10.0 = Some (1, Before))
    check "the lower half of a block is after it" (at column 50.0 30.0 = Some (1, After))
    check "the middle block, upper half" (at column 50.0 60.0 = Some (2, Before))
    check "the middle block, lower half" (at column 50.0 80.0 = Some (2, After))
    check "above the first block: before the first" (at column 50.0 (-30.0) = Some (1, Before))
    check "below the last block: after the last" (at column 50.0 400.0 = Some (3, After))
    check "in the gap, nearer to the upper block: after it" (at column 50.0 43.0 = Some (1, After))
    check "in the gap, nearer to the lower block: before it" (at column 50.0 47.0 = Some (2, Before))
    check "to the side of the column, level with a block, is that block" (at column 900.0 60.0 = Some (2, Before))
    // the mark
    let mark (x: float) (y: float) = (Drop.resolve column (pt x y)).Value.Mark
    check "the mark of a before is a line on the upper edge" (mark 50.0 10.0 = Rect.create 0.0f (0.0f - 1.5f) 200.0f 3.0f)
    check "the mark of an after is a line on the lower edge" (mark 50.0 30.0 = Rect.create 0.0f 38.5f 200.0f 3.0f)
    // containers
    let nested = [ slot 10 0.0 0.0 200.0 200.0 true ]
    check "a container: the top quarter is before" (at nested 50.0 20.0 = Some (10, Before))
    check "a container: the middle is into" (at nested 50.0 100.0 = Some (10, Into))
    check "a container: the bottom quarter is after" (at nested 50.0 180.0 = Some (10, After))
    check "the mark of an into is the outline of the container" ((Drop.resolve nested (pt 50.0 100.0)).Value.Mark = Rect.create 0.0f 0.0f 200.0f 200.0f)
    let inner = nested @ [ slot 11 10.0 60.0 180.0 40.0 false ]
    check "a slot inside a container wins where the pointer is in both" (at inner 50.0 70.0 = Some (11, Before))
    check "outside the inner slot the container is the one" (at inner 50.0 140.0 = Some (10, Into))
    // a row
    let row = [ { Id = 1; Bounds = Rect.create 0.0f 0.0f 40.0f 30.0f; Flow = Right; Nests = false }
                { Id = 2; Bounds = Rect.create 50.0f 0.0f 40.0f 30.0f; Flow = Right; Nests = false } ]
    check "a row reads the x axis: the left half is before" (at row 10.0 15.0 = Some (1, Before))
    check "a row: the right half of the second is after" (at row 80.0 15.0 = Some (2, After))
    check "the mark of a before in a row is a vertical line" ((Drop.resolve row (pt 10.0 15.0)).Value.Mark = Rect.create (0.0f - 1.5f) 0.0f 3.0f 30.0f)
    // a zero-size slot does not divide by zero
    check "an empty slot gives a position" ((Drop.resolve [ slot 7 5.0 5.0 0.0 0.0 false ] (pt 5.0 5.0)).IsSome)

/// a generated column of blocks of different heights: every pointer position gives a drop, and inside a block it is that block and the side of its middle
let private propertyTests () =
    let mutable seed = 2024
    let next () = (seed <- (seed * 1103515245 + 12345) &&& 0x7fffffff; (seed >>> 8) % 1000)
    let mutable wrong = 0
    for _ in 1 .. 200 do
        let n = 1 + next () % 8
        let mutable y = 0.0
        let slots =
            [ for i in 1 .. n do
                let h = 10.0 + float (next () % 60)
                let s = slot i 0.0 y 300.0 h false
                y <- y + h + float (next () % 20)
                yield s ]
        for s in slots do
            let cx = float (next () % 300)
            let cy = float s.Bounds.Y + float (next () % int (max 1.0f s.Bounds.H))
            match Drop.resolve slots (pt cx cy) with
            | Some d ->
                let upper = float32 cy < s.Bounds.Y + s.Bounds.H * 0.5f
                if d.Target <> s.Id || (upper && d.Position <> Before) || (not upper && d.Position <> After) then wrong <- wrong + 1
            | None -> wrong <- wrong + 1
    check "200 generated columns: inside a block the drop is that block, on the side of its middle" (wrong = 0)

let private sessionTests () =
    let slots = column
    let s0 : Session<string> = Drag.press "image" (pt 5.0 5.0)
    check "a press is a press" (match s0 with Pressed ("image", _) -> true | _ -> false)
    let tiny = Drag.move slots (pt 7.0 6.0) s0
    check "a small move stays a press: it is a click" (not (Drag.isDragging tiny))
    check "releasing a click drops nothing" (Drag.release tiny).IsNone
    let far = Drag.move slots (pt 60.0 60.0) s0
    check "beyond the threshold it is a drag" (Drag.isDragging far)
    check "a drag has its payload" (Drag.payload far = Some "image")
    check "a drag over a block has that drop" ((Drag.over far).Value.Target = 2)
    let moved = Drag.move slots (pt 60.0 130.0) far
    check "a drag follows the pointer to another block" ((Drag.over moved).Value.Target = 3 && (Drag.over moved).Value.Position = After)
    check "the release of a drag over a slot is the payload and the drop" (match Drag.release moved with Some ("image", d) -> d.Target = 3 | _ -> false)
    let outside = Drag.move [] (pt 60.0 60.0) far
    check "a drag over no slot has no drop" ((Drag.over outside).IsNone)
    check "releasing over no slot drops nothing" (Drag.release outside).IsNone
    check "cancel is idle and drops nothing" ((Drag.cancel () : Session<string>) = Idle && (Drag.release (Drag.cancel () : Session<string>)).IsNone)
    check "moving an idle session does nothing" (Drag.move slots (pt 60.0 60.0) (Idle : Session<string>) = Idle)
    // a drag does not start by itself from a position inside the threshold of the origin in both axes
    check "exactly at the threshold it is a drag" (Drag.isDragging (Drag.move slots (pt 10.0 5.0) s0))

let private scrollTests () =
    let view = Rect.create 0.0f 0.0f 400.0f 600.0f
    let near (x: float) (y: float) = Scroll.near view 40.0f 20.0f (pt x y)
    check "the quiet middle does not scroll" (near 200.0 300.0 = Vec2.create 0.0f 0.0f)
    check "near the bottom edge it scrolls down" ((near 200.0 590.0).Y > 0.0f && (near 200.0 590.0).X = 0.0f)
    check "near the top edge it scrolls up" ((near 200.0 10.0).Y < 0.0f)
    check "the nearer to the border the faster" ((near 200.0 599.0).Y > (near 200.0 575.0).Y)
    check "at the border it is the full speed" ((near 200.0 600.0).Y = 20.0f)
    check "beyond the border it stays at the full speed" ((near 200.0 700.0).Y = 20.0f)
    check "the sides scroll too" ((near 395.0 300.0).X > 0.0f && (near 5.0 300.0).X < 0.0f)
    check "no room for an edge: no scroll" ((Scroll.near view 0.0f 20.0f (pt 399.0 599.0)) = Vec2.create 0.0f 0.0f)

let run () : int =
    dropTests ()
    propertyTests ()
    sessionTests ()
    scrollTests ()
    printfn "%d checks, %d failed" count failures
    failures

#if FABLE_COMPILER
let interact_run () : int = run ()
/// how many checks the last run made (the host checks that it is not zero)
let interact_checks () : int = count
#else
[<EntryPoint>]
let main _ = if run () = 0 then 0 else 1
#endif
