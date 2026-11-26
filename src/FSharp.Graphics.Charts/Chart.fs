namespace FSharp.Graphics.Charts

open System
open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Primitives
open FSharp.Graphics.Scene

/// The colours and the font of a chart.
type Theme =
    { Font: FontId
      Background: Color
      Plot: Color
      Grid: Color
      Axis: Color
      Text: Color
      Muted: Color
      /// The colours of the series, in order.
      Palette: Color[]
      Up: Color
      Down: Color
      Tip: Color
      TextPx: float32 }

module Theme =
    let private hex (h: int) = Color.hex (uint32 h)
    /// Slate with the colours of the scene gallery.
    let dark (font: FontId) : Theme =
        { Font = font; Background = hex 0x0F172A; Plot = hex 0x111C33; Grid = Color.withAlpha 0.16f (hex 0x94A3B8); Axis = hex 0x475569
          Text = hex 0xE2E8F0; Muted = hex 0x94A3B8
          Palette = [| hex 0x3B82F6; hex 0x22C55E; hex 0xF59E0B; hex 0xA78BFA; hex 0xEC4899; hex 0x06B6D4 |]
          Up = hex 0x22C55E; Down = hex 0xEF4444; Tip = hex 0x1E293B; TextPx = 12.0f }

/// The drawing of one chart: the plot rectangle inside the margins, and the scales that map data to it.
type Frame = { Size: Vec2; Plot: Rect; X: Linear; Y: Linear }

module Frame =
    /// A plot inside margins (left, top, right, bottom) of a chart of this size, showing [x0, x1] x [y0, y1]: y grows upwards.  The plot sits on whole pixels, so grid lines and the cells of
    /// a density map are sharp.
    let create (w: float32) (h: float32) (left: float32) (top: float32) (right: float32) (bottom: float32) (x0: float, x1: float) (y0: float, y1: float) : Frame =
        let plot = Rect.create (MathF.Round left) (MathF.Round top) (MathF.Round (max 1.0f (w - left - right))) (MathF.Round (max 1.0f (h - top - bottom)))
        { Size = Vec2.create w h
          Plot = plot
          X = Linear.create x0 x1 plot.X (plot.X + plot.W)
          Y = Linear.create y0 y1 (plot.Y + plot.H) plot.Y }

    let contains (f: Frame) (x: float32) (y: float32) : bool =
        x >= f.Plot.X && x <= f.Plot.X + f.Plot.W && y >= f.Plot.Y && y <= f.Plot.Y + f.Plot.H

