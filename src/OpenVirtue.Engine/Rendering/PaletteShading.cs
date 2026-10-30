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
    /// A shade table: for each palette index and level (0…31), the index drawn, as measured in
    /// OV-20261029-autorange. Colours outside every range are not shaded (manual p.34). The
    /// <c>INFINITY</c> colour is index 1. The colour i places into a sorted range of length n moves
    /// <c>⌊(n + 1 − i)·level / 32⌋</c> places toward the range's dark end. A move past the end gives
    /// index 1. With <paramref name="autoRange"/> a colour in any range instead fades toward index 1
    /// in 6-bit VGA values, <c>(c·(32 − level) + infinity·level) >> 5</c> per channel. It then takes
    /// the nearest colour of the ranges or index 1 by squared RGB distance, the lower index on a tie.
    /// </summary>
    /// <param name="palette">256 colours, 3 bytes each (8-bit channels; the engine compares the top 6 bits).</param>
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

        if (autoRange)
        {
            var vga = new int[256 * 3];
            for (int i = 0; i < vga.Length; i++)
            {
                vga[i] = palette[i] >> 2;
            }

            var inRange = new bool[256];
            foreach ((int start, int length) in ranges)
            {
                for (int i = Math.Max(0, start); i < Math.Min(256, start + length); i++)
                {
                    inRange[i] = true;
                }
            }

            int[] candidates = [1, .. Enumerable.Range(0, 256).Where(i => inRange[i] && i != 1)];
            Array.Sort(candidates);
            for (int index = 0; index < 256; index++)
            {
                if (!inRange[index])
                {
                    continue;
                }

                for (int level = 1; level < Levels; level++)
                {
                    int r = ((vga[index * 3] * (32 - level)) + (vga[3] * level)) >> 5;
                    int g = ((vga[(index * 3) + 1] * (32 - level)) + (vga[4] * level)) >> 5;
                    int b = ((vga[(index * 3) + 2] * (32 - level)) + (vga[5] * level)) >> 5;
                    table[(index * Levels) + level] = (byte)Nearest(vga, candidates, r, g, b);
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
                int place = index - start;
                for (int level = 0; level < Levels; level++)
                {
                    int moved = place + ((length + 1 - place) * level / 32);
                    table[(index * Levels) + level] = (byte)(moved < length ? Math.Min(last, start + moved) : 1);
                }
            }
        }

        return table;
    }

    /// <summary>The candidate nearest (r, g, b) by squared distance; the lowest index on a tie.</summary>
    private static int Nearest(int[] vga, int[] candidates, int r, int g, int b)
    {
        int best = candidates[0];
        int bestDistance = int.MaxValue;
        foreach (int index in candidates)
        {
            int dr = vga[index * 3] - r;
            int dg = vga[(index * 3) + 1] - g;
            int db = vga[(index * 3) + 2] - b;
            int distance = (dr * dr) + (dg * dg) + (db * db);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        return best;
    }
}
