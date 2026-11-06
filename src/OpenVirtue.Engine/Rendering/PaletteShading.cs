// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Rendering;

/// <summary>
/// The original's palette shading, as measured in OV-20261105-shade-global, which replaces the
/// continuous formula of OV-20261029-shading. A texel's shade level runs from 0 (its own colour) to
/// 30 (the far end of its <c>RANGE</c>) in steps of 2 — 16 grades over a 32-colour range — and is
/// <c>2·⌊(30 + φ − 32·A − 2·m)/2⌋</c>, where <c>A</c> is the sum of the global, region and texture
/// <c>AMBIENT</c>, <c>φ</c> is <c>LIGHT_DIST/32</c> while the player's light burns, and <c>m</c>
/// counts the light zones at the texel. Level 32 and up is past the range and shows the
/// <c>INFINITY</c> colour (palette entry 1).
/// </summary>
/// <remarks>
/// The zones come from the light alone, apart from <c>A</c>: <c>Qf = 15·PLAYER_LIGHT·LIGHT_DIST +
/// 4.3·(LIGHT_DIST/20)³</c> and <c>Qw = Qf + 1</c> when the light burns, 0 otherwise;
/// <c>m = ⌈Qf/d⌉</c> for a floor, ceiling or other quad, <c>⌊Qw/d⌋</c> for a sprite, and
/// <c>⌈mw⌉</c> for a wall, whose value <c>mw</c> steps across the screen at a rounded slope
/// (<see cref="WallStep"/>). <c>d</c> is the depth along the line of sight, so the inverse-distance
/// falloff of OV-20261029-shading survives as the zone count. <c>Qf</c> is a fit: measured at
/// <c>LIGHT_DIST</c> 10 and 20 and <c>PLAYER_LIGHT</c> 0.05…1, other distances are unmeasured.
/// </remarks>
public static class PaletteShading
{
    /// <summary>The darkest level of a range.</summary>
    public const int DarkestLevel = 30;

    /// <summary>
    /// The level past the range: a texel this dark shows the <c>INFINITY</c> colour, palette entry 1
    /// (OV-20261104-genius: a wall darkened past 30 by a lamp behind it).
    /// </summary>
    public const int InfinityLevel = 32;

    /// <summary>The number of levels a shade table holds per colour (0…31).</summary>
    public const int Levels = 32;

    /// <summary>
    /// Marks a sprite's batch for the renderer (OV-20261105-shade-global): a sprite floors its zones,
    /// <c>⌊Qw/d⌋</c>, where a floor or ceiling ceilings <c>Qf/d</c> and a wall steps them, so the
    /// shader has to tell the three apart. Only being above 0 matters.
    /// </summary>
    public const int SpriteMarker = 1;

    /// <summary>
    /// <c>Qf = 15·p·l + 4.3·(l/20)³</c> for <c>PLAYER_LIGHT</c> <paramref name="playerLight"/> and
    /// <c>LIGHT_DIST</c> <paramref name="lightDistance"/>, else 0 — the light's zone count per unit of
    /// depth, before a texel's own depth divides it. A fit to the measured light at 10 and 20 steps.
    /// </summary>
    public static double SurfaceQ(double playerLight, double lightDistance)
    {
        if (!(playerLight > 0) || !(lightDistance > 0))
        {
            return 0;
        }

        double scale = lightDistance / 20;
        return (15 * playerLight * lightDistance) + (4.3 * scale * scale * scale);
    }

    /// <summary><c>Qw = Qf + 1</c> when the light burns: one zone more, which a wall and a sprite count by.</summary>
    public static double WallQ(double playerLight, double lightDistance)
    {
        double surface = SurfaceQ(playerLight, lightDistance);
        return surface > 0 ? surface + 1 : 0;
    }

    /// <summary><c>φ = l/32</c> while the player's light burns, else 0: the half level the light adds.</summary>
    public static double Phi(double playerLight, double lightDistance) =>
        playerLight > 0 ? lightDistance / 32 : 0;

