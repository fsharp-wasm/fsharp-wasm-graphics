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

/// What the pointer shows over a widget, at the middle of it.
let cursorOver (scene: Scene) : CursorKind =
    let r = resolved scene
    let b = match Resolve.bounds r with Some b -> b | None -> Rect.zero
    let d = Hit.Dispatcher()
    d.Handle(r, PointerMove(Vec2.create (b.X + b.W * 0.5f) (b.Y + b.H * 0.5f))) |> ignore
    d.Cursor

let cursorTests () =
    section "The pointer over the widgets"
    reset ()
    let ui = mkUi ()
    check "a button shows the pointing hand" (cursorOver (Button.view ui "b" (Button.make "Save" ignore)) = CursorKind.Pointer)
    check "a button that is off shows that it is" (cursorOver (Button.view ui "d" { Button.make "Off" ignore with Disabled = true }) = CursorKind.NotAllowed)
    check "a check box and a toggle show the pointing hand" (cursorOver (CheckBox.view ui "c" "Remember me" false ignore) = CursorKind.Pointer && cursorOver (Toggle.view ui "t" "On" false ignore) = CursorKind.Pointer)
    check "a text field shows the text cursor" (cursorOver (TextField.viewNamed ui "f" "Name" ignore { Value = "Ada"; Placeholder = ""; Width = 200.0f }) = CursorKind.Text)
    check "a slider shows the hand that grabs" (cursorOver (Slider.viewNamed ui "s" "Volume" { Min = 0.0f; Max = 1.0f; Value = 0.5f; Width = 200.0f; OnChange = ignore }) = CursorKind.Grab)
    check "a label shows nothing in particular" (cursorOver (Label.view ui Label.Body "Hello") = CursorKind.Default)

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
    check "a caret sleeps between its steps: it asks to be drawn again when the next half second begins, never 'now'"
        (let d t = animationDelay t (field true)
         d 0.0f = ValueSome 500.0f && d 100.0f = ValueSome 400.0f && d 500.0f = ValueSome 500.0f && d 1234.0f = ValueSome 266.0f)
    check "an animation that moves continuously asks for a frame every time; with a caret next to it, too"
        (animationDelay 100.0f (Group [ field true; Animated(Transition.loop (Transition.linear 300), Empty) ]) = ValueSome 0.0f)
    check "a scene that stands still has no delay" (animationDelay 100.0f (field false) = ValueNone)
    check "a finite animation asks for frames until it has run, then no more"
        (let a = Animated(Transition.linear 500, Empty)
         isAnimating 100.0f a && not (isAnimating 500.0f a))
    check "an animation inside a transform, a clip, an opacity and a mask is found"
        (let a = Animated(Transition.loop (Transition.linear 500), Empty)
         [ Transform(Mat3x2.identity, a); Clip(Rect.create 0.0f 0.0f 1.0f 1.0f, a); Opacity(0.5f, a); Mask(Empty, a); Mask(a, Empty); Group [ Empty; Padding(Insets.zero, a) ] ]
         |> List.forall (isAnimating 0.0f))

