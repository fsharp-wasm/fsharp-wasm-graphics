namespace FSharp.Graphics.Charts

open System

/// A linear map between a domain (data units; a double, because sample numbers and timestamps do not fit a float32) and a range (pixels).
type Linear = { D0: float; D1: float; R0: float32; R1: float32 }

module Linear =
    let create (d0: float) (d1: float) (r0: float32) (r1: float32) : Linear = { D0 = d0; D1 = d1; R0 = r0; R1 = r1 }

    /// The pixel of a value.  A domain of no extent maps to the middle of the range, never to NaN.
    let map (s: Linear) (d: float) : float32 =
        let span = s.D1 - s.D0
        if span = 0.0 then (s.R0 + s.R1) * 0.5f else s.R0 + float32 ((d - s.D0) / span) * (s.R1 - s.R0)

    /// The value under a pixel.
    let invert (s: Linear) (px: float32) : float =
        let span = s.R1 - s.R0
        if span = 0.0f then s.D0 else s.D0 + float ((px - s.R0) / span) * (s.D1 - s.D0)

    /// Domain units per pixel.
    let perPixel (s: Linear) : float =
        let px = abs (float (s.R1 - s.R0))
        if px = 0.0 then 0.0 else abs (s.D1 - s.D0) / px

/// Which part of the data a chart shows, and how a wheel and a drag change it: all pure, so a test can drive them.
module Domain =
    /// The domain (d0, d1) zoomed by `k` (below 1 zooms in) about the value `anchor`, which stays where it is; the result stays inside `limits` and is not narrower than `minSpan`.
    let zoom (limits: float * float) (minSpan: float) (k: float) (anchor: float) (d0: float, d1: float) : float * float =
        let (lo, hi) = limits
        let full = hi - lo
        let span = max (min full minSpan) (min full ((d1 - d0) * k))
        let t = if d1 = d0 then 0.5 else (anchor - d0) / (d1 - d0)
        let a = anchor - t * span
        let a = max lo (min (hi - span) a)
        (a, a + span)

    /// The domain moved by `dd`, kept inside `limits` at the same span.
    let pan (limits: float * float) (dd: float) (d0: float, d1: float) : float * float =
        let (lo, hi) = limits
        let span = d1 - d0
        let a = max lo (min (hi - span) (d0 + dd))
        (a, a + span)

/// "Nice" tick positions and the text of a number.
module Ticks =
    /// The step of a sequence of ticks (1, 2 or 5 times a power of ten) that gives about `count` of them over [lo, hi].
    let step (lo: float) (hi: float) (count: int) : float =
        let raw = (hi - lo) / float (max 1 count)
        if raw <= 0.0 || Double.IsNaN raw || Double.IsInfinity raw then 1.0
        else
            let mag = Math.Pow(10.0, Math.Floor(Math.Log10 raw))
            let f = raw / mag
            let nice = if f < 1.5 then 1.0 elif f < 3.0 then 2.0 elif f < 7.0 then 5.0 else 10.0
            nice * mag

    /// The multiples of `step` inside [lo, hi].
    let values (lo: float) (hi: float) (step: float) : float[] =
        if step <= 0.0 || hi < lo then [||]
        else
            let first = Math.Ceiling(lo / step - 1.0e-9)
            let last = Math.Floor(hi / step + 1.0e-9)
            let n = int (last - first) + 1
            if n <= 0 || n > 2000 then [||] else Array.init n (fun i -> (first + float i) * step)

module Format =
    let private pow10 (n: int) : float =
        let mutable r = 1.0
        for _ in 1 .. n do r <- r * 10.0
        r

    /// `v` with exactly `decimals` digits after the point.
    let fixedPoint (decimals: int) (v: float) : string =
        if Double.IsNaN v then "NaN"
        elif Double.IsInfinity v then (if v > 0.0 then "∞" else "-∞")
        else
            let d = max 0 (min 9 decimals)
            let scale = pow10 d
            let r = Math.Floor(abs v * scale + 0.5)
            let ip = Math.Floor(r / scale)
            let fp = int64 (r - ip * scale)
            let frac = if d = 0 then "" else (let s = string fp in "." + String.replicate (d - s.Length) "0" + s)
            (if v < 0.0 && r > 0.0 then "-" else "") + string (int64 ip) + frac

    /// How many digits a step needs after the point to tell its multiples apart (0.25 needs two).
    let decimalsFor (step: float) : int =
        if step <= 0.0 || Double.IsNaN step then 0
        else
            let mutable d = 0
            let mutable s = step
            while d < 9 && abs (s - Math.Round s) > 1.0e-9 * max 1.0 s do
                s <- s * 10.0
                d <- d + 1
            d

    /// An integer with its thousands separated by a thin space: 10 000 000.
    let grouped (v: float) : string =
        let s = string (int64 (Math.Floor(abs v + 0.5)))
        let mutable out = ""
        for i in 0 .. s.Length - 1 do
            out <- out + (if i > 0 && (s.Length - i) % 3 = 0 then "\u2009" else "") + string s.[i]
        (if v < 0.0 then "-" else "") + out

    /// A tick of a sequence with this step, abbreviated by its own size: 0, 500k, 1.0M, 1.5M.
    let tick (step: float) (v: float) : string =
        let a = abs v
        let (unitOf, suffix) = if a >= 1.0e9 then (1.0e9, "G") elif a >= 1.0e6 then (1.0e6, "M") elif a >= 1.0e3 then (1.0e3, "k") else (1.0, "")
        if a = 0.0 then "0" else fixedPoint (decimalsFor (step / unitOf)) (v / unitOf) + suffix
