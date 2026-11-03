// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>startup</c> probe: the frames of the start-up and the start-up push-out.
/// The oracle-diff replay has to guess the original's frames before a trace's first sample, and
/// several view-activity carriers differ only by an actor's skip phase, which counts those frames.
/// OV-20261029-scheduler-order printed one launch's start-up (cycle 1 at <c>TICKS</c> 0, a 10/32
/// step, then the 60 Hz 8/9 pattern); this probe repeats it over several launches. Each case
/// arms a global <c>EACH_TICK</c> sampler at the top of <c>IF_START</c>, before the settling
/// <c>WAITT 16</c>, that prints <c>TICKS</c>, a cycle counter, <c>TIME_CORR</c> and the player's
/// position in 1/1024 units every cycle. The <c>push-</c> and rule cases place objects around
/// the player start (0, 0), as OV-20261029-overlap did, to see in which cycles and by what steps
/// the player is pushed out, and which object counts.
/// </summary>
internal static class StartupProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVSUP0.TXT).</summary>
    public const string TraceFile = "ovsup";

    private const string Skills =
        "SKILL su_n { VAL 0; }\nSKILL su_x { VAL 0; }\nSKILL su_y { VAL 0; }\n" +
        "TEXTURE su_t4 { SCALE_X 4; SCALE_Y 4; BMAPS red_map; }\n" +
        "TEXTURE su_t8 { SCALE_X 8; SCALE_Y 8; BMAPS red_map; }\n" +
        "TEXTURE su_t16 { SCALE_X 16; SCALE_Y 16; BMAPS red_map; }\n" +
        "TEXTURE su_t32 { SCALE_X 32; SCALE_Y 32; BMAPS red_map; }\n" +
        "ACTOR su_a2 { TEXTURE su_t32; SPEED 0; }\n" +
        "ACTOR su_b2 { TEXTURE su_t32; SPEED 0; }\n" +
        "ACTOR su_a4 { TEXTURE su_t16; SPEED 0; }\n" +
        "ACTOR su_a8 { TEXTURE su_t8; SPEED 0; }\n" +
        "ACTOR su_a16 { TEXTURE su_t4; SPEED 0; }\n" +
        "THING su_h2 { TEXTURE su_t32; }\n";

    /// <summary>One sample: token, <c>TICKS</c>, the cycle count, <c>TIME_CORR</c>, and <c>PLAYER_X</c>/<c>PLAYER_Y</c> × 1024.</summary>
    private static string L(string token) =>
        "    RULE su_x = PLAYER_X * 1024;\n    RULE su_y = PLAYER_Y * 1024;\n" + V(token, "TICKS", "su_n", "TIME_CORR", "su_x", "su_y");

    private static readonly string Sampler =
        Action("su_tick", "    RULE su_n = su_n + 1;\n    IF (su_n < 170) {\n" + Indent(L("Y")) + "    }\n");

    private static readonly string Prelude =
        "    PRINTFILE \"" + TraceFile + "\", 0;\n" + L("S0") + "    SET EACH_TICK.1, su_tick;\n" + L("S1");

    private static readonly string Run = L("GO") + "    WAIT 100;\n" + L("RET") + "    SET EACH_TICK.1, NULL;\n";

    private static TraceProbeCase Case(string label, string question, string[] placements) => new(
        label,
        question,
        Skills + Sampler,
        Run,
        Predict(
            ("the start-up runs 60 Hz frames after a first step", "Y 0.000 1 1.000, then 8/32 and 9/32 steps; W16 ends near TICKS 13.4"),
            ("the push is one jump in one cycle", "su_x changes once, in the first cycles"),
            ("the push is a movement over several cycles", "su_x changes in several cycles"),
            ("only the last placed object counts", "no push when the last placed object does not overlap"),
            ("the first overlapping object from the last counts", "a push whenever any object overlaps off its centre")),
        Map: placements.Length > 0 ? new TraceProbeMap(Placements: placements) : null,
        Prelude: Prelude);

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Case("clock-1", "An empty room: TICKS and TIME_CORR of every cycle from IF_START (launch 1 of 4).", []),
        Case("clock-2", "The same carrier as clock-1, launch 2.", []),
        Case("clock-3", "The same carrier as clock-1, launch 3.", []),
        Case("clock-4", "The same carrier as clock-1, launch 4.", []),
        Case("push-d21", "A 2-wide actor at (2.1, 0): in which cycles does the player move, and by how much?", ["ACTOR su_a2 2.1 0 0 0"]),
        Case("push-d10", "A 2-wide actor at (1.0, 0).", ["ACTOR su_a2 1.0 0 0 0"]),
        Case("push-d01", "A 2-wide actor at (0.1, 0) (ended 2.239 out, not 2.22).", ["ACTOR su_a2 0.1 0 0 0"]),
        Case("push-d005", "A 2-wide actor at (0.05, 0) (y ended at 0.024, not 0.004).", ["ACTOR su_a2 0.05 0 0 0"]),
        Case("push-w4-14", "A 4-wide actor at (0.4, -0.1), 14 degrees off the axis.", ["ACTOR su_a4 0.4 -0.1 0 0"]),
        Case("push-negx", "A 2-wide actor at (-0.3, 0).", ["ACTOR su_a2 -0.3 0 0 0"]),
        Case("last-far", "A 2-wide actor at (0.3, 0), then a second one placed last far away at (20, 0): is the player pushed out of the first?",
            ["ACTOR su_a2 0.3 0 0 0", "ACTOR su_b2 20 0 0 0"]),
        Case("last-near", "A far 2-wide actor at (20, 0), then one placed last at (0.3, 0).",
            ["ACTOR su_b2 20 0 0 0", "ACTOR su_a2 0.3 0 0 0"]),
        Case("thing-last", "A far 2-wide actor at (20, 0), then a 2-wide thing at (0.3, 0) placed last.",
            ["ACTOR su_b2 20 0 0 0", "THING su_h2 0.3 0 0 0"]),
        Case("actor-after-thing", "A 2-wide thing at (0.3, 0), then a far 2-wide actor at (20, 0) placed last.",
            ["THING su_h2 0.3 0 0 0", "ACTOR su_b2 20 0 0 0"]),
        Case("wide16-3", "A 16-wide actor (contact 9.2) at (3, 0).", ["ACTOR su_a16 3 0 0 0"]),
        Case("wide16-bb", "A 16-wide actor at (8, -2), 8.25 away (below-beam's layout, where nothing moved).", ["ACTOR su_a16 8 -2 0 0"]),
        Case("wide8-49", "An 8-wide actor (contact 5.2) at (4.9, 0).", ["ACTOR su_a8 4.9 0 0 0"]),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "startup",
        TraceFile,
        "ovsus",
        "`S0`/`S1` are printed at the top of `IF_START` around arming the sampler; `Y` lines (TICKS, cycle,\n" +
        "TIME_CORR, PLAYER_X×1024, PLAYER_Y×1024) every cycle from then on, through the settling `WAITT 16`;\n" +
        "`GO` after it and `RET` 100 cycles later. The four `clock-` carriers are identical (several launches).",
        All);
}
