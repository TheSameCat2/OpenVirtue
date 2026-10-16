// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>flic-detail</c> probe
/// (<c>docs/clean-room/observations/OV-20261015-flic-scheduler-detail.md</c>), which settles the open
/// questions of OV-20261015-flic-play-blocking: how <c>WAITT</c> and <c>WAIT</c> count while a flic
/// plays, whether object scheduler actions, region and proximity events and actor movement run
/// during it (the manual, p.82, says movement stops), whether a mouse click reaches <c>IF_LEFT</c>,
/// and what a second or a missing <c>PLAY_FLICFILE</c> does. Every carrier plays
/// <see cref="FlicProbeCases.FlicFile"/> (430 frames at 70 ms). A global <c>EACH_TICK.1</c> sampler
/// counts its own runs in <c>fd_n</c>, so every trace line carries <c>FLIC_FRAME</c>, <c>TICKS</c>
/// and the number of scheduler cycles so far. The control cases run the same script without the
/// flic. Every case clears its slots and hooks before <c>RET</c>, because the carrier ends with
/// <c>WAITT 4; SCREENSHOT; EXIT</c>.
/// </summary>
internal static class FlicDetailProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVFLD0.TXT).</summary>
    public const string TraceFile = "ovfld";

    /// <summary>The second, shorter flic of the <c>replace</c> case (dark red with a white sweep).</summary>
    public const string SecondFlicFile = "PRBFL2.FLC";

    /// <summary>Displayed frames of the second flic: 120 × 70 ms = 8.4 s.</summary>
    public const int SecondFlicFrames = 120;

    /// <summary>The third flic of the <c>speed</c> case: <see cref="FastFlicFrames"/> frames at <see cref="FastFlicSpeedMs"/> ms.</summary>
    public const string FastFlicFile = "PRBFL3.FLC";

    public const int FastFlicFrames = 200;

    public const int FastFlicSpeedMs = 40;

    /// <summary>A flic file name that is not in the carrier.</summary>
    public const string MissingFlicFile = "NOFLIC.FLC";

    /// <summary>When <c>run.sh</c> clicks: 25 s after launch, about 7 s after the probe action starts.</summary>
    public const double ClickSeconds = 25;

    private const string Skills =
        "SKILL fd_n { VAL 0; }\n" +
        "SKILL fd_tnext { VAL 0; }\n" +
        "SKILL fd_fnext { VAL 0; }\n";

    /// <summary>A trace line: the token, then <c>FLIC_FRAME</c>, <c>TICKS</c>, the sampler's run count, then any extra skills.</summary>
    private static string L(string token, params string[] extra) =>
        V(token, ["FLIC_FRAME", "TICKS", "fd_n", .. extra]);

    /// <summary>
    /// The global EACH_TICK sampler: counts its runs, prints <c>TK</c> (with <c>TIME_CORR</c> and the
    /// extra skills) every 32 ticks and <c>FR</c> every <paramref name="frameStep"/> flic frames.
    /// <paramref name="prologue"/> runs first on every cycle. No WAIT (p.96).
    /// </summary>
    private static string Sampler(int frameStep = 50, string prologue = "", params string[] extra) => Action(
        "fd_tick",
        "    RULE fd_n = fd_n + 1;\n" + prologue +
        "    IF (TICKS > fd_tnext) {\n        RULE fd_tnext = TICKS + 32;\n" + Indent(L("TK", ["TIME_CORR", .. extra])) + "    }\n" +
        $"    IF (FLIC_FRAME > fd_fnext) {{\n        RULE fd_fnext = FLIC_FRAME + {frameStep - 1};\n" + Indent(L("FR")) + "    }\n");

    private const string ArmSampler = "    SET EACH_TICK.1, fd_tick;\n";

    private const string ClearSampler = "    SET EACH_TICK.1, NULL;\n";

    private static string Play(string file = FlicProbeCases.FlicFile) =>
        L("PLAY") + $"    PLAY_FLICFILE <{file}>;\n" + L("AFTER");

    // ---- waits -------------------------------------------------------------------------------

    /// <summary>
    /// Waiters for the <c>waits</c> cases. <c>fd_wa</c>/<c>fd_wb</c> are CALLed before
    /// <c>PLAY_FLICFILE</c>, <c>fd_wc</c>/<c>fd_wd</c> right after it in the same frame, and
    /// <c>fd_chain</c> starts its later waits inside the flic, so each of its intervals is measured
    /// wholly within flic frames.
    /// </summary>
    private static readonly string Waiters =
        Action("fd_wa", "    WAITT 32;\n" + L("WA32")) +
        Action("fd_wb", "    WAIT 16;\n" + L("WB16")) +
        Action("fd_wc", "    WAITT 32;\n" + L("WC32")) +
        Action("fd_wd", "    WAIT 16;\n" + L("WD16")) +
        Action(
            "fd_chain",
            "    WAITT 40;\n" + L("S0") +
            "    WAITT 32;\n" + L("S1") +
            "    WAITT 64;\n" + L("S2") +
            "    WAITT 16;\n" + L("S3") +
            "    WAIT 16;\n" + L("S4") +
            "    WAIT 64;\n" + L("S5") +
            "    WAIT 1;\n" + L("S6") +
            "    WAITT 1;\n" + L("S7"));

    private static string WaitsRun(bool flic) =>
        ArmSampler + "    CALL fd_wa;\n    CALL fd_wb;\n" +
        (flic ? Play() : L("PLAY") + L("AFTER")) +
        "    CALL fd_wc;\n    CALL fd_wd;\n    CALL fd_chain;\n" +
        "    WAITT 32;\n" + L("M32") +
        "    WAITT 868;\n" + L("RET") + ClearSampler;

    // ---- world -------------------------------------------------------------------------------

    private const string WorldSkills =
        "SKILL fd_an { VAL 0; }\n" +
        "SKILL fd_cn { VAL 0; }\n" +
        "SKILL fd_rn { VAL 0; }\n" +
        "SKILL fd_nn { VAL 0; }\n" +
        "SKILL fd_anext { VAL 0; }\n" +
        "SKILL fd_ax { VAL 0; }\n" +
        "SKILL fd_ay { VAL 0; }\n";

    /// <summary>
    /// A FOLLOW actor 41 steps from the player, at 0.2 steps per tick (p.65, p.68): moving, it comes
    /// within its DIST of 6 about 175 ticks in, inside the flic. Its texture animates in two 4-tick
    /// phases, so its EACH_CYCLE runs every 8 ticks. Its EACH_TICK counts in <c>fd_an</c>, prints
    /// <c>AT</c> with its position every 32 ticks and points <c>fd_act</c> at itself, so the global
    /// sampler can read the position while object actions might be paused. The room region's
    /// EACH_TICK counts in <c>fd_rn</c>. <c>fd_tele</c> beams the player into the pad island 64
    /// ticks after it is CALLed (the pad's IF_ENTER prints <c>ENTER</c>).
    /// </summary>
    private static readonly string WorldDeclarations =
        Skills + WorldSkills +
        "TEXTURE fd_atex { SCALE_X 4; SCALE_Y 4; CYCLES 2; BMAPS red_map, grn_map; DELAY 4, 4; }\n" +
        "SYNONYM fd_act { TYPE ACTOR; }\n" +
        "ACTOR fd_actor { TEXTURE fd_atex; TARGET FOLLOW; SPEED 0.2; DIST 6; IF_NEAR fd_near; EACH_TICK fd_atick; EACH_CYCLE fd_acyc; FLAGS LIBER; }\n" +
        Action(
            "fd_atick",
            "    RULE fd_an = fd_an + 1;\n    SET fd_act, MY;\n" +
            "    IF (TICKS > fd_anext) {\n        RULE fd_anext = TICKS + 32;\n        RULE fd_ax = MY.X;\n        RULE fd_ay = MY.Y;\n" +
            Indent(L("AT", "fd_ax", "fd_ay")) + "    }\n") +
        Action("fd_acyc", "    RULE fd_cn = fd_cn + 1;\n") +
        Action("fd_rtick", "    RULE fd_rn = fd_rn + 1;\n") +
        Action("fd_near", "    RULE fd_nn = fd_nn + 1;\n    RULE fd_ax = MY.X;\n    RULE fd_ay = MY.Y;\n" + L("NEAR", "fd_nn", "fd_ax", "fd_ay", "PLAYER_X", "PLAYER_Y")) +
        Action("fd_enter", L("ENTER", "PLAYER_X", "PLAYER_Y")) +
        Action("fd_leave", L("LEAVE", "PLAYER_X", "PLAYER_Y")) +
        Action(
            "fd_tele",
            "    WAITT 64;\n" + L("TELE", "PLAYER_X", "PLAYER_Y") +
            "    SET PLAYER_X, -8;\n    SET PLAYER_Y, 16;\n" + L("TELED", "PLAYER_X", "PLAYER_Y")) +
        Sampler(
            50,
            "    RULE fd_ax = fd_act.X;\n    RULE fd_ay = fd_act.Y;\n",
            "fd_an", "fd_cn", "fd_rn", "fd_ax", "fd_ay", "PLAYER_X", "PLAYER_Y");

    private static readonly TraceProbeMap WorldMap = new(
        RoomBody: "EACH_TICK fd_rtick;",
        PadBody: "IF_ENTER fd_enter;\n    IF_LEAVE fd_leave;",
        Placements: ["ACTOR fd_actor 40 -12 180 0"]);

    private static string WorldRun(bool flic) =>
        ArmSampler + (flic ? Play() : L("PLAY") + L("AFTER")) + "    CALL fd_tele;\n" +
        "    WAITT 800;\n" + L("RET", "fd_an", "fd_cn", "fd_rn", "fd_nn") + ClearSampler;

    // ---- walk and near -----------------------------------------------------------------------

    /// <summary>
    /// The player walks from the start toward the pad: the sampler sets <c>PLAYER_VX</c> −0.1 and
    /// <c>PLAYER_VY</c> 0.2 steps per tick (p.104) every cycle, so a moving player crosses into the
    /// pad (y = 12) about 60 ticks in and out of it (y = 20) about 100 ticks in. The pad's
    /// IF_ENTER/IF_LEAVE print <c>ENTER</c>/<c>LEAVE</c>.
    /// </summary>
    private static readonly string WalkDeclarations =
        Skills + WorldSkills +
        Action("fd_enter", L("ENTER", "PLAYER_X", "PLAYER_Y")) +
        Action("fd_leave", L("LEAVE", "PLAYER_X", "PLAYER_Y")) +
        Sampler(50, "    SET PLAYER_VX, -0.1;\n    SET PLAYER_VY, 0.2;\n", "PLAYER_X", "PLAYER_Y");

    private static readonly TraceProbeMap WalkMap = new(PadBody: "IF_ENTER fd_enter;\n    IF_LEAVE fd_leave;");

    private static string WalkRun(bool flic) =>
        (flic ? Play() : L("PLAY") + L("AFTER")) + ArmSampler +
        "    WAITT 700;\n" + L("RET", "PLAYER_X", "PLAYER_Y") + ClearSampler;

    /// <summary>
    /// A THING with DIST 6 at (24, −12); <c>fd_beam</c> beams the player to (24, −8), 4 steps from
    /// it, 64 ticks after it is CALLed. The thing's IF_NEAR counts in <c>fd_nn</c> and prints the
    /// first three runs, then every 50th.
    /// </summary>
    private static readonly string NearDeclarations =
        Skills + WorldSkills +
        "THING fd_post { TEXTURE red_tex; DIST 6; IF_NEAR fd_pnear; IF_FAR fd_pfar; }\n" +
        Action(
            "fd_pnear",
            "    RULE fd_nn = fd_nn + 1;\n" +
            "    IF (fd_nn < 4) {\n" + Indent(L("NEAR", "fd_nn")) + "    }\n" +
            "    IF (fd_nn > fd_anext) {\n        RULE fd_anext = fd_nn + 49;\n" + Indent(L("NEARN", "fd_nn")) + "    }\n") +
        Action("fd_pfar", L("FAR", "fd_nn")) +
        Action(
            "fd_beam",
            "    WAITT 64;\n" + L("BEAM", "PLAYER_X", "PLAYER_Y") +
            "    SET PLAYER_X, 24;\n    SET PLAYER_Y, -8;\n" + L("BEAMED", "PLAYER_X", "PLAYER_Y")) +
        Sampler(50, "", "fd_nn", "PLAYER_X", "PLAYER_Y");

    private static readonly TraceProbeMap NearMap = new(Placements: ["THING fd_post 24 -12 0 0"]);

    private static string NearRun(bool flic) =>
        ArmSampler + "    RULE fd_anext = 3;\n" + (flic ? Play() : L("PLAY") + L("AFTER")) + "    CALL fd_beam;\n" +
        "    WAITT 700;\n" + L("RET", "fd_nn") + ClearSampler;

    // ---- mouse -------------------------------------------------------------------------------

    private static readonly string LeftAction = Action("fd_left", L("LEFT", "MOUSE_X", "MOUSE_Y"));

    // ---- second and missing flic -------------------------------------------------------------

    private static readonly string ReplaceDeclarations =
        Skills + Sampler(25) +
        Action(
            "fd_rep",
            "    WAITT 48;\n" + L("P2") + $"    PLAY_FLICFILE <{SecondFlicFile}>;\n" + L("P2A") +
            "    WAITT 8;\n    SCREENSHOT \"ovrpl\", 0;\n" + L("SHOT"));

    /// <summary>
    /// Writes out the trace so far: selecting another PRINTFILE closes the trace file, and
    /// re-selecting it appends (OV-20261009-shoot-mapping). A first <c>missing</c> run crashed the
    /// emulated CPU and lost the buffered trace.
    /// </summary>
    private const string Flush = "    PRINTFILE \"ovfsh\", 0;\n    PRINTFILE \"" + TraceFile + "\", 0;\n";

    private static readonly string MissingRun =
        ArmSampler + "    WAITT 8;\n" + L("PLAY") + Flush + $"    PLAY_FLICFILE <{MissingFlicFile}>;\n" + L("AFTER") + Flush +
        "    WAITT 32;\n" + L("W32") + "    WAITT 600;\n" + L("RET") + ClearSampler;

    private static readonly string MissingDuringRun =
        ArmSampler + Play() + "    WAITT 48;\n" + L("P2") + Flush + $"    PLAY_FLICFILE <{MissingFlicFile}>;\n" + L("P2A") + Flush +
        "    WAITT 32;\n" + L("W32") + Flush + "    WAITT 600;\n" + L("RET") + ClearSampler;

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "waits-control",
            "Baseline without a flic: when do WAITT 32/WAIT 16 armed before and after the PLAY point, and each wait of the chain, resume in TICKS and in scheduler cycles (fd_n)?",
            Skills + Sampler() + Waiters,
            WaitsRun(flic: false),
            Predict(
                ("WAITT counts TICKS", "WA32 and WC32 about 32 ticks after PLAY (to the next world frame); S1-S3 32, 64, 16 ticks apart"),
                ("WAIT counts scheduler cycles", "WB16/WD16 16 fd_n after PLAY; S4-S6 16, 64, 1 fd_n apart"))),
        new TraceProbeCase(
            "waits",
            "During the flic: do WAITT intervals match TICKS or run fast (the first probe saw WAITT 32 resume after 24.2 ticks); does WAIT count flic frames; does it matter whether the wait was armed before or after PLAY_FLICFILE?",
            Skills + Sampler() + Waiters,
            WaitsRun(flic: true),
            Predict(
                ("WAITT counts TICKS during the flic", "S1-S3 with TICKS gaps 32, 64, 16 (about 29, 58, 15 flic frames)"),
                ("WAITT runs fast during the flic", "S1-S3 TICKS gaps clearly below 32, 64, 16, in a fixed ratio"),
                ("the first flic frame carries the last world frame's time", "WA32/WC32 earlier than S1's gap would give, by about one world frame (8 ticks)"),
                ("WAIT counts flic frames", "S4-S6 16, 64, 1 flic frames (and fd_n) apart"),
                ("WAIT frozen during the flic", "S4 only after the flic ends"),
                ("armed before vs after PLAY", "WA32 vs WC32 and WB16 vs WD16 at the same or different lines"))),
        new TraceProbeCase(
            "world-control",
            "Baseline without a flic: actor EACH_TICK/EACH_CYCLE and region EACH_TICK counters, FOLLOW movement and IF_NEAR, and whether beaming the player with SET PLAYER_X/PLAYER_Y fires the pad's IF_ENTER.",
            WorldDeclarations,
            WorldRun(flic: false),
            Predict(
                ("object and region actions run each cycle", "TK lines with fd_an, fd_rn rising like fd_n; fd_cn about one per 8 ticks"),
                ("FOLLOW moves the actor", "AT/TK fd_ax, fd_ay closing on the player at about 0.2 steps per tick"),
                ("IF_NEAR fires", "NEAR when the actor is within about 6 steps of the player"),
                ("beaming fires IF_ENTER", "ENTER after TELED, in the same or the next cycle"),
                ("beaming does not fire IF_ENTER", "TELED, then no ENTER")),
            Map: WorldMap),
        new TraceProbeCase(
            "world",
            "During the flic: do the actor's EACH_TICK/EACH_CYCLE and the region's EACH_TICK run, does the FOLLOW actor move, does IF_NEAR fire, and does a beam into the pad fire IF_ENTER during the flic or after it?",
            WorldDeclarations,
            WorldRun(flic: true),
            Predict(
                ("object and region actions paused", "TK lines inside the flic with fd_n rising and fd_an, fd_cn, fd_rn constant; no AT line between AFTER and the end"),
                ("object and region actions run", "fd_an, fd_rn rise with fd_n during the flic; AT lines inside it"),
                ("movement stops (p.82)", "fd_ax, fd_ay constant from AFTER to the end of the flic, then closing again"),
                ("movement goes on", "fd_ax, fd_ay closing during the flic; NEAR inside it"),
                ("region events wait for the end", "TELED inside the flic, ENTER only after FLIC_FRAME is 0"),
                ("region events during the flic", "ENTER inside the flic")),
            Map: WorldMap),
        new TraceProbeCase(
            "walk-control",
            "Baseline without a flic: does a player moved by PLAYER_VX/PLAYER_VY walk into the pad and fire its IF_ENTER and IF_LEAVE?",
            WalkDeclarations,
            WalkRun(flic: false),
            Predict(
                ("the player walks", "TK PLAYER_X, PLAYER_Y moving by about (-0.1, 0.2) per tick"),
                ("walking fires the region events", "ENTER near y = 12, LEAVE near y = 20")),
            Map: WalkMap),
        new TraceProbeCase(
            "walk",
            "During the flic, with the velocity set every cycle: does the player move, and do IF_ENTER/IF_LEAVE fire during the flic or only after it?",
            WalkDeclarations,
            WalkRun(flic: true),
            Predict(
                ("player movement stops (p.82)", "PLAYER_X, PLAYER_Y constant while FLIC_FRAME > 0; ENTER/LEAVE only after the end"),
                ("the player moves", "PLAYER_X, PLAYER_Y change during the flic; ENTER inside it")),
            Map: WalkMap),
        new TraceProbeCase(
            "near-control",
            "Baseline without a flic: does beaming the player within a THING's DIST fire its IF_NEAR, once or every cycle?",
            NearDeclarations,
            NearRun(flic: false),
            Predict(
                ("beaming fires IF_NEAR", "NEAR after BEAMED"),
                ("IF_NEAR repeats while near", "NEAR 1-3 in consecutive cycles, NEARN every 50"),
                ("beaming does not fire IF_NEAR", "BEAMED, then no NEAR")),
            Map: NearMap),
        new TraceProbeCase(
            "near",
            "During the flic, with the player beamed within the THING's DIST: does IF_NEAR fire during the flic or only after it?",
            NearDeclarations,
            NearRun(flic: true),
            Predict(
                ("proximity events wait for the end", "BEAMED inside the flic, the first NEAR with FLIC_FRAME 0"),
                ("proximity events during the flic", "NEAR with FLIC_FRAME > 0")),
            Map: NearMap),
        new TraceProbeCase(
            "mouse-control",
            "Harness control, no flic: does the host-side X11 click reach IF_LEFT? AUTOTYPE -list records which button names DOSBox-X can type.",
            Skills + LeftAction,
            L("ARM") + "    SET IF_LEFT, fd_left;\n    WAITT 560;\n" + L("RET") + "    SET IF_LEFT, NULL;\n",
            Predict(
                ("the click reaches IF_LEFT", "ARM, LEFT, RET"),
                ("the click did not reach the runtime (harness, not a finding)", "ARM, RET; check click-*.log")),
            Autotype: "-list > OVATL.TXT",
            ClickAfterSeconds: ClickSeconds),
        new TraceProbeCase(
            "mouse",
            "With IF_LEFT armed before PLAY_FLICFILE: does a left click during the flic run the hook?",
            Skills + Sampler() + LeftAction,
            ArmSampler + "    SET IF_LEFT, fd_left;\n" + Play() + "    WAITT 560;\n" + L("RET") + ClearSampler + "    SET IF_LEFT, NULL;\n",
            Predict(
                ("clicks are delivered during the flic", "LEFT with 0 < FLIC_FRAME < 430"),
                ("clicks are dropped during the flic", "no LEFT line"),
                ("clicks are held until the flic ends", "LEFT with FLIC_FRAME 0 right after the end")),
            ClickAfterSeconds: ClickSeconds),
        new TraceProbeCase(
            "replace",
            "A second PLAY_FLICFILE (a 120-frame flic) while the first plays: does it replace the first, get ignored, or queue behind it?",
            ReplaceDeclarations,
            ArmSampler + Play() + "    CALL fd_rep;\n    WAITT 700;\n" + L("RET") + ClearSampler,
            Predict(
                ("replaces", "P2A with FLIC_FRAME 1; FR lines count 1..120 again; FLIC_FRAME 0 about 120 frames after P2; OVRPL0.PCX dark red"),
                ("ignored", "FLIC_FRAME goes on from P2 to 430; OVRPL0.PCX blue"),
                ("queued", "FLIC_FRAME runs to 430, then counts 1..120 again before 0"))),
        new TraceProbeCase(
            "speed",
            $"A {FastFlicFrames}-frame flic at {FastFlicSpeedMs} ms per frame: is TIME_CORR during a flic the frame delay in 64 ms units (0.625), in 62.5 ms ticks (0.64), or the measured frame time?",
            Skills + Sampler(),
            ArmSampler + Play(FastFlicFile) + "    WAITT 400;\n" + L("RET") + ClearSampler,
            Predict(
                ("delay / 64 ms", "TK lines during the flic with TIME_CORR 0.625 and TICKS 0.625 per frame"),
                ("delay / 62.5 ms", "TIME_CORR 0.640"),
                ("one scheduler cycle per flic frame", "fd_n rising with FLIC_FRAME"))),
        new TraceProbeCase(
            "missing",
            "PLAY_FLICFILE of a missing file with no flic playing: FLIC_FRAME afterwards, and does the action go on?",
            Skills + Sampler(),
            MissingRun,
            Predict(
                ("a missing file is skipped", "AFTER with FLIC_FRAME 0, then W32 and the rest"),
                ("a missing file is fatal", "trace ends at PLAY; an error in OVCON.TXT or an emulator fault; no OVEND"))),
        new TraceProbeCase(
            "missing-during",
            "PLAY_FLICFILE of a missing file while a flic plays: does the action go on, and does the playing flic stop?",
            Skills + Sampler(),
            MissingDuringRun,
            Predict(
                ("a missing file leaves the playing flic alone", "P2A and later FR lines with FLIC_FRAME still rising"),
                ("a missing file stops the playing flic", "FLIC_FRAME 0 from P2A or the next cycle on"),
                ("a missing file is fatal", "trace ends at P2; no OVEND"))),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "flic-detail",
        TraceFile,
        "ovfds",
        "After the skill dump, `IF_START` selects the trace file, writes `BEGIN` and runs the variant's case.\n" +
        $"Every carrier holds `{FlicProbeCases.FlicFile}` ({FlicProbeCases.FlicFrames} frames at {FlicProbeCases.FlicSpeedMs} ms, blue with a yellow sweep)\n" +
        $"and `{SecondFlicFile}` ({SecondFlicFrames} frames, dark red with a white sweep). Each trace line is a token followed by\n" +
        "`FLIC_FRAME`, `TICKS` and `fd_n` (the global EACH_TICK sampler's run count) when it was printed, then the\n" +
        "case's extra skills. The `world` cases add a FOLLOW actor and a pad region (see `TITLE.WMP`). The mouse\n" +
        $"cases send a left click {ClickSeconds} s after launch through `x11_click.py` (see `click-*.log`). Read the\n" +
        "trace, compare it with `case.json` → `predictions`, and check `OVCON.TXT` for compile errors. Run the\n" +
        "control cases first.",
        All);

    private static string Indent(string lines) =>
        string.Concat(lines.Split('\n').Select(static l => l.Length == 0 ? "" : "    " + l + "\n"));
}
