// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Host;

/// <summary>Keys the host publishes as <c>KEY_*</c> skills and <c>IF_*</c> hooks.</summary>
public enum HostKey
{
    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    Numpad0, Numpad1, Numpad2, Numpad3, Numpad4,
    Numpad5, Numpad6, Numpad7, Numpad8, Numpad9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Escape, Tab, LeftShift, RightShift, LeftControl, RightControl, LeftAlt, RightAlt,
    Space, Backspace, Up, Down, Left, Right,
    PageUp, PageDown, Home, End, Insert, Delete, Pause,
    Plus, Minus, NumpadPlus, NumpadMinus, Enter,
    Comma, Period, Quote, LeftBracket, RightBracket,
}

/// <summary>Mouse buttons published as <c>MOUSE_LEFT</c> / <c>MOUSE_MIDDLE</c> / <c>MOUSE_RIGHT</c>.</summary>
public enum MouseButton
{
    Left,
    Middle,
    Right,
}
