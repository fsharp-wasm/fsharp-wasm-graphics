namespace FSharp.Graphics.Widgets

open System
open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Scene
open FSharp.Graphics.SceneDsl

/// Transient interaction state shared by all widgets. Lives in the application model.
type UiState =
    { Hover: string option
      Pressed: string option
      Focus: string option
      Shift: bool }

[<RequireQualifiedAccess>]
type UiMsg =
    | Hover of string
    | Unhover of string
    | Press of string
    | Release
    | Focus of string
    | Blur
    | Shift of bool

module UiState =
    let empty = { Hover = None; Pressed = None; Focus = None; Shift = false }

    let update (s: UiState) (m: UiMsg) : UiState =
        match m with
        | UiMsg.Hover id   -> { s with Hover = Some id }
        | UiMsg.Unhover id -> if s.Hover = Some id then { s with Hover = None } else s
        | UiMsg.Press id   -> { s with Pressed = Some id }
        | UiMsg.Release    -> { s with Pressed = None }
        | UiMsg.Focus id   -> { s with Focus = Some id }
        | UiMsg.Blur       -> { s with Focus = None }
        | UiMsg.Shift v    -> { s with Shift = v }

    /// Tab / Shift+Tab over an explicit focus order.
    let cycleFocus (order: string list) (backwards: bool) (s: UiState) : UiState =
        match order with
        | [] -> s
        | _ ->
            let arr = Array.ofList order
            let n = arr.Length
            let next =
                match s.Focus |> Option.bind (fun f -> Array.tryFindIndex ((=) f) arr) with
                | Some i -> arr.[((if backwards then i - 1 else i + 1) % n + n) % n]
                | None -> if backwards then arr.[n - 1] else arr.[0]
            { s with Focus = Some next }

