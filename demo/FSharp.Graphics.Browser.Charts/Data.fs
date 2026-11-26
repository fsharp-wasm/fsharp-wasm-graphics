/// The data of the demo: deterministic, so that the same numbers come out under .NET and in the browser, and cheap enough to make ten million of.
module FSharp.Graphics.Browser.ChartData

open System

/// A stream of numbers in [0, 1): a linear congruential generator (the numbers matter less than getting them fast and the same everywhere).
type Rng(seed: uint32) =
    let mutable s = seed
    member _.Next() : float32 =
        s <- s * 1664525u + 1013904223u
        float32 (s >>> 8) / 16777216.0f
    /// About a normal number (mean 0, deviation 1): four uniform numbers summed, which is near enough and has no logarithm.
    member this.Gauss() : float32 = (this.Next() + this.Next() + this.Next() + this.Next() - 2.0f) * 1.7320508f

/// A sine by recurrence: one rotation per step instead of a call of sin; double precision keeps it true over ten million steps.
type Oscillator(omega: float, amplitude: float) =
    let c = Math.Cos omega
    let s = Math.Sin omega
    let mutable x = 0.0
    let mutable y = amplitude
    member _.Next() : float32 =
        let nx = x * c - y * s
        let ny = x * s + y * c
        x <- nx
        y <- ny
        float32 x

/// `n` samples: a slow wave, a faster one, a walk that returns to zero and noise, a quiet stretch and a noisy one, and a few spikes one sample wide that no zoom may lose.
let signal (seed: uint32) (n: int) : float32[] =
    let rng = Rng(seed)
    let slow = Oscillator(6.28318530718 / float n * 3.0, 40.0)
    let fast = Oscillator(6.28318530718 / 90000.0, 9.0)
    let ys = Array.zeroCreate<float32> n
    let mutable walk = 0.0f
    for i in 0 .. n - 1 do
        walk <- walk * 0.999995f + (rng.Next() - 0.5f) * 0.6f
        let noise = if i > n * 6 / 10 && i < n * 63 / 100 then 7.0f else 1.2f
        ys.[i] <- slow.Next() + fast.Next() + walk + rng.Gauss() * noise
    // a spike every 1.73 million samples, up and down in turn
    let mutable at = 1000003
    let mutable up = true
    while at < n do
        ys.[at] <- ys.[at] + (if up then 120.0f else -120.0f)
        up <- not up
        at <- at + 1731017
    ys

/// A stream of samples for the live chart: the same recipe at a shorter wave.
type Source(seed: uint32, wave: float, amplitude: float) =
    let rng = Rng(seed)
    let osc = Oscillator(6.28318530718 / wave, amplitude)
    let mutable walk = 0.0f
    member _.Next() : float32 =
        walk <- walk * 0.9995f + (rng.Next() - 0.5f) * 0.8f
        osc.Next() + walk + rng.Gauss() * 0.9f

/// `n` points of a cloud: five clusters, a spiral of two arms and a scatter of noise.  xs and ys are filled.
let cloud (seed: uint32) (n: int) (xs: float32[]) (ys: float32[]) : unit =
    let rng = Rng(seed)
    // a table of sines, so that a spiral costs no call of sin
    let table = Array.init 4096 (fun i -> float32 (Math.Sin(6.28318530718 * float i / 4096.0)))
    let sinOf (turns: float32) = table.[(int (turns * 4096.0f)) &&& 4095]
    let cosOf (turns: float32) = table.[(int (turns * 4096.0f) + 1024) &&& 4095]
    let centers = [| (-6.0f, -3.0f, 1.1f); (-1.5f, 4.5f, 0.7f); (5.0f, 2.0f, 1.6f); (2.0f, -5.0f, 0.5f); (8.5f, -4.0f, 0.9f) |]
    for i in 0 .. n - 1 do
        let pick = rng.Next()
        if pick < 0.55f then
            let (cx, cy, sd) = centers.[int (rng.Next() * 5.0f) % 5]
            xs.[i] <- cx + rng.Gauss() * sd
            ys.[i] <- cy + rng.Gauss() * sd * 0.8f
        elif pick < 0.88f then
            let t = rng.Next()
            let arm = if rng.Next() < 0.5f then 0.0f else 0.5f
            let turns = arm + t * 1.3f
            let r = 0.4f + t * 7.0f
            xs.[i] <- r * cosOf turns + rng.Gauss() * 0.18f
            ys.[i] <- r * sinOf turns + rng.Gauss() * 0.18f
        else
            xs.[i] <- rng.Next() * 22.0f - 11.0f
            ys.[i] <- rng.Next() * 14.0f - 7.0f

/// Candles: the open is the close before, the close is a step of the volatility of the stretch, the high and the low reach beyond both.
let candles (seed: uint32) (n: int) (opens: float32[]) (closes: float32[]) (highs: float32[]) (lows: float32[]) : unit =
    let rng = Rng(seed)
    let mutable price = 100.0f
    let mutable vol = 0.6f
    for i in 0 .. n - 1 do
        if i % 400 = 0 then vol <- 0.25f + rng.Next() * 1.1f
        opens.[i] <- price
        price <- max 5.0f (price + rng.Gauss() * vol + 0.004f * (100.0f - price))
        closes.[i] <- price
        highs.[i] <- max opens.[i] price + abs (rng.Gauss()) * vol * 0.7f
        lows.[i] <- min opens.[i] price - abs (rng.Gauss()) * vol * 0.7f
