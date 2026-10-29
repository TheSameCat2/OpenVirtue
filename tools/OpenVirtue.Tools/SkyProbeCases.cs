// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>sky</c> probe: where the original draws a <c>FLAGS SKY</c> texture (manual
/// p.36–38, p.103). The manual says a sky's sides spread evenly over the 360° panorama, its
/// horizontal scale follows from the bitmap width and the number of sides, it is not scaled
/// vertically, its lower edge "usually" touches the horizon, the bitmap's top and bottom rows fill
/// the rest, and <c>OFFSET_X/Y</c> and <c>SKY_OFFS_X/Y</c> shift it in pixels. It does not say which
/// side faces angle 0, which way it scrolls when the player turns, whether a column's position is
/// linear in the screen column or follows the projection's angles, or how <c>PLAYER_TILT</c> moves it.
/// <para>
/// The room's ceiling is the sky texture: four 64 × 64 sides, the left half of each in red, green,
/// blue or magenta and the right half yellow, with a marker band over rows 16–47. Each case sets the
/// view, waits, and the scaffold's <c>SCREENSHOT</c> captures it (<c>OVSKS0.PCX</c>); the trace
/// records the skills the screenshot was taken with.
/// </para>
/// </summary>
internal static class SkyProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVSKY0.TXT).</summary>
    public const string TraceFile = "ovsky";

    public const int SideWidth = 64;

    public const int SideHeight = 64;

    /// <summary>The bitmap file of sky side <paramref name="side"/> (0–3).</summary>
    public static string SideFile(int side) => $"PRBSK{(char)('A' + side)}.PCX";

    private static readonly string Declarations =
        "BMAP sky_a,<" + SideFile(0) + ">;\nBMAP sky_b,<" + SideFile(1) + ">;\nBMAP sky_c,<" + SideFile(2) + ">;\nBMAP sky_d,<" + SideFile(3) + ">;\n" +
        "TEXTURE sky_tex { SIDES 4; BMAPS sky_a, sky_b, sky_c, sky_d; FLAGS SKY; }\n";

    private static readonly TraceProbeMap SkyRoom = new(RoomCeiling: "sky_tex", Textures: Declarations);

    private static TraceProbeCase View(string label, string question, string setup) => new(
        label,
        question,
        "",
        setup + "    WAIT 2;\n" +
        V("VIEW", "PLAYER_ANGLE", "PLAYER_TILT", "PLAYER_ARC", "SKY_OFFS_X", "SKY_OFFS_Y", "SCREEN_WIDTH", "SCREEN_HGT"),
        Predict(
            ("sides spread over 360°, linear in the column", "colour boundaries equally spaced, 64 px of bitmap per (π/2)/4 of angle"),
            ("columns follow the projection's angles", "boundaries closer together toward the screen centre"),
            ("lower edge on the horizon", "the marker band ends 16 rows above the horizon row")),
        Map: SkyRoom);

    private static string Angle(double angle) =>
        string.Create(CultureInfo.InvariantCulture, $"    SET PLAYER_ANGLE, {angle};\n");

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        View("angle-0", "Facing angle 0, tilt 0: which sides and halves are on screen, where are their boundaries, where is the band?", Angle(0)),
        View("angle-0.2", "Turned to 0.2 rad: which way and how far do the boundaries move?", Angle(0.2)),
        View("angle-0.4", "Turned to 0.4 rad.", Angle(0.4)),
        View("angle-1.6", "Turned to 1.6 rad (about a quarter turn): the next side.", Angle(1.6)),
        View("angle-3.2", "Turned to 3.2 rad (about a half turn).", Angle(3.2)),
        View("tilt-up", "PLAYER_TILT -0.3 (looking up): does the band move with the y-shear, by how many rows?", Angle(0) + "    SET PLAYER_TILT, -0.3;\n"),
        View("tilt-down", "PLAYER_TILT 0.3 (looking down).", Angle(0) + "    SET PLAYER_TILT, 0.3;\n"),
        View("offs-x", "SKY_OFFS_X 16: which way do the boundaries move, and by 16 screen columns or 16 bitmap pixels scaled?", Angle(0) + "    SET SKY_OFFS_X, 16;\n"),
        View("offs-y", "SKY_OFFS_Y 8: which way does the band move?", Angle(0) + "    SET SKY_OFFS_Y, 8;\n"),
        View("arc-2", "PLAYER_ARC 2: is the sky scaled with the field of view or fixed in pixels per angle?", Angle(0) + "    SET PLAYER_ARC, 2;\n"),
        View("high", "Round 3. The eye raised to 15 in the 16-high room, so the far wall's top sits just above the horizon and the band shows: where is it, how tall?", Angle(0) + High),
        View("high-tilt-up", "Round 3. Eye at 15, PLAYER_TILT -0.1: does the band move with the world's y-shear (48 rows down)?", Angle(0) + High + "    SET PLAYER_TILT, -0.1;\n"),
        View("high-tilt-down", "Round 3. Eye at 15, PLAYER_TILT 0.1.", Angle(0) + High + "    SET PLAYER_TILT, 0.1;\n"),
        View("high-offs-y", "Round 3. Eye at 15, SKY_OFFS_Y 8: which way does the band move, and by 8 rows?", Angle(0) + High + "    SET SKY_OFFS_Y, 8;\n"),
    ];

    /// <summary>Raises the eye to 15 (the room is 16 high), so the band clears the far wall's top.</summary>
    private const string High = "    SET PLAYER_SIZE, 15;\n    SET PLAYER_Z, 15;\n";

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "sky",
        TraceFile,
        "ovsks",
        "The room's ceiling is a four-sided sky: the left half of each side red, green, blue or magenta,\n" +
        "the right half yellow, a marker band over bitmap rows 16-47. Read `OVSKS0.PCX`: the colour runs\n" +
        "along a row in the ceiling area give the horizontal mapping, a column through the band the vertical\n" +
        "placement. `OVSKY0.TXT` holds the view the screenshot was taken with.",
        All);
}