/// Everything a widget needs to draw and to talk back to the application.
/// `Lift` wraps widget-level messages into the app's message type, `Send` dispatches.
type Ui<'Msg> =
    { Theme: Theme
      Text: ITextEngine
      State: UiState
      Time: float32
      Lift: UiMsg -> 'Msg
      Send: 'Msg -> unit }

module Ui =
    let inline private emit (ui: Ui<'m>) (m: UiMsg) = ui.Send(ui.Lift m)

    let isHover (ui: Ui<'m>) id = ui.State.Hover = Some id
    let isPressed (ui: Ui<'m>) id = ui.State.Pressed = Some id
    let isFocused (ui: Ui<'m>) id = ui.State.Focus = Some id

    let textStyle (ui: Ui<'m>) (size: float32) (color: Color) : TextStyle =
        { TextStyle.defaultStyle ui.Theme.Font with SizePx = size; Color = color; Wrap = false }

    /// Natural size of arbitrary content (runs real layout + text shaping).
    let measure (ui: Ui<'m>) (s: Scene) : Rect =
        match Resolve.bounds (Resolve.resolve ui.Text Single.MaxValue s) with
        | Some b -> b
        | None -> Rect.zero

    let textWidth (ui: Ui<'m>) (size: float32) (s: string) : float32 =
        if s.Length = 0 then 0.0f else (ui.Text.Layout(s, textStyle ui size Color.Black, 0.0f)).Width

    /// Move content so its natural top-left is the origin.
    let normalize (ui: Ui<'m>) (s: Scene) : Scene * Vec2 =
        let b = measure ui s
        (if b.X = 0.0f && b.Y = 0.0f then s else Scene.translate (-b.X) (-b.Y) s), Vec2.create b.W b.H

    /// The smallest target a pointer or a finger has to hit (WCAG 2.5.8): widgets are at least this high and wide.
    [<Literal>]
    let MinTarget = 24.0f

    /// The focus ring of a widget of `w` x `h`: 2 px inside its box, in the theme's `Focus` colour, only while it has the
    /// focus.  Inside, so that focusing never changes the layout.
    let focusRing (ui: Ui<'m>) (id: string) (w: float32) (h: float32) (corner: float32) : Scene =
        if isFocused ui id then
            rectangle { bounds 1.0f 1.0f (w - 2.0f) (h - 2.0f); radius corner; stroke ui.Theme.Focus 2.0f }
        else Empty

    /// Describes a widget to assistive technology.  The keyboard focus of the platform and the toolkit's follow each other
    /// through `Focus` / `Blur`; `act` handles the rest (`Activate`, `Increment`, …).
    let semantic (ui: Ui<'m>) (id: string) (role: Role) (name: string) (configure: Semantics -> Semantics)
                 (act: SemanticAction -> unit) (content: Scene) : Scene =
        let focused = isFocused ui id
        let act' (a: SemanticAction) =
            match a with
            | SemanticAction.Focus -> if not focused then emit ui (UiMsg.Focus id)
            | SemanticAction.Blur -> if focused then emit ui UiMsg.Blur
            | other -> act other
        Semantic(configure { Semantics.create id role name with Focusable = true; Focused = focused; Act = act' }, content)

    /// Standard pointer behaviour: hover / press / focus bookkeeping + a click callback.
    let interactive (ui: Ui<'m>) (id: string) (onClick: unit -> unit) (content: Scene) : Scene =
        Cursor(CursorKind.Pointer, Reactive(
            (fun e ->
                match e with
                | PointerEnter _ -> emit ui (UiMsg.Hover id); true
                | PointerLeave _ -> emit ui (UiMsg.Unhover id); true
                | PointerDown(_, 0) -> emit ui (UiMsg.Press id); emit ui (UiMsg.Focus id); true
                | PointerUp(p, 0) ->
                    emit ui UiMsg.Release
                    // pointer positions are local to this widget: activate only if released over it
                    if Rect.contains p (measure ui content) then onClick ()
                    true
                | _ -> false),
            content))

    /// The toolkit's name of a key: a browser says `ArrowLeft` and `" "`, a desktop host (Silk.NET) `Left` and `Space`.
    let keyName (key: string) : string =
        match key with
        | "ArrowLeft" -> "Left"
        | "ArrowRight" -> "Right"
        | "ArrowUp" -> "Up"
        | "ArrowDown" -> "Down"
        | " " -> "Space"
        | other -> other

    /// The keys the toolkit handles for every application: Tab and Shift+Tab over the focusable widgets (in the order of the
    /// scene), Enter and Space on the focused button / check box / switch / combo box, the arrows, Home and End on a
    /// slider or a scrollable region, Escape on an open combo box.  True when it took the key.
    let private handleKey (ui: Ui<'m>) (content: Scene) (key: string) : bool =
        let nodes = SemanticTree.walk content
        let order = SemanticTree.focusOrder nodes
        let focused = ui.State.Focus |> Option.bind (fun f -> nodes |> Array.tryFind (fun s -> s.Id = f))
        let act (a: SemanticAction) =
            match focused with
            | Some s when not s.Disabled -> s.Act a; true
            | _ -> false
        let isRole (r: Role) = match focused with Some s -> s.Role = r | None -> false
        match key with
        | "Tab" when order.Length > 0 ->
            let n = order.Length
            let at = ui.State.Focus |> Option.bind (fun f -> order |> Array.tryFindIndex (fun s -> s.Id = f))
            let next =
                match at with
                | Some i -> order.[((if ui.State.Shift then i - 1 else i + 1) % n + n) % n]
                | None -> if ui.State.Shift then order.[n - 1] else order.[0]
            emit ui (UiMsg.Focus next.Id)
            true
        | "Enter" | "Space" ->
            if isRole Role.Button || isRole Role.CheckBox || isRole Role.Switch || isRole Role.Option || isRole Role.ComboBox
            then act SemanticAction.Activate
            else false
        | "Left" | "Down" -> if isRole Role.Slider || isRole Role.ComboBox || isRole Role.Region then act SemanticAction.Decrement else false
        | "Right" | "Up" -> if isRole Role.Slider || isRole Role.ComboBox || isRole Role.Region then act SemanticAction.Increment else false
        | "Home" -> if isRole Role.Slider || isRole Role.Region then act SemanticAction.First else false
        | "End" -> if isRole Role.Slider || isRole Role.Region then act SemanticAction.Last else false
        | "Escape" ->
            match focused with
            | Some s when s.Expanded = ValueSome true -> act SemanticAction.Dismiss
            | _ -> false
        | _ -> false

    /// Wrap the whole UI once: delivers key events to the app (the pointer is always "over" the
    /// root, so the hover chain always contains it) and clears focus on background clicks.  Keys the toolkit handles
    /// itself (see `handleKey`) do not reach `onKey`; the others do, under the toolkit's key names (`keyName`).
    let root (ui: Ui<'m>) (onKey: string -> unit) (content: Scene) : Scene =
        Reactive(
            (fun e ->
                match e with
                | KeyDown ("ShiftLeft" | "ShiftRight" | "Shift") -> emit ui (UiMsg.Shift true); true
                | KeyUp ("ShiftLeft" | "ShiftRight" | "Shift") -> emit ui (UiMsg.Shift false); true
                | KeyDown k ->
                    let k' = keyName k
                    if not (handleKey ui content k') then onKey k'
                    true
                | PointerDown _ -> emit ui UiMsg.Blur; false
                | _ -> false),
            content)

/// Minimal single-line text editing for hosts that only deliver key *names*
/// (Silk.NET style: "A", "Space", "Number1", "Backspace"). Caret is always at the end.
module TextEdit =
    /// The character a punctuation key name types.
    let private punct (key: string) : string option =
        match key with
        | "Space" -> Some " "
        | "Period" -> Some "."
        | "Comma" -> Some ","
        | "Minus" -> Some "-"
        | "Slash" -> Some "/"
        | "Semicolon" -> Some ";"
        | "Apostrophe" -> Some "'"
        | "Equal" -> Some "="
        | "Underscore" -> Some "_"
        | _ -> None

    /// The character a character types while Shift is held (US layout), for the keys that are not letters.
    let private shifted (c: string) : string option =
        match c with
        | "1" -> Some "!"
        | "2" -> Some "@"
        | "3" -> Some "#"
        | "4" -> Some "$"
        | "5" -> Some "%"
        | "6" -> Some "^"
        | "7" -> Some "&"
        | "8" -> Some "*"
        | "9" -> Some "("
        | "0" -> Some ")"
        | "-" -> Some "_"
        | "=" -> Some "+"
        | ";" -> Some ":"
        | "'" -> Some "\""
        | "," -> Some "<"
        | "." -> Some ">"
        | "/" -> Some "?"
        | _ -> None

    let keyChar (shift: bool) (key: string) : string option =
        let plain =
            if key.Length = 1 && Char.IsLetterOrDigit key.[0] then Some (key.ToLowerInvariant())
            elif (key.StartsWith "Number" || key.StartsWith "Keypad") && key.Length = 7 && Char.IsDigit key.[6] then Some (key.Substring 6)
            else punct key
        match plain with
        | Some c when shift -> (match shifted c with Some u -> Some u | None -> Some (c.ToUpperInvariant()))
        | other -> other

    /// Apply one key to the text (`shift` = Shift currently held, tracked in `UiState.Shift`).
    let apply (shift: bool) (key: string) (value: string) : string =
        match key with
        | "Backspace" -> if value.Length > 0 then value.Substring(0, value.Length - 1) else value
        | k -> match keyChar shift k with Some c -> value + c | None -> value
