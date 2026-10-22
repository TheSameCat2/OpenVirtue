// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Map;

namespace OpenVirtue.Engine.Tests;

public class AcknexObjectTests
{
    [Fact]
    public void UnknownSkill_DefaultsToZero()
    {
        var region = new Region("amphi");

        Assert.Equal(0, region["neverSet"]);
    }

    [Fact]
    public void DynamicSkill_RoundTrips()
    {
        var region = new Region("amphi");

        region["customSkill"] = 5;

        Assert.Equal(5, region["customSkill"]);
        Assert.True(region.HasSkill("customSkill"));
    }

    [Fact]
    public void TypedField_AccessibleReflectivelyBothWays()
    {
        var region = new Region("amphi");

        region.FloorHeight = 40;
        Assert.Equal(40, region["floor_hgt"]);     // typed -> reflective read

        region["ceil_hgt"] = 10;
        Assert.Equal(10, region.CeilHeight);        // reflective write -> typed
        Assert.False(region.HasSkill("ceil_hgt"));  // did NOT fall through to the dynamic table

        region["AMBIENT"] = 0.25;
        Assert.Equal(0.25, region.Ambient);
        Assert.False(region.HasSkill("AMBIENT"));

        region["FLOOR_OFFS_X"] = 8;
        region["FLOOR_OFFS_Y"] = 9;
        region["CEIL_OFFS_X"] = 10;
        region["CEIL_OFFS_Y"] = 11;
        Assert.Equal(8, region.FloorOffsetX);
        Assert.Equal(9, region.FloorOffsetY);
        Assert.Equal(10, region.CeilOffsetX);
        Assert.Equal(11, region.CeilOffsetY);
        Assert.False(region.HasSkill("FLOOR_OFFS_X"));
    }

    [Fact]
    public void WallOffsets_AccessibleReflectivelyByDocumentedNames()
    {
        var wall = new Wall("stone");

        wall["OFFSET_X"] = 12;
        wall["OFFSET_Y"] = 34;

        Assert.Equal(12, wall.OffsetX);
        Assert.Equal(34, wall.OffsetY);
        Assert.Equal(12, wall["offsx"]);
        Assert.Equal(34, wall["offsy"]);
        Assert.False(wall.HasSkill("OFFSET_X"));
        Assert.False(wall.HasSkill("OFFSET_Y"));
    }

    [Fact]
    public void ReflectiveAccess_IsCaseInsensitive()
    {
        var thing = new Thing("plant");

        thing["X"] = 3;
        thing["ANGLE"] = Math.PI / 2;

        Assert.Equal(3, thing.X);
        Assert.Equal(Math.PI / 2, thing.Angle);
        Assert.Equal(3, thing["x"]);
    }

    [Fact]
    public void MapEntity_PoseFields_RoundTrip()
    {
        var actor = new Actor("worldlyHead")
        {
            X = 101.4,
            Y = 603.8,
            PlacementAngleDegrees = 90,
            Angle = Math.PI,
            Height = 2.5,
            Region = 7,
        };

        Assert.Equal(101.4, actor["x"]);
        Assert.Equal(603.8, actor["y"]);
        Assert.Equal(Math.PI, actor["angle"]);
        Assert.Equal(2.5, actor["height"]);
        Assert.Equal(7, actor["region"]);
        Assert.Equal(Math.PI * 1.5, actor.FacingAngleRadians, precision: 12);

        actor["HEIGHT"] = -0.25;
        actor["REGION"] = 3;

        Assert.Equal(-0.25, actor.Height);
        Assert.Equal(3, actor.Region);
        Assert.False(actor.HasSkill("HEIGHT"));
        Assert.False(actor.HasSkill("REGION"));
    }

    [Theory]
    [InlineData("Ambient")]
    [InlineData("FLOOR_OFFS_X")]
    [InlineData("AMB\u0130ENT")]
    [InlineData("\u212Aambient")]
    [InlineData("FLOOR_OFFS_X_TOO_LONG")]
    [InlineData("")]
    public void TypedNames_MatchAsToLowerInvariantWould(string name)
    {
        var region = new Region("amphi");
        bool typed = name.ToLowerInvariant() is "ambient" or "floor_offs_x";

        region[name] = 7;

        Assert.Equal(typed, region.Ambient == 7 || region.FloorOffsetX == 7);
        Assert.Equal(!typed, region.HasSkill(name));
    }

    [Fact]
    public void RegionStringNames_AreCaseInsensitive()
    {
        var region = new Region("amphi");

        Assert.True(region.TrySetString("Floor_Tex", "grass"));
        Assert.True(region.TryGetString("FLOOR_TEX", out string? floor));
        Assert.Equal("grass", floor);
        Assert.True(region.TrySetString("EACH_CYCLE", "spin"));
        Assert.Equal("spin", region.EachCycleAction);
        Assert.False(region.TrySetString("FLOOR_TEXTURE_X", "grass"));
    }
}
