// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>wall-pieces</c> probe: how a wall between two regions of different heights
/// shows its pieces — the lower step between the floors and the upper step between the ceilings.
/// Two questions (manual p.54 and p.43): does <c>FLAGS PORTCULLIS</c> fasten the texture to the
/// piece's own border (<c>POSITION</c> 0 = the lower border, 1 = the upper), and where does an
/// <c>ATTACH</c> sit on a piece that does not start at height 0? The texmap and attach probes read
/// the repeat grid and the attach origin on walls standing on their region's floor; the pad island
/// (x −12…−4, y 12…20) is raised or sunk here so the two-region wall's pieces span heights that are
/// off that grid.
/// <para>
/// Block 1 puts the level palette's 16×12 swatch window at <c>SCALE</c> 2 (a repeat 6 steps, a texel
/// half a step, ceiling 16 off the grid; each pixel's index is 16·row + column). Block 2 puts a
/// 16×16 cyan square on the template's blue wall texture (4 pixels a step, a 16-step repeat), with
/// an 8-pixel-a-step variant for the 8-step grid and a <c>SCALE_Y</c> 3 variant whose repeat does
/// not divide the height.
/// </para>
/// </summary>
internal static class WallPiecesProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVWPC0.TXT).</summary>
    public const string TraceFile = "ovwpc";

    /// <summary>A 16×12 window of the level palette's swatch at SCALE 2: a repeat 6 steps, a texel half a step.</summary>
    private const string Swatch = "BMAP wp_c,<PRBPAL.PCX>,0,0,16,12;\nTEXTURE wp_tex { SCALE_X 2; SCALE_Y 2; BMAPS wp_c; }\n";

    /// <summary>The attach probe's 16×16 cyan square (index 10).</summary>
    private const string Square = $"BMAP wp_q,<{AttachProbeCases.SquareFile}>;\n";

    /// <summary>The template's blue wall texture: 64 px at SCALE 4, 4 pixels a step, a 16-step repeat.</summary>
    private const string Blue = "blu_tex";

    /// <summary>A copy of the blue bitmap at 8 pixels a step: an 8-step repeat.</summary>
    private const string Blue8 = "TEXTURE wp_b8 { SCALE_X 8; SCALE_Y 8; BMAPS blu_map; }\n";

    /// <summary>A copy of the blue bitmap at SCALE_Y 3: a 21.33-step repeat, taller than the wall.</summary>
    private const string Blue3 = "TEXTURE wp_b3 { SCALE_X 4; SCALE_Y 3; BMAPS blu_map; }\n";

    /// <summary>
    /// Turned to the pad island from (24, −8): the room is x −16…48, y −24…24, so the eye is 16
    /// steps from the nearest wall (y −24) and 24 from the far one. The angle is the direction to
    /// the pad centre (−8, 16): atan2(24, −32) = 2.498, which sees the pad's south (y 12) and east
    /// (x −4) faces whole.
    /// </summary>
    private const string FarRun = "    SET PLAYER_X, 24;\n    SET PLAYER_Y, -8;\n    SET PLAYER_ANGLE, 2.498;\n";

    /// <summary>Turned to the pad island from the origin and tilted down 0.3 (texmap's pit view).</summary>
    private const string NearRun = "    SET PLAYER_ANGLE, 2.03;\n    SET PLAYER_TILT, 0.3;\n";

    private const string Port0 = "FLAGS PORTCULLIS;";
    private const string Port1 = "FLAGS PORTCULLIS; POSITION 1;";

    /// <summary>A case whose pad island carries <paramref name="wallBody"/> on all four boundary walls.</summary>
    private static TraceProbeCase Pad(
        string label,
        string question,
        int floor,
        int? ceiling,
        string wallBody,
        string run,
        params (string, string)[] predictions) => new(
            label,
            question,
            "",
            run + "    WAITT 8;\n" + P("RET"),
            Predict(predictions),
            Map: new TraceProbeMap(
                Textures: Swatch + $"WALL wp_w {{ TEXTURE wp_tex; {wallBody} }}\n",
                PadBody: "",
                PadFloor: floor,
                PadCeiling: ceiling,
                PadWall: "wp_w"));

    /// <summary>A case whose free wall 32 steps ahead (map y −4…4) carries the square attach.</summary>
    private static TraceProbeCase Free(
        string label,
        string question,
        string wallBody,
        int offsetX,
        int offsetY,
        params (string, string)[] predictions) => new(
            label,
            question,
            "",
            "    WAITT 8;\n" + P("RET"),
            Predict(predictions),
            Map: new TraceProbeMap(
                Textures: Swatch + $"WALL wp_w {{ TEXTURE wp_tex; {wallBody} }}\n",
                Walls: [new TraceProbeWall("wp_w", 32, -4, 32, 4, offsetX, offsetY)]));

    /// <summary>
    /// A case whose pad island's walls are the blue base texture with the cyan square attached at
    /// <paramref name="posX"/>, <paramref name="posY"/> (base texture pixels).
    /// </summary>
    private static TraceProbeCase Attached(
        string label,
        string question,
        int floor,
        int? ceiling,
        string baseTexture,
        string baseDeclarations,
        int posX,
        int posY,
        string wallFlags,
        string run,
        params (string, string)[] predictions) => new(
            label,
            question,
            "",
            run + "    WAITT 8;\n" + P("RET"),
            Predict(predictions),
            Map: new TraceProbeMap(
                Textures: Square +
                    $"TEXTURE wp_a {{ BMAPS wp_q; POS_X {posX}; POS_Y {posY}; }}\n" +
                    baseDeclarations +
                    $"WALL wp_w {{ TEXTURE {baseTexture}; ATTACH wp_a; {wallFlags} }}\n",
                PadBody: "",
                PadFloor: floor,
                PadCeiling: ceiling,
                PadWall: "wp_w"));

    /// <summary>A 12×12 bitmap whose pixel (x, y) is palette index 16·y + x (round 2's native grid).</summary>
    public const string Native12File = "PRBP12.PCX";

    /// <summary>The attach square at <paramref name="posX"/>, <paramref name="posY"/> base-texture pixels.</summary>
    private static string Attach(int posX, int posY) =>
        $"TEXTURE wp_a {{ BMAPS wp_q; POS_X {posX}; POS_Y {posY}; }}\n";

    /// <summary>The default view: the eye at the origin looking along +x, no tilt.</summary>
    private const string RoomRun = "";

    /// <summary>Round 2's 8-pixel-a-step base as a WALL body's TEXTURE clause.</summary>
    private const string Blue8Body = "TEXTURE wp_b8;";

    /// <summary>
    /// Round 2's masks: a 12×16 window of the palette swatch (columns 0…11 of rows 0…15) at 2
    /// pixels a step, on a free wall in the room.
    /// </summary>
    private const string Mask12 = "BMAP wp_mb,<PRBPAL.PCX>,0,0,12,16;\nTEXTURE wp_mt { SCALE_X 2; SCALE_Y 2; BMAPS wp_mb; }\n";

    /// <summary>A round-2 pad case: the pad island's boundary walls carry <paramref name="wallBody"/>.</summary>
    private static TraceProbeCase Piece(
        string label,
        string question,
        int floor,
        int? ceiling,
        string declarations,
        string wallBody,
        string run,
        params (string, string)[] predictions) => new(
            label,
            question,
            "",
            run + "    WAITT 8;\n" + P("RET"),
            Predict(predictions),
            Map: new TraceProbeMap(
                Textures: declarations + $"WALL wp_w {{ {wallBody} }}\n",
                PadBody: "",
                PadFloor: floor,
                PadCeiling: ceiling,
                PadWall: "wp_w"));

    /// <summary>
    /// The whole level-palette swatch — 16×16, index 16·row + column — at SCALE 2: 16 rows, an
    /// 8-step repeat, and no mask surprises because both sides are a power of two.
    /// </summary>
    private const string Swatch16 = "BMAP wp_s,<PRBPAL.PCX>;\nTEXTURE wp_st { SCALE_X 2; SCALE_Y 2; BMAPS wp_s; }\n";

    /// <summary>
    /// A case whose pad island carries a slope flag and corner heights, and whose boundary walls
    /// carry <paramref name="wallBody"/> — the sloped piece's edges, not flat ones.
    /// </summary>
    private static TraceProbeCase Sloped(
        string label,
        string question,
        int floor,
        string padBody,
        IReadOnlyList<double> padZ,
        string declarations,
        string wallBody,
        string run,
        params (string, string)[] predictions) => new(
            label,
            question,
            "",
            run + "    WAITT 8;\n" + P("RET"),
            Predict(predictions),
            Map: new TraceProbeMap(
                Textures: declarations + $"WALL wp_w {{ {wallBody} }}\n",
                PadBody: padBody,
                PadFloor: floor,
                PadWall: "wp_w",
                PadZ: padZ));

    /// <summary>
    /// A round-2 pad case whose pad island keeps its invisible passable boundary, so a free wall
    /// inside the pad (region 2 on both sides) carries the texture.
    /// </summary>
    private static TraceProbeCase PadFree(
        string label,
        string question,
        int floor,
        string declarations,
        string wallBody,
        IReadOnlyList<TraceProbeWall> walls,
        string run,
        params (string, string)[] predictions) => new(
            label,
            question,
            "",
            run + "    WAITT 8;\n" + P("RET"),
            Predict(predictions),
            Map: new TraceProbeMap(
                Textures: declarations + $"WALL wp_w {{ {wallBody} }}\n",
                PadBody: "",
                PadFloor: floor,
                PadWall: null,
                Walls: walls));

    /// <summary>
    /// A round-2 case whose free wall in the room (<paramref name="wall"/>) carries the texture,
    /// the room on both sides.
    /// </summary>
    private static TraceProbeCase RoomWall(
        string label,
        string question,
        string declarations,
        TraceProbeWall wall,
        string wallBody,
        string run,
        params (string, string)[] predictions) => new(
            label,
            question,
            "",
            run + "    WAITT 8;\n" + P("RET"),
            Predict(predictions),
            Map: new TraceProbeMap(
                Textures: declarations + $"WALL {wall.Keyword} {{ {wallBody} }}\n",
                Walls: [wall]));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        // Block 1 — FLAGS PORTCULLIS and POSITION on a two-region wall's pieces.
        Pad(
            "plain-step",
            "Baseline for PORTCULLIS. The pad raised to floor 5, its wall seen from the room: the texture's rows on a plain wall stack from height 0 (row(z) = floor(−2z) mod 12 at 2 pixels a step, a repeat 6 steps).",
            5,
            null,
            "",
            FarRun,
            ("rows from height 0, the known rule", "row 2 at the top of the step z 5, row 11 at z 0"),
            ("rows from the pad's floor", "row 0 at the top of the step")),
        Pad(
            "port-step",
            "As plain-step with FLAGS PORTCULLIS: the lower border is on the height-0 grid already, so does the flag change anything at z 0?",
            5,
            null,
            Port0,
            FarRun,
            ("the flag fastens the texture to the lower border (p.54), which is z 0 here", "as plain-step: row 2 at z 5, row 11 at z 0"),
            ("the flag shifts the texture", "a different row at z 0")),
        Pad(
            "port-step-p1",
            "As port-step with POSITION 1 (p.54: the upper border): the step's top z 5 becomes the texture's vertical zero.",
            5,
            null,
            Port1,
            FarRun,
            ("the upper border z 5 is the vertical zero", "row 0 at z 5, row 9 at z 0"),
            ("POSITION is ignored", "as plain-step: row 2 at z 5")),
        Pad(
            "plain-ceil",
            "Baseline for the upper piece. The pad's ceiling dropped to 11, so only the piece between 11 and the room's ceiling 16 exists: where are its rows on the height-0 grid?",
            0,
            11,
            "",
            FarRun,
            ("rows from height 0", "row 4 at z 16 and row 1 at z 11"),
            ("rows from the pad's ceiling 11", "row 0 at z 11")),
        Pad(
            "port-ceil",
            "As plain-ceil with FLAGS PORTCULLIS: is the piece its own texture — rows counting up from its lower border z 11 — while the whole wall keeps the height-0 grid?",
            0,
            11,
            Port0,
            FarRun,
            ("the piece starts its own repeat at its lower border z 11", "row 11 at z 11, row 2 at z 16"),
            ("the whole wall keeps the height-0 grid", "as plain-ceil: row 4 at z 16, row 1 at z 11")),
        Pad(
            "port-ceil-p1",
            "As port-ceil with POSITION 1: the piece's upper border z 16 (the room's ceiling) becomes the vertical zero.",
            0,
            11,
            Port1,
            FarRun,
            ("the upper border z 16 is the vertical zero", "row 0 at z 16, row 9 at z 11"),
            ("POSITION is ignored", "as port-ceil: row 11 at z 11")),
        Pad(
            "port-both",
            "The pad raised to floor 3 and its ceiling dropped to 11, so the wall has a lower piece (0…3) and an upper one (11…16) with an opening between: does each piece carry its own repeat?",
            3,
            11,
            Port0,
            FarRun,
            ("each piece is its own texture from its lower border", "the lower piece row 11 at z 0 to row 6 at z 3; the upper row 11 at z 11, row 2 at z 16"),
            ("one grid for the whole wall", "row 11 at z 0, row 5 at z 3, row 1 at z 11, row 4 at z 16")),
        Pad(
            "port-both-p1",
            "As port-both with POSITION 1: each piece's upper border becomes the vertical zero.",
            3,
            11,
            Port1,
            FarRun,
            ("each piece hangs from its upper border", "the lower piece row 0 at z 3; the upper row 0 at z 16"),
            ("only one border counts", "one piece as port-both")),
        Pad(
            "port-pit",
            "The pad sunk to floor −4, seen from the origin tilted down 0.3. FLAGS PORTCULLIS on the piece between −4 and 0: fastened to the piece's lower border −4, or on the height-0 grid?",
            -4,
            null,
            Port0,
            NearRun,
            ("fastened to the lower border z −4", "row 11 at z −4, row 6 at z 0"),
            ("the height-0 grid", "row 0 at z 0, row 5 at z −4")),
        Pad(
            "port-pit-p1",
            "As port-pit with POSITION 1: the piece's upper border z 0 becomes the vertical zero.",
            -4,
            null,
            Port1,
            NearRun,
            ("the upper border z 0 is the vertical zero", "row 0 at z 0, row 5 at z −3"),
            ("POSITION is ignored", "as port-pit: row 11 at z −4")),
        Free(
            "port-free",
            "A free wall standing on the room's floor 0 with FLAGS PORTCULLIS and POSITION 1: the upper border z 16 is its vertical zero, against the plain wall's row 4 at the ceiling.",
            Port1,
            0,
            0,
            ("the upper border z 16 is the vertical zero", "row 0 at z 16, the rows counting up from the ceiling"),
            ("as a plain wall", "row 4 at z 16")),
        Free(
            "port-freeoff",
            "As port-free with the map's OFFSET_X 4 and OFFSET_Y 6 (pixels; OFFSET_Y slid the plain wall 6 pixels down): does the offset still slide a PORTCULLIS texture's border origin?",
            Port1,
            4,
            6,
            ("the offset still slides the border origin", "row 6 at z 16"),
            ("the flag ignores the map offset", "row 0 at z 16")),
        Free(
            "port-free-o0",
            "As port-freeoff with POSITION 0 (the wall's own floor z 0 as the vertical zero) and the same offsets: which row sits just above z 0?",
            Port0,
            4,
            6,
            ("the offset still slides the origin", "row 5 just above z 0 (the plain row 11 plus 6, mod 12)"),
            ("the flag ignores the map offset", "row 11 just above z 0, as a plain wall")),

        // Block 2 — ATTACH on a two-region wall.
        Attached(
            "att-upper",
            "The pad's ceiling dropped to 8, so the attach sits on the upper piece (8…16). The square at POS_X 8, POS_Y 16 (2 steps along, 4 down at the wall texture's 4 pixels a step): does the attach hang from the repeat grid stacked from height 0 (top 16), as on a wall standing on the floor?",
            0,
            8,
            Blue,
            "",
            8,
            16,
            "",
            FarRun,
            ("the height-0 grid's repeat top 16", "cyan 2 steps along the upper piece, z 11–12"),
            ("the piece's own foot 8 as the origin", "cyan z 3–4, in the opening: none"),
            ("nothing drawn on a piece", "no cyan")),
        Attached(
            "att-open",
            "As att-upper with POS_Y 48 (12 steps down): from the grid's 16 the square's top lands on 4, below the piece's foot 8, in the opening.",
            0,
            8,
            Blue,
            "",
            8,
            48,
            "",
            FarRun,
            ("the height-0 grid", "no cyan; the square would be at z 3–4 over the opening"),
            ("drawn past the piece's foot", "cyan floating at z 3–4")),
        Attached(
            "att-cross",
            "As att-upper with POS_Y 30 (7.5 steps down): the square (top 8.5, bottom 7.5) crosses the piece's lower edge 8.",
            0,
            8,
            Blue,
            "",
            8,
            30,
            "",
            FarRun,
            ("cut at the piece's lower edge", "half a square at z 8–8.5"),
            ("drawn whole over the opening", "a whole square at z 7.5–8.5"),
            ("hidden", "no cyan")),
        Attached(
            "att-low",
            "The pad raised to floor 4 and its ceiling dropped to 12; the square at POS_Y 52 (13 steps down): from the grid's 16 its top lands on 3, on the lower piece (0…4).",
            4,
            12,
            Blue,
            "",
            8,
            52,
            "",
            FarRun,
            ("the height-0 grid", "cyan z 2–3 on the lower piece"),
            ("the lower piece's own foot as the origin", "cyan z 12–13, in the opening: none")),
        Attached(
            "att-high",
            "As att-low with POS_Y 4 (1 step down): from the grid's 16 the square sits at 15, on the upper piece (12…16).",
            4,
            12,
            Blue,
            "",
            8,
            4,
            "",
            FarRun,
            ("the height-0 grid", "cyan z 14–15 on the upper piece"),
            ("the upper piece's own top as the origin", "cyan z 1–2, in the opening: none")),
        Attached(
            "att-grid-up",
            "The pad's ceiling dropped to 12 with the base texture at 8 pixels a step (an 8-step repeat) and the square at POS_X 16, POS_Y 8 (2 steps along, 1 down): the first repeat above height 0 puts it at 7, in the opening; from the piece's own edges it would be elsewhere.",
            0,
            12,
            "wp_b8",
            Blue8,
            16,
            8,
            "",
            FarRun,
            ("the height-0 grid (repeat tops at 8, 16, 24)", "no cyan; the square would be at z 6–7 in the opening"),
            ("the grid from the piece's ceiling 16", "cyan z 14–15 on the upper piece")),
        Attached(
            "att-grid-st",
            "The pad raised to floor 9 with the 8-step grid and the square at POS_Y 8: the height-0 grid puts it at z 6–7, on the step; a grid from the pad's floor 9 would put it at 14–15.",
            9,
            null,
            "wp_b8",
            Blue8,
            16,
            8,
            "",
            FarRun,
            ("the grid from height 0", "cyan z 6–7, on the lower piece's step"),
            ("the grid from the higher floor 9", "the square at z 14–15 instead")),
        Attached(
            "att-grid-pit",
            "The pad sunk to floor −4 with the 8-step grid and the square at POS_Y 8, seen tilted down from the origin: height 0 puts it at 7 above the step; the pit's floor −4 puts the repeat top at 0.",
            -4,
            null,
            "wp_b8",
            Blue8,
            16,
            8,
            "",
            NearRun,
            ("the grid from height 0", "no cyan; the square would be at z 6–7 over the opening"),
            ("the grid from the pit's floor −4", "cyan z −2 to −1 on the far step")),
        Attached(
            "att-port-neg",
            "The pad's ceiling dropped to 11 and the square at POS_Y -4 (1 step up) on a FLAGS PORTCULLIS wall: p.43 puts the vertical zero at the piece's lower edge 11; the repeat grid would put it at 16.",
            0,
            11,
            Blue,
            "",
            8,
            -4,
            Port0,
            FarRun,
            ("the piece's lower edge 11 is the vertical zero", "cyan z 11–12 on the upper piece"),
            ("the repeat grid", "no cyan; the square would be at z 16–17")),
        Attached(
            "att-port-pos",
            "As att-port-neg with POS_Y 4 (1 step down): the repeat grid puts the square at 14–15 on the piece; the piece's lower edge 11 would put it at 9–10, where no wall exists.",
            0,
            11,
            Blue,
            "",
            8,
            4,
            Port0,
            FarRun,
            ("the repeat grid", "cyan z 14–15 on the upper piece"),
            ("the piece's lower edge 11 is the vertical zero", "no cyan; the square would be at z 9–10")),
        Attached(
            "att-port-p1",
            "The SCALE_Y 3 base texture (a 21.33-step repeat) with FLAGS PORTCULLIS, POSITION 1 and the square at POS_Y 12 (4 steps down), on the pad's upper piece (11…16): from the upper edge 16 the square sits at 12; from the grid (21.33 − 4) above the ceiling.",
            0,
            11,
            "wp_b3",
            Blue3,
            8,
            12,
            Port1,
            FarRun,
            ("the upper edge 16 is the vertical zero", "cyan z 11–12 on the upper piece"),
            ("the 21.33-step repeat grid", "no cyan; the square would be at z 16.33–17.33")),

        // Round 2 — the attach on a PORTCULLIS piece, the piece's edges as the grid's origin on
        // steps and pits, a negative POS_Y, a free wall inside the pad, a 15-step repeat, and the
        // horizontal and vertical texel masks of a bitmap whose size is not a power of two.
        Piece(
            "att-p0-48",
            "A FLAGS PORTCULLIS piece above the pad's dropped ceiling 11, the square at POS_X 8, POS_Y 48 (12 steps down the 4-pixel-step base). If the piece's repeat starts at its own lower edge 11, its top is 27 and the square lands at 15; if the flag leaves the height-0 grid (top 16), the square lands at 4, in the opening.",
            0,
            11,
            Square + Attach(8, 48),
            $"TEXTURE {Blue}; ATTACH wp_a; {Port0}",
            FarRun,
            ("the piece's lower edge 11 anchors the repeat (top 27)", "cyan 2 steps along the upper piece, z 14–15"),
            ("the flag has no effect, the height-0 grid's top 16", "no cyan; the square would be at z 3–4 over the opening")),
        Piece(
            "att-p1-48",
            "As att-p0-48 with POSITION 1 added (p.54: the upper border is the vertical zero). Does POSITION leave the flag's lower-edge rule in place, or move the repeat to the upper border 16 (where 16 + R − 12 is above the room, and 16 − 12 is in the opening)?",
            0,
            11,
            Square + Attach(8, 48),
            $"TEXTURE {Blue}; ATTACH wp_a; {Port1}",
            FarRun,
            ("POSITION is ignored; the lower edge 11 anchors the repeat", "cyan z 14–15"),
            ("the upper border 16 anchors the repeat and the offset counts up from it", "no cyan; the square would be at z 19–20"),
            ("the upper border 16 is the zero and the offset counts down from it", "no cyan; the square would be at z 3–4 over the opening")),
        Piece(
            "att-p1-80",
            "As att-p1-48 with POS_Y 80 (20 steps down): from the upper border 16 the square's top is 16 + R − 20 = 12 on the upper piece; from the flag's lower edge 11 it is 7, deep in the opening.",
            0,
            11,
            Square + Attach(8, 80),
            $"TEXTURE {Blue}; ATTACH wp_a; {Port1}",
            FarRun,
            ("the upper border 16 anchors the repeat (top 32, the square's top 12)", "cyan z 11–12 on the upper piece"),
            ("POSITION is ignored; the lower edge 11 anchors the repeat", "no cyan; the square would be at z 6–7 below the piece's foot")),
        Piece(
            "att-p1-16",
            "As att-p1-48 with POS_Y 16 (4 steps down): the manual's reading, the upper border 16 minus 4, keeps the square on the upper piece; abandoning the flag's own rule for the lower edge 11 + R − 4 puts it at 23, above the ceiling.",
            0,
            11,
            Square + Attach(8, 16),
            $"TEXTURE {Blue}; ATTACH wp_a; {Port1}",
            FarRun,
            ("POSITION 1 as the manual says: the upper border 16 − 4", "cyan z 11–12 on the upper piece"),
            ("POSITION is ignored, the piece's lower edge 11 + R − 4", "no cyan; the square would be at z 22–23")),
        Piece(
            "att-pstep",
            "The pad raised to floor 5, so the wall has one lower piece 0…5 and nothing above it. With POSITION 1 and POS_Y 48: does the flag still leave the height-0 grid (square at 4, just above the step) or does the piece's upper border 5 become the zero (5 + R − 12 = 9, over the opening)?",
            5,
            null,
            Square + Attach(8, 48),
            $"TEXTURE {Blue}; ATTACH wp_a; {Port1}",
            FarRun,
            ("POSITION is ignored; the height-0 grid from the piece's lower edge 0", "cyan z 3–4 on the step"),
            ("the piece's upper border 5 is the vertical zero", "no cyan; the square would be at z 8–9 over the opening")),
        Piece(
            "att-pstep72",
            "As att-pstep with POS_Y 72 (18 steps down): from the upper border 5 the square's top is 5 + R − 18 = 3, on the step; from the height-0 grid it is 16 − 18 = −2, under the room's floor.",
            5,
            null,
            Square + Attach(8, 72),
            $"TEXTURE {Blue}; ATTACH wp_a; {Port1}",
            FarRun,
            ("the piece's upper border 5 anchors the repeat", "cyan z 2–3 on the step"),
            ("POSITION is ignored; the height-0 grid", "no cyan; the square would be at z −3 to −2 under the floor")),
        Piece(
            "att-ppit",
            "The pad sunk to floor −4, seen tilted down from the origin, with FLAGS PORTCULLIS and POS_Y 56 (14 steps down): from the piece's lower edge −4 the square is at −2, on the sunk step; with no flag its top is 16 − 14 = 2, over the opening.",
            -4,
            null,
            Square + Attach(8, 56),
            $"TEXTURE {Blue}; ATTACH wp_a; {Port0}",
            NearRun,
            ("the piece's lower edge −4 anchors the repeat", "cyan z −3 to −2 on the sunk step"),
            ("the flag has no effect, the height-0 grid", "no cyan; the square would be at z 1–2 over the opening")),
        Piece(
            "att-pit-abs",
            "As att-ppit with no flag and POS_Y 72 (18 steps down): is the repeat grid absolute from height 0 (16 − 18 = −2, on the sunk step) or does it start at the pit's floor −4 (the first repeat above it reaches 12, and 12 − 18 is under the floor)?",
            -4,
            null,
            Square + Attach(8, 72),
            $"TEXTURE {Blue}; ATTACH wp_a;",
            NearRun,
            ("the height-0 grid (top 16 − 18)", "cyan z −3 to −2 on the sunk step"),
            ("the grid from the pit's floor −4", "no cyan; the square would be below the pit's floor")),
        Piece(
            "att-neg-up",
            "The pad's ceiling dropped to 8 with the 8-pixel-a-step base and the square at POS_Y −24 (3 steps up): a negative offset should ride up past the repeat top 8 to 11, on the upper piece; if a negative POS_Y cuts the texture instead, nothing is drawn.",
            0,
            8,
            Square + Attach(16, -24) + Blue8,
            $"{Blue8Body} ATTACH wp_a;",
            FarRun,
            ("a negative POS_Y steps up past the repeat top 8", "cyan z 10–11 on the upper piece"),
            ("a negative POS_Y draws nothing", "no cyan")),
        Piece(
            "att-neg-port",
            "The pad raised to floor 13 with the 8-pixel-a-step base, FLAGS PORTCULLIS and POS_Y −24: from the piece's lower edge 0 the square rides up past the repeat top 8 to 11, on the lower piece — the shape retail's mall signs take.",
            13,
            null,
            Square + Attach(16, -24) + Blue8,
            $"{Blue8Body} ATTACH wp_a; {Port0}",
            FarRun,
            ("the piece's lower edge 0 + R, then 3 steps up", "cyan z 10–11 on the lower piece"),
            ("the flag or the negative offset draws nothing", "no cyan")),
        PadFree(
            "att-free-9",
            "The pad raised to floor 9 with the 8-pixel-a-step base, its boundary invisible and a free wall (region 2 on both sides) inside it at y 16, x −11…−5; POS_Y 8 (1 step down). Does the repeat come from the region's floor 9 (14–15 on the wall) or from height 0 (below the wall's foot 9)?",
            9,
            Square + Attach(16, 8) + Blue8,
            $"{Blue8Body} ATTACH wp_a;",
            [new TraceProbeWall("wp_w", -11, 16, -5, 16, Region: 2)],
            FarRun,
            ("the first repeat above the region's floor 9", "cyan z 14–15 on the wall"),
            ("the grid from height 0", "no cyan; the square would be below the wall's foot 9")),
        PadFree(
            "att-free-neg",
            "As att-free-9 with POS_Y −24 (3 steps up): the height-0 grid puts the square at 11; a grid from the region's floor 9 would put it above the ceiling 16.",
            9,
            Square + Attach(16, -24) + Blue8,
            $"{Blue8Body} ATTACH wp_a;",
            [new TraceProbeWall("wp_w", -11, 16, -5, 16, Region: 2)],
            FarRun,
            ("the grid from height 0", "cyan z 10–11 on the wall"),
            ("the grid from the region's floor 9", "no cyan; the square would be above the ceiling 16")),
        RoomWall(
            "att-h60",
            "A free wall 32 steps ahead carrying a 60-pixel-tall cut of the blue bitmap at 4 pixels a step (15 steps a repeat) with the square at POS_Y 4: is the repeat the bitmap's own 15 steps (13–14) or the next power of two, 16 steps (14–15)?",
            "BMAP wp_h,<PRBBLU.PCX>,0,0,64,60;\nTEXTURE wp_h60 { SCALE_X 4; SCALE_Y 4; BMAPS wp_h; }\n" + Square + Attach(8, 4),
            new TraceProbeWall("wp_w", 32, -4, 32, 4),
            "TEXTURE wp_h60; ATTACH wp_a;",
            RoomRun,
            ("the repeat is the bitmap's height over the scale: 15 steps", "cyan z 13–14"),
            ("the repeat is the next power of two: 16 steps", "cyan z 14–15")),

        // Round 2's masks — a bitmap whose width (and, natively, height) is 12: a modulo of the
        // bitmap's size, or a bit mask of the next size down.
        RoomWall(
            "mask-c12",
            "A free wall 32 steps ahead carrying a 12×16 window of the palette swatch at 2 pixels a step. Along one row: does the column count 0…11 and back to 0 (a modulo of the bitmap's own 12), or 0,1,2,3,0,1,2,3,8,9,10,11,0 (a bit mask)? Down one column likewise.",
            Mask12,
            new TraceProbeWall("wp_m", 32, -8, 32, 8),
            "TEXTURE wp_mt;",
            RoomRun,
            ("a modulo of the bitmap's own size 12", "columns 0…11 then 0, 1, …; rows 0…11 then 0, 1, …"),
            ("a bit mask of the next size down (11)", "columns 0,1,2,3,0,1,2,3,8,9,10,11,0…; rows likewise")),
        RoomWall(
            "mask-nat12",
            "As mask-c12 with a bitmap natively 12×12 (PRBP12.PCX, pixel (x, y) = index 16·y + x) instead of a cut window: do the same 12-index sequences appear across and down when the bitmap is that size to begin with?",
            $"BMAP wp_mb,<{Native12File}>;\nTEXTURE wp_mt {{ SCALE_X 2; SCALE_Y 2; BMAPS wp_mb; }}\n",
            new TraceProbeWall("wp_m", 32, -8, 32, 8),
            "TEXTURE wp_mt;",
            RoomRun,
            ("a modulo of the bitmap's own size 12", "columns 0…11 then 0, 1, …; rows 0…11 then 0, 1, …"),
            ("a bit mask of the next size down (11)", "rows and columns 0,1,2,3,0,1,2,3,8,9,10,11,0…")),
        RoomWall(
            "mask-h8",
            "The control: a 16×8 window of the palette swatch at 2 pixels a step — the height is already a power of two, so the rows should count 0…7 and repeat (4 steps) and the columns 0…15, with no mask surprises.",
            "BMAP wp_mb,<PRBPAL.PCX>,0,0,16,8;\nTEXTURE wp_mt { SCALE_X 2; SCALE_Y 2; BMAPS wp_mb; }\n",
            new TraceProbeWall("wp_m", 32, -8, 32, 8),
            "TEXTURE wp_mt;",
            RoomRun,
            ("the rows repeat every 8 texels (4 steps)", "rows 0…7, 0…7, …"),
            ("the columns repeat every 16 texels (8 steps)", "columns 0…15, 0…15, …")),
        RoomWall(
            "mask-c12s4",
            "As mask-c12 at 4 pixels a step with the map's OFFSET_X 3: where does the texture begin along the wall (the second vertex, y 8, is on screen's left) and how does it wrap at 12?",
            "BMAP wp_mb,<PRBPAL.PCX>,0,0,12,16;\nTEXTURE wp_mt { SCALE_X 4; SCALE_Y 4; BMAPS wp_mb; }\n",
            new TraceProbeWall("wp_m", 32, -8, 32, 8, OffsetX: 3),
            "TEXTURE wp_mt;",
            RoomRun,
            ("the offset slides the origin 3 texels onto the wall's first column", "column 3 next to the second vertex, then 4, 5, …"),
            ("the offset is ignored", "column 0 next to the second vertex")),

        // Round 4 — the attach and the map's offsets, the attach on both pieces of one wall, and
        // whether a PORTCULLIS texture follows a sloped piece edge or hangs from one height.
        RoomWall(
            "att-off",
            "A free wall 32 steps ahead (second vertex at y 4, on screen's left) carrying the blue base (SCALE 4, a 16-step repeat) with the map's OFFSET_X 8 and OFFSET_Y 8, and the cyan 16×16 square attached at POS_X 16, POS_Y 16. Manual p.43 puts an attach in the wall's texture space, so the offsets should slide it too: does the square start 2 steps past the second vertex at z 9–10, or at POS_X's own 4 steps and z 11–12?",
            Square + Attach(16, 16),
            new TraceProbeWall("wp_w", 32, -4, 32, 4, 8, 8),
            "TEXTURE blu_tex; ATTACH wp_a;",
            RoomRun,
            ("the attach sits in the wall's texture space, the map's offsets included", "cyan from 2 steps past the second vertex, z 9–10"),
            ("the offsets leave POS_X/POS_Y alone", "cyan from 4 steps past the second vertex, z 11–12")),
        RoomWall(
            "att-off-p0",
            "As att-off with FLAGS PORTCULLIS on the wall: the piece's lower border is the room's floor 0, so the flag should change nothing. Does the square still land at z 9–10 (offsets counted) or at z 11–12 (offsets ignored)?",
            Square + Attach(16, 16),
            new TraceProbeWall("wp_w", 32, -4, 32, 4, 8, 8),
            $"TEXTURE blu_tex; ATTACH wp_a; {Port0}",
            RoomRun,
            ("the lower border 0 is the zero and the offsets still count", "cyan from 2 steps past the second vertex, z 9–10"),
            ("the flag, the offsets, or both are ignored", "cyan from 4 steps past the second vertex, z 11–12")),
        Attached(
            "att-two",
            "The pad raised to floor 3 and its ceiling dropped to 11, so the wall has a lower piece (0…3) and an upper one (11…16); the blue base with FLAGS PORTCULLIS and the square at POS_X 8, POS_Y 56 (14 steps down). If each piece counts its repeat from its own lower border, the square lands at 16 − 14 = 2 on the lower piece and 11 + 16 − 14 = 13 on the upper: does it appear on both pieces, or — one origin for the whole wall — on only one?",
            3,
            11,
            Blue,
            "",
            8,
            56,
            Port0,
            FarRun,
            ("each piece counts from its own lower border", "cyan twice: z 1–2 on the lower piece and z 12–13 on the upper"),
            ("the wall has one origin, so the square appears once", "cyan on only one of the two pieces")),
        PadFree(
            "plain-off3",
            "The pad raised to floor 3, its boundary invisible and a free wall inside it (region 2 on both sides) at y 16, x −11…−5, with the map's OFFSET_X 4 and OFFSET_Y 6; the whole 16×16 palette swatch at SCALE 2 (16 rows, an 8-step repeat, index 16·row + column). The control for the row rule: is a row at height z floor(−2z − 6) & 15 (the height-0 grid with OFFSET_Y), or does the grid start at the region's floor 3?",
            3,
            Swatch16,
            "TEXTURE wp_st;",
            [new TraceProbeWall("wp_w", -11, 16, -5, 16, 4, 6, Region: 2)],
            FarRun,
            ("rows from the height-0 grid with OFFSET_Y 6 subtracted", "row = (floor(−2z) − 6) & 15 at every height"),
            ("rows from the region's floor 3, with the offset", "row = (floor(−2(z − 3)) − 6) & 15")),
        PadFree(
            "port-off3",
            "As plain-off3 with FLAGS PORTCULLIS, so the piece's lower border z 3 is the vertical zero: do the rows count from 3 with OFFSET_Y 6 subtracted on top, or from 3 with the offset ignored? And where does the column count start — the second vertex (x −5) at column 2 if OFFSET_X 4 slides it, column 0 if not?",
            3,
            Swatch16,
            $"TEXTURE wp_st; {Port0}",
            [new TraceProbeWall("wp_w", -11, 16, -5, 16, 4, 6, Region: 2)],
            FarRun,
            ("the lower border 3 is the zero and OFFSET_Y 6 counts on top", "row = (floor(−2(z − 3)) − 6) & 15"),
            ("the lower border 3 is the zero and OFFSET_Y is ignored", "row = floor(−2(z − 3)) & 15"),
            ("the columns take the map's OFFSET_X as the texmap wall did", "column 4 next to the second vertex (x −5), then 5, 6, …; column 0 if the offset is ignored")),
        PadFree(
            "port-off3p1",
            "As port-off3 with POSITION 1: the piece's upper border z 16 is the vertical zero, so the rows count down from 16 — with OFFSET_Y 6 subtracted on top of that, or without it?",
            3,
            Swatch16,
            $"TEXTURE wp_st; {Port1}",
            [new TraceProbeWall("wp_w", -11, 16, -5, 16, 4, 6, Region: 2)],
            FarRun,
            ("the upper border 16 is the zero and OFFSET_Y 6 counts on top", "row = (floor(2(16 − z)) − 6) & 15"),
            ("the upper border 16 is the zero and the offset is ignored", "row = floor(2(16 − z)) & 15")),
        Sloped(
            "port-slope",
            "The pad sunk to floor −4 with FLAGS FLOOR_LIFTED and the corner heights 0, 2, 2, 0, so the far step (y 20, x −4…−12) has a lower edge sloping from −2 at x −4 to −4 at x −12 and a flat top at the room's floor 0; the swatch at SCALE 2 with FLAGS PORTCULLIS. Do the rows follow the sloped lower edge (the same row just above the edge at every column) or hang from one height (rows level across the wall, cut by the slope), and from which height?",
            -4,
            "FLAGS FLOOR_LIFTED;",
            [0, 2, 2, 0],
            Swatch16,
            $"TEXTURE wp_st; {Port0}",
            NearRun,
            ("the rows follow the sloped lower edge", "the same row just above the edge at x −4, −8 and −12"),
            ("the rows are level, counted from the region's declared floor −4", "one row per height all along the wall; the edge cuts different rows at the two ends"),
            ("the rows are level, on the height-0 grid", "row = floor(−2z) & 15 across the wall")),
        Sloped(
            "port-slop-p1",
            "The pad raised to floor 3 with FLAGS FLOOR_LIFTED and the corner heights 0, 2, 2, 0, so the lower piece's upper edge slopes on the south face y 12 (3 at x −12 to 5 at x −4) and is flat at 5 on the east face x −4, with POSITION 1. Does the swatch follow the sloped upper edge or hang from one height — and which?",
            3,
            "FLAGS FLOOR_LIFTED;",
            [0, 2, 2, 0],
            Swatch16,
            $"TEXTURE wp_st; {Port1}",
            FarRun,
            ("the rows follow the sloped upper edge", "the same row just below the edge at x −12, −8 and −4"),
            ("the rows are level, hung from the highest corner 5", "row = floor(2(5 − z)) & 15 across the face"),
            ("the rows are level, hung from the lowest corner 3", "row = floor(2(3 − z)) & 15 across the face")),

        // Round 5 — which piece's border anchors an attach on a wall that has a lower piece and an
        // upper one: the wall's lowest edge, each piece's own edge, or the lower piece only.
        Attached(
            "att-two-up",
            "As att-two with the square at POS_X 8, POS_Y 4 (1 step down from the repeat top). If the origin is the wall's lowest edge 0, 16 − 1 lands at 15 on the upper piece (11…16); if the origin is the upper piece's own edge 11, it lands at 26, above the ceiling; if the attach is drawn on the lower piece only, 16 − 1 is over the opening and nothing shows.",
            3,
            11,
            Blue,
            "",
            8,
            4,
            Port0,
            FarRun,
            ("the wall's lowest edge 0 is the origin and the upper piece is drawn", "cyan z 14–15 on the upper piece"),
            ("the upper piece's own edge 11 is the origin, so the square is at 26", "no cyan; it would be above the ceiling"),
            ("the attach is drawn on the lower piece only", "no cyan; 16 − 1 would be over the opening")),
        Attached(
            "att-two-p1",
            "As att-two with POSITION 1 and the square at POS_X 8, POS_Y 72 (18 steps down). If the wall hangs from the top of its upper piece 16, 16 + 16 − 18 lands at 14 on the upper piece; from the top of the lower piece 3 it lands at 1 on the lower piece; per piece, both.",
            3,
            11,
            Blue,
            "",
            8,
            72,
            Port1,
            FarRun,
            ("the zero is the upper piece's top 16", "cyan z 13–14 on the upper piece"),
            ("the zero is the lower piece's top 3", "cyan z 0–1 on the lower piece"),
            ("each piece has its own zero", "cyan twice: z 0–1 on the lower piece and z 13–14 on the upper")),

        // Round 6 — a wall against a solid region (floor and ceiling both 41 or 43, retail's rooms
        // against `border`), where the lower piece 0…F and the upper piece 16…C are the same wall
        // twice, and a gap case where the two pieces overlap (16…19 or 16…19). Which border
        // anchors POSITION 1 and an attach: the lower piece's top, the room's ceiling, or the
        // height-0 grid?
        Piece(
            "sol-p1",
            "A wall against a solid region: the pad's floor and ceiling both 41 (retail's rooms against `border`), the swatch at SCALE 2 with POSITION 1. Which height is the vertical zero — the lower piece's top 41 (row 2 just below the room's ceiling 16), the room's ceiling 16 (row 0), or the height-0 grid (row 0 as well)?",
            41,
            41,
            Swatch16,
            $"TEXTURE wp_st; {Port1}",
            FarRun,
            ("the lower piece's top 41 is the zero", "row 2 just below z 16; row = floor(2(41 − z)) & 15"),
            ("the room's ceiling 16 is the zero", "row 0 just below z 16; row = floor(2(16 − z)) & 15"),
            ("the height-0 grid", "row 0 just below z 16; row = floor(−2z) & 15")),
        Piece(
            "sol-p1-b",
            "As sol-p1 with the solid region at 43, which separates the room's ceiling from the height-0 grid: the lower piece's top 43 puts row 6 just below z 16, while 16 and the grid both stay at row 0.",
            43,
            43,
            Swatch16,
            $"TEXTURE wp_st; {Port1}",
            FarRun,
            ("the lower piece's top 43 is the zero", "row 6 just below z 16"),
            ("the room's ceiling 16 is the zero", "row 0 just below z 16"),
            ("the height-0 grid", "row 0 just below z 16")),
        Piece(
            "sol-p0",
            "As sol-p1 with POSITION 0: is the wall's bottom 0 the zero — row 15 just above z 0 and row 0 at z 8, the plain grid — or does the lower piece's 0…41 band move the rows?",
            41,
            41,
            Swatch16,
            $"TEXTURE wp_st; {Port0}",
            FarRun,
            ("the wall's bottom 0 is the zero, as the plain grid", "row 15 just above z 0, row 0 at z 8; row = floor(−2z) & 15"),
            ("the rows follow the piece's own span (0…41)", "a different row just above z 0")),
        Piece(
            "gap-p1",
            "The pad raised to floor 19 with its ceiling at 24, so the lower piece (0…19) and the upper piece (16…24) overlap between 16 and 19, and the wall is drawn twice there. With POSITION 1, which top is the zero: the lower piece's 19 (row 6 just below z 16), the room's ceiling 16 (row 0), or the upper piece's 24 (row 0)?",
            19,
            24,
            Swatch16,
            $"TEXTURE wp_st; {Port1}",
            FarRun,
            ("the lower piece's top 19 is the zero", "row 6 just below z 16"),
            ("the room's ceiling 16 is the zero", "row 0 just below z 16"),
            ("the upper piece's top 24 is the zero", "row 0 just below z 16")),
        Piece(
            "gap-p1-b",
            "As gap-p1 with the pad's ceiling at 25, which separates the upper piece's top from the room's ceiling: the lower piece's 19 puts row 6 just below z 16, the room's ceiling 16 row 0, and the upper piece's 25 row 2.",
            19,
            25,
            Swatch16,
            $"TEXTURE wp_st; {Port1}",
            FarRun,
            ("the lower piece's top 19 is the zero", "row 6 just below z 16"),
            ("the room's ceiling 16 is the zero", "row 0 just below z 16"),
            ("the upper piece's top 25 is the zero", "row 2 just below z 16")),
        Attached(
            "sol-att-a",
            "The solid-region wall (floor and ceiling 41) with POSITION 1 and the cyan square at POS_X 8, POS_Y 80 (20 steps down): from a zero at the room's ceiling 16 the square's top is 16 + 16 − 20 = 12 and it shows; from the lower piece's top 41 it is 37, above the ceiling and hidden.",
            41,
            41,
            Blue,
            "",
            8,
            80,
            Port1,
            FarRun,
            ("the room's ceiling 16 is the zero", "cyan z 11–12 on the wall"),
            ("the lower piece's top 41 is the zero", "no cyan; the square would be at z 36–37 above the ceiling")),
        Attached(
            "sol-att-b",
            "As sol-att-a with POS_Y 180 (45 steps down): from the lower piece's top 41 the square's top is 41 + 16 − 45 = 12 and it shows; from the room's ceiling 16 it is −13, under the floor.",
            41,
            41,
            Blue,
            "",
            8,
            180,
            Port1,
            FarRun,
            ("the lower piece's top 41 is the zero", "cyan z 11–12 on the wall"),
            ("the room's ceiling 16 is the zero", "no cyan; the square would be under the floor")),
        Attached(
            "sol-att-c",
            "As sol-p0 (solid region 41…41, FLAGS PORTCULLIS) with the square at POS_X 8, POS_Y 48 (12 steps down): if the wall's bottom 0 is the zero it lands at 4 (z 3–4); a zero at the lower piece's top 41 would put it at 45, above the ceiling.",
            41,
            41,
            Blue,
            "",
            8,
            48,
            Port0,
            FarRun,
            ("the wall's bottom 0 is the zero", "cyan z 3–4 on the wall"),
            ("the lower piece's top 41 is the zero", "no cyan; the square would be at z 44–45")),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "wall-pieces",
        TraceFile,
        "ovwps",
        "The final screenshot `OVWPS0.PCX` is the reading: on the pad cases each pixel's palette index is\n" +
        "the swatch texel (column = index mod 16, row = index div 16) under it, and the cyan (10) square is\n" +
        "the attach. Compare with `case.json` → `predictions`, check `OVCON.TXT` for compile errors.",
        All);
}
