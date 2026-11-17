namespace FSharp.Graphics.Canvas

open FSharp.Graphics.Core
open FSharp.Graphics.Primitives
open FSharp.Graphics.Scene

/// Resolved scene -> flat draw commands, immediate mode (no diff, no cache: the canvas is redrawn every frame).
/// The translation matches FSharp.Graphics.Render.CachedNode.emit, which feeds the encoder of the GPU hosts.
module Emit =
    let rec emit (out: ResizeArray<DrawCommand>) (s: Scene) : unit =
        let layer (p: LayerParams) (child: Scene) =
            out.Add(PushLayer p)
            emit out child
            out.Add PopLayer
        match s with
        | Scene.Empty -> ()
        | Scene.Shape p -> DrawList.emitShape out p
        // unresolved text has no glyphs: Resolve.resolve turns it into a GlyphRun first
        | Scene.Text _ -> ()
        | Scene.GlyphRun(fid, size, color, glyphs, _) ->
            if glyphs.Length > 0 then out.Add(DrawTextBatch(fid, size, glyphs, color))
        | Scene.Image(id, r, op) -> out.Add(DrawImage(id, r, op))
        | Scene.Group children | Scene.Stack(_, children) -> for c in children do emit out c
        | Scene.Clip(region, child) ->
            out.Add(PushClip region)
            emit out child
            out.Add PopClip
        | Scene.Padding(_, child) | Scene.Reactive(_, child) | Scene.Semantic(_, child) | Scene.Animated(_, child) | Scene.Mask(_, child) -> emit out child
        | Scene.Transform(m, child) ->
            out.Add(PushTransform m)
            emit out child
            out.Add PopTransform
        | Scene.Opacity(a, child) -> if a >= 1.0f then emit out child else layer { LayerParams.none with Alpha = a } child
        | Scene.Blur(sigma, child) -> if sigma <= 0.0f then emit out child else layer { LayerParams.none with BlurSigma = sigma } child
        | Scene.ColorFilter(m, child) -> layer { LayerParams.none with Filter = m } child
        | Scene.BlendMode(b, child) -> layer { LayerParams.none with Blend = b } child
