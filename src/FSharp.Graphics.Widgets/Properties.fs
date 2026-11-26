namespace FSharp.Graphics.Widgets

open System
open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Primitives
open FSharp.Graphics.Scene
open FSharp.Graphics.SceneDsl

/// A small rounded badge: the value of a property, the size of a selection, the kind of an element.
module Chip =
    let width (ui: Ui<'m>) (text: string) : float32 = Ui.textWidth ui 11.0f text + 12.0f

    let view (ui: Ui<'m>) (text: string) (bg: Color) (fg: Color) : Scene =
        Group [ rectangle { bounds 0.0f 0.0f (width ui text) 18.0f; radius 6.0f; fill bg }
                Scene.translate 6.0f 2.0f (Text(text, Ui.textStyle ui 11.0f fg)) ]

/// The rows of an inspector: the property widgets of an editor, made once for every editor (the widget editor, the newsletter designer).  Each is a pure function of the value it shows and
/// the callback that says what to do with a new one, built from the widgets of this toolkit, so the app keeps the state and the keyboard and the screen reader work as they do on those.
///
///   let range = Properties.range 8.0f 96.0f 1.0f |> Properties.unit "px"
///   Properties.number ui "fontsize" "Font size" range node.FontSize (fun v -> send (SetFontSize v))
///
/// A row is `Width` pixels wide.  An `id` is the base of the ids of its controls ("fontsize-minus", "fontsize-plus"), so it must be one of its own.
module Properties =
    let Width = 220.0f

    /// What a number may be: its limits, the step it moves in, the unit shown after it, and the decimals shown.
    type Range = { Min: float32; Max: float32; Step: float32; Unit: string; Decimals: int }

    let range (min: float32) (max: float32) (step: float32) : Range = { Min = min; Max = max; Step = step; Unit = ""; Decimals = 0 }
    let unit (u: string) (r: Range) : Range = { r with Unit = u }
    let decimals (d: int) (r: Range) : Range = { r with Decimals = d }

    /// The nearest step of the range, inside it.
    let snap (r: Range) (v: float32) : float32 =
        let c = Scalar.clamp r.Min r.Max v
        if r.Step <= 0.0f then c else Scalar.clamp r.Min r.Max (r.Min + float32 (int ((c - r.Min) / r.Step + 0.5f)) * r.Step)

    /// The value as it is shown, with its unit.
    let show (r: Range) (v: float32) : string =
        let n = match r.Decimals with 0 -> sprintf "%d" (int (v + (if v < 0.0f then -0.5f else 0.5f))) | 1 -> sprintf "%.1f" v | _ -> sprintf "%.2f" v
        n + r.Unit

    /// A caption over a control, with the value (when there is one to show) as a chip at the right of the caption.
    let field (ui: Ui<'m>) (caption: string) (value: string) (control: Scene) : Scene =
        let th = ui.Theme
        let head =
            Group [ Label.view ui Label.Caption caption
                    (if value = "" then Empty else Scene.translate (Width - Chip.width ui value) -3.0f (Chip.view ui value th.SurfaceRaised th.Text)) ]
        column { gap 4; child head; child control }

    /// A number on a slider: the value is the chip, a drag or an arrow key moves it in steps.
    let slider (ui: Ui<'m>) (id: string) (caption: string) (r: Range) (value: float32) (onChange: float32 -> unit) : Scene =
        field ui caption (show r value)
            (Slider.viewNamed ui id caption { Min = r.Min; Max = r.Max; Value = value; Width = Width; OnChange = fun v -> onChange (snap r v) })

    /// A number with a button for a step down and one for a step up, and the value between them: for a number that is read as a number (a font size, a count).
    let number (ui: Ui<'m>) (id: string) (caption: string) (r: Range) (value: float32) (onChange: float32 -> unit) : Scene =
        let th = ui.Theme
        let step (suffix: string) (label: string) (delta: float32) =
            Button.view ui (id + suffix)
                { Button.make label (fun () -> onChange (snap r (value + delta))) with
                    Variant = Button.Secondary; Width = ValueSome 32.0f
                    Disabled = (delta < 0.0f && value <= r.Min) || (delta > 0.0f && value >= r.Max) }
        let shown = show r value
        let w = Width - 2.0f * (32.0f + 8.0f)
        let box =
            Semantic({ Semantics.create "" Role.StaticText (caption + " " + shown) with Value = shown },
                     Group [ rectangle { bounds 0.0f 0.0f w th.ControlHeight; radius th.Radius; fill th.Background; stroke th.Border 1 }
                             Scene.translate ((w - Ui.textWidth ui th.FontSize shown) * 0.5f) ((th.ControlHeight - th.FontSize * 1.2f) * 0.5f)
                                 (Text(shown, Ui.textStyle ui th.FontSize th.Text)) ])
        field ui caption "" (row { gap 8; child (step "-minus" "-" (-r.Step)); child box; child (step "-plus" "+" r.Step) })

    /// A row of small buttons that set the value (the angles 0, 90, 180, 270; the sizes 12, 16, 24): the one that is the value is the primary one.
    let presets (ui: Ui<'m>) (id: string) (r: Range) (values: float32 list) (value: float32) (onPick: float32 -> unit) : Scene =
        let n = values.Length
        let w = (Width - 4.0f * float32 (n - 1)) / float32 n
        row { gap 4
              children (values |> List.mapi (fun i v ->
                  Button.view ui (id + "-preset" + string i)
                      { Button.make (show r v) (fun () -> onPick v) with
                          Variant = (if abs (v - value) < 0.5f * (if r.Step > 0.0f then r.Step else 1.0f) then Button.Primary else Button.Secondary)
                          Width = ValueSome w })) }

    /// An angle in degrees: a slider over the turn and the right angles as presets.
    let angle (ui: Ui<'m>) (id: string) (caption: string) (value: float32) (onChange: float32 -> unit) : Scene =
        let r = range 0.0f 360.0f 1.0f |> unit "°"
        column { gap 6
                 child (slider ui id caption r value onChange)
                 child (presets ui id r [ 0.0f; 90.0f; 180.0f; 270.0f ] value onChange) }

    /// One of a few named choices (an alignment, a weight): a segmented row of buttons, the chosen one is the primary one.
    let choice (ui: Ui<'m>) (id: string) (caption: string) (options: string list) (selected: int) (onSelect: int -> unit) : Scene =
        let n = options.Length
        let w = (Width - 4.0f * float32 (n - 1)) / float32 n
        field ui caption ""
            (row { gap 4
                   children (options |> List.mapi (fun i o ->
                       Button.view ui (id + "-" + string i)
                           { Button.make o (fun () -> onSelect i) with Variant = (if i = selected then Button.Primary else Button.Secondary); Width = ValueSome w })) })

    /// A switch with the caption as its label.
    let toggle (ui: Ui<'m>) (id: string) (caption: string) (value: bool) (onChange: bool -> unit) : Scene = Toggle.view ui id caption value onChange

    /// A line of text: the app takes the keys (a text field shows its text and the focus, it does not edit).
    let text (ui: Ui<'m>) (id: string) (caption: string) (value: string) (placeholder: string) : Scene =
        field ui caption "" (TextField.view ui id { Value = value; Placeholder = placeholder; Width = Width })

    /// The colours of the theme that make sense for text and shapes, with names for the screen reader.
    let themePalette (ui: Ui<'m>) : (string * Color) list =
        let th = ui.Theme
        [ "Text", th.Text; "Muted", th.TextMuted; "Accent", th.Accent; "Success", th.Success; "Danger", th.Danger; "Border", th.Border; "Control", th.Control ]

    /// A colour out of a palette of at most eight, as squares with the chosen one ringed; each is a button of its own with the colour's name.
    let color (ui: Ui<'m>) (id: string) (caption: string) (palette: (string * Color) list) (selected: int) (onSelect: int -> unit) : Scene =
        let th = ui.Theme
        let side = Ui.MinTarget
        let swatch (i: int) (name: string, c: Color) =
            let sid = id + "-" + string i
            let chosen = i = selected
            let body =
                Group [ rectangle { bounds 0.0f 0.0f side side; radius 6.0f; fill c; stroke (if chosen then th.Focus else th.Control) (if chosen then 2.5f else 1.0f) }
                        Ui.focusRing ui sid side side 6.0f ]
            Ui.semantic ui sid Role.Button name (fun s -> { s with Checked = ValueSome chosen })
                (fun a -> match a with SemanticAction.Activate -> onSelect i | _ -> ())
                (Ui.interactive ui sid (fun () -> onSelect i) body)
        let shown = match List.tryItem selected palette with Some (name, _) -> name | None -> ""
        field ui caption shown (row { gap 3; children (palette |> List.mapi swatch) })

    /// A group of properties under a heading.
    let section (ui: Ui<'m>) (title: string) (rows: Scene list) : Scene =
        column { gap 12
                 child (Label.view ui Label.Heading title)
                 children rows }
