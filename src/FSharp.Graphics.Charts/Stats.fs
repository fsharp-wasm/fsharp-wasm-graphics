namespace FSharp.Graphics.Charts

open System

/// The five numbers of a box plot, the whiskers (the extreme values within 1.5 times the interquartile range of the box) and what lies beyond them.
type BoxStats =
    { Count: int
      Min: float
      Q1: float
      Median: float
      Q3: float
      Max: float
      LowWhisker: float
      HighWhisker: float
      Outliers: float[] }

module Stats =
    /// The finite values among the first `n` of `xs`, sorted.
    let sorted (xs: float32[]) (n: int) : float32[] =
        let kept = ResizeArray<float32>()
        for i in 0 .. n - 1 do
            let x = xs.[i]
            if x - x = 0.0f then kept.Add x
        let a = kept.ToArray()
        Array.sortInPlace a
        a

    /// The `q`-quantile (0..1) of sorted values by linear interpolation between the neighbouring ones (the default of R, NumPy and Excel's QUARTILE.INC).  NaN when there are none.
    let quantile (sortedValues: float32[]) (q: float) : float =
        let n = sortedValues.Length
        if n = 0 then nan
        elif n = 1 then float sortedValues.[0]
        else
            let pos = max 0.0 (min 1.0 q) * float (n - 1)
            let i = min (n - 2) (int (Math.Floor pos))
            let f = pos - float i
            float sortedValues.[i] + (float sortedValues.[i + 1] - float sortedValues.[i]) * f

    let mean (xs: float32[]) (n: int) : float =
        let mutable s = 0.0
        let mutable k = 0
        for i in 0 .. n - 1 do
            let x = xs.[i]
            if x - x = 0.0f then (s <- s + float x; k <- k + 1)
        if k = 0 then nan else s / float k

    /// The sample standard deviation (n - 1 in the divisor).
    let stddev (xs: float32[]) (n: int) : float =
        let m = mean xs n
        let mutable s = 0.0
        let mutable k = 0
        for i in 0 .. n - 1 do
            let x = xs.[i]
            if x - x = 0.0f then (s <- s + (float x - m) * (float x - m); k <- k + 1)
        if k < 2 then nan else Math.Sqrt(s / float (k - 1))

    /// The box of the finite values among the first `n` of `xs`.
    let box (xs: float32[]) (n: int) : BoxStats =
        let s = sorted xs n
        if s.Length = 0 then
            { Count = 0; Min = nan; Q1 = nan; Median = nan; Q3 = nan; Max = nan; LowWhisker = nan; HighWhisker = nan; Outliers = [||] }
        else
            let q1 = quantile s 0.25
            let q3 = quantile s 0.75
            let reach = 1.5 * (q3 - q1)
            let lowFence = q1 - reach
            let highFence = q3 + reach
            let mutable lw = float s.[s.Length - 1]
            let mutable hw = float s.[0]
            for v in s do
                let x = float v
                if x >= lowFence && x < lw then lw <- x
                if x <= highFence && x > hw then hw <- x
            { Count = s.Length
              Min = float s.[0]; Q1 = q1; Median = quantile s 0.5; Q3 = q3; Max = float s.[s.Length - 1]
              LowWhisker = lw; HighWhisker = hw
              Outliers = s |> Array.filter (fun v -> float v < lowFence || float v > highFence) |> Array.map float }

/// Counts of values in equal bins over [Lo, Hi); a value equal to Hi counts in the last bin; values outside are counted apart.  Adding is O(1), so a histogram follows a stream: the
/// bins of a column that grew cost the new values and not the column.
type Histogram(lo: float, hi: float, bins: int) =
    let counts : int[] = Array.zeroCreate (max 1 bins)
    let mutable under = 0
    let mutable over = 0
    let mutable total = 0
    let width = (hi - lo) / float (max 1 bins)

    member _.Lo = lo
    member _.Hi = hi
    member _.Bins = counts.Length
    member _.Counts : int[] = counts
    /// The values that were added and fell in a bin.
    member _.Total : int = total
    member _.Underflow : int = under
    member _.Overflow : int = over
    member _.Width : float = width
    /// The edge between bin i - 1 and bin i (0 is Lo, Bins is Hi).
    member _.Edge(i: int) : float = lo + float i * width
    member _.Max : int =
        let mutable m = 0
        for c in counts do (if c > m then m <- c)
        m

    /// The bin of a value, or -1 below Lo and Bins above Hi (NaN is -1).
    member _.BinOf(x: float) : int =
        if Double.IsNaN x || x < lo then -1
        elif x > hi then counts.Length
        elif width = 0.0 then 0
        else min (counts.Length - 1) (int (Math.Floor((x - lo) / width)))

    member this.Add(x: float) : unit =
        if not (Double.IsNaN x) && not (Double.IsInfinity x) then
            let b = this.BinOf x
            if b < 0 then under <- under + 1
            elif b >= counts.Length then over <- over + 1
            else
                counts.[b] <- counts.[b] + 1
                total <- total + 1

    /// Add `count` values of `xs` from index `first`.
    member this.AddMany(xs: float32[], first: int, count: int) : unit =
        for i in first .. first + count - 1 do this.Add(float xs.[i])

    member _.Clear() : unit =
        for i in 0 .. counts.Length - 1 do counts.[i] <- 0
        under <- 0
        over <- 0
        total <- 0

    /// A histogram over the extent of the finite values among the first `n` of `xs`, with `bins` bins (Sturges' rule when `bins` is 0), filled.
    static member OfArray(xs: float32[], n: int, bins: int) : Histogram =
        let mutable l = infinity
        let mutable h = -infinity
        let mutable k = 0
        for i in 0 .. n - 1 do
            let x = float xs.[i]
            if not (Double.IsNaN x) && not (Double.IsInfinity x) then
                k <- k + 1
                if x < l then l <- x
                if x > h then h <- x
        if k = 0 then (l <- 0.0; h <- 1.0)
        if h = l then h <- l + 1.0
        let b = if bins > 0 then bins else max 1 (int (Math.Ceiling(Math.Log(float (max 1 k)) / Math.Log 2.0)) + 1)
        let hist = Histogram(l, h, b)
        hist.AddMany(xs, 0, n)
        hist
