// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Rendering;

/// <summary>
/// The original's palette shading, as measured in OV-20261029-shading. A texel's shade level runs
/// from 0 (its own colour) to 30 (the far end of its <c>RANGE</c>) in steps of 2 — 16 grades over a
/// 32-colour range — and is <c>30 − 32·AMBIENT − 30.4·PLAYER_LIGHT·LIGHT_DIST/d</c> rounded down to
/// an even level and clamped to 0…30, d being the distance from the eye: the player's light falls off
/// as the inverse distance, region <c>AMBIENT</c> 0.5 brightens by 16 levels, and with
/// <c>PLAYER_LIGHT</c> 0 the level is 30 everywhere. The coefficient is the mean of seven fits
/// (29.9 to 31.0) over <c>PLAYER_LIGHT</c> 0.25–1 and <c>LIGHT_DIST</c> 5–20.
/// </summary>
public static class PaletteShading
{
    /// <summary>The darkest level used.</summary>
    public const int DarkestLevel = 30;

    /// <summary>The number of levels a shade table holds per colour (0…31).</summary>
    public const int Levels = 32;

    /// <summary>The shade level at <paramref name="distance"/> from the eye.</summary>
    /// <param name="distance">The texel's distance from the eye, in steps.</param>
    /// <param name="playerLight">The <c>PLAYER_LIGHT</c> skill (0…1).</param>
    /// <param name="lightDistance">The <c>LIGHT_DIST</c> skill (default 10).</param>
    /// <param name="ambient">The sum of the region's and the texture's <c>AMBIENT</c>.</param>
    public static int Level(double distance, double playerLight, double lightDistance, double ambient)
    {
        double light = 30.4 * Math.Max(0, playerLight) * Math.Max(0, lightDistance) / Math.Max(distance, 0.001);
        double level = 30 - (32 * ambient) - light;
        return Math.Clamp(2 * (int)Math.Floor(level / 2), 0, DarkestLevel);
    }

    /// <summary>
    /// A shade table: for each palette index and level (0…31), the index drawn. Colours outside every
    /// range are not shaded (manual p.34). A sorted range moves a colour along it toward its darkest
    /// end; with <paramref name="autoRange"/> a colour in any range instead fades toward the
    /// <c>INFINITY</c> colour (index 1) and takes the nearest colour of the ranges (p.35).
    /// </summary>
    /// <param name="palette">256 colours, 3 bytes each.</param>
    /// <param name="ranges">The palette's <c>RANGE start, length</c> pairs.</param>
    /// <param name="autoRange">The palette's <c>AUTORANGE</c> flag.</param>
    public static byte[] BuildTable(ReadOnlySpan<byte> palette, IReadOnlyList<(int Start, int Length)> ranges, bool autoRange)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        if (palette.Length < 256 * 3)
        {
            throw new ArgumentException("A palette has 256 colours.", nameof(palette));
        }

        byte[] table = new byte[256 * Levels];
        for (int index = 0; index < 256; index++)
        {
            for (int level = 0; level < Levels; level++)
            {
                table[(index * Levels) + level] = (byte)index;
            }
        }

        var inRange = new bool[256];
        foreach ((int start, int length) in ranges)
        {
            for (int i = Math.Max(0, start); i < Math.Min(256, start + length); i++)
            {
                inRange[i] = true;
            }
        }

        if (autoRange)
        {
            int[] candidates = Enumerable.Range(0, 256).Where(i => inRange[i]).ToArray();
            (int R, int G, int B) infinity = (palette[3], palette[4], palette[5]);
            foreach (int index in candidates)
            {
                (int r, int g, int b) = (palette[index * 3], palette[(index * 3) + 1], palette[(index * 3) + 2]);
                for (int level = 1; level < Levels; level++)
                {
                    double t = level / (double)(Levels - 1);
                    double tr = r + ((infinity.R - r) * t);
                    double tg = g + ((infinity.G - g) * t);
                    double tb = b + ((infinity.B - b) * t);
                    table[(index * Levels) + level] = (byte)Nearest(palette, candidates, tr, tg, tb);
                }
            }

            return table;
        }

        foreach ((int start, int length) in ranges)
        {
            int first = Math.Max(0, start);
            int last = Math.Min(255, start + length - 1);
            for (int index = first; index <= last; index++)
            {
                for (int level = 0; level < Levels; level++)
                {
                    int shifted = index + (int)Math.Round(level * (length / (double)Levels));
                    table[(index * Levels) + level] = (byte)Math.Min(last, shifted);
                }
            }
        }

        return table;
    }

    private static int Nearest(ReadOnlySpan<byte> palette, int[] candidates, double r, double g, double b)
    {
        int best = candidates[0];
        double bestDistance = double.MaxValue;
        foreach (int index in candidates)
        {
            double dr = palette[index * 3] - r;
            double dg = palette[(index * 3) + 1] - g;
            double db = palette[(index * 3) + 2] - b;
            double distance = (dr * dr) + (dg * dg) + (db * db);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        return best;
    }
}
