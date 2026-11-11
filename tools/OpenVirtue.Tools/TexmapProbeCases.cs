// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>texmap</c> probe: how a texture is laid on a floor, a ceiling and a wall —
/// where its origin lies, which way its columns and rows run, what a wall's <c>OFFSET_X</c> and
/// <c>OFFSET_Y</c> do, and whether a wall's repeats stack from its region's floor or from height 0.
/// The texture is the level palette's 16×16 swatch (every index once) at <c>SCALE</c> 2, so each
/// screen pixel's index names the texel under it and a repeat is 8 steps.
/// </summary>
internal static class TexmapProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVTXM0.TXT).</summary>
    public const string TraceFile = "ovtxm";

    /// <summary>A 64×64 bitmap of 4×4-pixel cells, cell (c, r) holding index 16·r + c: a swatch floors can tile.</summary>
    public const string CellFile = "PRBPCEL.PCX";

    /// <summary>The cell swatch's pixel.</summary>
    public static byte CellPixel(int x, int y) => (byte)((16 * (y / 4)) + (x / 4));

    private const string Textures =
        "BMAP tm_sw,<PRBPAL.PCX>;\nTEXTURE tm_tex { SCALE_X 2; SCALE_Y 2; BMAPS tm_sw; }\nWALL tm_wall { TEXTURE tm_tex; }\n" +
        $"BMAP tm_cm,<{CellFile}>;\nTEXTURE tm_cell {{ SCALE_X 4; SCALE_Y 4; BMAPS tm_cm; }}\n";

    /// <summary>Round 2: the bitmap sizes floors and ceilings are tried at; retail has all but the smallest.</summary>
    public static readonly (int Width, int Height)[] SurfaceSizes =
    [
        (16, 16), (32, 32), (63, 63), (64, 64), (65, 65), (100, 100), (128, 128), (129, 128), (129, 129), (256, 256), (257, 257),
        (512, 512), (128, 64), (64, 128), (128, 257), (320, 128), (324, 132),
    ];

    public static string SizeFile(int width, int height) => $"S{width}X{height}.PCX";

    /// <summary>Index 16 + (x + y) mod 200 away from the room's inks, so a texel reads as its position.</summary>
    public static byte SizePixel(int x, int y) => (byte)(16 + (((x / 4) + (y / 4)) % 200));

    private static TraceProbeCase SizeCase(int width, int height)
    {
        string map = $"BMAP tz_m,<{SizeFile(width, height)}>;\nTEXTURE tz_t {{ SCALE_X 4; SCALE_Y 4; BMAPS tz_m; }}\n";
        return Case(
            $"sz-{width}x{height}",
            $"Round 2. A {width}×{height} bitmap as both floor and ceiling at SCALE 4: drawn, or entry 0, and how does it repeat?",
            new TraceProbeMap(Textures: map, RoomFloor: "tz_t", RoomCeiling: "tz_t"));
    }

    private static TraceProbeCase Case(string label, string question, TraceProbeMap map, string run = "", params (string, string)[] predictions) => new(
        label,
        question,
        "",
        run + "    WAITT 8;\n" + P("RET"),
        Predict(predictions),
        Map: map);

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        .. SurfaceSizes.Select(static size => SizeCase(size.Width, size.Height)),
        Case(
            "floor",
            "The swatch as the room's FLOOR_TEX: at which map point does texel 0, 0 lie, and do the columns run along +x or +y (facing +x, +y is to the left)?",
            new TraceProbeMap(Textures: Textures, RoomFloor: "tm_tex"),
            predictions: [("columns along +x from the origin, rows along −y", "the index along the screen's centre column grows by 1 every half step of depth")]),
        Case(
            "ceil",
            "The swatch as the room's CEIL_TEX: the same for the ceiling.",
            new TraceProbeMap(Textures: Textures, RoomCeiling: "tm_tex"),
            predictions: [("as the floor", "the same grid on the ceiling")]),
        Case(
            "wall-plain",
            "The swatch on a free wall 32 steps ahead from map y −4 to 4, no offsets: the baseline for wall-offset (the fade-pal swatch wall showed columns from the second vertex and rows down from each repeat's top).",
            new TraceProbeMap(Textures: Textures, Walls: [new TraceProbeWall("tm_wall", 32, -4, 32, 4)]),
            predictions: [("texel 0, 0 at the second vertex and the top of the first repeat above the floor", "index 0 at the wall's left end two repeats up, i.e. at the ceiling")]),
        Case(
            "wall-offset",
            "The same wall with the map's OFFSET_X 4 and OFFSET_Y 6 (pixels; 2 and 3 steps at SCALE 2): which way do they shift the texels at the second vertex and the top?",
            new TraceProbeMap(Textures: Textures, Walls: [new TraceProbeWall("tm_wall", 32, -4, 32, 4, 4, 6)]),
            predictions: [("the texture slides by the offset", "texel 4, 6 at the corner, or texel 12, 10")]),
        Case(
            "raised",
            "The pad island (floor 4) with the swatch on its walls, the player turned to face it: the step from 0 to 4 at its edge shows the rows of the repeat stacked from height 0 (rows 8 to 15) or from the pad's floor 4 (rows 0 to 7)?",
            new TraceProbeMap(Textures: Textures, PadBody: "", PadFloor: 4, PadWall: "tm_wall"),
            "    SET PLAYER_ANGLE, 2.03;\n",
            [("from height 0", "the step's top row is row 8"), ("from the region's floor", "the step's top row is row 0")]),
        Case(
            "floor-cell",
            "The 64×64 cell swatch (4×4-pixel cells, cell c, r = index 16r + c; 16 px a step, a cell a step, 4 steps a repeat) as the room's FLOOR_TEX, round 1's 16×16 swatch having drawn as index 0: where does cell 0, 0 lie and which way do columns and rows run?",
            new TraceProbeMap(Textures: Textures, RoomFloor: "tm_cell"),
            predictions: [("columns along +x from the origin, rows along −y", "along the centre column the cell column grows by one a step of depth; across a row the cell row changes with y")]),
        Case(
            "ceil-cell",
            "The cell swatch as the room's CEIL_TEX: the same for the ceiling.",
            new TraceProbeMap(Textures: Textures, RoomCeiling: "tm_cell"),
            predictions: [("as the floor", "the same grid on the ceiling")]),
        Case(
            "pit",
            "The pad sunk to floor −4 with the swatch on its walls, the player turned to it and tilted down 0.3: the step from −4 up to 0 seen over the edge shows rows 0 to 7 (repeats from height 0) or rows 8 to 15 (from the pit's floor)?",
            new TraceProbeMap(Textures: Textures, PadBody: "", PadFloor: -4, PadWall: "tm_wall"),
            "    SET PLAYER_ANGLE, 2.03;\n    SET PLAYER_TILT, 0.3;\n",
            [("from height 0", "row 0 at the top of the step"), ("from the lower region's floor", "row 8 at the top of the step")]),
        .. new[] { 4, 8, 12 }.Select(static eye => Case(
            $"round3/blank-eye{eye}",
            $"Round 3. With a blank ceiling (the 16×16 swatch) the walls reached above the room's ceiling 16, to about 24 with the eye at 8. With the eye at {eye}: where do they end (ceiling + 8: 24; 2·ceiling − eye: {32 - eye}; ceiling + eye: {16 + eye})?",
            new TraceProbeMap(Textures: Textures, RoomCeiling: "tm_tex"),
            $"    SET PLAYER_SIZE, {eye};\n    SET PLAYER_Z, {eye};\n",
            [("ceiling + 8", "the far wall's top at height 24"), ("mirror of the eye in the ceiling", $"at {32 - eye}"), ("ceiling + eye", $"at {16 + eye}")])),
        // Round 4: the retail levels' walls run at all angles from off-grid corners, and Heart's showed every texel
        // column a few pixels off. Is a wall's distance from its second vertex the straight-line one?
        Case(
            "round4/wall-diag45",
            "Round 4. The swatch on a 45° wall from (24, −6) to (36, 6): does the column grow by one every half step of straight-line distance from the second vertex, or of |dx| + |dy|, or of the larger part?",
            new TraceProbeMap(Textures: Textures, Walls: [new TraceProbeWall("tm_wall", 24, -6, 36, 6)]),
            predictions: [("straight-line distance", "the column at the wall's far end is 16.97 · 2 mod 16"), ("|dx| + |dy|", "the columns come 1.41 times as narrow")]),
        Case(
            "round4/wall-diag4",
            "Round 4. A steep wall from (30, −8) to (34, 8): the same question at slope 4.",
            new TraceProbeMap(Textures: Textures, Walls: [new TraceProbeWall("tm_wall", 30, -8, 34, 8)]),
            predictions: [("straight-line distance", "16.49 steps from end to end")]),
        Case(
            "round4/wall-frac",
            "Round 4. The baseline wall with corners off the grid, (32, −4.3) to (32, 4.61): is the column origin the corner cut toward zero, rounded, or exact?",
            new TraceProbeMap(Textures: Textures, Walls: [new TraceProbeWall("tm_wall", 32, -4.3, 32, 4.61)]),
            predictions: [("the corner cut toward zero, as the map's vertices are", "column 0 at y 4.6094")]),
        // Round 5: on round 4's oblique walls the texel columns came about 1.8 screen columns further along than on
        // walls along an axis. How does that lead go with the wall's angle and distance?
        .. new (string Label, double X1, double Y1, double X2, double Y2, string Question)[]
        {
            ("round5/wall-s8", 30, -8, 32, 8, "a nearly facing wall, slope 8"),
            ("round5/wall-s2", 26, -8, 34, 8, "a wall of slope 2"),
            ("round5/wall-d45n", 12, -6, 24, 6, "the 45° wall nearer, about 18 ahead"),
            ("round5/wall-d45f", 36, -6, 48, 6, "the 45° wall farther, about 42 ahead"),
            ("round5/wall-d45m", 24, 6, 36, -6, "the 45° wall of the other sense, from (24, 6) to (36, −6)"),
        }.Select(static w => Case(
            w.Label,
            $"Round 5. The swatch on {w.Question}: how far along are its texel columns against the exact u?",
            new TraceProbeMap(Textures: Textures, Walls: [new TraceProbeWall("tm_wall", w.X1, w.Y1, w.X2, w.Y2)]),
            predictions: [("the lead of round 4's oblique walls, about 1.8 columns", "the same lead at every angle and distance")])),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "texmap",
        TraceFile,
        "ovtxs",
        "The final screenshot `OVTXS0.PCX` is the reading: each pixel's palette index is the swatch texel\n" +
        "(column = index mod 16, row = index div 16) under it. Compare with `case.json` → `predictions`,\n" +
        "check `OVCON.TXT` for compile errors.",
        All);
}
