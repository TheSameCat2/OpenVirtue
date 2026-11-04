// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>move-mode</c> probe: what <c>MOVE_MODE</c> 0.5, 0 and −0.5 stop (manual
/// p.104: actors' movement and objects' <c>EACH_TICK</c>s at 0.5, the player's movement and the
/// object and region events too at 0, every action but the keyboard events at −0.5). The retail
/// title's <c>start_game</c> sets <c>MOVE_MODE</c> 0 for its menus. A global <c>EACH_TICK</c>
/// sampler prints every cycle: <c>TICKS</c>, its count, <c>MOVE_MODE</c>, the walking player's X, a
/// way actor's X, and the counts of the thing's, the actor's and the room's <c>EACH_TICK</c>s, the
/// second global slot, a <c>WAIT 1</c> loop and <c>EACH_SEC</c>. A Space pressed by DOSBox-X about
/// four seconds into the probe fires <c>IF_ANYKEY</c>, which prints <c>K</c> and sets
/// <c>MOVE_MODE</c> back to 1, in case the probe action itself is frozen.
/// </summary>
internal static class MoveModeProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVMVM0.TXT).</summary>
    public const string TraceFile = "ovmvm";

    private const string Skills =
        "SKILL mm_n { VAL 0; }\nSKILL mm_go { VAL 0; }\nSKILL mm_v { VAL 0; }\nSKILL mm_t { VAL 0; }\nSKILL mm_a { VAL 0; }\n" +
        "SKILL mm_r { VAL 0; }\nSKILL mm_g2 { VAL 0; }\nSKILL mm_w { VAL 0; }\nSKILL mm_s { VAL 0; }\nSKILL mm_k { VAL 0; }\n" +
        "SYNONYM mm_as { TYPE ACTOR; }\nWAY mm_way;\n" +
        "TEXTURE mm_tex { SCALE_X 16; SCALE_Y 16; BMAPS red_map; }\n";

    private static readonly string[] Columns = ["mm_n", "MOVE_MODE", "PLAYER_X", "mm_as.X", "mm_t", "mm_a", "mm_r", "mm_g2", "mm_w", "mm_s"];

    private static readonly string Declarations =
        Skills +
        Action("mm_tick",
            "    RULE mm_n = mm_n + 1;\n" +
            "    IF (mm_go > 0) {\n        RULE mm_v = 0.5 / TIME_CORR;\n        SET PLAYER_VX, mm_v;\n    }\n" +
            "    IF (mm_n < 320) {\n" + Indent(V("C", ["TICKS", .. Columns])) + "    }\n") +
        Action("mm_tt", "    RULE mm_t = mm_t + 1;\n") +
        Action("mm_at", "    RULE mm_a = mm_a + 1;\n") +
        Action("mm_rt", "    RULE mm_r = mm_r + 1;\n") +
        Action("mm_g2t", "    RULE mm_g2 = mm_g2 + 1;\n") +
        Action("mm_sec", "    RULE mm_s = mm_s + 1;\n") +
        Action("mm_loop", "    WHILE (mm_w < 10000)\n    {\n        RULE mm_w = mm_w + 1;\n        WAIT 1;\n    }\n") +
        Action("mm_key", "    RULE mm_k = mm_k + 1;\n" + V("K", ["TICKS", .. Columns]) + "    SET MOVE_MODE, 1;\n") +
        "THING mm_th { TEXTURE mm_tex; EACH_TICK mm_tt; }\n" +
        "ACTOR mm_wa { TEXTURE mm_tex; TARGET mm_way; SPEED 0.2; EACH_TICK mm_at; }\n";

    private static string Line(string token) => V(token, ["TICKS", .. Columns]);

    private static string Run(string mode) =>
        "    SET mm_as, mm_wa;\n    SET EACH_TICK.1, mm_tick;\n    SET EACH_TICK.2, mm_g2t;\n    SET EACH_SEC.1, mm_sec;\n" +
        "    CALL mm_loop;\n    SET IF_ANYKEY, mm_key;\n    RULE mm_go = 1;\n    WAITT 16;\n" + Line("A") +
        $"    SET MOVE_MODE, {mode};\n" + Line("B") + "    WAITT 16;\n" + Line("D") +
        "    SET MOVE_MODE, 1;\n    WAITT 16;\n" + Line("E") +
        "    RULE mm_go = 0;\n    SET PLAYER_VX, 0;\n    SET IF_ANYKEY, NULL;\n    SET EACH_TICK.1, NULL;\n    SET EACH_TICK.2, NULL;\n    SET EACH_SEC.1, NULL;\n" + P("RET");

    private static TraceProbeCase Case(string label, string mode, string question, params (string, string)[] predictions) => new(
        label,
        question,
        Declarations,
        Run(mode),
        Predict(predictions),
        "-w 10 space",
        new TraceProbeMap(RoomBody: "EACH_TICK mm_rt;", Placements: ["THING mm_th 10 -8 0 0", "ACTOR mm_wa 30 12 0 0", "WAY mm_way 30 12 46 12"]),
        HookToken: "K");

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Case(
            "control",
            "1",
            "MOVE_MODE left at 1: the walking player, the way actor, every counter and the key, as a baseline.",
            ("everything runs", "PLAYER_X and mm_as.X rise every cycle, every counter counts, K once")),
        Case(
            "half",
            "0.5",
            "MOVE_MODE 0.5 for 16 ticks: which of the actor's walk, the thing's, actor's and room's EACH_TICKs, the player's walk, the global slots, the WAIT loop and EACH_SEC stop (manual: actors and object EACH_TICKs)?",
            ("actors and object EACH_TICKs stop, the rest runs", "between B and D: mm_as.X, mm_t and mm_a hold; PLAYER_X, mm_r, mm_g2, mm_w, mm_s go on"),
            ("the room's EACH_TICK stops too", "mm_r holds"),
            ("the probe action goes on", "D 16 ticks after B")),
        Case(
            "zero",
            "0",
            "MOVE_MODE 0 for 16 ticks (the retail menus): does the player's walk stop too, the room's EACH_TICK, the global slots, the WAIT loop, EACH_SEC, the probe action's own WAITT; does the key hook still fire and restore it?",
            ("the player stops too, objects and region events stop, global slots and waits run", "between B and D: PLAYER_X holds, mm_r holds, mm_g2, mm_w, mm_s go on, D 16 ticks after B"),
            ("waits freeze", "no D before K; K at about 4 s restores and D follows"),
            ("the sampler stops", "no C lines between B and K")),
        Case(
            "minus",
            "-0.5",
            "MOVE_MODE −0.5 (every action but the keyboard events): do the global EACH_TICK slots, the WAIT loop, EACH_SEC and the probe action stop; does the key still fire and restore?",
            ("every action stops, the key fires", "no C lines after B, K at about 4 s, then C lines and D again"),
            ("the global slots run, waits freeze", "C lines go on with the counters frozen; no D before K")),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "move-mode",
        TraceFile,
        "ovmvs",
        "Every `C` line is a cycle: `TICKS`, the sampler's count, `MOVE_MODE`, the walking player's X, the way\n" +
        "actor's X, the thing's, actor's and room's `EACH_TICK` counts, the second global slot's, a `WAIT 1`\n" +
        "loop's and `EACH_SEC`'s. `A` is printed before `SET MOVE_MODE`, `B` after, `D` after the 16-tick\n" +
        "stand, `E` after the mode is back at 1; `K` is the Space (DOSBox-X `AUTOTYPE`), which also sets\n" +
        "`MOVE_MODE` 1. Compare with `case.json` → `predictions`, check `OVCON.TXT` for compile errors.",
        All);
}
