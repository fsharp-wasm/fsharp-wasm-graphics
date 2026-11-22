module FSharp.Graphics.WidgetTests.Program

open System
open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Scene
open FSharp.Graphics.SceneDsl
open FSharp.Graphics.Widgets

// ── mini harness ───────────────────────────────────────────────────────────
let mutable passed = 0
let failures = ResizeArray<string>()
let check (name: string) (ok: bool) =
    if ok then passed <- passed + 1 else failures.Add name; printfn "  FAIL  %s" name
let section (s: string) = printfn "%s" s

// ── 1. contrast (WCAG 1.4.3 text 4.5 : 1, 1.4.11 components 3 : 1) ────────
let channel (c: uint8) =
    let v = float c / 255.0
    if v <= 0.03928 then v / 12.92 else Math.Pow((v + 0.055) / 1.055, 2.4)
let luminance (c: Color) = 0.2126 * channel c.R + 0.7152 * channel c.G + 0.0722 * channel c.B
let contrast (a: Color) (b: Color) =
    let la, lb = luminance a, luminance b
    (max la lb + 0.05) / (min la lb + 0.05)

let contrastTests () =
    section "Theme contrast"
    for name, (t: Theme) in [ "dark", Theme.dark; "light", Theme.light ] do
        let surfaces = [ "Background", t.Background; "Surface", t.Surface; "SurfaceRaised", t.SurfaceRaised ]
        let text (what: string) (fg: Color) (bg: Color) = check (sprintf "%s: %s %.2f : 1 (4.5)" name what (contrast fg bg)) (contrast fg bg >= 4.5)
        let part (what: string) (fg: Color) (bg: Color) = check (sprintf "%s: %s %.2f : 1 (3)" name what (contrast fg bg)) (contrast fg bg >= 3.0)
        for sname, s in surfaces do
            text (sprintf "Text on %s" sname) t.Text s
            text (sprintf "TextMuted on %s" sname) t.TextMuted s
            part (sprintf "Control on %s" sname) t.Control s
            part (sprintf "Focus on %s" sname) t.Focus s
            part (sprintf "Indicator on %s" sname) t.Indicator s
        // the label of a filled button in every state
        for what, fill in [ "Accent", t.Accent; "AccentHover", t.AccentHover; "AccentPressed", t.AccentPressed; "Danger", t.Danger
                            "Danger hover", hex 0xB91C1C; "Danger pressed", hex 0x991B1B ] do
            text (sprintf "OnAccent on %s" what) t.OnAccent fill
        // the label of the other variants on their fills
        for what, fill in [ "Surface", t.Surface; "SurfaceRaised (hover)", t.SurfaceRaised; "Border (pressed)", t.Border ] do
            text (sprintf "Text on %s" what) t.Text fill
        // the knob of a toggle that is off, on its track
        part "toggle knob off on SurfaceRaised" t.Control t.SurfaceRaised
        // a combo box row under the pointer
        text "OnAccent on Accent (menu row)" t.OnAccent t.Accent

// ── 2. a UI to build widgets with ──────────────────────────────────────────
let mutable state = UiState.empty
let mkUi () : Ui<UiMsg> =
    { Theme = Theme.dark; Text = TextEngine.approximate; State = state; Time = 0.0f; Lift = id
      Send = fun m -> state <- UiState.update state m }
let reset () = state <- UiState.empty

let nodes (scene: Scene) = SemanticTree.walk scene
let find (id: string) (scene: Scene) = nodes scene |> Array.find (fun s -> s.Id = id)
let resolved (scene: Scene) = Resolve.resolve TextEngine.approximate 10000.0f scene
let size (scene: Scene) = match Resolve.bounds (resolved scene) with Some b -> b | None -> Rect.zero

