/// The module that becomes SceneGallery.wasm: the exports are what runtime/gfx.js (`mount`) calls, plus the selection.
///   start()                         bind the canvas, announce the scenes ("scene:<id>", title, covers) and the selected one
///   onResize(w, h, ratio)           CSS size of the canvas and the device pixel ratio
///   onPointer(kind, x, y, button)   kind 0 down, 1 up, 2 move, 3 leave
///   onWheel(dx, dy)                 (the scenes do not scroll)
///   onKey(kind)                     kind 0 down, 1 up; ← → Home End switch the scene
///   frame(nowMs)                    build, resolve, draw — only while the scene is animated or something changed
///   sceneCount() / select(i) / selected()
module FSharp.Graphics.Browser.SceneGallery

open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Scene
open FSharp.Graphics.Canvas
open FSharp.Graphics.Scenes
open FSharp.Graphics.Scenes.Kit

/// The scenes are laid out for a window about this wide; a narrower canvas shows them scaled down to fit (hit testing follows).
[<Literal>]
let DesignWidth = 800.0f

/// The selection on top of the frame plumbing of FSharp.Graphics.Canvas.
type Gallery() =
    let host = CanvasHost(CanvasTextEngine())
    let scenes = List.toArray Scenes.all
    let mutable index = 0

    member _.Count = scenes.Length
    member _.Index = index
    member _.Scenes = scenes
    member _.Resize(w: float32, h: float32, r: float32) = host.Resize(w, h, r)
    member _.Input(evt: InputEvent) = host.Input evt

    member _.Select(i: int) =
        let next = max 0 (min (scenes.Length - 1) i)
        if next <> index then
            index <- next
            host.Restart()
        index

    member _.Frame(nowMs: float) =
        let def = scenes.[index]
        host.Frame(nowMs, def.Animated, DesignWidth, fun time size -> def.Build { Time = time; Size = size; Font = FontId 0UL })

let private host = Gallery()

let private announceSelection () = Gfx.announce "selected" (string host.Index) ""

[<CompiledName("start")>]
let start () : unit =
    Gfx.init "gfx"
    for def in host.Scenes do
        Gfx.announce ("scene:" + def.Id) def.Title def.Covers
    announceSelection ()

[<CompiledName("onResize")>]
let onResize (w: float) (h: float) (ratio: float) : unit = host.Resize(float32 w, float32 h, float32 ratio)

[<CompiledName("onPointer")>]
let onPointer (kind: int) (x: float) (y: float) (button: int) : unit =
    let p = Vec2.create (float32 x) (float32 y)
    let evt =
        match kind with
        | 0 -> PointerDown(p, button)
        | 1 -> PointerUp(p, button)
        | 2 -> PointerMove p
        | _ -> PointerLeave p
    host.Input evt |> ignore

[<CompiledName("onWheel")>]
let onWheel (dx: float) (dy: float) : unit = host.Input(Scroll(Vec2.create (float32 dx) (float32 dy))) |> ignore

[<CompiledName("select")>]
let select (i: int) : unit =
    host.Select i |> ignore
    announceSelection ()

[<CompiledName("onKey")>]
let onKey (kind: int) : unit =
    let key = Gfx.pendingKey ()
    if kind = 0 then
        match key with
        | "ArrowRight" | "PageDown" -> select (host.Index + 1)
        | "ArrowLeft" | "PageUp" -> select (host.Index - 1)
        | "Home" -> select 0
        | "End" -> select (host.Count - 1)
        | _ -> host.Input(KeyDown key) |> ignore
    else
        host.Input(KeyUp key) |> ignore

[<CompiledName("sceneCount")>]
let sceneCount () : int = host.Count

[<CompiledName("selected")>]
let selected () : int = host.Index

[<CompiledName("frame")>]
let frame (nowMs: float) : unit = host.Frame nowMs
