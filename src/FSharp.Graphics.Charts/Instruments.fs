namespace FSharp.Graphics.Charts

open System
open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Primitives
open FSharp.Graphics.Scene

/// How an instrument shows its value.  `Fault` is a reading that cannot be trusted (a failed or cut-off instrument): the number is not shown as one.  An app takes it from what it
/// measures: the level of an alarm (FSharp.Wasm.Telemetry of fsharp-wasm-core) and the quality of the reading make a tone.
type Tone =
    | Normal
    | Warning
    | Alarm
    | Fault

/// A dial: `Value` between `Min` and `Max`, and the zones (from, to, tone) that are painted behind it.
type GaugeSpec = { Min: float; Max: float; Value: float; Zones: (float * float * Tone) list; Label: string; Unit: string; Tone: Tone }

/// A level (a tank, a stock): `Value` between `Min` and `Max`, and the marks (a limit and its tone) that are drawn across it.
type LevelSpec = { Min: float; Max: float; Value: float; Marks: (float * Tone) list; Label: string; Unit: string; Tone: Tone }

/// The instruments of a dashboard, as scenes of the scene graph: a gauge, a level, a lamp, a sparkline and the card of a key figure.  Each is a pure function of what it shows, with
/// a size in pixels, and is named for assistive technology with its value and its state in words (a colour is never the only thing that says it).
module Instruments =

    let private amber = Color.hex 0xF59E0Bu

    /// The colour of a tone: the chart's up colour, an amber, its down colour, and a grey.
    let color (th: Theme) (t: Tone) : Color =
        match t with
        | Normal -> th.Up
        | Warning -> amber
        | Alarm -> th.Down
        | Fault -> th.Muted

    /// What a tone is called, for the screen reader and for a legend.
    let toneName (t: Tone) : string =
        match t with
        | Normal -> "normal"
        | Warning -> "warning"
        | Alarm -> "alarm"
        | Fault -> "not trusted"

    let private valid (v: float) : bool = not (Double.IsNaN v || Double.IsInfinity v)

    /// Where `v` is between `lo` and `hi`, from 0 to 1; a value that is not a number, or a range that is none, is 0.
    let fraction (lo: float) (hi: float) (v: float) : float =
        if hi <= lo || not (valid v) then 0.0 else max 0.0 (min 1.0 ((v - lo) / (hi - lo)))

    /// A value as a person reads it: three digits at most, the unit after it, and a dash for what is not a number.
    let show (unit: string) (v: float) : string =
        if not (valid v) then "—"
        else
            let n = if abs v >= 100.0 then sprintf "%.0f" v elif abs v >= 10.0 then sprintf "%.1f" v else sprintf "%.2f" v
            if unit = "" then n else n + " " + unit

    /// A segment of a ring between two radii, from the angle `a0` to `a1` (degrees, clockwise from the east, y down), as a polygon.
    let arc (cx: float32) (cy: float32) (r0: float32) (r1: float32) (a0: float32) (a1: float32) : Vec2[] =
        let steps = max 2 (int (abs (a1 - a0) / 5.0f) + 1)
        let at (r: float32) (i: int) =
            let a = (a0 + (a1 - a0) * float32 i / float32 steps) * MathF.PI / 180.0f
            Vec2.create (cx + r * MathF.Cos a) (cy + r * MathF.Sin a)
        Array.init (2 * (steps + 1)) (fun k -> if k <= steps then at r1 k else at r0 (2 * steps + 1 - k))

    let private meter (id: string) (name: string) (lo: float) (hi: float) (v: float) (shown: string) (t: Tone) (scene: Scene) : Scene =
        let words = if t = Normal then shown else shown + ", " + toneName t
        Semantic({ Semantics.create id Role.ProgressBar name with Min = float32 lo; Max = float32 hi; Now = (if valid v then float32 v else 0.0f); Value = words }, scene)

    /// A dial of 270 degrees in a square of `size`: the zones behind, the value as a filled arc in its tone and as a needle, the number under the hub, the name and the ends of the scale
    /// at the bottom.  A value that is not a number (a bad reading) draws no needle and no arc, and says a dash.
    let gauge (th: Theme) (id: string) (size: float32) (g: GaugeSpec) : Scene =
        let cx, cy = size * 0.5f, size * 0.5f
        let r1 = size * 0.46f
        let r0 = r1 - size * 0.12f
        let start, sweep = 135.0f, 270.0f
        let angle (v: float) = start + sweep * float32 (fraction g.Min g.Max v)
        let ok = valid g.Value
        let zones = g.Zones |> List.map (fun (lo, hi, t) -> Parts.polygon (arc cx cy r0 r1 (angle lo) (angle hi)) (Color.withAlpha 0.4f (color th t)))
        let filled = if ok && g.Value > g.Min then Parts.polygon (arc cx cy (r0 + 3.0f) (r1 - 3.0f) start (angle g.Value)) (color th g.Tone) else Empty
        let needle =
            if not ok then Empty
            else
                let a = angle g.Value * MathF.PI / 180.0f
                let tip = Vec2.create (cx + (r0 - 4.0f) * MathF.Cos a) (cy + (r0 - 4.0f) * MathF.Sin a)
                Group [ Parts.polygon (Geometry.ribbon [| Vec2.create cx cy; tip |] 3.0f) th.Text; Parts.dot cx cy (size * 0.035f) th.Text ]
        let px = size * 0.13f
        let label = Parts.text th (size * 0.085f) th.Muted TextAlign.Center (cx - size * 0.22f) (size * 0.84f) (size * 0.44f) g.Label
        let ends =
            [ Parts.text th (size * 0.075f) th.Muted TextAlign.Left (size * 0.06f) (size * 0.84f) (size * 0.22f) (show "" g.Min)
              Parts.text th (size * 0.075f) th.Muted TextAlign.Right (size * 0.72f) (size * 0.84f) (size * 0.22f) (show "" g.Max) ]
        let value = Parts.text th px (if ok then th.Text else th.Muted) TextAlign.Center (cx - size * 0.32f) (cy + size * 0.12f) (size * 0.64f) (show g.Unit g.Value)
        meter id g.Label g.Min g.Max g.Value (show g.Unit g.Value) (if ok then g.Tone else Fault)
            (Group [ Parts.polygon (arc cx cy r0 r1 start (start + sweep)) th.Axis; Group zones; filled; needle; value; label; Group ends ])

    /// A vertical level of `w` by `h`: the fill from the bottom in its tone, a line across it at each mark, the number above and the name under it.
    let level (th: Theme) (id: string) (w: float32) (h: float32) (l: LevelSpec) : Scene =
        let ok = valid l.Value
        let top, bottom = 22.0f, h - 22.0f
        let y (v: float) = bottom - (bottom - top) * float32 (fraction l.Min l.Max v)
        let fill = if ok then Parts.box 1.0f (y l.Value) (w - 2.0f) (bottom - y l.Value) (Color.withAlpha 0.85f (color th l.Tone)) else Empty
        let marks = l.Marks |> List.map (fun (v, t) -> Parts.box -3.0f (y v - 1.0f) (w + 6.0f) 2.0f (color th t))
        let frame = Parts.box 0.0f top w (bottom - top) th.Axis
        let inner = Parts.box 1.0f (top + 1.0f) (w - 2.0f) (bottom - top - 2.0f) th.Plot
        meter id l.Label l.Min l.Max l.Value (show l.Unit l.Value) (if ok then l.Tone else Fault)
            (Group [ frame; inner; fill; Group marks
                     Parts.text th 12.0f (if ok then th.Text else th.Muted) TextAlign.Center -10.0f 2.0f (w + 20.0f) (show l.Unit l.Value)
                     Parts.text th 11.0f th.Muted TextAlign.Center -10.0f (h - 18.0f) (w + 20.0f) l.Label ])

    /// A lamp of `radius`: lit in its tone when it is on, dark when it is off, with a ring of light round it, and its name under it.  The state is said in words as well.
    let lamp (th: Theme) (id: string) (radius: float32) (t: Tone) (on: bool) (name: string) : Scene =
        let c = color th t
        let body =
            Group [ (if on then Parts.dot radius radius (radius * 1.0f) (Color.withAlpha 0.30f c) else Empty)
                    Parts.dot radius radius (radius * 0.72f) (if on then c else th.Axis)
                    Parts.text th 11.0f th.Muted TextAlign.Center (-radius) (radius * 2.0f + 2.0f) (radius * 4.0f) name ]
        Semantic({ Semantics.create id Role.Image name with Description = (if on then "on, " + toneName t else "off") }, body)

    /// The last values of a series as a line in a box of `w` by `h`, with a dot on the last one: no axes, no scale, the shape of what happened.  Fewer than two values: nothing.
    let sparkline (w: float32) (h: float32) (c: Color) (values: float32[]) : Scene =
        let n = values.Length
        if n < 2 then Empty
        else
            let lo, hi = Array.min values, Array.max values
            let range = if hi - lo < 1.0e-9f then 1.0f else hi - lo
            let pt i = Vec2.create (4.0f + (w - 8.0f) * float32 i / float32 (n - 1)) (h - 4.0f - (h - 8.0f) * (values.[i] - lo) / range)
            let pts = Array.init n pt
            Group [ Parts.polygon (Geometry.ribbon pts 1.6f) c; Parts.dot pts.[n - 1].X pts.[n - 1].Y 3.0f c ]

    /// The card of a key figure, `w` by `h`: its name, the big number, the change (in the colour of its tone, and in words) and the shape of the last values.
    let kpi (th: Theme) (id: string) (w: float32) (h: float32) (name: string) (value: string) (change: string) (t: Tone) (history: float32[]) : Scene =
        let c = color th t
        let body =
            Group [ Parts.box 0.0f 0.0f w h th.Plot
                    Parts.box 0.0f 0.0f 4.0f h c
                    Parts.text th 12.0f th.Muted TextAlign.Left 16.0f 10.0f (w - 24.0f) name
                    Parts.text th (min 34.0f (h * 0.34f)) th.Text TextAlign.Left 16.0f (h * 0.30f) (w * 0.6f) value
                    Parts.text th 12.0f c TextAlign.Left 16.0f (h - 26.0f) (w * 0.6f) change
                    Scene.Transform(Mat3x2.translation (w * 0.55f) (h * 0.30f), sparkline (w * 0.40f) (h * 0.46f) c history) ]
        Semantic({ Semantics.create id Role.Image (name + ": " + value) with Description = (if change = "" then toneName t else change + ", " + toneName t) }, body)