let semanticsTests () =
    section "Semantics of the widgets"
    reset ()
    let ui = mkUi ()
    let clicked = ref 0
    let btn = Button.view ui "b" (Button.make "Save" (fun () -> clicked.Value <- clicked.Value + 1))
    let s = find "b" btn
    check "button: role Button, name is the label" (s.Role = Role.Button && s.Name = "Save")
    check "button: focusable, not disabled" (s.Focusable && not s.Disabled)
    s.Act SemanticAction.Activate
    check "button: Activate runs OnClick" (clicked.Value = 1)
    let off = Button.view ui "d" { Button.make "Off" ignore with Disabled = true }
    check "button: disabled is reported" ((find "d" off).Disabled)

    let toggled = ref false
    let cb = CheckBox.view ui "c" "Remember me" true (fun v -> toggled.Value <- v)
    let c = find "c" cb
    check "check box: role, name and checked state" (c.Role = Role.CheckBox && c.Name = "Remember me" && c.Checked = ValueSome true)
    c.Act SemanticAction.Activate
    check "check box: Activate flips the value" (toggled.Value = false)
    let tg = Toggle.view ui "t" "Notifications" false ignore
    check "toggle: role Switch, unchecked" ((find "t" tg).Role = Role.Switch && (find "t" tg).Checked = ValueSome false)

    let value = ref 0.5f
    let sl = Slider.viewNamed ui "s" "Volume" { Min = 0.0f; Max = 1.0f; Value = 0.5f; Width = 200.0f; OnChange = fun v -> value.Value <- v }
    let sn = find "s" sl
    check "slider: role, name, range and value" (sn.Role = Role.Slider && sn.Name = "Volume" && sn.Min = 0.0f && sn.Max = 1.0f && sn.Now = 0.5f && sn.Value = "50%")
    sn.Act SemanticAction.Increment
    check "slider: Increment is 5 % of the range" (abs (value.Value - 0.55f) < 1e-4f)
    sn.Act SemanticAction.Last
    check "slider: Last is the maximum" (value.Value = 1.0f)

    let tf = TextField.viewNamed ui "f" "Name" (fun _ -> ()) { Value = "Ada"; Placeholder = "Your name"; Width = 200.0f }
    let fn = find "f" tf
    check "text field: role TextBox, name is the label, not the placeholder" (fn.Role = Role.TextBox && fn.Name = "Name" && fn.Value = "Ada")

    let dd = Dropdown.viewNamed ui "v" "Variant" { Options = [ "A"; "B" ]; Selected = 1; Open = true; Width = 160.0f; OnToggle = ignore; OnSelect = ignore }
    let all = nodes dd
    check "combo box: role, value and expanded state" (let d = all |> Array.find (fun s -> s.Id = "v") in d.Role = Role.ComboBox && d.Value = "B" && d.Expanded = ValueSome true)
    check "combo box: an open menu lists its options, the selected one is marked"
        (all |> Array.filter (fun s -> s.Role = Role.Option) |> Array.map (fun s -> s.Name, s.Selected) = [| "A", false; "B", true |])

    check "label: a heading is a heading, a body text is static text"
        ((nodes (Label.view ui Label.Heading "Inspector")).[0].Role = Role.Heading 2 && (nodes (Label.view ui Label.Body "Hello")).[0].Role = Role.StaticText)
    check "progress: role, name and value" (let p = (nodes (ProgressBar.viewNamed ui "Upload" 200.0f 0.25f)).[0] in p.Role = Role.ProgressBar && p.Name = "Upload" && p.Value = "25%")

