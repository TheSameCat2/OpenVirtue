// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>ui-layout</c> probe: where the original draws a panel's <c>DIGITS</c> and a
/// <c>TEXT</c> (manual p.126–131). The manual gives the panel and text positions; it does not say how
/// <c>DIGITS</c> places a number in its <c>len</c> places (right-aligned? leading blanks? the minus?),
/// what it shows for a number too long or with a fraction, or how <c>CENTER_X</c>, <c>CENTER_Y</c> and
/// <c>CONDENSED</c> move the characters.
/// <para>
/// Every glyph of the 128-character font <see cref="FontFile"/> (8 × 8) is filled with palette index
/// 64 + its code, every glyph of the 11-character digit font <see cref="DigitFile"/> with 230 + its
/// place (0–9, then space), and the panel bitmap with 250, so the screenshot (<c>OVULS0.PCX</c>) shows
/// which character lies where. The panel at (100, 60) has <c>DIGITS 4,4,5</c> in the digit font and
/// <c>DIGITS 4,14,5</c> in the text font, both showing <c>ui_v</c>; the text at (300, 200) shows
/// <c>ui_s</c>.
/// </para>
/// </summary>
internal static class UiLayoutProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVULT0.TXT).</summary>
    public const string TraceFile = "ovult";

    public const string FontFile = "PRBFNT.PCX";

    public const string DigitFile = "PRBDIG.PCX";

    public const string PanelFile = "PRBPAN.PCX";

    /// <summary>The 128-character font: glyph c (16 a row) in palette index 64 + c.</summary>
    public static byte FontPixel(int x, int y) => (byte)(64 + ((y / 8) * 16) + (x / 8));

    /// <summary>The digit font: 0–9 and space in palette index 230 + place.</summary>
    public static byte DigitPixel(int x, int y) => (byte)(230 + (x / 8));

    private static string Declarations(string text, string textFlags) =>
        "SKILL ui_v { VAL 0; }\n" +
        $"STRING ui_s,\"{text}\";\n" +
        $"FONT ui_font,<{FontFile}>,8,8;\nFONT ui_dig,<{DigitFile}>,8,8;\nBMAP ui_pan,<{PanelFile}>;\n" +
        "PANEL ui_p { PAN_MAP ui_pan; POS_X 100; POS_Y 60; DIGITS 4,4,5,ui_dig,1,ui_v; DIGITS 4,14,5,ui_font,1,ui_v; FLAGS VISIBLE, REFRESH; }\n" +
        "TEXT ui_t { POS_X 300; POS_Y 200; FONT ui_font; STRING ui_s; FLAGS VISIBLE" + textFlags + "; }\n";

    private static TraceProbeCase Show(string label, double value, string text = "Ab -9", string textFlags = "") => new(
        label,
        string.Create(CultureInfo.InvariantCulture, $"DIGITS showing {value}; TEXT \"{text}\"{(textFlags.Length > 0 ? " with" + textFlags : "")}: where are the glyphs?"),
        Declarations(text, textFlags),
        string.Create(CultureInfo.InvariantCulture, $"    SET ui_v, {value};\n    WAIT 3;\n") + V("V", "ui_v"),
        Predict(
            ("right-aligned in len places, leading blanks", "the digits end at 4 + 5·8 in the panel"),
            ("left-aligned", "the first digit at 4")),
        Map: null);

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Show("v7", 7),
        Show("v1234", 1234),
        Show("v-56", -56),
        Show("v3.7", 3.7),
        Show("v123456", 123456),
        Show("v0", 0),
        Show("center", 7, "Ab", ", CENTER_X, CENTER_Y"),
        Show("condensed", 7, "AAAA", ", CONDENSED"),
        Show("narrow", 7, "AAAA", ", NARROW"),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "ui-layout",
        TraceFile,
        "ovuls",
        "Read `OVULS0.PCX`: palette index 64 + c is font character c, 230 + k digit-font place k (10 = space),\n" +
        "250 the panel bitmap. The panel is at (100, 60), the text at (300, 200).",
        All);
}
