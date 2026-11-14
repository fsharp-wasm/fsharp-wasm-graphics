namespace FSharp.Graphics.Canvas

open System
open System.Collections.Generic
open FSharp.Graphics.Core

/// ITextEngine for the browser: the advance of each character comes from `measureText`, lines are broken greedily at
/// spaces, vertical metrics follow the desktop engine's convention (line height = size * LineHeight, the first baseline
/// centres the font's natural height in the line).  A glyph id is the UTF-16 code unit; the canvas host turns the
/// glyphs of a line back into a string and draws it with `fillText`.
type CanvasTextEngine() =
    // advance widths per size: ASCII + Latin-1 is cached, everything else is measured on demand
    let cache = Dictionary<int, float32[]>()

    // one font and one weight, like the desktop host (TextStyle.Weight is not rendered there either)
    let weight = 400

    let advance (size: float32) (c: char) : float32 =
        let key = int (size * 8.0f)
        let table =
            match cache.TryGetValue key with
            | true, t -> t
            | _ ->
                let t = Array.create 256 (-1.0f)
                cache.[key] <- t
                t
        let code = int c
        if code < 256 then
            let cached = table.[code]
            if cached >= 0.0f then cached
            else
                let w = float32 (Gfx.measure (string c) (float size) weight)
                table.[code] <- w
                w
        else float32 (Gfx.measure (string c) (float size) weight)

    /// Lay out one paragraph (no '\n'); `lineBreaks` receive the index of the first character of each line.
    let layoutParagraph (text: string) (style: TextStyle) (maxWidth: float32) (yOffset: float32) (glyphs: ResizeArray<GlyphPosition>) : float32 * int =
        let size = style.SizePx
        let ls = style.LetterSpacing
        let lh = size * style.LineHeight
        let natural = size * 1.16f
        let firstBaseline = (lh - natural) * 0.5f + size * 0.93f
        let wrap = style.Wrap && maxWidth > 0.0f && maxWidth < 1.0e7f
        let n = text.Length
        let adv = Array.zeroCreate<float32> n
        for i in 0 .. n - 1 do adv.[i] <- advance size text.[i] + ls
        // greedy line breaking: start index of every line
        let starts = ResizeArray<int>()
        starts.Add 0
        let mutable x = 0.0f
        let mutable lineStart = 0
        let mutable lastSpace = -1
        let mutable i = 0
        while i < n do
            let c = text.[i]
            if wrap && c <> ' ' && x + adv.[i] > maxWidth && i > lineStart then
                // break after the last space of this line, otherwise inside the word
                let breakAt = if lastSpace >= lineStart then lastSpace + 1 else i
                starts.Add breakAt
                lineStart <- breakAt
                lastSpace <- -1
                x <- 0.0f
                i <- breakAt
            else
                if c = ' ' then lastSpace <- i
                x <- x + adv.[i]
                i <- i + 1
        let lines = starts.Count
        // line extents (trailing spaces do not count) and glyph positions
        let lineW = Array.zeroCreate<float32> lines
        for l in 0 .. lines - 1 do
            let a = starts.[l]
            let b = if l + 1 < lines then starts.[l + 1] else n
            let mutable e = b
            while e > a && text.[e - 1] = ' ' do e <- e - 1
            let mutable w = 0.0f
            for k in a .. e - 1 do w <- w + adv.[k]
            lineW.[l] <- w
        let mutable widest = 0.0f
        for l in 0 .. lines - 1 do
            if lineW.[l] > widest then widest <- lineW.[l]
        let boxW = if wrap && style.Align <> TextAlign.Left then maxWidth else widest
        for l in 0 .. lines - 1 do
            let a = starts.[l]
            let b = if l + 1 < lines then starts.[l + 1] else n
            let shift =
                match style.Align with
                | TextAlign.Center -> (boxW - lineW.[l]) * 0.5f
                | TextAlign.Right -> boxW - lineW.[l]
                | _ -> 0.0f
            let mutable pen = shift
            for k in a .. b - 1 do
                glyphs.Add { GlyphId = uint32 text.[k]; X = pen; Y = yOffset + firstBaseline + float32 l * lh; AdvanceX = adv.[k] }
                pen <- pen + adv.[k]
        boxW, lines

    interface ITextEngine with
        member _.Layout(text, style, maxWidth) =
            let lh = style.SizePx * style.LineHeight
            let glyphs = ResizeArray<GlyphPosition>()
            let mutable y = 0.0f
            let mutable w = 0.0f
            let paragraphs = text.Split '\n'
            for p in paragraphs do
                let (pw, lines) = layoutParagraph p style maxWidth y glyphs
                if pw > w then w <- pw
                y <- y + float32 (max 1 lines) * lh
            { Glyphs = glyphs.ToArray(); Width = w; Height = y }
