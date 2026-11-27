namespace FSharp.Graphics.Charts

open System

/// A logarithmic scale: the data between D0 and D1 (both above zero) on the pixels between R0 and R1.  A value that is not above zero has no place on it: `map` gives NaN.
type Log = { D0: float; D1: float; R0: float32; R1: float32 }

module Log =
    let create (d0: float) (d1: float) (r0: float32) (r1: float32) : Log = { D0 = d0; D1 = d1; R0 = r0; R1 = r1 }

    let map (s: Log) (d: float) : float32 =
        if not (d > 0.0) || not (s.D0 > 0.0) || not (s.D1 > 0.0) then float32 nan
        else
            let a = Math.Log10 s.D0
            let b = Math.Log10 s.D1
            let t = if b = a then 0.5 else (Math.Log10 d - a) / (b - a)
            s.R0 + float32 t * (s.R1 - s.R0)

    let invert (s: Log) (px: float32) : float =
        let a = Math.Log10 s.D0
        let b = Math.Log10 s.D1
        let t = if s.R1 = s.R0 then 0.5 else float (px - s.R0) / float (s.R1 - s.R0)
        Math.Pow(10.0, a + t * (b - a))

    /// The ticks of [lo, hi] (above zero): every power of ten is a major tick; when the range covers fewer than three decades the 2 and 5 times a power are minor ones.
    let ticks (lo: float) (hi: float) : (float * bool)[] =
        if not (lo > 0.0) || not (hi > lo) then [||]
        else
            let first = int (Math.Floor(Math.Log10 lo))
            let last = int (Math.Ceiling(Math.Log10 hi))
            let minor = Math.Log10 hi - Math.Log10 lo < 3.0
            let out = ResizeArray<float * bool>()
            for e in first .. last do
                let p = Math.Pow(10.0, float e)
                for k in [ 1.0; 2.0; 5.0 ] do
                    let v = p * k
                    if v >= lo * (1.0 - 1.0e-9) && v <= hi * (1.0 + 1.0e-9) && (k = 1.0 || minor) then out.Add((v, (k = 1.0)))
            out.ToArray()

    /// A tick as text: 0.001, 0.1, 1, 10, 100, 1k, 10k, 1M, and 1e-5 for the very small.
    let label (v: float) : string =
        if not (v > 0.0) then ""
        elif v >= 1.0 then Format.tick v v                      // the step is the value itself: no decimals that the value does not have
        elif v >= 1.0e-4 then Format.fixedPoint (int (Math.Ceiling(-Math.Log10 v - 1.0e-9))) v
        else "1e" + string (int (Math.Round(Math.Log10 v)))

/// A time axis: values are seconds since 1970-01-01 00:00:00 UTC (as a float, so a second of a century is exact to the microsecond).
module Time =
    /// The steps ticks may have, in seconds: 1 s to 1 year; a step of a day or more is a multiple of that many days since the epoch (months and years are 30 and 365 days: not calendar months).
    let steps : float[] =
        [| 1.0; 2.0; 5.0; 10.0; 15.0; 30.0; 60.0; 120.0; 300.0; 600.0; 900.0; 1800.0; 3600.0; 7200.0; 10800.0; 21600.0; 43200.0
           86400.0; 172800.0; 604800.0; 2592000.0; 7776000.0; 15552000.0; 31536000.0 |]

    /// The smallest step that gives no more than `count` ticks over [lo, hi].
    let step (lo: float) (hi: float) (count: int) : float =
        let raw = (hi - lo) / float (max 1 count)
        let mutable i = 0
        while i < steps.Length - 1 && steps.[i] < raw do i <- i + 1
        steps.[i]

    /// The multiples of `step` inside [lo, hi].
    let values (lo: float) (hi: float) (step: float) : float[] =
        if not (step > 0.0) || not (hi >= lo) then [||]
        else
            let first = Math.Ceiling(lo / step - 1.0e-9)
            let last = Math.Floor(hi / step + 1.0e-9)
            Array.init (max 0 (int (last - first) + 1)) (fun i -> (first + float i) * step)

    /// The civil date of a day count since 1970-01-01 (Howard Hinnant's algorithm, proleptic Gregorian): year, month 1..12, day 1..31.
    let civil (days: int64) : int * int * int =
        let z = days + 719468L
        let era = (if z >= 0L then z else z - 146096L) / 146097L
        let doe = z - era * 146097L
        let yoe = (doe - doe / 1460L + doe / 36524L - doe / 146096L) / 365L
        let y = yoe + era * 400L
        let doy = doe - (365L * yoe + yoe / 4L - yoe / 100L)
        let mp = (5L * doy + 2L) / 153L
        let d = doy - (153L * mp + 2L) / 5L + 1L
        let m = if mp < 10L then mp + 3L else mp - 9L
        ((if m <= 2L then y + 1L else y) |> int, int m, int d)

    let private two (n: int) : string = if n < 10 then "0" + string n else string n

    /// `YYYY-MM-DD HH:MM:SS` (UTC).
    let format (t: float) : string =
        let s = int64 (Math.Floor t)
        let days = (if s >= 0L then s / 86400L else (s - 86399L) / 86400L)
        let sod = int (s - days * 86400L)
        let (y, m, d) = civil days
        string y + "-" + two m + "-" + two d + " " + two (sod / 3600) + ":" + two ((sod / 60) % 60) + ":" + two (sod % 60)

    /// A tick of this step as text: seconds and minutes as the time of day, a day or more as the date.
    let label (step: float) (t: float) : string =
        let full = format t
        if step < 60.0 then full.Substring(11, 8)
        elif step < 86400.0 then full.Substring(11, 5)
        elif step < 2592000.0 then full.Substring(5, 5)
        else full.Substring(0, 10)
