# Performance of the canvas stack: what was measured, what was changed, what is next

Every number here was measured on one machine (a shared 4-core Xeon, Chromium from Playwright, Node 22) on **2026-11-22**, with the scripts named below. Nothing is a claim about another machine.

## 1. What the builders cost

`rectangle { bounds …; fill … }`, `text { … }` and `column { … }` are computation expressions over a struct of the accumulated state; every operation is an `inline` update of that struct. The same scenes built with the constructors, in a loop
(a module compiled by fsharp-wasm, run on Node 22, median of 9 runs of 200 000 scenes):

| scene | builder | constructors | builder ÷ constructors |
|---|---:|---:|---:|
| rectangle, five operations | 61 ns | 43 ns | 1.41× |
| rectangle, two operations (`bounds`, `fill`) | 46 ns | 29 ns | 1.58× |
| text, four operations | 35 ns | 34 ns | 1.02× |
| column of three children | 100 ns | 82 ns | 1.22× |

The builder costs about 17 ns more per rectangle. In the widget editor, profiled in Chromium while a pointer moved over it (720 moves, one frame each), `Builders_Build_shape` took **8 ms of 12.1 s**; the F# code altogether took
402 µs per move, the Canvas 2D calls and the glue around them 1.2 ms, and the browser's own work (painting, compositing) 4.4 ms. So the builders are not where the time of a frame goes, and they are kept as they are.

## 2. The finding that mattered: every frame was drawn, always

The widget editor and the Ghost Todo client passed `animated = true` to `CanvasHost.Frame`, so a frame was built, resolved, emitted and drawn at every animation frame whether or not anything had happened.
Counted with a wrapper around every method of `CanvasRenderingContext2D` (a page that nobody touches, 60 Hz): **1 322 Canvas 2D calls a frame**
(`set fillStyle` 183, `beginPath` 175, `roundRect` 166, `fill` 143, `save` 107, `restore` 107, `transform` 99, `set font` 40) and an idle tab that was **busy 29.9 % of the time** (898 ms of 3 004 ms, a CPU profile).

Now (`CanvasHost`, graphics 3b14794): a frame is drawn when the page changed (a resize, an event a handler took, `Invalidate`, a model that changed), or when an `Animated` node of the scene is due.
The only thing in the widgets that depends on the clock is the caret of a focused text field; it is declared as a transition in two steps (`Easing.Steps`), and `SceneCombinators.animationDelay` tells the host when the next step is due.

| the editor, Chromium | before | after |
|---|---:|---:|
| nobody touches the page | 60 frames a second, busy 29.9 % | no frames, busy 0.7 % (20 ms of 3 005 ms) |
| a text field has the focus | 43 frames in 700 ms | 1 to 2 frames in 700 ms |

