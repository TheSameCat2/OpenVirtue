// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>flic-play</c> probe
/// (<c>docs/clean-room/observations/OV-20261015-flic-play-blocking.md</c>, issue #9). The 1998
/// manual (p.82) says that during flic play the screen building and movement stop and the 16 global
/// <c>EACH_TICK</c> actions keep running; it does not say whether the action that called
/// <c>PLAY_FLICFILE</c> waits for the flic, or whether key hooks fire while it plays. Each carrier
/// plays <see cref="FlicFile"/>, a synthetic 30-second FLC (<see cref="SyntheticFlic"/>), and its
/// actions print a token with <c>FLIC_FRAME</c> and <c>TICKS</c>. The key cases press Space through
/// DOSBox-X <c>AUTOTYPE</c> about 25 s after the emulator starts, so the key lands inside the flic
/// whatever the boot time; the <c>KEY</c> line records the frame it landed on. Every case clears its
/// slots and hooks before <c>RET</c>, because the carrier ends with <c>WAITT 4; SCREENSHOT; EXIT</c>.
/// </summary>
internal static class FlicProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVFLC0.TXT).</summary>
    public const string TraceFile = "ovflc";

    /// <summary>The synthetic flic every carrier plays (written into <c>work/</c>).</summary>
    public const string FlicFile = "PRBFLC.FLC";

    /// <summary>Displayed frames and frame delay of the synthetic flic: 430 × 70 ms ≈ 30 s.</summary>
    public const int FlicFrames = 430;

    public const int FlicSpeedMs = 70;

    /// <summary>The scripted key press: Space, 25 s after the autoexec line runs (the probe action starts about 18 s in).</summary>
    public const string KeyAutotype = "-w 25 -p 0.5 space";

    private const string Skills =
        "SKILL fl_tnext { VAL 0; }\n" +
        "SKILL fl_fnext { VAL 0; }\n";

    /// <summary>
    /// Global EACH_TICK sampler: a <c>TK</c> line every 32 ticks and an <c>FR</c> line every 50 flic
    /// frames, so the trace shows whether the slot runs during the flic even if one of the two
    /// clocks stands still. No WAIT (p.96).
    /// </summary>
    private static readonly string TickAction = Action(
        "fl_tick",
        "    IF (TICKS > fl_tnext) {\n        RULE fl_tnext = TICKS + 32;\n" + Indent(V("TK", "FLIC_FRAME", "TICKS")) + "    }\n" +
        "    IF (FLIC_FRAME > fl_fnext) {\n        RULE fl_fnext = FLIC_FRAME + 49;\n" + Indent(V("FR", "FLIC_FRAME", "TICKS")) + "    }\n");

    private static readonly string SecAction = Action("fl_sec", V("SEC", "FLIC_FRAME", "TICKS"));

    private static readonly string WaitAction = Action("fl_wait", "    WAITT 32;\n" + V("WAITED", "FLIC_FRAME", "TICKS"));

    private static readonly string KeyStopAction = Action(
        "fl_key",
        V("KEY", "FLIC_FRAME", "TICKS") + "    STOP_FLIC;\n" + V("STOPPED", "FLIC_FRAME", "TICKS") + "    SET IF_ANYKEY, NULL;\n");

    private static readonly string KeyOnlyAction = Action(
        "fl_key0",
        V("KEY", "FLIC_FRAME", "TICKS") + "    SET IF_ANYKEY, NULL;\n");

    private static readonly string Play =
        V("PLAY", "FLIC_FRAME", "TICKS") + $"    PLAY_FLICFILE <{FlicFile}>;\n" + V("AFTER", "FLIC_FRAME", "TICKS");

    private static readonly string Tail =
        "    WAITT 560;\n" + V("RET", "FLIC_FRAME", "TICKS");

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "key-control",
            "Harness control, no flic: does the AUTOTYPE Space reach the runtime and fire IF_ANYKEY?",
            KeyOnlyAction,
            V("ARM", "TICKS") + "    SET IF_ANYKEY, fl_key0;\n" + Tail + "    SET IF_ANYKEY, NULL;\n",
            Predict(
                ("the key reaches IF_ANYKEY", "ARM t, KEY 0 t', RET"),
                ("AUTOTYPE did not inject (harness failure, not a finding)", "ARM t, RET")),
            KeyAutotype),
        new TraceProbeCase(
            "no-key",
            "Without a key: does the statement after PLAY_FLICFILE run before the flic ends; do a WAITT started before it, " +
            "the global EACH_TICK slot and the EACH_SEC slot run during the flic; and what is FLIC_FRAME after the end?",
            Skills + TickAction + SecAction + WaitAction,
            "    SET EACH_TICK.1, fl_tick;\n    SET EACH_SEC.1, fl_sec;\n    CALL fl_wait;\n" + Play + Tail +
            "    SET EACH_TICK.1, NULL;\n    SET EACH_SEC.1, NULL;\n",
            Predict(
                ("the action goes on at once", "AFTER right after PLAY, same TICKS, FLIC_FRAME 0 or 1"),
                ("the action waits for the flic", "AFTER after the last FR line, FLIC_FRAME at the last frame, TICKS about 480 later (30 s)"),
                ("WAITs resume during the flic", "WAITED about 32 ticks after PLAY, with 0 < FLIC_FRAME < 430"),
                ("WAITs are frozen during the flic", "WAITED after the flic, FLIC_FRAME at its end value"),
                ("EACH_TICK runs during the flic (p.82)", "TK/FR lines with rising FLIC_FRAME between PLAY and the end"),
                ("EACH_SEC runs during the flic", "SEC lines with 0 < FLIC_FRAME < 430"),
                ("EACH_SEC paused during the flic", "no SEC line while FLIC_FRAME rises"),
                ("FLIC_FRAME after the end", "the TK lines after the last FR: held at the last frame, 0, or still counting"))),
        new TraceProbeCase(
            "key-before",
            "With IF_ANYKEY armed before PLAY_FLICFILE: does a key during the flic fire the hook, and does STOP_FLIC from the hook end the flic?",
            Skills + TickAction + KeyStopAction,
            "    SET EACH_TICK.1, fl_tick;\n    SET IF_ANYKEY, fl_key;\n" + Play + Tail +
            "    SET EACH_TICK.1, NULL;\n    SET IF_ANYKEY, NULL;\n",
            Predict(
                ("hooks fire during the flic and STOP_FLIC ends it", "KEY with 0 < FLIC_FRAME < 430, STOPPED, then no FR line after it"),
                ("hooks fire, STOP_FLIC does not end it", "KEY, STOPPED, FR lines go on to the end"),
                ("the action waits and STOP_FLIC releases it", "KEY, STOPPED, AFTER right after"),
                ("hooks do not fire during the flic", "no KEY before the flic ends; at most a late KEY after it"),
                ("the key came before the flic (rerun with a later -w)", "KEY before PLAY")),
            KeyAutotype),
        new TraceProbeCase(
            "retail-order",
            "The retail title's order: IF_ANYKEY armed after PLAY_FLICFILE in the same action. Can a key cut the flic short?",
            Skills + TickAction + KeyStopAction,
            "    SET EACH_TICK.1, fl_tick;\n" + Play + "    SET IF_ANYKEY, fl_key;\n" + Tail +
            "    SET EACH_TICK.1, NULL;\n    SET IF_ANYKEY, NULL;\n",
            Predict(
                ("the action goes on and the key skips the flic", "AFTER right after PLAY; KEY with 0 < FLIC_FRAME < 430, STOPPED, no FR line after it"),
                ("the action waits, so the key is not heard during the flic", "FR lines to the end, then AFTER; KEY absent (the key came before the hook was armed)")),
            KeyAutotype),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "flic-play",
        TraceFile,
        "ovfls",
        "After the skill dump, `IF_START` selects the trace file, writes `BEGIN` and runs the variant's case.\n" +
        $"Every carrier holds `{FlicFile}`, a synthetic {FlicFrames}-frame FLC at {FlicSpeedMs} ms per frame (about 30 s): a\n" +
        "blue screen with a yellow line sweeping down, one scanline per frame. Each trace line is a token\n" +
        "followed by `FLIC_FRAME` and `TICKS` when it was printed. The key cases press Space through DOSBox-X\n" +
        $"`AUTOTYPE {KeyAutotype}` (see `dosbox-x.conf`). Read the trace, compare it with `case.json` →\n" +
        "`predictions`, and check `OVCON.TXT` for compile errors. Run `key-control` first: without a `KEY`\n" +
        "line the scripted key did not reach the runtime, and the key cases say nothing.",
        All);

    private static string Indent(string lines) =>
        string.Concat(lines.Split('\n').Select(static l => l.Length == 0 ? "" : "    " + l + "\n"));
}
