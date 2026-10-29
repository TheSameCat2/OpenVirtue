// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Host;

/// <summary>Uploads textures and draws one frame described entirely in engine types.</summary>
public interface IRenderBackend
{
    /// <summary>Replaces the world-texture cache. Names are the keys <see cref="TextureLoader"/> produced.</summary>
    void UploadWorld(IReadOnlyDictionary<string, TextureImage> images);

    /// <summary>Replaces the UI bitmap cache.</summary>
    void UploadUi(IReadOnlyDictionary<string, TextureImage> images);

    /// <summary>Replaces the font atlas cache.</summary>
    void UploadFonts(IReadOnlyDictionary<string, TextureImage> images);

    /// <summary>Draws <paramref name="frame"/> into the current framebuffer. Does not present.</summary>
    void Draw(HostFrame frame);

    /// <summary>Reads the framebuffer as top-left-origin RGBA8.</summary>
    RgbaImage ReadRgba();

    /// <summary>Presents the framebuffer.</summary>
    /// <summary>
    /// The palette (256 × RGB) and shade table (256 colours × 32 levels) that shaded batches draw
    /// through, or <c>null</c>s to draw every batch in RGB (OV-20261029-shading).
    /// </summary>
    void UploadShading(byte[]? palette, byte[]? shadeTable)
    {
    }

    void Present();
}

/// <summary>One RGBA8 image, rows top to bottom.</summary>
public readonly record struct RgbaImage(int Width, int Height, byte[] Pixels);
