// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Text;
using static ProximityProbeCases;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>border-kinds</c> probe
/// (<c>docs/clean-room/observations/OV-20261019-border-kinds.md</c>), the wall and region-border
/// questions that OV-20261019-border-climb and -eye-clamp left open:
/// <list type="bullet">
/// <item><c>*-border</c>: the pad island's boundary walls are <c>IMPASSABLE</c> (p.53), <c>IMPASSABLE,
/// PASSABLE</c>, plain or <c>PASSABLE</c>, the pad level with the room. Does the player enter, and do
/// two way actors, one <c>CAREFULLY</c> (p.73) and one not, cross the pad?</item>
/// <item><c>actor-climb-*</c>: plain pad walls with the pad's floor 1 or 2 above the room's. Does
/// <c>ACTOR_CLIMB</c> (p.113, default 1) stop the two actors, and is the limit inclusive?</item>
/// <item><c>eye-edge</c>: the pad's floor at 3, <c>PLAYER_CLIMB</c> 10, the eye at 2.9 … 3.1. How far
/// above the new floor must the eye lie?</item>
/// <item><c>invisible*</c>: free-standing <c>INVISIBLE</c> walls without <c>PASSABLE</c>, and
/// <c>INVISIBLE</c> pad walls with the pad's floor 4 above the room's. Do they block?</item>
/// </list>
/// Region markers: the room has <c>SKILL1 1</c>, the pad <c>SKILL1 2</c>, printed as <c>HERE.SKILL1</c>.
/// </summary>
internal static class BorderKindsProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVBKX0.TXT).</summary>
    public const string TraceFile = "ovbkx";

    private const string ExtraSkills =
        "SKILL bk_cx { VAL 0; }\nSKILL bk_cy { VAL 0; }\nSKILL bk_ch { VAL 0; }\n" +
        "SKILL bk_px { VAL 0; }\nSKILL bk_py { VAL 0; }\nSKILL bk_ph { VAL 0; }\n";

    private const string ActorTexture = "TEXTURE bk_tex2 { SCALE_X 32; SCALE_Y 32; BMAPS red_map; }\n";

    /// <summary>A trace line with the position, eye height and region marker.</summary>
    private static string Line(string token) =>
        "    RULE pe_h = HERE.SKILL1;\n" + L(token, "PLAYER_X", "PLAYER_Y", "PLAYER_Z", "pe_h");

    private static string NearFarAndRegion(string wallFlags) =>
        Action("bk_near", "    RULE pe_nn = pe_nn + 1;\n    RULE pe_d = MY.DISTANCE;\n    RULE pe_h = HERE.SKILL1;\n" + L("N", "pe_nn", "pe_d", "PLAYER_X", "PLAYER_Y", "pe_h")) +
        Action("bk_far", "    RULE pe_fn = pe_fn + 1;\n    RULE pe_d = MY.DISTANCE;\n    RULE pe_h = HERE.SKILL1;\n" + L("F", "pe_fn", "pe_d", "PLAYER_X", "PLAYER_Y", "pe_h")) +
        Action("bk_enter", Line("ENTER")) +
        Action("bk_leave", Line("LEAVE")) +
        "WALL bk_wall { TEXTURE red_tex; IF_NEAR bk_near; IF_FAR bk_far;" + (wallFlags.Length > 0 ? $" FLAGS {wallFlags};" : "") + " }\n";

    private static TraceProbeMap PadMap(int floor, IReadOnlyList<string>? placements = null) => new(
        RoomBody: "SKILL1 1;",
        PadBody: "SKILL1 2;\n    IF_ENTER bk_enter;\n    IF_LEAVE bk_leave;",
        PadWall: "bk_wall",
        PadFloor: floor,
        Placements: placements);

    // ---- borders with actors -----------------------------------------------------------------

    /// <summary>
    /// Two 2-wide way actors at 0.2 steps per tick: <c>bk_c</c> (<c>CAREFULLY</c>) on the way
    /// (−10.5, 4) ↔ (−10.5, 22) and <c>bk_p</c> (plain) on (−5.5, 4) ↔ (−5.5, 22), both across the pad
    /// (y 12…20). The sampler prints their position and <c>HEIGHT</c> every 16 ticks.
    /// </summary>
    private static string ActorDeclarations(string wallFlags, string texture = ActorTexture) =>
        Skills + ExtraSkills + texture + NearFarAndRegion(wallFlags) +
        "SYNONYM bk_sc { TYPE ACTOR; }\nSYNONYM bk_sp { TYPE ACTOR; }\n" +
        "WAY bk_wc;\nWAY bk_wp;\n" +
        "ACTOR bk_c { TEXTURE bk_tex2; TARGET bk_wc; SPEED 0.2; FLAGS CAREFULLY; }\n" +
        "ACTOR bk_p { TEXTURE bk_tex2; TARGET bk_wp; SPEED 0.2; }\n" +
        Sampler(
            "    RULE pe_h = HERE.SKILL1;\n" +
            "    RULE bk_cx = bk_sc.X;\n    RULE bk_cy = bk_sc.Y;\n    RULE bk_ch = bk_sc.HEIGHT;\n" +
            "    RULE bk_px = bk_sp.X;\n    RULE bk_py = bk_sp.Y;\n    RULE bk_ph = bk_sp.HEIGHT;\n",
            "pe_h", "bk_cx", "bk_cy", "bk_ch", "bk_px", "bk_py", "bk_ph");

    private static readonly string[] ActorPlacements =
    [
        "ACTOR bk_c -10.5 4 90 0", "ACTOR bk_p -5.5 4 90 0",
        "WAY bk_wc -10.5 4 -10.5 22", "WAY bk_wp -5.5 4 -5.5 22",
    ];

    /// <summary>
    /// The player walks from (−8, 6) +Y across the pad's south wall (y = 12) to y ≈ 16, stands and
    /// walks back, then stands at (8, −8) for 400 ticks while the actors patrol.
    /// </summary>
    private static string ActorBorderRun(string extra = "") =>
        "    SET bk_sc, bk_c;\n    SET bk_sp, bk_p;\n" + extra +
        ArmSampler + Line("GO") +
        Beam(-8, 6, "B1", locate: true) + "    WAITT 8;\n" +
        Walk(0, 0.2, 50, "B1W") + Wait(16, "B1T") + Walk(0, -0.2, 50, "B1B") +
        Beam(8, -8, "B2", locate: true) + Wait(400, "B2S") +
        ClearSampler;

    // ---- the eye above the floor ---------------------------------------------------------------

    /// <summary>
    /// One walk: beam with <c>LOCATE</c> to (−8, 8), set <c>PLAYER_SIZE</c> 1, <c>PLAYER_Z</c>
    /// <paramref name="z"/> and <c>PLAYER_CLIMB</c> 10, stand 8, walk +Y at 0.2 for 40 ticks
    /// (across y = 12 after 20), stand 8, walk back, stand 8.
    /// </summary>
    private static string EyeWalk(string tag, double z)
    {
        var sb = new StringBuilder();
        sb.Append(Line(tag));
        sb.Append(CultureInfo.InvariantCulture, $"    SET PLAYER_X, -8;\n    SET PLAYER_Y, 8;\n    LOCATE;\n    SET PLAYER_SIZE, 1;\n    SET PLAYER_Z, {z};\n    SET PLAYER_CLIMB, 10;\n");
        sb.Append(Line(tag + "D"));
        sb.Append("    WAITT 8;\n").Append(Line(tag + "S"));
        sb.Append("    RULE pe_vy = 0.2;\n    WAITT 40;\n    RULE pe_vy = 0;\n").Append(Line(tag + "W"));
        sb.Append("    WAITT 8;\n").Append(Line(tag + "T"));
        sb.Append("    RULE pe_vy = -0.2;\n    WAITT 40;\n    RULE pe_vy = 0;\n").Append(Line(tag + "B"));
        sb.Append("    WAITT 8;\n").Append(Line(tag + "E"));
        return sb.ToString();
    }

    private static readonly string EyeEdgeDeclarations =
        Skills + NearFarAndRegion("") + Sampler("    RULE pe_h = HERE.SKILL1;\n", "pe_h", "PLAYER_Z");

    private static readonly string EyeEdgeRun =
        ArmSampler + Line("GO") +
        EyeWalk("A", 2.9) + EyeWalk("B", 2.99) + EyeWalk("C", 3) + EyeWalk("D", 3.01) + EyeWalk("E", 3.1) +
        ClearSampler;

    // ---- invisible walls -----------------------------------------------------------------------

    /// <summary>
    /// Free-standing walls at x = 24 with <c>IF_NEAR</c> (<c>DIST 0</c>): <c>INVISIBLE</c> from
    /// y −20 to −8 (<c>NA</c>), <c>INVISIBLE, IMPASSABLE</c> from −4 to 4 (<c>NB</c>), <c>PASSABLE</c>
    /// from 8 to 20 (<c>NC</c>), and a plain one at x = 36 from −4 to 4 (<c>ND</c>).
    /// </summary>
    private static readonly string InvisibleDeclarations =
        Skills +
        Action("iv_an", L("NA", "PLAYER_X", "PLAYER_Y")) + Action("iv_bn", L("NB", "PLAYER_X", "PLAYER_Y")) +
        Action("iv_cn", L("NC", "PLAYER_X", "PLAYER_Y")) + Action("iv_dn", L("ND", "PLAYER_X", "PLAYER_Y")) +
        "WALL iv_a { TEXTURE red_tex; IF_NEAR iv_an; FLAGS INVISIBLE; }\n" +
        "WALL iv_b { TEXTURE red_tex; IF_NEAR iv_bn; FLAGS INVISIBLE, IMPASSABLE; }\n" +
        "WALL iv_c { TEXTURE red_tex; IF_NEAR iv_cn; FLAGS PASSABLE; }\n" +
        "WALL iv_d { TEXTURE red_tex; IF_NEAR iv_dn; }\n" +
        Sampler();

    private static string WalkX(string tag, double x, double y, int ticks = 60) =>
        Beam(x, y, tag, locate: true) + "    WAITT 8;\n" + Walk(0.2, 0, ticks, tag + "W") + Wait(8, tag + "T") +
        Walk(-0.2, 0, 20, tag + "B");

    private static readonly string InvisibleRun =
        ArmSampler + Line("GO") +
        WalkX("A", 16, -14) + WalkX("B", 16, 0) + WalkX("C", 16, 14) + WalkX("D", 28, 0) +
        ClearSampler;

    private static readonly TraceProbeMap InvisibleMap = new(
        Walls:
        [
            new TraceProbeWall("iv_a", 24, -20, 24, -8), new TraceProbeWall("iv_b", 24, -4, 24, 4),
            new TraceProbeWall("iv_c", 24, 8, 24, 20), new TraceProbeWall("iv_d", 36, -4, 36, 4),
        ]);

    /// <summary>The pad's floor 4 above the room's (above the default PLAYER_CLIMB 1.5) behind INVISIBLE walls, and a walk out of the pad.</summary>
    private static readonly string InvisibleBorderRun =
        ArmSampler + Line("GO") +
        Beam(-8, 6, "B1", locate: true) + "    WAITT 8;\n" +
        Walk(0, 0.2, 50, "B1W") + Wait(16, "B1T") + Walk(0, -0.2, 50, "B1B") +
        Beam(-8, 16, "B2", locate: true) + "    WAITT 8;\n" +
        Walk(0, -0.2, 50, "B2W") + Wait(16, "B2T") + Walk(0, 0.2, 50, "B2B") +
        ClearSampler;

    private static readonly string InvisibleBorderDeclarations =
        Skills + NearFarAndRegion("INVISIBLE") + Sampler("    RULE pe_h = HERE.SKILL1;\n", "pe_h", "PLAYER_Z");

    /// <summary>
    /// Round 2 of the actor-climb cases: <c>SKIP_FRAMES</c> 0 (p.113: unseen actors outside
    /// <c>DIST</c> move only every 6th frame by default), the climb skills set by
    /// <paramref name="settings"/> and printed once.
    /// </summary>
    private static string ClimbRound2Run(string settings) =>
        ActorBorderRun("    SET SKIP_FRAMES, 0;\n" + settings) +
        L("SKL", "ACTOR_CLIMB", "PLAYER_CLIMB", "SKIP_FRAMES");

    private static TraceProbeCase ClimbRound2(string label, string question, int floor, string settings, params (string Reading, string Trace)[] predictions) =>
        new(label, "Round 2. " + question, ActorDeclarations(""), ClimbRound2Run(settings), Predict(predictions), Map: PadMap(floor, ActorPlacements));

    /// <summary>A 2-wide, 8-high actor texture (round 3).</summary>
    private const string TallTexture = "TEXTURE bk_tex2 { SCALE_X 32; SCALE_Y 8; BMAPS red_map; }\n";

    /// <summary>The round-3 cases, appended to <see cref="All"/>.</summary>
    private static readonly TraceProbeCase[] Round3 =
    [
        new(
            "climb-tall",
            "Round 3. ACTOR_CLIMB 3 did not lift the 2-high CAREFULLY actor onto a pad 2 steps up. An 8-high actor with ACTOR_CLIMB 3: does it climb now (a cap at the actor's height)?",
            ActorDeclarations("", TallTexture),
            ClimbRound2Run("    SET ACTOR_CLIMB, 3;\n"),
            Predict(
                ("the step must stay below the actor's height", "bk_cy runs 4 ↔ 22"),
                ("a fixed cap below 2", "bk_cy stays below 11")),
            Map: PadMap(2, ActorPlacements)),
        new(
            "climb-eq",
            "Round 3. A pad 1 step up with ACTOR_CLIMB exactly 1: is the limit inclusive?",
            ActorDeclarations(""),
            ClimbRound2Run("    SET ACTOR_CLIMB, 1;\n"),
            Predict(
                ("a step equal to ACTOR_CLIMB is climbed", "bk_cy runs 4 ↔ 22"),
                ("it must be below ACTOR_CLIMB", "bk_cy stays below 11")),
            Map: PadMap(1, ActorPlacements)),
    ];

    /// <summary>The round-2 cases, appended to <see cref="All"/>.</summary>
    private static readonly TraceProbeCase[] Round2 =
    [
        ClimbRound2(
            "climb-player-3",
            "ACTOR_CLIMB 3 did not let the CAREFULLY actor onto a pad 2 steps up. With PLAYER_CLIMB 3 instead (ACTOR_CLIMB at its default), does it climb?",
            2,
            "    SET PLAYER_CLIMB, 3;\n",
            ("CAREFULLY actors use PLAYER_CLIMB", "bk_cy runs 4 ↔ 22"),
            ("something else blocks a 2-step rise", "bk_cy stays below 11")),
        ClimbRound2(
            "climb-actor-05",
            "A pad 1 step up with ACTOR_CLIMB 0.5: is the actor stopped?",
            1,
            "    SET ACTOR_CLIMB, 0.5;\n",
            ("ACTOR_CLIMB is the limit", "bk_cy stays below 11"),
            ("ACTOR_CLIMB is ignored", "bk_cy runs 4 ↔ 22")),
        ClimbRound2(
            "climb-player-05",
            "A pad 1 step up with PLAYER_CLIMB 0.5: is the actor stopped?",
            1,
            "    SET PLAYER_CLIMB, 0.5;\n",
            ("CAREFULLY actors use PLAYER_CLIMB", "bk_cy stays below 11"),
            ("PLAYER_CLIMB does not apply to actors", "bk_cy runs 4 ↔ 22")),
        ClimbRound2(
            "climb-skip0",
            "A pad 2 steps up at the defaults, with SKIP_FRAMES 0: do the actor's 6-frame jumps over the pad stop?",
            2,
            "",
            ("SKIP_FRAMES 0 moves unseen actors every frame", "bk_cy stays at about 10.95 after B2; no jump to 20.5"),
            ("SKIP_FRAMES does not change it", "the jumps of actor-climb-2d")),
    ];

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "imp-border",
            "IMPASSABLE pad walls, the pad level with the room: does the player enter the pad, and does each actor (CAREFULLY or not) cross into it?",
            ActorDeclarations("IMPASSABLE"),
            ActorBorderRun(),
            Predict(
                ("IMPASSABLE blocks everyone", "no ENTER; the player stops at y ≈ 10.75 with N every pushing cycle; bk_cy stays below 11, bk_py too (or passes: plain actors do not collide)"),
                ("IMPASSABLE only blocks actors", "ENTER and LEAVE; bk_cy stays below 11"),
                ("plain actors ignore walls", "bk_py runs 4 ↔ 22 in every border case")),
            Map: PadMap(0, ActorPlacements)),
        new TraceProbeCase(
            "imp-pass-border",
            "IMPASSABLE and PASSABLE pad walls (p.53: passable for the player, not for actors).",
            ActorDeclarations("IMPASSABLE, PASSABLE"),
            ActorBorderRun(),
            Predict(
                ("as the manual says", "ENTER and LEAVE; bk_cy stays below 11"),
                ("PASSABLE wins for actors too", "bk_cy runs 4 ↔ 22")),
            Map: PadMap(0, ActorPlacements)),
        new TraceProbeCase(
            "plain-border",
            "Plain pad walls (control): the player crossed them in OV-20261019-clip-contact. Do the actors cross?",
            ActorDeclarations(""),
            ActorBorderRun(),
            Predict(
                ("region borders are portals for actors", "bk_cy and bk_py run 4 ↔ 22; ENTER and LEAVE")),
            Map: PadMap(0, ActorPlacements)),
        new TraceProbeCase(
            "actor-climb-1",
            "Plain pad walls, the pad's floor 1 above the room's, ACTOR_CLIMB at its default (p.113: 1): do the actors climb onto the pad? HEIGHT shows whether they follow its floor.",
            ActorDeclarations(""),
            ActorBorderRun(),
            Predict(
                ("a step equal to ACTOR_CLIMB is climbed", "bk_cy runs 4 ↔ 22"),
                ("a step equal to ACTOR_CLIMB blocks (as PLAYER_CLIMB)", "bk_cy stays below 11"),
                ("only CAREFULLY actors test steps", "bk_py runs 4 ↔ 22 in any case")),
            Map: PadMap(1, ActorPlacements)),
        new TraceProbeCase(
            "actor-climb-2",
            "The pad's floor 2 above the room's, ACTOR_CLIMB 3 set at the start: do the actors climb now?",
            ActorDeclarations(""),
            ActorBorderRun("    SET ACTOR_CLIMB, 3;\n"),
            Predict(
                ("ACTOR_CLIMB is the actors' step limit", "bk_cy runs 4 ↔ 22"),
                ("ACTOR_CLIMB has no effect", "as at the default")),
            Map: PadMap(2, ActorPlacements)),
        new TraceProbeCase(
            "actor-climb-2d",
            "The pad's floor 2 above the room's with the default ACTOR_CLIMB: are the actors stopped?",
            ActorDeclarations(""),
            ActorBorderRun(),
            Predict(
                ("a 2-step rise is above the default 1", "bk_cy stays below 11"),
                ("actors do not test steps", "bk_cy runs 4 ↔ 22")),
            Map: PadMap(2, ActorPlacements)),
        new TraceProbeCase(
            "eye-edge",
            "Plain pad walls, the pad's floor at 3, PLAYER_SIZE 1 and PLAYER_CLIMB 10 (so only the eye test can block): with the eye at 2.9, 2.99, 3, 3.01 and 3.1, which walks enter the pad?",
            EyeEdgeDeclarations,
            EyeEdgeRun,
            Predict(
                ("the eye strictly above the floor", "A, B, C blocked; D, E enter"),
                ("the eye at or above the floor", "A, B blocked; C, D, E enter"),
                ("a margin (e.g. 0.1)", "only E enters")),
            Map: PadMap(3)),
        new TraceProbeCase(
            "invisible",
            "Free-standing walls: INVISIBLE without PASSABLE, INVISIBLE with IMPASSABLE, PASSABLE and plain: which block the player, and does contact fire IF_NEAR?",
            InvisibleDeclarations,
            InvisibleRun,
            Predict(
                ("INVISIBLE walls are solid unless PASSABLE", "A, B, D stop at x ≈ 22.75 / 34.75 with N every pushing cycle; C walks through with one NC"),
                ("INVISIBLE walls are passable", "A walks through (NA once), B and D stop")),
            Map: InvisibleMap),
        new TraceProbeCase(
            "invisible-border",
            "INVISIBLE pad walls (no PASSABLE), the pad's floor 4 above the room's: does the climb test still block, and can the player walk off the pad?",
            InvisibleBorderDeclarations,
            InvisibleBorderRun,
            Predict(
                ("INVISIBLE region borders follow the climb rule", "B1 blocked at y ≈ 10.75 with N; B2 walks out with LEAVE"),
                ("INVISIBLE region borders are passable", "B1 enters the pad")),
            Map: PadMap(4)),
        .. Round2,
        .. Round3,
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "border-kinds",
        TraceFile,
        "ovbkp",
        "After the skill dump, `IF_START` selects the trace file, writes `BEGIN` and runs the variant's case.\n" +
        "Each trace line is a token followed by `TICKS` and `pe_n` (the global EACH_TICK sampler's run count)\n" +
        "when it was printed, then the case's extra skills. `pe_h` is HERE.SKILL1 (room 1, pad 2). In the\n" +
        "actor cases the sampler's `TK` lines add the CAREFULLY actor's X, Y, HEIGHT (`bk_c*`) and the plain\n" +
        "actor's (`bk_p*`). Compare the trace with `case.json` → `predictions`, and check `OVCON.TXT` for\n" +
        "compile errors.",
        All);
}
