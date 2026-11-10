// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>actor-width</c> probe: how far a <c>CAREFULLY</c> actor keeps from a wall it
/// walks beside, and whether <c>ACTOR_WIDTH</c> scales it. Apathy's beamed head (OV-20261110-retail-play)
/// passed 4.63 from a rock wall untouched where OpenVirtue held it off at its half width (3.08) times
/// <c>ACTOR_WIDTH</c> 1.5. Each carrier walks one actor 2 wide (a 64-pixel bitmap at <c>SCALE</c> 32)
/// along +x on a way parallel to the room's south wall at y −24, at a gap from it, and prints its y every
/// cycle.
/// </summary>
internal static class ActorWidthProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVAWT0.TXT).</summary>
    public const string TraceFile = "ovawt";

    private const string Declarations =
        "TEXTURE aw_tex { SCALE_X 32; SCALE_Y 32; BMAPS red_map; }\nWAY aw_w;\nSYNONYM aw_s { TYPE ACTOR; }\nSKILL aw_n { VAL 0; }\n" +
        "ACTOR aw_a { TEXTURE aw_tex; TARGET aw_w; SPEED 0; DIST 1; FLAGS CAREFULLY, FAR; }\n";

    private static TraceProbeCase Walk(string label, double width, double gap)
    {
        double y = -24 + gap;
        string run =
            string.Create(CultureInfo.InvariantCulture, $"    SET ACTOR_WIDTH, {width};\n") +
            "    SET aw_s, aw_a;\n" +
            "    WHILE (aw_n < 40) {\n" +
            "        IF (TIME_CORR > 0) { RULE aw_s.SPEED = 0.5 / TIME_CORR; }\n" +
            Indent(V("C", "TICKS", "aw_s.X", "aw_s.Y", "ACTOR_WIDTH")) +
            "        ADD aw_n, 1;\n        WAIT 1;\n    }\n";
        return new TraceProbeCase(
            label,
            string.Create(CultureInfo.InvariantCulture, $"An actor 2 wide walks beside the south wall {gap} from it, ACTOR_WIDTH {width}: held off, and to what?"),
            Declarations,
            run,
            Predict(
                ("held at half width times ACTOR_WIDTH", string.Create(CultureInfo.InvariantCulture, $"y moves to −24 + {width} + 0.049 if the gap is smaller")),
                ("held at the half width alone", "y moves to −24 + 1.049 only when the gap is under 1.049")),
            Map: new TraceProbeMap(
                Placements:
                [
                    string.Create(CultureInfo.InvariantCulture, $"ACTOR aw_a -10 {y} 0 0"),
                    string.Create(CultureInfo.InvariantCulture, $"WAY aw_w -10 {y} 40 {y}"),
                ]));
    }

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Walk("w1-g125", 1, 1.25),
        Walk("w15-g125", 1.5, 1.25),
        Walk("w15-g140", 1.5, 1.4),
        Walk("w15-g160", 1.5, 1.6),
        Walk("w2-g160", 2, 1.6),
        Walk("w2-g210", 2, 2.1),
        Walk("w05-g080", 0.5, 0.8),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "actor-width",
        TraceFile,
        "ovaws",
        "Read `OVAWT0.TXT`: the actor's y each cycle against the south wall at y −24.",
        All);
}
