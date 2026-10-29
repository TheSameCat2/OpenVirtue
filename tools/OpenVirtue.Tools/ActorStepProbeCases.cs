// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>actor-step</c> probe: how the original turns an actor's <c>ANGLE</c> and
/// <c>SPEED</c> into a step in X and Y. The actor-events replays showed a <c>BULLET</c> facing angle 0
/// gaining 4 raw units (4/1024) of Y in every step, whatever the step length. Each case points a
/// <c>BULLET</c> actor at (10, 0) at one angle with one speed. Its <c>EACH_TICK</c> prints, every
/// cycle, <c>TICKS</c>, a cycle count and its X, Y, <c>ANGLE</c> and <c>TIME_CORR</c> times 1024: the
/// raw 22.10 values, so each unit shows.
/// </summary>
internal static class ActorStepProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVAST0.TXT).</summary>
    public const string TraceFile = "ovast";

    private const string Skills =
        "SKILL as_n { VAL 0; }\nSKILL as_on { VAL 0; }\nSKILL as_x { VAL 0; }\nSKILL as_y { VAL 0; }\n" +
        "SKILL as_ang { VAL 0; }\nSKILL as_t { VAL 0; }\nSYNONYM as_s { TYPE ACTOR; }\n" +
        "TEXTURE as_tex { SCALE_X 32; SCALE_Y 32; BMAPS yel_map; }\n";

    private static readonly string Hooks =
        Action("as_g1", "    RULE as_n = as_n + 1;\n") +
        Action("as_tick",
            "    IF (as_on > 0) {\n        RULE as_x = MY.X * 1024;\n        RULE as_y = MY.Y * 1024;\n" +
            "        RULE as_ang = MY.ANGLE * 1024;\n        RULE as_t = TIME_CORR * 1024;\n" +
            Indent(V("A", "TICKS", "as_n", "as_x", "as_y", "as_ang", "as_t")) + "    }\n");

    private static TraceProbeCase Step(string label, double angle, double speed, string flags = "") => new(
        label,
        string.Create(CultureInfo.InvariantCulture, $"A BULLET at angle {angle}, SPEED {speed}{(flags.Length > 0 ? ", FLAGS " + flags : "")}: how do X and Y step, unit by unit?"),
        Skills + Hooks + "ACTOR as_a { TEXTURE as_tex; SPEED " + speed.ToString(CultureInfo.InvariantCulture) + "; EACH_TICK as_tick;" +
            (flags.Length > 0 ? " FLAGS " + flags + ";" : "") + " }\n",
        "    SET as_s, as_a;\n    SET EACH_TICK.1, as_g1;\n" +
        string.Create(CultureInfo.InvariantCulture, $"    SET as_s.ANGLE, {angle};\n") +
        "    SET as_s.TARGET, BULLET;\n    RULE as_on = 1;\n" + V("GO", "TICKS", "as_n") +
        "    WAITT 24;\n    RULE as_on = 0;\n    SET EACH_TICK.1, NULL;\n" + P("RET"),
        Predict(
            ("x += step·cos, y += step·sin in 22.10", "Y stays 0 at angle 0"),
            ("a lateral unit per step", "Y grows by 4 per step at angle 0")),
        Map: new TraceProbeMap(Placements: ["ACTOR as_a 10 0 0 0"]));

    /// <summary>
    /// Round 4: an actor walking a way from (10, 0) to <paramref name="to"/>, plain or
    /// <c>CAREFULLY</c>. The border-kinds actors showed a <c>CAREFULLY</c> one drifting 4–8 units off
    /// its way and losing 1–2 units a step against a plain one; which rule splits a step toward a point?
    /// </summary>
    private static TraceProbeCase WayStep(string label, string to, double speed, string flags = "") => new(
        label,
        string.Create(CultureInfo.InvariantCulture, $"Round 4. An actor on a way (10, 0) → ({to.Replace(' ', ',')}), SPEED {speed}{(flags.Length > 0 ? ", FLAGS " + flags : "")}: how do X and Y step toward the point?"),
        Skills + Hooks + "WAY as_w;\nACTOR as_a { TEXTURE as_tex; TARGET as_w; SPEED " + speed.ToString(CultureInfo.InvariantCulture) + "; EACH_TICK as_tick;" +
            (flags.Length > 0 ? " FLAGS " + flags + ";" : "") + " }\n",
        "    SET as_s, as_a;\n    SET EACH_TICK.1, as_g1;\n    RULE as_on = 1;\n" + V("GO", "TICKS", "as_n") +
        "    WAITT 24;\n    RULE as_on = 0;\n    SET EACH_TICK.1, NULL;\n" + P("RET"),
        Predict(
            ("both split the step by |dx| + |dy|", "the CAREFULLY actor's X stays within 4 units of 10"),
            ("CAREFULLY drifts", "its X wanders 4–8 units and its Y falls behind")),
        Map: new TraceProbeMap(Placements: ["ACTOR as_a 10 0 0 0", "WAY as_w 10 0 " + to]));

    /// <summary>
    /// Round 5: as round 4, but the actor stands (<c>SPEED</c> 0) until the probe starts, sets
    /// <c>SKIP_FRAMES</c> 0 and gives it <paramref name="speed"/>; the way runs from
    /// <paramref name="from"/> to <paramref name="to"/> inside the room.
    /// </summary>
    private static TraceProbeCase WayStep5(string label, string from, string to, double speed, string flags = "") => new(
        label,
        string.Create(CultureInfo.InvariantCulture, $"Round 5. An actor on a way ({from.Replace(' ', ',')}) → ({to.Replace(' ', ',')}), SPEED {speed} from the probe start{(flags.Length > 0 ? ", FLAGS " + flags : "")}: how do X and Y step toward the point?"),
        Skills + Hooks + "WAY as_w;\nACTOR as_a { TEXTURE as_tex; TARGET as_w; SPEED 0; EACH_TICK as_tick;" +
            (flags.Length > 0 ? " FLAGS " + flags + ";" : "") + " }\n",
        "    SET SKIP_FRAMES, 0;\n    SET as_s, as_a;\n    SET EACH_TICK.1, as_g1;\n    RULE as_on = 1;\n" +
        string.Create(CultureInfo.InvariantCulture, $"    SET as_s.SPEED, {speed};\n") + V("GO", "TICKS", "as_n") +
        "    WAITT 24;\n    RULE as_on = 0;\n    SET EACH_TICK.1, NULL;\n" + P("RET"),
        Predict(
            ("both split the step by |dx| + |dy|", "the CAREFULLY actor's X stays within 4 units of the way"),
            ("CAREFULLY drifts", "its X wanders 4–8 units and its Y falls behind")),
        Map: new TraceProbeMap(Placements: ["ACTOR as_a " + from + " 0 0", "WAY as_w " + from + " " + to]));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        WayStep5("v-p-y", "10 -20", "10 20", 0.5),
        WayStep5("v-c-y", "10 -20", "10 20", 0.5, "CAREFULLY"),
        WayStep5("v-c-y2", "10 -20", "10 20", 2, "CAREFULLY"),
        WayStep5("v-p-d", "-20 -20", "20 -5", 0.5),
        WayStep5("v-c-d", "-20 -20", "20 -5", 0.5, "CAREFULLY"),
        WayStep("w-p-y", "10 30", 2),
        WayStep("w-c-y", "10 30", 2, "CAREFULLY"),
        WayStep("w-c-y-fast", "10 60", 6.4, "CAREFULLY"),
        WayStep("w-c-y-slow", "10 30", 0.5, "CAREFULLY"),
        WayStep("w-p-diag", "30 7", 2),
        WayStep("w-c-diag", "30 7", 2, "CAREFULLY"),
        Step("a0", 0, 0.5),
        Step("a0-s1", 0, 1),
        Step("a0-s025", 0, 0.25),
        Step("a-quarter", 0.7854, 0.5),
        Step("a-1", 1, 0.5),
        Step("a-half-pi", 1.5708, 0.5),
        Step("a-2", 2, 0.5),
        Step("a-pi", 3.1416, 0.5),
        Step("a-neg-half-pi", 4.7124, 0.5),
        Step("a-0.1", 0.1, 0.5),
        Step("c-0", 0, 0.5, "CAREFULLY"),
        Step("c-half-pi", 1.5708, 0.5, "CAREFULLY"),
        Step("c-pi", 3.1416, 0.5, "CAREFULLY"),
        Step("c-neg-half-pi", 4.7124, 0.5, "CAREFULLY"),
        Step("c-1", 1, 0.5, "CAREFULLY"),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "actor-step",
        TraceFile,
        "ovass",
        "`A` lines: TICKS, the cycle count, then the actor's X, Y, ANGLE and TIME_CORR, each times 1024.",
        All);
}
