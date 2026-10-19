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
