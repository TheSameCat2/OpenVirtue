// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Text;
using static ProximityProbeCases;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>careful-stop</c> probe
/// (<c>docs/clean-room/observations/OV-20261103-careful-stop.md</c>), two questions the
/// oracle-diff replay of OV-20261019-border-kinds and OV-20261020-view-activity left open:
/// <list type="bullet">
/// <item><c>north-*</c>, <c>south-*</c>: where does a skipped <c>CAREFULLY</c> actor stop when its
/// six-cycle step and the half of it both end inside a pad 2 steps up? long-pad's walker from
/// 23 (3 from the border) stopped 1.049 short of it; actor-climb-2's from 22 (2 from it) stopped
/// 1.524 short, half the clear distance. Two walkers per carrier, each from its own gap, at
/// 1.6 a cycle set over <c>TIME_CORR</c> so the step is the same at any frame rate.</item>
/// <item><c>wall-dist</c>: berkeley-near read a wall's <c>DISTANCE</c> 9.990 at (16, 0), ten units
/// under |dx| + |dy| to either vertex, where other stands were exact. Stands at several points
/// with the wall's <c>DISTANCE</c> printed beside <c>PLAYER_X</c>/<c>PLAYER_Y</c>.</item>
/// </list>
/// While <c>cs_p</c> is set the sampler prints a <c>C</c> line (or <c>W</c>) in every cycle.
/// </summary>
internal static class CarefulStopProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVCST0.TXT).</summary>
    public const string TraceFile = "ovcst";

    private const string ActorTexture = "TEXTURE cs_tex { SCALE_X 32; SCALE_Y 32; BMAPS red_map; }\n";

    /// <summary>
    /// Two <c>CAREFULLY</c> way actors, their synonyms and position skills, and the sampler that sets
    /// each one's <c>SPEED</c> to 1.6 over <c>TIME_CORR</c> in every cycle and, while <c>cs_p</c> is
    /// set, prints <c>C</c> with both positions.
    /// </summary>
    private static readonly string WalkerDeclarations = BuildWalkerDeclarations();

    private static string BuildWalkerDeclarations()
    {
        var sb = new StringBuilder(Skills + ActorTexture + "SKILL cs_p { VAL 0; }\nWAY cs_w1;\nWAY cs_w2;\n" +
            "WALL cs_padw { TEXTURE red_tex; }\n");
        var rules = new StringBuilder();
        var printed = new List<string>();
        for (int i = 1; i <= 2; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"SYNONYM cs_s{i} {{ TYPE ACTOR; }}\nSKILL cs_x{i} {{ VAL 0; }}\nSKILL cs_y{i} {{ VAL 0; }}\n");
            sb.Append(CultureInfo.InvariantCulture, $"ACTOR cs_a{i} {{ TEXTURE cs_tex; TARGET cs_w{i}; SPEED 0; DIST 1; FLAGS CAREFULLY; }}\n");
            rules.Append(CultureInfo.InvariantCulture, $"    RULE cs_x{i} = cs_s{i}.X;\n    RULE cs_y{i} = cs_s{i}.Y;\n");
            printed.AddRange([$"cs_x{i}", $"cs_y{i}"]);
        }

        sb.Append(Sampler(
            "    IF (TIME_CORR > 0) {\n        RULE cs_s1.SPEED = 1.6 / TIME_CORR;\n        RULE cs_s2.SPEED = 1.6 / TIME_CORR;\n    }\n" +
            "    IF (cs_p > 0) {\n" + Indent(rules.ToString() + L("C", [.. printed])) + "    }\n"));
        return sb.ToString();
    }

    /// <summary>The player at (40, 0) facing +X, the walkers far behind it, for 48 cycles (three long steps each).</summary>
    private const string WalkerRun =
        "    SET cs_s1, cs_a1;\n    SET cs_s2, cs_a2;\n" + ArmSampler + "    RULE cs_p = 1;\n" +
        "    PRINT_STRING \"GO\";\n    PRINT_STRING \" \";\n    PRINT_VALUE PLAYER_X;\n    PRINT_STRING \" \";\n    PRINT_VALUE PLAYER_Y;\n    PRINT_STRING \"\\n\";\n" +
        "    SET PLAYER_X, 40;\n    SET PLAYER_Y, 0;\n    LOCATE;\n    SET PLAYER_ANGLE, 0;\n" +
        "    WAIT 48;\n" + "    RULE cs_p = 0;\n" + ClearSampler;

    /// <summary>
    /// Two walkers at x = −10.5 and −5.5 starting <paramref name="y1"/> and <paramref name="y2"/>, on
    /// ways to <paramref name="toY"/> across the pad (y 12…20, 2 steps up behind plain walls).
    /// </summary>
    private static TraceProbeCase Walkers(string label, string question, double y1, double y2, double toY) => new(
        label,
        question,
        WalkerDeclarations,
        WalkerRun,
        Predict(
            ("the clearance stop", "the refused walker stands 1.049 from the border it faces (21.049 or 10.951)"),
            ("half the clear distance", "it stands at its start less half of (gap − 1.049)"),
            ("the stop depends on the gap", "one walker at the clearance, the other short of it")),
        Map: new TraceProbeMap(
            PadBody: "SKILL1 2;",
            PadWall: "cs_padw",
            PadFloor: 2,
            Placements:
            [
                FormattableString.Invariant($"ACTOR cs_a1 -10.5 {y1} 90 0"), FormattableString.Invariant($"ACTOR cs_a2 -5.5 {y2} 90 0"),
                FormattableString.Invariant($"WAY cs_w1 -10.5 {y1} -10.5 {toY}"), FormattableString.Invariant($"WAY cs_w2 -5.5 {y2} -5.5 {toY}"),
            ]));

    // ---- a wall's DISTANCE after beams -----------------------------------------------------------

    private static readonly string WallDistDeclarations =
        Skills + "SKILL cs_p { VAL 0; }\nSKILL cs_d { VAL 0; }\nSYNONYM cs_sw { TYPE WALL; }\n" +
        "WALL cs_wall { TEXTURE red_tex; }\n" +
        Sampler("    IF (cs_p > 0) {\n        RULE cs_d = cs_sw.DISTANCE;\n" + Indent(L("W", "cs_d", "PLAYER_X", "PLAYER_Y", "PLAYER_ANGLE")) + "    }\n");

    private static string Stand(double x, double y, double angle, string tag) =>
        Beam(x, y, tag, locate: true) + FormattableString.Invariant($"    SET PLAYER_ANGLE, {angle:0.####};\n    WAIT 6;\n");

    private static readonly string WallDistRun =
        "    SET cs_sw, cs_wall;\n" + ArmSampler + "    RULE cs_p = 1;\n" + L("GO", "PLAYER_X", "PLAYER_Y") +
        Stand(16, 0, Math.PI, "S1") + Stand(16, 0, 0, "S2") + Stand(16, 1, Math.PI, "S3") + Stand(15, 0, Math.PI, "S4") +
        Stand(12, 3, 0, "S5") + Stand(8, 3, 0, "S6") + Stand(0, 0, 0, "S7") + Stand(20, 4, Math.PI, "S8") + Stand(10, 8, Math.PI, "S9") +
        Stand(16, 0, Math.PI, "S10") +
        "    RULE cs_p = 0;\n" + ClearSampler;

    /// <summary>
    /// Round 2: at (16, 0), each facing twice in a row (a beam to the same point with the same
    /// angle), then at (15, 0) with the angle kept, for angles from 0 to 5.5: is the wall's
    /// <c>DISTANCE</c> the map's |dx| + |dy| or a rotation of the offset in 22.10?
    /// </summary>
    private static readonly string WallAnglesRun =
        "    SET cs_sw, cs_wall;\n" + ArmSampler + "    RULE cs_p = 1;\n" + L("GO", "PLAYER_X", "PLAYER_Y") +
        string.Concat(new[] { 0.0, 0.5, 1.0, 1.5708, 2.0, 2.5, 3.1416, 4.0, 4.7124, 5.5 }.Select((a, i) =>
            Stand(16, 0, a, $"A{i}") + Stand(16, 0, a, $"B{i}") + Stand(15, 0, a, $"C{i}"))) +
        "    RULE cs_p = 0;\n" + ClearSampler;

    // ---- round 3: entities' DISTANCE after a beam, a walk at a facing, the sprite's column edge ----

    /// <summary>
    /// The wall of wall-dist, a still 2-wide thing at (16, 4) and a still 2-wide actor at (−8, 6), their
    /// <c>DISTANCE</c> printed with the wall's in every cycle: beams with and without a turn at facings
    /// 0.5 and 1.0, then a walk by velocity along +X facing 1.0.
    /// </summary>
    private static readonly string DistBeamDeclarations =
        Skills + "SKILL cs_p { VAL 0; }\nSKILL cs_d { VAL 0; }\nSKILL cs_dk { VAL 0; }\nSKILL cs_da { VAL 0; }\n" +
        "SYNONYM cs_sw { TYPE WALL; }\nSYNONYM cs_sk { TYPE THING; }\nSYNONYM cs_sa { TYPE ACTOR; }\n" +
        "TEXTURE cs_tex { SCALE_X 32; SCALE_Y 32; BMAPS red_map; }\n" +
        "WALL cs_wall { TEXTURE red_tex; }\nTHING cs_k { TEXTURE cs_tex; DIST 1; }\nACTOR cs_a { TEXTURE cs_tex; SPEED 0; DIST 1; }\n" +
        Sampler(
            "    IF (cs_p > 0) {\n        RULE cs_d = cs_sw.DISTANCE;\n        RULE cs_dk = cs_sk.DISTANCE;\n        RULE cs_da = cs_sa.DISTANCE;\n" +
            Indent(L("W", "cs_d", "cs_dk", "cs_da", "PLAYER_X", "PLAYER_Y", "PLAYER_ANGLE")) + "    }\n");

    private static readonly string DistBeamRun =
        "    SET cs_sw, cs_wall;\n    SET cs_sk, cs_k;\n    SET cs_sa, cs_a;\n" + ArmSampler + "    RULE cs_p = 1;\n" + L("GO", "PLAYER_X", "PLAYER_Y") +
        Stand(10, 0, 0.5, "A") + Stand(9, 0, 0.5, "B") + Stand(9, 0, 1.0, "C") + Stand(8, 1, 1.0, "D") +
        "    RULE pe_vx = 0.2;\n    WAITT 8;\n    RULE pe_vx = 0;\n" + L("E", "PLAYER_X", "PLAYER_Y") + "    WAIT 6;\n" +
        "    RULE pe_vx = 0.2;\n    RULE pe_vy = 0.1;\n    WAITT 8;\n    RULE pe_vx = 0;\n    RULE pe_vy = 0;\n" + L("F", "PLAYER_X", "PLAYER_Y") + "    WAIT 6;\n" +
        Stand(12, 2, 1.0, "G") +
        "    RULE cs_p = 0;\n" + ClearSampler;

    /// <summary>A still 2- or 4-wide actor 40 ahead (or 20), <c>PLAYER_ANGLE</c> swept by 0.001 across the cone's edge, its <c>VISIBLE</c> printed every cycle.</summary>
    private static string ColumnsDeclarations(string texture) =>
        Skills + "SKILL cs_p { VAL 0; }\nSKILL cs_k { VAL 0; }\nSKILL cs_v { VAL 0; }\nSYNONYM cs_s1 { TYPE ACTOR; }\n" +
        texture + "ACTOR cs_a1 { TEXTURE cs_tex; DIST 1; }\nWALL cs_occ { TEXTURE red_tex; }\n" +
        Sampler(
            "    IF (cs_p > 0) {\n        IF (cs_s1.VISIBLE == 1) {\n            RULE cs_v = 1;\n        } ELSE {\n            RULE cs_v = 0;\n        }\n" +
            Indent(L("C", "cs_v", "PLAYER_ANGLE", "PLAYER_Y")) + "    }\n");

    private static string Sweep(string skill, double from, double to, double step) =>
        FormattableString.Invariant($"    RULE cs_k = {from};\n    WHILE (cs_k < {to + (step / 2):0.####}) {{\n        RULE {skill} = cs_k;\n        WAIT 1;\n        RULE cs_k = cs_k + {step};\n    }}\n");

    private static string ColumnsRun(double angleFrom, double angleTo) =>
        "    SET cs_s1, cs_a1;\n" + ArmSampler + "    RULE cs_p = 1;\n" + L("GO", "PLAYER_X", "PLAYER_Y") +
        FormattableString.Invariant($"    SET PLAYER_ANGLE, {angleFrom};\n    WAIT 4;\n") + Sweep("PLAYER_ANGLE", angleFrom, angleTo, 0.001) + "    WAIT 4;\n" +
        "    RULE cs_p = 0;\n" + ClearSampler;

    private static readonly string OccludeRun =
        "    SET cs_s1, cs_a1;\n" + ArmSampler + "    RULE cs_p = 1;\n" + L("GO", "PLAYER_X", "PLAYER_Y") +
        "    SET PLAYER_ANGLE, 0;\n    SET PLAYER_Y, -3.5;\n    WAIT 4;\n" + Sweep("PLAYER_Y", -3.5, -2.3, 0.05) + "    WAIT 4;\n" +
        "    RULE cs_p = 0;\n" + ClearSampler;

    private const string Width2 = "TEXTURE cs_tex { SCALE_X 32; SCALE_Y 32; BMAPS red_map; }\n";
    private const string Width4 = "TEXTURE cs_tex { SCALE_X 16; SCALE_Y 16; BMAPS red_map; }\n";

    private static readonly IReadOnlyList<TraceProbeCase> Round3 =
    [
        new(
            "round3/dist-beam",
            "Round 3. A thing at (16, 4), an actor at (-8, 6) and the wall from (10, -4) to (10, 4): their DISTANCE after beams with and without a turn at facings 0.5 and 1.0, and after walks by velocity facing 1.0 (E along +X, F diagonal).",
            DistBeamDeclarations,
            DistBeamRun,
            Predict(
                ("things and actors as walls", "after B and D (no turn) the thing's and actor's DISTANCE are their view-space values; after a walk the map's"),
                ("things and actors always the map's", "|dx| + |dy| at every stand")),
            Map: new TraceProbeMap(Placements: ["THING cs_k 16 4 0 0", "ACTOR cs_a -8 6 0 0"], Walls: [new TraceProbeWall("cs_wall", 10, -4, 10, 4)])),
        new(
            "round3/cone-w2",
            "Round 3. A 2-wide actor 40 ahead at (40, 0); PLAYER_ANGLE from 0.505 to 0.535 by 0.001: in which frame is it no longer VISIBLE (its near edge leaves the cone at 0.525)?",
            ColumnsDeclarations(Width2),
            ColumnsRun(0.505, 0.535),
            Predict(
                ("whole columns from a rounded centre, width cut to pixels", "lost in the frame at about 0.521"),
                ("the continuous edge", "lost in the frame at 0.525")),
            Map: new TraceProbeMap(Placements: ["ACTOR cs_a1 40 0 0 0"])),
        new(
            "round3/cone-w4",
            "Round 3. A 4-wide actor 40 ahead; PLAYER_ANGLE from 0.530 to 0.560 by 0.001 (its near edge leaves the cone at 0.550).",
            ColumnsDeclarations(Width4),
            ColumnsRun(0.530, 0.560),
            Predict(("whole columns", "lost a few thousandths before 0.550"), ("the continuous edge", "lost at 0.550")),
            Map: new TraceProbeMap(Placements: ["ACTOR cs_a1 40 0 0 0"])),
        new(
            "round3/cone-w2-d20",
            "Round 3. A 2-wide actor 20 ahead at (20, 0); PLAYER_ANGLE from 0.530 to 0.560 by 0.001 (its near edge leaves the cone at 0.550).",
            ColumnsDeclarations(Width2),
            ColumnsRun(0.530, 0.560),
            Predict(("whole columns", "lost a few thousandths before 0.550"), ("the continuous edge", "lost at 0.550")),
            Map: new TraceProbeMap(Placements: ["ACTOR cs_a1 20 0 0 0"])),
        new(
            "round3/occlude-w2",
            "Round 3. The 2-wide actor at (40, 0) behind the end of a plain wall from (30, -20) to (30, 0); PLAYER_Y from -3.5 to -2.3 by 0.05 facing +X (its edge clears the wall's end at -3.0).",
            ColumnsDeclarations(Width2),
            OccludeRun,
            Predict(("whole columns", "VISIBLE from about -2.8"), ("the continuous edge", "VISIBLE from -2.95")),
            Map: new TraceProbeMap(Placements: ["ACTOR cs_a1 40 0 0 0"], Walls: [new TraceProbeWall("cs_occ", 30, -20, 30, 0)])),
    ];

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "round2/wall-angles",
            "Round 2. The wall of wall-dist read 9.990 at (16, 0) when the player faced 3.1416 in two successive stands and 10.000 after a turn: stands at (16, 0) twice and (15, 0) once for ten facings.",
            WallDistDeclarations,
            WallAnglesRun,
            Predict(
                ("|dx| + |dy| in the map", "10 and 9 at every facing"),
                ("the offset rotated by the facing, in 22.10", "at 1.0: 6·553/1024 + 4·861/1024 = 6.604 and |6·861 − 4·553|/1024 = 2.885, 9.488 in all; 9.990 at 3.1416"),
                ("short only after a repeated facing", "A exact, B and C short")),
            Map: new TraceProbeMap(Walls: [new TraceProbeWall("cs_wall", 10, -4, 10, 4)])),
        Walkers("north-2-25", "From 2 and 2.5 before the pad's north border (22 and 22.5), walking south: the six-cycle step and its half end inside the pad. Where does each walker stop?", 22, 22.5, 2),
        Walkers("north-3-35", "From 3 and 3.5 before the north border (long-pad's 23 stopped at 21.049).", 23, 23.5, 2),
        Walkers("north-4-45", "From 4 and 4.5 before the north border.", 24, 24.5, 2),
        Walkers("south-2-3", "From 2 and 3 before the south border (10 and 9), walking north.", 10, 9, 23),
        Walkers("south-4-45", "From 4 and 4.5 before the south border (8 and 7.5).", 8, 7.5, 23),
        new TraceProbeCase(
            "wall-dist",
            "A plain free-standing wall from (10, -4) to (10, 4): its DISTANCE after beams to (16, 0), (16, 1), (15, 0), (12, 3), (8, 3), the origin, (20, 4) and (10, 8), facing +X or -X, six cycles each.",
            WallDistDeclarations,
            WallDistRun,
            Predict(
                ("|dx| + |dy| to the nearer vertex", "W reads 10, 9, 9, 3, 3, 14, 10, 4 at the stands"),
                ("ten units short at (16, 0) only", "9.990 at S1, S2 and S10, the others exact"),
                ("short whenever |dy| equals the vertex's y", "9.990 at (16, 0) and (20, 4)")),
            Map: new TraceProbeMap(Walls: [new TraceProbeWall("cs_wall", 10, -4, 10, 4)])),
        .. Round3,
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "careful-stop",
        TraceFile,
        "ovcss",
        "`GO`, then while the case runs one `C` line every cycle with both walkers' X and Y (walker cases) or one `W`\n" +
        "line with the wall's DISTANCE, PLAYER_X, PLAYER_Y and PLAYER_ANGLE (wall-dist); the `TK` sampler every 16 ticks.",
        All);
}