/// The pieces every chart is made of; they build scenes and know nothing about the data.
module Parts =
    let box (x: float32) (y: float32) (w: float32) (h: float32) (c: Color) : Scene = Shape(Shapes.rectSimple x y w h (Solid c))
    let polygon (pts: Vec2[]) (c: Color) : Scene = if pts.Length < 3 then Empty else Shape(Path(PolygonSdf pts, Solid c, None, None))
    let dot (x: float32) (y: float32) (r: float32) (c: Color) : Scene = Shape(Shapes.circle (Vec2.create x y) r (Solid c))

    /// One line of text in a box `w` wide, aligned inside it (the box is a clip region, which is what wraps and aligns the text).  Give it room: text that does not fit wraps.
    let text (th: Theme) (size: float32) (color: Color) (align: TextAlign) (x: float32) (y: float32) (w: float32) (s: string) : Scene =
        let style = { TextStyle.defaultStyle th.Font with SizePx = size; Color = color; Align = align; Wrap = true }
        Scene.Transform(Mat3x2.translation x y, Scene.Clip(Rect.create 0.0f 0.0f w (size * 1.6f), Scene.Text(s, style)))

    /// The plot's background, its grid, its tick labels and its two axes.
    let axes (th: Theme) (f: Frame) (xLabel: float -> float -> string) (yLabel: float -> float -> string) : Scene =
        let p = f.Plot
        let xStep = Ticks.step f.X.D0 f.X.D1 (max 2 (int (p.W / 96.0f)))
        let yStep = Ticks.step f.Y.D0 f.Y.D1 (max 2 (int (p.H / 44.0f)))
        let xs = Ticks.values f.X.D0 f.X.D1 xStep
        let ys = Ticks.values f.Y.D0 f.Y.D1 yStep
        let vertical = [ for v in xs -> let x = MathF.Round(Linear.map f.X v) in box x p.Y 1.0f p.H th.Grid ]
        let horizontal = [ for v in ys -> let y = MathF.Round(Linear.map f.Y v) in box p.X y p.W 1.0f th.Grid ]
        let xText = [ for v in xs -> text th th.TextPx th.Muted TextAlign.Center (Linear.map f.X v - 40.0f) (p.Y + p.H + 6.0f) 80.0f (xLabel xStep v) ]
        let yText = [ for v in ys -> text th th.TextPx th.Muted TextAlign.Right (p.X - 76.0f) (Linear.map f.Y v - th.TextPx * 0.8f) 68.0f (yLabel yStep v) ]
        Group [ box p.X p.Y p.W p.H th.Plot
                Group vertical
                Group horizontal
                box p.X (p.Y + p.H) p.W 1.0f th.Axis
                box p.X p.Y 1.0f p.H th.Axis
                Group xText
                Group yText ]

    let private itemWidth (th: Theme) (name: string) : float32 = 22.0f + float32 name.Length * th.TextPx * 0.58f

    /// The width of a legend, to place it against the right edge.
    let legendWidth (th: Theme) (items: (string * Color) list) : float32 =
        items |> List.sumBy (fun (name, _) -> itemWidth th name + 14.0f)

    /// Swatches and names, from `x`, `y` to the right.
    let legend (th: Theme) (x: float32) (y: float32) (items: (string * Color) list) : Scene =
        let mutable at = x
        Group [ for (name, c) in items do
                    let w = itemWidth th name
                    let here = at
                    at <- at + w + 14.0f
                    yield Group [ box here (y + 4.0f) 10.0f 10.0f c
                                  text th th.TextPx th.Text TextAlign.Left (here + 16.0f) y (w + 40.0f) name ] ]

    /// The crosshair: the two lines through the pointer, a ring on each `mark` (x, y, colour) and a card with `lines` (text, colour) beside it, kept inside the plot.
    let crosshair (th: Theme) (f: Frame) (x: float32) (y: float32) (marks: (float32 * float32 * Color) list) (lines: (string * Color) list) : Scene =
        let p = f.Plot
        let chars = lines |> List.fold (fun m (s, _) -> max m s.Length) 0
        let w = float32 chars * th.TextPx * 0.6f + 22.0f
        let lh = th.TextPx * 1.5f
        let h = float32 lines.Length * lh + 12.0f
        let cx = if x + 16.0f + w > p.X + p.W then x - 16.0f - w else x + 16.0f
        let cy = max p.Y (min (p.Y + p.H - h) (y - h * 0.5f))
        let line = Color.withAlpha 0.55f th.Muted
        Group [ box (MathF.Round x) p.Y 1.0f p.H line
                box p.X (MathF.Round y) p.W 1.0f line
                Group [ for (mx, my, c) in marks do
                            yield dot mx my 5.0f th.Background
                            yield dot mx my 3.5f c ]
                (if lines.IsEmpty then Empty
                 else
                    Group [ Shape(Shapes.roundedRect (Rect.create cx cy w h) (CornerRadius.all 6.0f) (Solid th.Tip))
                            Group [ for (i, (s, c)) in List.indexed lines ->
                                        text th th.TextPx c TextAlign.Left (cx + 10.0f) (cy + 6.0f + float32 i * lh) (w - 10.0f) s ] ]) ]

    /// The chart as one picture for assistive technology, with a name and a description of what it shows.
    let describe (id: string) (name: string) (description: string) (scene: Scene) : Scene =
        Semantic({ Semantics.create id Role.Image name with Description = description }, scene)

/// A line or area of samples at regular spacing: sample i is at x = X0 + i * Dx.
type LineSeries = { Name: string; Color: Color; Width: float32; Fill: bool; Data: Pyramid; X0: float; Dx: float }

