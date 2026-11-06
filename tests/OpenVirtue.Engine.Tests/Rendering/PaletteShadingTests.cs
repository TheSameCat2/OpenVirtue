// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

/// <summary>
/// The shade level, its zones and the wall step against OV-20261105-shade-global (a 32-colour gray
/// RANGE): <c>level = 2·⌊(30 + φ − 32·A − 2·m)/2⌋</c> with <c>m = ⌈Qf/d⌉</c> for a surface,
/// <c>⌊Qw/d⌋</c> for a sprite and <c>⌈mw⌉</c> for a wall.
/// </summary>
public class PaletteShadingTests
{
    /// <summary>Retail's light is <c>PLAYER_LIGHT</c> 0.199219 (204/1024) and <c>LIGHT_DIST</c> 20.</summary>
    private const double RetailLight = 204 / 1024.0;

    private static readonly double[] QfRetail = [PaletteShading.SurfaceQ(RetailLight, 20)];

    [Theory]
    // Floors, PLAYER_LIGHT 0.199 (204/1024), LIGHT_DIST 20, AMBIENT 0: the level bands by depth. m
    // runs from 1 (level 28 beyond d ≈ 64.07) to 15 (level 0 on d 10.04…10.75); the measured ones are
    // 26 beyond ≈ 32.03, 24 nearer, 22 nearer than ≈ 21.36, 20 nearer than ≈ 16.02, 18 nearer than
    // ≈ 12.81, down to 6 nearer than ≈ 5.82.
    [InlineData(70.0, 28)]
    [InlineData(40.0, 26)]
    [InlineData(32.1, 26)]
    [InlineData(32.0, 24)]
    [InlineData(25.0, 24)]
    [InlineData(21.4, 24)]
    [InlineData(21.3, 22)]
    [InlineData(20.0, 22)]
    [InlineData(16.1, 22)]
    [InlineData(16.0, 20)]
    [InlineData(13.0, 20)]
    [InlineData(12.8, 18)]
    [InlineData(12.0, 18)]
    [InlineData(10.0, 16)]
    [InlineData(9.5, 16)]
    [InlineData(9.0, 14)]
    [InlineData(8.5, 14)]
    [InlineData(8.0, 12)]
    [InlineData(6.0, 8)]
    [InlineData(5.5, 6)]
    [InlineData(5.3, 4)]
    [InlineData(11.0, 18)]
    public void SurfaceLevel_RetailLight_FallsInTheMeasuredBands(double distance, int expected) =>
        Assert.Equal(expected, PaletteShading.SurfaceLevel(distance, RetailLight, 20, 0));

    [Theory]
    // Floors, PLAYER_LIGHT 1, LIGHT_DIST 10: level 22 on d 37.63…50.18 and 20 on 30.11…37.63, then 18,
    // 16, … and 0 nearer than 10.75.
    [InlineData(40.0, 22)]
    [InlineData(47.0, 22)]
    [InlineData(35.0, 20)]
    [InlineData(30.2, 20)]
    [InlineData(30.0, 18)]
    [InlineData(27.0, 18)]
    [InlineData(22.0, 16)]
    [InlineData(20.0, 14)]
    [InlineData(17.0, 12)]
    [InlineData(16.0, 10)]
    [InlineData(15.0, 8)]
    [InlineData(14.0, 8)]
    [InlineData(13.0, 6)]
    [InlineData(12.0, 4)]
    [InlineData(11.0, 2)]
    [InlineData(10.5, 0)]
    [InlineData(10.0, 0)]
    public void SurfaceLevel_StrongLight_FallsInTheMeasuredBands(double distance, int expected) =>
        Assert.Equal(expected, PaletteShading.SurfaceLevel(distance, 1, 10, 0));

