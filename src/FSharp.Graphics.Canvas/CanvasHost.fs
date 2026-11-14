namespace FSharp.Graphics.Canvas

open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Primitives
open FSharp.Graphics.Scene

/// The frame plumbing of a module that draws onto one canvas: the size, the input, and build -> resolve -> emit -> draw.
/// All mutable state of the page lives in this one object (a module-level `let mutable` is a wasm global; this keeps it
/// together).  A host that owns a model (an editor) builds its scene from it every frame; one that shows pure scenes (a
/// gallery) draws a static scene only when something changed.
type CanvasHost(text: ITextEngine) =
    let dispatcher = Hit.Dispatcher()
    let commands = ResizeArray<DrawCommand>(2048)
    let mutable resolved = Scene.Empty
    let mutable width = 1024.0f
    let mutable height = 768.0f
    let mutable ratio = 1.0f
    let mutable started = -1.0
    let mutable dirty = true

    /// CSS width of the canvas in pixels.
    member _.Width = width
    /// CSS height of the canvas in pixels.
    member _.Height = height

    /// The canvas was resized (CSS pixels) or the device pixel ratio changed.
    member _.Resize(w: float32, h: float32, r: float32) =
        width <- w
        height <- h
        ratio <- r
        dirty <- true

    /// Draw the next frame although nothing animates (the model changed behind the host's back).
    member _.Invalidate() = dirty <- true

    /// Start the clock of the scene again (another scene was selected).
    member _.Restart() =
        started <- -1.0
        dirty <- true

    /// Deliver an input event to the handlers of the last drawn scene; true when one took it (the next frame redraws).
    member _.Input(evt: InputEvent) : bool =
        let handled = dispatcher.Handle(resolved, evt)
        if handled then dirty <- true
        handled

    /// One animation frame at `nowMs`.  The scene is built, resolved and drawn when `animated` or when something changed
    /// since the last frame; `build time size` gets the milliseconds since the last `Restart` and the size of the canvas in
    /// the units of the scene.  A canvas narrower than `designWidth` shows the scene scaled down to fit (hit testing follows
    /// the transform); 0 never scales.
    member _.Frame(nowMs: float, animated: bool, designWidth: float32, build: float32 -> Vec2 -> Scene) =
        if started < 0.0 then started <- nowMs
        if animated || dirty then
            dirty <- false
            let k = if designWidth > 0.0f then min 1.0f (width / designWidth) else 1.0f
            let size = Vec2.create (width / k) (height / k)
            let scene = build (float32 (nowMs - started)) size
            resolved <- Resolve.resolve text size.X (if k < 1.0f then Scene.scale k k scene else scene)
            commands.Clear()
            Emit.emit commands resolved
            Gfx.beginFrame (float width) (float height) (float ratio)
            CanvasSink.run commands
