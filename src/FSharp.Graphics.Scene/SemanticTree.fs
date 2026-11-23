namespace FSharp.Graphics.Scene

open FSharp.Math

/// A described element, placed.
type SemanticNode =
    { Info: Semantics
      /// Index of the enclosing described element in the same array; -1 at the top.
      Parent: int
      /// Where it is drawn, in window units (every transform applied), cut by the clip regions around it.
      Bounds: Rect
      /// False when nothing of it is drawn (scrolled out of its clip region, no content): not exposed to the platform.
      Visible: bool }

/// The accessibility tree of a scene: the `Semantic` nodes, in tree order.
module SemanticTree =

    // A node before the siblings are put in their final order.
    [<Struct>]
    type private Raw = { RInfo: Semantics; RParent: int; RBounds: Rect; RVisible: bool }

    let private hasArea (r: Rect) = r.W > 0.0f && r.H > 0.0f

    /// The described elements of a RESOLVED scene (`Resolve.resolve`), with their places.  Siblings keep the order of the
    /// scene unless their `Order` differs (lower first, stable).
    let collect (resolved: Scene) : SemanticNode[] =
        let raws = ResizeArray<Raw>()
        let rec go (s: Scene) (world: Mat3x2) (clip: Rect voption) (parent: int) : unit =
            match s with
            | Empty | Text _ | Shape _ | GlyphRun _ | Image _ -> ()
            | Group cs | Stack(_, cs) -> for c in cs do go c world clip parent
            | Transform(m, c) -> go c (Mat3x2.multiply m world) clip parent
            | Clip(r, c) ->
                let w = Mat3x2.transformRect world r
                go c world (ValueSome(match clip with ValueSome k -> Rect.intersect k w | ValueNone -> w)) parent
            // decorative or inert (a picture of a widget): neither it nor anything inside it is exposed
            | Semantic(info, _) when info.Role = Role.None -> ()
            | Semantic(info, c) ->
                let own =
                    match Resolve.bounds c with
                    | Some b -> Mat3x2.transformRect world b
                    | None -> Rect.zero
                let placed = match clip with ValueSome k -> Rect.intersect k own | ValueNone -> own
                raws.Add { RInfo = info; RParent = parent; RBounds = placed; RVisible = hasArea placed }
                go c world clip (raws.Count - 1)
            | Padding(_, c) | Opacity(_, c) | Reactive(_, c) | Cursor(_, c) | Animated(_, c) | Blur(_, c)
            | ColorFilter(_, c) | BlendMode(_, c) | Mask(_, c) -> go c world clip parent
        go resolved Mat3x2.identity ValueNone -1

        // children of every element, in scene order; then a depth-first walk with each sibling group ordered by `Order`
        let n = raws.Count
        let children = Array.init (n + 1) (fun _ -> ResizeArray<int>())       // slot n = the top level
        for i in 0 .. n - 1 do
            let p = raws.[i].RParent
            children.[if p < 0 then n else p].Add i
        let result = ResizeArray<SemanticNode>(n)
        let rec emit (oldIdx: int) (newParent: int) : unit =
            let r = raws.[oldIdx]
            let me = result.Count
            result.Add { Info = r.RInfo; Parent = newParent; Bounds = r.RBounds; Visible = r.RVisible }
            let kids = children.[oldIdx] |> Seq.toList |> List.sortBy (fun k -> raws.[k].RInfo.Order * 1000000 + k)
            for k in kids do emit k me
        let tops = children.[n] |> Seq.toList |> List.sortBy (fun k -> raws.[k].RInfo.Order * 1000000 + k)
        for t in tops do emit t -1
        result.ToArray()

    /// The described elements of an AUTHORED scene, in the same order, without places: what handling a key needs (the tab
    /// order, the state and the actions of the focused element) and no layout.
    let walk (scene: Scene) : Semantics[] =
        let raws = ResizeArray<Raw>()
        let rec go (s: Scene) (parent: int) : unit =
            match s with
            | Empty | Text _ | Shape _ | GlyphRun _ | Image _ -> ()
            | Group cs | Stack(_, cs) -> for c in cs do go c parent
            | Semantic(info, _) when info.Role = Role.None -> ()
            | Semantic(info, c) ->
                raws.Add { RInfo = info; RParent = parent; RBounds = Rect.zero; RVisible = true }
                go c (raws.Count - 1)
            | Padding(_, c) | Clip(_, c) | Transform(_, c) | Opacity(_, c) | Reactive(_, c) | Cursor(_, c) | Animated(_, c)
            | Blur(_, c) | ColorFilter(_, c) | BlendMode(_, c) | Mask(_, c) -> go c parent
        go scene -1
        let n = raws.Count
        let children = Array.init (n + 1) (fun _ -> ResizeArray<int>())
        for i in 0 .. n - 1 do
            let p = raws.[i].RParent
            children.[if p < 0 then n else p].Add i
        let result = ResizeArray<Semantics>(n)
        let rec emit (idx: int) : unit =
            result.Add raws.[idx].RInfo
            for k in children.[idx] |> Seq.toList |> List.sortBy (fun k -> raws.[k].RInfo.Order * 1000000 + k) do emit k
        for t in children.[n] |> Seq.toList |> List.sortBy (fun k -> raws.[k].RInfo.Order * 1000000 + k) do emit t
        result.ToArray()

    /// The elements the keyboard can reach, in tab order.
    let focusOrder (nodes: Semantics[]) : Semantics[] =
        nodes |> Array.filter (fun s -> s.Focusable && not s.Disabled)
