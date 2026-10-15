// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>each-sec-cadence</c> probe
/// (<c>docs/clean-room/observations/OV-20261015-each-sec-cadence.md</c>). The 1998 manual documents
/// the 16 global <c>EACH_TICK</c> slots (p.96) and <c>SECS</c> (p.117) but never <c>EACH_SEC</c>, which
/// the retail scripts use for their timeouts. Every action prints <c>TICKS</c> (and <c>SECS</c>) next
/// to its token: at <c>forcerate=60</c> one frame advances <c>TICKS</c> by about 0.267, so two lines
/// with the same printed <c>TICKS</c> come from the same frame, and the token order inside a frame
/// is the dispatch order. Every case clears its slots before <c>RET</c>, because the carrier ends
/// with <c>WAITT 4; SCREENSHOT; EXIT</c>.
/// </summary>
internal static class EachSecProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVSEC0.TXT).</summary>
    public const string TraceFile = "ovsec";

    private static readonly string SecAction = Action("sec_a", V("S", "TICKS", "SECS"));

    private static readonly string Arm = V("ARM", "TICKS", "SECS");

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "cadence",
            "Is SET EACH_SEC.1 accepted, and how often does the slot's action run: once per 16 ticks, every frame, or never?",
            SecAction,
            Arm + "    SET EACH_SEC.1, sec_a;\n    WAITT 40;\n    SET EACH_SEC.1, NULL;\n" + P("RET"),
            Predict(
                ("once per second (16 ticks)", "ARM t, then 2-3 S lines whose TICKS differ by 16, then RET"),
                ("every frame", "ARM t, about 150 S lines, RET"),
                ("accepted but never runs", "ARM t, RET"),
                ("not a valid target", "no OVSEC0.TXT; a Genesis error in OVCON.TXT"))),
        new TraceProbeCase(
            "phase",
            "Armed 8 ticks later than in `cadence`: does the first run come one second after the SET, or at the runtime's own second boundary?",
            SecAction,
            "    WAITT 8;\n" + Arm + "    SET EACH_SEC.1, sec_a;\n    WAITT 40;\n    SET EACH_SEC.1, NULL;\n" + P("RET"),
            Predict(
                ("per-arm: one second after the SET", "first S at TICKS = t + 16 (one frame either way)"),
                ("global: a shared second boundary", "first S before t + 16, where SECS has just gone up, at the same point of the second as in `cadence`"))),
        new TraceProbeCase(
            "order-vs-tick",
            "In the frame where EACH_SEC.1 runs, does it run before or after the global EACH_TICK slots, or somewhere else in the frame loop?",
            SecAction + Action("tick_k", V("K", "TICKS")),
            Arm + "    SET EACH_TICK.1, tick_k;\n    SET EACH_SEC.1, sec_a;\n    WAITT 20;\n    SET EACH_SEC.1, NULL;\n    SET EACH_TICK.1, NULL;\n" + P("RET"),
            Predict(
                ("same frame, EACH_TICK first", "... K x, S x ... (equal TICKS, K before S)"),
                ("same frame, EACH_SEC first", "... S x, K x ..."),
                ("a separate point of the loop", "an S line whose TICKS matches no K line"))),
        new TraceProbeCase(
            "slot-16",
            "Is slot 16 accepted and run? (The retail scripts use slots 1, 9 and 10.)",
            SecAction,
            Arm + "    SET EACH_SEC.16, sec_a;\n    WAITT 20;\n    SET EACH_SEC.16, NULL;\n" + P("RET"),
            Predict(
                ("16 or more slots", "ARM t, S, RET"),
                ("fewer than 16 slots", "a Genesis error, or ARM t, RET"))),
        new TraceProbeCase(
            "slot-17",
            "Is slot 17 rejected (at compile time or at run time), or are there more than 16 slots?",
            SecAction,
            Arm + "    SET EACH_SEC.17, sec_a;\n    WAITT 20;\n    SET EACH_SEC.17, NULL;\n" + P("RET"),
            Predict(
                ("16 slots, checked by the compiler", "no OVSEC0.TXT; a Genesis error in OVCON.TXT"),
                ("16 slots, ignored at run time", "ARM t, RET"),
                ("more than 16 slots", "ARM t, S, RET"))),
        new TraceProbeCase(
            "wait-inside",
            "If an EACH_SEC action WAITs longer than a second, does each second start another instance, is the slot skipped while one waits, or is WAIT refused?",
            Action("sec_w", V("W1", "TICKS") + "    WAITT 24;\n" + V("W2", "TICKS")),
            Arm + "    SET EACH_SEC.1, sec_w;\n    WAITT 56;\n    SET EACH_SEC.1, NULL;\n" + P("RET") + "    WAITT 32;\n" + P("LATE"),
            Predict(
                ("a new instance every second", "a W1 every 16 ticks, each followed by its own W2 24 ticks later (W1 and W2 interleave)"),
                ("skipped while an instance waits", "W1 a, W2 a+24, W1 a+32, W2 a+56: one instance at a time"),
                ("WAIT refused in EACH_SEC actions", "a Genesis error, or W1 lines without any W2"))),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "each-sec-cadence",
        TraceFile,
        "ovses",
        "After the skill dump, `IF_START` selects the trace file, writes `BEGIN` and runs the variant's case.\n" +
        "Each line is a token followed by `TICKS` (and `SECS`) at the moment it was printed. At forcerate=60 a\n" +
        "frame advances `TICKS` by about 0.267, so equal `TICKS` values mean the same frame. Read the trace,\n" +
        "compare it with `case.json` → `predictions`, and check `OVCON.TXT` for compile errors.",
        All);
}
