// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Text;
using static ProximityProbeCases;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>distance-refresh</c> probe
/// (<c>docs/clean-room/observations/OV-20261021-distance-refresh.md</c>), the questions
/// OV-20261020-view-activity left open. That probe found that the original refreshes every actor's
/// <c>DISTANCE</c> only in cycles in which the player moved or an actor in front of the player
/// moved (its rule 5), and that the <c>SKIP_FRAMES</c> near test reads the refreshed value.
/// <list type="bullet">
/// <item><c>near-hook</c>: do <c>IF_NEAR</c>/<c>IF_FAR</c> and the <c>DIST</c> test that lifts
/// <c>SKIP_FRAMES</c> read that stale value too? Two actors walk in beside the player, where their
/// moves refresh nothing, to well within their <c>DIST</c>; then the player steps aside.</item>
/// <item><c>turn-refresh</c>: does turning (or tilting) the player refresh it?</item>
/// <item><c>thing-move</c>, <c>set-actor</c>: does a script's <c>SET</c> of a thing's or actor's
/// position, behind or in front of the player, refresh it, and is a thing's <c>DISTANCE</c> stale
/// in the same way?</item>
/// <item><c>region-seen</c>: a region's <c>#VISIBLE</c> and <c>SEEN</c> (p.53).</item>
/// </list>
/// While <c>dr_p</c> is set the sampler prints a <c>C</c> line in every cycle.
/// </summary>
internal static class DistanceRefreshProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVDRX0.TXT).</summary>
    public const string TraceFile = "ovdrx";

    /// <summary>The 2 × 2 sprite texture of every thing and actor.</summary>
    private const string Texture = "TEXTURE dr_tex { SCALE_X 32; SCALE_Y 32; BMAPS red_map; }\n";

    /// <summary>
    /// <c>IF_NEAR</c>/<c>IF_FAR</c> actions that print <c>N</c> or <c>F</c> with <c>MY.X</c>,
    /// <c>MY.Y</c> and <c>MY.DISTANCE</c>.
    /// </summary>
    private static readonly string Hooks =
        "SKILL dr_hx { VAL 0; }\nSKILL dr_hy { VAL 0; }\nSKILL dr_hd { VAL 0; }\n" +
        Action("dr_near", "    RULE dr_hx = MY.X;\n    RULE dr_hy = MY.Y;\n    RULE dr_hd = MY.DISTANCE;\n" + L("N", "dr_hx", "dr_hy", "dr_hd")) +
        Action("dr_far", "    RULE dr_hx = MY.X;\n    RULE dr_hy = MY.Y;\n    RULE dr_hd = MY.DISTANCE;\n" + L("F", "dr_hx", "dr_hy", "dr_hd"));

    /// <summary>
    /// An object the sampler prints through the synonym <c>dr_s</c><i>i</i>: X, Y and
    /// <c>DISTANCE</c> for things and actors, <c>VISIBLE</c> and <c>SEEN</c> for regions.
    /// </summary>
    private sealed record Watched(string Type, string Target);

    /// <summary>A script move: while <c>dr_m</c> equals <paramref name="Phase"/>, adds <paramref name="Delta"/> to watched object <paramref name="Index"/>'s <paramref name="Member"/> in every cycle.</summary>
    private sealed record ScriptMove(int Phase, int Index, string Member, double Delta);

    /// <summary>
    /// The sampler: in every cycle sets each way actor's (<paramref name="wayActors"/>, 1-based
    /// watched indices) <c>SPEED</c> to its step skill's steps per cycle (<c>dr_st</c>, or
    /// <c>dr_sf</c> for round 2's walker in front) over <c>TIME_CORR</c> (so a step does not depend
    /// on the emulator's rate), makes the <paramref name="moves"/> of the
    /// current <c>dr_m</c> phase, and while <c>dr_p</c> is set prints <c>C</c> with the watched
    /// objects' values, then <paramref name="extra"/>.
    /// </summary>
    private static string WatchSampler(Watched[] watched, (int Index, string Step)[] wayActors, ScriptMove[] moves, params string[] extra)
    {
        var skills = new StringBuilder("SKILL dr_p { VAL 0; }\nSKILL dr_st { VAL 0; }\nSKILL dr_sf { VAL 0; }\nSKILL dr_m { VAL 0; }\n");
        var rules = new StringBuilder();
        var printed = new List<string>();
        for (int i = 1; i <= watched.Length; i++)
        {
            skills.Append(CultureInfo.InvariantCulture, $"SYNONYM dr_s{i} {{ TYPE {watched[i - 1].Type}; }}\n");
            if (watched[i - 1].Type == "REGION")
            {
                skills.Append(CultureInfo.InvariantCulture, $"SKILL dr_v{i} {{ VAL 0; }}\nSKILL dr_e{i} {{ VAL 0; }}\n");
                rules.Append(CultureInfo.InvariantCulture, $"    IF (dr_s{i}.VISIBLE == 1) {{\n        RULE dr_v{i} = 1;\n    }} ELSE {{\n        RULE dr_v{i} = 0;\n    }}\n");
                rules.Append(CultureInfo.InvariantCulture, $"    IF (dr_s{i}.SEEN == 1) {{\n        RULE dr_e{i} = 1;\n    }} ELSE {{\n        RULE dr_e{i} = 0;\n    }}\n");
                printed.AddRange([$"dr_v{i}", $"dr_e{i}"]);
            }
            else
            {
                skills.Append(CultureInfo.InvariantCulture, $"SKILL dr_x{i} {{ VAL 0; }}\nSKILL dr_y{i} {{ VAL 0; }}\nSKILL dr_d{i} {{ VAL 0; }}\n");
                rules.Append(CultureInfo.InvariantCulture, $"    RULE dr_x{i} = dr_s{i}.X;\n    RULE dr_y{i} = dr_s{i}.Y;\n    RULE dr_d{i} = dr_s{i}.DISTANCE;\n");
                printed.AddRange([$"dr_x{i}", $"dr_y{i}", $"dr_d{i}"]);
            }
        }

        var control = new StringBuilder();
        if (wayActors.Length > 0)
        {
            control.Append("    IF (TIME_CORR > 0) {\n");
            foreach ((int i, string step) in wayActors)
            {
                control.Append(CultureInfo.InvariantCulture, $"        RULE dr_s{i}.SPEED = {step} / TIME_CORR;\n");
            }

            control.Append("    }\n");
        }

        foreach (ScriptMove move in moves)
        {
            control.Append(FormattableString.Invariant(
                $"    IF (dr_m == {move.Phase}) {{\n        RULE dr_s{move.Index}.{move.Member} = dr_s{move.Index}.{move.Member} {(move.Delta < 0 ? "-" : "+")} {Math.Abs(move.Delta)};\n    }}\n"));
        }

        return skills + Sampler(
            control + "    IF (dr_p > 0) {\n" + Indent(rules.ToString()) +
            Indent(L("C", [.. printed, .. extra])) + "    }\n");
    }

    private static string Ways(int count) =>
        string.Concat(Enumerable.Range(1, count).Select(static i => $"WAY dr_w{i};\n"));

    private static string Face(double angle) =>
        FormattableString.Invariant($"    SET PLAYER_ANGLE, {angle:0.####};\n");

    private static string Step(double stepPerCycle) =>
        FormattableString.Invariant($"    RULE dr_st = {stepPerCycle};\n");

    private static string Phase(int phase) => $"    RULE dr_m = {phase};\n";

    private static string Cycles(int cycles, string token) => $"    WAIT {cycles};\n" + L(token, "PLAYER_X", "PLAYER_Y", "PLAYER_ANGLE");

    /// <summary>Binds <c>dr_s</c><i>i</i> to each watched object, arms the sampler and prints <c>GO</c>.</summary>
    private static string Start(Watched[] watched) =>
        string.Concat(watched.Select(static (w, i) => $"    SET dr_s{i + 1}, {w.Target};\n")) +
        ArmSampler + "    RULE dr_p = 1;\n" + L("GO", "PLAYER_X", "PLAYER_Y", "PLAYER_ANGLE");

    private const string Stop = "    RULE dr_p = 0;\n    RULE dr_st = 0;\n    RULE dr_sf = 0;\n    RULE dr_m = 0;\n" + ClearSampler;

    // ---- near-hook: IF_NEAR / IF_FAR and the DIST lift on a stale DISTANCE ----------------------------

    /// <summary>
    /// Two way actors with <c>DIST 10</c> walk in beside the player at the origin (facing +X), where
    /// view-activity's round 9 found that their moves refresh nothing: <c>dr_a1</c> on the −Y axis
    /// from 22 to 4 with <c>IF_NEAR</c>/<c>IF_FAR</c>, <c>dr_a2</c> on the +Y axis from 22 to 4
    /// without hooks. Both are out of view and, at a stale 22, beyond the near range of 20, so they
    /// skip frames unless their <c>DIST</c> test lifts it. With the eye at 8 and the sprites' centre
    /// at 1 the vertical offset is 7, inside <c>DIST 10</c>.
    /// </summary>
    private static readonly Watched[] NearWatched = [new("ACTOR", "dr_a1"), new("ACTOR", "dr_a2")];

    private static readonly string NearDeclarations =
        Skills + Texture + Hooks + Ways(2) +
        "ACTOR dr_a1 { TEXTURE dr_tex; TARGET dr_w1; SPEED 0; DIST 10; IF_NEAR dr_near; IF_FAR dr_far; }\n" +
        "ACTOR dr_a2 { TEXTURE dr_tex; TARGET dr_w2; SPEED 0; DIST 10; }\n" +
        WatchSampler(NearWatched, [(1, "dr_st"), (2, "dr_st")], []);

    private static readonly string[] NearPlacements =
        ["ACTOR dr_a1 0 -22 0 0", "ACTOR dr_a2 0 22 0 0", "WAY dr_w1 0 -22 0 -4", "WAY dr_w2 0 22 0 4"];

    /// <summary>
    /// W1: 85 cycles walking in at 0.2 a cycle (to about 5 from the player); S1: 12 standing; a beam
    /// to (0.5, 0), which refreshes every <c>DISTANCE</c>, and S2: 12 standing; W2: 85 walking back
    /// out; S3: 12 standing; a beam back to the origin and S4: 12 standing.
    /// </summary>
    private static readonly string NearRun =
        Start(NearWatched) + Face(0) +
        Step(0.2) + Cycles(85, "W1") + Step(0) + Cycles(12, "S1") +
        Beam(0.5, 0, "B1") + Cycles(12, "S2") +
        Step(0.2) + Cycles(85, "W2") + Step(0) + Cycles(12, "S3") +
        Beam(0, 0, "B2") + Cycles(12, "S4") + Stop;

    // ---- turn-refresh -----------------------------------------------------------------------------------

    /// <summary>
    /// One way actor on the −Y axis from 22 to 12 at 0.1 a cycle, <c>DIST 1</c>; at the angles
    /// used (0, 0.3, π/2, 2.8) it is beside or behind the player, so its own moves refresh nothing.
    /// </summary>
    private static readonly Watched[] IndicatorWatched = [new("ACTOR", "dr_a1")];

    private const string IndicatorActor = "ACTOR dr_a1 { TEXTURE dr_tex; TARGET dr_w1; SPEED 0; DIST 1; }\n";

    private static readonly string[] IndicatorPlacements = ["ACTOR dr_a1 0 -22 0 0", "WAY dr_w1 0 -22 0 -12"];

    private static readonly string TurnDeclarations =
        Skills + Texture + Ways(1) + IndicatorActor + WatchSampler(IndicatorWatched, [(1, "dr_st")], [], "PLAYER_TILT");

    /// <summary>18 cycles each facing 0, 0.3, π/2, 2.8 and 0, then 18 with <c>PLAYER_TILT</c> 0.3 and 18 with it back at 0.</summary>
    private static readonly string TurnRun =
        Start(IndicatorWatched) + Step(0.1) +
        Face(0) + Cycles(18, "T0") + Face(0.3) + Cycles(18, "T1") + Face(Math.PI / 2) + Cycles(18, "T2") +
        Face(2.8) + Cycles(18, "T3") + Face(0) + Cycles(18, "T4") +
        "    SET PLAYER_TILT, 0.3;\n" + Cycles(18, "T5") + "    SET PLAYER_TILT, 0;\n" + Cycles(18, "T6") + Stop;

    // ---- thing-move and set-actor: a script's SET of a position ------------------------------------------

    /// <summary>
    /// The indicator actor of <see cref="TurnDeclarations"/>, and two objects of
    /// <paramref name="type"/> with <c>DIST 10</c> and the hooks: <c>dr_o2</c> behind the player at
    /// (−7, 4), <c>dr_o3</c> in front of it at (7, 4), each 11 away. M1 moves <c>dr_o2</c> +0.1 in X
    /// a cycle for 20 cycles (to 9 away, within DIST from the 10th), M3 moves <c>dr_o3</c> −0.1.
    /// </summary>
    private static string SetDeclarations(string type) =>
        Skills + Texture + Hooks + Ways(1) + IndicatorActor +
        $"{type} dr_o2 {{ TEXTURE dr_tex; DIST 10; IF_NEAR dr_near; IF_FAR dr_far; }}\n" +
        $"{type} dr_o3 {{ TEXTURE dr_tex; DIST 10; IF_NEAR dr_near; IF_FAR dr_far; }}\n" +
        WatchSampler(SetWatched(type), [(1, "dr_st")], [new ScriptMove(1, 2, "X", 0.1), new ScriptMove(3, 3, "X", -0.1)]);

    private static Watched[] SetWatched(string type) => [new("ACTOR", "dr_a1"), new(type, "dr_o2"), new(type, "dr_o3")];

    private static string[] SetPlacements(string type) =>
        [.. IndicatorPlacements, $"{type} dr_o2 -7 4 0 0", $"{type} dr_o3 7 4 0 0"];

    /// <summary>From the origin facing +X: M0 15 cycles; M1 20 moving the one behind; M2 10; M3 20 moving the one in front; M4 15.</summary>
    private static string SetRun(string type) =>
        Start(SetWatched(type)) + Step(0.1) + Face(0) +
        Cycles(15, "M0") + Phase(1) + Cycles(20, "M1") + Phase(0) + Cycles(10, "M2") +
        Phase(3) + Cycles(20, "M3") + Phase(0) + Cycles(15, "M4") + Stop;

    // ---- region-seen ------------------------------------------------------------------------------------

    /// <summary>
    /// The pad (x −12…−4, y 12…20, the room's heights, invisible passable border walls) and the room,
    /// each region's <c>VISIBLE</c> and <c>SEEN</c> printed; a plain free-standing wall from
    /// (−10, −6) to (−6, −6) hides the whole pad from (−8, −8) facing +Y, and nothing of it from the
    /// origin or from (−3, −8).
    /// </summary>
    private static readonly Watched[] RegionWatched = [new("REGION", "pad"), new("REGION", "room")];

    private static readonly string RegionDeclarations =
        Skills + WatchSampler(RegionWatched, [], [], "PLAYER_X", "PLAYER_Y", "PLAYER_ANGLE");

    /// <summary>
    /// 8 cycles each: R1 the origin facing +X (the pad at bearings 1.77–2.36, out of the ±0.5 cone);
    /// R2 facing 2.03 (the pad's centre); R3 facing +X; R4 facing 1.2 (the pad's nearest corner 0.07
    /// outside the cone); R5 facing 1.35 (that corner inside); R6 at (−8, −8) facing +Y (the pad in
    /// the cone, behind the wall); R7 at (−3, −8) facing +Y (past the wall's end).
    /// </summary>
    private static readonly string RegionRun =
        Start(RegionWatched) +
        Face(0) + Cycles(8, "R1") + Face(2.0344) + Cycles(8, "R2") + Face(0) + Cycles(8, "R3") +
        Face(1.2) + Cycles(8, "R4") + Face(1.35) + Cycles(8, "R5") +
        Beam(-8, -8, "R6", locate: true) + Face(Math.PI / 2) + Cycles(8, "R6S") +
        Beam(-3, -8, "R7", locate: true) + Cycles(8, "R7S") + Stop;

    // ---- round 2: what else refreshes, and things ----------------------------------------------------

    /// <summary>
    /// Round 2. The −Y indicator walker, a thing <c>dr_o2</c> behind the player at (−7, 4) with
    /// <c>DIST 10</c> and the hooks, and a way actor <c>dr_a3</c> in front of the player's plane,
    /// out of the cone, from (10, 22) to (10, 18), walking only while <c>dr_sf</c> is set. M1 and
    /// M3 move the thing 2 in and back out by script, which round 1 found refreshes nothing.
    /// </summary>
    private static readonly Watched[] ThingRefreshWatched = [new("ACTOR", "dr_a1"), new("THING", "dr_o2"), new("ACTOR", "dr_a3")];

    private static readonly string ThingRefreshDeclarations =
        Skills + Texture + Hooks + Ways(3) + IndicatorActor +
        "THING dr_o2 { TEXTURE dr_tex; DIST 10; IF_NEAR dr_near; IF_FAR dr_far; }\n" +
        "ACTOR dr_a3 { TEXTURE dr_tex; TARGET dr_w3; SPEED 0; DIST 1; }\n" +
        WatchSampler(ThingRefreshWatched, [(1, "dr_st"), (3, "dr_sf")], [new ScriptMove(1, 2, "X", 0.1), new ScriptMove(3, 2, "X", -0.1)], "PLAYER_Z", "PLAYER_ARC");

    private static readonly string[] ThingRefreshPlacements =
        [.. IndicatorPlacements, "THING dr_o2 -7 4 0 0", "ACTOR dr_a3 10 22 0 0", "WAY dr_w3 10 22 10 18"];

    /// <summary>
    /// M0 10 cycles; M1 20 moving the thing in to 9 away; M2 10; TA 12 after a turn to 0.3; M3 20
    /// moving it back out to 11; M4 10; FW 12 with the walker in front walking; FS 12 after it stops;
    /// PZ 12 after lowering the eye to 5; PA 12 after setting <c>PLAYER_ARC</c> 0.8.
    /// </summary>
    private static readonly string ThingRefreshRun =
        Start(ThingRefreshWatched) + Step(0.1) + Face(0) +
        Cycles(10, "M0") + Phase(1) + Cycles(20, "M1") + Phase(0) + Cycles(10, "M2") +
        Face(0.3) + Cycles(12, "TA") +
        Phase(3) + Cycles(20, "M3") + Phase(0) + Cycles(10, "M4") +
        "    RULE dr_sf = 0.1;\n" + Cycles(12, "FW") + "    RULE dr_sf = 0;\n" + Cycles(12, "FS") +
        "    SET PLAYER_SIZE, 5;\n    SET PLAYER_Z, 5;\n" + Cycles(12, "PZ") +
        "    SET PLAYER_ARC, 0.8;\n" + Cycles(12, "PA") + "    SET PLAYER_ARC, 1;\n" + Stop;

    /// <summary>The round-2 cases, appended to <see cref="All"/>.</summary>
    private static readonly TraceProbeCase[] Round2 =
    [
        new(
            "thing-refresh",
            "Round 2. Round 1 froze a thing's DISTANCE while a script moved it. Is it refreshed with the actors' by a turn and by an actor walking in front, does its IF_NEAR/IF_FAR then fire, and does lowering the eye (PLAYER_Z) or changing PLAYER_ARC refresh?",
            ThingRefreshDeclarations,
            ThingRefreshRun,
            Predict(
                ("things refresh with the actors", "dr_d2 9 right after TA with an N line; 11 during FW with an F line"),
                ("a thing's DISTANCE is only set at load", "dr_d2 stays 11, no N or F"),
                ("PLAYER_Z refreshes", "dr_d1 changes once after FS"),
                ("PLAYER_ARC refreshes", "dr_d1 changes once after PZ")),
            Map: new TraceProbeMap(Placements: ThingRefreshPlacements)),
    ];

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new(
            "near-hook",
            "Do IF_NEAR/IF_FAR and the DIST test that lifts SKIP_FRAMES read the stale DISTANCE of view-activity rule 5? Two DIST 10 actors walk in beside the player (moves that refresh nothing) from 22 to about 5, dr_a1 with IF_NEAR/IF_FAR, dr_a2 without; then a beam 0.5 aside refreshes, they walk back out, and a beam back refreshes again.",
            NearDeclarations,
            NearRun,
            Predict(
                ("IF_NEAR tests the current distance", "an N line in W1 once dr_y1 > −10, with dr_hd under 10; an F line in W2 once dr_y1 < −10"),
                ("IF_NEAR tests the stale DISTANCE", "no N in W1 or S1; N right after B1 (dr_hd 4.5 or so); no F in W2 or S3; F right after B2"),
                ("the DIST test refreshes its own actor", "dr_d1 follows dr_y1 while dr_d2 stays at 22"),
                ("the SKIP_FRAMES DIST lift tests the current distance", "dr_y2 changes every cycle once it is under 10 in W1"),
                ("the DIST lift tests the stale DISTANCE", "dr_y2 changes every 6th cycle all through W1")),
            Map: new TraceProbeMap(Placements: NearPlacements)),
        new(
            "turn-refresh",
            "Does turning or tilting the player refresh DISTANCE? A walker on the −Y axis from 22 to 12 (beside or behind the player at every angle used, its own moves refreshing nothing); the player turns to 0.3, π/2, 2.8 and back to 0, then tilts 0.3 and back.",
            TurnDeclarations,
            TurnRun,
            Predict(
                ("a turn refreshes", "dr_d1 changes once right after each turn, to the current |dy|"),
                ("only position changes refresh", "dr_d1 stays at 22 throughout while dr_y1 walks to −12"),
                ("a tilt refreshes", "dr_d1 changes once after T4 and once after T5")),
            Map: new TraceProbeMap(Placements: IndicatorPlacements)),
        new(
            "thing-move",
            "Does a script SET of a thing's X refresh DISTANCE, and does a thing's own DISTANCE follow it? The −Y walker as indicator; things with DIST 10 and IF_NEAR/IF_FAR 11 away behind (−7, 4) and in front (7, 4) of the player, moved toward it 0.1 a cycle for 20 cycles one after the other.",
            SetDeclarations("THING"),
            SetRun("THING"),
            Predict(
                ("a thing's DISTANCE follows its SET", "dr_d2 goes 11 → 9 in M1, dr_d3 in M3"),
                ("a thing's DISTANCE is refreshed with the actors'", "dr_d2 stays at 11 in M1"),
                ("a moving thing in front refreshes every DISTANCE", "dr_d1 follows dr_y1 in M3 only"),
                ("things do not refresh", "dr_d1 stays at 22 throughout"),
                ("IF_NEAR on a thing tests the current distance", "an N line in M1 with dr_hx ≈ −6 and one in M3 with dr_hx ≈ 6")),
            Map: new TraceProbeMap(Placements: SetPlacements("THING"))),
        new(
            "set-actor",
            "As thing-move with two still actors (no TARGET) instead of the things: does a script SET of an actor's X refresh every DISTANCE as an actor's walk in front does, and its own?",
            SetDeclarations("ACTOR"),
            SetRun("ACTOR"),
            Predict(
                ("a SET counts as a move", "dr_d1 follows dr_y1 in M3 (in front), not in M1 (behind)"),
                ("only an actor's own walk counts", "dr_d1 stays at 22 throughout"),
                ("a SET refreshes the actor's own DISTANCE", "dr_d2 goes 11 → 9 in M1 while dr_d1 stays"),
                ("IF_NEAR tests the stale DISTANCE", "no N line in M1; one in M3 only if M3 refreshes")),
            Map: new TraceProbeMap(Placements: SetPlacements("ACTOR"))),
        new(
            "region-seen",
            "A region's VISIBLE and SEEN (p.53: 'as long as a floor, ceiling or wall of the region is visible'): the pad (flat, invisible borders) out of the cone, in it, at its edge and behind a plain free-standing wall, and the room the player stands in.",
            RegionDeclarations,
            RegionRun,
            Predict(
                ("VISIBLE as walls and sprites (in the cone, not hidden by a wall)", "pad dr_v1: R1 0, R2 1, R3 0, R4 0, R5 1, R6S 0, R7S 1; dr_e1 0 in R1, 1 from R2 on"),
                ("the cone alone", "dr_v1 1 in R6S"),
                ("the room the player is in is always visible", "dr_v2 1 throughout"),
                ("region VISIBLE is not maintained", "dr_v1 and dr_e1 0 throughout")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;", Walls: [new TraceProbeWall("red_wall", -10, -6, -6, -6)])),
        .. Round2,
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "distance-refresh",
        TraceFile,
        "ovdrp",
        "After the skill dump, `IF_START` selects the trace file, writes `BEGIN` and runs the variant's case.\n" +
        "Each trace line is a token followed by `TICKS` and `pe_n` (the global EACH_TICK sampler's run count)\n" +
        "when it was printed, then the case's extra skills. `C` lines print in every cycle with each watched\n" +
        "thing's or actor's X, Y and DISTANCE (`dr_x*`, `dr_y*`, `dr_d*`), or each watched region's VISIBLE and\n" +
        "SEEN (`dr_v*`, `dr_e*`). `N`/`F` lines are IF_NEAR/IF_FAR runs with MY.X, MY.Y and MY.DISTANCE.\n" +
        "Compare the trace with `case.json` → `predictions`, and check `OVCON.TXT` for compile errors.",
        All);
}
