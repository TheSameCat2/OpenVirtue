// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Host;

/// <summary>Everything the backend needs to draw one frame. Viewports are framebuffer pixels, origin top-left.</summary>
public sealed class HostFrame
{
    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Clear color in 0..1 RGBA. Depth is cleared to the far plane when <see cref="ClearDepth"/> is set.</summary>
    public Vector4 ClearColor { get; init; }

    public bool ClearDepth { get; init; }

    /// <summary><c>PLAYER_LIGHT</c> and <c>LIGHT_DIST</c> for the shaded batches (OV-20261029-shading).</summary>
    public Vector2 Light { get; init; }

    public List<DrawBatch> Batches { get; } = [];
}

/// <summary>One textured triangle list. <see cref="Transform"/> is the full model-view-projection the shader multiplies.</summary>
public sealed class DrawBatch
{
    public string? Texture { get; init; }

    /// <summary>Which cache <see cref="Texture"/> is looked up in.</summary>
    public TextureSlot Slot { get; init; }

    /// <summary>When set, uploaded for this batch instead of a cached name. Used for the FLIC frame.</summary>
    public TextureImage? DynamicImage { get; init; }

    public Matrix4x4 Transform { get; init; }

    public PixelRect Viewport { get; init; }

    public bool AlphaTest { get; init; }

    public bool DepthTest { get; init; }

    /// <summary>Added to each vertex's ambient, then saturated. This is the D3D constant <c>TintAndLight.a</c>.</summary>
    public float Ambient { get; init; }

    /// <summary>Multiplies the textured color. Palette fades live here.</summary>
    public Vector3 Tint { get; init; } = Vector3.One;

    /// <summary>
    /// Draw by palette index through the uploaded shade table: <see cref="Ambient"/> plus each
    /// vertex's ambient is the texel's <c>AMBIENT</c>, and its distance and <see cref="HostFrame.Light"/>
    /// set its shade level.
    /// </summary>
    public bool Shaded { get; init; }

    public required RenderVertex[] Vertices { get; init; }
}

/// <summary>Which uploaded cache a batch's texture name refers to.</summary>
public enum TextureSlot
{
    World,
    Ui,
    Font,
}

/// <summary>An integer framebuffer rectangle, origin top-left.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public static PixelRect From(DisplayRectangle rectangle, int framebufferWidth, int framebufferHeight)
    {
        int x = (int)MathF.Round(rectangle.X);
        int y = (int)MathF.Round(rectangle.Y);
        int width = Math.Max(1, (int)MathF.Round(rectangle.Width));
        int height = Math.Max(1, (int)MathF.Round(rectangle.Height));
        if (x < 0)
        {
            width += x;
            x = 0;
        }

        if (y < 0)
        {
            height += y;
            y = 0;
        }

        width = Math.Min(width, Math.Max(1, framebufferWidth - x));
        height = Math.Min(height, Math.Max(1, framebufferHeight - y));
        return new PixelRect(x, y, Math.Max(1, width), Math.Max(1, height));
    }

    public static PixelRect Full(int width, int height) => new(0, 0, Math.Max(1, width), Math.Max(1, height));
}
