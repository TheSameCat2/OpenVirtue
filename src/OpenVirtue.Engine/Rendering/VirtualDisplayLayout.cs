// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;

namespace OpenVirtue.Engine.Rendering;

/// <summary>A physical rectangle in client-area pixels.</summary>
public readonly record struct DisplayRectangle(float X, float Y, float Width, float Height);

/// <summary>Maps a WDL VIDEO mode's virtual pixels into a uniformly scaled physical client area.</summary>
public readonly record struct VirtualDisplayLayout(
    int VirtualWidth,
    int VirtualHeight,
    float Scale,
    float OffsetX,
    float OffsetY)
{
    /// <summary>Creates a centered, aspect-preserving virtual display mapping.</summary>
    public static VirtualDisplayLayout Create(
        int physicalWidth,
        int physicalHeight,
        int virtualWidth,
        int virtualHeight)
    {
        physicalWidth = Math.Max(1, physicalWidth);
        physicalHeight = Math.Max(1, physicalHeight);
        virtualWidth = Math.Max(1, virtualWidth);
        virtualHeight = Math.Max(1, virtualHeight);
        float scale = Math.Min(physicalWidth / (float)virtualWidth, physicalHeight / (float)virtualHeight);
        return new VirtualDisplayLayout(
            virtualWidth,
            virtualHeight,
            scale,
            (physicalWidth - (virtualWidth * scale)) * 0.5f,
            (physicalHeight - (virtualHeight * scale)) * 0.5f);
    }

    /// <summary>Maps a physical client point into virtual WDL screen pixels.</summary>
    public Vector2 ToVirtual(float x, float y) =>
        new((x - OffsetX) / Scale, (y - OffsetY) / Scale);

    /// <summary>Maps a virtual WDL screen rectangle into physical client pixels.</summary>
    public DisplayRectangle ToPhysical(float x, float y, float width, float height) =>
        new(OffsetX + (x * Scale), OffsetY + (y * Scale), width * Scale, height * Scale);

    /// <summary>The full virtual display's physical letterboxed rectangle.</summary>
    public DisplayRectangle PhysicalDisplay =>
        ToPhysical(0, 0, VirtualWidth, VirtualHeight);
}
