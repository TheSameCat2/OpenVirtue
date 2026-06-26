// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Map;

/// <summary>A sector with a floor and ceiling height and surface textures.</summary>
public sealed class Region : AcknexObject
{
    public Region(string name) : base(name) { }

    public double FloorHeight { get; set; }
    public double CeilHeight { get; set; }
    public string? FloorTexture { get; set; }
    public string? CeilTexture { get; set; }

    protected override bool TryGetTyped(string name, out double value)
    {
        switch (name.ToLowerInvariant())
        {
            case "floor_hgt": value = FloorHeight; return true;
            case "ceil_hgt": value = CeilHeight; return true;
            default: value = 0; return false;
        }
    }

    protected override bool TrySetTyped(string name, double value)
    {
        switch (name.ToLowerInvariant())
        {
            case "floor_hgt": FloorHeight = value; return true;
            case "ceil_hgt": CeilHeight = value; return true;
            default: return false;
        }
    }
}
