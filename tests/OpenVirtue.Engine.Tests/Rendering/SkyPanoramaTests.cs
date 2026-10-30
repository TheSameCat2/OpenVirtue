// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

/// <summary>
/// The sky panorama against the screenshots of OV-20261029-sky (640 × 480 window, four 64 × 64
/// sides): the column where side 0 begins and the row of the bitmap's first line.
/// </summary>
public class SkyPanoramaTests
{
    [Theory]
    [InlineData(0.0, 1.0, 0.0, 137)] // angle-0: yellow (side 1's right half) up to 136, red from 137
    [InlineData(0.199, 1.0, 0.0, 265)] // angle-0.2
    [InlineData(0.399, 1.0, 0.0, 393)] // angle-0.4
    [InlineData(0.0, 2.0, 0.0, 389)] // arc-2
    [InlineData(0.0, 1.0, 16.0, 153)] // offs-x
    public void Columns_Side0BeginsWhereTheOriginalDrewIt(double angle, double arc, double offsetX, int firstRedColumn)
    {
        IReadOnlyList<SkySpan> spans = SkyPanorama.Columns(640, arc, angle, 4, offsetX);

        SkySpan side0 = Assert.Single(spans, static s => s.Side == 0);
        // A column shows side 0 when its centre (c + 0.5) is past the boundary.
        Assert.InRange(side0.X0, firstRedColumn - 0.5, firstRedColumn + 0.5);
        Assert.Equal(0, side0.U0, 3);
        SkySpan before = Assert.Single(spans, static s => s.Side == 1);
        Assert.True(before.U1 > 0.99); // the end of side 1, its right half
    }

    [Fact]
    public void Columns_QuarterTurn_ShowsSide1FromColumn156()
    {
        // angle-1.6: side 2's right half (yellow) up to 155, side 1's left half (green) from 156.
        IReadOnlyList<SkySpan> spans = SkyPanorama.Columns(640, 1, 1.6, 4, 0);

        SkySpan side1 = Assert.Single(spans, static s => s.Side == 1);
        Assert.InRange(side1.X0, 155.5, 156.5);
        Assert.Contains(spans, static s => s.Side == 2);
    }

    [Theory]
    [InlineData(0.0, 0.0, 177)] // high: the band (bitmap rows 16-47) at window rows 193-224
    [InlineData(-0.1, 0.0, 225)] // high-tilt-up: band at 240-271 (the original moved it 47 rows)
    [InlineData(0.1, 0.0, 129)] // high-tilt-down: band at 145-176
    [InlineData(0.0, 8.0, 185)] // high-offs-y: band at 201-
    public void TopRow_BitmapSitsOnTheEyeLine(double tilt, double offsetY, double expectedTop)
    {
        Assert.Equal(expectedTop, SkyPanorama.TopRow(480, tilt, 64, offsetY), 0);
    }
}
