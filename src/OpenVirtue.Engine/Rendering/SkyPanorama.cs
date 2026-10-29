// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Rendering;

/// <summary>One stretch of screen columns that shows one sky side.</summary>
/// <param name="X0">First column (window pixels, from the left).</param>
/// <param name="X1">Column after the last.</param>
/// <param name="Side">The side (0-based, the texture's <c>BMAPS</c> order).</param>
/// <param name="U0">Horizontal texture coordinate (0…1 across the side's bitmap) at <paramref name="X0"/>.</param>
/// <param name="U1">Horizontal texture coordinate at <paramref name="X1"/>.</param>
public readonly record struct SkySpan(double X0, double X1, int Side, double U0, double U1);

/// <summary>
/// Where the original draws a <c>FLAGS SKY</c> texture, as measured in OV-20261029-sky:
/// <list type="bullet">
/// <item>the sides spread evenly over the full turn, side k centred on the direction k·2π/N, and the
/// panorama scales with the field of view: <c>SCREEN_WIDTH / PLAYER_ARC</c> pixels per radian;</item>
/// <item>column x shows the direction <c>PLAYER_ANGLE + PLAYER_ARC·(W − x)/W</c> — the right edge
/// the view angle itself, the left edge a whole <c>PLAYER_ARC</c> to the left — so the sky sits half
/// a field of view off the world, and <c>SKY_OFFS_X</c> moves it that many pixels right;</item>
/// <item>vertically it is not scaled: one window row per bitmap row, the bitmap's last row on the
/// eye line (<c>H/2 − PLAYER_TILT·H</c>), <c>SKY_OFFS_Y</c> rows lower, and the top and bottom rows
/// repeated above and below it (manual p.38).</item>
/// </list>
/// </summary>
public static class SkyPanorama
{
    /// <summary>The spans of columns 0…<paramref name="width"/> and the sides and bitmap columns they show.</summary>
    public static IReadOnlyList<SkySpan> Columns(double width, double playerArc, double playerAngle, int sides, double skyOffsetX)
    {
        var spans = new List<SkySpan>();
        if (width <= 0 || sides <= 0 || playerArc <= 0)
        {
            return spans;
        }

        // q counts sides to the right from side 0's left edge (side k covers q ∈ [−k, −k + 1)); it
        // grows linearly with the column, so each whole q is one boundary.
        double perColumn = playerArc * sides / (Math.Tau * width);
        double qAtZero = ((Math.PI / sides) - (playerAngle + (playerArc * (width + skyOffsetX) / width))) * sides / Math.Tau;
        double ColumnOf(double q) => (q - qAtZero) / perColumn;

        for (double cell = Math.Floor(qAtZero); ColumnOf(cell) < width; cell++)
        {
            double x0 = Math.Max(0, ColumnOf(cell));
            double x1 = Math.Min(width, ColumnOf(cell + 1));
            if (x1 - x0 < 1e-6)
            {
                continue;
            }

            int side = (int)((((-(long)cell) % sides) + sides) % sides);
            double u0 = Math.Clamp(qAtZero + (perColumn * x0) - cell, 0, 1);
            double u1 = Math.Clamp(qAtZero + (perColumn * x1) - cell, 0, 1);
            spans.Add(new SkySpan(x0, x1, side, u0, u1));
        }

        return spans;
    }

    /// <summary>
    /// The window row (pixel-edge coordinates) where the sky bitmap's top edge goes; its bottom edge is
    /// <paramref name="bitmapHeight"/> rows lower, one row below the eye line.
    /// </summary>
    public static double TopRow(double height, double playerTilt, int bitmapHeight, double skyOffsetY) =>
        (height * 0.5) - (playerTilt * height) - bitmapHeight + 1 + skyOffsetY;
}
