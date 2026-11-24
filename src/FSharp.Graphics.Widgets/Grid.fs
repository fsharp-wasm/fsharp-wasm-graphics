namespace FSharp.Graphics.Widgets

open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Primitives
open FSharp.Graphics.Scene
open FSharp.Graphics.SceneDsl

/// A grid of cells for tables that are far larger than the screen: a ledger, a pivot table, the lines behind a total.  It draws what is in the window and nothing else, so the
/// cost of a frame is the size of the window and not of the table, and it knows nothing of what the rows and columns are: they are lines with a label, a depth and a
/// caret (a line that is the total of the lines below it), and the cells are asked for as functions, when they are on screen.  Opening and folding is the application's
/// (it decides which lines there are); scrolling and the selection are `State`, which the application keeps.
///
/// The arithmetic is separate from the drawing (`hit`, `key`, `reveal`, `clamp` are plain functions), so that it is tested without a canvas.
module Grid =

    /// The header of a row or of a column.
    type Line =
        { Label: string
          /// How deep: a row's label is set in by it; a total of totals is a lower number than the totals it is made of.
          Level: int
          /// The line is the total of lines below it: it has a caret, and a click on it opens or folds it.
          Group: bool
          Open: bool }

    /// A cell: what it says, whether it is dimmed (a zero in a ledger is quiet) and whether it is set at the left (text) rather than the right (numbers).
    type Cell = { Text: string; Muted: bool; Left: bool }

    /// Sizes in pixels.  Rows and columns are of one size each, which is what lets any cell be found without looking at the others.
    type Metrics = { RowHeight: float32; HeaderHeight: float32; ColWidth: float32; HeaderWidth: float32 }

    let metrics : Metrics = { RowHeight = 28.0f; HeaderHeight = 32.0f; ColWidth = 112.0f; HeaderWidth = 240.0f }

    /// The size of the table and of the window it is seen through.
    type Frame = { Rows: int; Cols: int; Width: float32; Height: float32; Metrics: Metrics }

    /// Where the grid is scrolled to, and which cell is selected.
    type State = { OffsetX: float32; OffsetY: float32; Selected: (int * int) option }

    let start : State = { OffsetX = 0.0f; OffsetY = 0.0f; Selected = None }

    let private bodyWidth (f: Frame) : float32 = Scalar.max' 0.0f (f.Width - f.Metrics.HeaderWidth)
    let private bodyHeight (f: Frame) : float32 = Scalar.max' 0.0f (f.Height - f.Metrics.HeaderHeight)
    let private maxOffsetX (f: Frame) : float32 = Scalar.max' 0.0f (float32 f.Cols * f.Metrics.ColWidth - bodyWidth f)
    let private maxOffsetY (f: Frame) : float32 = Scalar.max' 0.0f (float32 f.Rows * f.Metrics.RowHeight - bodyHeight f)

    /// The last line or column that is in the window, -1 when none is.  A window that ends exactly on a border does not show the next line.
    let private lastVisible (count: int) (offset: float32) (extent: float32) (size: float32) : int =
        if extent <= 0.0f then -1 else min (count - 1) (int ((offset + extent - 0.01f) / size))

    /// The state with its offsets inside the table: after a resize, after lines were folded away.
    let clamp (f: Frame) (s: State) : State =
        { s with OffsetX = Scalar.clamp 0.0f (maxOffsetX f) s.OffsetX; OffsetY = Scalar.clamp 0.0f (maxOffsetY f) s.OffsetY }

    /// Scrolled so that the cell (row, column) is in the window, by the least that does it.
    let reveal (f: Frame) (r: int) (c: int) (s: State) : State =
        let m = f.Metrics
        let top = float32 r * m.RowHeight
        let left = float32 c * m.ColWidth
        let y = if top < s.OffsetY then top elif top + m.RowHeight > s.OffsetY + bodyHeight f then top + m.RowHeight - bodyHeight f else s.OffsetY
        let x = if left < s.OffsetX then left elif left + m.ColWidth > s.OffsetX + bodyWidth f then left + m.ColWidth - bodyWidth f else s.OffsetX
        clamp f { s with OffsetX = x; OffsetY = y }

    /// What a point of the grid (in its own coordinates) is on.
    type Hit =
        | InBody of row: int * col: int
        | InRowHeader of row: int
        | InColHeader of col: int
        | Corner
        | Outside

    let hit (f: Frame) (s: State) (x: float32) (y: float32) : Hit =
        let m = f.Metrics
        if x < 0.0f || y < 0.0f || x >= f.Width || y >= f.Height then Outside
        elif x < m.HeaderWidth && y < m.HeaderHeight then Corner
        elif y < m.HeaderHeight then
            let c = int ((x - m.HeaderWidth + s.OffsetX) / m.ColWidth)
            if c < f.Cols then InColHeader c else Outside
        elif x < m.HeaderWidth then
            let r = int ((y - m.HeaderHeight + s.OffsetY) / m.RowHeight)
            if r < f.Rows then InRowHeader r else Outside
        else
            let r = int ((y - m.HeaderHeight + s.OffsetY) / m.RowHeight)
            let c = int ((x - m.HeaderWidth + s.OffsetX) / m.ColWidth)
            if r < f.Rows && c < f.Cols then InBody(r, c) else Outside

    /// What a key does to the grid.
    type Key =
        | Moved of State
        | ToggleRow of int
        | ToggleCol of int
        | Open of row: int * col: int
        | Ignored

    /// The keys of a grid, by the toolkit's names (`Ui.keyName`): the arrows, Page Up and Page Down, Home and End (the first and last column of the row) move the selection and
    /// scroll it into view; Enter opens the cell; Space opens or folds the row, with Shift the column.
    let key (f: Frame) (shift: bool) (name: string) (s: State) : Key =
        let go (r: int) (c: int) = Moved(reveal f r c { s with Selected = Some(r, c) })
        match s.Selected with
        | _ when f.Rows = 0 || f.Cols = 0 -> Ignored
        | None ->
            (match name with
             | "Up" | "Down" | "Left" | "Right" | "PageUp" | "PageDown" | "Home" | "End" -> go 0 0
             | _ -> Ignored)
        | Some(r, c) ->
            let page = max 1 (int (bodyHeight f / f.Metrics.RowHeight) - 1)
            (match name with
             | "Up" -> go (max 0 (r - 1)) c
             | "Down" -> go (min (f.Rows - 1) (r + 1)) c
             | "Left" -> go r (max 0 (c - 1))
             | "Right" -> go r (min (f.Cols - 1) (c + 1))
             | "PageUp" -> go (max 0 (r - page)) c
             | "PageDown" -> go (min (f.Rows - 1) (r + page)) c
             | "Home" -> go r 0
             | "End" -> go r (f.Cols - 1)
             | "Enter" -> Open(r, c)
             | "Space" -> if shift then ToggleCol c else ToggleRow r
             | _ -> Ignored)

    type Props<'m> =
        { Frame: Frame
          State: State
          /// the corner of the headers says what the rows and the columns are ("Account / Period")
          Corner: string
          Row: int -> Line
          Col: int -> Line
          Cell: int -> int -> Cell
          /// scrolled, or a cell selected
          OnState: State -> unit
          OnToggleRow: int -> unit
          OnToggleCol: int -> unit
          /// the selected cell is asked for again: it is opened
          OnOpen: int -> int -> unit }

    let private caret (isOpen: bool) (color: Color) (x: float32) (y: float32) : Scene =
        let a, b, c =
            if isOpen then Vec2.create (x - 1.0f) (y - 2.0f), Vec2.create (x + 3.0f) (y + 2.0f), Vec2.create (x + 7.0f) (y - 2.0f)
            else Vec2.create (x + 1.0f) (y - 4.0f), Vec2.create (x + 5.0f) y, Vec2.create (x + 1.0f) (y + 4.0f)
        Group [ Shape(Path(LineSdf(a, b, 1.6f), Solid color, None, None)); Shape(Path(LineSdf(b, c, 1.6f), Solid color, None, None)) ]

    let private hint = "Arrow keys move, Enter opens the cell, Space opens or folds the row, Shift and Space the column."

    /// The grid, as a region named `name` whose value is the selected cell.  Place it where it is to be; `p.Frame.Width` and `Height` are its size.
    let view (ui: Ui<'m>) (id: string) (name: string) (p: Props<'m>) : Scene =
        let th = ui.Theme
        let f = p.Frame
        let m = f.Metrics
        let s = p.State
        let bw = bodyWidth f
        let bh = bodyHeight f
        let fs = th.FontSize
        let lineH = fs * 1.4f
        let ty = (m.RowHeight - fs * 1.2f) * 0.5f
        // the lines and columns that are in the window, and where they start
        let r0 = int (s.OffsetY / m.RowHeight)
        let r1 = lastVisible f.Rows s.OffsetY bh m.RowHeight
        let c0 = int (s.OffsetX / m.ColWidth)
        let c1 = lastVisible f.Cols s.OffsetX bw m.ColWidth
        let nr = max 0 (r1 - r0 + 1)
        let nc = max 0 (c1 - c0 + 1)
        let rows = Array.init nr (fun i -> p.Row(r0 + i))
        let cols = Array.init nc (fun j -> p.Col(c0 + j))
        let rowY (i: int) : float32 = float32 i * m.RowHeight + float32 r0 * m.RowHeight - s.OffsetY
        let colX (j: int) : float32 = float32 j * m.ColWidth + float32 c0 * m.ColWidth - s.OffsetX
        let quiet = Color.withAlpha 0.55f th.SurfaceRaised
        let selRow = (match s.Selected with Some(r, _) -> r | None -> -1)
        let selCol = (match s.Selected with Some(_, c) -> c | None -> -1)
        let tint = Color.withAlpha 0.18f th.Accent
        let right (color: Color) : TextStyle = { Ui.textStyle ui fs color with Wrap = true; Align = TextAlign.Right }
        let left (color: Color) : TextStyle = { Ui.textStyle ui fs color with Wrap = true }

        let body =
            Group [
                for j in 0 .. nc - 1 do
                    if cols.[j].Group then yield rectangle { bounds (colX j) 0.0f m.ColWidth bh; fill quiet }
                for i in 0 .. nr - 1 do
                    let y = rowY i
                    if rows.[i].Group then yield rectangle { bounds 0.0f y bw m.RowHeight; fill quiet }
                    yield rectangle { bounds 0.0f (y + m.RowHeight - 1.0f) bw 1.0f; fill th.Border }
                    for j in 0 .. nc - 1 do
                        let cell = p.Cell (r0 + i) (c0 + j)
                        if cell.Text.Length > 0 then
                            yield shift (colX j + 8.0f) (y + ty) (Clip(Rect.create 0.0f 0.0f (m.ColWidth - 16.0f) lineH, Text(cell.Text, (if cell.Left then left else right) (if cell.Muted then th.TextMuted else th.Text))))
                for j in 0 .. nc - 1 do
                    yield rectangle { bounds (colX j + m.ColWidth - 1.0f) 0.0f 1.0f bh; fill th.Border }
                if selRow >= r0 && selRow <= r1 && selCol >= c0 && selCol <= c1 then
                    yield rectangle { bounds (colX (selCol - c0) + 1.0f) (rowY (selRow - r0) + 1.0f) (m.ColWidth - 2.0f) (m.RowHeight - 2.0f); fill tint; stroke th.Focus 2.0f }
            ]

        let rowHeader =
            Group [
                for i in 0 .. nr - 1 do
                    let y = rowY i
                    let line = rows.[i]
                    if line.Group then yield rectangle { bounds 0.0f y m.HeaderWidth m.RowHeight; fill quiet }
                    if r0 + i = selRow then yield rectangle { bounds 0.0f y m.HeaderWidth m.RowHeight; fill tint }
                    yield rectangle { bounds 0.0f (y + m.RowHeight - 1.0f) m.HeaderWidth 1.0f; fill th.Border }
                    let indent = 10.0f + float32 line.Level * 16.0f
                    if line.Group then yield caret line.Open th.Text (indent) (y + m.RowHeight * 0.5f)
                    let at = indent + 16.0f
                    yield shift at (y + ty) (Clip(Rect.create 0.0f 0.0f (Scalar.max' 0.0f (m.HeaderWidth - at - 8.0f)) lineH, Text(line.Label, left th.Text)))
                yield rectangle { bounds (m.HeaderWidth - 1.0f) 0.0f 1.0f bh; fill th.Border }
            ]

        let colHeader =
            Group [
                for j in 0 .. nc - 1 do
                    let x = colX j
                    let line = cols.[j]
                    if line.Group then yield rectangle { bounds x 0.0f m.ColWidth m.HeaderHeight; fill quiet }
                    if c0 + j = selCol then yield rectangle { bounds x 0.0f m.ColWidth m.HeaderHeight; fill tint }
                    if line.Group then yield caret line.Open th.Text (x + 8.0f) (m.HeaderHeight * 0.5f)
                    let hy = (m.HeaderHeight - fs * 1.2f) * 0.5f
                    yield shift (x + 24.0f) hy (Clip(Rect.create 0.0f 0.0f (m.ColWidth - 32.0f) lineH, Text(line.Label, right th.Text)))
                    yield rectangle { bounds (x + m.ColWidth - 1.0f) 0.0f 1.0f m.HeaderHeight; fill th.Border }
                yield rectangle { bounds 0.0f (m.HeaderHeight - 1.0f) bw 1.0f; fill th.Border }
            ]

        let corner =
            Group [
                rectangle { bounds 0.0f 0.0f m.HeaderWidth m.HeaderHeight; fill th.SurfaceRaised }
                shift 10.0f ((m.HeaderHeight - fs * 1.2f) * 0.5f) (Clip(Rect.create 0.0f 0.0f (m.HeaderWidth - 20.0f) lineH, Text(p.Corner, left th.TextMuted)))
                rectangle { bounds 0.0f (m.HeaderHeight - 1.0f) m.HeaderWidth 1.0f; fill th.Border }
                rectangle { bounds (m.HeaderWidth - 1.0f) 0.0f 1.0f m.HeaderHeight; fill th.Border }
            ]

        let bars =
            let maxY = maxOffsetY f
            let maxX = maxOffsetX f
            let thumb = Color.withAlpha 0.55f th.TextMuted
            Group [
                if maxY > 0.0f then
                    let content = float32 f.Rows * m.RowHeight
                    let h = Scalar.max' 24.0f (bh * bh / content)
                    yield rectangle { bounds (f.Width - 7.0f) (m.HeaderHeight + (bh - h) * (s.OffsetY / maxY)) 4.0f h; radius 2.0f; fill thumb }
                if maxX > 0.0f then
                    let content = float32 f.Cols * m.ColWidth
                    let w = Scalar.max' 24.0f (bw * bw / content)
                    yield rectangle { bounds (m.HeaderWidth + (bw - w) * (s.OffsetX / maxX)) (f.Height - 7.0f) w 4.0f; radius 2.0f; fill thumb }
            ]

        let drawn =
            Group [
                rectangle { bounds 0.0f 0.0f f.Width f.Height; radius th.Radius; fill th.Surface }
                Clip(Rect.create m.HeaderWidth m.HeaderHeight bw bh, shift m.HeaderWidth m.HeaderHeight body)
                Clip(Rect.create 0.0f m.HeaderHeight m.HeaderWidth bh, shift 0.0f m.HeaderHeight rowHeader)
                Clip(Rect.create m.HeaderWidth 0.0f bw m.HeaderHeight, shift m.HeaderWidth 0.0f colHeader)
                corner
                bars
                rectangle { bounds 0.5f 0.5f (f.Width - 1.0f) (f.Height - 1.0f); radius th.Radius; stroke th.Border 1.0f }
                Ui.focusRing ui id f.Width f.Height th.Radius
            ]

        let handle (e: InputEvent) : bool =
            match e with
            | Scroll d ->
                // the wheel in notches, positive Y up and positive X right (see `InputEvent`): up is toward the top of the table, right toward its last column; with Shift the wheel
                // scrolls sideways, as in a spreadsheet
                let dx = d.X * 36.0f - (if ui.State.Shift then d.Y * 36.0f else 0.0f)
                let dy = if ui.State.Shift then 0.0f else -d.Y * 36.0f
                p.OnState(clamp f { s with OffsetX = s.OffsetX + dx; OffsetY = s.OffsetY + dy })
                true
            | PointerDown(pt, 0) ->
                ui.Send(ui.Lift(UiMsg.Focus id))
                (match hit f s pt.X pt.Y with
                 | InBody(r, c) -> if s.Selected = Some(r, c) then p.OnOpen r c else p.OnState(reveal f r c { s with Selected = Some(r, c) })
                 | InRowHeader r ->
                    if (p.Row r).Group then p.OnToggleRow r
                    else p.OnState(reveal f r (max 0 selCol) { s with Selected = Some(r, max 0 selCol) })
                 | InColHeader c -> if (p.Col c).Group then p.OnToggleCol c
                 | Corner | Outside -> ())
                true
            | _ -> false

        let describe =
            if selRow >= 0 && selRow < f.Rows && selCol >= 0 && selCol < f.Cols then (p.Row selRow).Label + ", " + (p.Col selCol).Label + ": " + (p.Cell selRow selCol).Text
            else ""
        let act (a: SemanticAction) =
            match a with
            | SemanticAction.Activate -> if selRow >= 0 && selCol >= 0 then p.OnOpen selRow selCol
            | _ -> ()
        // the area the region answers for is below what is drawn, as in a scroll view
        let reactive = Reactive(handle, drawn)
        Ui.semantic ui id Role.Grid name (fun sem -> { sem with Value = describe; Description = hint }) act reactive
