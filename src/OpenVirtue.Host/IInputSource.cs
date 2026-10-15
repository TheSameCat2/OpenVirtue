// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;

namespace OpenVirtue.Host;

/// <summary>
/// Keyboard and mouse state for one frame. Edges (<see cref="Pressed"/>, <see cref="WasPressed"/>)
/// are the transitions since the previous pump; the source clears them on the next pump.
/// </summary>
public interface IInputSource
{
    /// <summary>True while <paramref name="key"/> is held.</summary>
    bool IsDown(HostKey key);

    /// <summary>Keys that transitioned to down since the previous pump. Repeats are not included.</summary>
    IReadOnlyCollection<HostKey> Pressed { get; }

    /// <summary>True while the mouse button is held.</summary>
    bool IsDown(MouseButton button);

    /// <summary>True if the button went down since the previous pump.</summary>
    bool WasPressed(MouseButton button);

    /// <summary>True if the button went up since the previous pump.</summary>
    bool WasReleased(MouseButton button);

    /// <summary>Pointer position in framebuffer pixels, origin top-left.</summary>
    Vector2 PointerPosition { get; }

    /// <summary>Returns the pointer motion since the last call, in framebuffer pixels, and clears it.</summary>
    Vector2 ConsumeMotion();
}
