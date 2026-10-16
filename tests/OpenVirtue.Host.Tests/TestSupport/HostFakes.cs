// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Host;

namespace OpenVirtue.Host.Tests.TestSupport;

// Platform, input and render fakes shared by the RuntimeHost tests.

internal sealed class FakeWindow : IPlatformWindow
{
    public int Width { get; } = 640;

    public int Height { get; } = 480;

    public string Title { get; set; } = "test";

    public bool IsOpen { get; private set; } = true;

    public bool Focused { get; } = true;

    public bool PointerInside { get; } = true;

    public void Close() => IsOpen = false;

    public void SetCursorVisible(bool visible)
    {
    }

    public string? Prompt(string label, string initialValue, int capacity) => null;
}

internal sealed class FakeInput : IInputSource
{
    public HashSet<HostKey> Down { get; } = [];

    public List<HostKey> PressedKeys { get; } = [];

    public Vector2 Motion { get; set; }

    public Vector2 PointerPosition { get; set; }

    public bool IsDown(HostKey key) => Down.Contains(key);

    public IReadOnlyCollection<HostKey> Pressed => PressedKeys;

    public bool IsDown(MouseButton button) => false;

    public bool WasPressed(MouseButton button) => false;

    public bool WasReleased(MouseButton button) => false;

    public Vector2 ConsumeMotion()
    {
        Vector2 motion = Motion;
        Motion = Vector2.Zero;
        return motion;
    }
}

internal sealed class FakeRender : IRenderBackend
{
    public void UploadWorld(IReadOnlyDictionary<string, TextureImage> images)
    {
    }

    public void UploadUi(IReadOnlyDictionary<string, TextureImage> images)
    {
    }

    public void UploadFonts(IReadOnlyDictionary<string, TextureImage> images)
    {
    }

    /// <summary>The most recent frame the host drew.</summary>
    public HostFrame? LastFrame { get; private set; }

    public void Draw(HostFrame frame) => LastFrame = frame;

    public RgbaImage ReadRgba() => new(1, 1, [0, 0, 0, 255]);

    public void Present()
    {
    }
}
