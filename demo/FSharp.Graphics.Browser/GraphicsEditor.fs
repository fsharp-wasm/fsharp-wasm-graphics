/// The module that becomes GraphicsEditor.wasm: the exports are what runtime/gfx.js (`mount`) calls.
///   start()                         create the app and bind the canvas
///   onResize(w, h, ratio)           CSS size of the canvas and the device pixel ratio
///   onPointer(kind, x, y, button)   kind 0 down, 1 up, 2 move, 3 leave
///   onWheel(dx, dy)
///   onKey(kind)                     kind 0 down, 1 up; the key name is fetched with Gfx.pendingKey
///   frame(nowMs)                    build, resolve, draw
module FSharp.Graphics.Browser.GraphicsEditor

open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Primitives
open FSharp.Graphics.Scene
open FSharp.Graphics.GuiBuilder
open FSharp.Graphics.Scenes.Kit

/// All mutable state of the page, in one object (a module-level `let mutable` is a global; this keeps it together).
type Host() =
    let text = CanvasTextEngine() :> ITextEngine
    let app = App text
    let dispatcher = Hit.Dispatcher()
    let commands = ResizeArray<DrawCommand>(1024)
    let mutable resolved = Scene.Empty
    let mutable width = 1024.0f
    let mutable height = 768.0f
    let mutable ratio = 1.0f
    let mutable started = -1.0

    member _.Resize(w: float32, h: float32, r: float32) =
        width <- w
        height <- h
        ratio <- r

    member _.Input(evt: InputEvent) : bool = dispatcher.Handle(resolved, evt)

    member _.Frame(nowMs: float) =
        if started < 0.0 then started <- nowMs
        let ctx = { Time = float32 (nowMs - started); Size = Vec2.create width height; Font = FontId 0UL }
        resolved <- Resolve.resolve text width (app.Build ctx)
        commands.Clear()
        Emit.emit commands resolved
        Gfx.beginFrame (float width) (float height) (float ratio)
        CanvasSink.run commands

let private host = Host()

[<CompiledName("start")>]
let start () : unit = Gfx.init "gfx"

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

[<CompiledName("onKey")>]
let onKey (kind: int) : unit =
    let key = Gfx.pendingKey ()
    host.Input(if kind = 0 then KeyDown key else KeyUp key) |> ignore

[<CompiledName("frame")>]
let frame (nowMs: float) : unit = host.Frame nowMs