module Line =
    /// The number of samples per pixel column above which a line is drawn as a band (below, as a ribbon through the samples).
    [<Literal>]
    let DenseAt = 1.5

    /// The sample at or before x, and its value; None outside the data.
    let nearest (s: LineSeries) (x: float) : (int * float32) option =
        let i = int (Math.Round((x - s.X0) / s.Dx))
        if i < 0 || i >= s.Data.Count then None else Some (i, s.Data.[i])

    /// The line inside the plot.  Many samples per pixel: the band between the smallest and largest of each column (what a line through all of them would cover), found by the pyramid in
    /// O(width log n).  Few: the ribbon through the samples, with a dot on each when they are far apart.
    let scene (f: Frame) (s: LineSeries) : Scene =
        let p = f.Plot
        let iL = (f.X.D0 - s.X0) / s.Dx
        let iR = (f.X.D1 - s.X0) / s.Dx
        let cols = max 1 (int p.W)
        let step = (iR - iL) / float cols
        let colW = p.W / float32 cols
        let half = s.Width * 0.5f
        let baseline = p.Y + p.H
        let fill (pts: Vec2[]) (alpha: float32) = Parts.polygon (Geometry.area pts baseline) (Color.withAlpha alpha s.Color)
        let scene =
            if step >= DenseAt then
                let lo = Array.zeroCreate<float32> cols
                let hi = Array.zeroCreate<float32> cols
                s.Data.Columns(iL, step, cols, lo, hi)
                let top = Array.zeroCreate<float32> cols
                let bottom = Array.zeroCreate<float32> cols
                let mutable first = -1
                let mutable last = -1
                for c in 0 .. cols - 1 do
                    if hi.[c] >= lo.[c] then
                        if first < 0 then first <- c
                        last <- c
                        top.[c] <- Linear.map f.Y (float hi.[c]) - half
                        bottom.[c] <- Linear.map f.Y (float lo.[c]) + half
                if first < 0 then Empty
                else
                    let edge = Array.init (last - first + 1) (fun i -> Vec2.create (p.X + (float32 (first + i) + 0.5f) * colW) top.[first + i])
                    Group [ (if s.Fill then fill edge 0.18f else Empty)
                            Parts.polygon (Geometry.band p.X colW first last top bottom) s.Color ]
            else
                let i0 = max 0 (int (Math.Floor iL) - 1)
                let i1 = min (s.Data.Count - 1) (int (Math.Ceiling iR) + 1)
                if i1 <= i0 then Empty
                else
                    let pts = Array.init (i1 - i0 + 1) (fun k -> let i = i0 + k in Vec2.create (Linear.map f.X (s.X0 + float i * s.Dx)) (Linear.map f.Y (float s.Data.[i])))
                    let dots = if float32 pts.Length * 8.0f < p.W && pts.Length <= 400 then [ for q in pts -> Parts.dot q.X q.Y (half + 1.5f) s.Color ] else []
                    Group [ (if s.Fill then fill pts 0.18f else Empty)
                            Parts.polygon (Geometry.ribbon pts s.Width) s.Color
                            Group dots ]
        Scene.Clip(p, scene)

    /// The y range that holds the samples of the x range, with a margin of `pad` of it on both sides.
    let yRange (series: LineSeries list) (x0: float, x1: float) (pad: float) : float * float =
        let span =
            series |> List.fold (fun (acc: Span) s ->
                let l = int (Math.Floor((x0 - s.X0) / s.Dx))
                let r = int (Math.Ceiling((x1 - s.X0) / s.Dx)) + 1
                Span.join acc (s.Data.Range(l, r))) Span.empty
        if Span.isEmpty span then (0.0, 1.0)
        else
            let lo = float span.Lo
            let hi = float span.Hi
            let m = if hi > lo then (hi - lo) * pad else max 1.0 (abs lo * pad)
            (lo - m, hi + m)

/// Open, high, low and close at regular spacing.  High and low are pyramids, so a candle that stands for a thousand of them is a query.
type CandleSeries = { Open: float32[]; Close: float32[]; High: Pyramid; Low: Pyramid; X0: float; Dx: float }

