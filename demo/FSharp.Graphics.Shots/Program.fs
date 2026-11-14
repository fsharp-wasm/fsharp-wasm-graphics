module FSharp.Graphics.Shots.Program

open System
open System.IO
open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Scene
open FSharp.Graphics.Render
open FSharp.Graphics.Host
open FSharp.Graphics.Widgets
open FSharp.Graphics.SceneDsl

let white = Color.rgba 255uy 255uy 255uy 255uy

[<EntryPoint>]
let main argv =
    let out = if argv.Length > 0 then argv.[0] else "shots"
    Directory.CreateDirectory out |> ignore
    let fonts = FontLocator.loadDefault ()
    let text = GlyphAuraTextEngine fonts :> ITextEngine
    let mutable failures = 0

    let save (name: string) (w: int) (h: int) (buf: byte[]) (len: int) =
        let r = SoftwareRenderer(w, h, fonts)
        match CommandReader.run r buf len with
        | Decoded n ->
            File.WriteAllBytes(Path.Combine(out, name + ".png"), Png.encode w h (r.ToRgba8 white))
            printfn "%-22s %7d bytes  %5d cmds" name len n
        | Malformed(off, msg) ->
            failures <- failures + 1
            eprintfn "%s: MALFORMED at %d: %s" name off msg

    let shot (name: string) (w: int) (h: int) (scene: Scene) =
        let stage = Stage(text, float32 w, float32 h)
        let struct (buf, len) = stage.Frame scene
        save name w h buf len

    // 1. DSL, plus proof that the optimiser changes nothing observable
    let dslScene = Showcase.dsl ()
    shot "dsl" 900 560 dslScene
    let encode (s: Scene) =
        let stage = Stage(text, 900.0f, 560.0f)
        let struct (b, l) = stage.Frame s
        Array.sub b 0 l
    let optimised = Optimize.scene dslScene
    let same = encode dslScene = encode optimised
    printfn "optimiser: %d -> %d nodes, identical command stream: %b" (countNodes dslScene) (countNodes optimised) same
    if not same then failures <- failures + 1

    // 2. widget gallery + CSG
    let galleryUi : Ui<unit> =
        { Theme = Theme.dark; Text = text; State = UiState.empty; Time = 0.0f; Lift = ignore; Send = ignore }
    shot "gallery" 900 620 (Showcase.gallery galleryUi)
    shot "gallery-light" 900 620 (Showcase.gallery { galleryUi with Theme = Theme.light })
    shot "csg" 920 240 (Showcase.csg ())
    if failures > 0 then 1 else 0
