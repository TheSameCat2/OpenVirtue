// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The control-flow cases of the <c>branch-semantics</c> probe
/// (<c>docs/clean-room/observations/OV-20261015-branch-tail-jump.md</c>). The 1998 manual
/// indexes <c>BRANCH</c> at p.79 and p.98 but describes only <c>CALL</c> (p.79: the caller goes
/// on after the called action ends or reaches a <c>WAIT</c>). Each case is one carrier: its
/// actions print short trace tokens to <c>OVBRN0.TXT</c> (one <c>PRINTFILE</c>, appended by
/// every <c>PRINT_STRING</c>, OV-20261009-shoot-mapping), so the token order shows which
/// statements ran. One case per carrier keeps a crash or a compile error from hiding the rest.
/// </summary>
internal static class BranchProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVBRN0.TXT).</summary>
    public const string TraceFile = "ovbrn";

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "fallthrough",
            "Does the statement after BRANCH run, and does the action that CALLed the branching action resume?",
            Action("br_tgt", P("T1")) +
            Action("br_src", P("E1") + "    BRANCH br_tgt;\n" + P("E2")),
            "    CALL br_src;\n" + P("RET"),
            Predict(
                ("tail jump, CALLer resumes", "E1 T1 RET"),
                ("returns like CALL", "E1 T1 E2 RET"),
                ("ends the whole call chain", "E1 T1"))),
        new TraceProbeCase(
            "call-control",
            "Control: the same fixture with CALL instead of BRANCH (manual p.79).",
            Action("br_tgt", P("T1")) +
            Action("br_src", P("E1") + "    CALL br_tgt;\n" + P("E2")),
            "    CALL br_src;\n" + P("RET"),
            Predict(("documented CALL", "E1 T1 E2 RET"))),
        new TraceProbeCase(
            "wait",
            "With a WAITT inside the BRANCHed-to action, does the branching action resume at the WAIT, and does its CALLer?",
            Action("br_tgt", P("T1") + "    WAITT 2;\n" + P("T2")) +
            Action("br_src", P("E1") + "    BRANCH br_tgt;\n" + P("E2")),
            "    CALL br_src;\n" + P("RET") + "    WAITT 8;\n" + P("LATE"),
            Predict(
                ("tail jump, CALLer resumes at the WAIT", "E1 T1 RET T2 LATE"),
                ("returns like CALL", "E1 T1 E2 RET T2 LATE"))),
        new TraceProbeCase(
            "wait-control",
            "Control: the wait fixture with CALL instead of BRANCH (manual p.80: the CALLer goes on during the WAIT).",
            Action("br_tgt", P("T1") + "    WAITT 2;\n" + P("T2")) +
            Action("br_src", P("E1") + "    CALL br_tgt;\n" + P("E2")),
            "    CALL br_src;\n" + P("RET") + "    WAITT 8;\n" + P("LATE"),
            Predict(("documented CALL", "E1 T1 E2 RET T2 LATE"))),
        new TraceProbeCase(
            "synonym",
            "Does BRANCH through an ACTION synonym (SET at run time, as the title menu does) behave like a direct name?",
            "SYNONYM br_syn { TYPE ACTION; }\n" +
            Action("br_tgt", P("T1")) +
            Action("br_src", P("E1") + "    BRANCH br_syn;\n" + P("E2")),
            "    SET br_syn, br_tgt;\n    CALL br_src;\n" + P("RET"),
            Predict(
                ("tail jump", "E1 T1 RET"),
                ("returns like CALL", "E1 T1 E2 RET"))),
        new TraceProbeCase(
            "blocks",
            "Does BRANCH inside an IF block leave the action, and inside a WHILE leave the loop?",
            "SKILL br_z { VAL 0; }\nSKILL br_n { VAL 0; }\n" +
            Action("br_tgt", P("T1")) +
            Action("br_tgt2", P("T2")) +
            Action("br_src", P("E1") + "    IF (br_z == 0) {\n        BRANCH br_tgt;\n" + P("E2", 8) + "    }\n" + P("E3")) +
            Action("br_loop", P("L1") + "    WHILE (br_n < 3) {\n        RULE br_n = br_n + 1;\n" + P("W", 8) + "        BRANCH br_tgt2;\n" + P("W2", 8) + "    }\n" + P("L2")),
            "    CALL br_src;\n" + P("RET") + "    CALL br_loop;\n" + P("RET2"),
            Predict(
                ("tail jump", "E1 T1 RET L1 W T2 RET2"),
                ("returns like CALL", "E1 T1 E2 E3 RET L1 W T2 W2 W T2 W2 W T2 W2 L2 RET2"))),
        new TraceProbeCase(
            "legacy-if",
            "The title menu's shape: an IF_EQUAL chain whose first BRANCH target changes the compared skill.",
            "SKILL br_pos { VAL 1; }\n" +
            Action("br_tgt1", P("T1") + "    SET br_pos, 4;\n") +
            Action("br_tgt4", P("T4")) +
            Action("br_src", "    IF_EQUAL br_pos, 1;\n    BRANCH br_tgt1;\n    IF_EQUAL br_pos, 4;\n    BRANCH br_tgt4;\n" + P("E2")),
            "    CALL br_src;\n" + P("RET"),
            Predict(
                ("tail jump", "T1 RET"),
                ("returns like CALL", "T1 T4 E2 RET"))),
        new TraceProbeCase(
            "depth",
            "Can an action BRANCH to itself 1000 times without a WAIT (no nesting limit), or does the runtime stop or fail?",
            "SKILL br_n { VAL 0; }\n" +
            Action("br_loop", "    RULE br_n = br_n + 1;\n    IF (br_n < 1000) {\n        BRANCH br_loop;\n    }\n"),
            "    CALL br_loop;\n" + P("RET") + "    PRINT_STRING \"N=\";\n    PRINT_VALUE br_n;\n" + P(""),
            Predict(
                ("unbounded tail jump", "RET N=1000"),
                ("nesting limit", "RET N=<limit>, or no END marker if the runtime fails"))),
        new TraceProbeCase(
            "unresolved-null",
            "BRANCH through an ACTION synonym set to NULL: does the action go on, or end?",
            "SYNONYM br_syn { TYPE ACTION; }\n" +
            Action("br_src", P("E1") + "    BRANCH br_syn;\n" + P("E2")),
            "    SET br_syn, NULL;\n    CALL br_src;\n" + P("RET"),
            Predict(
                ("BRANCH ends the action regardless", "E1 RET"),
                ("an empty BRANCH is skipped", "E1 E2 RET"))),
        new TraceProbeCase(
            "unresolved-name",
            "BRANCH to a name that is declared nowhere: compile error (see OVCON.TXT) or run-time behavior?",
            Action("br_src", P("E1") + "    BRANCH br_none;\n" + P("E2")),
            "    CALL br_src;\n" + P("RET"),
            Predict(
                ("compile error", "no OVBRN0.TXT; Genesis error in OVCON.TXT"),
                ("BRANCH ends the action", "E1 RET"),
                ("skipped", "E1 E2 RET"))),
        new TraceProbeCase(
            "each-tick",
            "Does BRANCH from an EACH_TICK action behave the same (the action clears its own slot first)?",
            Action("br_tgt", P("T1")) +
            Action("br_tick", "    SET EACH_TICK.1, NULL;\n" + P("K1") + "    BRANCH br_tgt;\n" + P("K2")),
            "    SET EACH_TICK.1, br_tick;\n    WAITT 4;\n" + P("RET"),
            Predict(
                ("tail jump", "K1 T1 RET"),
                ("returns like CALL", "K1 T1 K2 RET"))),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "branch-semantics",
        TraceFile,
        "ovbrs",
        "After the skill dump, `IF_START` selects the trace file, writes `BEGIN` and runs the variant's case.\n" +
        "The case's actions write short trace tokens (one per line), so the token order shows which statements\n" +
        "ran. There is no measurement script: read the trace, compare it with `case.json` → `predictions`, and\n" +
        "check `OVCON.TXT` for compile errors. The screenshot is incidental.",
        All);
}
