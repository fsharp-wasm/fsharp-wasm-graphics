namespace FSharp.Graphics.Charts

open System
open FSharp.Math

/// A colour map: anchors at equal steps of [0, 1], blended linearly between neighbours.  A value outside [0, 1] takes the colour of the nearest end; NaN has the colour `Missing`.
type Colormap = { Name: string; Stops: Color[]; Missing: Color }

module Colormap =
    let private hex (h: int) = Color.hex (uint32 h)

    /// Viridis (Nathaniel Smith, Stefan van der Walt and Eric Firing, CC0) at ten of its steps: perceptually even, readable in grey and with the common forms of colour blindness.
    let viridis : Colormap =
        { Name = "viridis"
          Stops = [| hex 0x440154; hex 0x482878; hex 0x3E4989; hex 0x31688E; hex 0x26828E; hex 0x1F9E89; hex 0x35B779; hex 0x6ECE58; hex 0xB5DE2B; hex 0xFDE725 |]
          Missing = hex 0x808080 }

    /// Black to white: lightness only.
    let grayscale : Colormap = { Name = "grayscale"; Stops = [| hex 0x000000; hex 0xFFFFFF |]; Missing = hex 0xFF00FF }

    /// A diverging map for values around a middle (a difference, a residual): blue, a light grey in the middle, red (the end points of Moreland's cool-warm map).
    let coolwarm : Colormap = { Name = "coolwarm"; Stops = [| hex 0x3B4CC0; hex 0xDDDDDD; hex 0xB40426 |]; Missing = hex 0x808080 }

    let private blend (a: Color) (b: Color) (f: float32) : Color =
        let ch (x: uint8) (y: uint8) = uint8 (float32 x + (float32 y - float32 x) * f + 0.5f)
        Color.rgba (ch a.R b.R) (ch a.G b.G) (ch a.B b.B) (ch a.A b.A)

    /// The colour at `t` in [0, 1].
    let at (m: Colormap) (t: float) : Color =
        if Double.IsNaN t then m.Missing
        else
            let n = m.Stops.Length
            if n = 1 then m.Stops.[0]
            else
                let x = max 0.0 (min 1.0 t) * float (n - 1)
                let i = min (n - 2) (int x)
                blend m.Stops.[i] m.Stops.[i + 1] (float32 (x - float i))

    /// The colour of `v` when `lo` is the start of the map and `hi` its end.
    let scaled (m: Colormap) (lo: float) (hi: float) (v: float) : Color =
        if hi = lo then at m 0.5 else at m ((v - lo) / (hi - lo))

    /// `n` colours at equal steps, the first at 0 and the last at 1.
    let levels (m: Colormap) (n: int) : Color[] =
        Array.init n (fun i -> at m (if n <= 1 then 0.0 else float i / float (n - 1)))

    let reversed (m: Colormap) : Colormap = { m with Name = m.Name + "_r"; Stops = Array.rev m.Stops }

    /// `n` colours for a density map or a raster: index 0 is transparent (nothing there), 1 .. n - 1 run through the map.
    let ramp (m: Colormap) (n: int) : Color[] =
        let body = levels m (max 1 (n - 1))
        Array.init n (fun i -> if i = 0 then Color.Transparent else body.[i - 1])
