// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>wall-light</c> probe: a wall between two regions with different
/// <c>AMBIENT</c> (manual p.58) — does it take its left region's, the region on the face the eye
/// sees, or the eye's own region? OV-20261029-shading read one wall <c>AMBIENT</c> per carrier and
/// walked away from it with the left region's value; every carrier here has two.
/// <para>
/// The pad island (x −12…−4, y 12…20) carries the room's blue wall texture, whose bitmap is the
/// gray ramp's brightest colour (index 16) and whose palette declares one <c>RANGE</c>
/// (<see cref="ProjectionProbeGenerator.ShadeRampStart"/>, 32 entries), with
/// <c>PLAYER_LIGHT</c> 0 so one wall shows one index: 16 + (30 − 32·AMBIENT), i.e. index 30 at
/// <c>AMBIENT</c> 0.5 and 38 at 0.25. "A" is the room at 0.5 and the pad at 0.25, "B" the swap. In
/// the WMP the pad is region 2 and the room region 0, so every pad wall is <c>2 0</c> and the left
/// region is the pad.
/// </para>
/// </summary>
internal static class WallLightProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVWLG0.TXT).</summary>
    public const string TraceFile = "ovwlg";

    /// <summary>The gray ramp as one RANGE, so a shade level lands on a palette index.</summary>
    private static readonly string Range = string.Create(
        CultureInfo.InvariantCulture,
        $"RANGE {ProjectionProbeGenerator.ShadeRampStart},{ProjectionProbeGenerator.ShadeRampLength};");

    /// <summary>Turned to the pad island from (24, −8); the room is x −16…48, y −24…24, so 16 steps from the nearest wall.</summary>
    private const string FarRun = "    SET PLAYER_X, 24;\n    SET PLAYER_Y, -8;\n    SET PLAYER_ANGLE, 2.498;\n    SET PLAYER_LIGHT, 0;\n";

    /// <summary>Turned to the pad island from the origin, tilted down 0.3, with the player light off.</summary>
    private const string NearRun = "    SET PLAYER_ANGLE, 2.03;\n    SET PLAYER_TILT, 0.3;\n    SET PLAYER_LIGHT, 0;\n";

    /// <summary>Every carrier reads one wall, so it shows one of three indices — one per candidate rule.</summary>
    private static (string, string)[] Rules(int left, int facing, int eye) =>
    [
        ("the left region's AMBIENT", $"index {left}"),
        ("the region on the face the eye sees", $"index {facing}"),
        ("the eye's own region", $"index {eye}"),
    ];

    private static TraceProbeCase Pad(
        string label,
        string question,
        bool swapped,
        int floor,
        int? ceiling,
        string run,
        params (string, string)[] predictions) => new(
            label,
            question,
            "",
            run + "    WAITT 8;\n" + P("RET"),
            Predict(predictions),
            Map: new TraceProbeMap(
                RoomBody: swapped ? "AMBIENT 0.25;" : "AMBIENT 0.5;",
                PadBody: swapped ? "AMBIENT 0.5;" : "AMBIENT 0.25;",
                PadFloor: floor,
                PadCeiling: ceiling,
                PadWall: "blu_wall",
                PaletteBody: Range));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Pad(
            "step-a",
            "The pad raised to floor 6, its south and east faces seen from the room; the room's AMBIENT 0.5, the pad's 0.25.",
            swapped: false,
            floor: 6,
            ceiling: null,
            FarRun,
            predictions: Rules(38, 30, 30)),
        Pad(
            "step-b",
            "As step-a with the ambients swapped: the room 0.25, the pad 0.5.",
            swapped: true,
            floor: 6,
            ceiling: null,
            FarRun,
            predictions: Rules(30, 38, 38)),
        Pad(
            "ceil-a",
            "The pad's ceiling dropped to 11, so the wall on view is the upper piece between 11 and 16; the room's AMBIENT 0.5, the pad's 0.25.",
            swapped: false,
            floor: 0,
            ceiling: 11,
            FarRun,
            predictions: Rules(38, 30, 30)),
        Pad(
            "ceil-b",
            "As ceil-a with the ambients swapped.",
            swapped: true,
            floor: 0,
            ceiling: 11,
            FarRun,
            predictions: Rules(30, 38, 38)),
        Pad(
            "pit-a",
            "The pad sunk to floor −4, seen from the origin tilted down 0.3: the eye stands in the room while the face it looks into is the pit's; the room's AMBIENT 0.5, the pad's 0.25.",
            swapped: false,
            floor: -4,
            ceiling: null,
            NearRun,
            predictions: Rules(38, 38, 30)),
        Pad(
            "pit-b",
            "As pit-a with the ambients swapped.",
            swapped: true,
            floor: -4,
            ceiling: null,
            NearRun,
            predictions: Rules(30, 30, 38)),
        Pad(
            "both-a",
            "The pad raised to floor 4 and its ceiling dropped to 12: a lower piece (0…4) and an upper one (12…16) at once; the room's AMBIENT 0.5, the pad's 0.25.",
            swapped: false,
            floor: 4,
            ceiling: 12,
            FarRun,
            predictions: Rules(38, 30, 30)),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "wall-light",
        TraceFile,
        "ovwls",
        "The final screenshot `OVWLS0.PCX` is the reading: the index over the pad's wall is 16 plus its\n" +
        "shade level (30 for AMBIENT 0.5, 38 for 0.25). Compare with `case.json` → `predictions`, check\n" +
        "`OVCON.TXT` for compile errors.",
        All);
}
