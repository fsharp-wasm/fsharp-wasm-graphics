namespace FSharp.Graphics.Widgets

open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Primitives
open FSharp.Graphics.Scene
open FSharp.Graphics.SceneDsl

/// A strip of tabs.  The strip is only the strip: the panel under it is the application's (wrap it in `Tabs.panel`), so that a tab can hold anything, a grid among it.
module Tabs =

    type Tab = { Id: string; Title: string; Closable: bool }

    type Props =
        { Tabs: Tab list
          Selected: string
          /// the line under the strip runs at least this far
          Width: float32
          OnSelect: string -> unit
          OnClose: string -> unit }

    let make (tabs: Tab list) (selected: string) (onSelect: string -> unit) : Props =
        { Tabs = tabs; Selected = selected; Width = 0.0f; OnSelect = onSelect; OnClose = ignore }

    /// The height of the strip.
    let height (ui: Ui<'m>) : float32 = ui.Theme.ControlHeight + 4.0f

    let private closeSize = 18.0f

    let private tabWidth (ui: Ui<'m>) (t: Tab) : float32 =
        let th = ui.Theme
        th.PadX * 2.0f + Ui.textWidth ui th.FontSize t.Title + (if t.Closable then closeSize + 6.0f else 0.0f)

    /// Where each tab starts and how wide it is (a panel can line up with it, a test can click in it).
    let layout (ui: Ui<'m>) (tabs: Tab list) : (Tab * float32 * float32) list =
        let _, placed = tabs |> List.fold (fun (x, acc) t -> let w = tabWidth ui t in (x + w, (t, x, w) :: acc)) (0.0f, [])
        List.rev placed

    /// What is under the strip: the panel of the selected tab, named after it.
    let panel (id: string) (name: string) (content: Scene) : Scene =
        Semantic(Semantics.create (id + "/panel") Role.TabPanel name, content)

    /// The strip, as a tab list named `name`.  A tab is activated by a click, Enter or Space; with a tab focused the arrows move to the neighbour, Home and End to the ends.
    let viewNamed (ui: Ui<'m>) (id: string) (name: string) (p: Props) : Scene =
        let th = ui.Theme
        let h = height ui
        let items = layout ui p.Tabs
        let total = items |> List.sumBy (fun (_, _, w) -> w)
        let ids = items |> List.map (fun (t, _, _) -> t.Id)
        let at = ids |> List.tryFindIndex (fun i -> i = p.Selected)
        let go (i: int) = match List.tryItem i ids with Some target -> p.OnSelect target | None -> ()
        let ty = (h - 3.0f - th.FontSize * 1.2f) * 0.5f
        let tabScene (i: int) ((t: Tab), (x: float32), (w: float32)) : Scene =
            let tid = id + "/" + t.Id
            let selected = (t.Id = p.Selected)
            let hot = Ui.isHover ui tid
            let face =
                Group [
                    rectangle { bounds 0.0f 0.0f w (h - 1.0f); fill (if selected then th.Surface elif hot then th.SurfaceRaised else Color.Transparent) }
                    shift th.PadX ty (Text(t.Title, Ui.textStyle ui th.FontSize (if selected then th.Text else th.TextMuted)))
                    (if selected then rectangle { bounds 0.0f (h - 3.0f) w 3.0f; fill th.Indicator } else Empty)
                    Ui.focusRing ui tid w (h - 1.0f) 0.0f
                ]
            let act (a: SemanticAction) =
                match a with
                | SemanticAction.Activate -> p.OnSelect t.Id
                | SemanticAction.Increment -> go (i + 1)
                | SemanticAction.Decrement -> go (i - 1)
                | SemanticAction.First -> go 0
                | SemanticAction.Last -> go (ids.Length - 1)
                | _ -> ()
            // only the selected tab is in the order of Tab: the arrows are the way to the others
            let tab = Ui.semantic ui tid Role.Tab t.Title (fun s -> { s with Selected = selected; Focusable = selected }) act (Ui.interactive ui tid (fun () -> p.OnSelect t.Id) face)
            let close =
                if not t.Closable then Empty
                else
                    let cid = id + "/close/" + t.Id
                    let c = if Ui.isHover ui cid then th.Text else th.TextMuted
                    let cx = w - closeSize - th.PadX * 0.5f
                    let cy = (h - 1.0f - closeSize) * 0.5f
                    let cross =
                        Group [
                            rectangle { bounds 0.0f 0.0f closeSize closeSize; radius 4.0f; fill (if Ui.isHover ui cid then th.SurfaceRaised else Color.Transparent) }
                            Shape(Path(LineSdf(Vec2.create 5.5f 5.5f, Vec2.create 12.5f 12.5f, 1.6f), Solid c, None, None))
                            Shape(Path(LineSdf(Vec2.create 12.5f 5.5f, Vec2.create 5.5f 12.5f, 1.6f), Solid c, None, None))
                        ]
                    shift cx cy (Ui.semantic ui cid Role.Button ("Close " + t.Title) (fun s -> { s with Focusable = false }) (fun a -> match a with SemanticAction.Activate -> p.OnClose t.Id | _ -> ()) (Ui.interactive ui cid (fun () -> p.OnClose t.Id) cross))
            shift x 0.0f (Group [ tab; close ])
        let line = rectangle { bounds 0.0f (h - 1.0f) (Scalar.max' total p.Width) 1.0f; fill th.Border }
        Semantic(Semantics.create id Role.TabList name, Group (line :: (items |> List.mapi tabScene)))

    /// The strip named "Tabs"; prefer `viewNamed`.
    let view (ui: Ui<'m>) (id: string) (p: Props) : Scene = viewNamed ui id "Tabs" p
