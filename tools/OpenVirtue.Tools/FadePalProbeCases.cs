// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>fade-pal</c> probe: what <c>FADE_PAL palette, factor</c> does to the
/// screen's palette (manual pp. 83–84: fades from the current palette toward the named one by the
/// factor, 0..1; a factor of 1 or above makes the named palette the current one). The retail title
/// fades every level in from and out to a uniform near-black palette, flashes a uniform red on
/// damage and tints its menus a uniform blue, all with plain factors. Each case takes
/// <c>SCREENSHOT</c>s between its fades; the saved PCX carries the palette the screen showed, so
/// the per-entry rule (lerp, rounding, the 6-bit DAC), the base of a fade (the level palette or the
/// current one), what a factor of 0 restores after a factor of 1, and factors outside 0..1 are read
/// from the screenshots' palettes against the known palette files.
/// </summary>
internal static class FadePalProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVFPL0.TXT).</summary>
    public const string TraceFile = "ovfpl";

    /// <summary>The screenshot base name (the template's final shot is OVFPS0.PCX, the cases' own 1…n).</summary>
    public const string ScreenshotName = "ovfps";

    /// <summary>A palette whose every entry is black.</summary>
    public const string BlackFile = "PRBPBLK.PCX";

    /// <summary>A palette whose every entry is the retail damage red (200, 30, 15).</summary>
    public const string RedFile = "PRBPRED.PCX";

    /// <summary>A palette whose entry i is (i, 255 − i, 128): every entry differs, so a fade shows its per-entry rule.</summary>
    public const string RampFile = "PRBPRMP.PCX";

    /// <summary>The ramp palette's entry.</summary>
    public static (byte R, byte G, byte B) RampEntry(int index) => ((byte)index, (byte)(255 - index), 128);

    /// <summary>
    /// A palette whose entries are told apart on the 6-bit DAC: R = 4·(i mod 64), G = 4·21·(i div 64),
    /// B = 4·(63 − i mod 64); declared last in the swatch case so that it is the one shown at the start.
    /// </summary>
    public const string UniqueFile = "PRBPUNI.PCX";

    /// <summary>The unique palette's entry.</summary>
    public static (byte R, byte G, byte B) UniqueEntry(int index) => ((byte)(4 * (index % 64)), (byte)(4 * 21 * (index / 64)), (byte)(4 * (63 - (index % 64))));

    private static readonly string Declarations =
        $"PALETTE fp_blk {{ PALFILE <{BlackFile}>; }}\nPALETTE fp_red {{ PALFILE <{RedFile}>; }}\nPALETTE fp_rmp {{ PALFILE <{RampFile}>; }}\n";

    private static string Shot(int number) =>
        $"    SCREENSHOT \"{ScreenshotName}\", {number.ToString(CultureInfo.InvariantCulture)};\n" + V("S" + number.ToString(CultureInfo.InvariantCulture), "TICKS");

    private static string Fade(string palette, string factor, int shot, string wait = "    WAITT 2;\n") =>
        $"    FADE_PAL {palette}, {factor};\n" + wait + Shot(shot);

    private static TraceProbeCase Case(string label, string question, string run, string? paletteBody, params (string, string)[] predictions) => new(
        label,
        question,
        Declarations,
        run + P("RET"),
        Predict(predictions),
        Map: new TraceProbeMap(PaletteBody: paletteBody));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Case(
            "start",
            "No FADE_PAL at all, three more palettes declared after the level's: which palette does the screen show at the start, the first declared (the level's PRBPAL.PCX) or the last (the ramp)?",
            "",
            null,
            ("the first declared palette is the level's", "OVFPS0.PCX carries PRBPAL.PCX's colours (252 for 255 through the 6-bit DAC)"),
            ("the last declared palette is current", "OVFPS0.PCX carries the ramp")),
        Case(
            "black",
            "FADE_PAL fp_blk at 0.25, 0.5, 0.75 and 1 (shots 1–4), then 0 (shot 5) and 0.5 again (shot 6): the per-entry rule toward black, what 0 restores after 1 made black current, and the base of the fade after that.",
            Fade("fp_blk", "0.25", 1) + Fade("fp_blk", "0.5", 2) + Fade("fp_blk", "0.75", 3) + Fade("fp_blk", "1", 4) + Fade("fp_blk", "0", 5) + Fade("fp_blk", "0.5", 6),
            null,
            ("entry = level + f·(black − level), floor to the 6-bit DAC", "shot 2's white is 124 or 128; shot 5 the level palette again; shot 6 as shot 2"),
            ("1 makes black current and 0 keeps it", "shots 5 and 6 black"),
            ("the base is the current palette, so 0.5 after 0.25 fades from the 0.25 blend", "shot 2 darker than a plain 0.5 blend")),
        Case(
            "ramp",
            "FADE_PAL fp_rmp at 0.5, 0.3 and 1 (shots 1–3), then fp_blk 0.5 and 0 (shots 4, 5), probe_pal 1 (shot 6) and fp_rmp 0 (shot 7): the per-entry rule with every entry different, whether a fade after a factor of 1 starts from the new current palette, whether 0 of another palette restores the level's or the current one, and whether the level palette can be named.",
            Fade("fp_rmp", "0.5", 1) + Fade("fp_rmp", "0.3", 2) + Fade("fp_rmp", "1", 3) + Fade("fp_blk", "0.5", 4) + Fade("fp_blk", "0", 5) + Fade("probe_pal", "1", 6) + Fade("fp_rmp", "0", 7),
            null,
            ("per entry, from the level palette, 0 always the level palette", "shot 1 entry i = (i/2, (255−i)/2, 64) over the unset entries; shot 5 the level palette"),
            ("1 replaces the current palette and later fades start from it", "shot 4 the ramp halved, shot 5 the ramp"),
            ("probe_pal can be faded to", "shot 6 the level palette")),
        Case(
            "red",
            "FADE_PAL fp_red at 0.9 and 0 (shots 1, 2), 0.3 (shot 3), then fp_blk 0.3 without a 0 between (shot 4), fp_red 1.5 (shot 5), −0.5 (shot 6) and 0.125 (shot 7): the rule toward the retail red, a change of target mid-fade, and factors outside 0..1.",
            Fade("fp_red", "0.9", 1) + Fade("fp_red", "0", 2) + Fade("fp_red", "0.3", 3) + Fade("fp_blk", "0.3", 4) + Fade("fp_red", "1.5", 5) + Fade("fp_red", "-0.5", 6) + Fade("fp_red", "0.125", 7),
            null,
            ("a factor above 1 acts as 1, below 0 as 0", "shot 5 red, shot 6 the level palette"),
            ("the target changes from the level palette, not from the red blend", "shot 4 = level + 0.3·(black − level)"),
            ("a factor below 0 extrapolates", "shot 6 brighter than the level palette")),
        Case(
            "timing",
            "FADE_PAL fp_blk 0.5 and a SCREENSHOT in the same cycle with no wait (shot 1), one WAITT 1 later (shot 2), then 0.75 and a WAIT 1 (shot 3): does the palette change at the instruction or with the next frame?",
            Fade("fp_blk", "0.5", 1, wait: "") + "    WAITT 1;\n" + Shot(2) + Fade("fp_blk", "0.75", 3, wait: "    WAIT 1;\n"),
            null,
            ("at the instruction", "shot 1 already at 0.5"),
            ("with the next frame", "shot 1 the level palette, shot 2 at 0.5")),
        Case(
            "range-half",
            "The level palette with RANGE 16, 32 (its gray ramp) made current by FADE_PAL probe_pal, 1 (shot 1), then fp_blk 0.5 and 0.25 (shots 2, 3), fp_red 0.5 (shot 4) and fp_blk 0 (shot 5): do fractional factors fade only the entries inside a shading range (round 1 faded nothing, with no RANGE in the current palette)?",
            Fade("probe_pal", "1", 1) + Fade("fp_blk", "0.5", 2) + Fade("fp_blk", "0.25", 3) + Fade("fp_red", "0.5", 4) + Fade("fp_blk", "0", 5),
            "RANGE 16, 32;",
            ("only the ranged entries fade", "shot 2: entries 16–47 halved, 2 (white) and 3 (red) unchanged"),
            ("nothing fades below 1", "shots 2–4 the level palette"),
            ("everything fades once the palette has a range", "shot 2: white 124 or 128")),
        Case(
            "range-all",
            "As range-half with RANGE 2, 254 (every colour but 0, 1 and 255): the per-entry rule over the level's sparse palette and the retail-like whole-palette range, and whether 0, 1 and 255 stay.",
            Fade("probe_pal", "1", 1) + Fade("fp_blk", "0.5", 2) + Fade("fp_blk", "0.25", 3) + Fade("fp_red", "0.5", 4) + Fade("fp_blk", "0", 5),
            "RANGE 2, 254;",
            ("entries 2–255 fade, 0 and 1 stay", "shot 4: entry 100 = (100, 14, 6) cut to the 6-bit DAC, entry 1 black, entry 255 faded"),
            ("the rounding", "shot 2: white 124 (floor of 252/2 in 6 bits) or 128")),
        Case(
            "range-auto",
            "As range-all with FLAGS AUTORANGE (the retail palettes' flags without BLUR): does AUTORANGE change which entries fade or how?",
            Fade("probe_pal", "1", 1) + Fade("fp_blk", "0.5", 2) + Fade("fp_blk", "0.25", 3) + Fade("fp_red", "0.5", 4) + Fade("fp_blk", "0", 5),
            "RANGE 2, 254; FLAGS AUTORANGE;",
            ("as range-all", "the same palettes"),
            ("AUTORANGE sorts the palette first", "the entries move")),
        Case(
            "hold",
            "For a capture of the emulated screen itself (round 1's SCREENSHOT palettes never showed a fractional fade): the level palette made current, a WAITT 64 settle, then fp_blk 0.5 held for 480 ticks, fp_red 0.5 held for 480, fp_blk 0 held for 480, each phase printed with TICKS; the window is grabbed from outside every few seconds.",
            Fade("probe_pal", "1", 1) + "    WAITT 64;\n" + V("H0", "TICKS") +
            "    FADE_PAL fp_blk, 0.5;\n    WAITT 480;\n" + V("H1", "TICKS") + "    FADE_PAL fp_red, 0.5;\n    WAITT 480;\n" + V("H2", "TICKS") +
            "    FADE_PAL fp_blk, 0;\n    WAITT 480;\n" + V("H3", "TICKS"),
            null,
            ("the screen fades although the screenshot's palette does not", "the grabs show the room half dark, then half red, then plain"),
            ("no fractional fade on screen either", "every grab the same colours")),
        new TraceProbeCase(
            "swatch",
            "For the per-entry rule from a capture of the emulated screen: the unique palette declared last (so shown at the start) and the 16x16 swatch PRBPAL.PCX as a free wall's texture, every entry on screen; a WAITT 64 settle, then fp_blk 0.5, fp_red 0.3, fp_red 0.9, fp_rmp 0.5 (a palette with every entry different as the target) and fp_blk 0, each held 320 ticks and printed with TICKS; the window is grabbed from outside every few seconds.",
            Declarations + $"PALETTE fp_uni {{ PALFILE <{UniqueFile}>; }}\n",
            "    WAITT 64;\n" + V("H0", "TICKS") +
            "    FADE_PAL fp_blk, 0.5;\n    WAITT 320;\n" + V("H1", "TICKS") + "    FADE_PAL fp_red, 0.3;\n    WAITT 320;\n" + V("H2", "TICKS") +
            "    FADE_PAL fp_red, 0.9;\n    WAITT 320;\n" + V("H3", "TICKS") + "    FADE_PAL fp_rmp, 0.5;\n    WAITT 320;\n" + V("H4", "TICKS") +
            "    FADE_PAL fp_blk, 0;\n    WAITT 320;\n" + V("H5", "TICKS") + P("RET"),
            Predict(
                ("entry = floor(current + f·(target − current)) in 6-bit DAC units", "fp_red 0.9 leaves a B of 0 at 2 (6-bit) rather than 3 (8-bit arithmetic cut afterwards)"),
                ("8-bit arithmetic, cut to 6 bits for the DAC", "fp_red 0.9 leaves a B of 0 at 3"),
                ("a factor of 0 shows the current palette again", "the last phase as the first")),
            Map: new TraceProbeMap(
                Textures: "BMAP fp_swm,<PRBPAL.PCX>;\nTEXTURE fp_swt { SCALE_X 2; SCALE_Y 2; BMAPS fp_swm; }\nWALL fp_sw { TEXTURE fp_swt; }\n",
                Walls: [new TraceProbeWall("fp_sw", 16, -8, 16, 8)])),
        Case(
            "blur",
            "The level palette with FLAGS BLUR and MOTION_BLUR 1 (shot 1), 0.5 (shot 2) and 0 (the final shot): the manual says the screenshot's size depends on MOTION_BLUR; what sizes, and does the palette change?",
            "    SET MOTION_BLUR, 1;\n    WAITT 8;\n" + Shot(1) + "    SET MOTION_BLUR, 0.5;\n    WAITT 8;\n" + Shot(2) + "    SET MOTION_BLUR, 0;\n    WAITT 8;\n",
            "RANGE 16, 32; FLAGS BLUR;",
            ("a blurred frame is grabbed at half size", "shot 1 320x240 or 320x200"),
            ("the size is the screen's", "every shot 640x480")),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "fade-pal",
        TraceFile,
        ScreenshotName,
        "Every `S<n>` line is a screenshot taken (`OVFPS<n>.PCX`) and `TICKS`; the palettes are read from the\n" +
        "screenshots against `PRBPAL.PCX` (the level's), `PRBPBLK.PCX` (black), `PRBPRED.PCX` (200, 30, 15) and\n" +
        "`PRBPRMP.PCX` (entry i = i, 255 − i, 128). Compare with `case.json` → `predictions`, check `OVCON.TXT`\n" +
        "for compile errors.",
        All);
}