// ── 7. tabs ────────────────────────────────────────────────────────────────
let tabsTests () =
    section "Tabs"
    reset ()
    let ui = mkUi ()
    let chosen = ref ""
    let closed = ref ""
    let tab (id: string) (title: string) (closable: bool) : Tabs.Tab = { Id = id; Title = title; Closable = closable }
    let tabs = [ tab "pivot" "Pivot" false; tab "e1" "Entries 4010" true; tab "e2" "Entries 6200" true ]
    let props = { Tabs.make tabs "e1" (fun id -> chosen.Value <- id) with OnClose = fun id -> closed.Value <- id }
    let strip = Tabs.viewNamed ui "tabs" "Open tables" props
    let all = nodes strip
    check "tabs: the strip is a tab list with its name" (all |> Array.exists (fun s -> s.Id = "tabs" && s.Role = Role.TabList && s.Name = "Open tables"))
    let tabNodes = all |> Array.filter (fun s -> s.Role = Role.Tab)
    check "tabs: every tab is a tab, named by its title" (tabNodes |> Array.map (fun s -> s.Name) = [| "Pivot"; "Entries 4010"; "Entries 6200" |])
    check "tabs: exactly the selected one is selected, and only it is in the order of Tab" (tabNodes |> Array.map (fun s -> s.Selected, s.Focusable) = [| false, false; true, true; false, false |])
    check "tabs: a tab that can be closed has a button named after it" (all |> Array.filter (fun s -> s.Role = Role.Button) |> Array.map (fun s -> s.Name) = [| "Close Entries 4010"; "Close Entries 6200" |])
    (find "tabs/e2" strip).Act SemanticAction.Activate
    check "tabs: Activate selects the tab" (chosen.Value = "e2")
    (find "tabs/e1" strip).Act SemanticAction.Increment
    check "tabs: the right arrow goes to the next" (chosen.Value = "e2")
    (find "tabs/e1" strip).Act SemanticAction.Decrement
    check "tabs: the left arrow goes to the previous" (chosen.Value = "pivot")
    chosen.Value <- ""
    (find "tabs/e1" strip).Act SemanticAction.First
    check "tabs: Home goes to the first" (chosen.Value = "pivot")
    (find "tabs/e1" strip).Act SemanticAction.Last
    check "tabs: End goes to the last" (chosen.Value = "e2")
    chosen.Value <- ""
    (find "tabs/e1" (Tabs.viewNamed ui "tabs" "t" (Tabs.make [ tabs.[0]; tabs.[1] ] "e1" (fun id -> chosen.Value <- id)))).Act SemanticAction.Increment
    check "tabs: the right arrow on the last tab stays where it is" (chosen.Value = "")
    (find "tabs/close/e2" strip).Act SemanticAction.Activate
    check "tabs: the close button closes its tab" (closed.Value = "e2")
    let placed = Tabs.layout ui tabs
    check "tabs: tabs follow each other without gaps" (placed |> List.pairwise |> List.forall (fun ((_, x, w), (_, x2, _)) -> abs (x + w - x2) < 0.01f))
    check "tabs: a tab with a close button is wider than the same title without" (let (_, _, w) = placed.[1] in w > Ui.textWidth ui ui.Theme.FontSize "Entries 4010" + 2.0f * ui.Theme.PadX)
    // a click on the second tab, and on the first one's neighbour
    let d = Hit.Dispatcher()
    let (_, x, w) = placed.[2]
    let p = Vec2.create (x + 12.0f) 10.0f
    d.Handle(strip, PointerMove p) |> ignore
    d.Handle(strip, PointerDown(p, 0)) |> ignore
    d.Handle(strip, PointerUp(p, 0)) |> ignore
    check "tabs: a click on a tab selects it" (chosen.Value = "e2")
    check "tabs: a tab is hit-tested by its own area (the title's area has its width)" (w > 40.0f)
    // the arrows reach a focused tab through the toolkit's key handling
    reset ()
    chosen.Value <- ""
    let ui2 = { mkUi () with State = { UiState.empty with Focus = Some "tabs/e1" } }
    let stripFocused = Tabs.viewNamed ui2 "tabs" "Open tables" props
    let delivered = ref ""
    let root = Ui.root ui2 (fun k -> delivered.Value <- k) stripFocused
    (match root with Reactive(h, _) -> h (KeyDown "ArrowRight") |> ignore | _ -> ())
    check "tabs: the right arrow of a browser moves the focused tab to the next" (chosen.Value = "e2" && delivered.Value = "")

// ── 8. the grid ────────────────────────────────────────────────────────────
let gridFrame : Grid.Frame = { Rows = 1000; Cols = 12; Width = 240.0f + 4.0f * 112.0f; Height = 32.0f + 10.0f * 28.0f; Metrics = Grid.metrics }