let placementTests () =
    section "Placement and size of the targets"
    reset ()
    let ui = mkUi ()
    for what, scene in [ "check box", CheckBox.view ui "c" "Remember me" false ignore
                         "toggle", Toggle.view ui "t" "On" false ignore
                         "slider", Slider.viewNamed ui "s" "V" { Min = 0.0f; Max = 1.0f; Value = 0.5f; Width = 200.0f; OnChange = ignore }
                         "button", Button.view ui "b" (Button.make "Save" ignore) ] do
        let b = size scene
        check (sprintf "%s is at least 24 x 24 (WCAG 2.5.8): %.0f x %.0f" what b.W b.H) (b.H >= 24.0f && b.W >= 24.0f)
    // the place of an element after the transforms around it
    let placed = Scene.translate 100.0f 50.0f (Button.view ui "b" (Button.make "Save" ignore))
    let all = SemanticTree.collect (resolved placed)
    let b = all.[0].Bounds
    check "collect: a button translated by (100, 50) is drawn there" (abs (b.X - 100.0f) < 0.01f && abs (b.Y - 50.0f) < 0.01f && b.W > 0.0f && b.H > 0.0f)
    check "collect: an element in a clip region that hides it is not visible"
        (let hidden = Clip(Rect.create 0.0f 0.0f 10.0f 10.0f, Scene.translate 100.0f 100.0f (Button.view ui "b" (Button.make "Save" ignore)))
         not (SemanticTree.collect (resolved hidden)).[0].Visible)
    check "collect: children follow their parent, siblings keep the scene's order"
        (let sc = Group [ Semantic(Semantics.create "list" Role.List "Items", Group [ Button.view ui "a" (Button.make "A" ignore); Button.view ui "b" (Button.make "B" ignore) ]) ]
         let n = SemanticTree.collect (resolved sc)
         n.Length = 3 && n.[0].Parent = -1 && n.[1].Parent = 0 && n.[2].Parent = 0 && n.[1].Info.Name = "A" && n.[2].Info.Name = "B")
    check "a Role.None element hides everything inside it, from the tab order too"
        (let sc = Group [ Semantic(Semantics.create "" Role.None "", Button.view ui "picture" (Button.make "A picture" ignore)); Button.view ui "real" (Button.make "Real" ignore) ]
         let names = SemanticTree.walk sc |> Array.map (fun s -> s.Name)
         let collected = SemanticTree.collect (resolved sc) |> Array.map (fun n -> n.Info.Name)
         names = [| "Real" |] && collected = [| "Real" |] && (SemanticTree.focusOrder (SemanticTree.walk sc)).Length = 1)
    check "collect: Order puts a sibling first"
        (let sc = Group [ Semantic({ Semantics.create "late" Role.Button "Late" with Order = 1 }, Empty); Semantic({ Semantics.create "early" Role.Button "Early" with Order = 0 }, Empty) ]
         (SemanticTree.walk sc |> Array.map (fun s -> s.Name)) = [| "Early"; "Late" |])

// ── 3. the keyboard ────────────────────────────────────────────────────────
let keyboardTests () =
    section "Keyboard"
    let keys = ResizeArray<string>()
    let value = ref 0.5f
    let clicks = ref 0
    let build () =
        let ui = mkUi ()
        let page =
            Group [
                Button.view ui "one" (Button.make "One" (fun () -> clicks.Value <- clicks.Value + 1))
                CheckBox.view ui "two" "Two" false ignore
                Slider.viewNamed ui "three" "Three" { Min = 0.0f; Max = 1.0f; Value = value.Value; Width = 200.0f; OnChange = fun v -> value.Value <- v }
                Button.view ui "off" { Button.make "Disabled" ignore with Disabled = true }
            ]
        Ui.root ui (fun k -> keys.Add k) page
    let press (key: string) =
        match build () with
        | Reactive(h, _) -> h (KeyDown key) |> ignore
        | _ -> ()
    reset ()
    press "Tab"
    check "Tab focuses the first focusable widget" (state.Focus = Some "one")
    press "Tab"
    press "Tab"
    check "Tab walks the order and arrives at the slider" (state.Focus = Some "three")
    press "Tab"
    check "Tab wraps around, a disabled widget is skipped" (state.Focus = Some "one")
    press "Shift" |> ignore
    reset ()
    // Shift+Tab goes backwards
    (match build () with Reactive(h, _) -> h (KeyDown "ShiftLeft") |> ignore | _ -> ())
    press "Tab"
    check "Shift+Tab from nowhere starts at the last focusable widget" (state.Focus = Some "three")
    (match build () with Reactive(h, _) -> h (KeyUp "ShiftLeft") |> ignore | _ -> ())
    state <- { state with Focus = Some "one" }
    press "Enter"
    press "Space"
    check "Enter and Space activate the focused button" (clicks.Value = 2)
    press " "
    check "a browser's \" \" is Space" (clicks.Value = 3)
    state <- { state with Focus = Some "three" }
    press "ArrowRight"
    check "ArrowRight on a slider steps up (a browser's name)" (abs (value.Value - 0.55f) < 1e-4f)
    press "Left"
    press "Left"
    check "Left steps down" (abs (value.Value - 0.45f) < 1e-4f)
    press "End"
    check "End jumps to the maximum" (value.Value = 1.0f)
    press "Home"
    check "Home jumps to the minimum" (value.Value = 0.0f)
    check "the keys the toolkit handled did not reach the application" (keys.Count = 0)
    press "q"
    check "other keys reach the application" (keys.Count = 1 && keys.[0] = "q")
    state <- { state with Focus = Some "two" }
    press "ArrowLeft"
    check "arrows on a check box are the application's (not a slider)" (keys.Count = 2 && keys.[1] = "Left")

