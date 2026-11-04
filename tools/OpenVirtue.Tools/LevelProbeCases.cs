// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>level</c> probe: what <c>LEVEL</c> does (manual p.88: loads the new world,
/// runs its <c>IF_START</c>, replaces every region, object and skill but the <c>GLOBAL</c> ones;
/// p.101: <c>GLOBAL</c> skills are reloaded with <c>LOAD_INFO</c> after a level change). The
/// retail title changes level with <c>LEVEL "apathy.wdl", "apathy.wrs"</c> after a
/// <c>SAVE_INFO</c>. The probe reloads its own world (<c>LEVEL "TITLE.WDL"</c>): does the action go
/// on after the instruction, does <c>IF_START</c> run again (a second <c>START</c> in
/// <c>OVSTA0.TXT</c>, a second <c>BEGIN</c>), do <c>TICKS</c> and <c>TOTAL_FRAMES</c> start over, does
/// a <c>GLOBAL</c> skill keep its value where a plain one starts over, does a waiting action
/// survive, and where does the player stand?
/// </summary>
internal static class LevelProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVLVL0.TXT).</summary>
    public const string TraceFile = "ovlvl";

    private static readonly string[] Columns = ["TICKS", "TOTAL_FRAMES", "lv_g", "lv_p", "lv_n", "lv_gn", "PLAYER_X", "PLAYER_ANGLE"];

    private static readonly string Declarations =
        "SKILL lv_g { TYPE GLOBAL; VAL 0; }\nSKILL lv_gn { TYPE GLOBAL; VAL 0; }\nSKILL lv_p { VAL 0; }\nSKILL lv_n { VAL 0; }\n" +
        Action("lv_tick", "    RULE lv_n = lv_n + 1;\n    RULE lv_gn = lv_gn + 1;\n    IF (lv_n < 400) {\n" + Indent(V("C", Columns)) + "    }\n") +
        Action("lv_wait", "    WAITT 64;\n" + V("W", Columns));

    private static readonly string Run =
        "    RULE lv_g = lv_g + 1;\n    RULE lv_p = lv_p + 1;\n    SET EACH_TICK.1, lv_tick;\n" + V("S", Columns) +
        "    IF (lv_g == 1) {\n" +
        Indent("    SET PLAYER_X, 5;\n    SET PLAYER_ANGLE, 1;\n    CALL lv_wait;\n    WAITT 8;\n" + V("L", Columns) +
               "    LEVEL \"TITLE.WDL\";\n" + V("AFTER", Columns) + "    WAITT 8;\n" + V("AFTER8", Columns)) +
        "    }\n" +
        "    IF (lv_g == 2) {\n" + Indent("    WAITT 40;\n" + V("E", Columns)) + "    }\n" +
        "    IF (lv_g > 2) {\n" + Indent(V("X", Columns)) + "    }\n" +
        "    SET EACH_TICK.1, NULL;\n" + P("RET");

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "reload",
            "LEVEL \"TITLE.WDL\" from the probe action, 8 ticks after a beam to x 5 and a CALLed WAITT 64: does the action go on (AFTER, AFTER8), does IF_START run again (a second BEGIN and S), do TICKS and TOTAL_FRAMES start over, does the GLOBAL lv_g keep 1 (so the second pass reads 2 and stops) where the plain lv_p starts over, does the waiting action print W after the change, and where does the player stand (PLAYER_X 5 or the start's 0)?",
            Declarations,
            Run,
            Predict(
                ("the action ends at LEVEL, IF_START runs again with TICKS at 0, GLOBAL skills kept, plain ones and waiting actions gone", "no AFTER; a second S with lv_g 2, lv_p 1, TICKS 0, PLAYER_X 0; no W; E, RET"),
                ("the action goes on until the cycle ends", "AFTER printed, AFTER8 not"),
                ("GLOBAL skills start over too (manual p.101: reload them with LOAD_INFO)", "every pass reads lv_g 1 and the level reloads until the timeout"),
                ("TICKS go on", "the second S at TICKS above the first pass's L")),
            Map: new TraceProbeMap()),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "level",
        TraceFile,
        "ovlvs",
        "Every line is a token, `TICKS`, `TOTAL_FRAMES`, the GLOBAL lv_g and the plain lv_p (both counted once per pass\n" +
        "of the probe action), the sampler's plain and GLOBAL counts, `PLAYER_X` and `PLAYER_ANGLE`. The trace file\n" +
        "is appended to across the level change (PRINT_VALUE appends, manual p.86); `OVSTA0.TXT` gets a `START` per\n" +
        "`IF_START`. Compare with `case.json` → `predictions`, check `OVCON.TXT` for compile errors.",
        All);
}
