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
        "wall's EACH_TICK count, VISIBLE and DISTANCE. Compare the trace with `case.json` → `predictions`, and\n" +
        "check `OVCON.TXT` for compile errors.",
        All);
}
