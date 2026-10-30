// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Tests;

/// <summary>
/// Region slopes as measured in OV-20261029-slope: a square region (floor 0, ceiling 16) whose
/// corners have the heights 0, 2, 4, 2, with one slope flag at a time; the readings at (−11, 13)
/// and (−5, 19) were 0.5 and 3.5 above or below the flat height.
/// </summary>
public class SlopeTests
{
    private const string Map =
        "VERTEX -12 12 0;#0\nVERTEX -4 12 2;#1\nVERTEX -4 20 4;#2\nVERTEX -12 20 2;#3\n" +
        "REGION pad 0 16;#0\n" +
        "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\nWALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\n" +
        "PLAYER_START -8 16 0 0;#4";

    [Theory]
    [InlineData("", 0, 0, 16, 16)]
    [InlineData("FLOOR_ASCEND", 0.5, 3.5, 16, 16)]
    [InlineData("FLOOR_DESCEND", -0.5, -3.5, 16, 16)]
    [InlineData("FLOOR_LIFTED", 0.5, 3.5, 16, 16)]
    [InlineData("CEIL_ASCEND", 0, 0, 16.5, 19.5)]
    [InlineData("CEIL_DESCEND", 0, 0, 15.5, 12.5)]
    [InlineData("CEIL_LIFTED", 0, 0, 15.5, 12.5)]
    [InlineData("FLOOR_ASCEND, CEIL_ASCEND", 0.5, 3.5, 16.5, 19.5)]
    public void FloorAndCeilingHeightAt_FollowTheSlopeFlags(string flags, double floorP0, double floorP2, double ceilP0, double ceilP2)
    {
        string main = "MAPFILE <m.wmp>;" + (flags.Length == 0 ? "" : $" REGION pad {{ FLAGS {flags}; }}");
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = Map };
        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(floorP0, LevelTopology.FloorHeightAt(level, 0, -11, 13), 6);
        Assert.Equal(floorP2, LevelTopology.FloorHeightAt(level, 0, -5, 19), 6);
        Assert.Equal(ceilP0, LevelTopology.CeilingHeightAt(level, 0, -11, 13), 6);
        Assert.Equal(ceilP2, LevelTopology.CeilingHeightAt(level, 0, -5, 19), 6);
    }

    [Theory]
    [InlineData(-8, 16, 2.5)] // the centre: bilinear, not on either triangle's plane (5 or 0)
    [InlineData(-11.5, 12.5, 0.5859375)]
    [InlineData(-4.5, 18, 2.34375)]
    public void FloorHeightAt_NonPlanarSquare_IsBilinear(double x, double y, double expected)
    {
        // OV-20261029-slope round 2: FLOOR_LIFTED with corner heights 0, 10, 0, 0.
        const string map =
            "VERTEX -12 12 0;#0\nVERTEX -4 12 10;#1\nVERTEX -4 20 0;#2\nVERTEX -12 20 0;#3\n" +
            "REGION pad 0 16;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\nWALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\n" +
            "PLAYER_START -8 16 0 0;#4";
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        Level level = LevelLoader.LoadCore("x", "MAPFILE <m.wmp>; REGION pad { FLAGS FLOOR_LIFTED; }", n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(expected, LevelTopology.FloorHeightAt(level, 0, x, y), 4);
    }
}
