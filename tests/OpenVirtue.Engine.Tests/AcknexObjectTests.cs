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
    }

    [Fact]
    public void ReflectiveAccess_IsCaseInsensitive()
    {
        var thing = new Thing("plant");

        thing["X"] = 3;
        thing["ANGLE"] = 90;

        Assert.Equal(3, thing.X);
        Assert.Equal(90, thing.Angle);
        Assert.Equal(3, thing["x"]);
    }

    [Fact]
    public void MapEntity_PoseFields_RoundTrip()
    {
        var actor = new Actor("worldlyHead") { X = 101.4, Y = 603.8, Angle = 180 };

        Assert.Equal(101.4, actor["x"]);
        Assert.Equal(603.8, actor["y"]);
        Assert.Equal(180, actor["angle"]);
    }
}
