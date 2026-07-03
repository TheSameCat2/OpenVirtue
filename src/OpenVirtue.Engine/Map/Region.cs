// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Map;

/// <summary>A sector with a floor and ceiling height and surface textures.</summary>
public sealed class Region : AcknexObject
{
    public Region(string name) : base(name) { }

    public double FloorHeight { get; set; }
    public double CeilHeight { get; set; }
    public double Ambient { get; set; }
    public double FloorOffsetX { get; set; }
    public double FloorOffsetY { get; set; }
    public double CeilOffsetX { get; set; }
    public double CeilOffsetY { get; set; }
    public string? FloorTexture { get; set; }
    public string? CeilTexture { get; set; }

    public override bool TryGetString(string name, out string? value)
    {
        switch (name.ToLowerInvariant())
        {
            case "floor_tex": value = FloorTexture; return true;
            case "ceil_tex": value = CeilTexture; return true;
            default: value = null; return false;
        }
    }

    public override bool TrySetString(string name, string? value)
    {
        switch (name.ToLowerInvariant())
        {
            case "floor_tex": FloorTexture = value; return true;
            case "ceil_tex": CeilTexture = value; return true;
            default: return false;
        }
    }

    protected override bool TryGetTyped(string name, out double value)
    {
        switch (name.ToLowerInvariant())
        {
            case "floor_hgt": value = FloorHeight; return true;
            case "ceil_hgt": value = CeilHeight; return true;
            case "ambient": value = Ambient; return true;
            case "floor_offs_x": value = FloorOffsetX; return true;
            case "floor_offs_y": value = FloorOffsetY; return true;
            case "ceil_offs_x": value = CeilOffsetX; return true;
            case "ceil_offs_y": value = CeilOffsetY; return true;
            default: value = 0; return false;
        }
    }

    protected override bool TrySetTyped(string name, double value)
    {
        switch (name.ToLowerInvariant())
        {
            case "floor_hgt": FloorHeight = value; return true;
            case "ceil_hgt": CeilHeight = value; return true;
            case "ambient": Ambient = value; return true;
            case "floor_offs_x": FloorOffsetX = value; return true;
            case "floor_offs_y": FloorOffsetY = value; return true;
            case "ceil_offs_x": CeilOffsetX = value; return true;
            case "ceil_offs_y": CeilOffsetY = value; return true;
            default: return false;
        }
    }
}
