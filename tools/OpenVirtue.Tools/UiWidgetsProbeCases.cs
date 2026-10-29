// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>ui-widgets</c> probe: what the original draws for a panel's <c>HBAR</c>,
/// <c>VBAR</c>, <c>WINDOW</c>, <c>PICTURE</c> and <c>BUTTON</c> and for the <c>MSPRITE</c> mouse pointer
/// (manual p.125–128). The manual says a bar shows its bitmap shifted by <c>fac·skill</c> pixels, a
/// window a cutout at the skills' offsets, a picture the side the skill names, a button its off
/// bitmap, and the pointer's <c>POS_X</c>/<c>POS_Y</c> is its hot spot. It does not say which way a bar
/// shifts, how fractions and overruns are handled, or which side a picture shows for 0.
/// <para>
/// The bar and window bitmaps are gradients, palette index 2 + their column (or row), so the
/// screenshot (<c>OVUWS0.PCX</c>) shows the slice drawn. The panel (index 251) is at (100, 300):
/// <c>HBAR 4,4,16</c> (64 × 6), <c>VBAR 30,4,16</c> (6 × 64), <c>WINDOW 50,4,16,8</c> on a column
/// gradient at (<c>ui_v</c>, 0), <c>WINDOW 80,4,8,16</c> on a row gradient at (0, <c>ui_v</c>),
/// <c>PICTURE 100,4</c> of three sides (indices 120–122) at <c>ui_v</c>, and <c>BUTTON 120,4</c> (off 130,
/// on 131). The pointer (index 140, hot spot (2, 3)) is at <c>MOUSE_X</c> 400, <c>MOUSE_Y</c> 200.
/// </para>
/// </summary>
internal static class UiWidgetsProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVUWT0.TXT).</summary>
    public const string TraceFile = "ovuwt";

    /// <summary>The bitmaps the generator writes: file, width, height, pixel.</summary>
    public static readonly IReadOnlyList<(string File, int Width, int Height, Func<int, int, byte> Pixel)> Bitmaps =
    [
        ("PRBUPN.PCX", 200, 60, static (x, y) => 251),
        ("PRBUHB.PCX", 62, 6, static (x, y) => (byte)(2 + x)),
        ("PRBUVB.PCX", 6, 62, static (x, y) => (byte)(2 + y)),
        ("PRBUWX.PCX", 62, 32, static (x, y) => (byte)(2 + x)),
        ("PRBUWY.PCX", 32, 62, static (x, y) => (byte)(2 + y)),
        ("PRBUP1.PCX", 8, 8, static (x, y) => 120),
        ("PRBUP2.PCX", 8, 8, static (x, y) => 121),
        ("PRBUP3.PCX", 8, 8, static (x, y) => 122),
        ("PRBUB0.PCX", 8, 8, static (x, y) => 130),
        ("PRBUB1.PCX", 8, 8, static (x, y) => 131),
        ("PRBUMS.PCX", 8, 8, static (x, y) => 140),
        ("PRBUKN.PCX", 4, 4, static (x, y) => 150),
    ];

    private const string Declarations =
        "SKILL ui_v { VAL 0; }\nSKILL ui_z { VAL 0; }\n" +
        "BMAP ui_pan,<PRBUPN.PCX>;\nBMAP ui_hb,<PRBUHB.PCX>;\nBMAP ui_vb,<PRBUVB.PCX>;\nBMAP ui_wx,<PRBUWX.PCX>;\nBMAP ui_wy,<PRBUWY.PCX>;\n" +
        "BMAP ui_p1,<PRBUP1.PCX>;\nBMAP ui_p2,<PRBUP2.PCX>;\nBMAP ui_p3,<PRBUP3.PCX>;\nBMAP ui_b0,<PRBUB0.PCX>;\nBMAP ui_b1,<PRBUB1.PCX>;\n" +
        "OVLY ui_mm,<PRBUMS.PCX>;\n" +
        "TEXTURE ui_ptex { SIDES 3; BMAPS ui_p1, ui_p2, ui_p3; }\n" +
        "OVERLAY ui_ms { OVLYS ui_mm; POS_X 2; POS_Y 3; }\n" +
        "PANEL ui_q { PAN_MAP ui_pan; POS_X 100; POS_Y 300; " +
        "HBAR 4,4,16,ui_hb,1,ui_v; VBAR 30,4,16,ui_vb,1,ui_v; WINDOW 50,4,16,8,ui_wx,ui_v,ui_z; WINDOW 80,4,8,16,ui_wy,ui_z,ui_v; " +
        "PICTURE 100,4,ui_ptex,ui_v; BUTTON 120,4,ui_b1,ui_b0,NULL,NULL; FLAGS VISIBLE, REFRESH; }\n";

    private static TraceProbeCase Show(string label, double value) => new(
        label,
        string.Create(CultureInfo.InvariantCulture, $"The widgets at ui_v {value}: which slices, which side, where the pointer?"),
        Declarations,
        string.Create(CultureInfo.InvariantCulture, $"    SET ui_v, {value};\n    SET MSPRITE, ui_ms;\n    SET MOUSE_MODE, 1;\n    SET MOUSE_X, 400;\n    SET MOUSE_Y, 200;\n    WAIT 3;\n") +
        V("V", "ui_v", "MOUSE_X", "MOUSE_Y", "MOUSE_MODE"),
        Predict(
            ("a bar shows its bitmap from fac·skill on", "HBAR columns ui_v … ui_v + 15"),
            ("the pointer's hot spot at MOUSE_X/Y", "pointer bitmap at (398, 197)")));

    /// <summary>
    /// Round 2: an <c>HSLIDER 4,20,40</c> and a <c>VSLIDER 60,4,40</c> (knob 4 × 4, index 150) on the
    /// panel at (100, 300), both on <c>ui_s</c> with <c>MIN</c> 2 and <c>MAX</c> 6 (p.127: the start
    /// position is <c>MIN</c>, the end position <c>MAX</c>). Where is the knob for a value?
    /// </summary>
    private static TraceProbeCase Slider(string label, double value) => new(
        label,
        string.Create(CultureInfo.InvariantCulture, $"Round 2. Sliders at ui_s {value} (MIN 2, MAX 6, 40 pixels): where are the knobs, and is the skill clamped?"),
        "SKILL ui_s { VAL 2; MIN 2; MAX 6; }\nBMAP ui_pan,<PRBUPN.PCX>;\nBMAP ui_kn,<PRBUKN.PCX>;\n" +
        "PANEL ui_q { PAN_MAP ui_pan; POS_X 100; POS_Y 300; HSLIDER 4,20,40,ui_kn,ui_s; VSLIDER 60,4,40,ui_kn,ui_s; FLAGS VISIBLE, REFRESH; }\n",
        string.Create(CultureInfo.InvariantCulture, $"    SET ui_s, {value};\n    WAIT 3;\n") + V("V", "ui_s"),
        Predict(
            ("the knob moves (value − MIN)/(MAX − MIN) · len", "HSLIDER knob at x 104 + 10·(ui_s − 2)"),
            ("the slider range excludes the knob", "the knob reaches 104 + 36 at MAX")));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Slider("s2", 2),
        Slider("s3", 3),
        Slider("s4.5", 4.5),
        Slider("s6", 6),
        Slider("s8", 8),
        Slider("s0", 0),
        Show("v0", 0),
        Show("v1", 1),
        Show("v3", 3),
        Show("v8.6", 8.6),
        Show("v60", 60),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "ui-widgets",
        TraceFile,
        "ovuws",
        "Read `OVUWS0.PCX`: bars and windows show palette index 2 + bitmap column/row, the picture's sides\n" +
        "120–122, the button 130 (off) / 131 (on), the pointer 140, the panel 251 at (100, 300).",
        All);
}