let gridTests () =
    section "The grid"
    let f = gridFrame
    // 448 x 280 of body: 10 rows and 4 columns are in the window
    let c1 = Grid.clamp f { Grid.start with OffsetX = -5.0f; OffsetY = 1.0e9f }
    check "grid: offsets are held inside the table" (c1.OffsetX = 0.0f && c1.OffsetY = 1000.0f * 28.0f - 280.0f)
    check "grid: a table smaller than its window does not scroll" (let g = { f with Rows = 3; Cols = 2 } in let c = Grid.clamp g { Grid.start with OffsetX = 50.0f; OffsetY = 50.0f } in c.OffsetX = 0.0f && c.OffsetY = 0.0f)
    let r500 = Grid.reveal f 500 0 Grid.start
    check "grid: a cell far down is scrolled to by the least that shows it (its bottom at the window's)" (r500.OffsetY = 501.0f * 28.0f - 280.0f)
    check "grid: a cell that is in the window does not scroll" (let s = Grid.reveal f 3 1 Grid.start in s.OffsetX = 0.0f && s.OffsetY = 0.0f)
    check "grid: a cell above the window is scrolled to by its top" ((Grid.reveal f 2 0 { Grid.start with OffsetY = 1000.0f }).OffsetY = 56.0f)
    let h x y = Grid.hit f Grid.start x y
    check "grid: the body, the headers and the corner are told apart" (h 300.0f 50.0f = Grid.InBody(0, 0) && h 20.0f 50.0f = Grid.InRowHeader 0 && h 300.0f 10.0f = Grid.InColHeader 0 && h 10.0f 10.0f = Grid.Corner)
    check "grid: the cell under a point follows the scroll" (Grid.hit f { Grid.start with OffsetX = 112.0f; OffsetY = 56.0f } 245.0f 40.0f = Grid.InBody(2, 1))
    check "grid: outside the window, and past the last column, is nothing" (h -1.0f 5.0f = Grid.Outside && h 800.0f 50.0f = Grid.Outside && Grid.hit { f with Cols = 2 } Grid.start 600.0f 50.0f = Grid.Outside)
    let key name s = Grid.key f false name s
    let sel r c = { Grid.start with Selected = Some(r, c) }
    check "grid: a first arrow key selects the first cell" (match key "Down" Grid.start with Grid.Moved s -> s.Selected = Some(0, 0) | _ -> false)
    check "grid: the arrows move the selection by one, and stay at the edges"
        ((match key "Down" (sel 3 3) with Grid.Moved s -> s.Selected = Some(4, 3) | _ -> false)
         && (match key "Up" (sel 0 3) with Grid.Moved s -> s.Selected = Some(0, 3) | _ -> false)
         && (match key "Right" (sel 3 11) with Grid.Moved s -> s.Selected = Some(3, 11) | _ -> false)
         && (match key "Left" (sel 3 0) with Grid.Moved s -> s.Selected = Some(3, 0) | _ -> false))
    check "grid: a page is the window's height less a row" (match key "PageDown" (sel 3 0) with Grid.Moved s -> s.Selected = Some(12, 0) | _ -> false)
    check "grid: Home and End go to the ends of the row" ((match key "Home" (sel 5 7) with Grid.Moved s -> s.Selected = Some(5, 0) | _ -> false) && (match key "End" (sel 5 7) with Grid.Moved s -> s.Selected = Some(5, 11) | _ -> false))
    check "grid: moving the selection out of the window scrolls it into view" (match key "Down" (sel 9 0) with Grid.Moved s -> s.OffsetY = 11.0f * 28.0f - 280.0f | _ -> false)
    check "grid: Enter opens the cell, Space folds the row, Shift and Space the column"
        (key "Enter" (sel 4 2) = Grid.Open(4, 2) && key "Space" (sel 4 2) = Grid.ToggleRow 4 && Grid.key f true "Space" (sel 4 2) = Grid.ToggleCol 2)
    check "grid: other keys, and any key on an empty table, are left alone" (key "x" (sel 1 1) = Grid.Ignored && Grid.key { f with Rows = 0 } false "Down" Grid.start = Grid.Ignored)

    // the widget: only what is in the window is asked for
    reset ()
    let ui = mkUi ()
    let asked = ref 0
    let rowsAsked = ref 0
    let log = ref ""
    let shown = ref Grid.start
    let props (st: Grid.State) : Grid.Props<UiMsg> =
        { Frame = f; State = st; Corner = "Account / Period"
          Row = fun r -> rowsAsked.Value <- rowsAsked.Value + 1; { Label = (if r % 10 = 0 then "Group " + string r else "Row " + string r); Level = (if r % 10 = 0 then 0 else 1); Group = (r % 10 = 0); Open = false }
          Col = fun c -> { Label = "Col " + string c; Level = 0; Group = (c = 0); Open = true }
          Cell = fun r c -> asked.Value <- asked.Value + 1; { Text = string (r * 100 + c); Muted = (c = 3); Left = false }
          OnState = fun s -> shown.Value <- s
          OnToggleRow = fun r -> log.Value <- "row " + string r
          OnToggleCol = fun c -> log.Value <- "col " + string c
          OnOpen = fun r c -> log.Value <- sprintf "open %d,%d" r c }
    let scene = Grid.view ui "grid" "Ledger" (props (sel 4 2))
    check "grid: exactly the cells of the window are asked for (10 rows of 4 columns), and the selected one for its description, whatever the size of the table" (asked.Value = 10 * 4 + 1)
    check "grid: the rows of the window are asked for once, and the selected one for the description" (rowsAsked.Value = 10 + 1)
    let g = find "grid" scene
    check "grid: a region of its own, named, with the selected cell as its value" (g.Role = Role.Grid && g.Name = "Ledger" && g.Value = "Row 4, Col 2: 402")
    check "grid: the keys are in its description" (g.Description.Contains "Arrow keys")
    g.Act SemanticAction.Activate
    check "grid: Activate opens the selected cell" (log.Value = "open 4,2")
    // a pointer on the grid
    let d = Hit.Dispatcher()
    let at (x: float32) (y: float32) = Vec2.create x y
    let click (x: float32) (y: float32) =
        d.Handle(scene, PointerMove(at x y)) |> ignore
        d.Handle(scene, PointerDown(at x y, 0)) |> ignore
        d.Handle(scene, PointerUp(at x y, 0)) |> ignore
    click 300.0f 50.0f
    check "grid: a click on a cell selects it" (shown.Value.Selected = Some(0, 0))
    click 300.0f 50.0f
    reset ()
    let scene2 = Grid.view (mkUi ()) "grid" "Ledger" (props { Grid.start with Selected = Some(0, 0) })
    d.Handle(scene2, PointerMove(at 300.0f 50.0f)) |> ignore
    d.Handle(scene2, PointerDown(at 300.0f 50.0f, 0)) |> ignore
    check "grid: a click on the cell that is selected opens it" (log.Value = "open 0,0")
    click 20.0f 50.0f
    check "grid: a click on a row that is a total folds or opens it (row 0 is one)" (log.Value = "row 0")
    click 20.0f 80.0f
    check "grid: a click on a row that is not a total selects it in the column that is selected" (shown.Value.Selected = Some(1, 2))
    click 300.0f 10.0f
    check "grid: a click on the header of a column that is a total folds or opens it (column 0 is one)" (log.Value = "col 0")
    // the wheel
    d.Handle(scene, PointerMove(at 300.0f 100.0f)) |> ignore
    d.Handle(scene, Scroll(at 0.0f 3.0f)) |> ignore
    check "grid: the wheel up at the top stays at the top" (shown.Value.OffsetY = 0.0f)
    d.Handle(scene, Scroll(at 0.0f -3.0f)) |> ignore
    check "grid: the wheel down (negative Y in notches) scrolls the table down, 36 px a notch" (shown.Value.OffsetY = 108.0f)
    d.Handle(scene, Scroll(at 1.0f 0.0f)) |> ignore
    check "grid: the wheel to the right scrolls toward the last column" (shown.Value.OffsetX = 36.0f)
    d.Handle(scene, Scroll(at 0.0f -1.0f)) |> ignore
    check "grid: a notch down is 36 px" (shown.Value.OffsetY = 36.0f)
    // an offset far down asks for rows far down
    asked.Value <- 0
    let far = Grid.view (mkUi ()) "grid" "Ledger" (props { Grid.start with OffsetY = 500.0f * 28.0f })
    check "grid: scrolled far down it still asks for a window's worth" (asked.Value = 10 * 4)
    check "grid: and says what it shows" ((find "grid" (Grid.view (mkUi ()) "grid" "Ledger" (props { Grid.start with OffsetY = 500.0f * 28.0f; Selected = Some(502, 1) }))).Value = "Row 502, Col 1: 50201")

