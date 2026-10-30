// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>save-format</c> probe: the files <c>SAVE</c> and <c>SAVE_INFO</c> write
/// (p.94), so OpenVirtue can read and write the original's saves. The manual says only that the name
/// is the first five letters plus a three-digit number plus <c>.SAV</c>, that <c>LOAD</c> "loads,
/// decompresses and decodes" it, that <c>SAVE_INFO</c> keeps the <c>LOCAL</c> and <c>GLOBAL</c> skills
/// (plus <c>FREEZE</c> bitmaps and <c>INKEY</c> strings) and <c>SAVE</c> the <c>GLOBAL</c> and
/// <c>PLAYER</c> ones (p.111), and that <c>SAVE_ALL</c> regions keep their walls and things.
/// <para>
/// The <c>info-*</c> and <c>full-*</c> cases differ in one value each, with distinctive raw fixed-point
/// patterns (1234.5 is 0x00134A00, 4321.25 is 0x00438500), so a byte comparison of the files finds
/// where each value lives once the file is decompressed. The <c>roundtrip-*</c> cases change the
/// values after saving, load the save and print what came back.
/// </para>
/// </summary>
internal static class SaveFormatProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVSVF0.TXT).</summary>
    public const string TraceFile = "ovsvf";

    private static string Skills(double global, double local, double player) => FormattableString.Invariant(
        $"SKILL sv_g {{ TYPE GLOBAL; VAL {global}; }}\nSKILL sv_l {{ TYPE LOCAL; VAL {local}; }}\nSKILL sv_p {{ VAL {player}; }}\n");

    private static readonly string Values = V("V", "sv_g", "sv_l", "sv_p", "PLAYER_X", "PLAYER_Y");

    private static TraceProbeCase Save(string label, string question, string skills, string instruction, int number, string before = "") => new(
        label,
        question,
        skills,
        before + Values + $"    {instruction} \"ovsav\", {number};\n" + P("SAVED") + P("RET"),
        Predict(
            ("the file is OVSAV<n>.SAV", "an OVSAV*.SAV in work/"),
            ("compressed like a WRS entry", "ovtool can decompress it with the archive's LZSS")));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Save("info-1", "SAVE_INFO with GLOBAL 1234.5, LOCAL 77, PLAYER 5: the baseline file.", Skills(1234.5, 77, 5), "SAVE_INFO", 1),
        Save("info-2", "As info-1 with GLOBAL 4321.25: which bytes change?", Skills(4321.25, 77, 5), "SAVE_INFO", 1),
        Save("info-3", "As info-1 with LOCAL 99 and PLAYER 6: is LOCAL stored, and PLAYER not?", Skills(1234.5, 99, 6), "SAVE_INFO", 1),
        Save("full-1", "SAVE with the player beamed to (5, 3): the baseline full save.", Skills(1234.5, 77, 5), "SAVE", 2,
            "    SET PLAYER_X, 5;\n    SET PLAYER_Y, 3;\n    WAIT 2;\n"),
        Save("full-2", "As full-1 with PLAYER 6 and the player at (6, 3): where are PLAYER skills and the player stored?", Skills(1234.5, 77, 6), "SAVE", 2,
            "    SET PLAYER_X, 6;\n    SET PLAYER_Y, 3;\n    WAIT 2;\n"),
        new TraceProbeCase(
            "roundtrip-info",
            "SAVE_INFO, change all three skills, LOAD_INFO: which values come back, and does the action go on after LOAD_INFO?",
            Skills(1234.5, 77, 5),
            Values + "    SAVE_INFO \"ovsav\", 3;\n    RULE sv_g = 1;\n    RULE sv_l = 1;\n    RULE sv_p = 1;\n" + Values +
            "    LOAD_INFO \"ovsav\", 3;\n" + P("LOADED") + Values + "    WAIT 1;\n" + Values + P("RET"),
            Predict(
                ("GLOBAL and LOCAL back, PLAYER not", "after LOADED: V 1234.5 77 1"),
                ("the action goes on", "LOADED and RET follow"))),
        new TraceProbeCase(
            "roundtrip-full",
            "SAVE with the player at (5, 3), change the skills and beam the player, LOAD: what comes back, and what runs after the LOAD (the saving action's continuation, IF_START, nothing)?",
            Skills(1234.5, 77, 5) + "SKILL sv_n { TYPE GLOBAL; VAL 0; }\n" +
            Action("sv_tick", "    RULE sv_n = sv_n + 1;\n    IF (sv_n < 6) {\n" + Indent(V("T", "sv_n", "sv_g", "sv_l", "sv_p", "PLAYER_X", "PLAYER_Y")) + "    }\n"),
            "    SET PLAYER_X, 5;\n    SET PLAYER_Y, 3;\n    WAIT 2;\n" + Values + "    SAVE \"ovsav\", 4;\n" + P("SAVED") +
            "    RULE sv_g = 1;\n    RULE sv_l = 1;\n    RULE sv_p = 1;\n    SET PLAYER_X, 2;\n    SET PLAYER_Y, 2;\n    WAIT 2;\n" + Values +
            "    SET EACH_TICK.1, sv_tick;\n    LOAD \"ovsav\", 4;\n" + P("LOADED") + Values + "    WAIT 4;\n" + Values + P("RET"),
            Predict(
                ("GLOBAL and PLAYER back, LOCAL not, player back at (5, 3)", "V 1234.5 1 5 5 3 after the load"),
                ("the saving action resumes after its SAVE", "a second SAVED line after the load"),
                ("the loading action goes on", "LOADED follows the LOAD"))),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "save-format",
        TraceFile,
        "ovsvs",
        "`V` lines print the GLOBAL, LOCAL and PLAYER skills and the player's position. The `info-*` and\n" +
        "`full-*` cases leave an `OVSAV*.SAV` in `work/` that differs from its sibling in one value; compare the\n" +
        "files (after decompressing them) to find where each value lives. The `roundtrip-*` cases print what a\n" +
        "load brings back and what runs after it.",
        All);
}
