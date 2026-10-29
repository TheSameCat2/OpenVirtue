// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>shading</c> probe: how the original darkens a texel with distance and light
/// (manual p.34–35, p.37, p.58, p.112). The palette's <c>RANGE</c> lists colours from bright to dark;
/// a texture's and its region's <c>AMBIENT</c>, the player's light source <c>PLAYER_LIGHT</c> (16
/// grades), <c>LIGHT_DIST</c> (where shading starts, default 10) and <c>DARK_DIST</c> (recomputed from
/// <c>PLAYER_LIGHT</c>) set a texel's brightness, and <c>FLAGS HARD</c> narrows the transition. None of
/// the curves are given.
/// <para>
/// The carriers' palette has a 32-step gray ramp at <see cref="ProjectionProbeGenerator.ShadeRampStart"/>
/// declared as one <c>RANGE</c>; the side walls and the floor are its brightest colour. Each screenshot
/// (<c>OVSHS0.PCX</c>) holds the shaded palette index at every depth of the receding side walls and
/// floor; the trace records the light skills.
/// </para>
/// </summary>
internal static class ShadingProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVSHD0.TXT).</summary>
    public const string TraceFile = "ovshd";

    private static readonly string Range = string.Create(
        CultureInfo.InvariantCulture,
        $"RANGE {ProjectionProbeGenerator.ShadeRampStart},{ProjectionProbeGenerator.ShadeRampLength}; ");

    private static TraceProbeCase View(string label, string question, string setup, string? roomBody = null, string? paletteFlags = null) => new(
        label,
        question,
        "",
        setup + "    WAIT 2;\n" + V("VIEW", "PLAYER_LIGHT", "LIGHT_DIST", "DARK_DIST", "PLAYER_ARC"),
        Predict(
            ("linear fade from LIGHT_DIST to DARK_DIST", "the side wall's index climbs the ramp steadily between the two distances"),
            ("stepped", "the index changes in steps (16 or 32 grades)")),
        Map: new TraceProbeMap(RoomBody: roomBody ?? "", PaletteBody: Range + (paletteFlags ?? "")));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        View("light-1", "PLAYER_LIGHT 1 (the default): the shade index against depth on the side walls and the floor.", ""),
        View("light-0.5", "PLAYER_LIGHT 0.5: where does darkness start and end now (DARK_DIST)?", "    SET PLAYER_LIGHT, 0.5;\n"),
        View("light-0", "PLAYER_LIGHT 0: all dark, or the ambient level?", "    SET PLAYER_LIGHT, 0;\n"),
        View("lightdist-20", "LIGHT_DIST 20: does the bright zone reach to 20?", "    SET LIGHT_DIST, 20;\n"),
        View("region-ambient", "Region AMBIENT 0.5 with PLAYER_LIGHT 0: the floor and walls lit by the region alone.", "    SET PLAYER_LIGHT, 0;\n", "AMBIENT 0.5;"),
        View("region-ambient-light", "Region AMBIENT 0.5 with PLAYER_LIGHT 1: how do the two add up?", "", "AMBIENT 0.5;"),
        View("hard", "FLAGS HARD on the palette: a narrower transition?", "", paletteFlags: "FLAGS HARD; "),
        Low("low-p1-ld10", 1, 10),
        Low("low-p1-ld5", 1, 5),
        Low("low-p1-ld20", 1, 20),
        Low("low-p1-ld40", 1, 40),
        Low("low-p075-ld10", 0.75, 10),
        Low("low-p05-ld10", 0.5, 10),
        Low("low-p025-ld10", 0.25, 10),
        Low("low-p05-ld20", 0.5, 20),
    ];

    /// <summary>
    /// Round 2: the eye lowered to 2, so the floor shows from about 5.5 steps out to the far wall at 48,
    /// with the given <c>PLAYER_LIGHT</c> and <c>LIGHT_DIST</c>.
    /// </summary>
    private static TraceProbeCase Low(string label, double light, double lightDistance) => View(
        label,
        string.Create(CultureInfo.InvariantCulture, $"Round 2. Eye at 2, PLAYER_LIGHT {light}, LIGHT_DIST {lightDistance}: the floor's shade from 5.5 to 48 steps."),
        string.Create(
            CultureInfo.InvariantCulture,
            $"    SET PLAYER_SIZE, 2;\n    SET PLAYER_Z, 2;\n    SET PLAYER_LIGHT, {light};\n    SET LIGHT_DIST, {lightDistance};\n"));

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "shading",
        TraceFile,
        "ovshs",
        "The palette has a 32-colour gray RANGE from index 16 (brightest) down; the side walls and the floor\n" +
        "are index 16. Read `OVSHS0.PCX`: the palette index along a row through the side walls (depth from\n" +
        "the projection) and down a column through the floor gives the shade against distance.\n" +
        "`OVSHD0.TXT` holds PLAYER_LIGHT, LIGHT_DIST, DARK_DIST and PLAYER_ARC.",
        All);
}
