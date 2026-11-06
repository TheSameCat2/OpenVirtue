// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Rendering;

/// <summary>
/// A renderer-ready vertex: a render-space position (x, y up, z), texture coordinate, and local
/// ambient light. WMP map coordinates (x, y) map to render (x, −z) and region heights to render y
/// (<see cref="RenderSpace"/>, ADR-0009), so that facing map +x puts map +y on the screen left
/// as the original does.
/// </summary>
/// <remarks>
/// <see cref="WallAX"/>, <see cref="WallAZ"/>, <see cref="WallBX"/> and <see cref="WallBZ"/> are the
/// two end vertices of the wall the vertex belongs to, in render space: a wall's shade zones step
/// across the screen from its own ends (OV-20261105-shade-global, <see cref="PaletteShading.WallStep"/>),
/// so every vertex of a wall's quad carries them instead of the quad's own, which a near clip would
/// cut. All four 0 means the vertex is no wall's — a surface's or a sprite's — and the shader shades
/// it as one.
/// </remarks>
public readonly record struct RenderVertex(
    float X,
    float Y,
    float Z,
    float U,
    float V,
    float Ambient = 0,
    float WallAX = 0,
    float WallAZ = 0,
    float WallBX = 0,
    float WallBZ = 0);
