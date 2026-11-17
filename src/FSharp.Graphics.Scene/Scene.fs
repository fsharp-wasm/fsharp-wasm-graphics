namespace FSharp.Graphics.Scene

open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Primitives

/// Input event types for reactive regions
type InputEvent =
    | PointerDown  of position: Vec2 * button: int
    | PointerUp    of position: Vec2 * button: int
    | PointerMove  of position: Vec2
    | PointerEnter of position: Vec2
    | PointerLeave of position: Vec2
    | KeyDown      of key: string
    | KeyUp        of key: string
    | Scroll       of delta: Vec2

/// Stack layout direction
type Direction = Horizontal | Vertical | ZStack

/// Cross-axis alignment
type CrossAxisAlignment = Stretch | Start | Center | End_

/// Main axis size mode
type SizeMode = FillParent | WrapContent | Fixed of float32

/// Stack layout properties
[<Struct>]
type StackProps = {
    Direction  : Direction
    Gap        : float32
    Alignment  : CrossAxisAlignment
    MainSize   : SizeMode
}

/// Edge insets
[<Struct>]
type Insets = { Top: float32; Right: float32; Bottom: float32; Left: float32 }

module Insets =
    let zero = { Top = 0.0f; Right = 0.0f; Bottom = 0.0f; Left = 0.0f }
    let all p = { Top = p; Right = p; Bottom = p; Left = p }
    let xy x y = { Top = y; Right = x; Bottom = y; Left = x }
    let create t r b l = { Top = t; Right = r; Bottom = b; Left = l }

/// What an element is, for assistive technology: the ARIA roles the widgets need (see `Semantics`).
[<RequireQualifiedAccess>]
type Role =
    /// Decorative: not exposed.
    | None
    | Group | Region | Toolbar | List | ListItem
    | Heading of level: int
    /// Text that is read as it is (a label, a paragraph).
    | StaticText
    | Button | CheckBox | Switch | Slider | TextBox | ComboBox | ListBox | Option | ProgressBar | Image

/// How a change of this element is announced without moving the focus.
[<RequireQualifiedAccess>]
type Live = Off | Polite | Assertive

/// What assistive technology (or the keyboard) may ask an element to do.
[<RequireQualifiedAccess>]
type SemanticAction =
    /// Click, Enter or Space.
    | Activate
    /// A slider one step up / down; `First` and `Last` jump to its ends.
    | Increment | Decrement | First | Last
    | SetValue of float32
    /// A text box got new text from the platform's own input element.
    | SetText of string
    /// The element received / lost the keyboard focus (the platform moved it).
    | Focus | Blur
    /// Escape: close what this element opened.
    | Dismiss

/// A widget's description for assistive technology.  A `Semantic` node wraps the scene of the widget; the host
/// turns the tree of them into the platform's accessibility tree (an overlay of ARIA elements in the browser),
/// placed where the widget is drawn.  `Act` is what the platform may ask of it: the widget runs its own handler.
type Semantics =
    { /// Stable across frames (the widget's id): the keyboard focus and the platform's element follow it.
      Id: string
      Role: Role
      /// The accessible name: what a screen reader says for this element (the label, never "button").
      Name: string
      /// A hint the name does not carry (`Arrow keys move the element`).
      Description: string
      /// The value as text (a text box's content, a slider's "50 %").
      Value: string
      /// `ValueNone`: not checkable.
      Checked: bool voption
      Selected: bool
      Disabled: bool
      /// `ValueNone`: does not expand.
      Expanded: bool voption
      /// Has the keyboard focus (the host moves the platform's focus here).
      Focused: bool
      /// Can receive the keyboard focus: in the tab order.
      Focusable: bool
      /// The range of a slider or progress bar; `Max <= Min`: none.
      Min: float32
      Max: float32
      Now: float32
      Live: Live
      /// Orders siblings (lower first) when the tab order should differ from the order of the scene.
      Order: int
      Act: SemanticAction -> unit }

module Semantics =
    /// An element with no state: a role and a name.
    let create (id: string) (role: Role) (name: string) : Semantics =
        { Id = id; Role = role; Name = name; Description = ""; Value = ""
          Checked = ValueNone; Selected = false; Disabled = false; Expanded = ValueNone
          Focused = false; Focusable = false
          Min = 0.0f; Max = 0.0f; Now = 0.0f; Live = Live.Off; Order = 0
          Act = ignore }

/// The immutable Scene tree
type Scene =
    // Leaf primitives
    | Shape     of PrimitiveShape
    | Text      of text: string * style: TextStyle
    /// Laid-out text: positioned glyphs, produced by Resolve.resolve from a Text node.
    /// Glyph (X,Y) are baseline pen positions relative to the local origin.
    | GlyphRun  of fontId: FontId * sizePx: float32 * color: Color * glyphs: GlyphPosition array * bounds: Rect
    | Image     of ImageId * Rect * opacity: float32
    | Empty

    // Structural compositing
    | Group     of children: Scene list
    | Stack     of StackProps * children: Scene list
    | Clip      of region: Rect * child: Scene
    | Padding   of Insets * child: Scene

    // Transformation
    | Transform of Mat3x2 * child: Scene
    | Opacity   of alpha: float32 * child: Scene

    // Interaction
    | Reactive  of handler: (InputEvent -> bool) * child: Scene
    /// Accessibility: describes `child` for assistive technology.  Transparent for layout, drawing and hit testing.
    | Semantic  of Semantics * child: Scene

    // Animation
    | Animated  of Transition * child: Scene

    // Effects
    | Blur      of sigma: float32 * child: Scene
    | ColorFilter of ColorMatrix * child: Scene
    | BlendMode of Blend * child: Scene
    | Mask      of mask: Scene * child: Scene

/// Pipeline combinators for Scene manipulation
module Scene =
    let translate dx dy s       = Transform(Mat3x2.translation dx dy, s)
    let scale sx sy s           = Transform(Mat3x2.scale sx sy, s)
    let rotate deg origin s     = Transform(Mat3x2.rotateAround deg origin, s)
    let opacity a s             = Opacity(a, s)
    let clip r s                = Clip(r, s)
    let pad i s                 = Padding(i, s)
    let padAll p s              = Padding(Insets.all p, s)
    let padXY x y s             = Padding(Insets.xy x y, s)
    let animated t s            = Animated(t, s)
    let reactive h s            = Reactive(h, s)
    let semantic info s         = Semantic(info, s)
    let blur sigma s            = Blur(sigma, s)
    let colorFilter m s         = ColorFilter(m, s)
    let blendMode b s           = BlendMode(b, s)
    let mask m s                = Mask(m, s)

    let hstack gap children     = Stack({ Direction = Horizontal; Gap = gap; Alignment = Start; MainSize = WrapContent }, children)
    let vstack gap children     = Stack({ Direction = Vertical;   Gap = gap; Alignment = Start; MainSize = WrapContent }, children)
    let zstack children         = Stack({ Direction = ZStack;     Gap = 0.0f; Alignment = Start; MainSize = WrapContent }, children)

    let group children          = Group children
    let empty                   = Empty

    let onClick handler s       = Reactive((fun evt -> match evt with PointerDown _ -> handler(); true | _ -> false), s)
    let onHover enter leave s   = Reactive((fun evt -> match evt with PointerEnter _ -> enter(); true | PointerLeave _ -> leave(); true | _ -> false), s)
