// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Text;
using static ProximityProbeCases;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>view-activity</c> probe
/// (<c>docs/clean-room/observations/OV-20261020-view-activity.md</c>), the view questions that
/// OV-20261019-border-kinds and -clip-detail left open:
/// <list type="bullet">
/// <item><c>view-*</c>: what does "not visible" mean for <c>SKIP_FRAMES</c> (p.113)? The actor's
/// <c>#VISIBLE</c> flag (p.70) is printed in every cycle with its position: in and out of the
/// <c>PLAYER_ARC</c> cone, at the cone's edge, half behind a free-standing wall and behind a
/// pillar region.</item>
/// <item><c>skip-phase</c>: when does the (<c>SKIP_FRAMES</c> + 1)-cycle count start? One actor
/// is never seen, one is seen until the player turns away at different cycles, one has
/// <c>FAR</c> (p.71) and one is unseen within its <c>DIST</c>.</item>
/// <item><c>skip-wall</c>, <c>skip-pad</c>: is a skipped actor's long step collision-tested on
/// the way? Two unseen <c>CAREFULLY</c> actors (steps of 9.6 and 4.8) walk at a free-standing
/// <c>IMPASSABLE</c> wall or a pad 2 steps up.</item>
/// <item><c>berkeley-near</c>: does a <c>BERKELEY</c> wall (p.53) run its <c>EACH_TICK</c> while
/// the player is within its <c>DIST</c> but looks away, or while it is in the cone but behind
/// another wall?</item>
/// <item>Rounds 2–9 (appended below): the edges of the cone and of occlusion, the near range and
/// what refreshes the distance it tests (<c>edge-*</c>, <c>walk-*</c>, <c>dist-*</c>, round 8 and
/// round 9), <c>TRANSPARENT</c> walls and sprites as occluders, and plain walls' <c>VISIBLE</c> and
/// <c>SEEN</c>.</item>
/// </list>
/// While <c>va_p</c> is set the sampler prints a <c>C</c> line in every cycle.
/// </summary>
internal static class ViewActivityProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVVAX0.TXT).</summary>
    public const string TraceFile = "ovvax";

    private const string ActorTexture = "TEXTURE va_tex { SCALE_X 32; SCALE_Y 32; BMAPS red_map; }\n";

    /// <summary>
    /// Actor synonyms <c>va_s1</c>…<c>va_s4</c>, their Y and visibility skills, and the sampler
    /// that prints, in every cycle while <c>va_p</c> is set, <c>C</c> followed by the
    /// <paramref name="watched"/> actors' X, Y and <c>VISIBLE</c> (1 or 0), then the <paramref name="extra"/> skills.
    /// </summary>
    private static string ActorSampler(int watched, params string[] extra) => ControlledActorSampler(watched, "", extra);

    /// <summary>As <see cref="ActorSampler"/>, running <paramref name="control"/> first in every cycle.</summary>
    private static string ControlledActorSampler(int watched, string control, params string[] extra)
    {
        var skills = new StringBuilder("SKILL va_p { VAL 0; }\n");
        var rules = new StringBuilder();
        var printed = new List<string>();
        for (int i = 1; i <= watched; i++)
        {
            skills.Append(CultureInfo.InvariantCulture, $"SYNONYM va_s{i} {{ TYPE ACTOR; }}\nSKILL va_x{i} {{ VAL 0; }}\nSKILL va_y{i} {{ VAL 0; }}\nSKILL va_v{i} {{ VAL 0; }}\n");
            rules.Append(CultureInfo.InvariantCulture, $"    RULE va_x{i} = va_s{i}.X;\n    RULE va_y{i} = va_s{i}.Y;\n");
            rules.Append(CultureInfo.InvariantCulture, $"    IF (va_s{i}.VISIBLE == 1) {{\n        RULE va_v{i} = 1;\n    }} ELSE {{\n        RULE va_v{i} = 0;\n    }}\n");
            printed.AddRange([$"va_x{i}", $"va_y{i}", $"va_v{i}"]);
        }

        return skills + Sampler(
            control + "    IF (va_p > 0) {\n" + Indent(rules.ToString()) +
            Indent(L("C", [.. printed, .. extra])) + "    }\n");
    }

    private static string Actor(string name, double speed, string extra = "") =>
        FormattableString.Invariant($"ACTOR {name} {{ TEXTURE va_tex; TARGET va_w{name[^1]}; SPEED {speed}; DIST 1;{extra} }}\n");

    private static string Ways(int count) =>
        string.Concat(Enumerable.Range(1, count).Select(static i => $"WAY va_w{i};\n"));

    private static string Bind(int count) =>
        string.Concat(Enumerable.Range(1, count).Select(static i => $"    SET va_s{i}, va_a{i};\n"));

    private static string Face(double angle) =>
        FormattableString.Invariant($"    SET PLAYER_ANGLE, {angle:0.####};\n");

    private static string Cycles(int cycles, string token) => $"    WAIT {cycles};\n" + L(token, "PLAYER_X", "PLAYER_Y", "PLAYER_ANGLE");

    private static string Start(int actors) =>
        Bind(actors) + ArmSampler + "    RULE va_p = 1;\n" + L("GO", "PLAYER_X", "PLAYER_Y", "PLAYER_ANGLE");

    private const string Stop = "    RULE va_p = 0;\n" + ClearSampler;

    // ---- what "visible" means --------------------------------------------------------------------

    /// <summary>One slow way actor (0.05 steps per tick) on (40, −16) ↔ (40, 16).</summary>
    private static readonly string OneActorDeclarations =
        Skills + ActorTexture + Ways(1) + Actor("va_a1", 0.05) + ActorSampler(1);

    private static readonly string[] OneActorPlacements = ["ACTOR va_a1 40 -16 90 0", "WAY va_w1 40 -16 40 16"];

    /// <summary>
    /// From the origin: 24 cycles facing +X (the whole way inside the ±0.5 cone), 36 facing −X,
    /// then 170 at angle 0.6 (the cone's right edge at bearing 0.1, y ≈ 4 on the way).
    /// </summary>
    private static readonly string ViewConeRun =
        Start(1) + Face(0) + Cycles(24, "F0") + Face(Math.PI) + Cycles(36, "FPI") + Face(0.6) + Cycles(170, "F06") + Stop;

    /// <summary>
    /// A plain free-standing wall from (30, −20) to (30, 0) hides the way's south half from the
    /// player at the origin facing +X; the wall's end is on bearing 0. 200 cycles.
    /// </summary>
    private static readonly string ViewOccludeRun = Start(1) + Face(0) + Cycles(200, "OCC") + Stop;

    /// <summary>
    /// The pad as a pillar (floor = ceiling = 16, plain red walls). One slow actor on
    /// (−10, 22.5) ↔ (−6, 22.5), right behind it as seen from (−8, −20) facing +Y.
    /// </summary>
    private static readonly string PillarDeclarations = OneActorDeclarations;

    private static readonly string[] PillarPlacements = ["ACTOR va_a1 -10 22.5 0 0", "WAY va_w1 -10 22.5 -6 22.5"];

    /// <summary>40 cycles at (−8, −20) facing +Y (behind the pillar), then 30 at (−15, 21) facing +X (in plain view).</summary>
    private static readonly string PillarRun =
        Start(1) +
        Beam(-8, -20, "P1", locate: true) + Face(Math.PI / 2) + Cycles(40, "P1S") +
        Beam(-15, 21, "P2", locate: true) + Face(0) + Cycles(30, "P2S") + Stop;

    // ---- when the count starts --------------------------------------------------------------------

    /// <summary>
    /// Four way actors at 0.1 steps per tick, each on a way from y = −18 to 4: <c>va_a1</c> at
    /// x = −14 (never seen), <c>va_a2</c> at x = 40 (seen while the player faces +X),
    /// <c>va_a3</c> at x = −10 with <c>FAR</c>, <c>va_a4</c> at x = −6 with <c>DIST 30</c> (within
    /// it everywhere). Facing +Y none of them is in the cone.
    /// </summary>
    private static readonly string PhaseDeclarations =
        Skills + ActorTexture + Ways(4) +
        Actor("va_a1", 0.1) + Actor("va_a2", 0.1) + Actor("va_a3", 0.1, " FLAGS FAR;") +
        "ACTOR va_a4 { TEXTURE va_tex; TARGET va_w4; SPEED 0.1; DIST 30; }\n" +
        ActorSampler(4);

    private static readonly string[] PhasePlacements =
    [
        "ACTOR va_a1 -14 -18 90 0", "ACTOR va_a2 40 -18 90 0", "ACTOR va_a3 -10 -18 90 0", "ACTOR va_a4 -6 -18 90 0",
        "WAY va_w1 -14 -18 -14 4", "WAY va_w2 40 -18 40 4", "WAY va_w3 -10 -18 -10 4", "WAY va_w4 -6 -18 -6 4",
    ];

    /// <summary>Faces +X for 13, 7, 8 and 9 cycles, each followed by 30 cycles facing +Y.</summary>
    private static readonly string PhaseRun =
        Start(4) +
        Face(0) + Cycles(13, "X1") + Face(Math.PI / 2) + Cycles(30, "Y1") +
        Face(0) + Cycles(7, "X2") + Face(Math.PI / 2) + Cycles(30, "Y2") +
        Face(0) + Cycles(8, "X3") + Face(Math.PI / 2) + Cycles(30, "Y3") +
        Face(0) + Cycles(9, "X4") + Face(Math.PI / 2) + Cycles(30, "Y4") + Stop;

    // ---- the long step --------------------------------------------------------------------------

    /// <summary>
    /// Two unseen <c>CAREFULLY</c> way actors on y 2 ↔ 23: <c>va_a1</c> at x = −10.5 with 0.2
    /// steps per tick, <c>va_a2</c> at x = −5.5 with 0.1.
    /// </summary>
    private static string CarefulDeclarations(string wall) =>
        Skills + ActorTexture + Ways(2) + wall +
        Actor("va_a1", 0.2, " FLAGS CAREFULLY;") + Actor("va_a2", 0.1, " FLAGS CAREFULLY;") +
        ActorSampler(2);

    private static readonly string[] CarefulPlacements =
    [
        "ACTOR va_a1 -10.5 2 90 0", "ACTOR va_a2 -5.5 2 90 0",
        "WAY va_w1 -10.5 2 -10.5 23", "WAY va_w2 -5.5 2 -5.5 23",
    ];

    /// <summary>The player stands at (40, 0) facing +X, the actors far behind, for 160 cycles.</summary>
    private static readonly string CarefulRun =
        Start(2) + Beam(40, 0, "B", locate: true) + Face(0) + Cycles(160, "S") + Stop;

    // ---- BERKELEY walls ---------------------------------------------------------------------------

    /// <summary>
    /// A passable <c>BERKELEY</c> wall from (10, −4) to (10, 4) with <c>DIST 6</c> and an
    /// <c>EACH_TICK</c> that only counts in <c>wb_k</c>, read through a wall synonym, and a plain
    /// solid wall from (5, −6) to (5, 6) that hides it from the origin. The sampler prints <c>W</c>
    /// in every cycle with the count, the wall's <c>VISIBLE</c> and <c>MY.DISTANCE</c>.
    /// </summary>
    private static readonly string BerkeleyDeclarations =
        Skills +
        "SKILL va_p { VAL 0; }\nSKILL wb_k { VAL 0; }\nSKILL wb_v { VAL 0; }\nSKILL wb_d { VAL 0; }\n" +
        "SYNONYM va_sw { TYPE WALL; }\n" +
        Action("wb_tick", "    RULE wb_k = wb_k + 1;\n") +
        "WALL wb_b { TEXTURE grn_tex; DIST 6; EACH_TICK wb_tick; FLAGS PASSABLE, BERKELEY; }\n" +
        Sampler(
            "    IF (va_p > 0) {\n" +
            Indent(
                "    RULE wb_d = va_sw.DISTANCE;\n" +
                "    IF (va_sw.VISIBLE == 1) {\n        RULE wb_v = 1;\n    } ELSE {\n        RULE wb_v = 0;\n    }\n" +
                L("W", "wb_k", "wb_v", "wb_d")) +
            "    }\n");

    /// <summary>
    /// 8 cycles each: O1 at the origin facing +X (in the cone, behind the plain wall, 14 away);
    /// N1 at (8, 3) facing +X (within DIST, in view); N2 there facing −X (within DIST, out of view);
    /// F1 at (16, 0) facing −X (6 + 4 = 10 away, in view from behind); F2 there facing +X (out of
    /// view, outside DIST); N3 at (8, 3) facing +X again.
    /// </summary>
    private static readonly string BerkeleyRun =
        "    SET va_sw, wb_b;\n" + ArmSampler + "    RULE va_p = 1;\n" + L("GO") +
        Face(0) + Cycles(8, "O1") +
        Beam(8, 3, "N1") + Cycles(8, "N1S") + Face(Math.PI) + Cycles(8, "N2S") +
        Beam(16, 0, "F1") + Cycles(8, "F1S") + Face(0) + Cycles(8, "F2S") +
        Beam(8, 3, "N3") + Cycles(8, "N3S") +
        Stop;

    // ---- round 2: the edges ------------------------------------------------------------------------

    /// <summary>One 2-wide actor without a target at (40, 0); the C lines add the player's angle and Y.</summary>
    private static readonly string StillActorDeclarations =
        Skills + ActorTexture + "SKILL va_k { VAL 0; }\nACTOR va_a1 { TEXTURE va_tex; DIST 1; }\n" + ActorSampler(1, "PLAYER_ANGLE", "PLAYER_Y");

    private static readonly string[] StillActorPlacements = ["ACTOR va_a1 40 0 90 0"];

    /// <summary>
    /// A sweep: <paramref name="skill"/> from <paramref name="from"/> to <paramref name="to"/> in steps
    /// of <paramref name="step"/>, one cycle each, as a <c>WHILE</c> loop (round 2 unrolled it into 45
    /// <c>SET</c>/<c>WAIT</c> pairs; that carrier failed for another reason, a stray skill name in
    /// the sampler).
    /// </summary>
    private static string Sweep(string skill, double from, double to, double step) =>
        FormattableString.Invariant($"    RULE va_k = {from};\n    WHILE (va_k < {to + (step / 2):0.####}) {{\n        RULE {skill} = va_k;\n        WAIT 1;\n        RULE va_k = va_k + {step};\n    }}\n");

    /// <summary>From the origin, PLAYER_ANGLE from 0.40 to 0.62 in steps of 0.005, one cycle each (the actor's centre leaves the ±0.5 cone at 0.5).</summary>
    private static readonly string ConeEdgeRun =
        Start(1) + Face(0.4) + Cycles(4, "A0") + Sweep("PLAYER_ANGLE", 0.4, 0.62, 0.005) + Cycles(4, "A1") + Stop;

    /// <summary>
    /// Facing +X with a plain wall from (30, −20) to (30, 0): PLAYER_Y from −5 to 5 in steps of
    /// 0.25. The sight line to the actor's centre clears the wall's end for PLAYER_Y above 0, to its
    /// near (+Y) edge above −3, to its far edge above 3.
    /// </summary>
    private static readonly string OccludeEdgeRun =
        Start(1) + Face(0) + "    SET PLAYER_Y, -5;\n" + Cycles(4, "Y0") + Sweep("PLAYER_Y", -5, 5, 0.25) + Cycles(4, "Y1") + Stop;

    /// <summary>
    /// Sets each actor's <c>SPEED</c> to its step per cycle over <c>TIME_CORR</c> in every cycle, so a
    /// step does not depend on the emulator's frame rate (round 1 ran between 0.25 and 8 ticks a
    /// cycle). A skipped actor's long step is then about six of these.
    /// </summary>
    private static string SpeedControl(params double[] stepsPerCycle) =>
        "    IF (TIME_CORR > 0) {\n" +
        string.Concat(stepsPerCycle.Select((step, i) => FormattableString.Invariant($"        RULE va_s{i + 1}.SPEED = {step} / TIME_CORR;\n"))) +
        "    }\n";

    /// <summary>
    /// A way actor on (6, −3) ↔ (6, 3), 0.1 a cycle: in the cone from the origin but, with the eye
    /// at 8, entirely below the bottom of the screen.
    /// </summary>
    private static readonly string NearLowDeclarations =
        Skills + ActorTexture + Ways(1) + Actor("va_a1", 0) + ControlledActorSampler(1, SpeedControl(0.1), "PLAYER_ANGLE", "PLAYER_Z");

    private static readonly string[] NearLowPlacements = ["ACTOR va_a1 6 -3 90 0", "WAY va_w1 6 -3 6 3"];

    /// <summary>30 cycles facing +X (below the screen), 30 facing −X, then 30 facing +X with the eye lowered to 1.5 (drawn).</summary>
    private static readonly string NearLowRun =
        Start(1) + Face(0) + Cycles(30, "L1") + Face(Math.PI) + Cycles(30, "L2") + Face(0) +
        "    SET PLAYER_SIZE, 1;\n    SET PLAYER_Z, 1.5;\n" + Cycles(30, "L3") + Stop;

    /// <summary>
    /// Three unseen <c>CAREFULLY</c> way actors with fixed steps per cycle (about six of them per
    /// skipped move): <c>va_a1</c> at x = −10.5 from y 4 with 1.6 (9.6 a move), <c>va_a2</c> at −8
    /// from y 8 with 0.8 (4.8), <c>va_a3</c> at −5.5 from y 2 with 3.2 (19.2), all toward y 23.
    /// </summary>
    private static string LongStepDeclarations(string wall) =>
        Skills + ActorTexture + Ways(3) + wall +
        Actor("va_a1", 0, " FLAGS CAREFULLY;") + Actor("va_a2", 0, " FLAGS CAREFULLY;") + Actor("va_a3", 0, " FLAGS CAREFULLY;") +
        ControlledActorSampler(3, SpeedControl(1.6, 0.8, 3.2));

    private static readonly string[] LongStepPlacements =
    [
        "ACTOR va_a1 -10.5 4 90 0", "ACTOR va_a2 -8 8 90 0", "ACTOR va_a3 -5.5 2 90 0",
        "WAY va_w1 -10.5 4 -10.5 23", "WAY va_w2 -8 8 -8 23", "WAY va_w3 -5.5 2 -5.5 23",
    ];

    /// <summary>The player at (40, 0) facing +X, the actors far behind, for 60 cycles.</summary>
    private static readonly string LongStepRun =
        Start(3) + Beam(40, 0, "B", locate: true) + Face(0) + Cycles(60, "S") + Stop;

    /// <summary>
    /// Round 3: eight way actors behind the player at (20, 0) facing +X, at x = 14, 12, … 0 (6 to 20
    /// away), each on y −1 ↔ 1 at 0.1 a cycle, all with <paramref name="dist"/>.
    /// </summary>
    private static string RangeDeclarations(double dist) =>
        Skills + ActorTexture + Ways(8) +
        string.Concat(Enumerable.Range(1, 8).Select(i => FormattableString.Invariant($"ACTOR va_a{i} {{ TEXTURE va_tex; TARGET va_w{i}; SPEED 0; DIST {dist}; }}\n"))) +
        ControlledActorSampler(8, SpeedControl(0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1));

    private static readonly string[] RangePlacements =
    [
        .. Enumerable.Range(1, 8).Select(static i => FormattableString.Invariant($"ACTOR va_a{i} {16 - (2 * i)} -1 90 0")),
        .. Enumerable.Range(1, 8).Select(static i => FormattableString.Invariant($"WAY va_w{i} {16 - (2 * i)} -1 {16 - (2 * i)} 1")),
    ];

    private static readonly string RangeRun =
        Start(8) + Beam(20, 0, "B", locate: true) + Face(0) + Cycles(40, "S") + Stop;

    /// <summary>A way actor on (30, −3) ↔ (30, 3), 0.1 a cycle, with the C lines adding the tilt.</summary>
    private static readonly string TiltFarDeclarations =
        Skills + ActorTexture + Ways(1) + Actor("va_a1", 0) + ControlledActorSampler(1, SpeedControl(0.1), "PLAYER_ANGLE", "PLAYER_TILT");

    private static readonly string[] TiltFarPlacements = ["ACTOR va_a1 30 -3 90 0", "WAY va_w1 30 -3 30 3"];

    /// <summary>From the origin: 24 cycles facing it, 30 looking up (PLAYER_TILT −0.8: the actor below the screen), 30 facing away.</summary>
    private static readonly string TiltFarRun =
        Start(1) + Face(0) + Cycles(24, "T0") + "    SET PLAYER_TILT, -0.8;\n" + Cycles(30, "T1") +
        "    SET PLAYER_TILT, 0;\n" + Face(Math.PI) + Cycles(30, "T2") + Stop;

    /// <summary>
    /// Round 4: behind the player at (20, 0) facing +X, eight way actors (0.4 long along x, 0.1 a
    /// cycle): on the axis at x = 1.5, 1, 0.5, 0 (18.5 to 20 away); off it at (6, −9) and (10, −14)
    /// (|dx| + |dy| 23 and 24, Euclidean 16.6 and 17.2); at (−10, 2) with <c>DIST 40</c> and at
    /// (−10, −2) with <c>DIST 1</c> (32 / 30.1 away).
    /// </summary>
    private static readonly (double X, double Y, double Dist)[] ShapeActors =
        [(1.5, 0, 1), (1, 0, 1), (0.5, 0, 1), (0, 0, 1), (6, -9, 1), (10, -14, 1), (-10, 2, 40), (-10, -2, 1)];

    private static readonly string ShapeDeclarations =
        Skills + ActorTexture + Ways(8) +
        string.Concat(ShapeActors.Select((a, i) => FormattableString.Invariant($"ACTOR va_a{i + 1} {{ TEXTURE va_tex; TARGET va_w{i + 1}; SPEED 0; DIST {a.Dist}; }}\n"))) +
        ControlledActorSampler(8, SpeedControl(0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1));

    private static readonly string[] ShapePlacements =
    [
        .. ShapeActors.Select(static (a, i) => FormattableString.Invariant($"ACTOR va_a{i + 1} {a.X} {a.Y} 0 0")),
        .. ShapeActors.Select(static (a, i) => FormattableString.Invariant($"WAY va_w{i + 1} {a.X} {a.Y} {a.X - 0.4:0.##} {a.Y}")),
    ];

    /// <summary>
    /// Round 5: eight way actors on the axis behind the player at (20, 0), at x = 0.4, 0.2, … −1
    /// (19.6 to 21 away), each on a way 0.2 long across the axis (y −0.1 ↔ 0.1), 0.1 a cycle.
    /// </summary>
    private static readonly string[] FinePlacements =
    [
        .. Enumerable.Range(0, 8).Select(static i => FormattableString.Invariant($"ACTOR va_a{i + 1} {0.4 - (0.2 * i):0.#} -0.1 90 0")),
        .. Enumerable.Range(0, 8).Select(static i => FormattableString.Invariant($"WAY va_w{i + 1} {0.4 - (0.2 * i):0.#} -0.1 {0.4 - (0.2 * i):0.#} 0.1")),
    ];

    private static TraceProbeCase Fine(string label) => new(
        label,
        "Round 5. range-d1 skipped an actor 20.2 behind, range-shape moved one at 20.2 every cycle; the runs went at 0.26 and 8 ticks a cycle. Eight actors 19.6 to 21 behind, in two runs: where is the edge, and does it move with the frame rate?",
        RangeDeclarations(1),
        RangeRun,
        Predict(
            ("a fixed edge", "the same actors move every cycle in both runs"),
            ("an edge that depends on TIME_CORR", "the split differs between runs that went at different rates")),
        Map: new TraceProbeMap(Placements: FinePlacements));

    // ---- round 6: the near range's shape, occlusion, walls' VISIBLE and SEEN ----------------------

    /// <summary>
    /// Way actors <c>va_a1</c>… (facing 0) on the given ways, the WMP lines for
    /// <see cref="WayActorDeclarations"/>. Coordinates are in tenths of a step, so no value prints as −0.
    /// </summary>
    private static string[] WayPlacements(params (int X, int Y, int X2, int Y2)[] tenths) =>
    [
        .. tenths.Select(static (w, i) => FormattableString.Invariant($"ACTOR va_a{i + 1} {w.X / 10.0} {w.Y / 10.0} 0 0")),
        .. tenths.Select(static (w, i) => FormattableString.Invariant($"WAY va_w{i + 1} {w.X / 10.0} {w.Y / 10.0} {w.X2 / 10.0} {w.Y2 / 10.0}")),
    ];

    /// <summary>Eight starts 19.6, 19.8, … 21 from the player, in tenths.</summary>
    private static readonly int[] EdgeStarts = [.. Enumerable.Range(0, 8).Select(static i => 196 + (2 * i))];

    /// <summary>
    /// range-shape's on-axis actors, extended: behind the player at (20, 0), on the axis 19.6 to 21
    /// away, each on a way 0.4 long straight away from the player (x → x − 0.4). The third is
    /// range-shape's actor 20.0–20.4 away.
    /// </summary>
    private static readonly string[] EdgeAlongPlacements = WayPlacements([.. EdgeStarts.Select(static m => (200 - m, 0, 196 - m, 0))]);

    /// <summary>
    /// The same along the −Y axis from the player at the origin, which stays there facing +X (no beam):
    /// (0, −m) → (0, −m − 0.4).
    /// </summary>
    private static readonly string[] EdgeAlongYPlacements = WayPlacements([.. EdgeStarts.Select(static m => (0, -m, 0, -m - 4))]);

    /// <summary>
    /// The same starts on the −Y axis from the origin, each on a way 0.4 long across the line of
    /// sight that starts on the axis: (0, −m) → (0.4, −m), so |dx| + |dy| grows from m to m + 0.4.
    /// </summary>
    private static readonly string[] EdgeAcrossPlacements = WayPlacements([.. EdgeStarts.Select(static m => (0, -m, 4, -m))]);

    /// <summary>
    /// Four way actors walking straight away from or toward the player at the origin (facing +X, no
    /// beam) between 17 and 22 (|dx| + |dy|): on the −Y axis, the +Y axis, back right and back left.
    /// <paramref name="outward"/> starts them at 17.
    /// </summary>
    private static string[] WalkPlacements(bool outward)
    {
        (int X, int Y, int X2, int Y2)[] ways = [(0, -170, 0, -220), (0, 170, 0, 220), (-85, -85, -110, -110), (-85, 85, -110, 110)];
        return WayPlacements([.. ways.Select(w => outward ? w : (w.X2, w.Y2, w.X, w.Y))]);
    }

    private static string WayActorDeclarations(int count) =>
        Skills + ActorTexture + Ways(count) +
        string.Concat(Enumerable.Range(1, count).Select(static i => $"ACTOR va_a{i} {{ TEXTURE va_tex; TARGET va_w{i}; SPEED 0; DIST 1; }}\n")) +
        ControlledActorSampler(count, SpeedControl([.. Enumerable.Repeat(0.1, count)]));

    /// <summary>40 cycles at the origin facing +X.</summary>
    private static readonly string StayRun = Start(8) + Face(0) + Cycles(40, "S") + Stop;

    /// <summary>120 cycles at the origin facing +X: at 0.1 a cycle a way leg takes 50 cycles when moved every cycle.</summary>
    private static readonly string WalkRun = Start(4) + Face(0) + Cycles(120, "S") + Stop;

    /// <summary>
    /// Five still actors 40 ahead of the player at the origin (facing +X), at y −15, −5, 5, 15 and
    /// 0, the first four each behind a free-standing wall at x = 30: <c>TRANSPARENT</c> with a solid
    /// texture, <c>TRANSPARENT</c> with a texture all in colour 0, <c>INVISIBLE</c>, and plain. The
    /// fifth is in the open.
    /// </summary>
    private static readonly string OccludeKindsDeclarations =
        Skills + ActorTexture +
        "BMAP clr_map,<PRBCLR.PCX>;\nTEXTURE clr_tex { SCALE_X 4; SCALE_Y 4; BMAPS clr_map; }\n" +
        "WALL va_tsol { TEXTURE red_tex; FLAGS TRANSPARENT; }\nWALL va_tclr { TEXTURE clr_tex; FLAGS TRANSPARENT; }\n" +
        "WALL va_inv { TEXTURE red_tex; FLAGS INVISIBLE; }\n" +
        string.Concat(Enumerable.Range(1, 5).Select(static i => $"ACTOR va_a{i} {{ TEXTURE va_tex; DIST 1; }}\n")) +
        ActorSampler(5);

    private static readonly string[] OccludeKindsPlacements =
        ["ACTOR va_a1 40 -15 0 0", "ACTOR va_a2 40 -5 0 0", "ACTOR va_a3 40 5 0 0", "ACTOR va_a4 40 15 0 0", "ACTOR va_a5 40 0 0 0"];

    private static readonly TraceProbeWall[] OccludeKindsWalls =
    [
        new("va_tsol", 30, -14, 30, -8), new("va_tclr", 30, -6, 30, -2), new("va_inv", 30, 2, 30, 6), new("red_wall", 30, 8, 30, 14),
    ];

    /// <summary>12 cycles facing +X, then 12 facing −X (none visible).</summary>
    private static readonly string OccludeKindsRun = Start(5) + Face(0) + Cycles(12, "K1") + Face(Math.PI) + Cycles(12, "K2") + Stop;

    /// <summary>
    /// With the eye lowered to 1.5 (the 2-high sprites' middle), from the origin facing +X:
    /// <c>va_a1</c> 40 ahead right behind the actor <c>va_a2</c> 10 ahead (the near sprite covers
    /// ±0.1 of the view, the far one ±0.025), <c>va_a3</c> at (40, 12) right behind the thing
    /// <c>va_t</c> at (10, 3), and <c>va_a4</c> at (40, −12) in the open.
    /// </summary>
    private static readonly string SpriteOccludeDeclarations =
        Skills + ActorTexture +
        string.Concat(Enumerable.Range(1, 4).Select(static i => $"ACTOR va_a{i} {{ TEXTURE va_tex; DIST 1; }}\n")) +
        "THING va_t { TEXTURE va_tex; }\n" +
        ActorSampler(4, "PLAYER_Y", "PLAYER_Z");

    private static readonly string[] SpriteOccludePlacements =
        ["ACTOR va_a1 40 0 0 0", "ACTOR va_a2 10 0 0 0", "ACTOR va_a3 40 12 0 0", "ACTOR va_a4 40 -12 0 0", "THING va_t 10 3 0 0"];

    /// <summary>12 cycles covered; 12 at (0, −3), where the sight lines part; 12 back at the origin.</summary>
    private static readonly string SpriteOccludeRun =
        Start(4) + "    SET PLAYER_SIZE, 1;\n    SET PLAYER_Z, 1.5;\n" + Face(0) + Cycles(12, "H1") +
        Beam(0, -3, "H2") + Cycles(12, "H2S") + Beam(0, 0, "H3") + Cycles(12, "H3S") + Stop;

    /// <summary>
    /// Plain walls (no <c>BERKELEY</c>): <c>va_wa</c> from (30, −4) to (30, 4) ahead of the player,
    /// <c>va_wb</c> from (40, −2) to (40, 2) right behind it, <c>va_wc</c> from (−10, −4) to (−10, 2)
    /// behind the player, and a still actor at (−14, 6) seen past <c>va_wc</c>'s end when facing −X.
    /// The sampler prints <c>W</c> in every cycle with each wall's <c>VISIBLE</c> and <c>SEEN</c>,
    /// then the actor's.
    /// </summary>
    private static readonly string WallSeenDeclarations =
        Skills +
        ActorTexture +
        "SKILL va_p { VAL 0; }\nSYNONYM va_s1 { TYPE ACTOR; }\nSYNONYM va_sw1 { TYPE WALL; }\nSYNONYM va_sw2 { TYPE WALL; }\nSYNONYM va_sw3 { TYPE WALL; }\n" +
        string.Concat(new[] { "w1", "w2", "w3", "a1" }.Select(static o => $"SKILL va_v{o} {{ VAL 0; }}\nSKILL va_e{o} {{ VAL 0; }}\n")) +
        "WALL va_wa { TEXTURE red_tex; }\nWALL va_wb { TEXTURE grn_tex; }\nWALL va_wc { TEXTURE grn_tex; }\n" +
        "ACTOR va_a1 { TEXTURE va_tex; DIST 1; }\n" +
        Sampler(
            "    IF (va_p > 0) {\n" +
            Indent(string.Concat(new[] { ("w1", "va_sw1"), ("w2", "va_sw2"), ("w3", "va_sw3"), ("a1", "va_s1") }.Select(static o =>
                $"    IF ({o.Item2}.VISIBLE == 1) {{\n        RULE va_v{o.Item1} = 1;\n    }} ELSE {{\n        RULE va_v{o.Item1} = 0;\n    }}\n" +
                $"    IF ({o.Item2}.SEEN == 1) {{\n        RULE va_e{o.Item1} = 1;\n    }} ELSE {{\n        RULE va_e{o.Item1} = 0;\n    }}\n")) +
                L("W", "va_vw1", "va_ew1", "va_vw2", "va_ew2", "va_vw3", "va_ew3", "va_va1", "va_ea1", "PLAYER_X", "PLAYER_ANGLE")) +
            "    }\n");

    /// <summary>
    /// With the eye at 1.5, 8 cycles each: S1 the origin facing +X (<c>va_wa</c> in view,
    /// <c>va_wb</c> hidden behind it); S2 facing −X (<c>va_wc</c> and the actor in view); S3 at
    /// (35, 0) facing +X (<c>va_wb</c> in view); S4 the origin facing +X again.
    /// </summary>
    private static readonly string WallSeenRun =
        "    SET va_sw1, va_wa;\n    SET va_sw2, va_wb;\n    SET va_sw3, va_wc;\n    SET va_s1, va_a1;\n" +
        "    SET PLAYER_SIZE, 1;\n    SET PLAYER_Z, 1.5;\n" +
        ArmSampler + "    RULE va_p = 1;\n" + L("GO") +
        Face(0) + Cycles(8, "S1") + Face(Math.PI) + Cycles(8, "S2") +
        Beam(35, 0, "S3") + Face(0) + Cycles(8, "S3S") + Beam(0, 0, "S4") + Cycles(8, "S4S") + Stop;

    // ---- round 7: what freezes a radial walker -------------------------------------------------------

    /// <summary>
    /// As <see cref="WayActorDeclarations"/> for four actors, the C lines adding each actor's
    /// <c>DISTANCE</c> (<c>va_d1</c>…<c>va_d4</c>) and the player's X.
    /// </summary>
    private static readonly string DistanceWalkDeclarations =
        Skills + ActorTexture + Ways(4) +
        string.Concat(Enumerable.Range(1, 4).Select(static i => $"SKILL va_d{i} {{ VAL 0; }}\nACTOR va_a{i} {{ TEXTURE va_tex; TARGET va_w{i}; SPEED 0; DIST 1; }}\n")) +
        ControlledActorSampler(
            4,
            SpeedControl(0.1, 0.1, 0.1, 0.1) +
            string.Concat(Enumerable.Range(1, 4).Select(static i => $"    RULE va_d{i} = va_s{i}.DISTANCE;\n")),
            "va_d1", "va_d2", "va_d3", "va_d4", "PLAYER_X");

    /// <summary>
    /// Starting at 22 (|dx| + |dy|) from the origin and walking in to 17: <c>va_a1</c> straight in
    /// on the −Y axis (walk-in's), <c>va_a2</c> from (0, 22) to (1, 17), a little off the sight
    /// line, <c>va_a3</c> from (−11, −11) to (−6, −11), along x, and <c>va_a4</c> straight in from
    /// back left (walk-in's).
    /// </summary>
    private static readonly string[] DistanceInPlacements = WayPlacements((0, -220, 0, -170), (0, 220, 10, 170), (-110, -110, -60, -110), (-110, 110, -85, 85));

    /// <summary>
    /// Starting at 17 and walking out: <c>va_a1</c> straight out on the −Y axis to 22,
    /// <c>va_a2</c> straight out back left to 29, <c>va_a3</c> from (−6, −11) to (−11, −11) along x
    /// (to 22), <c>va_a4</c> from (1, 17) to (0, 22), a little off the sight line.
    /// </summary>
    private static readonly string[] DistanceOutPlacements = WayPlacements((0, -170, 0, -220), (-85, 85, -145, 145), (-60, -110, -110, -110), (10, 170, 0, 220));

    /// <summary>60 cycles at the origin facing +X, then 60 at (2, 0): does the player's move update the frozen walkers?</summary>
    private static readonly string DistanceWalkRun =
        Start(4) + Face(0) + Cycles(60, "S") + Beam(2, 0, "P") + Cycles(60, "PS") + Stop;

    /// <summary>120 cycles at the origin facing +X.</summary>
    private static readonly string LongStayRun = Start(4) + Face(0) + Cycles(120, "S") + Stop;

    /// <summary>60 cycles at the origin, then the player walks 2 along +X (20 ticks at 0.1) and stands 60 cycles.</summary>
    private static readonly string WalkAsideRun =
        Start(4) + Face(0) + Cycles(60, "S") + Walk(0.1, 0, 20, "P") + Cycles(60, "PS") + Stop;

    /// <summary>As <see cref="DistanceWalkRun"/> with <c>LOCATE</c> after the beam.</summary>
    private static readonly string LocateAsideRun =
        Start(4) + Face(0) + Cycles(60, "S") + Beam(2, 0, "P", locate: true) + Cycles(60, "PS") + Stop;

    /// <summary>30 cycles at the origin, 30 at (2, 0), 60 back at the origin.</summary>
    private static readonly string BeamBackRun =
        Start(4) + Face(0) + Cycles(30, "S") + Beam(2, 0, "P") + Cycles(30, "PS") + Beam(0, 0, "Q") + Cycles(60, "QS") + Stop;

    /// <summary>As <see cref="DistanceWalkDeclarations"/> for <paramref name="count"/> actors.</summary>
    private static string DistanceDeclarations(int count) =>
        Skills + ActorTexture + Ways(count) +
        string.Concat(Enumerable.Range(1, count).Select(static i => $"SKILL va_d{i} {{ VAL 0; }}\nACTOR va_a{i} {{ TEXTURE va_tex; TARGET va_w{i}; SPEED 0; DIST 1; }}\n")) +
        ControlledActorSampler(
            count,
            SpeedControl([.. Enumerable.Repeat(0.1, count)]) +
            string.Concat(Enumerable.Range(1, count).Select(static i => $"    RULE va_d{i} = va_s{i}.DISTANCE;\n")),
            [.. Enumerable.Range(1, count).Select(static i => $"va_d{i}")]);

    /// <summary>dist-in's ways: straight in on −Y, a little off the sight line from +Y, along x, straight in from back left.</summary>
    private static readonly (int X, int Y, int X2, int Y2) InRadial = (0, -220, 0, -170);
    private static readonly (int X, int Y, int X2, int Y2) InOffLine = (0, 220, 10, 170);
    private static readonly (int X, int Y, int X2, int Y2) InAlongX = (-110, -110, -60, -110);
    private static readonly (int X, int Y, int X2, int Y2) InDiagonal = (-110, 110, -85, 85);

    private static TraceProbeCase Isolate(string label, string question, params (int X, int Y, int X2, int Y2)[] ways) => new(
        label,
        "Round 9. In dist-in every actor's DISTANCE (and so the near test) changed only in the cycles in which va_a2 (a little off its sight line) moved. " + question,
        DistanceDeclarations(ways.Length),
        Start(ways.Length) + Face(0) + Cycles(70, "S") + Stop,
        Predict(
            ("the second walker's moves refresh everyone", "va_d1 changes only when the trigger walker moves"),
            ("its own moves refresh a walker", "va_d1 follows va_a1's moves")),
        Map: new TraceProbeMap(Placements: WayPlacements(ways)));

    /// <summary>The round-9 cases, appended to <see cref="All"/>.</summary>
    private static readonly TraceProbeCase[] Round9 =
    [
        Isolate("solo-radial", "The straight-in walker alone: does its DISTANCE ever change?", InRadial),
        Isolate("pair-offline", "The straight-in walker, then the off-line one.", InRadial, InOffLine),
        Isolate("pair-offline-first", "The off-line walker first, then the straight-in one: does the order matter?", InOffLine, InRadial),
        Isolate("pair-alongx", "The straight-in walker, then the one along x.", InRadial, InAlongX),
        Isolate("pair-diagonal", "The straight-in walker, then the straight-in one from back left.", InRadial, InDiagonal),
    ];

    /// <summary>The round-8 cases, appended to <see cref="All"/>.</summary>
    private static readonly TraceProbeCase[] Round8 =
    [
        new(
            "ab-noread",
            "Round 8. dist-in without reading DISTANCE: does walk-in's freeze come back (va_a1 and va_a4 skipping all the way in)?",
            WayActorDeclarations(4),
            DistanceWalkRun,
            Predict(
                ("reading DISTANCE refreshes it", "va_a1 and va_a4 skip throughout before P, unlike dist-in"),
                ("reading does not matter", "the moves as in dist-in")),
            Map: new TraceProbeMap(Placements: DistanceInPlacements)),
        new(
            "long-read",
            "Round 8. dist-in without the beam, 120 cycles: does DISTANCE keep following?",
            DistanceWalkDeclarations,
            LongStayRun,
            Predict(
                ("the beam froze it", "va_d* follow throughout"),
                ("it freezes after a while", "va_d* stop changing near cycle 60")),
            Map: new TraceProbeMap(Placements: DistanceInPlacements)),
        new(
            "walk-aside",
            "Round 8. dist-in with the player walking 2 along +X instead of the beam.",
            DistanceWalkDeclarations,
            WalkAsideRun,
            Predict(
                ("any player move freezes it", "va_d* stop changing after P"),
                ("only a beam does", "va_d* follow throughout")),
            Map: new TraceProbeMap(Placements: DistanceInPlacements)),
        new(
            "locate-aside",
            "Round 8. dist-in with LOCATE after the beam.",
            DistanceWalkDeclarations,
            LocateAsideRun,
            Predict(
                ("LOCATE does not matter", "va_d* stop changing after P as in dist-in"),
                ("the beam without LOCATE froze it", "va_d* follow throughout")),
            Map: new TraceProbeMap(Placements: DistanceInPlacements)),
        new(
            "beam-back",
            "Round 8. Beamed to (2, 0) at 30 and back to the origin at 60: does DISTANCE follow again at the origin?",
            DistanceWalkDeclarations,
            BeamBackRun,
            Predict(
                ("following depends on where the player stands", "va_d* frozen in PS, following again in QS"),
                ("a beam freezes it for good", "va_d* frozen from P on")),
            Map: new TraceProbeMap(Placements: DistanceInPlacements)),
    ];

    /// <summary>The round-7 cases, appended to <see cref="All"/>.</summary>
    private static readonly TraceProbeCase[] Round7 =
    [
        new(
            "dist-in",
            "Round 7. walk-in's actors walked straight in from 22 to 17 and skipped throughout; edge-across's followed their position. What does DISTANCE say for walkers straight in, a little off the sight line and along x, and does the player's own move (a beam 2 along x) update them?",
            DistanceWalkDeclarations,
            DistanceWalkRun,
            Predict(
                ("DISTANCE is not updated by a move along the sight line", "va_d1 and va_d4 stay at 22 while the actors walk in; va_d3 follows"),
                ("DISTANCE follows, the skip state is cached", "va_d1 goes down to 17, the actor still skips"),
                ("the player's move refreshes it", "after P, va_a1 and va_a4 move every cycle while under 20 from (2, 0)")),
            Map: new TraceProbeMap(Placements: DistanceInPlacements)),
        new(
            "dist-out",
            "Round 7. The mirror: walkers starting at 17 and walking out, straight (to 22 and to 29), along x, and a little off the sight line.",
            DistanceWalkDeclarations,
            DistanceWalkRun,
            Predict(
                ("DISTANCE is not updated by a move along the sight line", "va_d1 and va_d2 stay at 17, va_a2 moves every cycle out to 29"),
                ("a radial walker keeps its state to some range", "va_a2 starts skipping somewhere beyond 22"),
                ("off the sight line follows the position", "va_a3 and va_a4 skip beyond 20")),
            Map: new TraceProbeMap(Placements: DistanceOutPlacements)),
    ];

    /// <summary>The round-6 cases, appended to <see cref="All"/>.</summary>
    private static readonly TraceProbeCase[] Round6 =
    [
        new(
            "edge-along",
            "Round 6. range-shape's actor 20.0–20.4 behind on a way along the line of sight moved every cycle; range-fine's at 20.0–20.1 on a way across it skipped. Eight actors on the axis 19.6 to 21 behind the player at (20, 0), each on a way 0.4 long straight away from it: where is the edge?",
            WayActorDeclarations(8),
            RangeRun,
            Predict(
                ("ways along the line of sight reach farther", "actors starting at 20.0 and beyond move every cycle too (the edge beyond range-fine's)"),
                ("the start position decides, 20 inclusive", "19.6 to 20.0 move every cycle throughout, 20.2 and beyond every 6th"),
                ("the position in each cycle, under 20", "19.6 and 19.8 skip whenever they are beyond 20; range-shape's 20.0 actor is not explained")),
            Map: new TraceProbeMap(Placements: EdgeAlongPlacements)),
        new(
            "edge-along-y",
            "Round 6. As edge-along on the −Y axis from the player at the origin, which never moves: does the edge follow the beam to (20, 0) that range-shape and range-fine began with?",
            WayActorDeclarations(8),
            StayRun,
            Predict(
                ("the same split as edge-along", "the beam does not matter"),
                ("a different split", "something before or at the beam (the actors near the origin) matters")),
            Map: new TraceProbeMap(Placements: EdgeAlongYPlacements)),
        new(
            "edge-across",
            "Round 6. The same starts on the −Y axis from the origin, each on a way 0.4 long across the line of sight that starts on the axis (range-fine's started 0.1 off it).",
            WayActorDeclarations(8),
            StayRun,
            Predict(
                ("the direction matters", "a different split from edge-along-y"),
                ("the start position decides", "the same split as edge-along-y")),
            Map: new TraceProbeMap(Placements: EdgeAcrossPlacements)),
        new(
            "walk-out",
            "Round 6. Four actors walk straight away from the player at the origin from 17 to 22 and back (|dx| + |dy|): do they skip once beyond 20, or does having started near keep them moving?",
            WayActorDeclarations(4),
            WalkRun,
            Predict(
                ("the position in each cycle", "every cycle under 20, every 6th beyond, on both legs"),
                ("once near, always", "every cycle throughout"),
                ("radial motion extends the range", "every cycle somewhat beyond 20 on both legs")),
            Map: new TraceProbeMap(Placements: WalkPlacements(outward: true))),
        new(
            "walk-in",
            "Round 6. The same four actors starting at 22 and walking in to 17 and back out.",
            WayActorDeclarations(4),
            WalkRun,
            Predict(
                ("the position in each cycle", "every 6th beyond 20, every cycle under, on both legs"),
                ("once near, always", "every 6th cycle until under 20, then every cycle throughout"),
                ("the start position decides", "every 6th cycle throughout")),
            Map: new TraceProbeMap(Placements: WalkPlacements(outward: false))),
        new(
            "occlude-kinds",
            "Round 6. Do TRANSPARENT walls (p.53: colour 0 see-through) hide a sprite behind them, with a solid texture and with one all in colour 0? And INVISIBLE walls? Still actors 40 ahead behind each, a plain wall and none.",
            OccludeKindsDeclarations,
            OccludeKindsRun,
            Predict(
                ("TRANSPARENT and INVISIBLE walls are see-through", "K1: va_v1, va_v2, va_v3 and va_v5 1, va_v4 0"),
                ("a TRANSPARENT wall hides by its pixels", "K1: va_v1 0, va_v2 1"),
                ("only INVISIBLE walls are see-through", "K1: va_v1 and va_v2 0, va_v3 1")),
            Map: new TraceProbeMap(Placements: OccludeKindsPlacements, Walls: OccludeKindsWalls)),
        new(
            "sprite-occlude",
            "Round 6. Do sprites hide each other? With the eye at the sprites' middle, an actor 40 ahead right behind an actor 10 ahead, another behind a thing, one in the open; then the player steps sideways so the sight lines part.",
            SpriteOccludeDeclarations,
            SpriteOccludeRun,
            Predict(
                ("sprites do not hide sprites", "H1: va_v1, va_v3, va_v4 1"),
                ("a nearer sprite hides", "H1: va_v1 and va_v3 0, va_v4 1; H2S: all 1; H3S as H1")),
            Map: new TraceProbeMap(Placements: SpriteOccludePlacements)),
        new(
            "wall-seen",
            "Round 6. Do plain walls (no BERKELEY) get VISIBLE as BERKELEY ones do (in view, not behind another wall), and SEEN (p.53) once seen? An actor's SEEN (p.71) alongside.",
            WallSeenDeclarations,
            WallSeenRun,
            Predict(
                ("plain walls get VISIBLE and SEEN", "S1: va_vw1 1, va_vw2 0, va_vw3 0; S2: va_vw3 1; S3S: va_vw2 1; SEEN stays 1 once 1"),
                ("only BERKELEY walls get VISIBLE", "va_vw* 0 throughout"),
                ("SEEN is not readable", "va_e* 0 throughout")),
            Map: new TraceProbeMap(
                Placements: ["ACTOR va_a1 -14 6 0 0"],
                Walls: [new TraceProbeWall("va_wa", 30, -4, 30, 4), new TraceProbeWall("va_wb", 40, -2, 40, 2), new TraceProbeWall("va_wc", -10, -4, -10, 2)])),
    ];

    /// <summary>The round-5 cases, appended to <see cref="All"/>.</summary>
    private static readonly TraceProbeCase[] Round5 = [Fine("range-fine-a"), Fine("range-fine-b")];

    /// <summary>The round-4 cases, appended to <see cref="All"/>.</summary>
    private static readonly TraceProbeCase[] Round4 =
    [
        new(
            "range-shape",
            "Round 4. range-d1/d2 put the near range between 18 and 20. Is it |dx| + |dy| or Euclidean, where between 18.5 and 20, and does DIST 40 lift skipping at 30 away (skip-phase's DIST 30 actor was also near)?",
            ShapeDeclarations,
            RangeRun,
            Predict(
                ("|dx| + |dy|", "va_a5 and va_a6 skip"),
                ("Euclidean", "va_a5 and va_a6 move every cycle"),
                ("DIST lifts it (p.113)", "va_a7 moves every cycle, va_a8 every 6th")),
            Map: new TraceProbeMap(Placements: ShapePlacements)),
    ];

    /// <summary>The round-3 cases, appended to <see cref="All"/>.</summary>
    private static readonly TraceProbeCase[] Round3 =
    [
        new(
            "tilt-far",
            "Round 3. near-low and view-pillar's below-the-screen actors were also near the player. An actor 30 ahead, in the cone but below the screen while the player looks up: does it skip?",
            TiltFarDeclarations,
            TiltFarRun,
            Predict(
                ("SKIP_FRAMES uses the horizontal view", "T1: va_v1 0, moves every cycle; T2: every 6th"),
                ("SKIP_FRAMES follows VISIBLE", "T1: va_v1 0, moves every 6th cycle")),
            Map: new TraceProbeMap(Placements: TiltFarPlacements)),
        new(
            "range-d1",
            "Round 3. near-low's actor 6 behind the player moved every cycle; skip-phase's 14 behind skipped. Eight actors (DIST 1) behind the player at 6, 8, … 20: which skip?",
            RangeDeclarations(1),
            RangeRun,
            Predict(
                ("a fixed near range", "the nearer actors move every cycle, the farther every 6th, the split the same in range-d2"),
                ("a range scaled by DIST", "the split moves outward in range-d2")),
            Map: new TraceProbeMap(Placements: RangePlacements)),
        new(
            "range-d2",
            "Round 3. As range-d1 with DIST 2.",
            RangeDeclarations(2),
            RangeRun,
            Predict(
                ("a fixed near range", "the split as in range-d1"),
                ("a range scaled by DIST", "the split farther out than in range-d1")),
            Map: new TraceProbeMap(Placements: RangePlacements)),
    ];

    /// <summary>The round-2 cases, appended to <see cref="All"/>.</summary>
    private static readonly TraceProbeCase[] Round2 =
    [
        new(
            "cone-edge",
            "Round 2. A still actor 40 ahead, the player turning from 0.40 to 0.62 rad: at which angle does VISIBLE drop? (view-cone's actor never crossed the edge.)",
            StillActorDeclarations,
            ConeEdgeRun,
            Predict(
                ("the actor's centre", "va_v1 drops between 0.495 and 0.505"),
                ("any part of the sprite", "va_v1 drops near 0.525"),
                ("the whole sprite", "va_v1 drops near 0.475")),
            Map: new TraceProbeMap(Placements: StillActorPlacements)),
        new(
            "occlude-edge",
            "Round 2. The still actor behind the end of a plain wall, the player stepping sideways: at which PLAYER_Y does VISIBLE come on?",
            StillActorDeclarations,
            OccludeEdgeRun,
            Predict(
                ("any part of the sprite drawn", "va_v1 1 from PLAYER_Y ≈ −3"),
                ("the centre column", "from PLAYER_Y ≈ 0"),
                ("the whole sprite", "from PLAYER_Y ≈ 3")),
            Map: new TraceProbeMap(Placements: StillActorPlacements, Walls: [new TraceProbeWall("red_wall", 30, -20, 30, 0)])),
        new(
            "near-low",
            "Round 2. In view-pillar the actor moved every cycle in front of the player with VISIBLE 0 (below the screen). An actor 6 ahead, entirely below the screen: does it move every cycle facing it, every 6th facing away, and is it VISIBLE once the eye is lowered?",
            NearLowDeclarations,
            NearLowRun,
            Predict(
                ("SKIP_FRAMES uses the horizontal view, VISIBLE the drawn sprite", "L1: va_v1 0, moves every cycle; L2: every 6th; L3: va_v1 1"),
                ("SKIP_FRAMES follows VISIBLE", "L1: moves every 6th cycle")),
            Map: new TraceProbeMap(Placements: NearLowPlacements)),
        new(
            "long-wall",
            "Round 2. skip-wall ran too slowly for long steps. Unseen CAREFULLY actors with moves of 9.6, 4.8 and 19.2 at a free-standing IMPASSABLE wall at y = 12: does a move that starts before the wall and ends beyond it pass?",
            LongStepDeclarations("WALL va_imp { TEXTURE red_tex; FLAGS IMPASSABLE; }\n"),
            LongStepRun,
            Predict(
                ("moves are swept", "all three stop at about 10.95"),
                ("moves are not swept", "va_y1 and va_y2 end beyond 12; va_y3 reaches 23")),
            Map: new TraceProbeMap(Placements: LongStepPlacements, Walls: [new TraceProbeWall("va_imp", -14, 12, -2, 12)])),
        new(
            "long-pad",
            "Round 2. The same actors at the pad 2 steps up (plain walls; it stops a CAREFULLY actor): a move can end inside the pad (9.6 from 4, 4.8 from 8) or beyond it (19.2 from 2).",
            LongStepDeclarations("WALL va_padw { TEXTURE red_tex; }\n"),
            LongStepRun,
            Predict(
                ("the end point is tested", "va_y1 and va_y2 never between 12 and 20; va_y3 reaches 23"),
                ("nothing is tested", "va_y1 and va_y2 land inside the pad"),
                ("moves are swept", "all three stop at about 10.95")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;", PadWall: "va_padw", PadFloor: 2, Placements: LongStepPlacements)),
    ];

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "view-cone",
            "What sets an actor's VISIBLE, and does SKIP_FRAMES follow it? A slow actor (0.4 steps a cycle when moving every cycle) in the cone, behind the player, and crossing the cone's edge (angle 0.6, edge at y ≈ 4).",
            OneActorDeclarations,
            ViewConeRun,
            Predict(
                ("SKIP_FRAMES follows VISIBLE", "F0: va_v1 1, va_y1 changes every cycle; FPI: va_v1 0, va_y1 changes every 6th cycle by about 2.4"),
                ("the cone counts the sprite's width", "at F06 va_v1 goes 0 → 1 with va_y1 ≈ 3, not 4"),
                ("the cone uses the actor's centre", "va_v1 switches at va_y1 ≈ 4")),
            Map: new TraceProbeMap(Placements: OneActorPlacements)),
        new TraceProbeCase(
            "view-occlude",
            "A plain free-standing wall hides the south half of the actor's way from the player (the wall's end on bearing 0, y = 0 at the way). Is a hidden actor VISIBLE, and does it skip frames?",
            OneActorDeclarations,
            ViewOccludeRun,
            Predict(
                ("VISIBLE means drawn", "va_v1 0 for va_y1 below about −1, moves every 6th cycle there"),
                ("VISIBLE means in the cone", "va_v1 1 throughout, moves every cycle")),
            Map: new TraceProbeMap(Placements: OneActorPlacements, Walls: [new TraceProbeWall("red_wall", 30, -20, 30, 0)])),
        new TraceProbeCase(
            "view-pillar",
            "The pad as a solid pillar (floor = ceiling, plain walls) between the player and an actor in the room behind it: is the actor VISIBLE? A stand in plain view is the control.",
            PillarDeclarations,
            PillarRun,
            Predict(
                ("hidden behind a region step means not visible", "P1S: va_v1 0, moves every 6th cycle; P2S: va_v1 1, moves every cycle"),
                ("only the cone counts", "va_v1 1 in both stands")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;", PadWall: "red_wall", PadFloor: 16, Placements: PillarPlacements)),
        new TraceProbeCase(
            "skip-phase",
            "When does the 6-cycle count start? va_a1 is never seen; va_a2 is seen until the player turns away after 13, 7, 8 and 9 cycles; va_a3 has FAR; va_a4 is unseen but within its DIST 30.",
            PhaseDeclarations,
            PhaseRun,
            Predict(
                ("one global count", "after each turn va_y2 jumps in the same cycles as va_y1"),
                ("a count per actor from when it was last seen", "va_y2's first jump comes 6 cycles after the turn, out of step with va_y1"),
                ("FAR and DIST lift it (p.71, p.113)", "va_y3 and va_y4 change every cycle")),
            Map: new TraceProbeMap(Placements: PhasePlacements)),
        new TraceProbeCase(
            "skip-wall",
            "Two unseen CAREFULLY actors with steps of 9.6 and 4.8 every 6th cycle walk at a free-standing IMPASSABLE wall at y = 12: does the long step jump it?",
            CarefulDeclarations("WALL va_imp { TEXTURE red_tex; FLAGS IMPASSABLE; }\n"),
            CarefulRun,
            Predict(
                ("the step is not swept", "va_y1 and va_y2 cross y = 12 (va_y1 as over the pad in OV-20261019-border-kinds)"),
                ("only the end point (two steps ahead) is tested", "a step whose end lies beyond the wall passes, one ending at the wall stops"),
                ("the step is swept", "both stay below about 10.95")),
            Map: new TraceProbeMap(Placements: CarefulPlacements, Walls: [new TraceProbeWall("va_imp", -14, 12, -2, 12)])),
        new TraceProbeCase(
            "skip-pad",
            "The same two actors at the pad 2 steps up (plain walls, blocks a CAREFULLY actor): with a 4.8 step the end point can land inside the pad. Is it stopped?",
            CarefulDeclarations("WALL va_padw { TEXTURE red_tex; }\n"),
            CarefulRun,
            Predict(
                ("the end point is tested", "va_y2 never lies between 12 and 20"),
                ("nothing is tested", "va_y2 lands inside the pad")),
            Map: new TraceProbeMap(PadBody: "SKILL1 2;", PadWall: "va_padw", PadFloor: 2, Placements: CarefulPlacements)),
        new TraceProbeCase(
            "berkeley-near",
            "A BERKELEY wall (DIST 6): does it run its EACH_TICK while the player is within DIST but looks away, while it is in the cone behind another wall, and seen from behind outside DIST?",
            BerkeleyDeclarations,
            BerkeleyRun,
            Predict(
                ("in view or within DIST (p.53)", "wb_k rises in N1S, N2S, F1S, N3S; not in O1 or F2S"),
                ("in view only (OV-20261019-clip-detail)", "wb_k rises in N1S, F1S, N3S; not in N2S"),
                ("the cone alone", "wb_k rises in O1 too")),
            Map: new TraceProbeMap(Walls: [new TraceProbeWall("wb_b", 10, -4, 10, 4), new TraceProbeWall("red_wall", 5, -6, 5, 6)])),
        .. Round2,
        .. Round3,
        .. Round4,
        .. Round5,
        .. Round6,
        .. Round7,
        .. Round8,
        .. Round9,
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "view-activity",
        TraceFile,
        "ovvap",
        "After the skill dump, `IF_START` selects the trace file, writes `BEGIN` and runs the variant's case.\n" +
        "Each trace line is a token followed by `TICKS` and `pe_n` (the global EACH_TICK sampler's run count)\n" +
        "when it was printed, then the case's extra skills. `C` lines print in every cycle with each watched\n" +
        "actor's X, Y and VISIBLE (`va_x*`, `va_y*`, `va_v*`); `W` lines (berkeley-near) with the BERKELEY\n" +
        "wall's EACH_TICK count, VISIBLE and DISTANCE; in wall-seen with each wall's and the actor's VISIBLE and\n" +
        "SEEN. The C lines of rounds 7–9 (but ab-noread) add each actor's DISTANCE. Compare the trace with\n" +
        "`case.json` → `predictions`, and check `OVCON.TXT` for compile errors.",
        All);
}