A pointer move costs the page 2.5 ms of CPU on average now (F# code 167 µs, JavaScript glue and Canvas 2D calls 554 µs, garbage collection 88 µs, the browser's own work 1.7 ms) where it was 6.2 ms (402 µs, 1.2 ms, 165 µs, 4.4 ms):
the 720 moves of the script are hovers and a drag, and a move that changes nothing draws nothing now.

The e2e tests of fsharp-wasm-ide and fsharp-wasm-backend count the frames (`clearRect` calls) and say so.

A scene that declares a time dependence in no other way than `Animated` is drawn too rarely: a widget that reads `ui.Time` must wrap what depends on it in an `Animated` node (the caret is the only one so far).
The pixels of two frames of the same scene are not always the same: with the same draw calls the soft shadows differ by up to 2 levels of 255 between a frame that has a layer of reduced opacity in it (a disabled button)
and one that has not. The e2e tests compare pictures with a tolerance for that reason; the cause in the browser was not investigated.

## 3. Where the time of a frame that is drawn goes (measured 2026-11-22)

Section 2 counted per pointer move, most of which draw nothing. This one counts per frame that draws: the two header buttons of the editor are hovered alternately, so every move changes a hover state and draws.
Same machine and browser as above (headless Chromium, which rasterises in software: the browser's own share is for this configuration only, a GPU changes it).

| per drawn frame (two runs of 500 frames; `tests/perf/drawn-frame.mjs` of fsharp-wasm-ide) | share of the busy time |
|---|---:|
| the browser's own work (raster, paint, compositing) | 71 % |
| the browser's Canvas 2D methods (`fill`, `fillText`, `roundRect` ...), called by the glue | 12 % |
| F# code (build, resolve, emit, the sink) | 6 to 7 % |
| JavaScript glue (`gfx.js`) | 5 % |
| garbage collection | 4 % |
| the crossing from the module into JavaScript (`wasm-to-js`) | 1 % |

The profile slows the code by about 1.7 times, so read the shares, not the milliseconds (4.9 to 5.4 ms busy per drawn frame).
(An earlier version of this table counted the browser's own methods, which the profiler lists without a url, as F# code; the corrected split is the one above.)

The callback of the animation frame, timed without a profiler (the part that the page's code owns; 500 drawn frames per run, two runs of each):

| Canvas 2D methods and setters | callback, median |
|---|---:|
| real | 1.10 and 1.20 ms |
| replaced by empty functions (`measureText` kept) | 0.40 and 0.30 ms |

So of the 1.1 to 1.2 ms the page's code spends per drawn frame, **about 0.8 ms is the Canvas 2D calls themselves and about 0.3 to 0.4 ms is everything else**: building, resolving and emitting the scene,
the roughly 690 calls from the module to JavaScript (that frame), and the glue. The crossing between WebAssembly and JavaScript is 1 % of the busy time in the profile: a flat command buffer that crosses
once (see below) can win only that and the allocation of the command list, and is not where to start.
(A replay of the recorded calls in plain JavaScript took 0.2 ms; it leaves out the gradients and sets none of their styles, so it understates what the same calls cost in place. The difference of the table above is the number to use.)

## 4. A frame redraws only what changed (measured 2026-11-22)

`CanvasHost` kept no memory of the canvas: every frame cleared it and drew all of it. It keeps the commands of the last frame now, `Damage.between` compares them with the new ones, and the canvas is
clipped to the boxes of what differs (cleared there, whole device pixels) while `CanvasSink.runIn` draws only the commands that reach a box, with every transform, clip and layer around them that holds
something in it. Nothing changed: nothing is drawn. Many changes, a changed transform or a different structure: the frame is drawn whole, as before. What a frame is made of is compared by value
(`Damage.same`), the boxes leave room for strokes, shadows, blurred layers and glyphs, and two boxes are one when they overlap or their union is hardly larger than the two.

The same eight hovers and the same loop as in section 3 (the editor, the two header buttons hovered alternately, 500 frames):

| per drawn frame | before | after |
|---|---:|---:|
| Canvas 2D calls | 1 322 | 67 to 89 |
| callback of the animation frame, median (no profiler) | 1.1 ms | 0.4 ms |
| busy time, CPU profile (slowed by the profiler) | 5.4 ms | 1.65 ms |
| of it the browser's own work (raster, paint, compositing) | 3.8 ms | 1.0 ms |
| of it F# (the module; the comparison of two command lists is in it now) | 0.35 ms | 0.33 ms |

`tests/e2e/redraw.mjs` of fsharp-wasm-ide runs the editor twice side by side (the second with `?fullframes`, every frame whole), sends the same events to both (hovers, clicks, Undo, drags from the
palette, a drag cancelled, text in the inspector, preview, the code panel, a seeded walk of 60 moves, a resize) and compares the two canvases pixel by pixel after each step (the tolerance is the 2 of 255
that soft shadows differ by anyway): no pixel beyond it.

## 5. What is next (not measured yet; each needs its own measurement before and after)

1. **Fewer Canvas 2D calls in a frame that is drawn whole** (a resize, a preview, a panel that opens, 1 322 calls): a `save`/`restore` pair and a `transform` per translated group where a translation
   alone could be added to the coordinates; consecutive rectangles of the same *opaque* fill, or that do not overlap, as one path (not for translucent fills that overlap).
2. **Static parts drawn once** to an offscreen canvas and copied; only the parts that changed redrawn. What the build-time pre-compiler can produce for the parts of a scene that no model decides.
3. **A flat command buffer in linear memory**, one call across the boundary a frame, with the protocol that `FSharp.Graphics.Render.Encode` and `FSharp.Graphics.Host.Reader` already are. It saves the
   crossing and the allocation of the command list (about 1 % and 4 % of the busy time of a drawn frame by the numbers of section 3), so it comes last.

## 6. How to measure again

`tests/perf/frames.mjs` of fsharp-wasm-ide (`bash build/build.sh && node tests/perf/frames.mjs`) prints the three numbers of sections 1 and 2: the Canvas 2D calls of a frame, the busy share of an untouched page over 3 s, and the CPU per pointer move split into
F# code, glue, garbage collection and the browser. The method: wrap the methods of `CanvasRenderingContext2D.prototype` (`addInitScript`) and count per frame (a frame starts with `clearRect`); take a CPU profile (`Profiler.start` through a CDP
session) and add up the self time per function name (the names of the module's name section are in the profile). The builder microbenchmark of section 1 is a program of one file that builds the same scene both ways in a loop and returns the number of nodes.
