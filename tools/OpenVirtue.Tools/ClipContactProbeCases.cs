// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static ProximityProbeCases;
using static TraceProbeWdl;
using static WallDistanceProbeCases;

/// <summary>
/// The cases of the <c>clip-contact</c> probe
/// (<c>docs/clean-room/observations/OV-20261019-clip-contact.md</c>), the questions
/// OV-20261019-wall-pass-clip left open:
/// <list type="bullet">
/// <item><c>border-*</c>: the pad island's four boundary walls carry <c>IF_NEAR</c>/<c>IF_FAR</c>
/// (<c>DIST 0</c>). Does walking through a passable wall between two regions fire it, and does
/// pushing against a wall between two regions that the player cannot cross?</item>
/// <item><c>liber</c>, <c>berkeley</c>: do the <c>LIBER</c> and <c>BERKELEY</c> flags (p.71) change
/// when a thing's or actor's <c>EACH_TICK</c> runs?</item>
/// <item><c>clip-near</c>: are a thing's and an actor's <c>IF_NEAR</c>/<c>IF_FAR</c> and
/// <c>EACH_CYCLE</c> gated by <c>CLIP_DIST</c> like <c>EACH_TICK</c> (p.69)?</item>
/// <item><c>clip-move*</c>: does a FOLLOW or WAY actor stop moving outside <c>CLIP_DIST</c>?</item>
/// <item><c>contact-*</c>: does pushing against a solid thing or actor fire its <c>IF_NEAR</c> in
/// every cycle, as for a solid wall?</item>
/// <item><c>clip-regions</c>: the room and the pad have different <c>CLIP_DIST</c>s; whose gates?</item>
/// </list>
/// Walks and beams come from the proximity-events probe; the counting <c>EACH_TICK</c> from
/// wall-pass-clip prints its token at most every 2 ticks with the run count, <c>MY.DISTANCE</c>
/// and the player position.
/// </summary>
internal static class ClipContactProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVCCX0.TXT).</summary>
    public const string TraceFile = "ovccx";

    /// <summary>A 2-step wide sprite texture (contact within about 2.2 steps of the centre).</summary>
    private const string SmallTexture = "TEXTURE cx_stex { SCALE_X 32; SCALE_Y 32; BMAPS red_map; }\n";

    /// <summary>
    /// <c>IF_NEAR</c>/<c>IF_FAR</c> actions <c>&lt;name&gt;n</c>/<c>&lt;name&gt;f</c> that count in
    /// <c>pe_nn</c>/<c>pe_fn</c> and print every run with <c>MY.DISTANCE</c> and the player position.
    /// </summary>
    private static string NearFar(string name, string nearToken, string farToken) =>
        Action(name + "n", "    RULE pe_nn = pe_nn + 1;\n    RULE pe_d = MY.DISTANCE;\n" + L(nearToken, "pe_nn", "pe_d", "PLAYER_X", "PLAYER_Y")) +
        Action(name + "f", "    RULE pe_fn = pe_fn + 1;\n    RULE pe_d = MY.DISTANCE;\n" + L(farToken, "pe_fn", "pe_d", "PLAYER_X", "PLAYER_Y"));

    private static string Stand(double x, double y, string token, int ticks = 32, bool locate = false) =>
        Beam(x, y, token, locate) + Wait(ticks, token + "S");

    // ---- region-border walls -----------------------------------------------------------------

    /// <summary>
    /// The pad's boundary wall with <c>DIST 0</c>: <c>N</c>/<c>F</c> lines with the run count,
    /// <c>MY.DISTANCE</c>, the player position and <c>HERE.SKILL1</c> (room 1, pad 2). The pad's
    /// <c>IF_ENTER</c>/<c>IF_LEAVE</c> print <c>ENTER</c>/<c>LEAVE</c>.
    /// </summary>
    private static string BorderDeclarations(string flags, bool printHeight = false) =>
        Skills +
        Action("cb_near", "    RULE pe_nn = pe_nn + 1;\n    RULE pe_d = MY.DISTANCE;\n    RULE pe_h = HERE.SKILL1;\n" + L("N", "pe_nn", "pe_d", "PLAYER_X", "PLAYER_Y", "pe_h")) +
        Action("cb_far", "    RULE pe_fn = pe_fn + 1;\n    RULE pe_d = MY.DISTANCE;\n    RULE pe_h = HERE.SKILL1;\n" + L("F", "pe_fn", "pe_d", "PLAYER_X", "PLAYER_Y", "pe_h")) +
        Action("cb_enter", "    RULE pe_h = HERE.SKILL1;\n" + L("ENTER", "PLAYER_X", "PLAYER_Y", "pe_h")) +
        Action("cb_leave", "    RULE pe_h = HERE.SKILL1;\n" + L("LEAVE", "PLAYER_X", "PLAYER_Y", "pe_h")) +
        "WALL cb_wall { TEXTURE red_tex; IF_NEAR cb_near; IF_FAR cb_far;" + (flags.Length > 0 ? $" FLAGS {flags};" : "") + " }\n" +
        (printHeight
            ? Sampler("    RULE pe_h = HERE.SKILL1;\n", "pe_h", "PLAYER_Z")
            : Sampler("    RULE pe_h = HERE.SKILL1;\n", "pe_h"));

    private static TraceProbeMap BorderMap(int padFloor) => new(
        RoomBody: "SKILL1 1;",
        PadBody: "SKILL1 2;\n    IF_ENTER cb_enter;\n    IF_LEAVE cb_leave;",
        PadWall: "cb_wall",
        PadFloor: padFloor);

    /// <summary>
    /// B1: from (−8, 6) in +Y across the pad's south wall (y = 12) to y ≈ 16, stand, back. B2: from
    /// (−8, 8) through the whole pad to y ≈ 22 (across y = 12 and y = 20), stand, back. B3: a
    /// diagonal (+1, +1) walk from (−14, 6) that meets the south wall at x ≈ −8.
    /// </summary>
    private static readonly string BorderRun =
        ArmSampler + L("GO", "PLAYER_X", "PLAYER_Y") +
        Beam(-8, 6, "B1") + Wait(16, "B1S") + Walk(0, 0.2, 50, "B1W") + Wait(32, "B1T") +
        Walk(0, -0.2, 50, "B1B") + Wait(16, "B1E") +
        Beam(-8, 8, "B2") + Wait(16, "B2S") + Walk(0, 0.2, 70, "B2W") + Wait(16, "B2T") +
        Walk(0, -0.2, 70, "B2B") + Wait(16, "B2E") +
        Beam(-14, 6, "B3") + Wait(16, "B3S") + Walk(0.2, 0.2, 45, "B3W") + Wait(16, "B3T") +
        Walk(-0.2, -0.2, 45, "B3B") + Wait(16, "B3E") +
        ClearSampler;

    // ---- LIBER and BERKELEY ------------------------------------------------------------------

    /// <summary>
    /// <c>CLIP_DIST 10</c>. A plain thing (<c>T</c>) at (24, −12), a <c>LIBER</c> thing (<c>L</c>)
    /// at (24, 12), a plain actor (<c>A</c>) at (40, −12) and a <c>LIBER</c> actor (<c>B</c>) at
    /// (40, 12), each with a counting <c>EACH_TICK</c>.
    /// </summary>
    private static readonly string LiberDeclarations =
        Skills + SmallTexture +
        CountingTick("lb_tt", "T", "q1") + CountingTick("lb_lt", "L", "q2") +
        CountingTick("lb_at", "A", "q3") + CountingTick("lb_bt", "B", "q4") +
        "THING lb_t { TEXTURE cx_stex; EACH_TICK lb_tt; }\n" +
        "THING lb_l { TEXTURE cx_stex; EACH_TICK lb_lt; FLAGS LIBER; }\n" +
        "ACTOR lb_a { TEXTURE cx_stex; EACH_TICK lb_at; }\n" +
        "ACTOR lb_b { TEXTURE cx_stex; EACH_TICK lb_bt; FLAGS LIBER; }\n" +
        Sampler();

    /// <summary>Stands 24 or more from every object, then 6 from each in turn (18 or more from the others).</summary>
    private static readonly string LiberRun =
        ArmSampler + Wait(32, "S0") +
        Stand(24, -6, "T6") + Stand(24, 6, "L6") + Stand(40, -6, "A6") + Stand(40, 6, "B6") +
        Stand(0, 0, "O") + ClearSampler;

    /// <summary>
    /// No <c>CLIP_DIST</c>. Behind the player (looking along +X from the origin): a
    /// <c>BERKELEY</c> thing (<c>B</c>, <c>DIST 4</c>, <c>IF_NEAR</c> <c>N</c>) at (−10, 0), a plain
    /// thing (<c>C</c>) at (−10, −3) and a <c>BERKELEY</c> actor (<c>D</c>, <c>DIST 4</c>) at (−10, 3).
    /// </summary>
    private static readonly string BerkeleyDeclarations =
        Skills + SmallTexture + NearFar("bk_", "N", "F") +
        CountingTick("bk_bt", "B", "kb") + CountingTick("bk_ct", "C", "kc") + CountingTick("bk_dt", "D", "kd") +
        "THING bk_b { TEXTURE cx_stex; DIST 4; IF_NEAR bk_n; IF_FAR bk_f; EACH_TICK bk_bt; FLAGS BERKELEY; }\n" +
        "THING bk_c { TEXTURE cx_stex; EACH_TICK bk_ct; }\n" +
        "ACTOR bk_d { TEXTURE cx_stex; DIST 4; IF_NEAR bk_n; IF_FAR bk_f; EACH_TICK bk_dt; FLAGS BERKELEY; }\n" +
        Sampler();

    /// <summary>
    /// S0 looking away from the three objects, S1 turned to face them (all within the view), S2
    /// turned away again, S3 beamed to (−7, 0), 3 from the thing, still looking away, S4 at (−7, 3),
    /// 3 from the actor, S5 back at the origin.
    /// </summary>
    private static readonly string BerkeleyRun =
        ArmSampler + Wait(32, "S0") +
        "    SET PLAYER_ANGLE, 3.1416;\n" + Wait(32, "S1") +
        "    SET PLAYER_ANGLE, 0;\n" + Wait(32, "S2") +
        Stand(-7, 0, "S3") + Stand(-7, 3, "S4") + Stand(0, 0, "S5") + ClearSampler;

    /// <summary>
    /// berkeley-front: a <c>BERKELEY</c> thing (<c>B</c>, <c>DIST 4</c>, <c>IF_NEAR</c> <c>N</c>) at
    /// (10, 0) in front of the player from the start, and a plain thing (<c>C</c>) at (10, −3). S0
    /// looking at them, S1 turned away, S2 looking again, S3 beamed to (7, 0), 3 from the thing,
    /// S4 back at the origin. The stand lines add <c>PLAYER_ANGLE</c>.
    /// </summary>
    private static readonly string BerkeleyFrontRun =
        ArmSampler + L("GO", "PLAYER_ANGLE") + Wait(32, "S0") + L("A0", "PLAYER_ANGLE") +
        "    SET PLAYER_ANGLE, 3.1416;\n" + Wait(32, "S1") + L("A1", "PLAYER_ANGLE") +
        "    SET PLAYER_ANGLE, 0;\n" + Wait(32, "S2") + L("A2", "PLAYER_ANGLE") +
        Stand(7, 0, "S3") + Stand(0, 0, "S4") + ClearSampler;

    // ---- other object actions outside CLIP_DIST ---------------------------------------------

    /// <summary>
    /// <c>CLIP_DIST 10</c>. Three objects with an animated 2-step texture (EACH_CYCLE every 8 ticks,
    /// counted in <c>cn_ct</c>/<c>cn_cl</c>/<c>cn_ca</c>, printed by the sampler), <c>DIST 15</c>,
    /// <c>IF_NEAR</c>/<c>IF_FAR</c> and a counting <c>EACH_TICK</c>: a plain thing (<c>N</c>/<c>F</c>,
    /// <c>T</c>) at (24, −16), a <c>LIBER</c> thing (<c>LN</c>/<c>LF</c>, <c>L</c>) at (24, 0) and a
    /// plain actor (<c>NA</c>/<c>FA</c>, <c>A</c>) at (24, 16).
    /// </summary>
    private static readonly string ClipNearDeclarations =
        Skills +
        "SKILL cn_ct { VAL 0; }\nSKILL cn_cl { VAL 0; }\nSKILL cn_ca { VAL 0; }\n" +
        "TEXTURE cn_atex { SCALE_X 32; SCALE_Y 32; CYCLES 2; BMAPS red_map, grn_map; DELAY 4, 4; }\n" +
        NearFar("cn_t", "N", "F") + NearFar("cn_l", "LN", "LF") + NearFar("cn_a", "NA", "FA") +
        CountingTick("cn_tt", "T", "nt") + CountingTick("cn_lt", "L", "nl") + CountingTick("cn_at", "A", "na") +
        Action("cn_tc", "    RULE cn_ct = cn_ct + 1;\n") +
        Action("cn_lc", "    RULE cn_cl = cn_cl + 1;\n") +
        Action("cn_ac", "    RULE cn_ca = cn_ca + 1;\n") +
        "THING cn_t { TEXTURE cn_atex; DIST 15; IF_NEAR cn_tn; IF_FAR cn_tf; EACH_CYCLE cn_tc; EACH_TICK cn_tt; }\n" +
        "THING cn_l { TEXTURE cn_atex; DIST 15; IF_NEAR cn_ln; IF_FAR cn_lf; EACH_CYCLE cn_lc; EACH_TICK cn_lt; FLAGS LIBER; }\n" +
        "ACTOR cn_a { TEXTURE cn_atex; DIST 15; IF_NEAR cn_an; IF_FAR cn_af; EACH_CYCLE cn_ac; EACH_TICK cn_at; }\n" +
        Sampler("", "cn_ct", "cn_cl", "cn_ca");

    /// <summary>
    /// For each object's line: beam to x = 0 (24 from it), stand, walk +X to x ≈ 20 (DIST 15 is
    /// entered at x ≈ 9, CLIP_DIST 10 at x ≈ 14), stand, walk back, stand. The other objects stay
    /// 20 or more away.
    /// </summary>
    private static readonly string ClipNearRun =
        ArmSampler + L("GO", "PLAYER_X", "PLAYER_Y") +
        ClipNearLine(-16, "T") + ClipNearLine(0, "L") + ClipNearLine(16, "A") + ClearSampler;

    private static string ClipNearLine(double y, string tag) =>
        Beam(0, y, tag) + Wait(32, tag + "S") + Walk(0.2, 0, 100, tag + "W") + Wait(32, tag + "T") +
        Walk(-0.2, 0, 100, tag + "B") + Wait(32, tag + "E");

    // ---- actor movement outside CLIP_DIST ----------------------------------------------------

    /// <summary>
    /// <c>CLIP_DIST 10</c>. A FOLLOW actor at (40, −12) and a WAY actor patrolling (30, 12) ↔
    /// (46, 12), both at 0.2 steps per tick, with counting <c>EACH_TICK</c>s (<c>A</c>, <c>W</c>).
    /// Synonyms point at them so the sampler prints their positions (<c>cm_fx</c>…<c>cm_wy</c>).
    /// </summary>
    private static string ClipMoveDeclarations(bool liber)
    {
        string flags = liber ? " FLAGS LIBER;" : "";
        return Skills +
               "SKILL cm_fx { VAL 0; }\nSKILL cm_fy { VAL 0; }\nSKILL cm_wx { VAL 0; }\nSKILL cm_wy { VAL 0; }\n" +
               SmallTexture +
               "SYNONYM cm_fs { TYPE ACTOR; }\nSYNONYM cm_ws { TYPE ACTOR; }\n" +
               "WAY cm_way;\n" +
               CountingTick("cm_ft", "A", "mf") + CountingTick("cm_wt", "W", "mw") +
               $"ACTOR cm_f {{ TEXTURE cx_stex; TARGET FOLLOW; SPEED 0.2; EACH_TICK cm_ft;{flags} }}\n" +
               $"ACTOR cm_w {{ TEXTURE cx_stex; TARGET cm_way; SPEED 0.2; EACH_TICK cm_wt;{flags} }}\n" +
               Sampler(ReadActorPositions, "cm_fx", "cm_fy", "cm_wx", "cm_wy");
    }

    private const string ReadActorPositions =
        "    RULE cm_fx = cm_fs.X;\n    RULE cm_fy = cm_fs.Y;\n    RULE cm_wx = cm_ws.X;\n    RULE cm_wy = cm_ws.Y;\n";

    /// <summary>
    /// The player stands at the origin (52 from the FOLLOW actor, 42 from the WAY actor) for 160
    /// ticks. F1: beamed 9 from the FOLLOW actor, stand 16 ticks, then walk −X at 0.5 steps per
    /// tick, faster than it follows. W1: beamed 6 from the WAY actor's start, so it stays within 10
    /// only while x &lt; 38; W2: beamed to (43, 8), 9 from x = 38.
    /// </summary>
    private static readonly string ClipMoveRun =
        "    SET cm_fs, cm_f;\n    SET cm_ws, cm_w;\n" + ReadActorPositions + L("SYN", "cm_fx", "cm_fy", "cm_wx", "cm_wy") +
        ArmSampler + Wait(160, "S0") +
        Stand(31, -12, "F1", 16) + Walk(-0.5, 0, 40, "F1W") + Wait(64, "F1E") +
        Stand(32, 8, "W1", 64) + Stand(43, 8, "W2", 64) + Stand(0, 0, "O", 64) +
        ClearSampler;

    private static readonly TraceProbeMap ClipMoveMap = new(
        RoomBody: "CLIP_DIST 10;",
        Placements: ["ACTOR cm_f 40 -12 180 0", "ACTOR cm_w 30 12 0 0", "WAY cm_way 30 12 46 12"]);

    // ---- the far DISTANCE of LIBER objects ---------------------------------------------------

    /// <summary>
    /// <c>CLIP_DIST 10</c>. Standing <c>LIBER</c> objects with counting <c>EACH_TICK</c>s: an actor
    /// (<c>A</c>) and a thing (<c>T</c>) at (36, 12), an actor (<c>B</c>) and a thing (<c>U</c>) at
    /// (10, 12). clip-move-liber's way actor read 29.438 right after a beam (three times its
    /// border), then the sentinel again at 39–41 while the player walked away, and the FOLLOW actor
    /// read the sentinel at 39.3 after one beam and 40.7 after another: these stands separate the
    /// player's beam from the actor's own move, and the actor from the thing.
    /// </summary>
    private static readonly string LiberFarDeclarations =
        Skills + SmallTexture +
        CountingTick("lf_at", "A", "fa") + CountingTick("lf_tt", "T", "ft") +
        CountingTick("lf_bt", "B", "fb") + CountingTick("lf_ut", "U", "fu") +
        "ACTOR lf_a { TEXTURE cx_stex; EACH_TICK lf_at; FLAGS LIBER; }\n" +
        "THING lf_t { TEXTURE cx_stex; EACH_TICK lf_tt; FLAGS LIBER; }\n" +
        "ACTOR lf_b { TEXTURE cx_stex; EACH_TICK lf_bt; FLAGS LIBER; }\n" +
        "THING lf_u { TEXTURE cx_stex; EACH_TICK lf_ut; FLAGS LIBER; }\n" +
        Sampler();

    private static readonly TraceProbeMap LiberFarMap = new(
        RoomBody: "CLIP_DIST 10;",
        Placements: ["ACTOR lf_a 36 12 0 0", "THING lf_t 36 12 0 0", "ACTOR lf_b 10 12 0 0", "THING lf_u 10 12 0 0"]);

    /// <summary>
    /// Beams only (|depth| + |lateral| from A/T, then B/U): S0 the origin (48, 22); P1 (31, −12)
    /// (29, 45; clip-move-liber's first beam); P2 (31, 0) (17, 33); P3 (31, 6) (11, 27: A/T within
    /// 15); P4 (19, −12) (41, 33); P5 (23, −12) (37, 37); P6 (15, −12) (45, 29); P7 (30, −8) (26, 40
    /// behind); P8 (36, −2) (14, 40 behind); P9 (12, 8) (28, 6: B/U within 15); P10 (30, −8) and
    /// P11 (36, −2) again (B/U 40 behind, now started); P12 the origin; P13 (19, −12).
    /// </summary>
    private static readonly string LiberFarBeamRun =
        ArmSampler + Wait(32, "S0") +
        Stand(31, -12, "P1") + Stand(31, 0, "P2") + Stand(31, 6, "P3") + Stand(19, -12, "P4") +
        Stand(23, -12, "P5") + Stand(15, -12, "P6") + Stand(30, -8, "P7") + Stand(36, -2, "P8") +
        Stand(12, 8, "P9") + Stand(30, -8, "P10") + Stand(36, -2, "P11") + Stand(0, 0, "P12") +
        Stand(19, -12, "P13") + ClearSampler;

    /// <summary>
    /// Walks: S1 at (31, 6) starts A/T (11); F1 beams to (31, −12) (29) and walks −X at 0.5 a tick
    /// for 40 ticks to x = 11 (A/T 33, 37, 41, 45, 49 a cycle, clip-move-liber's walk), stands, walks
    /// back, stands. S2 at (12, 8) starts B/U (6); G1 beams to (30, −8) (B/U 40 behind), walks +X
    /// 16 ticks to x = 38 (48), stands, walks −X 32 ticks to x = 22 (32), stands.
    /// </summary>
    private static readonly string LiberFarWalkRun =
        ArmSampler + Wait(32, "S0") + Stand(31, 6, "S1") +
        Stand(31, -12, "F1", 16) + Walk(-0.5, 0, 40, "F1W") + Wait(32, "F1S") +
        Walk(0.5, 0, 40, "F1B") + Wait(32, "F1E") +
        Stand(12, 8, "S2") +
        Stand(30, -8, "G1", 16) + Walk(0.5, 0, 16, "G1W") + Wait(32, "G1S") +
        Walk(-0.5, 0, 32, "G1B") + Wait(32, "G1E") + ClearSampler;

    /// <summary>
    /// The far-DISTANCE stands with TARGET actors that stay in place: <c>kind</c> "way" patrols a
    /// half-step way ((36, 12) ↔ (36.5, 12), (10, 12) ↔ (10.5, 12)), so each skipped move only
    /// arrives at the next point; "follow" is a <c>FOLLOW</c> actor with <c>SPEED 0</c>. A plain
    /// <c>LIBER</c> thing (<c>T</c>, <c>U</c>) stands at each spot as the control. Synonyms let the
    /// sampler print the actors' positions.
    /// </summary>
    private static string LiberFarMoverDeclarations(string kind)
    {
        string target(string way) => kind == "way" ? $"TARGET {way}; SPEED 0.2;" : "TARGET FOLLOW; SPEED 0;";
        return Skills +
               "SKILL lm_ax { VAL 0; }\nSKILL lm_ay { VAL 0; }\nSKILL lm_bx { VAL 0; }\nSKILL lm_by { VAL 0; }\n" +
               SmallTexture +
               "SYNONYM lm_as { TYPE ACTOR; }\nSYNONYM lm_bs { TYPE ACTOR; }\n" +
               (kind == "way" ? "WAY lm_wa;\nWAY lm_wb;\n" : "") +
               CountingTick("lf_at", "A", "fa") + CountingTick("lf_tt", "T", "ft") +
               CountingTick("lf_bt", "B", "fb") + CountingTick("lf_ut", "U", "fu") +
               $"ACTOR lf_a {{ TEXTURE cx_stex; {target("lm_wa")} EACH_TICK lf_at; FLAGS LIBER; }}\n" +
               "THING lf_t { TEXTURE cx_stex; EACH_TICK lf_tt; FLAGS LIBER; }\n" +
               $"ACTOR lf_b {{ TEXTURE cx_stex; {target("lm_wb")} EACH_TICK lf_bt; FLAGS LIBER; }}\n" +
               "THING lf_u { TEXTURE cx_stex; EACH_TICK lf_ut; FLAGS LIBER; }\n" +
               Sampler(ReadMoverPositions, "lm_ax", "lm_ay", "lm_bx", "lm_by");
    }

    private const string ReadMoverPositions =
        "    RULE lm_ax = lm_as.X;\n    RULE lm_ay = lm_as.Y;\n    RULE lm_bx = lm_bs.X;\n    RULE lm_by = lm_bs.Y;\n";

    private const string BindMovers = "    SET lm_as, lf_a;\n    SET lm_bs, lf_b;\n";

    private static TraceProbeMap LiberFarMoverMap(string kind) => new(
        RoomBody: "CLIP_DIST 10;",
        Placements: kind == "way"
            ? ["ACTOR lf_a 36 12 0 0", "THING lf_t 36 12 0 0", "ACTOR lf_b 10 12 0 0", "THING lf_u 10 12 0 0", "WAY lm_wa 36 12 36.5 12", "WAY lm_wb 10 12 10.5 12"]
            : ["ACTOR lf_a 36 12 0 0", "THING lf_t 36 12 0 0", "ACTOR lf_b 10 12 0 0", "THING lf_u 10 12 0 0"]);

    /// <summary>The beam stands of liber-far-beam, 48 ticks each (six cycles, one skipped move in each), then liber-far-walk's F1 walk.</summary>
    private static readonly string LiberFarMoverRun =
        BindMovers + ArmSampler + Wait(48, "S0") +
        Stand(31, -12, "P1", 48) + Stand(31, 0, "P2", 48) + Stand(31, 6, "P3", 48) + Stand(19, -12, "P4", 48) +
        Stand(23, -12, "P5", 48) + Stand(15, -12, "P6", 48) + Stand(30, -8, "P7", 48) + Stand(36, -2, "P8", 48) +
        Stand(12, 8, "P9", 48) + Stand(30, -8, "P10", 48) + Stand(36, -2, "P11", 48) + Stand(0, 0, "P12", 48) +
        Stand(19, -12, "P13", 48) + Stand(31, 6, "S1", 48) +
        Stand(31, -12, "F1", 16) + Walk(-0.5, 0, 40, "F1W") + Wait(48, "F1S") +
        Walk(0.5, 0, 40, "F1B") + Wait(48, "F1E") + ClearSampler;

    /// <summary>
    /// Two <c>LIBER</c> <c>FOLLOW</c> actors at 0.2 a tick (<c>A</c> from (40, −12), <c>B</c> from
    /// (40, 12)) with the player beamed between contact and far stands: B1 (31, −12) 24 cycles
    /// (both come into contact); B2 the origin (about 45 from both, clip-move-liber's 46.8 that
    /// stayed real); B3 (31, −12) again; B4 (12, 8) (about 39, its 39.3 that went to the sentinel);
    /// B5 (31, −12); B6 (−8, −12) (about 41); B7 (31, −12); B8 (36, 16) (about 30); B9 (31, −12).
    /// </summary>
    private static readonly string LiberFarFollowRun =
        BindMovers + ArmSampler + Wait(96, "S0") +
        Stand(31, -12, "B1", 192) + Stand(0, 0, "B2", 96) + Stand(31, -12, "B3", 96) +
        Stand(12, 8, "B4", 96) + Stand(31, -12, "B5", 96) + Stand(-8, -12, "B6", 96) +
        Stand(31, -12, "B7", 96) + Stand(36, 16, "B8", 96) + Stand(31, -12, "B9", 96) + ClearSampler;

    private static readonly string LiberFarFollowDeclarations =
        Skills +
        "SKILL lm_ax { VAL 0; }\nSKILL lm_ay { VAL 0; }\nSKILL lm_bx { VAL 0; }\nSKILL lm_by { VAL 0; }\n" +
        SmallTexture +
        "SYNONYM lm_as { TYPE ACTOR; }\nSYNONYM lm_bs { TYPE ACTOR; }\n" +
        CountingTick("lf_at", "A", "fa") + CountingTick("lf_bt", "B", "fb") +
        "ACTOR lf_a { TEXTURE cx_stex; TARGET FOLLOW; SPEED 0.2; EACH_TICK lf_at; FLAGS LIBER; }\n" +
        "ACTOR lf_b { TEXTURE cx_stex; TARGET FOLLOW; SPEED 0.2; EACH_TICK lf_bt; FLAGS LIBER; }\n" +
        Sampler(ReadMoverPositions, "lm_ax", "lm_ay", "lm_bx", "lm_by");

    private static readonly TraceProbeMap LiberFarFollowMap = new(
        RoomBody: "CLIP_DIST 10;",
        Placements: ["ACTOR lf_a 40 -12 180 0", "ACTOR lf_b 40 12 180 0"]);

    // ---- contact -----------------------------------------------------------------------------

    /// <summary>
    /// Three solid 4-step wide objects (contact within about 3.2 steps of the centre) with
    /// <c>IF_NEAR</c>/<c>IF_FAR</c>: <c>DIST 0</c> at (24, −14) (<c>N0</c>/<c>F0</c>), <c>DIST 2</c>
    /// at (24, 0) (<c>N2</c>/<c>F2</c>, below the contact distance) and <c>DIST 8</c> at (24, 14)
    /// (<c>N8</c>/<c>F8</c>, above it).
    /// </summary>
    private static string ContactDeclarations(string kind) =>
        Skills +
        "TEXTURE cc_tex { SCALE_X 16; SCALE_Y 16; BMAPS red_map; }\n" +
        NearFar("cc_0", "N0", "F0") + NearFar("cc_2", "N2", "F2") + NearFar("cc_8", "N8", "F8") +
        $"{kind} cc_k0 {{ TEXTURE cc_tex; IF_NEAR cc_0n; IF_FAR cc_0f; }}\n" +
        $"{kind} cc_k2 {{ TEXTURE cc_tex; DIST 2; IF_NEAR cc_2n; IF_FAR cc_2f; }}\n" +
        $"{kind} cc_k8 {{ TEXTURE cc_tex; DIST 8; IF_NEAR cc_8n; IF_FAR cc_8f; }}\n" +
        Sampler();

    private static TraceProbeMap ContactMap(string kind) => new(
        Placements: [$"{kind} cc_k0 24 -14 0 0", $"{kind} cc_k2 24 0 0 0", $"{kind} cc_k8 24 14 0 0"]);

    /// <summary>
    /// For each object's line: beam to x = 14 (10 from its centre), walk +X for 60 ticks (contact
    /// after about 34, then about 26 ticks of pushing), stand against it, back off 8 steps.
    /// </summary>
    private static readonly string ContactRun =
        ArmSampler + L("GO", "PLAYER_X", "PLAYER_Y") +
        ContactLine(-14, "K0") + ContactLine(0, "K2") + ContactLine(14, "K8") + ClearSampler;

    private static string ContactLine(double y, string tag) =>
        Beam(14, y, tag) + Wait(16, tag + "S") + Walk(0.2, 0, 60, tag + "W") + Wait(32, tag + "T") +
        Walk(-0.2, 0, 40, tag + "B") + Wait(16, tag + "E");

    // ---- regions with different CLIP_DIST ----------------------------------------------------

    /// <summary>
    /// The room has <c>CLIP_DIST 10</c>, the pad island <c>CLIP_DIST 30</c>. A thing in the room
    /// (<c>T</c>) at (−8, −8), a thing in the pad (<c>U</c>) at (−6, 18), and a passable wall in the
    /// room (<c>W</c>) from (−14, −2) to (−14, 2), each with a counting <c>EACH_TICK</c>.
    /// </summary>
    private static readonly string ClipRegionsDeclarations =
        Skills + SmallTexture +
        CountingTick("cr_tt", "T", "rt") + CountingTick("cr_ut", "U", "ru") + CountingTick("cr_wt", "W", "rw") +
        "THING cr_t { TEXTURE cx_stex; EACH_TICK cr_tt; }\n" +
        "THING cr_u { TEXTURE cx_stex; EACH_TICK cr_ut; }\n" +
        "WALL cr_wall { TEXTURE grn_tex; EACH_TICK cr_wt; FLAGS PASSABLE; }\n" +
        Sampler("    RULE pe_h = HERE.SKILL1;\n", "pe_h");

    /// <summary>
    /// Stands, each beamed with <c>LOCATE</c>: R1 in the room at (−8, 0) (T 8, U 20, W 8); P1 in
    /// the pad at (−10, 14) (T 24, U 8, W 16); R2 in the room at (−8, 8), just south of the pad
    /// (T 16, U 12, W 12); R3 at (−8, 0) again.
    /// </summary>
    private static readonly string ClipRegionsRun =
        ArmSampler + Wait(32, "S0") +
        Stand(-8, 0, "R1", locate: true) + Stand(-10, 14, "P1", locate: true) +
        Stand(-8, 8, "R2", locate: true) + Stand(-8, 0, "R3", locate: true) + ClearSampler;

    private static readonly TraceProbeMap ClipRegionsMap = new(
        RoomBody: "SKILL1 1;\n    CLIP_DIST 10;",
        PadBody: "SKILL1 2;\n    CLIP_DIST 30;",
        Placements: ["THING cr_t -8 -8 0 0", "THING cr_u -6 18 0 2"],
        Walls: [new TraceProbeWall("cr_wall", -14, -2, -14, 2)]);

    /// <summary>
    /// The clip-regions layout with <c>CLIP_DIST</c> before <c>SKILL1</c>, the order of the manual's
    /// region keyword list (p.56–58).
    /// </summary>
    private static TraceProbeMap OrderedClipRegionsMap(int room, int pad) => ClipRegionsMap with
    {
        RoomBody = $"CLIP_DIST {room};\n    SKILL1 1;",
        PadBody = $"CLIP_DIST {pad};\n    SKILL1 2;",
    };

    /// <summary>
    /// The player walks from (−8, 0) in +Y into the pad to (−8, 14) (T 22, U 6, W 18), stands, walks
    /// back to (−8, 8) (T 16, U 12, W 12), stands, and walks back to (−8, 0).
    /// </summary>
    private static readonly string ClipRegionsWalkRun =
        ArmSampler + Stand(-8, 0, "R1", locate: true) +
        Walk(0, 0.2, 70, "P1W") + Wait(32, "P1S") +
        Walk(0, -0.2, 30, "R2W") + Wait(32, "R2S") +
        Walk(0, -0.2, 40, "R3W") + Wait(32, "R3S") + ClearSampler;

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "border-pass",
            "The pad's boundary walls are PASSABLE with DIST 0 and the pad's floor is level with the room: does walking through a wall between two regions fire its IF_NEAR?",
            BorderDeclarations("PASSABLE"),
            BorderRun,
            Predict(
                ("crossing a region border fires IF_NEAR", "N at y ≈ 12 in B1W and B1B, at y ≈ 12 and 20 in B2W and B2B, in B3W; ENTER/LEAVE alongside"),
                ("only walls with one region on both sides fire", "ENTER/LEAVE but no N")),
            Map: BorderMap(0)),
        new TraceProbeCase(
            "border-plain",
            "The same walls without any flag (visible, not PASSABLE), the pad level with the room: is a wall between two regions a portal without PASSABLE, and if it blocks, does pushing against it fire IF_NEAR?",
            BorderDeclarations(""),
            BorderRun,
            Predict(
                ("region borders are portals whatever the flag", "as border-pass: ENTER/LEAVE and N on every crossing"),
                ("without PASSABLE the wall is solid, contact fires every cycle", "no ENTER; the player stops at y ≈ 10.75; N in every pushing cycle of B1W/B2W, B3W sliding"),
                ("solid, and region borders never fire", "no ENTER, no N")),
            Map: BorderMap(0)),
        new TraceProbeCase(
            "border-high",
            "The PASSABLE walls with the pad's floor at 14 (2 below the ceiling), so the player cannot step onto it: does pushing against the region border fire IF_NEAR?",
            BorderDeclarations("PASSABLE"),
            BorderRun,
            Predict(
                ("contact with an unenterable region border fires every cycle", "no ENTER; the player stops at y ≈ 10.75; N in every pushing cycle"),
                ("only a crossing fires", "no ENTER, no N")),
            Map: BorderMap(14)),
        new TraceProbeCase(
            "border-ceil",
            "border-high entered the pad with its floor at 14. The same walls with the pad's floor at the ceiling (16, no headroom, like the solid border region): is that region border solid, and does pushing against it fire IF_NEAR? The sampler adds PLAYER_Z.",
            BorderDeclarations("PASSABLE", printHeight: true),
            BorderRun,
            Predict(
                ("contact with an unenterable region border fires every cycle", "no ENTER; the player stops at y ≈ 10.75; N in every pushing cycle"),
                ("only a crossing fires", "no ENTER, no N"),
                ("any floor can be entered", "as border-high")),
            Map: BorderMap(16)),
        new TraceProbeCase(
            "liber",
            "CLIP_DIST 10: does a LIBER thing or actor run its EACH_TICK outside CLIP_DIST × THING_DIST/ACTOR_DIST (p.71)?",
            LiberDeclarations,
            LiberRun,
            Predict(
                ("LIBER lifts the gate", "L and B in every stand, T only in T6, A only in A6"),
                ("LIBER changes nothing here", "L only in L6, B only in B6")),
            Map: new TraceProbeMap(
                RoomBody: "CLIP_DIST 10;",
                Placements: ["THING lb_t 24 -12 0 0", "THING lb_l 24 12 0 0", "ACTOR lb_a 40 -12 0 0", "ACTOR lb_b 40 12 0 0"])),
        new TraceProbeCase(
            "berkeley",
            "BERKELEY objects behind the player (p.71: inactive while not seen and outside DIST): does their EACH_TICK stop while they are not seen, and run while seen or within DIST?",
            BerkeleyDeclarations,
            BerkeleyRun,
            Predict(
                ("BERKELEY stops EACH_TICK while unseen and outside DIST", "B, D only in S1, S3 (B) and S4 (D); C in every stand"),
                ("BERKELEY does not gate EACH_TICK", "B, D, C in every stand"),
                ("BERKELEY objects stay inactive within DIST while unseen", "no B in S3, no N")),
            Map: new TraceProbeMap(Placements: ["THING bk_b -10 0 0 0", "THING bk_c -10 -3 0 0", "ACTOR bk_d -10 3 0 0"])),
        new TraceProbeCase(
            "berkeley-front",
            "berkeley ran its BERKELEY objects only within DIST, also after the player turned to them. A BERKELEY thing in front of the player from the start: does it run while seen, stop when the player turns away, and fire IF_NEAR within DIST?",
            BerkeleyDeclarations,
            BerkeleyFrontRun,
            Predict(
                ("BERKELEY runs while seen", "B in S0 and S2, not in S1; C throughout"),
                ("BERKELEY runs only within DIST", "B only in S3")),
            Map: new TraceProbeMap(Placements: ["THING bk_b 10 0 0 0", "THING bk_c 10 -3 0 0"])),
        new TraceProbeCase(
            "clip-near",
            "CLIP_DIST 10 with DIST 15: are a thing's and an actor's IF_NEAR/IF_FAR and EACH_CYCLE gated by CLIP_DIST like EACH_TICK (p.69), and does LIBER lift that?",
            ClipNearDeclarations,
            ClipNearRun,
            Predict(
                ("IF_NEAR/IF_FAR not gated", "N at x ≈ 9 (pe_d ≈ 15), F at x ≈ 9 on the way back; same NA/FA, LN/LF"),
                ("IF_NEAR/IF_FAR gated", "N at x ≈ 14 (pe_d ≈ 10) or none; F at x ≈ 14 or none; LN/LF at 15"),
                ("EACH_CYCLE gated", "cn_ct/cn_ca rise only while the player is within 10 of them; cn_cl always"),
                ("EACH_CYCLE not gated", "cn_ct, cn_cl, cn_ca rise by 2 per 16 ticks throughout")),
            Map: new TraceProbeMap(
                RoomBody: "CLIP_DIST 10;",
                Placements: ["THING cn_t 24 -16 0 0", "THING cn_l 24 0 0 0", "ACTOR cn_a 24 16 0 0"])),
        new TraceProbeCase(
            "clip-move",
            "CLIP_DIST 10: does a FOLLOW or WAY actor move while outside CLIP_DIST × ACTOR_DIST, and does it resume within it?",
            ClipMoveDeclarations(liber: false),
            ClipMoveRun,
            Predict(
                ("movement gated", "S0: cm_f at (40, −12), cm_w at (30, 12); F1: cm_f follows until 10 behind the walking player, then stops; W1: cm_w stops at x ≈ 38; W2: it moves again"),
                ("movement not gated", "S0: cm_f approaches the origin, cm_w patrols")),
            Map: ClipMoveMap),
        new TraceProbeCase(
            "clip-move-liber",
            "The same actors with LIBER: do they move outside CLIP_DIST?",
            ClipMoveDeclarations(liber: true),
            ClipMoveRun,
            Predict(
                ("LIBER lifts the movement gate", "S0: cm_f approaches the origin, cm_w patrols 30 ↔ 46; A and W lines in every stand")),
            Map: ClipMoveMap),
        new TraceProbeCase(
            "liber-far-beam",
            "clip-move-liber's LIBER way actor read a DISTANCE of 29.438 two cycles after a beam (its border 10, the start gate 15) and the sentinel again at 39–41 later; its FOLLOW actor read the sentinel at 39.3 after one beam and 40.7 after another. Standing LIBER actors and things, beamed to the same offsets: when does a standing object's DISTANCE start, and does it ever return to the sentinel?",
            LiberFarDeclarations,
            LiberFarBeamRun,
            Predict(
                ("the start gate is 1.5 × the border for standing objects, a started one is always refreshed", "A/T 65535.999 in S0–P2, 11 from P3 on and every later stand (41, 37, 45, 26, 14, 28, …, 48, 41); B/U 65535.999 until P9 (6), then 40, 40, 22, 33"),
                ("a far refresh writes the sentinel beyond some range, started or not", "A/T 65535.999 again in P4 or P6; B/U in P10 or P11"),
                ("a beam starts every object", "A/T 29 in P1, B/U 45")),
            Map: LiberFarMap),
        new TraceProbeCase(
            "liber-far-walk",
            "The same objects with the player walking: started at 11 (S1) and beamed 29 away (F1), A/T read 33, 37, 41, 45, 49 as the player walks off at 4 a cycle. Does a started standing object keep its distance where clip-move-liber's way actor fell back to the sentinel at 41?",
            LiberFarDeclarations,
            LiberFarWalkRun,
            Predict(
                ("a started object is refreshed at any distance", "A/T 29 after F1, then 33, 37, 41, 45, 49 in F1W and back; B/U 40, 44, 48 in G1W and 32 after G1B"),
                ("the sentinel returns beyond some range", "A/T 65535.999 from 41 on, B/U from 44 or 48")),
            Map: LiberFarMap),
        new TraceProbeCase(
            "liber-far-way",
            "liber-far-beam: a standing LIBER object's DISTANCE starts within 15 and is then refreshed at any distance. clip-move-liber's way actor instead fell back to the sentinel at 39–41 and started at 29.4. The same stands with LIBER way actors that patrol half a step in place: does a TARGET actor's DISTANCE go to the sentinel beyond some range, and does a beam or its own move start it beyond 15?",
            LiberFarMoverDeclarations("way"),
            LiberFarMoverRun,
            Predict(
                ("as the standing objects", "A and T agree in every stand"),
                ("a TARGET actor beyond about 38 reads the sentinel, whatever T reads", "A 65535.999 in P4, P6, P12, P13 and in F1W from x < 23; B in P10, P11"),
                ("its own move starts it", "A 29 in P1 where T stays 65535.999")),
            Map: LiberFarMoverMap("way")),
        new TraceProbeCase(
            "liber-far-follow",
            "liber-far-way with FOLLOW actors of SPEED 0 in place of the way actors: does a FOLLOW actor's targeting un-start its DISTANCE beyond about 38 (clip-move-liber's follower read the sentinel at 39.3 after a beam)?",
            LiberFarMoverDeclarations("follow"),
            LiberFarMoverRun,
            Predict(
                ("as the standing objects", "A and T agree in every stand"),
                ("beyond about 38 the sentinel", "A 65535.999 in P4, P6, P12, P13; B in P10, P11")),
            Map: LiberFarMoverMap("follow")),
        new TraceProbeCase(
            "liber-far-contact",
            "Two LIBER FOLLOW actors that walk to the player and touch it, then beams to about 45, 39, 41 and 30 away and back: which beams leave a follower's DISTANCE real and which write the sentinel (clip-move-liber: 46.8 real after one beam, 39.3 the sentinel after another, 40.7 real after a third)?",
            LiberFarFollowDeclarations,
            LiberFarFollowRun,
            Predict(
                ("beyond about 38 the sentinel", "A, B 65535.999 in B2, B4, B6; real in B8"),
                ("a follower that was in contact reads the sentinel after any beam away", "65535.999 in B2, B4, B6, B8"),
                ("always real once started", "no 65535.999 after B1")),
            Map: LiberFarFollowMap),
        new TraceProbeCase(
            "contact-thing",
            "Pushing against a solid THING with DIST 0, 2 (below the contact distance) and 8: does contact fire IF_NEAR in every cycle while outside DIST, and does IF_FAR follow?",
            ContactDeclarations("THING"),
            ContactRun,
            Predict(
                ("contact fires every cycle outside DIST", "N0, N2 in every pushing cycle of K0W, K2W, none in K0T/K2T; N8 once at x ≈ 16; no F0/F2"),
                ("contact fires once", "one N0, one N2"),
                ("contact sets the near state", "F2 when backing off")),
            Map: ContactMap("THING")),
        new TraceProbeCase(
            "contact-actor",
            "The same with an ACTOR (no TARGET): does the player pushing against it fire its IF_NEAR in every cycle?",
            ContactDeclarations("ACTOR"),
            ContactRun,
            Predict(
                ("as contact-thing", "the same N/F lines")),
            Map: ContactMap("ACTOR")),
        new TraceProbeCase(
            "clip-regions",
            "The room has CLIP_DIST 10, the pad 30: does the player's region gate object EACH_TICKs, or the object's own region?",
            ClipRegionsDeclarations,
            ClipRegionsRun,
            Predict(
                ("the player's region", "R1: T, W; P1: T, U, W; R2: none; R3: T, W"),
                ("the object's region", "S0: U (24 < 30); R1: T, U, W; P1: U only; R2: U"),
                ("the larger of the two", "S0: U; R1: T, U, W; P1: T, U, W; R2: U")),
            Map: ClipRegionsMap),
        new TraceProbeCase(
            "clip-regions-order",
            "clip-regions never ran anything beyond 10, not even with the player in the pad (CLIP_DIST 30). The same with CLIP_DIST written before SKILL1, in the manual's keyword order: does the pad's CLIP_DIST apply now?",
            ClipRegionsDeclarations,
            ClipRegionsRun,
            Predict(
                ("the player's region", "R1: T, W; P1: T, U, W; R2: none; R3: T, W"),
                ("as clip-regions (order does not matter)", "nothing beyond 10 runs")),
            Map: OrderedClipRegionsMap(10, 30)),
        new TraceProbeCase(
            "clip-regions-walk",
            "The ordered layout, with the player walking into the pad instead of a beam with LOCATE: does the pad's CLIP_DIST 30 gate once the player has walked in?",
            ClipRegionsDeclarations,
            ClipRegionsWalkRun,
            Predict(
                ("the player's region, taken on walking in", "P1S: T, U, W; R2S: none; R3S: T, W"),
                ("the room's value throughout", "P1S: U only")),
            Map: OrderedClipRegionsMap(10, 30)),
        new TraceProbeCase(
            "clip-regions-swap",
            "The ordered layout with the values swapped (room 30, pad 10), beams with LOCATE: whose CLIP_DIST gates?",
            ClipRegionsDeclarations,
            ClipRegionsRun,
            Predict(
                ("the player's region", "S0: T (16), W (16); R1: T, U (20), W; P1: U only; R2: T, U, W"),
                ("the object's region", "S0: T, W; R1: T, W; P1: T (24), U, W (16); R2: T, W"),
                ("the smaller value, or the first region", "nothing beyond 10 runs")),
            Map: OrderedClipRegionsMap(30, 10)),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "clip-contact",
        TraceFile,
        "ovccp",
        "After the skill dump, `IF_START` selects the trace file, writes `BEGIN` and runs the variant's case.\n" +
        "Each trace line is a token followed by `TICKS` and `pe_n` (the global EACH_TICK sampler's run count)\n" +
        "when it was printed, then the case's extra skills. Single-letter lines from object EACH_TICKs carry\n" +
        "their run count, MY.DISTANCE and the player position; `N`/`F`-style lines are IF_NEAR/IF_FAR runs\n" +
        "(count, MY.DISTANCE, player position). `pe_h` is HERE.SKILL1 (room 1, pad 2). Compare the trace with\n" +
        "`case.json` → `predictions`, and check `OVCON.TXT` for compile errors.",
        All);
}
