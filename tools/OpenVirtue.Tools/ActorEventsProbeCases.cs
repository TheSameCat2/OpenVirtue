// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>actor-events</c> probe: how actors move toward their <c>TARGET</c> and when
/// <c>IF_ARRIVED</c> and <c>IF_HIT</c> run (manual p.65–70). The manual says a <c>WAY</c> actor
/// moves toward waypoint 1 and triggers <c>IF_ARRIVED</c> at each waypoint, a <c>VERTEX</c> or
/// object target triggers it on arrival, <c>FOLLOW</c>/<c>MOVE</c> with <c>CAREFULLY</c> trigger it at
/// every region border, and a <c>BULLET</c> triggers <c>IF_HIT</c> on an obstacle. It does not say
/// how close counts as arrived, whether the actor stops on the point or overshoots, when
/// <c>WAYPOINT</c> advances relative to the hook, whether a reached <c>VERTEX</c> keeps triggering,
/// or where the hooks run relative to the actor's own <c>EACH_TICK</c>.
/// <para>
/// The actor's <c>EACH_TICK</c> prints an <c>A</c> line every cycle while <c>ae_on</c> is set, the
/// hooks print <c>ARR</c>/<c>HIT</c>; every line is the token, <c>TICKS</c>, <c>ae_n</c> (the runs of
/// the global slot-1 counter, which counts first), then the actor's <c>X</c>, <c>Y</c>,
/// <c>WAYPOINT</c>, <c>TARGET_X</c> and <c>TARGET_Y</c>.
/// </para>
/// </summary>
internal static class ActorEventsProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVAEV0.TXT).</summary>
    public const string TraceFile = "ovaev";

    private const string Skills =
        "SKILL ae_n { VAL 0; }\nSKILL ae_on { VAL 0; }\nSYNONYM ae_s { TYPE ACTOR; }\n" +
        "TEXTURE ae_tex { SCALE_X 32; SCALE_Y 32; BMAPS yel_map; }\n";

    private static readonly string Counter = Action("ae_g1", "    RULE ae_n = ae_n + 1;\n");

    /// <summary>A line with the token, <c>TICKS</c>, <c>ae_n</c> and the actor's movement state.</summary>
    private static string A(string token) =>
        "    IF (ae_on > 0) {\n" + Indent(V(token, "TICKS", "ae_n", "MY.X", "MY.Y", "MY.WAYPOINT", "MY.TARGET_X", "MY.TARGET_Y")) + "    }\n";

    private static readonly string Hooks =
        Action("ae_tick", A("A")) + Action("ae_arr", A("ARR")) + Action("ae_hit", A("HIT"));

    /// <summary>
    /// Arms the counter, runs <paramref name="setup"/> on the actor (through the synonym <c>ae_s</c>),
    /// opens the window for <paramref name="ticks"/> ticks, closes it. The compiler accepts only
    /// <c>NULL</c>, <c>FOLLOW</c>, <c>REPEL</c>, a way or an object as a declared <c>TARGET</c>; the retail
    /// scripts, like these cases, set <c>MOVE</c>, <c>BULLET</c> and <c>VERTEX</c> with <c>SET</c> (Error 111
    /// otherwise, OV-20261029-actor-events).
    /// </summary>
    private static string Run(int ticks, string setup = "") =>
        "    SET ae_s, ae_a;\n    SET EACH_TICK.1, ae_g1;\n" + setup + "    RULE ae_on = 1;\n" + V("GO", "TICKS", "ae_n") +
        $"    WAITT {ticks};\n    RULE ae_on = 0;\n    SET EACH_TICK.1, NULL;\n" + P("RET");

    /// <summary>The actor, its properties in the manual's order (p.64–71): movement, hooks, flags.</summary>
    private static string Actor(string movement, string flags = "") =>
        Skills + Counter + Hooks + "ACTOR ae_a { TEXTURE ae_tex; " + movement +
        " IF_ARRIVED ae_arr; IF_HIT ae_hit; EACH_TICK ae_tick;" + (flags.Length > 0 ? " FLAGS " + flags + ";" : "") + " }\n";

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "round3/way-start",
            "Round 3. The WAY actor of `way`, with a slot armed in the start action printing TICKS, the cycle, TIME_CORR " +
            "and the actor's X from the first cycle: how far does it move in the first cycles, where TIME_CORR reads 1 and " +
            "TICKS 0? (`way` is 0.058 short of OpenVirtue by TICKS 13.4.)",
            "WAY ae_way;\n" + Actor("TARGET ae_way; SPEED 0.5;") +
            "SKILL ae_c { VAL 0; }\nACTION ae_y\n{\n    RULE ae_c = ae_c + 1;\n    IF (ae_c < 80) {\n" +
            Indent(Indent(V("Y", "TICKS", "ae_c", "TIME_CORR", "ae_a.X"))) + "    }\n}\n",
            "    SET EACH_TICK.2, NULL;\n" + P("RET"),
            Predict(
                ("a full first step", "X grows by 0.5 in the cycle with TIME_CORR 1"),
                ("no first step", "X is 10 until TICKS grows")),
            Map: new TraceProbeMap(Placements: ["ACTOR ae_a 10 -6 0 0", "WAY ae_way 20 -6 20 6 10 6"]),
            Prelude: "    PRINTFILE \"" + TraceFile + "\", 0;\n" + V("S0", "TICKS", "ae_c", "TIME_CORR", "ae_a.X") +
                "    SET EACH_TICK.2, ae_y;\n"),
        new TraceProbeCase(
            "way",
            "A WAY actor (SPEED 0.5) on a triangle of waypoints: how close does it come to each point, does it land on it, when does WAYPOINT advance relative to IF_ARRIVED, and does it wrap from the last point to the first?",
            "WAY ae_way;\n" + Actor("TARGET ae_way; SPEED 0.5;"),
            Run(160),
            Predict(
                ("lands on each point", "an ARR line at X/Y equal to the waypoint"),
                ("arrives within one step", "ARR with X/Y less than SPEED·TIME_CORR from the point"),
                ("WAYPOINT advances before the hook", "ARR already shows the next WAYPOINT"),
                ("cycles", "after waypoint 3 WAYPOINT goes back to 1")),
            Map: new TraceProbeMap(Placements: ["ACTOR ae_a 10 -6 0 0", "WAY ae_way 20 -6 20 6 10 6"])),
        new TraceProbeCase(
            "vertex",
            "TARGET VERTEX toward (20, 6) at SPEED 0.5: when does IF_ARRIVED run, once or in every cycle after arrival, and does the actor stop on the point?",
            Actor("SPEED 0.5;"),
            Run(64, "    SET ae_s.TARGET_X, 20;\n    SET ae_s.TARGET_Y, 6;\n    SET ae_s.TARGET, VERTEX;\n"),
            Predict(
                ("once, then stands", "one ARR; later A lines at (20, 6)"),
                ("every cycle once there", "an ARR in every cycle after arrival"),
                ("overshoots", "A lines past (20, 6)")),
            Map: new TraceProbeMap(Placements: ["ACTOR ae_a 10 -6 0 0"])),
        new TraceProbeCase(
            "thing-target",
            "TARGET naming a thing at (20, 6): where does the actor stop (on its position, at contact), and when does IF_ARRIVED run?",
            Actor("TARGET ae_goal; SPEED 0.5;") + "THING ae_goal { TEXTURE ae_tex; }\n",
            Run(64),
            Predict(
                ("on the position", "the actor reaches (20, 6)"),
                ("at contact", "it stops about the two textures' half widths away")),
            Map: new TraceProbeMap(Placements: ["THING ae_goal 20 6 0 0", "ACTOR ae_a 10 -6 0 0"])),
        new TraceProbeCase(
            "follow-border",
            "A CAREFULLY FOLLOW actor starts on the pad island (-8, 16) and walks to the player at the origin: does IF_ARRIVED run when it leaves the pad, in which cycle relative to the A line that shows it outside, and again anywhere else?",
            Actor("TARGET FOLLOW; SPEED 0.5;", "CAREFULLY"),
            Run(64),
            Predict(
                ("on crossing", "one ARR in the cycle its Y first drops below 12"),
                ("never", "no ARR line")),
            Map: new TraceProbeMap(PadBody: "", Placements: ["ACTOR ae_a -8 16 0 2"])),
        new TraceProbeCase(
            "move-border",
            "A CAREFULLY MOVE actor starts on the pad (-8, 16) facing -Y (angle 270): IF_ARRIVED at the pad border, and what happens at the room's wall at y = -24 (stop, slide, IF_HIT)?",
            Actor("SPEED 0.5;", "CAREFULLY"),
            Run(112, "    SET ae_s.TARGET, MOVE;\n"),
            Predict(
                ("ARR at the border, stops at the wall", "ARR near y 12, then Y stays near -24 + its half width"),
                ("IF_HIT at the wall", "a HIT line near y -24")),
            Map: new TraceProbeMap(PadBody: "", Placements: ["ACTOR ae_a -8 16 270 2"])),
        new TraceProbeCase(
            "bullet",
            "A CAREFULLY BULLET actor at (10, 0) facing +X (angle 0) flies to the far wall at x = 48: when does IF_HIT run, where is it then, and does it keep moving or keep hitting?",
            Actor("SPEED 0.5;", "CAREFULLY"),
            Run(112, "    SET ae_s.TARGET, BULLET;\n"),
            Predict(
                ("one hit, then it stops", "one HIT near x 47; A lines stand still after"),
                ("hits in every cycle", "a HIT line in every cycle after the first")),
            Map: new TraceProbeMap(Placements: ["ACTOR ae_a 10 0 0 0"])),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "actor-events",
        TraceFile,
        "ovaes",
        "Every line is a token, `TICKS`, `ae_n` (runs of the global slot-1 counter, which counts first), then the\n" +
        "actor's `X`, `Y`, `WAYPOINT`, `TARGET_X` and `TARGET_Y`. `A` comes from the actor's `EACH_TICK` in every\n" +
        "cycle, `ARR` from `IF_ARRIVED`, `HIT` from `IF_HIT`. Compare with `case.json` → `predictions` and run\n" +
        "`ovtool oracle diff` on the probe.",
        All);
}
