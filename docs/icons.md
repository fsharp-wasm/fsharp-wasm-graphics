# Icons on a canvas

Written 2026-11-26.  `FSharp.Graphics.Icons` draws the icons of `FSharp.Wasm.Icons` (fsharp-wasm-core: typed shapes on a 24 x 24 grid, stroked with a width of 2, round ends and joins, the
Lucide set) in the scene graph.  The shapes are the library's; the strokes are this layer's, so that no icon is written twice: the SVG text, the DOM view and the canvas are one description.

```fsharp
open FSharp.Wasm.Icons
open FSharp.Graphics.Icons

let plus : Scene = IconScene.view 16.0f theme.Text Lucide.plus      // 16 px, in the colour of the text around it, corner at the origin
```

## How

1. `IconScene.commands` turns each shape into the commands of a path (a circle is two arcs, a rounded rectangle lines and arcs; a `Path` is `PathData.parse` of its text).
2. `PathData.flatten stepsPerUnit` samples the curves and arcs to straight segments: a cubic or quadratic gets as many as its control polygon is long times `stepsPerUnit`, at least four;
   an arc the same by its length and at least one for every 15 degrees.  `IconScene` asks for enough that no segment is longer than about two pixels at the size drawn.
3. Each segment is a capsule (`LineSdf`) of the stroke width, `2 * size / 24` pixels: round ends and joins come out of that, as in the SVG.

`PathData` reads what SVG path data allows: `M L H V C S Q T A Z` in both cases, a number repeating the last command (after a move, it is a line), a sign or a second point starting the next
number (`3-4`, `.5.5`), exponents, and the two flags of an arc written together with the number that follows (`a2 2 0 011 1`).  What is not path data ends the reading there; the commands
before it are drawn, as a browser does.

## Not done

- One colour, as `currentColor` is in the SVG: a two-colour icon is not a thing of this set.
- A segment is a node of the scene: an icon of a hundred segments (the larger ones of the set, at a large size) is a hundred nodes.  A cache per (icon, size, colour) is the first thing to
  add if a screen shows many of them (`Memo`); it has not been measured to be needed.
- The accuracy of the arcs is that of `Math.Sin` and `Math.Cos` of the compiler: 1e-14 relative since 2026-11-26 (fsharp-wasm, `docs/STATUS.md`); before that, 4e-6 near π.
