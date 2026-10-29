// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>overlap</c> probe: what the original does with a player who starts inside an
/// actor. OV-20261019-actor-contact found that a beam into an object is resolved at the next
/// movement, pushing the player straight out to the contact distance (half the width plus
/// <c>PLAYER_WIDTH</c>, 2.2 here). The view-activity carriers, though, left a player on top of
/// eight still actors at (0, 0) in one layout and moved it about 1.21 in two others. Each case puts
/// one 2-wide actor (still, or on a way, <c>SPEED 0</c>) near the player start (0, 0) and prints the
/// player's position in every cycle for a while.
/// </summary>
internal static class OverlapProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVOVL0.TXT).</summary>
    public const string TraceFile = "ovovl";

    private const string Skills =
        "SKILL ov_n { VAL 0; }\nTEXTURE ov_tex { SCALE_X 32; SCALE_Y 32; BMAPS red_map; }\n";

    private static readonly string Sampler = Action(
        "ov_tick",
        "    RULE ov_n = ov_n + 1;\n    IF (ov_n < 40) {\n" + Indent(V("P", "TICKS", "ov_n", "PLAYER_X", "PLAYER_Y")) + "    }\n");

    private static TraceProbeCase Case(string label, string question, string actor, string[] placements, string run = "") => new(
        label,
        question,
        Skills + Sampler + actor,
        "    SET EACH_TICK.1, ov_tick;\n" + run + "    WAITT 16;\n    SET EACH_TICK.1, NULL;\n",
        Predict(
            ("a still overlap is left alone", "PLAYER_X 0 throughout"),
            ("the player is pushed out to 2.2", "PLAYER_X ±2.2")),
        Map: new TraceProbeMap(Placements: placements),
        Prelude: V("S0", "PLAYER_X", "PLAYER_Y"));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Case("still-center", "A still actor exactly on the player.", "ACTOR ov_a { TEXTURE ov_tex; SPEED 0; }\n", ["ACTOR ov_a 0 0 0 0"]),
        Case("still-off", "A still actor at (0.4, -0.1).", "ACTOR ov_a { TEXTURE ov_tex; SPEED 0; }\n", ["ACTOR ov_a 0.4 -0.1 0 0"]),
        Case("way-center", "An actor on a way (0, 0) → (0, 0.2) with SPEED 0, on the player.", "WAY ov_w;\nACTOR ov_a { TEXTURE ov_tex; TARGET ov_w; SPEED 0; }\n",
            ["ACTOR ov_a 0 0 0 0", "WAY ov_w 0 0 0 0.2"]),
        Case("way-off", "An actor on a way (0.4, -0.1) → (0.4, 0.1) with SPEED 0.", "WAY ov_w;\nACTOR ov_a { TEXTURE ov_tex; TARGET ov_w; SPEED 0; }\n",
            ["ACTOR ov_a 0.4 -0.1 0 0", "WAY ov_w 0.4 -0.1 0.4 0.1"]),
        Case("still-walk", "A still actor at (0.4, -0.1); the player walks with PLAYER_VX 0.01 from the probe start: pushed out then?",
            "ACTOR ov_a { TEXTURE ov_tex; SPEED 0; }\nSKILL ov_vx { VAL 0.01; }\n", ["ACTOR ov_a 0.4 -0.1 0 0"],
            "    SET PLAYER_VX, ov_vx;\n"),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "overlap",
        TraceFile,
        "ovovs",
        "`S0` is printed as the probe starts; `P` lines (TICKS, cycle, PLAYER_X, PLAYER_Y) every cycle.",
        All);
}
