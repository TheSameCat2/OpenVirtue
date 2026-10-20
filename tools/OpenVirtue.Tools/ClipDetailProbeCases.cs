// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static ProximityProbeCases;
using static TraceProbeWdl;
using static WallDistanceProbeCases;

/// <summary>
/// The cases of the <c>clip-detail</c> probe
/// (<c>docs/clean-room/observations/OV-20261019-clip-detail.md</c>), the <c>CLIP_DIST</c> questions
/// OV-20261019-clip-contact left open:
/// <list type="bullet">
/// <item><c>anim-gate</c>: outside its border, does a thing's or actor's texture animation stop
/// (its <c>CYCLE</c> frozen), or does it run on with only <c>EACH_CYCLE</c> skipped?</item>
/// <item><c>berkeley-wall</c>: does <c>BERKELEY</c> (p.53) gate a wall's <c>EACH_TICK</c> and
/// <c>IF_NEAR</c> like a thing's?</item>
/// <item><c>wall-dist-clip</c>: is a wall's <c>DIST</c> test (and its pass-through <c>IF_NEAR</c>)
/// gated by <c>CLIP_DIST</c> like a thing's?</item>
/// <item><c>wall-regions</c>: the room has <c>CLIP_DIST 30</c>, the pad 10. Which value gates the
/// <c>EACH_TICK</c> of a wall between the two?</item>
/// </list>
/// </summary>
internal static class ClipDetailProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVCDX0.TXT).</summary>
    public const string TraceFile = "ovcdx";

    private static string Stand(double x, double y, string token, int ticks = 32, bool locate = false) =>
        Beam(x, y, token, locate) + Wait(ticks, token + "S");

    private static string NearFar(string name, string nearToken, string farToken) =>
        Action(name + "n", "    RULE pe_nn = pe_nn + 1;\n    RULE pe_d = MY.DISTANCE;\n" + L(nearToken, "pe_nn", "pe_d", "PLAYER_X", "PLAYER_Y")) +
        Action(name + "f", "    RULE pe_fn = pe_fn + 1;\n    RULE pe_d = MY.DISTANCE;\n" + L(farToken, "pe_fn", "pe_d", "PLAYER_X", "PLAYER_Y"));

    // ---- texture animation outside the border --------------------------------------------------

    /// <summary>
    /// <c>CLIP_DIST 10</c>. A thing at (24, −8) and an actor at (24, 8) with a 4-phase 2-wide texture
    /// (3 ticks a phase, 12 a cycle) and counting <c>EACH_CYCLE</c>s. While <c>cd_p</c> is set the
    /// sampler prints a <c>V</c> line in every cycle with both <c>CYCLE</c>s and counts.
    /// </summary>
    private static readonly string AnimGateDeclarations =
        Skills +
        "SKILL cd_p { VAL 0; }\nSKILL cd_tc { VAL 0; }\nSKILL cd_ac { VAL 0; }\nSKILL cd_tn { VAL 0; }\nSKILL cd_an { VAL 0; }\n" +
        "SYNONYM cd_st { TYPE THING; }\nSYNONYM cd_sa { TYPE ACTOR; }\n" +
        "TEXTURE cd_atex { SCALE_X 32; SCALE_Y 32; CYCLES 4; BMAPS red_map, grn_map, red_map, grn_map; DELAY 3, 3, 3, 3; }\n" +
        Action("cd_tcy", "    RULE cd_tn = cd_tn + 1;\n") +
        Action("cd_acy", "    RULE cd_an = cd_an + 1;\n") +
        "THING cd_t { TEXTURE cd_atex; EACH_CYCLE cd_tcy; }\n" +
        "ACTOR cd_a { TEXTURE cd_atex; EACH_CYCLE cd_acy; }\n" +
        Sampler(
            "    IF (cd_p > 0) {\n        RULE cd_tc = cd_st.CYCLE;\n        RULE cd_ac = cd_sa.CYCLE;\n" +
            Indent(Indent(L("V", "PLAYER_X", "PLAYER_Y", "cd_tc", "cd_tn", "cd_ac", "cd_an"))) + "    }\n");

    /// <summary>
    /// V lines throughout: 48 ticks at the origin (both 32 away), 48 at (20, −8) (4 from the thing),
    /// 48 back at the origin, 48 at (20, 8) (4 from the actor), 48 at the origin.
    /// </summary>
    private static readonly string AnimGateRun =
        "    SET cd_st, cd_t;\n    SET cd_sa, cd_a;\n" + ArmSampler + "    RULE cd_p = 1;\n" + L("GO") +
        Wait(48, "S0") + Stand(20, -8, "T", 48) + Stand(0, 0, "O1", 48) + Stand(20, 8, "A", 48) + Stand(0, 0, "O2", 48) +
        "    RULE cd_p = 0;\n" + ClearSampler;

    private static readonly TraceProbeMap AnimGateMap = new(
        RoomBody: "CLIP_DIST 10;",
        Placements: ["THING cd_t 24 -8 0 0", "ACTOR cd_a 24 8 0 0"]);

    // ---- BERKELEY walls --------------------------------------------------------------------------

    /// <summary>
    /// No <c>CLIP_DIST</c>. Passable free-standing walls at x = 10: a <c>BERKELEY</c> one from y −4 to
    /// 4 with <c>DIST 4</c>, <c>IF_NEAR</c>/<c>IF_FAR</c> (<c>N</c>/<c>F</c>) and a counting
    /// <c>EACH_TICK</c> (<c>B</c>), and a plain one from y 8 to 16 with a counting <c>EACH_TICK</c>
    /// (<c>C</c>).
    /// </summary>
    private static readonly string BerkeleyWallDeclarations =
        Skills + NearFar("bw_", "N", "F") +
        CountingTick("bw_bt", "B", "wb") + CountingTick("bw_ct", "C", "wc") +
        "WALL bw_b { TEXTURE grn_tex; DIST 4; IF_NEAR bw_n; IF_FAR bw_f; EACH_TICK bw_bt; FLAGS PASSABLE, BERKELEY; }\n" +
        "WALL bw_c { TEXTURE red_tex; EACH_TICK bw_ct; FLAGS PASSABLE; }\n" +
        Sampler();

    /// <summary>
    /// S0 facing both walls from the origin (14 from the BERKELEY wall's nearer vertex), S1 turned
    /// away, S2 facing again, S3 at (8, 3) (3 from (10, 4)), S4 a walk +X through the BERKELEY wall
    /// from (6, 0) to (14, 0), S5 back at the origin.
    /// </summary>
    private static readonly string BerkeleyWallRun =
        ArmSampler + L("GO", "PLAYER_ANGLE") + Wait(32, "S0") +
        "    SET PLAYER_ANGLE, 3.1416;\n" + Wait(32, "S1") +
        "    SET PLAYER_ANGLE, 0;\n" + Wait(32, "S2") +
        Stand(8, 3, "S3") +
        Beam(6, 0, "S4") + Walk(0.2, 0, 40, "S4W") + Wait(16, "S4T") +
        Stand(0, 0, "S5") + ClearSampler;

    private static readonly TraceProbeMap BerkeleyWallMap = new(
        Walls: [new TraceProbeWall("bw_b", 10, -4, 10, 4), new TraceProbeWall("bw_c", 10, 8, 10, 16)]);

    // ---- a wall's DIST outside the border ------------------------------------------------------

    /// <summary>
    /// <c>CLIP_DIST 10</c>. A passable wall from (30, −2) to (30, 2) with <c>DIST 20</c>,
    /// <c>IF_NEAR</c>/<c>IF_FAR</c> (<c>N</c>/<c>F</c>) and a counting <c>EACH_TICK</c> (<c>W</c>).
    /// </summary>
    private static readonly string WallDistClipDeclarations =
        Skills + NearFar("wd_", "N", "F") + CountingTick("wd_tick", "W", "wd") +
        "WALL wd_w { TEXTURE grn_tex; DIST 20; IF_NEAR wd_n; IF_FAR wd_f; EACH_TICK wd_tick; FLAGS PASSABLE; }\n" +
        Sampler();

    /// <summary>
    /// Walk +X from the origin to x ≈ 24 (DIST 20 entered at x ≈ 12, CLIP_DIST 10 at x ≈ 22), stand,
    /// walk back to the origin, stand; then beam to (26, 6), walk +X through the wall to x ≈ 34 and back.
    /// </summary>
    private static readonly string WallDistClipRun =
        ArmSampler + L("GO", "PLAYER_X", "PLAYER_Y") +
        Walk(0.2, 0, 120, "W1") + Wait(32, "S1") + Walk(-0.2, 0, 120, "W2") + Wait(32, "S2") +
        Stand(26, 0, "P", 16) + Walk(0.2, 0, 40, "PW") + Wait(16, "PT") + Walk(-0.2, 0, 40, "PB") + Wait(16, "PE") +
        ClearSampler;

    private static readonly TraceProbeMap WallDistClipMap = new(
        RoomBody: "CLIP_DIST 10;",
        Walls: [new TraceProbeWall("wd_w", 30, -2, 30, 2)]);

    // ---- a wall between regions with different CLIP_DISTs -------------------------------------

    /// <summary>
    /// The room has <c>CLIP_DIST 30</c>, the pad 10. The pad's four boundary walls (invisible,
    /// passable) share a counting <c>EACH_TICK</c> (<c>P</c>, with <c>MY.DISTANCE</c> to tell them
    /// apart); a passable room wall from (12, −2) to (12, 2) has its own (<c>R</c>).
    /// </summary>
    private static readonly string WallRegionsDeclarations =
        Skills + CountingTick("wr_pt", "P", "wp") + CountingTick("wr_rt", "R", "wr") +
        "WALL wr_pad { TEXTURE red_tex; EACH_TICK wr_pt; FLAGS INVISIBLE, PASSABLE; }\n" +
        "WALL wr_room { TEXTURE grn_tex; EACH_TICK wr_rt; FLAGS PASSABLE; }\n" +
        Sampler("    RULE pe_h = HERE.SKILL1;\n", "pe_h");

    /// <summary>
    /// Stands with <c>LOCATE</c>: R1 at (−8, 0) in the room (16 from the pad's south wall, 22 from the
    /// room wall), R2 at (−8, 8) (8 from the south wall), P1 in the pad at (−8, 16) (8 or less from
    /// every pad wall), R3 at (8, 0) (24 from the pad's nearest vertex, 6 from the room wall).
    /// </summary>
    private static readonly string WallRegionsRun =
        ArmSampler + Wait(32, "S0") +
        Stand(-8, 0, "R1", locate: true) + Stand(-8, 8, "R2", locate: true) +
        Stand(-8, 16, "P1", locate: true) + Stand(8, 0, "R3", locate: true) + ClearSampler;

    private static readonly TraceProbeMap WallRegionsMap = new(
        RoomBody: "CLIP_DIST 30;\n    SKILL1 1;",
        PadBody: "CLIP_DIST 10;\n    SKILL1 2;",
        PadWall: "wr_pad",
        Walls: [new TraceProbeWall("wr_room", 12, -2, 12, 2)]);

    /// <summary>
    /// Round 3: wall-regions with visible pad walls (wall-regions' were <c>INVISIBLE</c> and never
    /// ran), and a free-standing <c>INVISIBLE</c> passable wall from (12, 6) to (12, 10) with its own
    /// counting <c>EACH_TICK</c> (<c>I</c>).
    /// </summary>
    private static readonly string WallRegionsVisibleDeclarations =
        Skills + CountingTick("wr_pt", "P", "wp") + CountingTick("wr_rt", "R", "wr") + CountingTick("wr_it", "I", "wi") +
        "WALL wr_pad { TEXTURE red_tex; EACH_TICK wr_pt; FLAGS PASSABLE; }\n" +
        "WALL wr_room { TEXTURE grn_tex; EACH_TICK wr_rt; FLAGS PASSABLE; }\n" +
        "WALL wr_inv { TEXTURE grn_tex; EACH_TICK wr_it; FLAGS INVISIBLE, PASSABLE; }\n" +
        Sampler("    RULE pe_h = HERE.SKILL1;\n", "pe_h");

    private static readonly TraceProbeMap WallRegionsVisibleMap = WallRegionsMap with
    {
        Walls = [new TraceProbeWall("wr_room", 12, -2, 12, 2), new TraceProbeWall("wr_inv", 12, 6, 12, 10)],
    };

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "anim-gate",
            "CLIP_DIST 10: outside its border, is a thing's and an actor's texture animation paused (CYCLE frozen) or does it run on with only EACH_CYCLE skipped?",
            AnimGateDeclarations,
            AnimGateRun,
            Predict(
                ("paused", "cd_tc and cd_ac constant in S0, O1S, O2S; cd_tc steps 0…3 every 3 ticks in TS, cd_ac in AS; counts rise only there"),
                ("only EACH_CYCLE skipped", "cd_tc and cd_ac step throughout; cd_tn, cd_an rise only in TS/AS"),
                ("animation needs the object in view", "a visible thing animates even outside its border")),
            Map: AnimGateMap),
        new TraceProbeCase(
            "berkeley-wall",
            "A BERKELEY wall (p.53: it exists only while viewed or within its border distance): does it run its EACH_TICK while seen, only within DIST, and does passing through it fire IF_NEAR?",
            BerkeleyWallDeclarations,
            BerkeleyWallRun,
            Predict(
                ("as BERKELEY things: active only within DIST", "B only in S3 and around S4W's crossing; no N/F"),
                ("active while seen or within DIST", "B in S0, S2, S3, S4; not in S1"),
                ("BERKELEY has no effect on walls", "B throughout, N in S3 and S4W")),
            Map: BerkeleyWallMap),
        new TraceProbeCase(
            "wall-dist-clip",
            "CLIP_DIST 10 and a wall with DIST 20: is the wall's DIST test gated by the border like a thing's (IF_NEAR at x ≈ 22, not 12), and does passing through it still fire IF_NEAR?",
            WallDistClipDeclarations,
            WallDistClipRun,
            Predict(
                ("gated like things", "N once at x ≈ 22 (pe_d < 10), no F on the way back"),
                ("not gated", "N at x ≈ 12 (pe_d ≈ 20), F at x ≈ 12 on the way back"),
                ("W only within the border", "W lines only for x > 22 and in P")),
            Map: WallDistClipMap),
        new TraceProbeCase(
            "wall-regions",
            "The room has CLIP_DIST 30, the pad 10: is the EACH_TICK of a wall between the room and the pad gated by the player's region, by the smaller of its two regions, or by its first region?",
            WallRegionsDeclarations,
            WallRegionsRun,
            Predict(
                ("the smaller of the player's region and both side regions", "R1: R only; R2: P (8), R; P1: P; R3: R only"),
                ("the player's region", "R1: P (16), R (22); R2: P, R; P1: P; R3: P (24), R"),
                ("the wall's first (left) region, the pad", "R1: R only; R2: P, R; P1: P; R3: R")),
            Map: WallRegionsMap),
        new TraceProbeCase(
            "wall-regions-visible",
            "Round 3. wall-regions never ran the pad walls' EACH_TICK, but they were INVISIBLE. With visible passable pad walls, which CLIP_DIST gates them, and does an INVISIBLE free-standing wall run its EACH_TICK at all?",
            WallRegionsVisibleDeclarations,
            WallRegionsRun,
            Predict(
                ("the smaller of the player's region and both side regions", "R1: R only; R2: P (8), R; P1: P; R3: R, I"),
                ("the player's region", "R1: P (16), R; R2: P, R; P1: P; R3: P (24), R, I"),
                ("INVISIBLE walls are inactive", "no I line anywhere")),
            Map: WallRegionsVisibleMap),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "clip-detail",
        TraceFile,
        "ovcdp",
        "After the skill dump, `IF_START` selects the trace file, writes `BEGIN` and runs the variant's case.\n" +
        "Each trace line is a token followed by `TICKS` and `pe_n` (the global EACH_TICK sampler's run count)\n" +
        "when it was printed, then the case's extra skills. Single-letter lines from object EACH_TICKs carry\n" +
        "their run count, MY.DISTANCE and the player position. `V` lines (anim-gate) print every cycle with\n" +
        "the thing's and actor's CYCLE and EACH_CYCLE counts. Compare the trace with `case.json` →\n" +
        "`predictions`, and check `OVCON.TXT` for compile errors.",
        All);
}
