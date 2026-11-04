// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>strings</c> probe: the string instructions the retail scripts use for their
/// panels and winner screen — <c>TO_STRING</c>'s digit representation of a skill, <c>SET_STRING</c>
/// and <c>ADD_STRING</c> against a string's original length, and <c>SET_SKILL</c>'s reading of a
/// string (manual pp. 85–86). Every line is a token and the string's content between the token and
/// a <c>|</c>, so trailing spaces show.
/// </summary>
internal static class StringProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVSTR0.TXT).</summary>
    public const string TraceFile = "ovstr";

    private const string Skills = "SKILL st_v { VAL 0; }\n";

    private static string Show(string token, string text) =>
        $"    PRINT_STRING \"{token} \";\n    PRINT_STRING {text};\n    PRINT_STRING \"|\\n\";\n";

    private static readonly double[] FormatValues = [0, 1, 1.5, -2.25, 1234.5678, 0.001, 65535.999, 100000, -0.5, 12, 0.333, 7.999, 1000000, -1000.125, 3.14159, 0.5, 2.5, -1];

    private static readonly string FormatDeclarations =
        Skills + "STRING st_n,\"              \";\nSTRING st_s,\"xx\";\n";

    private static readonly string FormatRun =
        string.Concat(FormatValues.Select((v, i) =>
            $"    RULE st_v = {v.ToString(CultureInfo.InvariantCulture)};\n    TO_STRING st_n, st_v;\n" + Show("F" + i.ToString(CultureInfo.InvariantCulture), "st_n"))) +
        "    TO_STRING st_n, 2.5;\n" + Show("FL", "st_n") +
        "    TO_STRING st_n, TICKS;\n" + Show("FT", "st_n") +
        "    RULE st_v = 1234.5;\n    TO_STRING st_s, st_v;\n" + Show("FS", "st_s") +
        "    RULE st_v = 7;\n    TO_STRING st_s, st_v;\n" + Show("FS7", "st_s") + P("RET");

    private static readonly string ConcatDeclarations =
        Skills + "STRING st_a,\"0123456789\";\nSTRING st_b,\"AB\";\nSTRING st_c,\"CDEFGH\";\nSTRING st_e,\"\";\nSTRING st_z,\"Z\";\n";

    private static readonly string ConcatRun =
        Show("A0", "st_a") + Show("B0", "st_b") + Show("E0", "st_e") +
        "    SET_STRING st_b, st_c;\n" + Show("B1", "st_b") +
        "    SET_STRING st_b, st_e;\n" + Show("B2", "st_b") +
        "    ADD_STRING st_b, st_c;\n" + Show("B3", "st_b") +
        "    SET_STRING st_a, st_b;\n" + Show("A1", "st_a") +
        "    ADD_STRING st_a, st_c;\n" + Show("A2", "st_a") +
        "    ADD_STRING st_a, st_c;\n" + Show("A3", "st_a") +
        "    ADD_STRING st_a, st_z;\n" + Show("A4", "st_a") +
        "    SET_STRING st_e, st_z;\n" + Show("E1", "st_e") +
        "    ADD_STRING st_e, st_z;\n" + Show("E2", "st_e") +
        "    SET_STRING st_z, st_e;\n" + Show("Z1", "st_z") + P("RET");

    private static readonly string[] ParseTexts = ["12.5", "abc", " 7", "1e3", "-3.25", "0x10", "12abc", "", "  ", "+4", ".5", "1,5", "65536", "3.14159265", "7.", "-"];

    private static readonly string ParseDeclarations =
        Skills + string.Concat(ParseTexts.Select((t, i) => $"STRING st_p{i.ToString(CultureInfo.InvariantCulture)},\"{t}\";\n"));

    private static readonly string ParseRun =
        string.Concat(ParseTexts.Select((t, i) =>
            $"    RULE st_v = 99;\n    SET_SKILL st_v, st_p{i.ToString(CultureInfo.InvariantCulture)};\n" + V("P" + i.ToString(CultureInfo.InvariantCulture), "st_v"))) + P("RET");

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "format",
            "TO_STRING into a 14-character string of 0, 1, 1.5, −2.25, 1234.5678, 0.001, 65535.999, 100000, −0.5, 12, 0.333, 7.999, 1000000, −1000.125, 3.14159, 0.5, 2.5 and −1, of a literal and of TICKS, and into a 2-character string: how many decimals, is the rest of the string kept, padded or cut, and what of a value longer than the string?",
            FormatDeclarations,
            FormatRun,
            Predict(
                ("three decimals as PRINT_VALUE, the string cut to the digits", "F2 1.500|, F0 0.000|"),
                ("integers without decimals", "F0 0|, F2 1.5|"),
                ("the string keeps its length, padded with spaces", "F0 0.000          |"),
                ("a value longer than the string is cut to its length", "FS 12|")),
            Map: new TraceProbeMap()),
        new TraceProbeCase(
            "concat",
            "SET_STRING and ADD_STRING against the strings' original lengths (10, 2, 6, 0, 1): is a longer source cut, does ADD_STRING stop at the length, can an empty string take anything?",
            ConcatDeclarations,
            ConcatRun,
            Predict(
                ("cut to the original length (manual p.86)", "B1 CD|, B3 CD|, A2 CDCDEFGH|, A3 CDCDEFGHCD|, E1 |"),
                ("ADD_STRING past the length does nothing", "A3 CDCDEFGH|"),
                ("the length grows", "B1 CDEFGH|")),
            Map: new TraceProbeMap()),
        new TraceProbeCase(
            "parse",
            "SET_SKILL from \"12.5\", \"abc\", \" 7\", \"1e3\", \"−3.25\", \"0x10\", \"12abc\", \"\", \"  \", \"+4\", \".5\", \"1,5\", \"65536\", \"3.14159265\", \"7.\" and \"-\", the skill at 99 before each: which parse, to what, and what does a non-number leave?",
            ParseDeclarations,
            ParseRun,
            Predict(
                ("a leading number parses, the rest leaves 0", "P0 12.500, P1 0.000, P2 7.000, P6 12.000"),
                ("a non-number leaves the skill as it was", "P1 99.000"),
                ("22.10 wraps at 65536", "P12 0.000 or 65535.999")),
            Map: new TraceProbeMap()),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "strings",
        TraceFile,
        "ovsts",
        "Every line is a token, then the string's content up to a `|` (format, concat) or the skill's value\n" +
        "(parse). Compare with `case.json` → `predictions`, check `OVCON.TXT` for compile errors.",
        All);
}
