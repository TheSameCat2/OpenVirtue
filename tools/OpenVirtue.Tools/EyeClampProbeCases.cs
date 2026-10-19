// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Text;
using static ProximityProbeCases;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>eye-clamp</c> probe (<c>docs/clean-room/observations/OV-20261019-eye-clamp.md</c>).
/// OV-20261019-border-climb saw the runtime keep <c>PLAYER_Z</c> within the floor + 0.35 and the
/// ceiling − 0.35 of the player's region with no movement script running, and saw the player stop
/// about 1.25 from a blocking wall with the default <c>PLAYER_WIDTH</c> 1.2. This probe asks how
/// <c>PLAYER_VZ</c> (p.111) moves the eye against that clamp (does it stop with the feet on the
/// floor, at the clamp, and is <c>PLAYER_VZ</c> changed when it stops), whether the 0.35 margin
/// depends on <c>PLAYER_WIDTH</c>, and how the stop distance from a solid wall and from a blocking
/// region border depends on <c>PLAYER_WIDTH</c> and on the walking speed. While <c>ec_p</c> is set,
/// the global sampler prints a <c>V</c> line in every cycle, before the runtime moves the player.
/// </summary>
internal static class EyeClampProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVECX0.TXT).</summary>
    public const string TraceFile = "ovecx";

    private const string ExtraSkills =
        "SKILL ec_p { VAL 0; }\n" +
        "SKILL ec_f { VAL 0; }\n" +
        "SKILL ec_vz { VAL 0; }\n";

    private static readonly string[] Pose = ["PLAYER_X", "PLAYER_Y", "PLAYER_Z", "PLAYER_VZ", "PLAYER_HGT", "IMPACT_VZ", "PLAYER_WIDTH"];

    /// <summary>A trace line with the player's pose, vertical speed, height above the floor and width.</summary>
    private static string Line(string token) => L(token, Pose);

    /// <summary>
    /// The sampler's prologue: while <c>ec_f</c> is set it writes <c>ec_vz</c> to <c>PLAYER_VZ</c> in
    /// every cycle; while <c>ec_p</c> is set it prints <c>V</c> with <c>TIME_CORR</c> and the pose.
    /// </summary>
    private static readonly string Prologue =
        "    IF (ec_f > 0) {\n        SET PLAYER_VZ, ec_vz;\n    }\n" +
        "    IF (ec_p > 0) {\n" + Indent(L("V", ["TIME_CORR", .. Pose])) + "    }\n";

    private static string Declarations(string extra = "") =>
        Skills + ExtraSkills + extra + Sampler(Prologue);

    /// <summary>
    /// One vertical move: set <c>PLAYER_SIZE</c>, <c>PLAYER_WIDTH</c> and <c>PLAYER_Z</c> with
    /// <c>PLAYER_VZ</c> 0, stand 8 ticks, then either set <c>PLAYER_VZ</c> once to
    /// <paramref name="vz"/> or (<paramref name="force"/>) write it in every cycle, for
    /// <paramref name="ticks"/> ticks with <c>V</c> lines, then stop and stand 8 ticks.
    /// </summary>
    private static string Vertical(string tag, double z, double vz, bool force = false, double size = 3, double width = 1.2, int ticks = 48)
    {
        var sb = new StringBuilder();
        sb.Append(Line(tag));
        sb.Append(CultureInfo.InvariantCulture, $"    SET PLAYER_SIZE, {size};\n    SET PLAYER_WIDTH, {width};\n    SET PLAYER_VZ, 0;\n    SET PLAYER_Z, {z};\n");
        sb.Append(Line(tag + "D"));
        sb.Append("    WAITT 8;\n").Append(Line(tag + "S"));
        sb.Append("    RULE ec_p = 1;\n");
        sb.Append(force
            ? FormattableString.Invariant($"    RULE ec_vz = {vz};\n    RULE ec_f = 1;\n")
            : FormattableString.Invariant($"    SET PLAYER_VZ, {vz};\n"));
        sb.Append(Line(tag + "V"));
        sb.Append(CultureInfo.InvariantCulture, $"    WAITT {ticks};\n");
        sb.Append("    RULE ec_f = 0;\n    RULE ec_p = 0;\n").Append(Line(tag + "W"));
        sb.Append("    SET PLAYER_VZ, 0;\n    WAITT 8;\n").Append(Line(tag + "E"));
        return sb.ToString();
    }

    /// <summary>
    /// One push: beam with <c>LOCATE</c> to (<paramref name="x"/>, <paramref name="y"/>), set
    /// <c>PLAYER_WIDTH</c>, stand 8 ticks, walk at (<paramref name="vx"/>, <paramref name="vy"/>) for
    /// <paramref name="ticks"/> ticks with <c>V</c> lines, stop and stand 8 ticks.
    /// </summary>
    private static string Push(string tag, double width, double x, double y, double vx, double vy, int ticks)
    {
        var sb = new StringBuilder();
        sb.Append(Line(tag));
        sb.Append(CultureInfo.InvariantCulture, $"    SET PLAYER_X, {x};\n    SET PLAYER_Y, {y};\n    LOCATE;\n    SET PLAYER_WIDTH, {width};\n");
        sb.Append(Line(tag + "D"));
        sb.Append("    WAITT 8;\n").Append(Line(tag + "S"));
        sb.Append(CultureInfo.InvariantCulture, $"    RULE ec_p = 1;\n    RULE pe_vx = {vx};\n    RULE pe_vy = {vy};\n    WAITT {ticks};\n");
        sb.Append("    RULE pe_vx = 0;\n    RULE pe_vy = 0;\n    RULE ec_p = 0;\n").Append(Line(tag + "W"));
        sb.Append("    WAITT 8;\n").Append(Line(tag + "E"));
        return sb.ToString();
    }

    /// <summary>A push from (18, 0) along +X into the solid wall at x = 24 (contact after 4.8 steps at width 1.2).</summary>
    private static string PushWall(string tag, double width, double speed = 0.2, int? ticks = null) =>
        Push(tag, width, 18, 0, speed, 0, ticks ?? (int)Math.Ceiling(9 / speed));

    /// <summary>A push from (−8, 6) along +Y into the pad's south wall at y = 12.</summary>
    private static string PushBorder(string tag, double width, double speed = 0.2) =>
        Push(tag, width, -8, 6, 0, speed, (int)Math.Ceiling(9 / speed));

    private static string Run(params string[] parts) =>
        ArmSampler + Line("GO") + string.Concat(parts) + ClearSampler;

    private static readonly TraceProbeWall SolidWall = new("ec_wall", 24, -8, 24, 8);

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "vz-move",
            "In the room (floor 0, ceiling 16): where does PLAYER_VZ stop the eye going down and up, is PLAYER_VZ (or IMPACT_VZ) changed when it stops, is it scaled by TIME_CORR, and does a set-once PLAYER_VZ keep moving the player?",
            Declarations(),
            Run(
                Vertical("A", 8, -0.25), Vertical("B", 8, 0.25),
                Vertical("C", 8, -0.25, force: true), Vertical("D", 8, 0.25, force: true),
                Vertical("E", 8, -0.25, size: 8), Vertical("F", 8, -0.25, width: 3),
                Vertical("G", 0.1, 0, width: 3), Vertical("H", 15.95, 0, width: 3)),
            Predict(
                ("free motion, clamped to [floor + 0.35, ceiling − 0.35] after each move", "A, C, E, F end at Z 0.35 (PLAYER_HGT −2.65 for size 3); B, D end at 15.65; G 0.35; H 15.65"),
                ("PLAYER_VZ stops with the feet on the floor", "A, C end at Z 3 (PLAYER_HGT 0); E at 8; B, D at 15.65"),
                ("the clamp zeroes PLAYER_VZ", "V lines show PLAYER_VZ 0 once Z stops (A, B)"),
                ("PLAYER_VZ is steps per tick", "Z changes by 0.25 × TIME_CORR per cycle"),
                ("the margin depends on PLAYER_WIDTH", "F, G end above 0.35, H below 15.65"))),
        new TraceProbeCase(
            "vz-rate",
            "Round 3. vz-move moved the eye 1.000 per cycle for PLAYER_VZ ±0.25 at TIME_CORR 0.25–0.281 (unscaled), and 2 per cycle at TIME_CORR 8 (a run at DOSBox-X cycles=fixed 1500). Is the move linear in PLAYER_VZ, and how does it depend on TIME_CORR? Run at several emulated CPU speeds.",
            Declarations(),
            Run(
                Vertical("A", 8, 0.05, ticks: 16), Vertical("B", 8, -0.1, ticks: 16), Vertical("C", 8, 0.5, ticks: 16),
                Vertical("D", 8, -1, ticks: 16), Vertical("E", 8, 0.02, ticks: 16)),
            Predict(
                ("4 × PLAYER_VZ per cycle", "A 0.2, B −0.4, C 2, D −4, E 0.08 per cycle at any rate"),
                ("PLAYER_VZ × max(TIME_CORR, 4)", "4 × PLAYER_VZ per cycle while TIME_CORR ≤ 4, PLAYER_VZ × TIME_CORR above"),
                ("PLAYER_VZ × TIME_CORR × 16, capped", "per cycle proportional to TIME_CORR until a cap"))),
        new TraceProbeCase(
            "stop-solid",
            "A solid free-standing wall at x = 24 (room on both sides): how far from it does a push along +X stop, for PLAYER_WIDTH 1.2, 2, 3 and 0.5 (p.110: minimum 1.2), and at 0.05 and 1 step per tick?",
            Declarations("WALL ec_wall { TEXTURE red_tex; }\n"),
            Run(
                PushWall("A", 1.2), PushWall("B", 2), PushWall("C", 3), PushWall("D", 0.5),
                PushWall("E", 1.2, 0.05), PushWall("F", 1.2, 1), PushWall("G", 3, 0.05)),
            Predict(
                ("PLAYER_WIDTH + 0.05", "A, E, F stop at x ≈ 22.75; B at 21.95; C, G at 20.95; D as A (1.2 minimum)"),
                ("a fixed 1.25 whatever PLAYER_WIDTH", "every walk stops at x ≈ 22.75"),
                ("PLAYER_WIDTH, with the last step rejected", "stops between 24 − w − step and 24 − w; E, G closer than A, C")),
            Map: new TraceProbeMap(Walls: [SolidWall])),
        new TraceProbeCase(
            "stop-border",
            "The pad with floor and ceiling at 16 behind plain walls (a blocking region border at y = 12): how far from it does a push along +Y stop, for PLAYER_WIDTH 1.2, 2, 3 and at 0.05 step per tick?",
            Declarations("WALL ec_bwall { TEXTURE red_tex; }\n"),
            Run(PushBorder("A", 1.2), PushBorder("B", 2), PushBorder("C", 3), PushBorder("E", 1.2, 0.05)),
            Predict(
                ("as stop-solid", "the same distances from y = 12 as from x = 24 in stop-solid"),
                ("a region border stops at PLAYER_WIDTH + 0.05", "A, E at y ≈ 10.75; B at 9.95; C at 8.95")),
            Map: new TraceProbeMap(PadWall: "ec_bwall", PadFloor: 16, PadCeiling: 16, PadBody: "SKILL1 2;")),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "eye-clamp",
        TraceFile,
        "ovecp",
        "After the skill dump, `IF_START` selects the trace file, writes `BEGIN` and runs the variant's case.\n" +
        "Each trace line is a token followed by `TICKS` and `pe_n` (the global EACH_TICK sampler's run count)\n" +
        "when it was printed, then PLAYER_X, PLAYER_Y, PLAYER_Z, PLAYER_VZ, PLAYER_HGT, IMPACT_VZ and\n" +
        "PLAYER_WIDTH. `V` lines come from the sampler in every cycle of a move (TIME_CORR, then the same\n" +
        "skills), before the runtime moves the player. A move `A` prints `A`, `AD` (after the set-up), `AS`,\n" +
        "`AV` (vertical moves: after PLAYER_VZ is set), `AW` (end of the move) and `AE`. Compare the trace\n" +
        "with `case.json` → `predictions`, and check `OVCON.TXT` for compile errors.",
        All);
}
