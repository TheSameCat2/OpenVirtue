// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>wall-sides</c> probe: which bitmap of a two- or four-sided wall texture
/// (manual p.37) the original draws, by the side and place it is seen from. A free-standing wall runs
/// from v1 (24, −6) to v2 (24, 6) with the room on both sides. Its texture's sides are the probe's
/// solid colours: four sides red, green, yellow, blue; two sides red, green. Each case puts the eye
/// 16 from the wall's line, in front of its middle or off towards one end, faces the wall's middle
/// and leaves the screenshot to show which colours the wall takes, left and right of the centre.
/// </summary>
internal static class WallSidesProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVWSD0.TXT).</summary>
    public const string TraceFile = "ovwsd";

    private static TraceProbeCase View(string label, string question, int sides, double x, double y, double angle) => new(
        label,
        question,
        "",
        FormattableString.Invariant(
            $"    SET PLAYER_X, {x};\n    SET PLAYER_Y, {y};\n    SET PLAYER_SIZE, 4;\n    SET PLAYER_Z, 8;\n    SET PLAYER_ANGLE, {angle};\n    LOCATE;\n    WAIT 3;\n") +
        V("V", "PLAYER_X", "PLAYER_Y", "PLAYER_ANGLE"),
        Predict(
            ("sides by the wall's half", "two colours split at the wall's middle"),
            ("sides by the viewpoint", "one colour, which changes with the end the eye is nearer")),
        Map: new TraceProbeMap(
            Textures: sides == 4
                ? "TEXTURE ws_tex { SCALE_X 4; SCALE_Y 4; SIDES 4; BMAPS red_map, grn_map, yel_map, blu_map; }\nWALL ws_w { TEXTURE ws_tex; }\n"
                : "TEXTURE ws_tex { SCALE_X 4; SCALE_Y 4; SIDES 2; BMAPS red_map, grn_map; }\nWALL ws_w { TEXTURE ws_tex; }\n",
            Walls: [new TraceProbeWall("ws_w", 24, -6, 24, 6)]));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        View("s4-west", "Four sides, eye at (8, 0) facing +x (the wall's west face, square on): which colours?", 4, 8, 0, 0),
        View("s4-east", "Four sides, eye at (40, 0) facing −x (the east face): which colours?", 4, 40, 0, 3.1416),
        View("s4-west-v1", "Four sides, eye at (8, −12), off towards v1, facing the wall's middle: which colours?", 4, 8, -12, 0.6435),
        View("s4-west-v2", "Four sides, eye at (8, 12), off towards v2, facing the wall's middle: which colours?", 4, 8, 12, -0.6435),
        View("s4-east-v1", "Four sides, eye at (40, −12) facing the wall's middle from the east: which colour?", 4, 40, -12, 2.4981),
        View("s4-east-v2", "Four sides, eye at (40, 12) facing the wall's middle from the east: which colour?", 4, 40, 12, -2.4981),
        View("s2-east-v1", "Two sides, eye at (40, −12) facing the wall's middle from the east: which colour?", 2, 40, -12, 2.4981),
        View("s2-west", "Two sides, eye at (8, 0) facing +x: which colours?", 2, 8, 0, 0),
        View("s2-east", "Two sides, eye at (40, 0) facing −x: which colours?", 2, 40, 0, 3.1416),
        View("s2-west-v1", "Two sides, eye at (8, −12) facing the wall's middle: which colours?", 2, 8, -12, 0.6435),
        View("s2-west-v2", "Two sides, eye at (8, 12) facing the wall's middle: which colours?", 2, 8, 12, -0.6435),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "wall-sides",
        TraceFile,
        "ovwss",
        "`V` holds the eye's position and angle; the screenshot `OVWSS0.PCX` shows the wall's colours.",
        All);
}
