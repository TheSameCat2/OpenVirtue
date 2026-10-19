// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>proximity-events</c> probe
/// (<c>docs/clean-room/observations/OV-20261019-proximity-events.md</c>). The no-flic controls of
/// OV-20261015-flic-scheduler-detail showed that beaming the player with <c>SET PLAYER_X/PLAYER_Y</c>
/// does not fire <c>IF_ENTER</c> while walking does, and that a FOLLOW actor's <c>IF_NEAR</c> ran in
/// 307 consecutive cycles while it pushed the player, but not while it sat on a player that no
/// longer moved. This probe asks when <c>IF_NEAR</c>/<c>IF_FAR</c> run for a player who walks
/// through, stands within, is beamed into and out of <c>DIST</c>, and for an actor that approaches a
/// player who stands still; and whether region events follow a beam later, on the next walk, or
/// after <c>LOCATE</c> (p.89; the manual's <c>HERE</c> note, p.99, says a beam can leave the region
/// stale). A global <c>EACH_TICK.1</c> sampler counts its runs in <c>pe_n</c> and sets
/// <c>PLAYER_VX/PLAYER_VY</c> to <c>pe_vx/pe_vy</c> in every cycle (p.111), so the run action walks
/// the player by setting those two skills. Every trace line is a token followed by <c>TICKS</c> and
/// <c>pe_n</c> when it was printed, then the case's extra skills. The proximity objects use a 2-step
/// wide texture, so the player touches them only within 2.2 steps (half the width plus
/// <c>PLAYER_WIDTH</c>, OV-20261015-flic-scheduler-detail), well inside their <c>DIST</c> of 10.
/// </summary>
internal static class ProximityProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVPRX0.TXT).</summary>
    public const string TraceFile = "ovprx";

    internal const string Skills =
        "SKILL pe_n { VAL 0; }\n" +
        "SKILL pe_tnext { VAL 0; }\n" +
        "SKILL pe_vx { VAL 0; }\n" +
        "SKILL pe_vy { VAL 0; }\n" +
        "SKILL pe_nn { VAL 0; }\n" +
        "SKILL pe_fn { VAL 0; }\n" +
        "SKILL pe_d { VAL 0; }\n" +
        "SKILL pe_h { VAL 0; }\n";

    /// <summary>A trace line: the token, then <c>TICKS</c>, the sampler's run count, then any extra skills.</summary>
    internal static string L(string token, params string[] extra) =>
        V(token, ["TICKS", "pe_n", .. extra]);

    /// <summary>
    /// The global EACH_TICK sampler: counts its runs, sets the player's velocity from
    /// <c>pe_vx/pe_vy</c>, and prints <c>TK</c> with the player position (and the extra skills)
    /// every 16 ticks. <paramref name="prologue"/> runs first on every cycle. No WAIT (p.96).
    /// </summary>
    internal static string Sampler(string prologue = "", params string[] extra) => Action(
        "pe_tick",
        "    RULE pe_n = pe_n + 1;\n" + prologue +
        "    SET PLAYER_VX, pe_vx;\n    SET PLAYER_VY, pe_vy;\n" +
        "    IF (TICKS > pe_tnext) {\n        RULE pe_tnext = TICKS + 16;\n" +
        Indent(L("TK", ["PLAYER_X", "PLAYER_Y", "pe_nn", "pe_fn", .. extra])) + "    }\n");

    internal const string ArmSampler = "    SET EACH_TICK.1, pe_tick;\n";

    internal const string ClearSampler =
        "    RULE pe_vx = 0;\n    RULE pe_vy = 0;\n    WAITT 2;\n    SET EACH_TICK.1, NULL;\n";

    /// <summary>Walks the player at (<paramref name="vx"/>, <paramref name="vy"/>) steps per tick for <paramref name="ticks"/> ticks, then stops and prints <paramref name="token"/>.</summary>
    internal static string Walk(double vx, double vy, int ticks, string token) =>
        FormattableString.Invariant($"    RULE pe_vx = {vx};\n    RULE pe_vy = {vy};\n    WAITT {ticks};\n") +
        "    RULE pe_vx = 0;\n    RULE pe_vy = 0;\n" + L(token, "PLAYER_X", "PLAYER_Y");

    /// <summary>Sets the player position directly (a beam), printing <paramref name="token"/> before and <paramref name="token"/>D after.</summary>
    internal static string Beam(int x, int y, string token, bool locate = false) =>
        L(token, "PLAYER_X", "PLAYER_Y") +
        FormattableString.Invariant($"    SET PLAYER_X, {x};\n    SET PLAYER_Y, {y};\n") +
        (locate ? "    LOCATE;\n" : "") +
        L(token + (locate ? "L" : "D"), "PLAYER_X", "PLAYER_Y");

    internal static string Wait(int ticks, string token) => $"    WAITT {ticks};\n" + L(token, "PLAYER_X", "PLAYER_Y");

    // ---- proximity ---------------------------------------------------------------------------

    /// <summary>
    /// <c>IF_NEAR</c>/<c>IF_FAR</c> actions that count in <c>pe_nn</c>/<c>pe_fn</c> and print every
    /// run with <c>MY.DISTANCE</c> and the player position, so consecutive <c>pe_n</c> values show
    /// whether a hook repeats in every cycle.
    /// </summary>
    private static readonly string ProximityActions =
        "TEXTURE pe_stex { SCALE_X 32; SCALE_Y 32; BMAPS red_map; }\n" +
        Action("pe_near", "    RULE pe_nn = pe_nn + 1;\n    RULE pe_d = MY.DISTANCE;\n" + L("N", "pe_nn", "pe_d", "PLAYER_X", "PLAYER_Y")) +
        Action("pe_far", "    RULE pe_fn = pe_fn + 1;\n    RULE pe_d = MY.DISTANCE;\n" + L("F", "pe_fn", "pe_d", "PLAYER_X", "PLAYER_Y"));

    /// <summary>A thing 6 steps left of the player's +X line at x = 16: within its DIST of 10 for x in 8…24 on that line.</summary>
    private static readonly string ThingDeclarations =
        Skills + ProximityActions +
        "THING pe_post { TEXTURE pe_stex; DIST 10; IF_NEAR pe_near; IF_FAR pe_far; }\n" +
        Sampler();

    private static readonly TraceProbeMap ThingMap = new(Placements: ["THING pe_post 16 6 0 0"]);

    /// <summary>
    /// Walk along +X at 0.2 steps per tick from x = 0 to 36 (inside DIST for x 8…24, ticks 40…120),
    /// stand outside, walk back to x = 16 (6 steps from the thing) and stand there.
    /// </summary>
    private static readonly string NearWalkRun =
        ArmSampler + L("GO", "PLAYER_X", "PLAYER_Y") +
        Walk(0.2, 0, 180, "STOP") +
        Wait(64, "OUT") +
        Walk(-0.2, 0, 100, "STOP2") +
        Wait(200, "STILL") +
        Walk(0.02, 0, 32, "CREPT") +
        Wait(64, "RET") + ClearSampler;

    /// <summary>
    /// Beam into DIST (6 steps from the thing, no contact) and stand; beam out (20.9 steps) and
    /// stand; beam back in and stand; then creep 0.6 steps (still inside DIST); then walk out.
    /// </summary>
    private static readonly string NearBeamRun =
        ArmSampler + "    WAITT 16;\n" +
        Beam(16, 0, "B1") + Wait(100, "S1") +
        Beam(36, 0, "B2") + Wait(64, "S2") +
        Beam(16, 0, "B3") + Wait(64, "S3") +
        Walk(0.02, 0, 32, "CREPT") + Wait(32, "S4") +
        Walk(0.2, 0, 80, "WOUT") + Wait(32, "RET") + ClearSampler;

    /// <summary>
    /// A FOLLOW actor with the small texture and DIST 10 starts 40 steps ahead of a player who
    /// stands still; at 0.2 steps per tick it reaches DIST about 150 ticks in and contact about
    /// 190 ticks in.
    /// </summary>
    private static readonly string ActorDeclarations =
        Skills + ProximityActions +
        "ACTOR pe_actor { TEXTURE pe_stex; TARGET FOLLOW; SPEED 0.2; DIST 10; IF_NEAR pe_near; IF_FAR pe_far; }\n" +
        Sampler();

    private static readonly TraceProbeMap ActorMap = new(Placements: ["ACTOR pe_actor 40 0 180 0"]);

    private static readonly string NearActorRun =
        ArmSampler + Wait(320, "STAY") +
        Walk(-0.2, 0, 40, "BACK") + Wait(80, "RET") + ClearSampler;

    // ---- regions -----------------------------------------------------------------------------

    /// <summary>
    /// Region events of the room and the pad print the player position and <c>HERE.SKILL1</c>
    /// (room 1, pad 2), the region the runtime holds for the player; the sampler prints it too.
    /// </summary>
    private static readonly string RegionDeclarations =
        Skills +
        Action("pe_enter", "    RULE pe_h = HERE.SKILL1;\n" + L("ENTER", "PLAYER_X", "PLAYER_Y", "pe_h")) +
        Action("pe_leave", "    RULE pe_h = HERE.SKILL1;\n" + L("LEAVE", "PLAYER_X", "PLAYER_Y", "pe_h")) +
        Action("pe_renter", "    RULE pe_h = HERE.SKILL1;\n" + L("RENTER", "PLAYER_X", "PLAYER_Y", "pe_h")) +
        Action("pe_rleave", "    RULE pe_h = HERE.SKILL1;\n" + L("RLEAVE", "PLAYER_X", "PLAYER_Y", "pe_h")) +
        Sampler("    RULE pe_h = HERE.SKILL1;\n", "pe_h");

    private static readonly TraceProbeMap RegionMap = new(
        RoomBody: "SKILL1 1;\n    IF_ENTER pe_renter;\n    IF_LEAVE pe_rleave;",
        PadBody: "SKILL1 2;\n    IF_ENTER pe_enter;\n    IF_LEAVE pe_leave;");

    /// <summary>
    /// Beam into the pad (x −12…−4, y 12…20) without LOCATE, creep inside it, walk out across
    /// x = −4 to x ≈ 1.6, then walk back into it.
    /// </summary>
    private static readonly string RegionBeamWalkRun =
        ArmSampler + "    WAITT 16;\n" +
        Beam(-8, 16, "B1") + Wait(32, "S1") +
        Walk(0.05, 0, 32, "W1") + Wait(32, "S2") +
        Walk(0.2, 0, 40, "W2") + Wait(32, "S3") +
        Walk(-0.2, 0, 40, "W3") + Wait(32, "RET") + ClearSampler;

    /// <summary>
    /// Beam into the pad with LOCATE, beam out with LOCATE, then beam in without LOCATE and LOCATE
    /// 32 ticks later.
    /// </summary>
    private static readonly string RegionLocateRun =
        ArmSampler + "    WAITT 16;\n" +
        Beam(-8, 16, "B1", locate: true) + Wait(32, "S1") +
        Beam(0, 0, "B2", locate: true) + Wait(32, "S2") +
        Beam(-8, 16, "B3") + Wait(32, "S3") +
        "    LOCATE;\n" + L("L3", "PLAYER_X", "PLAYER_Y") + Wait(32, "RET") + ClearSampler;

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "near-walk",
            "A player walks past a THING (DIST 10) without touching it, stands outside, walks back within DIST and stands there, then creeps: when do IF_NEAR and IF_FAR run?",
            ThingDeclarations,
            NearWalkRun,
            Predict(
                ("IF_NEAR on the far-to-near edge", "one N near x = 8 (pe_d about 10), no N while standing at x = 16"),
                ("IF_NEAR in every cycle within DIST", "N in consecutive pe_n from x = 8 to 24 and on every cycle of STILL"),
                ("IF_NEAR in every moving cycle within DIST", "N in consecutive pe_n from x = 8 to 24 and on the way back, none during STILL, again during CREPT"),
                ("IF_FAR on the near-to-far edge", "one F near x = 24 (only the first walk leaves DIST)"),
                ("IF_FAR in every moving cycle outside DIST", "F in consecutive pe_n beyond x = 24")),
            Map: ThingMap),
        new TraceProbeCase(
            "near-beam",
            "The player is beamed within a THING's DIST without touching it, beamed out, beamed back, then creeps and walks out: do beams fire IF_NEAR/IF_FAR, and does standing still repeat them?",
            ThingDeclarations,
            NearBeamRun,
            Predict(
                ("beams are checked", "N after B1D and B3D, F after B2D (same or next cycle)"),
                ("beams are not checked, movement is", "no N/F around B1-B3; N during CREPT; F during WOUT"),
                ("standing still repeats IF_NEAR", "N on every cycle of S1 and S3")),
            Map: ThingMap),
        new TraceProbeCase(
            "near-actor",
            "A FOLLOW actor (DIST 10, small texture) approaches a player who stands still, then the player backs off: does actor movement fire IF_NEAR at DIST, at contact, once or every cycle?",
            ActorDeclarations,
            NearActorRun,
            Predict(
                ("actor movement is checked at DIST", "first N with pe_d about 10 while the player is at (0, 0)"),
                ("only contact fires", "first N with pe_d about 2.2"),
                ("only player movement is checked", "no N until BACK's walk (or a push) moves the player")),
            Map: ActorMap),
        new TraceProbeCase(
            "region-beam-walk",
            "After a beam into the pad without LOCATE: does creeping inside the pad fire its IF_ENTER, and what do walking out and back in fire? HERE.SKILL1 shows the region the runtime holds (room 1, pad 2).",
            RegionDeclarations,
            RegionBeamWalkRun,
            Predict(
                ("a beam is not a region change; HERE stays", "B1D with pe_h 1; no ENTER during S1"),
                ("the next movement finds the new region", "ENTER during W1 with pe_h 2 (and RLEAVE)"),
                ("only wall crossings change the region", "no ENTER during W1; at W2 a crossing of the pad wall, read from the stale region"),
                ("walking in and out fires both regions' events", "W2: LEAVE + RENTER; W3: RLEAVE + ENTER")),
            Map: RegionMap),
        new TraceProbeCase(
            "region-locate",
            "A beam into or out of the pad followed by LOCATE (p.89), and a LOCATE 32 ticks after a beam: does LOCATE fire IF_ENTER/IF_LEAVE?",
            RegionDeclarations,
            RegionLocateRun,
            Predict(
                ("LOCATE fires the region events", "ENTER (and RLEAVE) after B1L; LEAVE + RENTER after B2L; ENTER after L3"),
                ("LOCATE only reassigns HERE", "pe_h 2 after B1L, 1 after B2L, 2 after L3; no ENTER/LEAVE lines")),
            Map: RegionMap),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "proximity-events",
        TraceFile,
        "ovprs",
        "After the skill dump, `IF_START` selects the trace file, writes `BEGIN` and runs the variant's case.\n" +
        "Each trace line is a token followed by `TICKS` and `pe_n` (the global EACH_TICK sampler's run count)\n" +
        "when it was printed, then the case's extra skills. `N`/`F` are IF_NEAR/IF_FAR runs (count, MY.DISTANCE,\n" +
        "player position). The region cases add the pad island (see `TITLE.WMP`); `pe_h` is HERE.SKILL1\n" +
        "(room 1, pad 2). Read the trace, compare it with `case.json` → `predictions`, and check `OVCON.TXT`\n" +
        "for compile errors.",
        All);

    internal static string Indent(string lines) =>
        string.Concat(lines.Split('\n').Select(static l => l.Length == 0 ? "" : "    " + l + "\n"));
}
