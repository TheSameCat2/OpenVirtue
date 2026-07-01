// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine;

/// <summary>
/// A texture resolved from the WDL declaration chain (<c>TEXTURE</c> → <c>BMAPS</c> →
/// <c>BMAP</c>): the source PCX file, the sub-rectangle within it, and the texture's
/// scale. The renderer loads <see cref="File"/> and samples the given rectangle.
/// </summary>
public readonly record struct LevelTexture(
    string Name,
    string File,
    int X,
    int Y,
    int Width,
    int Height,
    double ScaleX,
    double ScaleY,
    bool IsSky);
