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

    private static TraceProbeCase View(string label, string question, string setup, string? roomBody = null, string? paletteFlags = null, string? textures = null) => new(
        label,
        question,
        "",
        setup + "    WAIT 2;\n" + V("VIEW", "PLAYER_LIGHT", "LIGHT_DIST", "DARK_DIST", "PLAYER_ARC"),
        Predict(
            ("linear fade from LIGHT_DIST to DARK_DIST", "the side wall's index climbs the ramp steadily between the two distances"),
            ("stepped", "the index changes in steps (16 or 32 grades)")),
        Map: new TraceProbeMap(RoomBody: roomBody ?? "", PaletteBody: Range + (paletteFlags ?? ""), Textures: textures));

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
        View("neg-ambient", "Round 3. Eye at 2, region AMBIENT -0.25 with PLAYER_LIGHT 1: does a negative ambient darken by 8 levels, clamped at 30?",
            LowEye, "AMBIENT -0.25;"),
        View("big-ambient", "Round 3. Eye at 2, region AMBIENT 1 with PLAYER_LIGHT 1: clamped at level 0 everywhere?",
            LowEye, "AMBIENT 1;"),
        View("tex-ambient", "Round 3. Eye at 2, the floor's texture with AMBIENT 0.25 (p.37), PLAYER_LIGHT 1: the floor 8 levels brighter than the walls?",
            LowEye, "FLOOR_TEX sh_flr;", textures: "TEXTURE sh_flr { SCALE_X 4; SCALE_Y 4; BMAPS flr_map; AMBIENT 0.25; }\n"),
        Albedo("round4/albedo", "", "V3.963 has no LIGHT_ANGLE skill (\"Parameter unknown\")"),
        new TraceProbeCase(
            "round4/genius",
            "Round 4. As albedo, with the room's GENIUS an invisible thing near the right wall (20, -20) whose texture has " +
            "AMBIENT 1 (p.58): do the walls' shades now follow their facing toward it?",
            "",
            "    SET PLAYER_LIGHT, 0;\n    WAIT 2;\n" + V("VIEW", "PLAYER_LIGHT", "LIGHT_DIST", "DARK_DIST"),
            Predict(
                ("a light source", "the left, right and far walls take different shades than in albedo"),
                ("no effect", "the same screenshot as albedo")),
            Map: new TraceProbeMap(
                RoomBody: "AMBIENT 0.5; GENIUS sh_lamp;",
                PaletteBody: Range,
                WallTextureBody: "ALBEDO 1; ",
                Textures: "TEXTURE sh_lamp_tex { SCALE_X 4; SCALE_Y 4; BMAPS cel_map; AMBIENT 1; }\nTHING sh_lamp { TEXTURE sh_lamp_tex; FLAGS INVISIBLE; }\n",
                Placements: ["THING sh_lamp 20 -20 0 0"])),
    ];

    /// <summary>
    /// Round 4: the room walls' textures with <c>ALBEDO 1</c> (p.33: reflection of light from
    /// <c>LIGHT_ANGLE</c>), lit by the region's <c>AMBIENT 0.5</c> alone (<c>PLAYER_LIGHT</c> 0), as
    /// <c>region-ambient</c> without the albedo: how do the left, right and far walls differ?
    /// </summary>
    private static TraceProbeCase Albedo(string label, string setup, string angle) => new(
        label,
        $"Round 4. Wall textures with ALBEDO 1, {angle}, region AMBIENT 0.5 and PLAYER_LIGHT 0: does each wall's shade change with its facing?",
        "",
        "    SET PLAYER_LIGHT, 0;\n" + setup + "    WAIT 2;\n" + V("VIEW", "PLAYER_LIGHT", "LIGHT_DIST", "DARK_DIST"),
        Predict(
            ("facing matters", "the left and right side walls and the far wall take different shades"),
            ("no effect", "every wall at region-ambient's shade")),
        Map: new TraceProbeMap(RoomBody: "AMBIENT 0.5;", PaletteBody: Range, WallTextureBody: "ALBEDO 1; "));

    private const string LowEye = "    SET PLAYER_SIZE, 2;\n    SET PLAYER_Z, 2;\n";

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
