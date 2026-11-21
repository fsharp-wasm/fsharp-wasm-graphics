namespace FSharp.Graphics.Interact

open FSharp.Math

/// How the children of a slot follow each other: down (a list of blocks) or to the right (a row).
type Flow =
    | Down
    | Right

/// A place that things can be dropped before, after or (when it nests) into.  `Id` is the application's: a block number, a node, a path.
type Slot =
    { Id: int
      Bounds: Rect
      Flow: Flow
      /// a container takes a drop into it (the middle of its extent); a leaf only before or after
      Nests: bool }

type Position =
    | Before
    | After
    | Into

/// What a pointer position means: the slot, which side or the inside, and the thin rectangle (or, for `Into`, the outline) where a view draws the mark.
type Drop =
    { Target: int
      Position: Position
      Mark: Rect }

module Drop =

    /// The thickness of the mark of a `Before` / `After`.
    let markThickness = 3.0f

    /// The share of a container's extent, at each end, that means before / after; the rest between means into.
    let edge = 0.25f

    let private markOf (b: Rect) (flow: Flow) (position: Position) : Rect =
        let t = markThickness
        match position, flow with
        | Into, _ -> b
        | Before, Down -> Rect.create b.X (b.Y - t * 0.5f) b.W t
        | After, Down -> Rect.create b.X (Rect.bottom b - t * 0.5f) b.W t
        | Before, Right -> Rect.create (b.X - t * 0.5f) b.Y t b.H
        | After, Right -> Rect.create (Rect.right b - t * 0.5f) b.Y t b.H

    /// 0 where the pointer is inside the rectangle, else how far outside (the distance to the nearest edge).
    let private distance (r: Rect) (p: Vec2) : float32 =
        let dx = if p.X < r.X then r.X - p.X elif p.X > Rect.right r then p.X - Rect.right r else 0.0f
        let dy = if p.Y < r.Y then r.Y - p.Y elif p.Y > Rect.bottom r then p.Y - Rect.bottom r else 0.0f
        dx * dx + dy * dy

    /// Where along the slot's flow the pointer is: 0 at the start, 1 at the end (outside that range when the pointer is outside the slot).
    let private along (s: Slot) (p: Vec2) : float32 =
        match s.Flow with
        | Down -> if s.Bounds.H <= 0.0f then 0.5f else (p.Y - s.Bounds.Y) / s.Bounds.H
        | Right -> if s.Bounds.W <= 0.0f then 0.5f else (p.X - s.Bounds.X) / s.Bounds.W

    let private positionIn (s: Slot) (p: Vec2) : Position =
        let t = along s p
        if s.Nests then (if t < edge then Before elif t > 1.0f - edge then After else Into)
        else (if t < 0.5f then Before else After)

    /// The slot nearest to the pointer: the one that has it (the smallest of those, so a slot inside a container wins), or, with the pointer in a gap or outside
    /// the list, the nearest one, which is what makes a drop above the first or below the last block mean before it or after it.
    let private nearest (slots: Slot list) (p: Vec2) : Slot option =
        let better (candidate: Slot) (current: Slot) : bool =
            let dc = distance candidate.Bounds p
            let dn = distance current.Bounds p
            dc < dn || (dc = dn && Rect.area candidate.Bounds < Rect.area current.Bounds)
        match slots with
        | [] -> None
        | first :: rest -> Some (List.fold (fun (best: Slot) (s: Slot) -> if better s best then s else best) first rest)

    /// The drop that a pointer position means over these slots; none when there are no slots.
    let resolve (slots: Slot list) (p: Vec2) : Drop option =
        match nearest slots p with
        | None -> None
        | Some s ->
            let position = positionIn s p
            Some { Target = s.Id; Position = position; Mark = markOf s.Bounds s.Flow position }

/// A drag as a session of what the pointer did.  `'p` is what is dragged (a palette entry, a block): the application's.
type Session<'p> =
    /// nothing is being dragged
    | Idle
    /// pressed on something that can be dragged, and not moved far enough yet: it is a click until it is
    | Pressed of payload: 'p * origin: Vec2
    /// being dragged: where the pointer is, and the drop it means (none: outside every slot)
    | Dragging of payload: 'p * origin: Vec2 * at: Vec2 * over: Drop option

module Drag =

    /// How far (in pixels) the pointer must move after the press before it is a drag and not a click.
    let threshold = 5.0f

    let press (payload: 'p) (at: Vec2) : Session<'p> = Pressed (payload, at)

    let private farFrom (origin: Vec2) (at: Vec2) : bool =
        let dx = at.X - origin.X
        let dy = at.Y - origin.Y
        dx * dx + dy * dy >= threshold * threshold

    /// The pointer moved.  A press becomes a drag beyond the threshold; a drag follows the pointer and re-reads the drop (the slots may have moved: they are given each time).
    let move (slots: Slot list) (at: Vec2) (s: Session<'p>) : Session<'p> =
        match s with
        | Idle -> s
        | Pressed (payload, origin) -> if farFrom origin at then Dragging (payload, origin, at, Drop.resolve slots at) else s
        | Dragging (payload, origin, _, _) -> Dragging (payload, origin, at, Drop.resolve slots at)

    /// The pointer was released: what is dropped and where, for a drag that is over a slot; nothing for a click (never moved far) or a release outside every slot.
    let release (s: Session<'p>) : ('p * Drop) option =
        match s with
        | Dragging (payload, _, _, Some drop) -> Some (payload, drop)
        | _ -> None

    /// Escape, or the pointer left for good: the drag is over, nothing is dropped.
    let cancel () : Session<'p> = Idle

    let isDragging (s: Session<'p>) : bool =
        match s with
        | Dragging _ -> true
        | _ -> false

    /// What is being dragged, once it is a drag (a view draws it under the pointer).
    let payload (s: Session<'p>) : 'p option =
        match s with
        | Dragging (p, _, _, _) -> Some p
        | _ -> None

    /// The drop that the drag means now, for the view's mark.
    let over (s: Session<'p>) : Drop option =
        match s with
        | Dragging (_, _, _, over) -> over
        | _ -> None

module Scroll =

    /// A ramp from 0 (at `edge` pixels inside the viewport) to 1 (at its border or beyond).
    let private ramp (depth: float32) (edge: float32) : float32 =
        if edge <= 0.0f then 0.0f
        else
            let t = depth / edge
            if t < 0.0f then 0.0f elif t > 1.0f then 1.0f else t

    /// How far a scrollable area should move in this frame while something is dragged with the pointer at `p`: nothing in the quiet middle, and, within `edge` pixels
    /// of a border, up to `maxSpeed` pixels towards that border, faster the closer the pointer is to it.  Positive is right / down.
    let near (viewport: Rect) (edge: float32) (maxSpeed: float32) (p: Vec2) : Vec2 =
        let axis (pos: float32) (lo: float32) (hi: float32) : float32 =
            let towardsLow = ramp (lo + edge - pos) edge
            let towardsHigh = ramp (pos - (hi - edge)) edge
            maxSpeed * (towardsHigh - towardsLow)
        Vec2.create (axis p.X viewport.X (Rect.right viewport)) (axis p.Y viewport.Y (Rect.bottom viewport))
