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

    private static TraceProbeCase Step(string label, double angle, double speed) => new(
        label,
        string.Create(CultureInfo.InvariantCulture, $"A BULLET at angle {angle}, SPEED {speed}: how do X and Y step, unit by unit?"),
        Skills + Hooks + "ACTOR as_a { TEXTURE as_tex; SPEED " + speed.ToString(CultureInfo.InvariantCulture) + "; EACH_TICK as_tick; }\n",
        "    SET as_s, as_a;\n    SET EACH_TICK.1, as_g1;\n" +
        string.Create(CultureInfo.InvariantCulture, $"    SET as_s.ANGLE, {angle};\n") +
        "    SET as_s.TARGET, BULLET;\n    RULE as_on = 1;\n" + V("GO", "TICKS", "as_n") +
        "    WAITT 24;\n    RULE as_on = 0;\n    SET EACH_TICK.1, NULL;\n" + P("RET"),
        Predict(
            ("x += step·cos, y += step·sin in 22.10", "Y stays 0 at angle 0"),
            ("a lateral unit per step", "Y grows by 4 per step at angle 0")),
        Map: new TraceProbeMap(Placements: ["ACTOR as_a 10 0 0 0"]));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
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
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "actor-step",
        TraceFile,
        "ovass",
        "`A` lines: TICKS, the cycle count, then the actor's X, Y, ANGLE and TIME_CORR, each times 1024.",
        All);
}