    [Theory]
    // The same at PLAYER_LIGHT 0.199, LIGHT_DIST 20 with the ambient added: the level beyond the first
    // band and the one nearer, per ambient. A = the sum of the global and region AMBIENT.
    [InlineData(0.1992, 20, 18)]
    [InlineData(0.2002, 20, 18)]
    [InlineData(0.2998, 16, 14)]
    [InlineData(0.499, 10, 8)]
    [InlineData(0.5, 10, 8)]
    [InlineData(-0.0996, 28, 26)]
    public void SurfaceLevel_RetailLightAmbient_MovesTheLevelsByEightPerUnit(double ambient, int beyond, int nearer)
    {
        Assert.Equal(beyond, PaletteShading.SurfaceLevel(40, RetailLight, 20, ambient));
        Assert.Equal(nearer, PaletteShading.SurfaceLevel(24, RetailLight, 20, ambient));
    }

    [Theory]
    // With no light the level is flat: 30 − 32·A rounded down to an even level.
    [InlineData(0.2002, 22)]
    [InlineData(0.2998, 20)]
    [InlineData(0.499, 14)]
    public void SurfaceLevel_NoLight_IsFlatInTheAmbient(double ambient, int expected)
    {
        Assert.Equal(expected, PaletteShading.SurfaceLevel(20, 0, 20, ambient));
        Assert.Equal(expected, PaletteShading.SurfaceLevel(45, 0, 20, ambient));
    }

    [Theory]
    // The region AMBIENT in 128ths: the level steps down between k = 1 and 2 and between 9 and 10 at
    // LIGHT_DIST 10 (p 0.199, 0.5 and 1 alike), and between 2 and 3 and 10 and 11 at LIGHT_DIST 20.
    [InlineData(0.1992, 10, 1, 9)]
    [InlineData(0.5, 10, 1, 9)]
    [InlineData(1.0, 10, 1, 9)]
    [InlineData(0.1992, 20, 2, 10)]
    public void SurfaceLevel_RegionAmbientIn128ths_StepsDownWhereMeasured(
        double playerLight, double lightDistance, int first, int second)
    {
        var flips = new List<int>();
        int previous = PaletteShading.SurfaceLevel(30, playerLight, lightDistance, 0);
        for (int k = 1; k <= 15; k++)
        {
            int level = PaletteShading.SurfaceLevel(30, playerLight, lightDistance, k / 128.0);
            if (level < previous)
            {
                flips.Add(k - 1);
            }

            previous = level;
        }

        Assert.Equal([first, second], flips);
    }

    [Theory]
    // Sprites, PLAYER_LIGHT 1, LIGHT_DIST 10 unless noted: floor(Qw/d) zones, one fewer than a floor's
    // where the light reaches.
    [InlineData(20.0, 1, 10, 0, 16)]
    [InlineData(28.0, 1, 10, 0, 20)]
    [InlineData(36.0, 1, 10, 0, 22)]
    [InlineData(44.0, 1, 10, 0, 24)]
    [InlineData(28.0, 1, 10, 0.25, 12)]
    [InlineData(15.2, 1, 10, 0, 12)] // 9.969 zones floor to 9
    [InlineData(19.0, 1, 10, 0, 16)] // 7.976 floor to 7
    [InlineData(30.4, 1, 10, 0, 22)] // 4.985 floor to 4
    [InlineData(38.0, 1, 10, 0, 24)] // 3.988 floor to 3
    [InlineData(28.0, 0, 10, 0, 30)]
    [InlineData(20.0, 1, 10, 0.5, 0)]
    [InlineData(28.0, 0, 10, 0.25, 22)]
    [InlineData(20.0, 0.1, 10, 0.25, 22)] // 0.83 zones floor to 0
    public void SpriteLevel_FloorsTheWallZones(double distance, double playerLight, double lightDistance, double ambient, int expected) =>
        Assert.Equal(expected, PaletteShading.SpriteLevel(distance, playerLight, lightDistance, ambient));

    [Theory]
    // Frontal walls (slope 0): mw = Qw/d, m = ceil, so the level changes a whole zone later than a
    // floor's at the same depth.
    [InlineData(25.0, 16)]
    [InlineData(25.5, 18)]
    [InlineData(30.0, 18)]
    [InlineData(30.5, 20)]
    [InlineData(37.5, 20)]
    [InlineData(38.0, 22)]
    public void WallLevel_StrongLightFrontal_StepsAtTheMeasuredDepths(double distance, int expected) =>
        Assert.Equal(expected, PaletteShading.WallLevel(PaletteShading.WallQ(1, 10) / distance, 1, 10, 0));