let textTests () =
    section "Typed text"
    // what a browser says for a key is the character, with the layout and Shift applied: all of these were dropped
    let apply shift key = TextEdit.apply shift key ""
    check "text: punctuation as a browser names it is typed (. , - / ; ' = _)" ([ "."; ","; "-"; "/"; ";"; "'"; "="; "_" ] |> List.forall (fun k -> apply false k = k))
    check "text: what Shift made is typed as it is (! ? : \" @ #)" ([ "!"; "?"; ":"; "\""; "@"; "#" ] |> List.forall (fun k -> apply false k = k || apply true k = k))
    check "text: a letter of another layout is typed (ä ö ü ß)" ([ "ä"; "ö"; "ü"; "ß" ] |> List.forall (fun k -> apply false k = k))
    check "text: a key name of a desktop host is still a key (Period, Number1, Space, Backspace)" (apply false "Period" = "." && apply false "Number1" = "1" && apply false "Space" = " " && TextEdit.apply false "Backspace" "ab" = "a")
    check "text: a control character or an unknown key name types nothing" (apply false "\t" = "" && apply false "F5" = "" && apply false "Dead" = "")
    check "text: typed text is appended as the platform made it (Caps Lock, AltGr, accents, the euro sign)" (TextEdit.typed "H" "" = "H" && TextEdit.typed "@" "a" = "a@" && TextEdit.typed "\u20AC" "5" = "5\u20AC" && TextEdit.typed "\u00E9" "caf" = "caf\u00E9")
    check "text: a control character is not text" (TextEdit.typed "\n" "a" = "a" && TextEdit.typed "" "a" = "a")
    let keys = ResizeArray<string>()
    let texts = ResizeArray<string>()
    let ui = mkUi ()
    let page = Ui.rootText ui (fun k -> keys.Add k) (fun t -> texts.Add t) (Group [ Label.view ui Label.Caption "x" ])
    let send e = match page with Reactive(h, _) -> h e |> ignore | _ -> ()
    send (TextInput "@"); send (KeyDown "Backspace"); send (TextInput "H"); send (KeyDown "ArrowLeft")
    check "text: rootText hands text to onText and the other keys to onKey" (List.ofSeq texts = [ "@"; "H" ] && List.ofSeq keys = [ "Backspace"; "Left" ])
    let keys2 = ResizeArray<string>()
    let plain = Ui.root ui (fun k -> keys2.Add k) (Group [ Label.view ui Label.Caption "x" ])
    (match plain with Reactive(h, _) -> h (TextInput "a") |> ignore; h (KeyDown "Enter") |> ignore | _ -> ())
    check "text: root does not hear text, only keys" (List.ofSeq keys2 = [ "Enter" ])

