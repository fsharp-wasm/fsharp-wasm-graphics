namespace FSharp.Graphics.Charts

/// The smallest and largest of some samples.  An empty range has Lo = +infinity and Hi = -infinity, so that joining it with anything leaves that unchanged.
type Span = { Lo: float32; Hi: float32 }

module Span =
    let empty : Span = { Lo = infinityf; Hi = -infinityf }
    let isEmpty (s: Span) : bool = s.Lo > s.Hi
    let join (a: Span) (b: Span) : Span = { Lo = (if b.Lo < a.Lo then b.Lo else a.Lo); Hi = (if b.Hi > a.Hi then b.Hi else a.Hi) }

/// Samples with the minimum and maximum of every aligned block of 2, 4, 8 ... of them.  The range of any run of samples is then the join of at most 2 log n blocks, so drawing a line
/// of ten million samples into 1000 pixels costs 1000 queries and not ten million reads (the data is read once, to build the pyramid; an appended sample costs O(1) on average).
///
/// Level k holds the blocks of 2^k samples: block j covers [j * 2^k, (j + 1) * 2^k).  Only complete blocks exist, and a query never asks for another one.  Level 0 is the samples, which
/// are their own minimum and maximum.  The memory is the samples plus two further arrays of their size (the blocks of all levels add up to one array each).
type Pyramid(capacity: int) =
    let cap = max 1 capacity
    let levels =
        let mutable n = 1
        while (cap >>> n) > 0 do n <- n + 1
        n
    let ys : float32[] = Array.zeroCreate cap
    let los : float32[][] = Array.init levels (fun k -> if k = 0 then ys else Array.zeroCreate (cap >>> k))
    let his : float32[][] = Array.init levels (fun k -> if k = 0 then ys else Array.zeroCreate (cap >>> k))
    let mutable count = 0

    /// How many samples there are.
    member _.Count = count
    member _.Capacity = cap
    /// The sample at `i` (unchecked).
    member _.Item(i: int) : float32 = ys.[i]
    member _.Samples : float32[] = ys

    /// Forget the samples (a recording that starts again).  The blocks need no clearing: they are written again before a query can reach them.
    member _.Clear() : unit = count <- 0

    /// Add a sample at the end (`Capacity` is a limit: the pyramid does not grow).
    member _.Append(v: float32) : unit =
        let i = count
        ys.[i] <- v
        count <- i + 1
        // a block is complete when its right half is: join the halves, and go on while the block is itself a right half
        let mutable k = 0
        let mutable j = i
        while (j &&& 1) = 1 && k + 1 < levels do
            let p = j >>> 1
            let a = los.[k].[j - 1]
            let b = los.[k].[j]
            los.[k + 1].[p] <- (if b < a then b else a)
            let c = his.[k].[j - 1]
            let d = his.[k].[j]
            his.[k + 1].[p] <- (if d > c then d else c)
            j <- p
            k <- k + 1

    /// Add the first `n` values of `src` at the end: the blocks are built level by level, which reads the data once in order.
    member this.AppendMany(src: float32[], n: int) : unit =
        if count = 0 then
            let m = min n cap
            System.Array.Copy(src, ys, m)
            count <- m
            for k in 0 .. levels - 2 do
                let lo0 = los.[k]
                let hi0 = his.[k]
                let lo1 = los.[k + 1]
                let hi1 = his.[k + 1]
                for p in 0 .. (m >>> (k + 1)) - 1 do
                    let a = lo0.[2 * p]
                    let b = lo0.[2 * p + 1]
                    lo1.[p] <- (if b < a then b else a)
                    let c = hi0.[2 * p]
                    let d = hi0.[2 * p + 1]
                    hi1.[p] <- (if d > c then d else c)
        else
            for i in 0 .. n - 1 do this.Append src.[i]

    /// The smallest and largest sample of [l, r) (clamped to the samples there are).
    member _.Range(l0: int, r0: int) : Span =
        let mutable l = max 0 l0
        let mutable r = min count r0
        let mutable lo = infinityf
        let mutable hi = -infinityf
        let mutable k = 0
        while l < r do
            if (l &&& 1) = 1 then
                let a = los.[k].[l]
                let b = his.[k].[l]
                if a < lo then lo <- a
                if b > hi then hi <- b
                l <- l + 1
            if (r &&& 1) = 1 then
                r <- r - 1
                let a = los.[k].[r]
                let b = his.[k].[r]
                if a < lo then lo <- a
                if b > hi then hi <- b
            l <- l >>> 1
            r <- r >>> 1
            k <- k + 1
        { Lo = lo; Hi = hi }

    /// The columns of a plot: column c holds the samples from the one at `start + c * step` up to the one at `start + (c + 1) * step`, and also the one before (the line comes into the
    /// column from there, so the columns touch).  Columns without a sample get an empty span.  The spans are written into `lo` and `hi`, so a frame allocates nothing here.
    member this.Columns(start: float, step: float, cols: int, lo: float32[], hi: float32[]) : unit =
        let n = float count
        let mutable s = System.Math.Floor start
        for c in 0 .. cols - 1 do
            let e = System.Math.Floor(start + float (c + 1) * step)
            if e <= 0.0 || s >= n then
                lo.[c] <- infinityf
                hi.[c] <- -infinityf
            else
                let a = int (max 0.0 s)
                // at least one sample: a zoom beyond one sample per column repeats the sample
                let b = max (int (min n e)) (a + 1)
                let r = this.Range((if a > 0 then a - 1 else 0), b)
                lo.[c] <- r.Lo
                hi.[c] <- r.Hi
            s <- e

    /// The same samples as a pyramid, built from an array in one pass.
    static member OfArray(src: float32[]) : Pyramid =
        let p = Pyramid(src.Length)
        p.AppendMany(src, src.Length)
        p
