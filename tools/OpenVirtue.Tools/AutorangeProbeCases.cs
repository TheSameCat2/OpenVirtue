// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>autorange</c> probe: the shade table the original builds from a palette
/// (manual p.34–35). A sorted <c>RANGE</c> runs from its brightest to its darkest colour; with
/// <c>FLAGS AUTORANGE</c> every colour in any range is shaded "according to their similarity to the
/// INFINITY colour", index 1, even unsorted. Neither the step through a range of a length other than
/// 32 nor the AUTORANGE rule is given.
/// <para>
/// The far wall's square target face shows all 256 palette indices as a 16 × 16 grid of cells
/// (<see cref="GridBitmap"/>). <c>PLAYER_LIGHT</c> 0 and the room's <c>AMBIENT</c> put every texel
/// on one shade level, <c>30 − 32·AMBIENT</c> (OV-20261029-shading), so one screenshot
/// (<c>OVARS0.PCX</c>) reads a whole level of the table. The <c>auto-*</c> cases use
/// <see cref="AutoPalette"/> (pseudo-random colours, a blue-gray INFINITY colour, one range over
/// 2–255, <c>AUTORANGE</c>); the <c>sorted-*</c> cases use <see cref="SortedPalette"/> (gray, red,
/// green and blue ramps of 32, 16, 8 and 64 colours, each its own range).
/// </para>
/// </summary>
internal static class AutorangeProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVARG0.TXT).</summary>
    public const string TraceFile = "ovarg";

    /// <summary>The INFINITY colour (index 1) of the auto palette.</summary>
    public static readonly (byte R, byte G, byte B) Infinity = (32, 48, 96);

    /// <summary>The sorted palette's ramps: first index, length, and the colour channel mask.</summary>
    public static readonly IReadOnlyList<(int Start, int Length, (int R, int G, int B) Mask)> Ramps =
    [
        (16, 32, (1, 1, 1)),
        (48, 16, (1, 0, 0)),
        (64, 8, (0, 1, 0)),
        (80, 64, (0, 0, 1)),
    ];

    /// <summary>The 64 × 64 target face: cell (x/4, y/4) in index y/4·16 + x/4.</summary>
    public static byte GridBitmap(int x, int y) => (byte)(((y / 4) * 16) + (x / 4));

    /// <summary>Pseudo-random colours 2–255 (a fixed LCG), black at 0 and <see cref="Infinity"/> at 1.</summary>
    public static SyntheticPcx.Palette AutoPalette()
    {
        var palette = new SyntheticPcx.Palette();
        palette.Set(0, 0, 0, 0);
        palette.Set(1, Infinity.R, Infinity.G, Infinity.B);
        uint state = 0x2026_1029;
        for (int i = 2; i < 256; i++)
        {
            byte Next()
            {
                state = (state * 1664525) + 1013904223;
                return (byte)(state >> 24);
            }

            palette.Set(i, Next(), Next(), Next());
        }

        return palette;
    }

    /// <summary>The <see cref="Ramps"/> from bright to black, black at 0 and 1, every other colour mid-gray.</summary>
    public static SyntheticPcx.Palette SortedPalette()
    {
        var palette = new SyntheticPcx.Palette();
        for (int i = 0; i < 256; i++)
        {
            palette.Set(i, 128, 128, 128);
        }

        palette.Set(0, 0, 0, 0);
        palette.Set(1, 0, 0, 0);
        foreach ((int start, int length, (int r, int g, int b)) in Ramps)
        {
            for (int i = 0; i < length; i++)
            {
                byte level = (byte)(248 - (i * 248 / (length - 1)));
                palette.Set(start + i, (byte)(r * level), (byte)(g * level), (byte)(b * level));
            }
        }

        return palette;
    }

    private static string AutoBody => "RANGE 2,254; FLAGS AUTORANGE; ";

    private static string SortedBody => string.Concat(Ramps.Select(r => string.Create(CultureInfo.InvariantCulture, $"RANGE {r.Start},{r.Length}; ")));

    private static TraceProbeCase Level(string palette, int level) => new(
        string.Create(CultureInfo.InvariantCulture, $"{palette}-{level:00}"),
        string.Create(CultureInfo.InvariantCulture, $"Shade level {level} ({palette} palette): which index does each of the 256 grid cells show?"),
        "",
        "    SET PLAYER_LIGHT, 0;\n    WAIT 2;\n" + V("VIEW", "PLAYER_LIGHT", "AMBIENT"),
        Predict(
            ("sorted ranges step by length/32 per level", "a 16-colour range moves half a colour per level"),
            ("AUTORANGE fades toward INFINITY by nearest RGB", "each cell's index is the range colour nearest the mix")),
        Map: new TraceProbeMap(
            RoomBody: string.Create(CultureInfo.InvariantCulture, $"AMBIENT {(30 - level) / 32.0};"),
            PaletteBody: palette == "auto" ? AutoBody : SortedBody));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        .. Enumerable.Range(0, 16).Select(i => Level("auto", 30 - (2 * i))),
        .. Enumerable.Range(0, 16).Select(i => Level("sorted", 30 - (2 * i))),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "autorange",
        TraceFile,
        "ovars",
        "The far wall's target face is a 16 x 16 grid of the 256 palette indices. `PLAYER_LIGHT` 0 and the\n" +
        "room's `AMBIENT` put every texel on the case's shade level; read each cell's index in `OVARS0.PCX`.\n" +
        "`auto-*` cases use a pseudo-random AUTORANGE palette, `sorted-*` cases four sorted ramps.",
        All);
}
