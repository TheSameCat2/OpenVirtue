// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>slope</c> probe: how vertex heights tilt a region (manual p.59–62). The manual
/// says <c>FLOOR_ASCEND</c>/<c>CEIL_ASCEND</c> lift a floor's or ceiling's corners by their vertices'
/// Z and the <c>DESCEND</c> flags lower them; the retail levels also use <c>FLOOR_LIFTED</c> and
/// <c>CEIL_LIFTED</c>, which it does not mention, and many of their regions share sloped vertices
/// without any flag.
/// <para>
/// The pad island's corners (−12,12), (−4,12), (−4,20), (−12,20) get the heights 0, 2, 4, 2; the pad
/// (floor 0, ceiling 16) carries one flag per case. The player is beamed to five points on the pad,
/// <c>LOCATE</c>d, given two cycles, and every point prints <c>FLOOR_HGT</c>, <c>CEIL_HGT</c>,
/// <c>PLAYER_Z</c> and <c>PLAYER_HGT</c> (<c>PLAYER_REGION</c> is a region, which <c>PRINT_VALUE</c>
/// rejects: "Error(66): Wrong typ … FIXED REGION").
/// </para>
/// </summary>
internal static class SlopeProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVSLP0.TXT).</summary>
    public const string TraceFile = "ovslp";

    private static readonly double[] CornerZ = [0, 2, 4, 2];

    private static readonly (string Token, double X, double Y)[] Points =
        [("P0", -11, 13), ("P1", -5, 13), ("P2", -5, 19), ("P3", -11, 19), ("PC", -8, 16)];

    private static string Visit(string token, double x, double y) =>
        FormattableString.Invariant($"    SET PLAYER_X, {x};\n    SET PLAYER_Y, {y};\n    LOCATE;\n    WAIT 2;\n") +
        V(token, "PLAYER_X", "PLAYER_Y", "FLOOR_HGT", "CEIL_HGT", "PLAYER_Z", "PLAYER_HGT");

    private static readonly string Tour = string.Concat(Points.Select(static p => Visit(p.Token, p.X, p.Y))) + P("RET");

    private static TraceProbeCase Slope(string label, string flags, string question) => new(
        label,
        question,
        "",
        Tour,
        Predict(
            ("the flagged surface follows the corner heights", "FLOOR_HGT (or CEIL_HGT) 0, 2, 4, 2 at the corners, 2 at the centre"),
            ("DESCEND lowers", "−0, −2, −4, −2"),
            ("no flag: flat", "FLOOR_HGT 0 and CEIL_HGT 16 everywhere")),
        Map: new TraceProbeMap(PadBody: flags.Length == 0 ? "" : $"FLAGS {flags};", PadZ: CornerZ));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Slope("none", "", "Corner heights 0, 2, 4, 2 but no slope flag: is the pad flat?"),
        Slope("floor-ascend", "FLOOR_ASCEND", "FLOOR_ASCEND: does the floor rise by the corner heights, and does the ceiling stay?"),
        Slope("floor-descend", "FLOOR_DESCEND", "FLOOR_DESCEND: does the floor sink by them?"),
        Slope("floor-lifted", "FLOOR_LIFTED", "FLOOR_LIFTED (undocumented, used by the retail levels): what does it do to the floor?"),
        Slope("ceil-ascend", "CEIL_ASCEND", "CEIL_ASCEND: does the ceiling rise, and the floor stay?"),
        Slope("ceil-descend", "CEIL_DESCEND", "CEIL_DESCEND: does the ceiling sink?"),
        Slope("ceil-lifted", "CEIL_LIFTED", "CEIL_LIFTED (undocumented): what does it do to the ceiling?"),
        Slope("both-ascend", "FLOOR_ASCEND, CEIL_ASCEND", "Both ASCEND flags: floor and ceiling rise together?"),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "slope",
        TraceFile,
        "ovsls",
        "The pad's corners have the heights 0, 2, 4, 2; each case sets one slope flag on the pad. Every line\n" +
        "is a point (P0-P3 the corners' insides, PC the centre) with PLAYER_X, PLAYER_Y, FLOOR_HGT, CEIL_HGT,\n" +
        "PLAYER_Z and PLAYER_HGT after a beam, LOCATE and two cycles.",
        All);
}
