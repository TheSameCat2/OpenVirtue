// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

/// <summary>The shade level and tables against OV-20261029-shading (a 32-colour gray RANGE).</summary>
public class PaletteShadingTests
{
    [Theory]
    // PLAYER_LIGHT 1, LIGHT_DIST 10: the floor's levels between their measured boundaries.
    [InlineData(42.0, 1, 10, 0, 22)]
    [InlineData(33.0, 1, 10, 0, 20)]
    [InlineData(27.0, 1, 10, 0, 18)]
    [InlineData(16.0, 1, 10, 0, 10)]
    [InlineData(11.0, 1, 10, 0, 2)]
    [InlineData(9.0, 1, 10, 0, 0)]
    // PLAYER_LIGHT 0.5 and 0.25.
    [InlineData(42.0, 0.5, 10, 0, 26)]
    [InlineData(10.0, 0.5, 10, 0, 14)]
    [InlineData(6.0, 0.5, 10, 0, 4)]
    [InlineData(42.0, 0.25, 10, 0, 28)]
    [InlineData(25.0, 0.25, 10, 0, 26)]
    // LIGHT_DIST 20 and 5.
    [InlineData(45.0, 1, 20, 0, 16)]
    [InlineData(22.0, 1, 20, 0, 2)]
    [InlineData(40.0, 1, 5, 0, 26)]
    [InlineData(6.0, 1, 5, 0, 4)]
    // PLAYER_LIGHT 0: dark everywhere; region AMBIENT 0.5 brightens by 16 levels.
    [InlineData(30.0, 0, 10, 0, 30)]
    [InlineData(30.0, 0, 10, 0.5, 14)]
    [InlineData(40.0, 1, 10, 0.5, 6)]
    public void Level_MatchesTheMeasuredFloor(double distance, double playerLight, double lightDistance, double ambient, int expected)
    {
        Assert.Equal(expected, PaletteShading.Level(distance, playerLight, lightDistance, ambient));
    }

    [Fact]
    public void BuildTable_SortedRange_MovesAlongTheRangeAndLeavesOtherColoursAlone()
    {
        byte[] palette = new byte[768];
        byte[] table = PaletteShading.BuildTable(palette, [(16, 32)], autoRange: false);

        Assert.Equal(16, table[(16 * 32) + 0]);
        Assert.Equal(16 + 22, table[(16 * 32) + 22]);
        Assert.Equal(47, table[(40 * 32) + 30]); // clamped to the range's darkest colour
        Assert.Equal(5, table[(5 * 32) + 30]); // outside every range: not shaded
    }

    [Fact]
    public void BuildTable_AutoRange_FadesTowardTheInfinityColour()
    {
        byte[] palette = new byte[768];
        palette[3] = palette[4] = palette[5] = 0; // INFINITY: black
        palette[30] = 255; // index 10: pure red
        palette[33] = 128; // index 11: dark red
        byte[] table = PaletteShading.BuildTable(palette, [(1, 20)], autoRange: true);

        Assert.Equal(10, table[(10 * 32) + 0]);
        Assert.Equal(11, table[(10 * 32) + 16]);
        Assert.Equal(1, table[(10 * 32) + 31]);
    }
}
