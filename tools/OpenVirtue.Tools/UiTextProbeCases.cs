// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>ui-text</c> probe: how the original lays out a <c>TEXT</c> of several lines
/// (manual p.129–131): <c>STRINGS</c> with several <c>STRING</c>s, a "\n" inside a string, <c>OFFSET_Y</c>
/// (the first pixel line shown at <c>POS_Y</c>), <c>SIZE_Y</c> (the shown height) and <c>CENTER_X</c>/<c>CENTER_Y</c>
/// over lines of different lengths. The font is the ui-layout font (<see cref="UiLayoutProbeCases"/>):
/// character c in palette index 64 + c, 8 × 8; the text is at (300, 200). Read <c>OVUTS0.PCX</c>.
/// </summary>
internal static class UiTextProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVUTT0.TXT).</summary>
    public const string TraceFile = "ovutt";

    private static TraceProbeCase Text(string label, string question, string strings, string text) => new(
        label,
        question,
        $"FONT ui_font,<{UiLayoutProbeCases.FontFile}>,8,8;\n" + strings + text + "\n",
        "    WAIT 3;\n" + P("SHOWN"),
        Predict(("lines 8 pixels apart", "line k at y 200 + 8k")));

    private const string Three = "STRING ut_a,\"AB\";\nSTRING ut_b,\"CDE\";\nSTRING ut_c,\"F\";\n";

    private static string Block(string extra, string flags = "") =>
        "TEXT ut_t { POS_X 300; POS_Y 200; " + extra + "STRINGS 3; FONT ui_font; STRING ut_a, ut_b, ut_c; FLAGS VISIBLE" + flags + "; }";

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Text("lines", "Three strings AB, CDE, F: one line each, 8 pixels apart?", Three, Block("")),
        Text("newline", "One string \"AB\\nCD\": does \\n break the line?", "STRING ut_a,\"AB\\nCD\";\n",
            "TEXT ut_t { POS_X 300; POS_Y 200; FONT ui_font; STRING ut_a; FLAGS VISIBLE; }"),
        Text("offset", "OFFSET_Y 3: the first 3 pixel lines hidden, the block 3 pixels up?", Three, Block("OFFSET_Y 3; ")),
        Text("sizey", "SIZE_Y 12: only y 200…211 shown?", Three, Block("SIZE_Y 12; ")),
        Text("offset-sizey", "OFFSET_Y 3 and SIZE_Y 12 together.", Three, Block("SIZE_Y 12; OFFSET_Y 3; ")),
        Text("center", "CENTER_X and CENTER_Y over lines of 2, 3 and 1 characters: each line centred, the block centred on 200?", Three, Block("", ", CENTER_X, CENTER_Y")),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "ui-text",
        TraceFile,
        "ovuts",
        "Read `OVUTS0.PCX`: palette index 64 + c is character c; the text is at (300, 200).",
        All);
}
