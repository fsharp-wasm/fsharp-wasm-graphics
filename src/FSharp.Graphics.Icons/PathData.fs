namespace FSharp.Graphics.Icons

open System
open FSharp.Math

/// One step of a path, with absolute coordinates: what SVG path data says once relative moves, `H` and `V`, and the smooth curves `S` and `T` are resolved.
type Command =
    | MoveTo of x: float * y: float
    | LineTo of x: float * y: float
    | CubicTo of x1: float * y1: float * x2: float * y2: float * x: float * y: float
    | QuadTo of x1: float * y1: float * x: float * y: float
    /// an elliptical arc to (x, y): the radii, the turn of the ellipse in degrees, and the two flags of SVG (the longer way round, the positive direction)
    | ArcTo of rx: float * ry: float * rotation: float * large: bool * sweep: bool * x: float * y: float
    | Close

/// A run of points: one subpath of a path, flattened to straight segments.  A closed one is stroked from its last point back to its first.
type Polyline = { Points: Vec2 array; Closed: bool }

module PathData =

    // ── reading the text ────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    let private isSep (c: char) : bool = c = ' ' || c = ',' || c = '\t' || c = '\n' || c = '\r'
    let private isDigit (c: char) : bool = c >= '0' && c <= '9'

    let private skipSeparators (d: string) (i: int) : int =
        let mutable j = i
        while j < d.Length && isSep d.[j] do j <- j + 1
        j

    /// The digits from `i` on as a whole number added to `acc`, and where they end.
    let rec private digitsFrom (d: string) (i: int) (acc: float) : float * int =
        if i < d.Length && isDigit d.[i] then digitsFrom d (i + 1) (acc * 10.0 + float (int d.[i] - int '0')) else acc, i

    /// The digits after the point from `i` on, each worth a tenth of the one before, added to `acc`, and where they end.
    let rec private fractionFrom (d: string) (i: int) (acc: float) (scale: float) : float * int =
        if i < d.Length && isDigit d.[i] then fractionFrom d (i + 1) (acc + float (int d.[i] - int '0') * scale) (scale * 0.1) else acc, i

    /// A number at `i` (after separators), as SVG writes it: an optional sign, digits, a point and digits, an exponent.  `1.5.5` is 1.5 and .5; `-.5` is a number.  None when there is none.
    let private readNumber (d: string) (i: int) : (float * int) option =
        let start = skipSeparators d i
        let negative = start < d.Length && d.[start] = '-'
        let body = if start < d.Length && (d.[start] = '-' || d.[start] = '+') then start + 1 else start
        let whole, afterWhole = digitsFrom d body 0.0
        let hasPoint = afterWhole < d.Length && d.[afterWhole] = '.'
        let fraction, afterFraction = if hasPoint then fractionFrom d (afterWhole + 1) 0.0 0.1 else (0.0, afterWhole)
        let digitsSeen = afterWhole > body || (hasPoint && afterFraction > afterWhole + 1)
        if not digitsSeen then None
        else
            let value = whole + fraction
            let signed = if negative then -value else value
            // an exponent only when digits follow it
            let hasExp = afterFraction < d.Length && (d.[afterFraction] = 'e' || d.[afterFraction] = 'E')
            if not hasExp then Some (signed, afterFraction)
            else
                let expNegative = afterFraction + 1 < d.Length && d.[afterFraction + 1] = '-'
                let expStart = if afterFraction + 1 < d.Length && (d.[afterFraction + 1] = '-' || d.[afterFraction + 1] = '+') then afterFraction + 2 else afterFraction + 1
                let e, afterExp = digitsFrom d expStart 0.0
                if afterExp = expStart then Some (signed, afterFraction)
                else Some (signed * Math.Pow(10.0, (if expNegative then -e else e)), afterExp)

    /// The flag of an arc: one character, 0 or 1, which SVG lets follow the next number with no separator (`a2 2 0 011 1`).
    let private readFlag (d: string) (i: int) : (bool * int) option =
        let j = skipSeparators d i
        if j < d.Length && (d.[j] = '0' || d.[j] = '1') then Some (d.[j] = '1', j + 1) else None

    /// How many numbers (and flags) a command takes.
    let private arity (c: char) : int =
        match Char.ToLowerInvariant c with
        | 'm' | 'l' | 't' -> 2
        | 'h' | 'v' -> 1
        | 'c' -> 6
        | 's' | 'q' -> 4
        | 'a' -> 7
        | _ -> 0

    let private isCommand (c: char) : bool = "MmLlHhVvCcSsQqTtAaZz".IndexOf c >= 0

    /// The pen between steps: where it is, where the subpath began, what the last curve's control was (for `S` and `T`), and what has been read.
    type private Pen =
        { I: int
          Cmd: char
          X: float
          Y: float
          StartX: float
          StartY: float
          /// the second control of the last cubic, or the control of the last quadratic: reflected by the smooth command that follows it
          Control: (float * float) option
          Out: Command list }

    /// The arguments of the command at the pen: `arity` numbers, the two flags of an arc read as flags.  None when the text ends or is not a number where one must be.
    let private readArgs (d: string) (cmd: char) (i: int) : (float list * int) option =
        let n = arity cmd
        let isArc = Char.ToLowerInvariant cmd = 'a'
        let rec go (k: int) (i: int) (acc: float list) =
            if k = n then Some (List.rev acc, i)
            else
                let flag = isArc && (k = 3 || k = 4)
                if flag then
                    match readFlag d i with
                    | Some (b, next) -> go (k + 1) next ((if b then 1.0 else 0.0) :: acc)
                    | None -> None
                else
                    match readNumber d i with
                    | Some (v, next) -> go (k + 1) next (v :: acc)
                    | None -> None
        go 0 i []

    let private reflect (pen: Pen) : float * float =
        match pen.Control with
        | Some (cx, cy) -> 2.0 * pen.X - cx, 2.0 * pen.Y - cy
        | None -> pen.X, pen.Y

    /// One command applied: the commands it makes (in order of writing, last first) and the pen after it.
    let private apply (cmd: char) (args: float list) (pen: Pen) : Pen =
        let relative = Char.IsLower cmd
        let ox, oy = if relative then pen.X, pen.Y else 0.0, 0.0
        let lineTo x y = { pen with X = x; Y = y; Control = None; Out = LineTo (x, y) :: pen.Out }
        match Char.ToLowerInvariant cmd, args with
        | 'm', [ x; y ] -> { pen with X = ox + x; Y = oy + y; StartX = ox + x; StartY = oy + y; Control = None; Out = MoveTo (ox + x, oy + y) :: pen.Out }
        | 'l', [ x; y ] -> lineTo (ox + x) (oy + y)
        | 'h', [ x ] -> lineTo (ox + x) pen.Y
        | 'v', [ y ] -> lineTo pen.X (oy + y)
        | 'c', [ x1; y1; x2; y2; x; y ] ->
            { pen with X = ox + x; Y = oy + y; Control = Some (ox + x2, oy + y2); Out = CubicTo (ox + x1, oy + y1, ox + x2, oy + y2, ox + x, oy + y) :: pen.Out }
        | 's', [ x2; y2; x; y ] ->
            let x1, y1 = reflect pen
            { pen with X = ox + x; Y = oy + y; Control = Some (ox + x2, oy + y2); Out = CubicTo (x1, y1, ox + x2, oy + y2, ox + x, oy + y) :: pen.Out }
        | 'q', [ x1; y1; x; y ] ->
            { pen with X = ox + x; Y = oy + y; Control = Some (ox + x1, oy + y1); Out = QuadTo (ox + x1, oy + y1, ox + x, oy + y) :: pen.Out }
        | 't', [ x; y ] ->
            let x1, y1 = reflect pen
            { pen with X = ox + x; Y = oy + y; Control = Some (x1, y1); Out = QuadTo (x1, y1, ox + x, oy + y) :: pen.Out }
        | 'a', [ rx; ry; rot; large; sweep; x; y ] ->
            { pen with X = ox + x; Y = oy + y; Control = None; Out = ArcTo (rx, ry, rot, large <> 0.0, sweep <> 0.0, ox + x, oy + y) :: pen.Out }
        | _ -> pen

    /// The commands of SVG path data, with absolute coordinates.  What is not path data ends the reading there: the commands up to it are the result, as a renderer shows the part of a path
    /// that is valid.  Numbers after a command repeat it (after a move, they are lines).
    let parse (d: string) : Command list =
        let rec run (pen: Pen) : Command list =
            let i = skipSeparators d pen.I
            if i >= d.Length then List.rev pen.Out
            elif isCommand d.[i] then
                let c = d.[i]
                if arity c = 0 then run { pen with I = i + 1; Cmd = c; X = pen.StartX; Y = pen.StartY; Control = None; Out = Close :: pen.Out }
                else step { pen with I = i + 1; Cmd = c }
            elif pen.Cmd <> ' ' && arity pen.Cmd > 0 then
                // a number where a command could be: the last command again (a move repeats as a line)
                step { pen with I = i; Cmd = (match pen.Cmd with 'M' -> 'L' | 'm' -> 'l' | c -> c) }
            else List.rev pen.Out
        and step (pen: Pen) : Command list =
            match readArgs d pen.Cmd pen.I with
            | Some (args, next) -> run (apply pen.Cmd args { pen with I = next })
            | None -> List.rev pen.Out
        run { I = 0; Cmd = ' '; X = 0.0; Y = 0.0; StartX = 0.0; StartY = 0.0; Control = None; Out = [] }

    // ── flattening ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    let private clampSteps (lo: int) (hi: int) (x: float) : int = int (Math.Max(float lo, Math.Min(float hi, Math.Ceiling x)))

    let private v (x: float) (y: float) : Vec2 = Vec2.create (float32 x) (float32 y)

    /// The points of a cubic Bézier after its start: `n` of them, the last one its end.
    let private cubic (n: int) (x0: float) (y0: float) (x1: float) (y1: float) (x2: float) (y2: float) (x3: float) (y3: float) : Vec2 list =
        [ for k in 1 .. n ->
            let t = float k / float n
            let u = 1.0 - t
            let a, b, c, e = u * u * u, 3.0 * u * u * t, 3.0 * u * t * t, t * t * t
            v (a * x0 + b * x1 + c * x2 + e * x3) (a * y0 + b * y1 + c * y2 + e * y3) ]

    let private quad (n: int) (x0: float) (y0: float) (x1: float) (y1: float) (x2: float) (y2: float) : Vec2 list =
        [ for k in 1 .. n ->
            let t = float k / float n
            let u = 1.0 - t
            v (u * u * x0 + 2.0 * u * t * x1 + t * t * x2) (u * u * y0 + 2.0 * u * t * y1 + t * t * y2) ]

    let private angle (ux: float) (uy: float) (vx: float) (vy: float) : float = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy)

    /// The points of an arc after its start (SVG's implementation notes, F.6.5: from the two end points and the radii to the centre and the angles).  A radius of 0 is a line.
    let private arc (stepsPerUnit: float) (x1: float) (y1: float) (rx0: float) (ry0: float) (rotation: float) (large: bool) (sweep: bool) (x2: float) (y2: float) : Vec2 list =
        if x1 = x2 && y1 = y2 then []
        elif rx0 = 0.0 || ry0 = 0.0 then [ v x2 y2 ]
        else
            let phi = rotation * Math.PI / 180.0
            // most arcs are not turned: no trigonometry for them (and exactly 1 and 0, not what a polynomial gives)
            let cosPhi, sinPhi = if rotation = 0.0 then (1.0, 0.0) else (Math.Cos phi, Math.Sin phi)
            let dx, dy = (x1 - x2) / 2.0, (y1 - y2) / 2.0
            let x1', y1' = cosPhi * dx + sinPhi * dy, -sinPhi * dx + cosPhi * dy
            let rx0, ry0 = Math.Abs rx0, Math.Abs ry0
            // radii too small to reach: scaled up until they just do
            let lambda = (x1' * x1') / (rx0 * rx0) + (y1' * y1') / (ry0 * ry0)
            let rx, ry = (if lambda > 1.0 then rx0 * Math.Sqrt lambda else rx0), (if lambda > 1.0 then ry0 * Math.Sqrt lambda else ry0)
            let num = rx * rx * ry * ry - rx * rx * y1' * y1' - ry * ry * x1' * x1'
            let den = rx * rx * y1' * y1' + ry * ry * x1' * x1'
            // radii that had to be scaled up just reach: the centre is the middle of the two points, exactly (the root of a number that is 0 up to rounding would move it)
            let coef = if lambda >= 1.0 then 0.0 else (if large = sweep then -1.0 else 1.0) * Math.Sqrt(Math.Max(0.0, num / den))
            let cx', cy' = coef * rx * y1' / ry, -coef * ry * x1' / rx
            let cx, cy = cosPhi * cx' - sinPhi * cy' + (x1 + x2) / 2.0, sinPhi * cx' + cosPhi * cy' + (y1 + y2) / 2.0
            let ux, uy = (x1' - cx') / rx, (y1' - cy') / ry
            let wx, wy = (-x1' - cx') / rx, (-y1' - cy') / ry
            let theta = Math.Atan2(uy, ux)
            let turn0 = angle ux uy wx wy
            let turn = if not sweep && turn0 > 0.0 then turn0 - 2.0 * Math.PI elif sweep && turn0 < 0.0 then turn0 + 2.0 * Math.PI else turn0
            // as many steps as the length asks (stepsPerUnit per unit of the arc's length), and at least one per 15 degrees
            let length = Math.Abs turn * (rx + ry) / 2.0
            let n = max (clampSteps 1 256 (length * stepsPerUnit)) (clampSteps 1 256 (Math.Abs turn / (Math.PI / 12.0)))
            [ for k in 1 .. n ->
                if k = n then v x2 y2
                else
                    let a = theta + turn * float k / float n
                    let ex, ey = rx * Math.Cos a, ry * Math.Sin a
                    v (cosPhi * ex - sinPhi * ey + cx) (sinPhi * ex + cosPhi * ey + cy) ]

    /// A path as runs of straight segments.  A curve becomes as many segments as `stepsPerUnit` asks for its length (the size of a segment in the units of the path), at least four.  A subpath
    /// of a move and nothing else has no points worth drawing and is left out; a subpath of one point and a close is a dot.
    let flatten (stepsPerUnit: float) (commands: Command list) : Polyline list =
        let finish (points: Vec2 list) (closed: bool) (acc: Polyline list) : Polyline list =
            match points with
            | [] | [ _ ] when not closed -> acc
            | _ -> { Points = Array.ofList (List.rev points); Closed = closed } :: acc
        let curveSteps (length: float) : int = clampSteps 4 64 (length * stepsPerUnit)
        // the pen: the point, the points of the open subpath (last first), its first point, the finished subpaths (last first)
        let rec go (cmds: Command list) (x: float) (y: float) (sx: float) (sy: float) (points: Vec2 list) (acc: Polyline list) : Polyline list =
            match cmds with
            | [] -> List.rev (finish points false acc)
            | MoveTo (mx, my) :: rest -> go rest mx my mx my [ v mx my ] (finish points false acc)
            | LineTo (lx, ly) :: rest -> go rest lx ly sx sy (v lx ly :: points) acc
            | CubicTo (x1, y1, x2, y2, ex, ey) :: rest ->
                let length = Math.Sqrt((x1 - x) ** 2.0 + (y1 - y) ** 2.0) + Math.Sqrt((x2 - x1) ** 2.0 + (y2 - y1) ** 2.0) + Math.Sqrt((ex - x2) ** 2.0 + (ey - y2) ** 2.0)
                go rest ex ey sx sy (List.rev (cubic (curveSteps length) x y x1 y1 x2 y2 ex ey) @ points) acc
            | QuadTo (x1, y1, ex, ey) :: rest ->
                let length = Math.Sqrt((x1 - x) ** 2.0 + (y1 - y) ** 2.0) + Math.Sqrt((ex - x1) ** 2.0 + (ey - y1) ** 2.0)
                go rest ex ey sx sy (List.rev (quad (curveSteps length) x y x1 y1 ex ey) @ points) acc
            | ArcTo (rx, ry, rot, large, sweep, ex, ey) :: rest ->
                go rest ex ey sx sy (List.rev (arc stepsPerUnit x y rx ry rot large sweep ex ey) @ points) acc
            // a close ends the subpath; a drawing that goes on after it starts again where it began
            | Close :: rest -> go rest sx sy sx sy [ v sx sy ] (finish points true acc)
        go commands 0.0 0.0 0.0 0.0 [] []
