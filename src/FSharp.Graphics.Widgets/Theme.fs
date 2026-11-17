namespace FSharp.Graphics.Widgets

open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.SceneDsl

/// Design tokens. Widgets read everything visual from here, so re-theming is one record.  The two themes below meet
/// WCAG 2.2 AA for every pair a widget draws (the tests check each pair); a theme of your own should keep that.
type Theme =
    { Font: FontId
      Background: Color
      Surface: Color
      SurfaceRaised: Color
      Border: Color
      Text: Color
      TextMuted: Color
      Accent: Color
      AccentHover: Color
      AccentPressed: Color
      OnAccent: Color
      Danger: Color
      Success: Color
      /// Boundaries and tracks of controls (a field's border, a slider's track): 3 : 1 against every surface (WCAG 1.4.11).
      /// `Border` is for decoration (dividers, panel edges) and carries no such promise.
      Control: Color
      /// The focus ring: 3 : 1 against every surface (WCAG 2.4.7, 1.4.11).
      Focus: Color
      /// Non-text accent (a checked box, the value of a slider, a toggle that is on): 3 : 1 against every surface.
      Indicator: Color
      Radius: float32
      ControlHeight: float32
      PadX: float32
      Gap: float32
      FontSize: float32 }

module Theme =
    let dark : Theme =
        { Font = FontId 0UL
          Background = hex 0x0F172A; Surface = hex 0x1E293B; SurfaceRaised = hex 0x273449
          Border = hex 0x3B4A63; Text = hex 0xF1F5F9; TextMuted = hex 0x94A3B8
          // `Accent` is the fill of a button whose label is `OnAccent`: 4.5 : 1 (WCAG 1.4.3); hover and pressed get darker, not lighter
          Accent = hex 0x2563EB; AccentHover = hex 0x1D4ED8; AccentPressed = hex 0x1E40AF; OnAccent = hex 0xFFFFFF
          Danger = hex 0xDC2626; Success = hex 0x22C55E
          Control = hex 0x8393AD; Focus = hex 0x93C5FD; Indicator = hex 0x3B82F6
          Radius = 8.0f; ControlHeight = 34.0f; PadX = 14.0f; Gap = 8.0f; FontSize = 14.0f }

    let light : Theme =
        { dark with
            Background = hex 0xF1F5F9; Surface = hex 0xFFFFFF; SurfaceRaised = hex 0xE2E8F0
            Border = hex 0xCBD5E1; Text = hex 0x0F172A; TextMuted = hex 0x475569
            Success = hex 0x15803D
            Control = hex 0x5B6B82; Focus = hex 0x1D4ED8; Indicator = hex 0x2563EB }
