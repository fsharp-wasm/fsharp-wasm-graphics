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
open FSharp.Graphics.Primitives
open FSharp.Graphics.Scene
open FSharp.Graphics.Scenes
open FSharp.Graphics.Scenes.Kit

/// The scenes are laid out for a window about this wide; a narrower canvas shows them scaled down (hit-testing follows).
[<Literal>]
let DesignWidth = 800.0f

/// All mutable state of the page, in one object (a module-level `let mutable` is a global; this keeps it together).
type Host() =
    let text = CanvasTextEngine() :> ITextEngine
    let scenes = List.toArray Scenes.all
    let dispatcher = Hit.Dispatcher()
    let commands = ResizeArray<DrawCommand>(2048)
    let mutable resolved = Scene.Empty
    let mutable width = 1024.0f
    let mutable height = 768.0f
    let mutable ratio = 1.0f
    let mutable index = 0
    let mutable shownAt = -1.0
    let mutable dirty = true

    let fit () = min 1.0f (width / DesignWidth)

    member _.Count = scenes.Length
    member _.Index = index
    member _.Scenes = scenes

    member _.Resize(w: float32, h: float32, r: float32) =
        width <- w
        height <- h
        ratio <- r
        dirty <- true

    member _.Select(i: int) =
        let next = max 0 (min (scenes.Length - 1) i)
        if next <> index then
            index <- next
            shownAt <- -1.0
            dirty <- true
        index

    member _.Input(evt: InputEvent) : bool =
        let handled = dispatcher.Handle(resolved, evt)
        if handled then dirty <- true
        handled

    member _.Frame(nowMs: float) =
        let def = scenes.[index]
        if shownAt < 0.0 then shownAt <- nowMs
        if def.Animated || dirty then
            dirty <- false
            let k = fit ()
            let ctx = { Time = float32 (nowMs - shownAt); Size = Vec2.create (width / k) (height / k); Font = FontId 0UL }
            let scene = def.Build ctx
            resolved <- Resolve.resolve text (width / k) (if k < 1.0f then Scene.scale k k scene else scene)
            commands.Clear()
            Emit.emit commands resolved
            Gfx.beginFrame (float width) (float height) (float ratio)
            CanvasSink.run commands

let private host = Host()

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
