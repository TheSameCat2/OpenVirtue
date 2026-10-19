// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static ProximityProbeCases;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>wall-distance</c> probe
/// (<c>docs/clean-room/observations/OV-20261019-wall-distance.md</c>). OV-20261019-proximity-events
/// measured a thing's <c>DIST</c> test and <c>MY.DISTANCE</c> as |dx| + |dy| to its centre. For a
/// wall the manual (p.51–52) measures both to the wall's nearest vertex, approximately (±20 %), and
/// only within the player's <c>CLIP_DIST</c>. This probe walks the player past a free-standing,
/// passable 16-step wall from (24, −8) to (24, 8) with <c>DIST 6</c>, <c>IF_NEAR</c>,
/// <c>IF_FAR</c> and an <c>EACH_TICK</c> action, on four paths that separate the candidate metrics:
/// <list type="bullet">
/// <item>A: along y = 0 from x = 0 to 36, through the wall's centre. The segment is within 6 for
/// x 18…30; both vertices stay 8 or more away, so a vertex metric never fires.</item>
/// <item>B: along y = −12 from x = 0 to 36, 4 steps beyond v1. |dx| + |dy| to v1 is within 6
/// for x 22…26, the straight-line distance (here equal to the segment distance) for x
/// 19.5…28.5.</item>
/// <item>C: along y = +12 from x = 36 to 0, the mirror of B beyond v2, so a metric that only
/// reads v1 shows.</item>
/// <item>D: diagonally (+1, +1) from (10, −22) through v1 to (34, 2). |dx| + |dy| enters at
/// (21, −11) and leaves at (27, −5); the straight-line distance enters at (19.8, −12.2) and
/// leaves at (28.2, −3.8); the segment distance leaves at (30, −2).</item>
/// </list>
/// The wall's <c>EACH_TICK</c> action prints <c>MY.DISTANCE</c> every 2 ticks with the player's
/// position and the position it had in the previous cycle (a value computed while drawing the
/// previous frame would match the latter). <c>IF_NEAR</c>/<c>IF_FAR</c> print every run.
/// </summary>
internal static class WallDistanceProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVWDS0.TXT).</summary>
    public const string TraceFile = "ovwds";

    private const string WallSkills =
        "SKILL pw_tnext { VAL 0; }\n" +
        "SKILL pw_d { VAL 0; }\n" +
        "SKILL pw_px { VAL 0; }\n" +
        "SKILL pw_py { VAL 0; }\n";

    /// <summary>
    /// The wall and its actions. <c>W</c> lines: <c>MY.DISTANCE</c>, the player position, the
    /// position at the end of the wall's previous <c>EACH_TICK</c> run. <c>N</c>/<c>F</c> lines:
    /// the hook's run count, <c>MY.DISTANCE</c> and the player position.
    /// </summary>
    private static readonly string Declarations =
        Skills + WallSkills +
        Action("pw_near", "    RULE pe_nn = pe_nn + 1;\n    RULE pe_d = MY.DISTANCE;\n" + L("N", "pe_nn", "pe_d", "PLAYER_X", "PLAYER_Y")) +
        Action("pw_far", "    RULE pe_fn = pe_fn + 1;\n    RULE pe_d = MY.DISTANCE;\n" + L("F", "pe_fn", "pe_d", "PLAYER_X", "PLAYER_Y")) +
        Action(
            "pw_tick",
            "    RULE pw_d = MY.DISTANCE;\n" +
            "    IF (TICKS > pw_tnext) {\n        RULE pw_tnext = TICKS + 2;\n" +
            Indent(L("W", "pw_d", "PLAYER_X", "PLAYER_Y", "pw_px", "pw_py")) + "    }\n" +
            "    RULE pw_px = PLAYER_X;\n    RULE pw_py = PLAYER_Y;\n") +
        "WALL pw_wall { TEXTURE grn_tex; DIST 6; IF_NEAR pw_near; IF_FAR pw_far; EACH_TICK pw_tick; FLAGS PASSABLE; }\n" +
        Sampler();

    private static readonly TraceProbeWall ProbeWall = new("pw_wall", 24, -8, 24, 8);

    /// <summary>Paths A–D (see the class summary), each followed by a short stand.</summary>
    private static string Run(string prologue = "") =>
        prologue + ArmSampler + L("GO", "PLAYER_X", "PLAYER_Y") +
        Walk(0.2, 0, 180, "A") + Wait(16, "SA") +
        Beam(0, -12, "B") + Wait(16, "SB0") + Walk(0.2, 0, 180, "BW") + Wait(16, "SB") +
        Beam(36, 12, "C") + Wait(16, "SC0") + Walk(-0.2, 0, 180, "CW") + Wait(16, "SC") +
        Beam(10, -22, "D") + Wait(16, "SD0") + Walk(0.2, 0.2, 120, "DW") + Wait(32, "RET") +
        ClearSampler;

    private static readonly Dictionary<string, string> MetricPredictions = Predict(
        ("|dx| + |dy| to the nearest vertex", "A: no N/F, W min 8 at x = 24; B: N x ≈ 22, F x ≈ 26; C: N x ≈ 26, F x ≈ 22; D: N (21, −11), F (27, −5)"),
        ("straight-line distance to the nearest vertex", "A: no N/F, W min 8; B: N x ≈ 19.5, F x ≈ 28.5; C mirrored; D: N (19.8, −12.2), F (28.2, −3.8)"),
        ("distance to the wall segment", "A: N x ≈ 18, F x ≈ 30, W 0 at x = 24; B, C as straight-line; D: N (19.8, −12.2), F (30, −2)"),
        ("only v1 counts", "C: no N/F, W ≥ 16"),
        ("passing through the wall fires IF_NEAR", "A: N at x ≈ 24"));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "wall-front",
            "The player, looking along +X at the wall, walks paths A–D past a WALL with DIST 6: what metric does MY.DISTANCE read and the DIST test compare?",
            Declarations,
            Run(),
            MetricPredictions,
            Map: new TraceProbeMap(Walls: [ProbeWall])),
        new TraceProbeCase(
            "wall-behind",
            "The same paths with the player looking along −X, so the wall is behind them while x < 24: is MY.DISTANCE refreshed (and the DIST test run) for a wall that is not drawn?",
            Declarations,
            Run("    SET PLAYER_ANGLE, 3.1416;\n"),
            Predict(
                ("refreshed for every wall", "the same W values and N/F positions as wall-front"),
                ("refreshed only while drawn", "W values frozen while x < 24; N/F missing or late")),
            Map: new TraceProbeMap(Walls: [ProbeWall])),
        new TraceProbeCase(
            "wall-clip",
            "The same paths in a room with CLIP_DIST 10, looking along +X: what does MY.DISTANCE read for a wall outside the player's CLIP_DIST (p.52), and does the DIST test still run?",
            Declarations,
            Run(),
            Predict(
                ("computed regardless of CLIP_DIST", "the same W values and N/F positions as wall-front"),
                ("frozen outside CLIP_DIST", "W values stay at the last value read within 10 steps of a vertex")),
            Map: new TraceProbeMap(RoomBody: "CLIP_DIST 10;", Walls: [ProbeWall])),
    ];

    // ---- wall-pass-clip (OV-20261019-wall-pass-clip) -----------------------------------------

    /// <summary>The PRINTFILE base name of the wall-pass-clip trace (written as OVWPC0.TXT).</summary>
    public const string PassClipTraceFile = "ovwpc";

    /// <summary>
    /// An <c>EACH_TICK</c> action that counts its runs in <c>&lt;prefix&gt;_k</c> and prints
    /// <paramref name="token"/> with the count, <c>MY.DISTANCE</c> and the player position, at most
    /// every 2 ticks. After a gap the first run prints at once, so the lines show when the action
    /// stopped and started running.
    /// </summary>
    internal static string CountingTick(string name, string token, string prefix) =>
        $"SKILL {prefix}_k {{ VAL 0; }}\nSKILL {prefix}_t {{ VAL 0; }}\nSKILL {prefix}_d {{ VAL 0; }}\n" +
        Action(
            name,
            $"    RULE {prefix}_k = {prefix}_k + 1;\n    RULE {prefix}_d = MY.DISTANCE;\n" +
            $"    IF (TICKS > {prefix}_t) {{\n        RULE {prefix}_t = TICKS + 2;\n" +
            Indent(L(token, $"{prefix}_k", $"{prefix}_d", "PLAYER_X", "PLAYER_Y")) + "    }\n");

    /// <summary>The <c>IF_NEAR</c>/<c>IF_FAR</c> actions of the wall-distance probe and a counting <c>EACH_TICK</c>.</summary>
    private static readonly string PassActions =
        Action("pw_near", "    RULE pe_nn = pe_nn + 1;\n    RULE pe_d = MY.DISTANCE;\n" + L("N", "pe_nn", "pe_d", "PLAYER_X", "PLAYER_Y")) +
        Action("pw_far", "    RULE pe_fn = pe_fn + 1;\n    RULE pe_d = MY.DISTANCE;\n" + L("F", "pe_fn", "pe_d", "PLAYER_X", "PLAYER_Y")) +
        CountingTick("pw_tick", "W", "pw");

    private static string PassDeclarations(int dist, bool passable) =>
        Skills + PassActions +
        FormattableString.Invariant($"WALL pw_wall {{ TEXTURE grn_tex; DIST {dist}; IF_NEAR pw_near; IF_FAR pw_far; EACH_TICK pw_tick;") +
        (passable ? " FLAGS PASSABLE;" : "") + " }\n" +
        Sampler();

    /// <summary>A walk preceded by a beam to its start and a short stand.</summary>
    private static string BeamWalk(double x, double y, double vx, double vy, int ticks, string token) =>
        Beam(x, y, token) + Wait(16, token + "S") + Walk(vx, vy, ticks, token + "W") + Wait(16, token + "E");

    /// <summary>
    /// DIST 6, passable. P1/P2: through the centre (8 steps from both vertices) in +X, then back in
    /// −X. P3/P4: along y ≈ 5, where |dx| + |dy| to v2 enters DIST at x ≈ 21, the wall is crossed at
    /// x = 24 within DIST, and DIST is left at x ≈ 27.5; then back. P5: beams across the centre.
    /// </summary>
    private static readonly string PassDist6Run =
        ArmSampler + L("GO", "PLAYER_X", "PLAYER_Y") +
        BeamWalk(20, 0, 0.2, 0, 40, "P1") +
        Walk(-0.2, 0, 40, "P2W") + Wait(16, "P2E") +
        BeamWalk(20, 5, 0.2, 0, 40, "P3") +
        Walk(-0.2, 0, 40, "P4W") + Wait(16, "P4E") +
        Beam(20, 0, "P5") + Wait(16, "P5S") + Beam(28, 0, "P5X") + Wait(16, "P5XS") +
        Beam(20, 0, "P5Y") + Wait(16, "P5YS") +
        "    WAITT 16;\n" + ClearSampler;

    /// <summary>
    /// DIST 0, passable. Q1/Q2: through the centre and back. Q3: creep to x ≈ 23 (about one step
    /// short), stand, then creep across. Q4: beams across. V1–V5: diagonal (+1, +1) walks that cross
    /// the wall's line x = 24 at y = 7, 7.5, 8 (v2 itself), 8.5 and 9.5.
    /// </summary>
    private static readonly string PassDist0Run =
        ArmSampler + L("GO", "PLAYER_X", "PLAYER_Y") +
        BeamWalk(20, 0, 0.2, 0, 40, "Q1") +
        Walk(-0.2, 0, 40, "Q2W") + Wait(16, "Q2E") +
        BeamWalk(20, 0, 0.05, 0, 60, "Q3") + Wait(16, "Q3T") + Walk(0.05, 0, 40, "Q3X") + Wait(16, "Q3E") +
        Beam(20, 0, "Q4") + Wait(16, "Q4S") + Beam(28, 0, "Q4X") + Wait(16, "Q4XS") +
        Beam(20, 0, "Q4Y") + Wait(16, "Q4YS") +
        BeamWalk(20, 3, 0.2, 0.2, 40, "V1") +
        BeamWalk(20, 3.5, 0.2, 0.2, 40, "V2") +
        BeamWalk(20, 4, 0.2, 0.2, 40, "V3") +
        BeamWalk(20, 4.5, 0.2, 0.2, 40, "V4") +
        BeamWalk(20, 5.5, 0.2, 0.2, 40, "V5") +
        ClearSampler;

    /// <summary>
    /// A solid wall: walk into its centre from x = 18 and keep pushing for about 30 ticks, stand
    /// against it, back off; then push into it diagonally near v2 (sliding along it).
    /// </summary>
    private static readonly string SolidRun =
        ArmSampler + L("GO", "PLAYER_X", "PLAYER_Y") +
        BeamWalk(18, 0, 0.2, 0, 60, "S1") + Wait(32, "S1T") +
        Walk(-0.2, 0, 20, "S1B") + Wait(16, "S1E") +
        BeamWalk(18, 0, 0.2, 0.1, 60, "S2") + Wait(16, "S2T") +
        Walk(-0.2, 0, 20, "S2B") + Wait(16, "S2E") +
        ClearSampler;

    /// <summary>
    /// <c>CLIP_DIST 10</c>. Beams to positions at |dx| + |dy| 9, 10, 11 from v2, along the wall's
    /// line and diagonally (where the straight-line distance is 7.2 or 8.1), each followed by a
    /// 32-tick stand.
    /// </summary>
    private static readonly string ClipWallRun =
        ArmSampler + L("GO", "PLAYER_X", "PLAYER_Y") + Wait(32, "S0") +
        Stand(15, 8, "C9") + Stand(14, 8, "C10") + Stand(13, 8, "C11") + Stand(14, 8, "C10B") +
        Stand(18, 4, "D10") + Stand(17, 4, "D11") + Stand(19, 4, "D9") +
        Stand(24, 17, "Y9") + Stand(24, 18, "Y10") + Stand(24, 19, "Y11") +
        ClearSampler;

    private static string Stand(double x, double y, string token) => Beam(x, y, token) + Wait(32, token + "S");

    /// <summary>
    /// <c>CLIP_DIST 20</c>, a thing at (24, −12) and an actor at (24, 12), each with a counting
    /// <c>EACH_TICK</c>. Phase 1 prints the default <c>THING_DIST</c>/<c>ACTOR_DIST</c> and stands at
    /// |dx| + |dy| 19, 20, 21 (and 30) from each; phase 2 sets <c>THING_DIST 0.5</c> and
    /// <c>ACTOR_DIST 0.25</c> and stands at 9, 10, 11 from the thing and 3–6 from the actor. Each
    /// object is at least 24 steps from every stand on the other's line.
    /// </summary>
    private static readonly string ClipObjectsRun =
        ArmSampler + L("DEF", "THING_DIST", "ACTOR_DIST") + Wait(32, "S0") +
        Stand(5, -12, "T19") + Stand(4, -12, "T20") + Stand(3, -12, "T21") +
        Stand(12, -4, "TD20") + Stand(12, -3, "TD21") + Stand(-6, -12, "T30") +
        Stand(5, 12, "A19") + Stand(4, 12, "A20") + Stand(3, 12, "A21") +
        Stand(12, 4, "AD20") + Stand(12, 3, "AD21") +
        "    SET THING_DIST, 0.5;\n    SET ACTOR_DIST, 0.25;\n" + L("SET", "THING_DIST", "ACTOR_DIST") +
        Stand(15, -12, "T9") + Stand(14, -12, "T10") + Stand(13, -12, "T11") +
        Stand(18, -8, "TD10") + Stand(17, -8, "TD11") +
        Stand(21, 12, "A3") + Stand(20, 12, "A4") + Stand(19, 12, "A5") + Stand(18, 12, "A6") +
        Stand(21, 10, "AD5") + Stand(20, 10, "AD6") +
        ClearSampler;

    private static readonly string ClipWallDeclarations =
        Skills + CountingTick("pw_tick", "W", "pw") +
        "WALL pw_wall { TEXTURE grn_tex; EACH_TICK pw_tick; FLAGS PASSABLE; }\n" +
        Sampler();

    private static readonly string ClipObjectDeclarations =
        Skills +
        "TEXTURE pc_stex { SCALE_X 32; SCALE_Y 32; BMAPS red_map; }\n" +
        CountingTick("pc_ttick", "T", "pt") + CountingTick("pc_atick", "A", "pa") +
        "THING pc_thing { TEXTURE pc_stex; EACH_TICK pc_ttick; }\n" +
        "ACTOR pc_actor { TEXTURE pc_stex; EACH_TICK pc_atick; }\n" +
        Sampler();

    public static readonly IReadOnlyList<TraceProbeCase> PassClipCases =
    [
        new TraceProbeCase(
            "pass-dist6",
            "A passable WALL with DIST 6: does walking through it fire IF_NEAR in both directions, again while already within DIST, and for a beam across it?",
            PassDeclarations(6, passable: true),
            PassDist6Run,
            Predict(
                ("a crossing fires IF_NEAR on its own", "N at x ≈ 24 in P1 and P2; P3: N at x ≈ 21 and again at x ≈ 24, F at x ≈ 27.5"),
                ("a crossing only counts from outside DIST", "N at x ≈ 24 in P1 and P2; P3: one N at x ≈ 21, F at x ≈ 27.5"),
                ("a crossing sets the near state", "P1: N at x ≈ 24, F in the next cycle"),
                ("beams count as crossings", "N after P5XS's beam (P5X) and after P5Y"),
                ("only +X crossings (from the left region) count", "no N in P2/P4")),
            Map: new TraceProbeMap(Walls: [ProbeWall])),
        new TraceProbeCase(
            "pass-dist0",
            "A passable WALL with DIST 0 (p.51: touching or passing through fires IF_NEAR): does a crossing, standing one step short, a beam across, or a crossing near or beyond a vertex fire it?",
            PassDeclarations(0, passable: true),
            PassDist0Run,
            Predict(
                ("crossing the segment fires IF_NEAR", "N at x ≈ 24 in Q1, Q2, Q3X, V1, V2; none in V4, V5"),
                ("touching (within PLAYER_WIDTH) fires IF_NEAR", "N before x = 24 in Q1 and during Q3T; V4 (and maybe V5) fire"),
                ("DIST 0 disables the hooks", "no N/F at all"),
                ("beams count as crossings", "N after Q4X and Q4Y"),
                ("IF_FAR follows a DIST 0 IF_NEAR", "F a cycle after each N")),
            Map: new TraceProbeMap(Walls: [ProbeWall])),
        new TraceProbeCase(
            "solid-dist0",
            "A solid (not passable) WALL with DIST 0: does touching it, pushing against it, or sliding along it fire IF_NEAR, once or every cycle?",
            PassDeclarations(0, passable: false),
            SolidRun,
            Predict(
                ("contact fires IF_NEAR once", "one N when the player is stopped at x ≈ 24 − PLAYER_WIDTH in S1W"),
                ("contact fires IF_NEAR every cycle", "N in consecutive pe_n while pushing in S1W and S2W, none during S1T"),
                ("only a crossing fires", "no N")),
            Map: new TraceProbeMap(Walls: [ProbeWall])),
        new TraceProbeCase(
            "solid-dist6",
            "The solid WALL with DIST 6: does touching its centre (8 steps from both vertices) fire IF_NEAR?",
            PassDeclarations(6, passable: false),
            SolidRun,
            Predict(
                ("contact fires IF_NEAR whatever DIST", "N in S1W with pe_d ≈ 9"),
                ("only DIST fires", "no N in S1W; N in S2W when |dx| + |dy| to v2 drops to 6")),
            Map: new TraceProbeMap(Walls: [ProbeWall])),
        new TraceProbeCase(
            "clip-wall",
            "A wall's EACH_TICK with CLIP_DIST 10: is the border |dx| + |dy| < 10 or ≤ 10, and is it measured by axes or in a straight line?",
            ClipWallDeclarations,
            ClipWallRun,
            Predict(
                ("< CLIP_DIST by axes", "W during C9, D9, Y9 only"),
                ("≤ CLIP_DIST by axes", "W during C9, C10, C10B, D10, D9, Y9, Y10"),
                ("straight-line distance", "W also during D11 (8.1)")),
            Map: new TraceProbeMap(RoomBody: "CLIP_DIST 10;", Walls: [ProbeWall])),
        new TraceProbeCase(
            "clip-objects",
            "A thing's and an actor's EACH_TICK with CLIP_DIST 20: are they gated by CLIP_DIST, by THING_DIST/ACTOR_DIST × CLIP_DIST (p.105), or not at all?",
            ClipObjectDeclarations,
            ClipObjectsRun,
            Predict(
                ("not gated", "T and A lines during every stand"),
                ("gated by CLIP_DIST × THING_DIST/ACTOR_DIST, < by axes", "phase 1: T in T19, A in A19 only; phase 2: T in T9, A in A3, A4"),
                ("ACTOR_DIST scales THING_DIST's border", "phase 2: A only in A3 (border 2.5)"),
                ("gated by CLIP_DIST only", "phase 2 the same as phase 1: T and A in every stand within 20")),
            Map: new TraceProbeMap(RoomBody: "CLIP_DIST 20;", Placements: ["THING pc_thing 24 -12 0 0", "ACTOR pc_actor 24 12 180 0"])),
    ];

    /// <summary>The wall-pass-clip probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet PassClipSet = new(
        "wall-pass-clip",
        PassClipTraceFile,
        "ovwpp",
        "After the skill dump, `IF_START` selects the trace file, writes `BEGIN` and runs the variant's case.\n" +
        "Each trace line is a token followed by `TICKS` and `pe_n` (the global EACH_TICK sampler's run count)\n" +
        "when it was printed, then the case's extra skills. `W`/`T`/`A` lines come from the wall's, thing's and\n" +
        "actor's EACH_TICK at most every 2 ticks (run count, MY.DISTANCE, player position); `N`/`F` are\n" +
        "IF_NEAR/IF_FAR runs (count, MY.DISTANCE, player position). The wall runs from (24, −8) to (24, 8).\n" +
        "Compare the trace with `case.json` → `predictions`, and check `OVCON.TXT` for compile errors.",
        PassClipCases);

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "wall-distance",
        TraceFile,
        "ovwdp",
        "After the skill dump, `IF_START` selects the trace file, writes `BEGIN` and runs the variant's case.\n" +
        "Each trace line is a token followed by `TICKS` and `pe_n` (the global EACH_TICK sampler's run count)\n" +
        "when it was printed, then the case's extra skills. `W` lines come from the wall's EACH_TICK every\n" +
        "2 ticks (MY.DISTANCE, player position, player position in the previous cycle); `N`/`F` are\n" +
        "IF_NEAR/IF_FAR runs (count, MY.DISTANCE, player position). The wall runs from (24, −8) to (24, 8).\n" +
        "Compare the trace with `case.json` → `predictions`, and check `OVCON.TXT` for compile errors.",
        All);
}
