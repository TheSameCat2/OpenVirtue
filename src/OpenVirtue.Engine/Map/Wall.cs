// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Map;

/// <summary>
/// A wall segment between two vertices, with a region on each side (a portal when both
/// sides are real regions). Indices reference the level's vertex and region tables.
/// </summary>
public sealed class Wall : AcknexObject
{
    public Wall(string name) : base(name) { }

    public int Vertex1 { get; set; }
    public int Vertex2 { get; set; }
    public int LeftRegion { get; set; }
    public int RightRegion { get; set; }
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public string? Texture { get; set; }

    protected override bool TryGetTyped(string property, out double value)
    {
        switch (property.ToLowerInvariant())
        {
            case "offsx": value = OffsetX; return true;
            case "offsy": value = OffsetY; return true;
            default: value = 0; return false;
        }
    }

    protected override bool TrySetTyped(string property, double value)
    {
        switch (property.ToLowerInvariant())
        {
            case "offsx": OffsetX = value; return true;
            case "offsy": OffsetY = value; return true;
            default: return false;
        }
    }
}