    /// <summary>A floor, ceiling or other quad's shade level at <paramref name="distance"/>: <c>⌈Qf/d⌉</c> zones.</summary>
    /// <param name="distance">The texel's depth along the line of sight, in steps.</param>
    /// <param name="playerLight">The <c>PLAYER_LIGHT</c> skill.</param>
    /// <param name="lightDistance">The <c>LIGHT_DIST</c> skill (default 10).</param>
    /// <param name="ambient">The sum of the global, region's and texture's <c>AMBIENT</c>.</param>
    public static int SurfaceLevel(double distance, double playerLight, double lightDistance, double ambient) =>
        LevelOf(Math.Ceiling(Zones(SurfaceQ(playerLight, lightDistance), distance)), playerLight, lightDistance, ambient);

    /// <summary>
    /// A sprite's shade level at <paramref name="distance"/>: <c>⌊Qw/d⌋</c> zones, one fewer than a
    /// floor's at the same depth wherever the light reaches (OV-20261105-shade-global, replacing the
    /// two levels OV-20261104-sprite-shade took off).
    /// </summary>
    public static int SpriteLevel(double distance, double playerLight, double lightDistance, double ambient) =>
        LevelOf(Math.Floor(Zones(WallQ(playerLight, lightDistance), distance)), playerLight, lightDistance, ambient);

    /// <summary>
    /// A wall's shade level from its stepped zone value <paramref name="wallZones"/> (see
    /// <see cref="WallStep"/>): <c>⌈mw⌉</c> zones.
    /// </summary>
    public static int WallLevel(double wallZones, double playerLight, double lightDistance, double ambient) =>
        LevelOf(Math.Ceiling(wallZones), playerLight, lightDistance, ambient);

    /// <summary>
    /// A wall's stepped zone value at a screen <paramref name="column"/>: <c>mStart + (column −
    /// startColumn)·r</c>, where the original advances by <c>r = ⌈1024·s⌉/1024</c> from the wall's
    /// first visible column <paramref name="startColumn"/> rather than by the true slope
    /// <paramref name="slope"/>. Moving right the zones therefore gain up to 1/1024 a column more than
    /// true, up slopes and down slopes alike: s −16.5/1024 reads −16/1024, +16.5 reads +17, −6.74
    /// reads −6, +6.74 reads +7. A frontal wall's slope is 0 exactly and its step is 0; a slope under
    /// 1e-3 of a 1/1024 unit is taken as 0, so float noise cannot round up to a whole unit.
    /// </summary>
    /// <param name="mStart">The true zone value at <paramref name="startColumn"/>.</param>
    /// <param name="slope">The true slope of the zone value per column (zones/column).</param>
    /// <param name="column">The column to evaluate.</param>
    /// <param name="startColumn">The wall's first visible column.</param>
    public static double WallStep(double mStart, double slope, double column, double startColumn)
    {
        double units = 1024 * slope;
        double step = Math.Abs(units) < 1e-3 ? 0 : Math.Ceiling(units) / 1024;
        return mStart + ((column - startColumn) * step);
    }

    /// <summary>The zone value <c>Q/d</c>, infinite (the darkest) at the eye itself.</summary>
    private static double Zones(double q, double distance) => q / Math.Max(distance, 0.001);

    /// <summary>
    /// The level an integer zone count gives: <c>2·⌊(30 + φ − 32·A − 2·m)/2⌋</c>, past the range at
    /// <see cref="InfinityLevel"/> and otherwise clamped to 0…30.
    /// </summary>
    private static int LevelOf(double zones, double playerLight, double lightDistance, double ambient)
    {
        double level = 2 * Math.Floor((DarkestLevel + Phi(playerLight, lightDistance) - (32 * ambient) - (2 * zones)) / 2);
        return level >= InfinityLevel ? InfinityLevel : (int)Math.Clamp(level, 0, DarkestLevel);
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
