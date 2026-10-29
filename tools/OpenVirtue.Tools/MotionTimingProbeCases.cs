// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>motion-timing</c> probe: the two frame-rate dependent engine primitives that
/// scripted motion and animation rest on.
/// <list type="bullet">
/// <item><c>ACCEL skill, a</c> (p.76) accelerates a speed "in a special way" with <c>INERTIA</c>
/// (default 1) and <c>FRICTION</c> (default 0.5); the retail scripts drive walking, jumping and
/// gravity through it. The formula is not given.</item>
/// <item>Texture animation: <c>DELAY</c> lists phase durations in ticks, "the higher the DELAY value,
/// the more does the animation speed remain constant on different frame rates" (p.39), which hints
/// that a phase lasts at least one frame; <c>RANDOM</c> adds up to that fraction to every phase;
/// <c>ONESHOT</c> plays once when <c>PLAY</c> is set and then clears it and runs <c>EACH_CYCLE</c>.</item>
/// </list>
/// Every line is a token, <c>TICKS</c>, <c>TIME_CORR</c> and <c>mt_n</c> (cycles counted by the global
/// slot-1 action, which runs first), then the case's values. DOSBox-X's frame rate varies from run to
/// run, so each <c>ACCEL</c> line carries the speed before and after one call together with that
/// cycle's <c>TIME_CORR</c>, enough to fit the formula.
/// </summary>
internal static class MotionTimingProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVMOT0.TXT).</summary>
    public const string TraceFile = "ovmot";

    private const string Skills =
        "SKILL mt_n { VAL 0; }\nSKILL mt_v { VAL 0; }\nSKILL mt_v0 { VAL 0; }\nSKILL mt_on { VAL 0; }\n" +
        "SKILL mt_a { VAL 0; }\nSKILL mt_p { VAL 0; }\n" +
        "SYNONYM mt_st { TYPE THING; }\n";

    private static string L(string token, params string[] extra) => V(token, ["TICKS", "TIME_CORR", "mt_n", .. extra]);

    /// <summary>A global slot-1 action that counts and, for the first 48 cycles, applies one ACCEL and prints the speed before and after.</summary>
    private static string AccelTick(string acceleration) => Action(
        "mt_tick",
        "    RULE mt_n = mt_n + 1;\n    IF (mt_n < 48) {\n        RULE mt_v0 = mt_v;\n" +
        $"        ACCEL mt_v, {acceleration};\n" + Indent(L("C", "mt_v0", "mt_v")) + "    }\n");

    private static TraceProbeCase Accel(string label, string setup, string acceleration, string question) => new(
        label,
        question,
        Skills + AccelTick(acceleration),
        setup + "    SET EACH_TICK.1, mt_tick;\n" + L("GO", "FRICTION", "INERTIA") + "    WAITT 24;\n    SET EACH_TICK.1, NULL;\n" + P("RET"),
        Predict(
            ("v' = (1 - TIME_CORR·FRICTION)·v + TIME_CORR·a/INERTIA (OpenVirtue's guess)", "every C line satisfies it with its own TIME_CORR"),
            ("not scaled by TIME_CORR", "v' = (1 - FRICTION)·v + a/INERTIA whatever TIME_CORR is"),
            ("exponential friction", "v' = v·(1 - FRICTION)^TIME_CORR + …")));

    /// <summary>The counting slot-1 action for the animation cases.</summary>
    private static readonly string CounterTick = Action("mt_tick", "    RULE mt_n = mt_n + 1;\n");

    /// <summary>
    /// <c>PLAY</c> as a number in <c>mt_p</c>: a flag cannot be printed (<c>PRINT_VALUE MY.PLAY</c> is
    /// "Error(66): Wrong typ … FIXED FLAGS", OV-20261029-motion-timing), but it can be compared.
    /// </summary>
    private const string ReadPlay = "    RULE mt_p = 0;\n    IF (MY.PLAY == 1) { RULE mt_p = 1; }\n";

    /// <summary>A thing with <paramref name="texture"/> whose EACH_TICK prints its CYCLE (and PLAY) and whose EACH_CYCLE prints E, while mt_on.</summary>
    private static string AnimThing(string texture) =>
        Skills + CounterTick +
        Action("mt_at", "    IF (mt_on > 0) {\n" + Indent(ReadPlay + L("A", "MY.CYCLE", "mt_p")) + "    }\n") +
        Action("mt_ae", "    IF (mt_on > 0) {\n" + Indent(ReadPlay + L("E", "MY.CYCLE", "mt_p")) + "    }\n") +
        texture +
        "THING mt_t { TEXTURE mt_tex; EACH_CYCLE mt_ae; EACH_TICK mt_at; }\n";

    private static readonly TraceProbeMap AnimMap = new(Placements: ["THING mt_t 20 0 0 0"]);

    private static string AnimRun(int ticks, string middle = "") =>
        "    SET mt_st, mt_t;\n    SET EACH_TICK.1, mt_tick;\n    RULE mt_on = 1;\n" + L("GO") + middle +
        $"    WAITT {ticks};\n    RULE mt_on = 0;\n    SET EACH_TICK.1, NULL;\n" + P("RET");

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Accel("accel-default", "", "1", "ACCEL with the defaults (FRICTION 0.5, INERTIA 1) and a = 1 from v = 0: what is v after each call, given that cycle's TIME_CORR?"),
        Accel("accel-friction0", "    SET FRICTION, 0;\n", "1", "ACCEL with FRICTION 0: does v grow by TIME_CORR·a per call (no decay)?"),
        Accel("accel-friction1", "    SET FRICTION, 1;\n", "1", "ACCEL with FRICTION 1: does v settle where TIME_CORR·a balances the friction, or reset to the acceleration each call?"),
        Accel("accel-inertia4", "    SET INERTIA, 4;\n", "1", "ACCEL with INERTIA 4: is the acceleration divided by the inertia, and is the friction too?"),
        Accel("accel-decay", "    RULE mt_v = 10;\n", "0", "ACCEL with a = 0 from v = 10: how does friction alone shrink v per call and per tick?"),
        Accel("accel-negative", "    RULE mt_v = 2;\n    RULE mt_a = 0 - 1;\n", "mt_a", "ACCEL with a = -1 from v = 2: does friction act toward zero symmetrically, and does v cross zero?"),
        new TraceProbeCase(
            "anim-delay",
            "A thing texture with CYCLES 3, DELAY 1, 2, 3: in which cycles does CYCLE change and EACH_CYCLE run? Does a phase last its ticks exactly (leftover carried), at least one frame, or whole frames rounded up?",
            AnimThing("TEXTURE mt_tex { SCALE_X 32; SCALE_Y 32; CYCLES 3; BMAPS red_map, grn_map, yel_map; DELAY 1, 2, 3; }\n"),
            AnimRun(40),
            Predict(
                ("ticks with leftover carried", "phase changes 1, 2, 3 ticks apart on average; an E every 6 ticks"),
                ("whole frames, no carry", "every phase lasts its ticks rounded up to frames, so the 6-tick cycle stretches"),
                ("CYCLE counts phases", "CYCLE goes 0, 1, 2, 0 …")),
            Map: AnimMap),
        new TraceProbeCase(
            "anim-delay8",
            "CYCLES 2, DELAY 8, 8: does the phase change exactly every 8 ticks of TICKS, and when does EACH_CYCLE run relative to the CYCLE change and the thing's EACH_TICK?",
            AnimThing("TEXTURE mt_tex { SCALE_X 32; SCALE_Y 32; CYCLES 2; BMAPS red_map, grn_map; DELAY 8, 8; }\n"),
            AnimRun(56),
            Predict(
                ("exact ticks", "CYCLE changes in the first cycle at or past each 8-tick boundary since GO"),
                ("E before the thing's EACH_TICK", "E and A with equal TICKS, E first")),
            Map: AnimMap),
        new TraceProbeCase(
            "anim-random",
            "CYCLES 2, DELAY 2, 2, RANDOM 1: how long do the phases last (2 to 3 ticks, uniformly?) over about 60 phases?",
            AnimThing("TEXTURE mt_tex { SCALE_X 32; SCALE_Y 32; CYCLES 2; BMAPS red_map, grn_map; DELAY 2, 2; RANDOM 1; }\n"),
            AnimRun(160),
            Predict(
                ("up to DELAY·RANDOM more", "phases between 2 and 4 ticks"),
                ("up to RANDOM ticks more", "phases between 2 and 3 ticks")),
            Map: AnimMap),
        new TraceProbeCase(
            "anim-oneshot",
            "A ONESHOT texture (CYCLES 3, DELAY 2, 2, 2): does it stand until PLAY is set, then play once, clear PLAY and run EACH_CYCLE at the end, and on which CYCLE does it rest?",
            AnimThing("TEXTURE mt_tex { SCALE_X 32; SCALE_Y 32; CYCLES 3; BMAPS red_map, grn_map, yel_map; DELAY 2, 2, 2; FLAGS ONESHOT; }\n"),
            AnimRun(24, "    WAITT 4;\n" + L("PLAY") + "    SET mt_st.PLAY, 1;\n"),
            Predict(
                ("plays once from PLAY", "CYCLE 0 until PLAY, then 1, 2 at 2-tick steps, E once, PLAY back to 0"),
                ("rests on the last phase", "CYCLE stays 2 after E"),
                ("rests on the first phase", "CYCLE back to 0 after E")),
            Map: AnimMap),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "motion-timing",
        TraceFile,
        "ovmos",
        "Every line is a token, `TICKS`, `TIME_CORR` and `mt_n` (the runs of the global slot-1 action), then the\n" +
        "case's values: `C` lines carry the speed before and after one `ACCEL`, `A` lines a thing's `CYCLE` and\n" +
        "`PLAY` from its `EACH_TICK`, `E` lines its `EACH_CYCLE`. Fit the readings in `case.json` → `predictions`\n" +
        "and run `ovtool oracle diff` on the probe.",
        All);
}