let propertiesTests () =
    section "Property widgets"
    let ui = mkUi ()
    let sizeR = Properties.range 8.0f 96.0f 2.0f |> Properties.unit "px"
    check "properties: a value snaps to the step and stays inside the range" (Properties.snap sizeR 9.4f = 10.0f && Properties.snap sizeR 1.0f = 8.0f && Properties.snap sizeR 500.0f = 96.0f && Properties.snap sizeR 8.9f = 8.0f)
    check "properties: a value is shown with its unit and decimals" (Properties.show sizeR 16.0f = "16px" && Properties.show (Properties.range 0.0f 1.0f 0.05f |> Properties.decimals 2) 0.5f = "0.50" && Properties.show (Properties.range -10.0f 10.0f 1.0f) -3.0f = "-3")

    // the number with a step down and a step up
    let got = ResizeArray<float32>()
    let number v = Properties.number ui "fs" "Font size" sizeR v got.Add
    let act (id: string) (scene: Scene) = (find id scene).Act SemanticAction.Activate
    act "fs-plus" (number 16.0f); act "fs-minus" (number 16.0f)
    check "properties: the buttons of a number step it by the step" (List.ofSeq got = [ 18.0f; 14.0f ])
    check "properties: the step down is off at the lowest value, the step up at the highest" ((find "fs-minus" (number 8.0f)).Disabled && not (find "fs-plus" (number 8.0f)).Disabled && (find "fs-plus" (number 96.0f)).Disabled)
    check "properties: the value of a number is read out with its caption" (nodes (number 16.0f) |> Array.exists (fun s -> s.Name = "Font size 16px"))

    // a choice and its segments
    let picked = ResizeArray<int>()
    let align = Properties.choice ui "align" "Align" [ "Left"; "Centre"; "Right" ] 1 picked.Add
    act "align-2" align; act "align-0" align
    check "properties: a segment of a choice picks its index" (List.ofSeq picked = [ 2; 0 ])
    check "properties: every segment is a button with its name" ([ "Left"; "Centre"; "Right" ] |> List.forall (fun n -> nodes align |> Array.exists (fun s -> s.Role = Role.Button && s.Name = n)))

    // a colour of a palette
    let palette = [ "Red", Color.rgba 220uy 38uy 38uy 255uy; "Green", Color.rgba 22uy 163uy 74uy 255uy; "Blue", Color.rgba 37uy 99uy 235uy 255uy ]
    let chosen = ResizeArray<int>()
    let colour = Properties.color ui "tint" "Colour" palette 1 chosen.Add
    act "tint-0" colour; act "tint-2" colour
    check "properties: a swatch picks its colour" (List.ofSeq chosen = [ 0; 2 ])
    check "properties: the chosen swatch says it is chosen, the others do not" ((find "tint-1" colour).Checked = ValueSome true && (find "tint-0" colour).Checked = ValueSome false)
    check "properties: a swatch has the name of its colour and a target of 24 pixels" ((find "tint-0" colour).Name = "Red" && (size (Properties.color ui "t" "c" [ "A", Color.White ] 0 ignore)).H >= 24.0f)

    // an angle: the slider moves in steps of one, the presets are the right angles
    let turns = ResizeArray<float32>()
    let angle = Properties.angle ui "rot" "Rotation" 90.0f turns.Add
    act "rot-preset2" angle; act "rot-preset0" angle
    check "properties: the presets of an angle are the right angles" (List.ofSeq turns = [ 180.0f; 0.0f ])
    (find "rot" angle).Act SemanticAction.Increment
    check "properties: an arrow key moves the slider and the value is a whole degree" (turns.Count = 3 && turns.[2] = Properties.snap (Properties.range 0.0f 360.0f 1.0f) turns.[2] && turns.[2] > 90.0f)
    check "properties: the preset that is the value is the primary button" (nodes angle |> Array.exists (fun s -> s.Name = "90\u00B0"))

    // a section keeps its rows in order under the heading
    let sec = Properties.section ui "Text" [ Properties.toggle ui "bold" "Bold" true ignore; number 12.0f ]
    check "properties: a section has its heading and its rows" (nodes sec |> Array.exists (fun s -> s.Name = "Text") && nodes sec |> Array.exists (fun s -> s.Id = "bold") && nodes sec |> Array.exists (fun s -> s.Id = "fs-plus"))
    check "properties: a row is as wide as the column of the inspector" ((size (Properties.slider ui "w" "Width" sizeR 20.0f ignore)).W <= Properties.Width + 1.0f)
    // a label of its own size
    let big = Label.viewSized ui Label.Body (ValueSome 40.0f) "Hello"
    check "properties: a label of its own font size is taller than the body text" ((size big).H > (size (Label.view ui Label.Body "Hello")).H + 10.0f)

[<EntryPoint>]
let main _ =
    contrastTests ()
    semanticsTests ()
    placementTests ()
    cursorTests ()
    keyboardTests ()
    scrollTests ()
    clockTests ()
    tabsTests ()
    gridTests ()
    textTests ()
    propertiesTests ()
    printfn ""
    if failures.Count = 0 then printfn "%d passed, 0 failed" passed; 0
    else printfn "%d passed, %d failed" passed failures.Count; 1