module Candles =
    let scene (th: Theme) (f: Frame) (s: CandleSeries) : Scene =
        let p = f.Plot
        let n = s.High.Count
        let iL = (f.X.D0 - s.X0) / s.Dx
        let iR = (f.X.D1 - s.X0) / s.Dx
        let cols = max 1 (int p.W)
        let perCol = (iR - iL) / float cols
        let y (v: float32) = Linear.map f.Y (float v)
        let candle (x: float32) (bodyW: float32) (o: float32) (c: float32) (hi: float32) (lo: float32) : Scene =
            let color = if c >= o then th.Up else th.Down
            let yo = y o
            let yc = y c
            let top = min yo yc
            let yh = y hi
            let yl = y lo
            Group [ Parts.box (MathF.Round x) yh 1.0f (max 1.0f (yl - yh)) color
                    Parts.box (MathF.Round(x - bodyW * 0.5f)) top (max 1.0f (MathF.Round bodyW)) (max 1.0f (abs (yc - yo))) color ]
        let shapes =
            if perCol <= 1.0 then
                let i0 = max 0 (int (Math.Floor iL))
                let i1 = min (n - 1) (int (Math.Ceiling iR))
                let slot = p.W / float32 (iR - iL)
                [ for i in i0 .. i1 ->
                    candle (Linear.map f.X (s.X0 + (float i + 0.5) * s.Dx)) (slot * 0.7f) s.Open.[i] s.Close.[i] s.High.[i] s.Low.[i] ]
            else
                [ for c in 0 .. cols - 1 do
                    let a = int (Math.Floor(iL + float c * perCol))
                    let b = max (a + 1) (int (Math.Floor(iL + float (c + 1) * perCol)))
                    if a >= 0 && a < n then
                        let b = min n b
                        yield candle (p.X + float32 c + 0.5f) 1.0f s.Open.[a] s.Close.[b - 1] (s.High.Range(a, b).Hi) (s.Low.Range(a, b).Lo) ]
        Scene.Clip(p, Group shapes)

/// Bars over named categories.
module Bars =
    /// One bar per item from zero up (or down) to its value; the names are centred under them.  `Frame.X` is not used: the categories share the plot's width.
    let scene (th: Theme) (f: Frame) (items: (string * float32) array) (colors: Color[]) : Scene =
        let p = f.Plot
        let n = max 1 items.Length
        let slot = p.W / float32 n
        let zero = Linear.map f.Y 0.0
        Group [ for i in 0 .. items.Length - 1 do
                    let (name, v) = items.[i]
                    let x = p.X + slot * float32 i + slot * 0.15f
                    let yv = Linear.map f.Y (float v)
                    let top = min yv zero
                    yield Scene.Clip(p, Shape(Shapes.roundedRect (Rect.create x top (slot * 0.7f) (max 1.0f (abs (yv - zero)))) (CornerRadius.create 4.0f 4.0f 0.0f 0.0f) (Solid colors.[i % colors.Length])))
                    yield Parts.text th th.TextPx th.Muted TextAlign.Center (p.X + slot * float32 i) (p.Y + p.H + 6.0f) slot name ]

/// A density map of a grid of points: the cells of the plot, each as dark or as light as the points in it.
module Heatmap =
    /// `levels` colours from nothing (transparent) through the ramp's stops.
    let ramp (levels: int) : Color[] =
        let stops = [| 0x0E1B3D; 0x2A1F70; 0x6A2390; 0xB8307C; 0xEE6350; 0xFDB84A; 0xFCF4A8 |] |> Array.map (fun h -> Color.hex (uint32 h))
        Array.init levels (fun i ->
            if i = 0 then Color.Transparent
            else
                let t = float32 (i - 1) / float32 (max 1 (levels - 2)) * float32 (stops.Length - 1)
                let k = min (stops.Length - 2) (int t)
                let u = t - float32 k
                let a = stops.[k]
                let b = stops.[k + 1]
                let mix (x: uint8) (y: uint8) = uint8 (float32 x + (float32 y - float32 x) * u + 0.5f)
                Color.rgb (mix a.R b.R) (mix a.G b.G) (mix a.B b.B))

    /// The map of `grid` as the plot shows it, in cells of `cell` pixels (a whole number, so that neighbours meet).  Returns the shapes, one per run of equal colour in a row.
    let scene (f: Frame) (grid: DensityGrid) (cell: int) (colors: Color[]) : Scene list =
        let p = f.Plot
        let cols = max 1 (int p.W / cell)
        let rows = max 1 (int p.H / cell)
        let dens = Array.zeroCreate<float32> (cols * rows)
        let best = grid.Window(f.X.D0, f.X.D1, f.Y.D0, f.Y.D1, cols, rows, dens)
        let lv = Array.zeroCreate<int> (cols * rows)
        Heat.levels colors.Length best dens (cols * rows) lv
        let c = float32 cell
        let out = ResizeArray<Scene>()
        Heat.runs lv cols rows (fun r col len level -> out.Add(Parts.box (p.X + float32 col * c) (p.Y + float32 r * c) (float32 len * c) c colors.[level]))
        List.ofSeq out
