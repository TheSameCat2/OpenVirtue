// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;

namespace OpenVirtue.Engine.Rendering;

/// <summary>A physical rectangle in client-area pixels.</summary>
public readonly record struct DisplayRectangle(float X, float Y, float Width, float Height);

/// <summary>
/// Maps a WDL VIDEO mode's virtual pixel grid into a physical client area. The grid is
/// letterboxed to the <em>display</em> aspect of the mode, not to its own pixel aspect: the
/// documented VGA modes were all shown on 4:3 displays, so a 320x200 or X320x400 frame is
/// stretched to 4:3 (OV-20261009-pixel-aspect-video-modes) and the horizontal and vertical
/// scales differ.
/// </summary>
public readonly record struct VirtualDisplayLayout(
    int VirtualWidth,
    int VirtualHeight,
    float ScaleX,
    float ScaleY,
    float OffsetX,
    float OffsetY)
{
    /// <summary>Creates a centered mapping that keeps the virtual grid's own pixel aspect (square pixels).</summary>
    public static VirtualDisplayLayout Create(
        int physicalWidth,
        int physicalHeight,
        int virtualWidth,
        int virtualHeight) =>
        Create(physicalWidth, physicalHeight, virtualWidth, virtualHeight,
            Math.Max(1, virtualWidth) / (double)Math.Max(1, virtualHeight));

    /// <summary>
    /// Creates a centered mapping that letterboxes the virtual grid into the largest
    /// <paramref name="displayAspect"/> (width / height) box that fits the client area.
    /// </summary>
    public static VirtualDisplayLayout Create(
        int physicalWidth,
        int physicalHeight,
        int virtualWidth,
        int virtualHeight,
        double displayAspect)
    {
        physicalWidth = Math.Max(1, physicalWidth);
        physicalHeight = Math.Max(1, physicalHeight);
        virtualWidth = Math.Max(1, virtualWidth);
        virtualHeight = Math.Max(1, virtualHeight);
        if (!(displayAspect > 0) || double.IsInfinity(displayAspect))
        {
            displayAspect = virtualWidth / (double)virtualHeight;
        }

        double boxWidth = physicalWidth;
        double boxHeight = boxWidth / displayAspect;
        if (boxHeight > physicalHeight)
        {
            boxHeight = physicalHeight;
            boxWidth = boxHeight * displayAspect;
        }

        return new VirtualDisplayLayout(
            virtualWidth,
            virtualHeight,
            (float)(boxWidth / virtualWidth),
            (float)(boxHeight / virtualHeight),
            (float)((physicalWidth - boxWidth) * 0.5),
            (float)((physicalHeight - boxHeight) * 0.5));
    }

    /// <summary>Maps a physical client point into virtual WDL screen pixels.</summary>
    public Vector2 ToVirtual(float x, float y) =>
        new((x - OffsetX) / ScaleX, (y - OffsetY) / ScaleY);

    /// <summary>Maps a virtual WDL screen rectangle into physical client pixels.</summary>
    public DisplayRectangle ToPhysical(float x, float y, float width, float height) =>
        new(OffsetX + (x * ScaleX), OffsetY + (y * ScaleY), width * ScaleX, height * ScaleY);

    /// <summary>The full virtual display's physical letterboxed rectangle.</summary>
    public DisplayRectangle PhysicalDisplay =>
        ToPhysical(0, 0, VirtualWidth, VirtualHeight);
}
