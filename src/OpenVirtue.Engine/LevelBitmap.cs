// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine;

/// <summary>
/// A top-level WDL <c>BMAP</c> declaration: the source PCX file and optional rectangle.
/// Non-positive dimensions request the whole image.
/// </summary>
public readonly record struct LevelBitmap(
    string Name,
    string File,
    int X,
    int Y,
    int Width,
    int Height);