// ── 5. a widget inside a scroll region ─────────────────────────────────────
let scrollTests () =
    section "A widget inside a ScrollView"
    reset ()
    let clicks = ref 0
    let ui = mkUi ()
    let inner = Button.view ui "inner" (Button.make "Inner" (fun () -> clicks.Value <- clicks.Value + 1))
    let scene = ScrollView.viewNamed ui "scroll" "Content" { Width = 200.0f; Height = 100.0f; Offset = 0.0f; OnScroll = ignore } inner
    let p = Vec2.create 20.0f 15.0f                                   // on the button
    // the router: the button is under the pointer, so it takes the press and the release; the region around it did not take them from it
    let d = Hit.Dispatcher()
    d.Handle(scene, PointerMove p) |> ignore
    d.Handle(scene, PointerDown(p, 0)) |> ignore
    d.Handle(scene, PointerUp(p, 0)) |> ignore
    check "a button in a ScrollView can be clicked" (clicks.Value = 1)
    check "a press on it focuses the button, not the region" (state.Focus = Some "inner")
    // where nothing of the content is, the region itself answers (a press focuses it)
    reset ()
    let q = Vec2.create 150.0f 80.0f
    d.Handle(scene, PointerMove q) |> ignore
    d.Handle(scene, PointerDown(q, 0)) |> ignore
    d.Handle(scene, PointerUp(q, 0)) |> ignore
    check "a press on the empty part of the region focuses the region" (state.Focus = Some "scroll")

/// A host that draws only what changed asks the scene whether it changes with the clock: only a caret that blinks does, and only while its field has the focus.
let clockTests () =
    section "What changes with the clock"
    reset ()
    let ui = mkUi ()
    let field (focused: bool) =
        let ui = if focused then { ui with State = { ui.State with Focus = Some "f" } } else ui
        TextField.viewNamed ui "f" "Name" ignore { Value = "Ada"; Placeholder = ""; Width = 200.0f }
    check "a text field that has no focus does not depend on the clock" (not (isAnimating 0.0f (field false)))
    check "a text field that has the focus blinks, whenever one looks" (isAnimating 0.0f (field true) && isAnimating 987654.0f (field true))
    check "a button and a label stand still" (not (isAnimating 0.0f (Group [ Button.view ui "b" (Button.make "Save" ignore); Label.view ui Label.Body "Hello" ])))
    check "a finite animation asks for frames until it has run, then no more"
        (let a = Animated(Transition.linear 500, Empty)
         isAnimating 100.0f a && not (isAnimating 500.0f a))
    check "an animation inside a transform, a clip, an opacity and a mask is found"
        (let a = Animated(Transition.loop (Transition.linear 500), Empty)
         [ Transform(Mat3x2.identity, a); Clip(Rect.create 0.0f 0.0f 1.0f 1.0f, a); Opacity(0.5f, a); Mask(Empty, a); Mask(a, Empty); Group [ Empty; Padding(Insets.zero, a) ] ]
         |> List.forall (isAnimating 0.0f))

[<EntryPoint>]
let main _ =
    contrastTests ()
    semanticsTests ()
    placementTests ()
    keyboardTests ()
    scrollTests ()
    clockTests ()
    printfn ""
    if failures.Count = 0 then printfn "%d passed, 0 failed" passed; 0
    else printfn "%d passed, %d failed" passed failures.Count; 1
