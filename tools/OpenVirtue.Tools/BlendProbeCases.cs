// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>blend</c> probe: what <c>DIAPHANOUS</c> and <c>GHOST</c> draw (manual p.44:
/// GHOST "semi-transparent on things, actors or transparent walls", DIAPHANOUS "like GHOST, except
/// … diaphanous and not dithered", only with a <c>BLUR</c> palette), and which way a region's
/// <c>FLOOR_OFFS_X/Y</c> and <c>CEIL_OFFS_X/Y</c> slide a surface's texture (the retail scripts
/// scroll floors with them). The retail head shadows are <c>DIAPHANOUS, BEHIND, SHADOW</c> strips of
/// the palette's black. A thing of the 32×48 orange bitmap stands 24 steps ahead with a 48×12 strip of
/// index 1 (the <c>INFINITY</c> black) attached as a <c>SHADOW</c>, so the strip's lower half lies
/// over the floor, which is the gray ramp's first index (<c>RANGE 16, 32</c>) shaded by depth; the
/// strip's pixels against the floor's beside it give the blend. The offset cases put the texmap
/// probe's 64×64 cell swatch on the floor and ceiling with offsets.
/// </summary>
internal static class BlendProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVBLD0.TXT).</summary>
    public const string TraceFile = "ovbld";

    /// <summary>The 48×12 strip of index 1.</summary>
    public const string BlackStripFile = "PRBATK.PCX";

    private const string Bitmaps =
        $"BMAP bl_bm,<{AttachProbeCases.BaseFile}>;\nBMAP bl_km,<{BlackStripFile}>;\nTEXTURE bl_base {{ SCALE_XY 16, 16; BMAPS bl_bm; }}\n";

    private const string Cells =
        $"BMAP bl_cm,<{TexmapProbeCases.CellFile}>;\nTEXTURE bl_cell {{ SCALE_X 4; SCALE_Y 4; BMAPS bl_cm; }}\n";

    private static TraceProbeCase Strip(string label, string question, string flags, string paletteBody, params (string, string)[] predictions) => new(
        label,
        question,
        Bitmaps + $"TEXTURE bl_x {{ BMAPS bl_km; FLAGS {flags}; }}\nTHING bl_t {{ TEXTURE bl_base; ATTACH bl_x; }}\n",
        "    WAITT 24;\n" + P("RET"),
        Predict(predictions),
        Map: new TraceProbeMap(Placements: ["THING bl_t 24 0 0 0"], PaletteBody: paletteBody));

    private static TraceProbeCase Surface(string label, string question, string roomBody, string? floor, string? ceiling, params (string, string)[] predictions) => new(
        label,
        question,
        "",
        "    WAITT 8;\n" + P("RET"),
        Predict(predictions),
        Map: new TraceProbeMap(Textures: Cells, RoomBody: roomBody, RoomFloor: floor, RoomCeiling: ceiling));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Strip(
            "control",
            "The black strip as a plain SHADOW attach on the ramp floor with a BLUR palette: the strip solid index 1, and the floor's shaded index in the rows beside it, as the baseline.",
            "SHADOW",
            "RANGE 16, 32; FLAGS BLUR;",
            ("solid", "index 1 over the floor and the base")),
        Strip(
            "diaph",
            "The strip with FLAGS DIAPHANOUS, SHADOW on a BLUR palette (the retail shadows): over the floor, which index does black over the floor's shaded gray give (nearest colour to the mean, half way down the ramp, or something else), and what over the orange base?",
            "DIAPHANOUS, SHADOW",
            "RANGE 16, 32; FLAGS BLUR;",
            ("the nearest palette colour to the mean of the two", "over floor index 16 + k the strip reads about 16 + k/2 + 15"),
            ("half way along the range toward INFINITY", "over 16 + k the strip reads 16 + k + 16, cut at 47")),
        Strip(
            "diaph-noblur",
            "FLAGS DIAPHANOUS, SHADOW without BLUR on the palette: solid, as the manual's \"only works with a BLUR palette\" suggests, or blended anyway?",
            "DIAPHANOUS, SHADOW",
            "RANGE 16, 32;",
            ("solid black", "index 1"), ("blended", "as diaph")),
        Strip(
            "ghost",
            "FLAGS GHOST, SHADOW on a BLUR palette: a dither (every other pixel the strip, the rest the floor) or a blend?",
            "GHOST, SHADOW",
            "RANGE 16, 32; FLAGS BLUR;",
            ("a checkerboard of index 1 and the floor", "alternate pixels"), ("as diaph", "blended indices")),
        Surface(
            "floor-offs",
            "The cell swatch on the floor with FLOOR_OFFS_X 16 and FLOOR_OFFS_Y 32 (4 and 8 steps at SCALE 4): which way do the cells move against the texmap floor-cell reading (column floor(−x), row floor(y))?",
            "FLOOR_OFFS_X 16; FLOOR_OFFS_Y 32;",
            "bl_cell",
            null,
            ("the offsets add to the texel", "column floor(−x) + 4, row floor(y) + 8"),
            ("the offsets slide the texture along +x and +y", "column floor(−x) − 4, row floor(y) − 8")),
        Surface(
            "ceil-offs",
            "The cell swatch on the ceiling with CEIL_OFFS_X 8 and CEIL_OFFS_Y 24: the same for the ceiling.",
            "CEIL_OFFS_X 8; CEIL_OFFS_Y 24;",
            null,
            "bl_cell",
            ("as the floor", "column floor(−x) + 2, row floor(y) + 6")),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "blend",
        TraceFile,
        "ovbls",
        "The final screenshot `OVBLS0.PCX` is the reading: the strip's indices against the floor's in the same\n" +
        "rows (strip cases), or the cell under each pixel (offset cases). Compare with `case.json` →\n" +
        "`predictions`, check `OVCON.TXT` for compile errors.",
        All);
}
