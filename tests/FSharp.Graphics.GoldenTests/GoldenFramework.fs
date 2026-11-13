module FSharp.Graphics.GoldenTests.GoldenFramework

open System.IO
open FSharp.Math

/// Result of comparing actual vs golden
type CompareResult =
    | Pass
    | Fail of maxDelta: int * failBytes: int * totalBytes: int
    | MissingGolden

/// Compare two RGBA byte arrays pixel-by-pixel within tolerance
let compare (tolerance: int) (actual: byte array) (golden: byte array) : CompareResult =
    if actual.Length <> golden.Length then
        Fail(255, actual.Length, actual.Length)
    else
        let mutable maxDelta = 0
        let mutable failCount = 0
        for i = 0 to actual.Length - 1 do
            let delta = abs (int actual.[i] - int golden.[i])
            if delta > maxDelta then maxDelta <- delta
            if delta > tolerance then failCount <- failCount + 1
        if failCount = 0 then Pass
        else Fail(maxDelta, failCount, actual.Length)

let saveRaw (path: string) (pixels: byte array) =
    File.WriteAllBytes(path, pixels)

let loadRaw (path: string) : byte array option =
    if File.Exists(path) then Some(File.ReadAllBytes(path))
    else None

/// Run a golden test
let runGoldenTest
    (name: string)
    (goldenDir: string)
    (actualDir: string)
    (generateGolden: bool)
    (width: int)
    (height: int)
    (pixels: byte array) : bool =

    Directory.CreateDirectory(goldenDir) |> ignore
    Directory.CreateDirectory(actualDir) |> ignore

    let goldenRaw = Path.Combine(goldenDir, name + ".raw")
    let goldenPng = Path.Combine(goldenDir, name + ".png")
    let actualPng = Path.Combine(actualDir, name + ".png")

    // Always save the actual PNG
    let pngBytes = FSharp.Graphics.Host.Png.encode width height pixels
    File.WriteAllBytes(actualPng, pngBytes)

    if generateGolden then
        saveRaw goldenRaw pixels
        File.WriteAllBytes(goldenPng, pngBytes)
        printfn "  GOLDEN GENERATED: %s" name
        true
    else
        match loadRaw goldenRaw with
        | None ->
            printfn "  MISSING GOLDEN:   %s (run with --generate-golden to create)" name
            false
        | Some golden ->
            match compare 2 pixels golden with
            | Pass ->
                printfn "  PASS:             %s" name
                true
            | Fail(maxDelta, failBytes, totalBytes) ->
                printfn "  FAIL:             %s (max delta=%d, %d/%d bytes differ)" name maxDelta failBytes totalBytes
                false
            | MissingGolden ->
                printfn "  MISSING GOLDEN:   %s" name
                false
