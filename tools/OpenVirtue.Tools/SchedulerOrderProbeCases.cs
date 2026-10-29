// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>scheduler-order</c> probe. The manual says global, region, wall and object
/// <c>EACH_TICK</c> actions run "after every frame cycle" (p.53, p.59, p.66, p.96) and
/// <c>EACH_CYCLE</c> at the end of a texture's animation cycle, but never in which order, nor where
/// resumed <c>WAIT</c>s and <c>EACH_SEC</c> fit, nor when the first cycle after <c>IF_START</c> comes.
/// OV-20260701-scheduler-if-start-cycle only showed that an <c>EACH_TICK</c> armed in <c>IF_START</c>
/// runs eventually, and the oracle-diff harness found <c>WAITT 16</c> armed in <c>IF_START</c> ending
/// at <c>TICKS</c> 13.4 in fast runs, before 16 ticks had passed.
/// <para>
/// Every line is a token, <c>TICKS</c> and <c>so_n</c>, the count of the global slot-1 action's runs.
/// That action counts first thing, so lines printed before it in a cycle show the previous count:
/// within one cycle the token order is the dispatch order. Object actions print only while
/// <c>so_on</c> is set, for about 70 cycles.
/// </para>
/// </summary>
internal static class SchedulerOrderProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVSDO0.TXT).</summary>
    public const string TraceFile = "ovsdo";

    private const string Skills =
        "SKILL so_n { VAL 0; }\nSKILL so_on { VAL 0; }\nSKILL so_k { VAL 0; }\nSKILL so_c { VAL 0; }\n" +
        "SYNONYM so_st { TYPE THING; }\n";

    /// <summary>A trace line: the token, <c>TICKS</c> and <c>so_n</c>, then any extra skills.</summary>
    private static string L(string token, params string[] extra) => V(token, ["TICKS", "so_n", .. extra]);

    /// <summary>A trace line printed only while <c>so_on</c> is set.</summary>
    private static string W(string token) => "    IF (so_on > 0) {\n" + Indent(L(token)) + "    }\n";

    /// <summary>The slot-1 counter: counts, then prints <c>G1</c> while the window is open.</summary>
    private static readonly string Counter = Action("so_g1", "    RULE so_n = so_n + 1;\n" + W("G1"));

    /// <summary>The window: open it, print <c>M</c> from the waiting main action every cycle for 70 cycles, close it.</summary>
    private static readonly string Window =
        "    RULE so_on = 1;\n    RULE so_k = 0;\n" +
        "    WHILE (so_k < 70) {\n" + Indent(L("M")) + "        RULE so_k = so_k + 1;\n        WAIT 1;\n    }\n" +
        "    RULE so_on = 0;\n";

    private const string ClearSlots =
        "    SET EACH_TICK.1, NULL;\n    SET EACH_TICK.2, NULL;\n    SET EACH_SEC.1, NULL;\n";

    /// <summary>
    /// The actors of every object kind: two walls, two things and two actors with EACH_TICK
    /// (declared a before b, placed b before a in <c>order</c> and a before b in <c>order-wmp</c>),
    /// a thing and a wall whose 2-phase texture (1 tick a phase) runs <c>EACH_CYCLE</c>, the room's
    /// <c>EACH_TICK</c>, global slot 2 and <c>EACH_SEC.1</c>.
    /// </summary>
    private static readonly string OrderDeclarations =
        Skills + Counter +
        Action("so_g2", W("G2")) +
        Action("so_sec", W("SEC")) +
        Action("so_reg", W("R")) +
        Action("so_wat", W("WA")) + Action("so_wbt", W("WB")) +
        Action("so_tat", W("TA")) + Action("so_tbt", W("TB")) +
        Action("so_aat", W("AA")) + Action("so_abt", W("AB")) +
        Action("so_tcy", W("TC")) + Action("so_wcy", W("WC")) +
        "TEXTURE so_anim { SCALE_X 32; SCALE_Y 32; CYCLES 2; BMAPS red_map, grn_map; DELAY 1, 1; }\n" +
        "TEXTURE so_still { SCALE_X 32; SCALE_Y 32; BMAPS yel_map; }\n" +
        "WALL so_wa { TEXTURE red_tex; EACH_TICK so_wat; }\n" +
        "WALL so_wb { TEXTURE red_tex; EACH_TICK so_wbt; }\n" +
        "WALL so_wc { TEXTURE so_anim; EACH_CYCLE so_wcy; }\n" +
        "THING so_ta { TEXTURE so_still; EACH_TICK so_tat; }\n" +
        "THING so_tb { TEXTURE so_still; EACH_TICK so_tbt; }\n" +
        "THING so_tc { TEXTURE so_anim; EACH_CYCLE so_tcy; }\n" +
        "ACTOR so_aa { TEXTURE so_still; EACH_TICK so_aat; }\n" +
        "ACTOR so_ab { TEXTURE so_still; EACH_TICK so_abt; }\n";

    private static readonly string OrderRun =
        "    SET EACH_TICK.2, so_g2;\n    SET EACH_TICK.1, so_g1;\n    SET EACH_SEC.1, so_sec;\n" +
        L("GO") + Window + ClearSlots + P("RET");

    private static TraceProbeMap OrderMap(bool declarationOrder) => new(
        RoomBody: "EACH_TICK so_reg;",
        Placements: declarationOrder
            ? ["THING so_ta 20 -6 0 0", "THING so_tb 20 6 0 0", "THING so_tc 24 0 0 0", "ACTOR so_aa 16 -6 0 0", "ACTOR so_ab 16 6 0 0"]
            : ["THING so_tb 20 6 0 0", "THING so_ta 20 -6 0 0", "THING so_tc 24 0 0 0", "ACTOR so_ab 16 6 0 0", "ACTOR so_aa 16 -6 0 0"],
        Walls: declarationOrder
            ? [new TraceProbeWall("so_wa", 30, -8, 30, -4), new TraceProbeWall("so_wb", 30, 4, 30, 8), new TraceProbeWall("so_wc", 32, -2, 32, 2)]
            : [new TraceProbeWall("so_wb", 30, 4, 30, 8), new TraceProbeWall("so_wa", 30, -8, 30, -4), new TraceProbeWall("so_wc", 32, -2, 32, 2)]);

    private static readonly IReadOnlyDictionary<string, string> OrderPredictions = Predict(
        ("globals first", "per cycle: G1 (so_n new), G2, then region/wall/thing/actor lines, then M"),
        ("objects first", "R/W*/T*/A* lines with the previous so_n before G1"),
        ("resumed WAITs before the slots", "M with the previous so_n at the start of each cycle"),
        ("declaration order", "TA before TB, AA before AB, WA before WB in both cases"),
        ("WMP order", "TB before TA in `order`, TA before TB in `order-wmp` (likewise walls and actors)"),
        ("EACH_CYCLE in the animation step", "TC/WC every 2 ticks, at a fixed place relative to the EACH_TICK lines"));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "startup",
            "What are TICKS and TIME_CORR in IF_START and in each of the first cycles? Does an EACH_TICK armed in IF_START run in the IF_START frame or the next? After how many cycles and ticks does a WAITT 16 armed in IF_START end, and how long do WAIT 1 and WAITT 1 armed later take?",
            Skills +
            Action("so_y", "    RULE so_n = so_n + 1;\n    IF (so_n < 400) {\n" + Indent(L("Y", "TIME_CORR")) + "    }\n"),
            L("W16", "TIME_CORR") + "    WAIT 1;\n" + L("W1", "TIME_CORR") + "    WAIT 1;\n" + L("W1B", "TIME_CORR") +
            "    WAITT 1;\n" + L("WT1", "TIME_CORR") + "    WAITT 4;\n" + L("WT4", "TIME_CORR") +
            "    SET EACH_TICK.1, NULL;\n" + P("RET"),
            Predict(
                ("TICKS is 0 in IF_START", "S0 0.000 ..."),
                ("the slot runs in the IF_START frame", "a Y line with the S0 TICKS"),
                ("WAITT counts TIME_CORR from the arming cycle", "W16 in the first cycle whose summed TIME_CORR since S0 reaches 16"),
                ("the first cycles carry a large TIME_CORR", "a Y line with TIME_CORR well above the later ones"),
                ("WAIT 1 resumes in the next cycle", "W1 one so_n after W16")),
            Prelude: "    PRINTFILE \"" + TraceFile + "\", 0;\n" + L("S0", "TIME_CORR") + "    SET EACH_TICK.1, so_y;\n" + L("S1", "TIME_CORR")),
        new TraceProbeCase(
            "order",
            "In one cycle, in what order do the global EACH_TICK slots, the region's, walls', things' and actors' EACH_TICK, EACH_CYCLE, EACH_SEC and a resumed WAIT run? Objects are declared a before b and placed b before a.",
            OrderDeclarations,
            OrderRun,
            OrderPredictions,
            Map: OrderMap(declarationOrder: false)),
        new TraceProbeCase(
            "order-wmp",
            "As `order`, with the objects placed in declaration order (a before b), to tell WMP order from reverse declaration order.",
            OrderDeclarations,
            OrderRun,
            OrderPredictions,
            Map: OrderMap(declarationOrder: true)),
        new TraceProbeCase(
            "set-midcycle",
            "Does a SET EACH_TICK made by an action take effect in the same cycle? Global slot 1 arms slot 2 and the thing's EACH_TICK when so_n reaches 5, and clears slot 2 at 9.",
            Skills +
            Action("so_g1", "    RULE so_n = so_n + 1;\n" + W("G1") +
                "    IF (so_n == 5) {\n        SET EACH_TICK.2, so_g2;\n        SET so_st.EACH_TICK, so_tat;\n" + Indent(L("ARM")) + "    }\n" +
                "    IF (so_n == 9) {\n        SET EACH_TICK.2, NULL;\n" + Indent(L("DIS")) + "    }\n") +
            Action("so_g2", W("G2")) + Action("so_tat", W("TA")) +
            "TEXTURE so_still { SCALE_X 32; SCALE_Y 32; BMAPS yel_map; }\n" +
            "THING so_ta { TEXTURE so_still; }\n",
            "    SET so_st, so_ta;\n    RULE so_on = 1;\n    SET EACH_TICK.1, so_g1;\n" + L("GO") +
            "    WAITT 6;\n    RULE so_on = 0;\n" + ClearSlots + "    SET so_st.EACH_TICK, NULL;\n" + P("RET"),
            Predict(
                ("same cycle", "ARM, then G2 and TA with so_n 5 in the same cycle; DIS, then no G2 with so_n 9"),
                ("next cycle", "first G2/TA with so_n 6; a last G2 with so_n 9")),
            Map: new TraceProbeMap(Placements: ["THING so_ta 20 0 0 0"])),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "scheduler-order",
        TraceFile,
        "ovsds",
        "Every line is a token, `TICKS` and `so_n` (the runs of the global slot-1 action, which counts first),\n" +
        "so equal `TICKS` mean the same cycle and the token order inside a cycle is the dispatch order. The\n" +
        "`startup` case selects the trace file at the top of `IF_START` (the prelude) and samples every cycle\n" +
        "of the settling `WAITT 16`. Compare the trace with `case.json` → `predictions`, check `OVCON.TXT`\n" +
        "for compile errors, and run `ovtool oracle diff` on the probe to see where OpenVirtue differs.",
        All);
}