    [Theory]
    [InlineData(32.0, 24)]
    [InlineData(33.0, 26)]
    [InlineData(60.0, 26)]
    [InlineData(61.0, 26)]
    public void WallLevel_RetailLightFrontal_StepsAtTheMeasuredDepths(double distance, int expected) =>
        Assert.Equal(expected, PaletteShading.WallLevel(PaletteShading.WallQ(RetailLight, 20) / distance, RetailLight, 20, 0));

    [Theory]
    // OV-20261105-shade-global: r = ceil(1024·s)/1024, so the step a column covers reads one unit more
    // than true for either sign; a frontal wall's step is 0 and float noise under 1e-3 is 0 too.
    [InlineData(-16.5 / 1024, -16.0 / 1024)]
    [InlineData(16.5 / 1024, 17.0 / 1024)]
    [InlineData(-6.74 / 1024, -6.0 / 1024)]
    [InlineData(6.74 / 1024, 7.0 / 1024)]
    [InlineData(-4.40 / 1024, -4.0 / 1024)]
    [InlineData(4.40 / 1024, 5.0 / 1024)]
    [InlineData(-1.87 / 1024, -1.0 / 1024)]
    [InlineData(1.87 / 1024, 2.0 / 1024)]
    [InlineData(0, 0)]
    [InlineData(0.0004 / 1024, 0)]
    public void WallStep_RoundsTheSlopeUpToA1024th(double slope, double expectedStep)
    {
        double start = 3.25;
        Assert.Equal(expectedStep, PaletteShading.WallStep(start, slope, 1, 0) - start, 12);
        Assert.Equal(expectedStep * 100, PaletteShading.WallStep(start, slope, 100, 0) - start, 10);
        Assert.Equal(start, PaletteShading.WallStep(start, slope, 0, 0), 12);
    }

    [Fact]
    public void WallStep_MovesFromTheWallsFirstVisibleColumn()
    {
        // A wall whose true zones fall 6.74/1024 a column from column 240: 60 columns on it reads
        // 6.74·60 = 0.395 a zone less than at the anchor, but the step gains a 1024th each column.
        double start = 2.5;
        double slope = -6.74 / 1024;
        Assert.Equal(start - (6.0 / 1024 * 60), PaletteShading.WallStep(start, slope, 300, 240), 12);
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(0, 20, 0)]
    [InlineData(1, 10, 0.3125)]
    [InlineData(0.5, 20, 0.625)]
    public void Phi_AddsTheLightDistanceOver32WhileTheLightBurns(double playerLight, double lightDistance, double expected) =>
        Assert.Equal(expected, PaletteShading.Phi(playerLight, lightDistance), 6);

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(-1, 10, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 10, 150.5375)]
    [InlineData(0.19921875, 20, 64.065625)]
    public void SurfaceQ_FitsTheMeasuredZones(double playerLight, double lightDistance, double expected) =>
        Assert.Equal(expected, PaletteShading.SurfaceQ(playerLight, lightDistance), 6);

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 151.5375)]
    [InlineData(0.19921875, 20, 65.065625)]
    public void WallQ_AddsOneZoneToTheFit(double playerLight, double lightDistance, double expected) =>
        Assert.Equal(expected, PaletteShading.WallQ(playerLight, lightDistance), 6);

    [Fact]
    public void Zones_AreZeroWithoutAPlayerLight_WhateverTheDistance()
    {
        Assert.Equal(0, PaletteShading.SurfaceQ(0, 10));
        Assert.Equal(0, PaletteShading.WallQ(0, 10));
        Assert.Equal(30, PaletteShading.SurfaceLevel(20, 0, 10, 0));
        Assert.Equal(30, PaletteShading.SpriteLevel(20, 0, 10, 0));
        Assert.Equal(30, PaletteShading.WallLevel(PaletteShading.WallQ(0, 10) / 20, 0, 10, 0));
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
