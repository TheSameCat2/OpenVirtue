// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>screen-frame</c> probe: what the screen holds outside the 3-D window. Manual
/// p.125 says a panel without <c>REFRESH</c> is redrawn only when a skill it shows changes, so the
/// screen looks like one persistent picture, and every retail level does <c>SET FRAME_COLOR, 1;
/// WAIT 2; SET FRAME_COLOR, 0;</c> with the comment "clear frame to black", which reads as a paint
/// rather than a colour. The surround of Apathy's start is palette entry 1, not 0.
/// <para>
/// The panel is 64 × 24 of index 250, a font glyph c is index 64 + c, a digit-font place k index 230
/// + k, the frame colours 200 and 201 are unused by the room (ceiling 9, floor 8). Every step takes a
/// numbered <c>SCREENSHOT</c>, read as palette indices.
/// </para>
/// </summary>
internal static class ScreenFrameProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVFRT0.TXT).</summary>
    public const string TraceFile = "ovfrt";

    private const string ShotName = "ovfrs";

    private const string Resources =
        "SKILL fr_v { VAL 0; }\n" +
        "STRING fr_s,\"Ab\";\nSTRING fr_t,\"Abcdef\";\n" +
        $"FONT fr_font,<{UiLayoutProbeCases.FontFile}>,8,8;\nFONT fr_dig,<{UiLayoutProbeCases.DigitFile}>,8,8;\nBMAP fr_pan,<{UiLayoutProbeCases.PanelFile}>;\n";

    private const string Small = "    SET SCREEN_X, 32;\n    SET SCREEN_Y, 32;\n    SET SCREEN_WIDTH, 576;\n    SET SCREEN_HGT, 388;\n";

    private const string Smaller = "    SET SCREEN_X, 64;\n    SET SCREEN_Y, 64;\n    SET SCREEN_WIDTH, 512;\n    SET SCREEN_HGT, 320;\n";

    private static string Shot(int number) =>
        string.Create(CultureInfo.InvariantCulture, $"    WAITT 2;\n    SCREENSHOT \"{ShotName}\", {number};\n") +
        V(string.Create(CultureInfo.InvariantCulture, $"S{number}"), "TICKS");

    private static string Paint(int color) =>
        string.Create(CultureInfo.InvariantCulture, $"    SET FRAME_COLOR, {color};\n    WAITT 2;\n    SET FRAME_COLOR, 0;\n");

    private static string Panel(string name, int x, int y, string flags, string members = "") =>
        string.Create(CultureInfo.InvariantCulture, $"PANEL {name} {{ PAN_MAP fr_pan; POS_X {x}; POS_Y {y}; {members}FLAGS {flags}; }}\n");

    private static string Text(string name, int x, int y, string text = "fr_s") =>
        string.Create(CultureInfo.InvariantCulture, $"TEXT {name} {{ POS_X {x}; POS_Y {y}; FONT fr_font; STRING {text}; FLAGS VISIBLE; }}\n");

    private static string Overlay(string name, int x, int y) =>
        string.Create(CultureInfo.InvariantCulture, $"OVLY {name}_m,<{UiLayoutProbeCases.PanelFile}>;\nOVERLAY {name} {{ OVLYS {name}_m; POS_X {x}; POS_Y {y}; FLAGS VISIBLE; }}\n");

    private static TraceProbeCase Case(string label, string question, string declarations, string run, string? prelude = null) => new(
        label,
        question,
        Resources + declarations,
        run,
        Predict(
            ("the screen is one persistent picture", "what nothing repaints stays, whatever it was"),
            ("every frame is rebuilt", "the border is FRAME_COLOR and only visible elements show")),
        Prelude: prelude);

    /// <summary>Twelve numbered shots, one a cycle.</summary>
    private static string Steps12() =>
        string.Concat(Enumerable.Range(1, 12).Select(static i => string.Create(CultureInfo.InvariantCulture, $"    SCREENSHOT \"{ShotName}\", {i};\n") + V(string.Create(CultureInfo.InvariantCulture, $"S{i}"), "TICKS") + "    WAIT 1;\n"));

    /// <summary>Show, hide, show again, move and repaint one element named <paramref name="element"/>.</summary>
    private static string Steps(string element) =>
        Shot(1) +
        $"    SET {element}.VISIBLE, 0;\n" + Shot(2) +
        $"    SET {element}.VISIBLE, 1;\n" + Shot(3) +
        $"    SET {element}.POS_X, {element}.POS_X + 10;\n".Replace("SET", "RULE", StringComparison.Ordinal).Replace(",", " =", StringComparison.Ordinal) + Shot(4) +
        Paint(200) + Shot(5);

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Case("start-small", "The window is small before the first frame: what does the border show?", "", Shot(1), Small),
        Case("shrink", "The window shrinks in play with FRAME_COLOR 0: does the old view stay in the border?", "", Small + Shot(1)),
        Case("hold", "FRAME_COLOR 200 held: the border, and then a smaller window?", "", Small + "    SET FRAME_COLOR, 200;\n" + Shot(1) + Smaller + Shot(2)),
        Case("paint", "FRAME_COLOR 200 for two ticks then 0: does the border keep 200, and what does a smaller window leave?", "", Small + Paint(200) + Shot(1) + Smaller + Shot(2) + Paint(201) + Small + Shot(3)),
        Case("paint-same", "FRAME_COLOR 200 and 0 in one cycle: painted or not?", "", Small + "    SET FRAME_COLOR, 200;\n    SET FRAME_COLOR, 0;\n" + Shot(1)),
        Case("panel-out", "A panel outside the window, no REFRESH: hide, show, move, repaint?", Panel("fr_p", 4, 4, "VISIBLE"), Small + Paint(201) + Steps("fr_p")),
        Case("panel-out-r", "The same with REFRESH.", Panel("fr_p", 4, 4, "VISIBLE, REFRESH"), Small + Paint(201) + Steps("fr_p")),
        Case("text-out", "A text outside the window: hide, show, move, repaint, then a shorter string?", Text("fr_x", 40, 8, "fr_t"), Small + Paint(201) + Steps("fr_x") + "    SET fr_x.STRING, fr_s;\n" + Shot(6)),
        Case("overlay-out", "An overlay outside the window: hide, show, move, repaint?", Overlay("fr_o", 4, 4), Small + Paint(201) + Steps("fr_o")),
        Case("hold-panel", "FRAME_COLOR 200 held over a panel outside the window: which one is on top?", Panel("fr_p", 4, 4, "VISIBLE") + Text("fr_x", 100, 8), Small + "    SET FRAME_COLOR, 200;\n" + Shot(1) + "    SET fr_p.VISIBLE, 0;\n" + Shot(2) + "    SET fr_p.VISIBLE, 1;\n" + Shot(3)),
        Case("panel-in", "A panel inside the window, no REFRESH: drawn at all? hide, show, move?", Panel("fr_p", 300, 200, "VISIBLE"), Small + Steps("fr_p")),
        Case("panel-in-r", "The same with REFRESH.", Panel("fr_p", 300, 200, "VISIBLE, REFRESH"), Small + Steps("fr_p")),
        Case("text-in", "A text inside the window: hide, show, move?", Text("fr_x", 300, 200), Small + Steps("fr_x")),
        Case("overlay-in", "An overlay inside the window: hide, show, move?", Overlay("fr_o", 300, 200), Small + Steps("fr_o")),
        Case(
            "digits-out",
            "DIGITS on a panel outside the window, no REFRESH: 1234, then 7, then a paint, then 8: what is redrawn?",
            Panel("fr_p", 4, 4, "VISIBLE", "DIGITS 4,4,5,fr_dig,1,fr_v; "),
            Small + Paint(201) + "    SET fr_v, 1234;\n" + Shot(1) + "    SET fr_v, 7;\n" + Shot(2) + Paint(200) + Shot(3) + "    SET fr_v, 8;\n" + Shot(4)),
        Case(
            "digits-out-r",
            "The same with REFRESH.",
            Panel("fr_p", 4, 4, "VISIBLE, REFRESH", "DIGITS 4,4,5,fr_dig,1,fr_v; "),
            Small + Paint(201) + "    SET fr_v, 1234;\n" + Shot(1) + "    SET fr_v, 7;\n" + Shot(2) + Paint(200) + Shot(3) + "    SET fr_v, 8;\n" + Shot(4)),
        Case(
            "digits-in",
            "DIGITS on a panel inside the window, no REFRESH: are they drawn on the view?",
            Panel("fr_p", 300, 200, "VISIBLE", "DIGITS 4,4,5,fr_dig,1,fr_v; "),
            Small + "    SET fr_v, 1234;\n" + Shot(1) + "    WAITT 8;\n" + Shot(2) + "    SET fr_v, 7;\n    SCREENSHOT \"ovfrs\", 3;\n" + Shot(4)),
        Case(
            "text-over",
            "A text moving over a panel without REFRESH outside the window: does it leave a trail?",
            Panel("fr_p", 4, 4, "VISIBLE") + Text("fr_x", 8, 8),
            Small + Paint(201) + Shot(1) + "    SET fr_x.POS_X, 24;\n" + Shot(2) + "    SET fr_x.POS_X, 40;\n" + Shot(3) + "    SET fr_x.POS_X, 300;\n" + Shot(4)),
        Case(
            "text-over-r",
            "The same with REFRESH.",
            Panel("fr_p", 4, 4, "VISIBLE, REFRESH") + Text("fr_x", 8, 8),
            Small + Paint(201) + Shot(1) + "    SET fr_x.POS_X, 24;\n" + Shot(2) + "    SET fr_x.POS_X, 40;\n" + Shot(3) + "    SET fr_x.POS_X, 300;\n" + Shot(4)),
        Case(
            "top-row",
            "Elements on the view's first row, whole window: are they drawn there?",
            Panel("fr_p", 100, 0, "VISIBLE") + Panel("fr_q", 400, 0, "VISIBLE, REFRESH") + Text("fr_x", 200, 0) + Overlay("fr_o", 300, 0),
            Shot(1)),
        Case(
            "top-row-small",
            "Elements on and above the view's first row, small window.",
            Panel("fr_p", 100, 32, "VISIBLE, REFRESH") + Panel("fr_q", 400, 20, "VISIBLE, REFRESH") + Text("fr_x", 200, 32) + Overlay("fr_o", 300, 32),
            Small + Paint(201) + Shot(1)),
        Case(
            "pointer",
            "The mouse pointer over the border: does it leave a trail, and after MOUSE_MODE 0?",
            Overlay("fr_o", 4, 4).Replace("FLAGS VISIBLE; ", "", StringComparison.Ordinal),
            Small + Paint(201) + "    SET MSPRITE, fr_o;\n    SET MOUSE_MODE, 1;\n    SET MOUSE_X, 100;\n    SET MOUSE_Y, 10;\n" + Shot(1) +
            "    SET MOUSE_X, 200;\n" + Shot(2) + "    SET MOUSE_X, 300;\n    SET MOUSE_Y, 200;\n" + Shot(3) + "    SET MOUSE_MODE, 0;\n" + Shot(4)),

        // Round 2: frame by frame after a change, with no settling wait between the shots.
        Case("k-vis", "Round 2. A panel inside the window without REFRESH: VISIBLE off and on, a shot every cycle.", Panel("fr_p", 300, 200, "VISIBLE"), Small + "    SET fr_p.VISIBLE, 0;\n    WAITT 4;\n    SET fr_p.VISIBLE, 1;\n" + Steps12()),
        Case("k-pos", "Round 2. The same panel moved.", Panel("fr_p", 300, 200, "VISIBLE"), Small + "    WAITT 4;\n    SET fr_p.POS_X, 310;\n" + Steps12()),
        Case("k-dig", "Round 2. A digits value changed on the panel inside the window.", Panel("fr_p", 300, 200, "VISIBLE", "DIGITS 4,4,5,fr_dig,1,fr_v; "), Small + "    WAITT 4;\n    SET fr_v, 1234;\n" + Steps12()),
        Case("k-hold", "Round 2. FRAME_COLOR 200 held over a panel outside the window: VISIBLE off and on.", Panel("fr_p", 4, 4, "VISIBLE"), Small + "    SET FRAME_COLOR, 200;\n    SET fr_p.VISIBLE, 0;\n    WAITT 4;\n    SET fr_p.VISIBLE, 1;\n" + Steps12()),
        Case("k-first", "Round 2. A panel inside the window from the start, shots from the first cycles.", Panel("fr_p", 300, 200, "VISIBLE"), "", Steps12()),
        Case("k-small-dig", "Round 2. A value changed in the cycle the window shrinks (digits-in's S1).", Panel("fr_p", 300, 200, "VISIBLE", "DIGITS 4,4,5,fr_dig,1,fr_v; "), Small + "    SET fr_v, 1234;\n" + Steps12()),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "screen-frame",
        TraceFile,
        ShotName,
        "Read `OVFRS1.PCX`… as palette indices: 250 the panel, 64 + c a font glyph, 230 + k a digit-font place,\n" +
        "200 and 201 the painted frame colours. The window, when small, is (32, 32) 576 × 388.",
        All);
}
