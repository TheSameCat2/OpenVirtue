// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Formats.Wmp;

/// <summary>A map vertex (a 2D point with a z/height component).</summary>
public readonly record struct WmpVertex(float X, float Y, float Z);

/// <summary>A sector with a floor and ceiling height. Referenced by walls (by index).</summary>
public readonly record struct WmpRegion(string Name, float FloorHeight, float CeilHeight);

/// <summary>
/// A wall segment between two vertices, with a region on each side (a portal when
/// both are real regions). Indices refer to the map's vertex and region tables, in
/// declaration order.
/// </summary>
public readonly record struct WmpWall(
    string Name,
    int Vertex1,
    int Vertex2,
    int Region1,
    int Region2,
    float OffsetX,
    float OffsetY);

/// <summary>
/// A placed object — a <c>THING</c> (static sprite) or <c>ACTOR</c> (dynamic/AI
/// sprite). Both share the same fields. <see cref="Name"/> ties the placement to a
/// WDL-defined object type.
/// </summary>
public readonly record struct WmpPlacement(string Name, float X, float Y, float Angle, int Region);

/// <summary>The player's spawn position, facing angle, and starting region.</summary>
public readonly record struct WmpPlayerStart(float X, float Y, float Angle, int Region);
