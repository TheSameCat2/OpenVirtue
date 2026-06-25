// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Rendering;

/// <summary>
/// A renderer-ready vertex: a world position (x, y up, z) and a texture coordinate.
/// WMP map coordinates (x, y) map to world (x, z); region heights map to world y.
/// </summary>
public readonly record struct RenderVertex(float X, float Y, float Z, float U, float V);
