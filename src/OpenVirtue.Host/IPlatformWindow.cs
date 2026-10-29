// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Interpreter;

namespace OpenVirtue.Host;

/// <summary>The window the frame loop presents into. Pixel size, not logical points.</summary>
public interface IPlatformWindow
{
    /// <summary>Framebuffer width in pixels.</summary>
    int Width { get; }

    /// <summary>Framebuffer height in pixels.</summary>
    int Height { get; }

    /// <summary>Window title. The host writes the level name and a short status into it.</summary>
    string Title { get; set; }

    /// <summary>False after the user or the host has asked the window to close.</summary>
    bool IsOpen { get; }

    /// <summary>True while the window has keyboard focus.</summary>
    bool Focused { get; }

    /// <summary>True while the pointer is inside the client area.</summary>
    bool PointerInside { get; }

    /// <summary>Asks the window to close. <see cref="IsOpen"/> becomes false.</summary>
    void Close();

    /// <summary>Shows or hides the OS cursor. Hiding does not by itself grab the pointer.</summary>
    void SetCursorVisible(bool visible);

    /// <summary>
    /// Blocks in a text prompt for WDL <c>INKEY</c>. Returns the entered text, or null when
    /// the user cancels.
    /// </summary>
    string? Prompt(string label, string initialValue, int capacity);

    /// <summary>
    /// Starts sending typed characters and editing keys to <see cref="ConsumeTextKeys"/> instead of
    /// the game keys, so an <c>INKEY</c> input runs while the world goes on (OV-20261029-inkey).
    /// <c>false</c> when the window offers only the blocking <see cref="Prompt"/>.
    /// </summary>
    bool BeginTextInput() => false;

    /// <summary>Ends what <see cref="BeginTextInput"/> started.</summary>
    void EndTextInput()
    {
    }

    /// <summary>The characters and editing keys typed since the last call.</summary>
    IReadOnlyList<(InkeyKey Key, char Character)> ConsumeTextKeys() => [];
}
