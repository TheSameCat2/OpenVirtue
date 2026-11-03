// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>contact-clock</c> probe, three questions the earlier traces left open:
/// <list type="bullet">
/// <item>The <c>step-</c> cases: how the player stops against a thing with steps of 0.4 to 3.2.
/// OV-20261029-overlap (still-walk) measured 4/1024 steps, but actor-contact stop-width's 1.6 steps
/// were put back further out than that rule gives.</item>
/// <item>The <c>clock-</c> cases: the clock behind <c>SECS</c> and <c>EACH_SEC</c>. A second took 61
/// or 62 cycles and drifted from <c>TICKS</c> (OV-20261029-scheduler-order).</item>
/// <item>The <c>follow-</c> cases: the first move of a skipped <c>FOLLOW</c> actor after the start-up
/// (actor-events follow-border), sampled from cycle 1.</item>
/// </list>
/// A global <c>EACH_TICK</c> sampler, armed at the top of <c>IF_START</c>, prints <c>TICKS</c>, a
/// cycle counter and <c>TIME_CORR</c> every cycle, with the quantities of the case; positions are
/// printed × 1024. The <c>step-</c> cases set <c>PLAYER_VX</c> to the step over <c>TIME_CORR</c> in
/// every cycle once <c>GO</c> is printed, so the step is the same at any frame rate.
/// </summary>
internal static class ContactClockProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVCCL0.TXT).</summary>
    public const string TraceFile = "ovccl";

    private const string Skills =
        "SKILL cc_n { VAL 0; }\nSKILL cc_go { VAL 0; }\nSKILL cc_s { VAL 0; }\nSKILL cc_v { VAL 0; }\n" +
        "SKILL cc_x { VAL 0; }\nSKILL cc_y { VAL 0; }\nSKILL cc_d { VAL 0; }\nSYNONYM cc_f { TYPE ACTOR; }\n" +
        "TEXTURE cc_t2 { SCALE_X 32; SCALE_Y 32; BMAPS red_map; }\n" +
        "TEXTURE cc_t4 { SCALE_X 16; SCALE_Y 16; BMAPS red_map; }\n";

    private const string Position = "    RULE cc_x = PLAYER_X * 1024;\n    RULE cc_y = PLAYER_Y * 1024;\n";

    /// <summary>A global sampler printing <paramref name="token"/>, <c>TICKS</c>, the cycle and <c>TIME_CORR</c>, then <paramref name="extra"/>.</summary>
    private static string Sampler(string token, string before, params string[] extra) =>
        Action(
            "cc_tick",
            "    RULE cc_n = cc_n + 1;\n" +
            "    IF (cc_go > 0) {\n        RULE cc_v = cc_s / TIME_CORR;\n        SET PLAYER_VX, cc_v;\n    }\n" +
            "    IF (cc_n < 420) {\n" + Indent(before + V(token, ["TICKS", "cc_n", "TIME_CORR", .. extra])) + "    }\n");

    private static readonly string Prelude =
        "    PRINTFILE \"" + TraceFile + "\", 0;\n" + V("S0", "TICKS", "cc_n", "TIME_CORR") + "    SET EACH_TICK.1, cc_tick;\n";

    // ---- contact steps -------------------------------------------------------------------------

    private static readonly string NearHook =
        Action("cc_near", "    RULE cc_d = MY.DISTANCE;\n" + Position + V("N", "TICKS", "cc_n", "cc_d", "cc_x", "cc_y"));

    private static TraceProbeCase Step(string label, double step, string thing, string question) => new(
        label,
        question,
        Skills + NearHook + Sampler("Y", Position, "cc_x", "cc_y") +
        "THING cc_k2 { TEXTURE cc_t2; IF_NEAR cc_near; }\nTHING cc_k4 { TEXTURE cc_t4; IF_NEAR cc_near; }\n",
        FormattableString.Invariant($"    RULE cc_s = {step};\n    RULE cc_go = 1;\n") + V("GO", "TICKS", "cc_n") +
        "    WAIT 90;\n    RULE cc_go = 0;\n    SET PLAYER_VX, 0;\n" + P("RET"),
        Predict(
            ("one step ahead, as still-walk", "the first push puts the player at C² from the step's end"),
            ("two steps ahead", "the first push puts the player at C² from the point two steps on"),
            ("sub-steps", "the step is cut into parts and stopped at the first part inside")),
        Map: new TraceProbeMap(Placements: [thing]),
        Prelude: Prelude);

    // ---- the seconds clock ---------------------------------------------------------------------

    private static readonly string SecondHook = Action("cc_sec", V("SEC", "TICKS", "cc_n", "SECS"));

    private static TraceProbeCase Clock(string label, string question, params string[] skills) => new(
        label,
        question,
        Skills + SecondHook + Sampler("C", "", ["SECS", "TOTAL_FRAMES", .. skills]),
        V("GO", "TICKS", "cc_n") + "    WAIT 400;\n" + P("RET"),
        Predict(
            ("SECS on a 16-tick clock", "SECS steps when TICKS passes a multiple of 16"),
            ("SECS on a finer real-time clock", "SECS steps 61-62 cycles apart, drifting from TICKS")),
        Prelude: Prelude + "    SET EACH_SEC.1, cc_sec;\n");

    // ---- the follower --------------------------------------------------------------------------

    private static TraceProbeCase Follow(string label, string question) => new(
        label,
        question,
        Skills + Sampler("F", "", "cc_f.X", "cc_f.Y") +
        "ACTOR cc_a { TEXTURE cc_t2; TARGET FOLLOW; SPEED 0.5; FLAGS CAREFULLY; }\n",
        V("GO", "TICKS", "cc_n") + "    WAIT 120;\n" + P("RET"),
        Predict(
            ("the first move covers TICKS from cycle 1", "the first F line that moves covers 0.5 × TICKS of that cycle"),
            ("the first move covers the skipped cycles' TIME_CORR", "it covers the TIME_CORR of the cycles since its last move")),
        Map: new TraceProbeMap(PadBody: "", Placements: ["ACTOR cc_a -8 16 0 2"]),
        Prelude: Prelude.Replace("    SET EACH_TICK.1, cc_tick;\n", "    SET cc_f, cc_a;\n    SET EACH_TICK.1, cc_tick;\n", StringComparison.Ordinal));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Step("step16-off", 1.6, "THING cc_k2 16 -0.01953125 0 0",
            "1.6 steps +X into a 2-wide thing at (16, -20/1024), as stop-width at 8 ticks a cycle: where is the player put back each cycle?"),
        Step("step16-on", 1.6, "THING cc_k2 16 0 0 0", "1.6 steps +X into a 2-wide thing on the line."),
        Step("step08-off", 0.8, "THING cc_k2 16 -0.01953125 0 0", "0.8 steps into the 2-wide thing 20/1024 off the line."),
        Step("step04-off", 0.4, "THING cc_k2 16 -0.01953125 0 0", "0.4 steps into the 2-wide thing 20/1024 off the line."),
        Step("step32-off", 3.2, "THING cc_k2 16 -0.01953125 0 0", "3.2 steps, more than the contact distance, into the 2-wide thing: stopped or through?"),
        Step("step16-w4", 1.6, "THING cc_k4 16 -0.01953125 0 0", "1.6 steps into a 4-wide thing 20/1024 off the line."),
        Clock("clock-sec", "SECS, TOTAL_FRAMES and EACH_SEC every cycle from IF_START for 420 cycles: when does a second end?"),
        Clock("clock-stats", "The clock-sec sampler with TOTAL_TIME and the save's run-time statistics TIME_TOTAL, TIME_DRAW and TIME_FBUFFER: is one of them a finer clock?",
            "TOTAL_TIME", "TIME_TOTAL", "TIME_DRAW", "TIME_FBUFFER"),
        Follow("follow-1", "follow-border's CAREFULLY FOLLOW actor on the pad (-8, 16), sampled from cycle 1: in which cycle does it first move, and how far?"),
        Follow("follow-2", "The same carrier as follow-1, launch 2."),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "contact-clock",
        TraceFile,
        "ovccs",
        "`S0` at the top of `IF_START`, then one sampler line every cycle (TICKS, cycle, TIME_CORR, then the\n" +
        "case's values): `Y` with PLAYER_X/Y × 1024 and `N` from the thing's IF_NEAR (step cases), `C` with SECS and\n" +
        "TOTAL_FRAMES and `SEC` from EACH_SEC.1 (clock cases), `F` with the follower's X and Y (follow cases).",
        All);
}
