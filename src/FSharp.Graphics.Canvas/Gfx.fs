namespace FSharp.Graphics.Canvas

open Fable.Core

/// The drawing surface: every function is an import of `runtime/gfx.js` (fsharp-wasm-runtime).
/// Numbers cross as JS numbers; colour channels are 0..255 ints; text crosses as a JS string.
module Gfx =
    [<Import("init", "runtime/gfx.js")>]
    let init (canvasId: string) : unit = nativeOnly
    /// Start a frame: backing store for the device pixel ratio, reset state, clear.
    [<Import("begin", "runtime/gfx.js")>]
    let beginFrame (w: float) (h: float) (ratio: float) : unit = nativeOnly

    /// Redraw only this part of the canvas (CSS px): clip to it and clear it; `regionEnd` gives the clip back.
    [<Import("region", "runtime/gfx.js")>]
    let region (x: float) (y: float) (w: float) (h: float) : unit = nativeOnly
    [<Import("regionEnd", "runtime/gfx.js")>]
    let regionEnd () : unit = nativeOnly
    /// 1 when the page asks for every frame to be drawn whole (`?fullframes`), 0 otherwise.
    [<Import("fullFrames", "runtime/gfx.js")>]
    let fullFrames () : int = nativeOnly

    [<Import("save", "runtime/gfx.js")>]
    let save () : unit = nativeOnly
    [<Import("restore", "runtime/gfx.js")>]
    let restore () : unit = nativeOnly
    [<Import("transform", "runtime/gfx.js")>]
    let transform (a: float) (b: float) (c: float) (d: float) (e: float) (f: float) : unit = nativeOnly
    /// Multiplies the current alpha (a layer's group alpha).
    [<Import("layer", "runtime/gfx.js")>]
    let layer (alpha: float) (blend: int) (blurPx: float) : unit = nativeOnly
    /// A 4x5 colour matrix for the current layer: matrixBegin, 20 x matrixValue (row by row, offsets in 0..1), matrixUse.
    [<Import("matrixBegin", "runtime/gfx.js")>]
    let matrixBegin () : unit = nativeOnly
    [<Import("matrixValue", "runtime/gfx.js")>]
    let matrixValue (v: float) : unit = nativeOnly
    [<Import("matrixUse", "runtime/gfx.js")>]
    let matrixUse () : unit = nativeOnly
    [<Import("clip", "runtime/gfx.js")>]
    let clip (x: float) (y: float) (w: float) (h: float) : unit = nativeOnly
    /// Clip to the current path.
    [<Import("clipPath", "runtime/gfx.js")>]
    let clipPath () : unit = nativeOnly

    [<Import("pathBegin", "runtime/gfx.js")>]
    let pathBegin () : unit = nativeOnly
    [<Import("roundRect", "runtime/gfx.js")>]
    let roundRect (x: float) (y: float) (w: float) (h: float) (tl: float) (tr: float) (br: float) (bl: float) : unit = nativeOnly
    [<Import("ellipse", "runtime/gfx.js")>]
    let ellipse (x: float) (y: float) (rx: float) (ry: float) : unit = nativeOnly
    [<Import("moveTo", "runtime/gfx.js")>]
    let moveTo (x: float) (y: float) : unit = nativeOnly
    [<Import("lineTo", "runtime/gfx.js")>]
    let lineTo (x: float) (y: float) : unit = nativeOnly
    [<Import("closePath", "runtime/gfx.js")>]
    let closePath () : unit = nativeOnly

    [<Import("solid", "runtime/gfx.js")>]
    let solid (r: int) (g: int) (b: int) (a: int) : unit = nativeOnly
    [<Import("gradLinear", "runtime/gfx.js")>]
    let gradLinear (x0: float) (y0: float) (x1: float) (y1: float) : unit = nativeOnly
    [<Import("gradRadial", "runtime/gfx.js")>]
    let gradRadial (x: float) (y: float) (r: float) : unit = nativeOnly
    [<Import("gradStop", "runtime/gfx.js")>]
    let gradStop (offset: float) (r: int) (g: int) (b: int) (a: int) : unit = nativeOnly
    [<Import("gradUse", "runtime/gfx.js")>]
    let gradUse () : unit = nativeOnly

    [<Import("fill", "runtime/gfx.js")>]
    let fill () : unit = nativeOnly
    [<Import("fillEvenOdd", "runtime/gfx.js")>]
    let fillEvenOdd () : unit = nativeOnly
    [<Import("strokeStyle", "runtime/gfx.js")>]
    let strokeStyle (r: int) (g: int) (b: int) (a: int) (width: float) (cap: int) (join: int) (miter: float) : unit = nativeOnly
    [<Import("stroke", "runtime/gfx.js")>]
    let stroke () : unit = nativeOnly

    /// Begin a shadow-only draw: what is filled next is moved off the canvas, its shadow lands in place.
    [<Import("shadowBegin", "runtime/gfx.js")>]
    let shadowBegin (ox: float) (oy: float) (blur: float) (r: int) (g: int) (b: int) (a: int) : unit = nativeOnly
    [<Import("shadowEnd", "runtime/gfx.js")>]
    let shadowEnd () : unit = nativeOnly

    [<Import("text", "runtime/gfx.js")>]
    let text (s: string) (x: float) (y: float) (size: float) (weight: int) (r: int) (g: int) (b: int) (a: int) : unit = nativeOnly
    [<Import("measure", "runtime/gfx.js")>]
    let measure (s: string) (size: float) (weight: int) : float = nativeOnly

    [<Import("cursor", "runtime/gfx.js")>]
    let cursor (kind: int) : unit = nativeOnly
    /// Tell the page something that is not drawing: a `gfx` event on the canvas (detail = { kind, a, b }).  A module can send
    /// strings out but not receive them, so this is how it names its parts (the scenes of the gallery, the selected one).
    [<Import("announce", "runtime/gfx.js")>]
    let announce (kind: string) (a: string) (b: string) : unit = nativeOnly
    /// The key of the key event being delivered.
    [<Import("pendingKey", "runtime/gfx.js")>]
    let pendingKey () : string = nativeOnly
