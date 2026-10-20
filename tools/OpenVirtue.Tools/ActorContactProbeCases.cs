// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Text;
using static ProximityProbeCases;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>actor-contact</c> probe
/// (<c>docs/clean-room/observations/OV-20261019-actor-contact.md</c>), the contact questions that
/// OV-20261019-proximity-events, -clip-contact and -eye-clamp left open:
/// <list type="bullet">
/// <item><c>stop-*</c>: how far from a solid thing's or actor's centre does the player stop, as a
/// function of the texture width, <c>PLAYER_WIDTH</c> and <c>THING_WIDTH</c>/<c>ACTOR_WIDTH</c>
/// (p.114)?</item>
/// <item><c>actor-push*</c>: a FOLLOW actor walks into a player who stands still. Is the player
/// pushed, how far, and does the actor's <c>IF_NEAR</c> fire in every pushing cycle while the
/// player is outside its <c>DIST</c>? Does <c>CAREFULLY</c> (p.73) change that?</item>
/// <item><c>beam-inside</c>: what happens to a player beamed inside a solid or passable thing?</item>
/// <item><c>distance-refresh</c>: when does a thing's <c>MY.DISTANCE</c> leave its 65535.999
/// start value, with and without <c>DIST</c>, <c>LIBER</c> and the <c>CLIP_DIST</c> border?</item>
/// <item><c>dist-edge</c>: is <c>DIST</c> compared with <c>&lt;</c> or <c>&lt;=</c>, for a thing
/// and a wall?</item>
/// </list>
/// While <c>ac_p</c> is set, the global sampler prints a <c>V</c> line in every cycle.
/// </summary>
internal static class ActorContactProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVACX0.TXT).</summary>
    public const string TraceFile = "ovacx";

    private const string ExtraSkills =
        "SKILL ac_p { VAL 0; }\n" +
        "SKILL ac_ax { VAL 0; }\n" +
        "SKILL ac_ay { VAL 0; }\n" +
        "SKILL ac_ad { VAL 0; }\n";

    /// <summary>Sprite textures 2, 4 and 8 steps wide (the probe bitmaps are 64 pixels wide).</summary>
    private const string Textures =
        "TEXTURE ac_tex2 { SCALE_X 32; SCALE_Y 32; BMAPS red_map; }\n" +
        "TEXTURE ac_tex4 { SCALE_X 16; SCALE_Y 16; BMAPS red_map; }\n" +
        "TEXTURE ac_tex8 { SCALE_X 8; SCALE_Y 8; BMAPS red_map; }\n";

    /// <summary>
    /// <c>IF_NEAR</c>/<c>IF_FAR</c> actions <c>&lt;name&gt;n</c>/<c>&lt;name&gt;f</c> that count in
    /// <c>pe_nn</c>/<c>pe_fn</c> and print every run with <c>MY.DISTANCE</c> and the player position.
    /// </summary>
    private static string NearFar(string name, string nearToken, string farToken) =>
        Action(name + "n", "    RULE pe_nn = pe_nn + 1;\n    RULE pe_d = MY.DISTANCE;\n" + L(nearToken, "pe_nn", "pe_d", "PLAYER_X", "PLAYER_Y")) +
        Action(name + "f", "    RULE pe_fn = pe_fn + 1;\n    RULE pe_d = MY.DISTANCE;\n" + L(farToken, "pe_fn", "pe_d", "PLAYER_X", "PLAYER_Y"));

    /// <summary>The sampler with a per-cycle <c>V</c> line (TIME_CORR, the player position and <paramref name="skills"/>) while <c>ac_p</c> is set.</summary>
    private static string PerCycleSampler(string read = "", params string[] skills) =>
        Sampler("    IF (ac_p > 0) {\n" + Indent(read) + Indent(L("V", ["TIME_CORR", "PLAYER_X", "PLAYER_Y", .. skills])) + "    }\n");

    private static string Line(string token, params string[] extra) => L(token, ["PLAYER_X", "PLAYER_Y", .. extra]);

    /// <summary>
    /// One push: beam with <c>LOCATE</c> to (<paramref name="x"/>, <paramref name="y"/>), set
    /// <c>PLAYER_WIDTH</c>, stand 8 ticks, walk +X at 0.2 steps per tick for <paramref name="ticks"/>
    /// ticks, stand 8, back off −X for 20 ticks, stand 8.
    /// </summary>
    private static string Push(string tag, double width, double x, double y, int ticks = 70)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"    SET PLAYER_X, {x};\n    SET PLAYER_Y, {y};\n    LOCATE;\n    SET PLAYER_WIDTH, {width};\n");
        sb.Append(Line(tag, "PLAYER_WIDTH"));
        sb.Append("    WAITT 8;\n").Append(Line(tag + "S"));
        sb.Append(CultureInfo.InvariantCulture, $"    RULE pe_vx = 0.2;\n    WAITT {ticks};\n    RULE pe_vx = 0;\n");
        sb.Append(Line(tag + "W"));
        sb.Append("    WAITT 8;\n").Append(Line(tag + "T"));
        sb.Append("    RULE pe_vx = -0.2;\n    WAITT 20;\n    RULE pe_vx = 0;\n");
        sb.Append("    WAITT 8;\n").Append(Line(tag + "E"));
        return sb.ToString();
    }

    // ---- stop distance -----------------------------------------------------------------------

    /// <summary>Three solid things with <c>IF_NEAR</c> (<c>DIST 0</c>): 2 wide at (24, −14), 4 wide at (24, 0), 8 wide at (24, 14).</summary>
    private static readonly string StopWidthDeclarations =
        Skills + ExtraSkills + Textures +
        NearFar("sw_2", "N2", "F2") + NearFar("sw_4", "N4", "F4") + NearFar("sw_8", "N8", "F8") +
        "THING sw_k2 { TEXTURE ac_tex2; IF_NEAR sw_2n; IF_FAR sw_2f; }\n" +
        "THING sw_k4 { TEXTURE ac_tex4; IF_NEAR sw_4n; IF_FAR sw_4f; }\n" +
        "THING sw_k8 { TEXTURE ac_tex8; IF_NEAR sw_8n; IF_FAR sw_8f; }\n" +
        Sampler();

    /// <summary>For PLAYER_WIDTH 1.2, 2 and 0.5: push +X from x = 12 into each thing's centre line.</summary>
    private static readonly string StopWidthRun =
        ArmSampler + Line("GO") +
        Push("A2", 1.2, 12, -14) + Push("A4", 1.2, 12, 0) + Push("A8", 1.2, 10, 14) +
        Push("B2", 2, 12, -14) + Push("B4", 2, 12, 0) + Push("B8", 2, 10, 14) +
        Push("C2", 0.5, 12, -14) + Push("C4", 0.5, 12, 0) + Push("C8", 0.5, 10, 14) +
        ClearSampler;

    private static readonly TraceProbeMap StopWidthMap = new(
        Placements: ["THING sw_k2 24 -14 0 0", "THING sw_k4 24 0 0 0", "THING sw_k8 24 14 0 0"]);

    /// <summary>A 4-wide thing at (24, −8) and a 4-wide actor without a target at (24, 8), both with IF_NEAR.</summary>
    private static readonly string StopFactorDeclarations =
        Skills + ExtraSkills + Textures +
        NearFar("sf_t", "NT", "FT") + NearFar("sf_a", "NA", "FA") +
        "THING sf_k { TEXTURE ac_tex4; IF_NEAR sf_tn; IF_FAR sf_tf; }\n" +
        "ACTOR sf_c { TEXTURE ac_tex4; IF_NEAR sf_an; IF_FAR sf_af; }\n" +
        Sampler();

    /// <summary>Pushes at the defaults, then with THING_WIDTH 2 and ACTOR_WIDTH 0.5, then the reverse.</summary>
    private static readonly string StopFactorRun =
        ArmSampler + Line("GO", "THING_WIDTH", "ACTOR_WIDTH") +
        Push("AT", 1.2, 12, -8) + Push("AA", 1.2, 12, 8) +
        "    SET THING_WIDTH, 2;\n    SET ACTOR_WIDTH, 0.5;\n" + Line("SET1", "THING_WIDTH", "ACTOR_WIDTH") +
        Push("BT", 1.2, 12, -8) + Push("BA", 1.2, 12, 8) +
        "    SET THING_WIDTH, 0.5;\n    SET ACTOR_WIDTH, 2;\n" + Line("SET2", "THING_WIDTH", "ACTOR_WIDTH") +
        Push("CT", 1.2, 12, -8) + Push("CA", 1.2, 12, 8) +
        ClearSampler;

    private static readonly TraceProbeMap StopFactorMap = new(
        Placements: ["THING sf_k 24 -8 0 0", "ACTOR sf_c 24 8 0 0"]);

    // ---- an actor pushing the player -----------------------------------------------------------

    private const string ReadActor =
        "RULE ac_ax = ac_s.X;\nRULE ac_ay = ac_s.Y;\nRULE ac_ad = ac_s.DISTANCE;\n";

    /// <summary>
    /// A 4-wide FOLLOW actor (contact within about 3.2 of its centre) at (30, 0) facing the player,
    /// <c>SPEED 0.2</c>, <c>DIST 2</c> (below the contact distance), <c>IF_NEAR</c>/<c>IF_FAR</c>.
    /// The <c>V</c> line adds the actor's position and <c>DISTANCE</c>.
    /// </summary>
    private static string ActorPushDeclarations(string flags) =>
        Skills + ExtraSkills + Textures +
        "SYNONYM ac_s { TYPE ACTOR; }\n" +
        NearFar("ap_", "N", "F") +
        $"ACTOR ap_k {{ TEXTURE ac_tex4; TARGET FOLLOW; SPEED 0.2; DIST 2; IF_NEAR ap_n; IF_FAR ap_f;{flags} }}\n" +
        PerCycleSampler(ReadActor, "ac_ax", "ac_ay", "ac_ad");

    /// <summary>
    /// The player stands at the origin while the actor approaches (contact after about 134 ticks)
    /// and pushes for about 66 ticks, then the actor's SPEED is set to 0 for 32 ticks, the player
    /// walks into it for 30 ticks and backs off.
    /// </summary>
    private static readonly string ActorPushRun =
        "    SET ac_s, ap_k;\n" + ArmSampler + Line("GO") +
        "    RULE ac_p = 1;\n    WAITT 200;\n" + Line("PUSHED") +
        "    SET ac_s.SPEED, 0;\n    WAITT 32;\n" + Line("HALT") +
        "    RULE pe_vx = 0.2;\n    WAITT 30;\n    RULE pe_vx = 0;\n" + Line("INTO") +
        "    WAITT 8;\n    RULE pe_vx = -0.2;\n    WAITT 40;\n    RULE pe_vx = 0;\n" + Line("BACK") +
        "    WAITT 16;\n    RULE ac_p = 0;\n" + Line("RET") + ClearSampler;

    private static readonly TraceProbeMap ActorPushMap = new(Placements: ["ACTOR ap_k 30 0 180 0"]);

    // ---- beamed inside a thing ---------------------------------------------------------------

    /// <summary>
    /// Three 4-wide things with <c>IF_NEAR</c>/<c>IF_FAR</c>: solid, <c>DIST 0</c> at (24, −10)
    /// (<c>N0</c>); solid, <c>DIST 6</c> at (24, 10) (<c>N6</c>); <c>PASSABLE</c>, <c>DIST 0</c> at
    /// (24, 0) (<c>NP</c>).
    /// </summary>
    private static readonly string BeamInsideDeclarations =
        Skills + ExtraSkills + Textures +
        NearFar("bi_0", "N0", "F0") + NearFar("bi_6", "N6", "F6") + NearFar("bi_p", "NP", "FP") +
        "THING bi_k0 { TEXTURE ac_tex4; IF_NEAR bi_0n; IF_FAR bi_0f; }\n" +
        "THING bi_k6 { TEXTURE ac_tex4; DIST 6; IF_NEAR bi_6n; IF_FAR bi_6f; }\n" +
        "THING bi_kp { TEXTURE ac_tex4; IF_NEAR bi_pn; IF_FAR bi_pf; FLAGS PASSABLE; }\n" +
        PerCycleSampler();

    /// <summary>A beam (no LOCATE) to (<paramref name="x"/>, <paramref name="y"/>) with 16 ticks of V lines, then a 16-tick stand.</summary>
    private static string BeamIn(string tag, double x, double y) =>
        Line(tag) +
        FormattableString.Invariant($"    SET PLAYER_X, {x};\n    SET PLAYER_Y, {y};\n") +
        Line(tag + "D") + "    RULE ac_p = 1;\n    WAITT 16;\n    RULE ac_p = 0;\n" + Line(tag + "S") +
        "    WAITT 16;\n" + Line(tag + "E");

    /// <summary>
    /// Beam 1 step from each thing's centre (slightly off its line), then back to the origin; then
    /// inside the passable thing again and walk +X through it.
    /// </summary>
    private static readonly string BeamInsideRun =
        ArmSampler + Line("GO") +
        BeamIn("K0", 23, -9.5) + BeamIn("O1", 0, 0) +
        BeamIn("K6", 23, 10.5) + BeamIn("O2", 0, 0) +
        BeamIn("KP", 23, 0.5) +
        "    RULE ac_p = 1;\n    RULE pe_vx = 0.2;\n    WAITT 30;\n    RULE pe_vx = 0;\n    RULE ac_p = 0;\n" + Line("KPW") +
        BeamIn("O3", 0, 0) + ClearSampler;

    private static readonly TraceProbeMap BeamInsideMap = new(
        Placements: ["THING bi_k0 24 -10 0 0", "THING bi_k6 24 10 0 0", "THING bi_kp 24 0 0 0"]);

    // ---- MY.DISTANCE refresh -----------------------------------------------------------------

    /// <summary>
    /// <c>CLIP_DIST 10</c>, five 2-wide things read through synonyms: <c>dr_a</c> no DIST at (4, 4)
    /// (8 away, within the border), <c>dr_b</c> no DIST at (20, −8) (28), <c>dr_c</c> DIST 15 at
    /// (24, 8) (32), <c>dr_d</c> DIST 15 LIBER at (24, −16) (40), <c>dr_e</c> no DIST LIBER at
    /// (−10, 16) (26). The <c>V</c> line adds the five <c>DISTANCE</c>s.
    /// </summary>
    private static readonly string DistanceRefreshDeclarations =
        Skills + ExtraSkills + Textures +
        "SKILL dr_va { VAL 0; }\nSKILL dr_vb { VAL 0; }\nSKILL dr_vc { VAL 0; }\nSKILL dr_vd { VAL 0; }\nSKILL dr_ve { VAL 0; }\n" +
        "SYNONYM dr_sa { TYPE THING; }\nSYNONYM dr_sb { TYPE THING; }\nSYNONYM dr_sc { TYPE THING; }\nSYNONYM dr_sd { TYPE THING; }\nSYNONYM dr_se { TYPE THING; }\n" +
        NearFar("dr_", "N", "F") +
        "THING dr_a { TEXTURE ac_tex2; }\n" +
        "THING dr_b { TEXTURE ac_tex2; }\n" +
        "THING dr_c { TEXTURE ac_tex2; DIST 15; IF_NEAR dr_n; IF_FAR dr_f; }\n" +
        "THING dr_d { TEXTURE ac_tex2; DIST 15; IF_NEAR dr_n; IF_FAR dr_f; FLAGS LIBER; }\n" +
        "THING dr_e { TEXTURE ac_tex2; FLAGS LIBER; }\n" +
        PerCycleSampler(ReadDistances, "dr_va", "dr_vb", "dr_vc", "dr_vd", "dr_ve");

    private const string ReadDistances =
        "RULE dr_va = dr_sa.DISTANCE;\nRULE dr_vb = dr_sb.DISTANCE;\nRULE dr_vc = dr_sc.DISTANCE;\nRULE dr_vd = dr_sd.DISTANCE;\nRULE dr_ve = dr_se.DISTANCE;\n";

    /// <summary>
    /// V lines throughout: stand 16 ticks at the origin, walk +X to x ≈ 20 (dr_b within the border
    /// from x ≈ 10, dr_c within DIST from x ≈ 9 and the border from x ≈ 14), stand, walk back, stand.
    /// </summary>
    private static readonly string DistanceRefreshRun =
        "    SET dr_sa, dr_a;\n    SET dr_sb, dr_b;\n    SET dr_sc, dr_c;\n    SET dr_sd, dr_d;\n    SET dr_se, dr_e;\n" +
        ArmSampler + "    RULE ac_p = 1;\n" + Line("GO") + Wait(16, "S0") +
        Walk(0.2, 0, 100, "W1") + Wait(16, "S1") + Walk(-0.2, 0, 100, "W2") + Wait(16, "S2") +
        "    RULE ac_p = 0;\n" + ClearSampler;

    private static readonly TraceProbeMap DistanceRefreshMap = new(
        RoomBody: "CLIP_DIST 10;",
        Placements: ["THING dr_a 4 4 0 0", "THING dr_b 20 -8 0 0", "THING dr_c 24 8 0 0", "THING dr_d 24 -16 0 0", "THING dr_e -10 16 0 0"]);

    // ---- DIST edge -----------------------------------------------------------------------------

    /// <summary>
    /// A 2-wide thing with <c>DIST 10</c> at (30, −20) (<c>NT</c>/<c>FT</c>) and a passable wall with
    /// <c>DIST 10</c> from (40, 10) to (40, 20) (<c>NW</c>/<c>FW</c>).
    /// </summary>
    private static readonly string DistEdgeDeclarations =
        Skills + ExtraSkills + Textures +
        NearFar("de_t", "NT", "FT") + NearFar("de_w", "NW", "FW") +
        "THING de_k { TEXTURE ac_tex2; DIST 10; IF_NEAR de_tn; IF_FAR de_tf; }\n" +
        "WALL de_wall { TEXTURE grn_tex; DIST 10; IF_NEAR de_wn; IF_FAR de_wf; FLAGS PASSABLE; }\n" +
        Sampler();

    private static string Stand(double x, double y, string tag) =>
        Beam(x, y, tag) + Wait(16, tag + "S");

    /// <summary>
    /// Beams to |dx| + |dy| = 10, 9.99 and 10.01 from the thing and from the wall's nearer vertex
    /// (40, 10), each followed by a beam back to the origin.
    /// </summary>
    private static readonly string DistEdgeRun =
        ArmSampler + Line("GO") + Wait(16, "S0") +
        Stand(30, -10, "T10") + Stand(0, 0, "O1") +
        Stand(30, -10.01, "T999") + Stand(30, -9.99, "T1001") + Stand(0, 0, "O2") +
        Stand(30, 10, "W10") + Stand(0, 0, "O3") +
        Stand(30.01, 10, "W999") + Stand(29.99, 10, "W1001") + Stand(0, 0, "O4") +
        ClearSampler;

    private static readonly TraceProbeMap DistEdgeMap = new(
        Placements: ["THING de_k 30 -20 0 0"],
        Walls: [new TraceProbeWall("de_wall", 40, 10, 40, 20)]);

    // ---- round 2 ------------------------------------------------------------------------------

    /// <summary>
    /// Round 2 of distance-refresh: three 2-wide things beside the +X line, read through synonyms:
    /// <c>dr_a</c> no DIST at (40, −6), <c>dr_b</c> DIST 25 at (40, 6), <c>dr_c</c> no DIST at
    /// (−14, 20) (34 from the origin).
    /// </summary>
    private static readonly string RefreshDeclarations =
        Skills + ExtraSkills + Textures +
        "SKILL dr_va { VAL 0; }\nSKILL dr_vb { VAL 0; }\nSKILL dr_vc { VAL 0; }\n" +
        "SYNONYM dr_sa { TYPE THING; }\nSYNONYM dr_sb { TYPE THING; }\nSYNONYM dr_sc { TYPE THING; }\n" +
        NearFar("dr_", "N", "F") +
        "THING dr_a { TEXTURE ac_tex2; }\n" +
        "THING dr_b { TEXTURE ac_tex2; DIST 25; IF_NEAR dr_n; IF_FAR dr_f; }\n" +
        "THING dr_c { TEXTURE ac_tex2; }\n" +
        PerCycleSampler(
            "RULE dr_va = dr_sa.DISTANCE;\nRULE dr_vb = dr_sb.DISTANCE;\nRULE dr_vc = dr_sc.DISTANCE;\n",
            "dr_va", "dr_vb", "dr_vc");

    /// <summary>V lines throughout: stand 16 ticks, walk +X at 0.1 steps per tick to x ≈ 30, stand, walk back, stand.</summary>
    private static readonly string RefreshRun =
        "    SET dr_sa, dr_a;\n    SET dr_sb, dr_b;\n    SET dr_sc, dr_c;\n" +
        ArmSampler + "    RULE ac_p = 1;\n" + Line("GO") + Wait(16, "S0") +
        Walk(0.1, 0, 300, "W1") + Wait(16, "S1") + Walk(-0.1, 0, 300, "W2") + Wait(16, "S2") +
        "    RULE ac_p = 0;\n" + ClearSampler;

    private static TraceProbeMap RefreshMap(string roomBody) => new(
        RoomBody: roomBody,
        Placements: ["THING dr_a 40 -6 0 0", "THING dr_b 40 6 0 0", "THING dr_c -14 20 0 0"]);

    /// <summary>
    /// The 4-wide DIST 6 thing of beam-inside at (24, 10) alone. A: beamed inside it, stand, walk −X
    /// out of DIST, stand. B: beamed 4.5 from its centre (within DIST, no contact), stand, beamed to
    /// the origin, stand. C: beamed inside, stand, beamed 4.5 from it, stand, beamed to the origin.
    /// </summary>
    private static readonly string BeamStateDeclarations =
        Skills + ExtraSkills + Textures +
        NearFar("bs_", "N", "F") +
        "THING bs_k { TEXTURE ac_tex4; DIST 6; IF_NEAR bs_n; IF_FAR bs_f; }\n" +
        PerCycleSampler();

    private static readonly string BeamStateRun =
        ArmSampler + Line("GO") +
        BeamIn("A", 23, 10.5) + Walk(-0.2, 0, 40, "AW") + Wait(16, "AS2") +
        BeamIn("B", 19.5, 10) + BeamIn("BO", 0, 0) +
        BeamIn("C", 23, 10.5) + BeamIn("CM", 19.5, 10) + BeamIn("CO", 0, 0) +
        ClearSampler;

    /// <summary>
    /// Round 3: the beam-state thing read through a synonym, its <c>DISTANCE</c> on every V line.
    /// P: beamed 4.5 from it (within DIST, no contact) before any contact, then to the origin;
    /// Q: beamed inside it; R: beamed 4.5 from it again, then to the origin; W: walk +X from x = 12
    /// along y = 10 to contact.
    /// </summary>
    private static readonly string BeamOrderDeclarations =
        Skills + ExtraSkills + Textures +
        "SYNONYM ac_s { TYPE THING; }\n" +
        NearFar("bs_", "N", "F") +
        "THING bs_k { TEXTURE ac_tex4; DIST 6; IF_NEAR bs_n; IF_FAR bs_f; }\n" +
        PerCycleSampler("RULE ac_ad = ac_s.DISTANCE;\n", "ac_ad");

    private static readonly string BeamOrderRun =
        "    SET ac_s, bs_k;\n" + ArmSampler + Line("GO") + "    WAITT 16;\n" +
        BeamIn("P", 19.5, 10) + BeamIn("PO", 0, 0) +
        BeamIn("Q", 23, 10.5) + BeamIn("QO", 0, 0) +
        BeamIn("R", 19.5, 10) + BeamIn("RO", 0, 0) +
        BeamIn("W", 12, 10) + "    RULE ac_p = 1;\n    RULE pe_vx = 0.2;\n    WAITT 50;\n    RULE pe_vx = 0;\n    RULE ac_p = 0;\n" + Line("WW") +
        BeamIn("WO", 0, 0) + ClearSampler;

    /// <summary>
    /// Round 4: five things of one texture (<paramref name="texture"/>) at x = 30, y = −20, −10, 0,
    /// 10, 20, with the <c>DIST</c>s <paramref name="dists"/> and IF_NEAR/IF_FAR tokens
    /// <c>NA</c>/<c>FA</c> … <c>NE</c>/<c>FE</c>.
    /// </summary>
    private static string DistThresholdDeclarations(string texture, params double[] dists)
    {
        var sb = new StringBuilder(Skills + ExtraSkills + Textures);
        for (int i = 0; i < dists.Length; i++)
        {
            char letter = (char)('a' + i);
            char token = char.ToUpperInvariant(letter);
            sb.Append(NearFar($"dt_{letter}", $"N{token}", $"F{token}"));
            sb.Append(CultureInfo.InvariantCulture, $"THING dt_k{letter} {{ TEXTURE {texture}; DIST {dists[i]}; IF_NEAR dt_{letter}n; IF_FAR dt_{letter}f; }}\n");
        }

        return sb.Append(Sampler()).ToString();
    }

    private static readonly TraceProbeMap DistThresholdMap = new(
        Placements: ["THING dt_ka 30 -20 0 0", "THING dt_kb 30 -10 0 0", "THING dt_kc 30 0 0 0", "THING dt_kd 30 10 0 0", "THING dt_ke 30 20 0 0"]);

    /// <summary>For each thing: beam to x = 16 on its line, set PLAYER_WIDTH, walk +X at 0.1 steps per tick for 120 ticks (to contact), stand, walk back 120 ticks.</summary>
    private static string DistThresholdRun(double width) =>
        ArmSampler + Line("GO") +
        string.Concat(new[] { -20, -10, 0, 10, 20 }.Select((y, i) =>
            FormattableString.Invariant($"    SET PLAYER_X, 16;\n    SET PLAYER_Y, {y};\n    LOCATE;\n    SET PLAYER_WIDTH, {width};\n") +
            Line("L" + (char)('A' + i)) + "    WAITT 8;\n" +
            Walk(0.1, 0, 120, "W" + (char)('A' + i)) + Wait(8, "T" + (char)('A' + i)) +
            Walk(-0.1, 0, 120, "B" + (char)('A' + i)))) +
        ClearSampler;

    /// <summary>
    /// Round 6: a 2-wide thing with DIST 10 at (30, 0) (<c>NA</c>/<c>FA</c>) and a 4-wide one with
    /// DIST 6.5 at (30, 20) (<c>NB</c>/<c>FB</c>). Beams (no contact) to 4, 6 and 5.9 from the first
    /// and to 4 from the second, each followed by a beam back to the origin.
    /// </summary>
    private static readonly string DistInnerDeclarations =
        Skills + ExtraSkills + Textures +
        NearFar("di_a", "NA", "FA") + NearFar("di_b", "NB", "FB") +
        "THING di_ka { TEXTURE ac_tex2; DIST 10; IF_NEAR di_an; IF_FAR di_af; }\n" +
        "THING di_kb { TEXTURE ac_tex4; DIST 6.5; IF_NEAR di_bn; IF_FAR di_bf; }\n" +
        Sampler();

    private static readonly string DistInnerRun =
        ArmSampler + Line("GO") + Wait(16, "S0") +
        Stand(26, 0, "A4") + Stand(0, 0, "O1") +
        Stand(24, 0, "A6") + Stand(0, 0, "O2") +
        Stand(24.1, 0, "A59") + Stand(0, 0, "O3") +
        Stand(26, 20, "B4") + Stand(0, 0, "O4") +
        ClearSampler;

    /// <summary>
    /// Round 7: 2-wide things with DIST 6.5 at (30, −10) (<c>NA</c>) and DIST 8 at (30, 0)
    /// (<c>NB</c>), a 4-wide thing with DIST 5 at (30, 10) (<c>NC</c>). Each line is walked into
    /// with the eye (PLAYER_SIZE and PLAYER_Z) at 3, then at 15.
    /// </summary>
    private static readonly string DistEyeDeclarations =
        Skills + ExtraSkills + Textures +
        NearFar("de_a", "NA", "FA") + NearFar("de_b", "NB", "FB") + NearFar("de_c", "NC", "FC") +
        "THING de_ka { TEXTURE ac_tex2; DIST 6.5; IF_NEAR de_an; IF_FAR de_af; }\n" +
        "THING de_kb { TEXTURE ac_tex2; DIST 8; IF_NEAR de_bn; IF_FAR de_bf; }\n" +
        "THING de_kc { TEXTURE ac_tex4; DIST 5; IF_NEAR de_cn; IF_FAR de_cf; }\n" +
        Sampler();

    private static string DistEyeLine(string tag, double y, double eye) =>
        FormattableString.Invariant($"    SET PLAYER_X, 16;\n    SET PLAYER_Y, {y};\n    LOCATE;\n    SET PLAYER_SIZE, {eye};\n    SET PLAYER_Z, {eye};\n") +
        Line(tag, "PLAYER_Z") + "    WAITT 8;\n" +
        Walk(0.1, 0, 120, tag + "W") + L(tag + "Z", "PLAYER_Z") + Wait(8, tag + "T") + Walk(-0.1, 0, 120, tag + "B");

    private static readonly string DistEyeRun =
        ArmSampler + Line("GO", "PLAYER_Z") +
        DistEyeLine("LA", -10, 3) + DistEyeLine("LB", 0, 3) + DistEyeLine("LC", 10, 3) +
        DistEyeLine("HA", -10, 15) + DistEyeLine("HB", 0, 15) + DistEyeLine("HC", 10, 15) +
        ClearSampler;

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "stop-width",
            "The player pushes +X into solid things 2, 4 and 8 steps wide, with PLAYER_WIDTH 1.2, 2 and 0.5: at what distance from the centre does it stop?",
            StopWidthDeclarations,
            StopWidthRun,
            Predict(
                ("half the width + PLAYER_WIDTH (+ about 0.05)", "A2 x ≈ 21.8, A4 20.8, A8 18.8; B 0.8 less; C 0.7 more"),
                ("PLAYER_WIDTH held at the 1.2 minimum", "C as A"),
                ("contact fires IF_NEAR every pushing cycle", "N2/N4/N8 in every pushing cycle")),
            Map: StopWidthMap),
        new TraceProbeCase(
            "stop-factor",
            "THING_WIDTH and ACTOR_WIDTH (p.114: factors on the texture width for collision): does the stop distance from a 4-wide thing and actor scale with them?",
            StopFactorDeclarations,
            StopFactorRun,
            Predict(
                ("the half width is scaled", "AT, AA x ≈ 20.8; BT 18.8, BA 21.8; CT 21.8, CA 18.8"),
                ("the factors do not apply to the player", "every push stops at x ≈ 20.8")),
            Map: StopFactorMap),
        new TraceProbeCase(
            "actor-push",
            "A 4-wide FOLLOW actor (DIST 2, below the contact distance) walks into a player who stands still: is the player pushed, how far from the actor's centre, and does IF_NEAR fire in every pushing cycle?",
            ActorPushDeclarations(""),
            ActorPushRun,
            Predict(
                ("the actor pushes the player at contact distance", "from about tick 134 the player's X falls with the actor's, ac_ax − PLAYER_X ≈ 3.2"),
                ("contact by the actor fires IF_NEAR every cycle", "N in every pushing cycle, none after HALT"),
                ("the actor stops at contact", "the player stays at the origin; the actor stops at x ≈ 3.2")),
            Map: ActorPushMap),
        new TraceProbeCase(
            "actor-push-careful",
            "The same actor with CAREFULLY (p.73: it avoids walls, things and other actors): does it still push the player?",
            ActorPushDeclarations(" FLAGS CAREFULLY;"),
            ActorPushRun,
            Predict(
                ("as actor-push", "the same pushing and N lines"),
                ("a CAREFULLY actor stops at the player", "the player stays at the origin; N in every cycle the actor is blocked, or once")),
            Map: ActorPushMap),
        new TraceProbeCase(
            "beam-inside",
            "The player is beamed 1 step from the centre of a solid thing (DIST 0, then DIST 6) and of a PASSABLE thing: is it pushed out, where to, and which hooks fire?",
            BeamInsideDeclarations,
            BeamInsideRun,
            Predict(
                ("pushed out to the contact distance at once", "K0: V lines show the player at |dx| + |dy| ≈ 3.2 from (24, −10); N0 once; K6: N6 once, no F6"),
                ("not pushed until the player moves", "K0: the player stays at (23, −9.5)"),
                ("a passable thing does not push", "KP: the player stays at (23, 0.5); KPW walks through")),
            Map: BeamInsideMap),
        new TraceProbeCase(
            "distance-refresh",
            "CLIP_DIST 10: when does a thing's DISTANCE leave 65535.999, for things with and without DIST and LIBER, inside and outside the border?",
            DistanceRefreshDeclarations,
            DistanceRefreshRun,
            Predict(
                ("refreshed every cycle for every thing", "all five read |dx| + |dy| from the first V line"),
                ("refreshed only for things within the border or LIBER", "va, ve from the start; vb from x ≈ 10; vc from x ≈ 14; vd from the start"),
                ("refreshed only once a DIST test fires", "vd from x ≈ 9 (within DIST 15), vc from x ≈ 14; va, vb, ve never")),
            Map: DistanceRefreshMap),
        new TraceProbeCase(
            "dist-edge",
            "Is DIST compared with < or <=? Beams to |dx| + |dy| = 10, 9.99 and 10.01 from a DIST 10 thing and from the nearer vertex of a DIST 10 wall.",
            DistEdgeDeclarations,
            DistEdgeRun,
            Predict(
                ("strictly less", "no NT after T10, NT after T999, FT after T1001; the same for NW"),
                ("less or equal", "NT after T10, FT after O1; no FT after T1001 (T999 is already near)")),
            Map: DistEdgeMap),
        new TraceProbeCase(
            "refresh-20",
            "Round 2. distance-refresh (CLIP_DIST 10) saw a thing's DISTANCE leave 65535.999 once it came within about 15, its DIST or not, and stay current after that. With CLIP_DIST 20, where does that start?",
            RefreshDeclarations,
            RefreshRun,
            Predict(
                ("1.5 × CLIP_DIST (30)", "va from x ≈ 16, vb from x ≈ 16; vc never"),
                ("a fixed 15", "va from x ≈ 31 (never on this walk), vb from x ≈ 21 if DIST counts"),
                ("CLIP_DIST or DIST", "va from x ≈ 26, vb from x ≈ 21")),
            Map: RefreshMap("CLIP_DIST 20;")),
        new TraceProbeCase(
            "refresh-default",
            "Round 2. The same things without a CLIP_DIST: what does the default give?",
            RefreshDeclarations,
            RefreshRun,
            Predict(
                ("a large default border", "va, vb, vc current from the first V line")),
            Map: RefreshMap("")),
        new TraceProbeCase(
            "beam-state",
            "Round 2. beam-inside pushed the player out of a DIST 6 thing with one IF_NEAR and then fired no IF_FAR on a beam out. After such a push, do walking out, a beam within DIST and a beam out fire IF_NEAR/IF_FAR?",
            BeamStateDeclarations,
            BeamStateRun,
            Predict(
                ("the push leaves the near state set", "A: F when walking out; B: N; BO: F; C: N once, CM: nothing, CO: F"),
                ("the push sets nothing and the DIST test misses the pushed position", "A: N next cycle, or nothing until AW, then F")),
            Map: new TraceProbeMap(Placements: ["THING bs_k 24 10 0 0"])),
        new TraceProbeCase(
            "beam-order",
            "Round 3. In beam-state the DIST 6 thing fired only contact IF_NEARs: no IF_NEAR on a beam within DIST, no IF_FAR on leaving it. Does a beam within DIST fire before any contact, and does contact switch the DIST test off?",
            BeamOrderDeclarations,
            BeamOrderRun,
            Predict(
                ("contact switches the DIST test off", "N after P, F after PO; contact N after Q; nothing after R, RO, W"),
                ("this thing's DIST test never fires", "no N/F after P or PO either"),
                ("a walk fires where beams do not", "N during W at |dx| + |dy| < 6")),
            Map: new TraceProbeMap(Placements: ["THING bs_k 24 10 0 0"])),
        new TraceProbeCase(
            "dist-4wide",
            "Round 4. A 4-wide thing ignored DIST 6 but fired at DIST 8. Five 4-wide things with DIST 5, 6, 6.5, 7 and 7.5, each walked into from 14 away: which fire IF_NEAR at DIST?",
            DistThresholdDeclarations("ac_tex4", 5, 6, 6.5, 7, 7.5),
            DistThresholdRun(1.2),
            Predict(
                ("DIST must exceed 2 × (half the width + PLAYER_WIDTH) = 6.4", "NC… only for 6.5, 7, 7.5 at |dx| < DIST; NA, NB only at contact (3.2)"),
                ("DIST must exceed the width + PLAYER_WIDTH = 5.2", "NB… for 6 and up")),
            Map: DistThresholdMap),
        new TraceProbeCase(
            "dist-4wide-pw05",
            "Round 4. The same with PLAYER_WIDTH 0.5: does the threshold move with the player's width?",
            DistThresholdDeclarations("ac_tex4", 5, 6, 6.5, 7, 7.5),
            DistThresholdRun(0.5),
            Predict(
                ("it depends on PLAYER_WIDTH", "a lower threshold than dist-4wide (2 × 2.5 = 5: all five fire)"),
                ("it depends on the object only", "the same things fire as in dist-4wide")),
            Map: DistThresholdMap),
        new TraceProbeCase(
            "dist-2wide",
            "Round 4. Five 2-wide things (contact 2.2) with DIST 3, 4, 4.5, 5 and 6: which fire IF_NEAR at DIST?",
            DistThresholdDeclarations("ac_tex2", 3, 4, 4.5, 5, 6),
            DistThresholdRun(1.2),
            Predict(
                ("DIST must exceed 2 × 2.2 = 4.4", "NC, ND, NE at DIST; NA, NB only at contact"),
                ("DIST must exceed 3.2", "NB… for 4 and up")),
            Map: DistThresholdMap),
        new TraceProbeCase(
            "dist-fine",
            "Round 5. Round 4 found no DIST test at DIST 6 or less, whatever the size and PLAYER_WIDTH, and one at 6.5. Four-wide things with DIST 6.1, 6.2, 6.3, 6.4 and 6.45: where is the limit?",
            DistThresholdDeclarations("ac_tex4", 6.1, 6.2, 6.3, 6.4, 6.45),
            DistThresholdRun(1.2),
            Predict(
                ("a fixed limit L in (6, 6.5]", "the things with DIST > L fire at DIST, the others only at contact")),
            Map: DistThresholdMap),
        new TraceProbeCase(
            "dist-2wide-big",
            "Round 5. Two-wide things with DIST 6.25, 6.5, 7, 8 and 10: do they fire at DIST above the limit, like the 4-wide ones?",
            DistThresholdDeclarations("ac_tex2", 6.25, 6.5, 7, 8, 10),
            DistThresholdRun(1.2),
            Predict(
                ("the limit does not depend on the size", "NB… (6.5 and up) at DIST, as for 4-wide things")),
            Map: DistThresholdMap),
        new TraceProbeCase(
            "dist-inner",
            "Round 6. Every DIST IF_NEAR so far came at |dx| + |dy| ≥ 6, and DIST 6 never fired. Is there a limit on DIST (only DIST > 6 is tested), or an inner band (IF_NEAR only between 6 and DIST)? Beams to 4, 6 and 5.9 from a DIST 10 thing and to 4 from a DIST 6.5 thing.",
            DistInnerDeclarations,
            DistInnerRun,
            Predict(
                ("a limit on DIST", "NA after A4, FA after O1, and the same for A6, A59; NB after B4, FB after O4"),
                ("an inner band [6, DIST)", "NA after A6 only; no NA after A4 or A59; no NB")),
            Map: new TraceProbeMap(Placements: ["THING di_ka 30 0 0 0", "THING di_kb 30 20 0 0"])),
        new TraceProbeCase(
            "dist-eye",
            "Round 7. The DIST limit was about 7 for 2-high things and 6 for 4-high ones with the eye at 8: the eye's height above the object's centre. With the eye at 3 and at 15, do a 2-wide DIST 6.5, a 2-wide DIST 8 and a 4-wide DIST 5 thing fire?",
            DistEyeDeclarations,
            DistEyeRun,
            Predict(
                ("the eye's height above the object's centre must be below DIST", "eye 3: NA, NB, NC at DIST; eye 15: only contact Ns"),
                ("a limit set by the object alone", "eye 3 as eye 8: NB only; eye 15 the same")),
            Map: new TraceProbeMap(Placements: ["THING de_ka 30 -10 0 0", "THING de_kb 30 0 0 0", "THING de_kc 30 10 0 0"])),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "actor-contact",
        TraceFile,
        "ovacp",
        "After the skill dump, `IF_START` selects the trace file, writes `BEGIN` and runs the variant's case.\n" +
        "Each trace line is a token followed by `TICKS` and `pe_n` (the global EACH_TICK sampler's run count)\n" +
        "when it was printed, then the player position and the case's extra skills. `V` lines come from the\n" +
        "sampler in every cycle while `ac_p` is set (TIME_CORR, the player position, then the case's skills).\n" +
        "`N`/`F`-style lines are IF_NEAR/IF_FAR runs (count, MY.DISTANCE, player position). Compare the trace\n" +
        "with `case.json` → `predictions`, and check `OVCON.TXT` for compile errors.",
        All);
}
