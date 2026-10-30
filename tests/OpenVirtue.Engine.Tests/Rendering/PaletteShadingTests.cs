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
    // Round 3: AMBIENT −0.25 darkens by 8 levels up to 30, and AMBIENT 1 leaves level 0 everywhere.
    [InlineData(40.0, 1, 10, -0.25, 30)]
    [InlineData(16.0, 1, 10, -0.25, 18)]
    [InlineData(40.0, 1, 10, 1, 0)]
    // Region 0 plus the floor texture's AMBIENT 0.25: 8 levels brighter.
    [InlineData(40.0, 1, 10, 0.25, 14)]
    public void Level_MatchesTheMeasuredFloor(double distance, double playerLight, double lightDistance, double ambient, int expected)
    {
        Assert.Equal(expected, PaletteShading.Level(distance, playerLight, lightDistance, ambient));
    }

    [Theory]
    [InlineData(32, 2, new[] { 2, 2, 1, 1 }, 18, 0)]
    [InlineData(32, 30, new[] { 30, 30, 29, 28 }, 17, -1)]
    [InlineData(16, 6, new[] { 3, 3, 2, 2 }, 12, 0)]
    [InlineData(8, 6, new[] { 1, 1, 1, 1 }, 4, 0)]
    [InlineData(64, 2, new[] { 4, 4, 3, 3 }, 50, 0)]
    public void BuildTable_SortedRange_MovesEachColourAsMeasured(int length, int level, int[] firstMoves, int place, int moveAtPlace)
    {
        // OV-20261029-autorange: colour i moves floor((n + 1 - i) * level / 32) places; past the end
        // it becomes the INFINITY colour (index 1), shown here as a move of -1.
        const int start = 80;
        byte[] palette = new byte[768];
        byte[] table = PaletteShading.BuildTable(palette, [(start, length)], autoRange: false);

        int Move(int i)
        {
            int shaded = table[((start + i) * 32) + level];
            return shaded == 1 ? -1 : shaded - start - i;
        }

        Assert.Equal(firstMoves, Enumerable.Range(0, firstMoves.Length).Select(Move));
        Assert.Equal(moveAtPlace, Move(place));
        Assert.Equal(start + 3, table[((start + 3) * 32) + 0]);
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

    [Fact]
    public void BuildTable_AutoRange_FadesInWholeSixBitSteps()
    {
        // OV-20261029-autorange, colour 7 of the probe palette at level 2: the 6-bit target
        // (32·30 + 8·2) >> 5 … = (30, 16, 42) is nearer colour 69 than colour 7 itself, though the
        // exact fade (30.5, 16.7, 42.7) would be nearer colour 7.
        byte[] palette = new byte[768];
        void Set(int index, int r, int g, int b) =>
            (palette[index * 3], palette[(index * 3) + 1], palette[(index * 3) + 2]) = ((byte)(r * 4), (byte)(g * 4), (byte)(b * 4));
        Set(1, 8, 12, 24);
        Set(7, 32, 17, 44);
        Set(69, 31, 14, 43);
        byte[] table = PaletteShading.BuildTable(palette, [(2, 254)], autoRange: true);

        Assert.Equal(69, table[(7 * 32) + 2]);
    }
}
