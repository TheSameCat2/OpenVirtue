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

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
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
