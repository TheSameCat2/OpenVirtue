// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>numeric-edge</c> probe: how the original stores and computes skill values.
/// The manual (p.76) lists the operators and functions and says <c>%</c>, <c>|</c>, <c>^</c> and
/// <c>&amp;</c> are "for integer values only", but not the number format, the precision, the range,
/// how negative operands round, or what a division by zero or a function outside its domain does.
/// <c>MY.DISTANCE</c> reads 65535.999 for a thing far away (OV-20261019-actor-contact), which hints
/// at a fixed-point format.
/// <para>
/// <c>PRINT_VALUE</c> writes three decimals, so precision is read from scaled values: <c>1/3</c> is
/// printed as itself, times 1024 and times 65536. Every line is a token followed by the values. The
/// risky operations each get their own case, with a <c>B</c> line before and an <c>A</c> line after,
/// so an abort shows as a missing <c>A</c> (and the caller's <c>C</c> line shows whether the abort
/// also stops the caller).
/// </para>
/// </summary>
internal static class NumericEdgeProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVNUM0.TXT).</summary>
    public const string TraceFile = "ovnum";

    private const string Skills =
        "SKILL ne_a { VAL 0; }\nSKILL ne_b { VAL 0; }\nSKILL ne_c { VAL 0; }\nSKILL ne_d { VAL 0; }\nSKILL ne_e { VAL 0; }\n";

    /// <summary>Assigns <paramref name="expression"/> to <c>ne_a</c> and prints it under <paramref name="token"/>.</summary>
    private static string R(string token, string expression) => $"    RULE ne_a = {expression};\n" + V(token, "ne_a");

    /// <summary>
    /// The precision probes: <c>1/3</c> unscaled and scaled, a small constant, a large constant,
    /// sums past 32767, 65535 and 2^21, and products that would overflow narrow formats.
    /// </summary>
    private static readonly string ValuesRun =
        "    RULE ne_b = 1;\n    RULE ne_c = 3;\n    RULE ne_a = ne_b / ne_c;\n" + V("THIRD", "ne_a") +
        "    RULE ne_d = ne_a * 1024;\n    RULE ne_e = ne_a * 65536;\n" + V("THIRDX", "ne_d", "ne_e") +
        "    RULE ne_a = 0.001;\n    RULE ne_d = ne_a * 1000000;\n" + V("MILLI", "ne_a", "ne_d") +
        "    RULE ne_a = 0.0001;\n    RULE ne_d = ne_a * 1000000;\n" + V("TENTH", "ne_a", "ne_d") +
        R("BIG1", "32767 + 2") + R("BIG2", "65535 + 2") + R("BIG3", "2097151 + 2") + R("BIG4", "1000000 * 1000") +
        R("NEG1", "0 - 32769") + R("NEG2", "0 - 2097153") +
        R("DIV1", "7 / 2") + R("DIV2", "(0 - 7) / 2") +
        R("INT1", "INT(2.7)") + R("INT2", "INT(0 - 2.7)") + R("INT3", "INT(0 - 2.5)") +
        R("MOD1", "7 % 3") + R("MOD2", "(0 - 7) % 3") + R("MOD3", "7 % (0 - 3)") + R("MOD4", "7.9 % 3") + R("MOD5", "7 % 2.5") +
        R("AND1", "5.7 & 3") + R("AND2", "(0 - 1) & 255") + R("OR1", "4 | 2.9") + R("XOR1", "6 ^ 3") +
        R("SGN0", "SIGN(0)") + R("TAN1", "TAN(1.5707)") + R("EXP1", "EXP(20)") +
        R("SIN1", "SIN(0.5) * 1000000") + R("SQR2", "SQRT(2) * 1000000") +
        P("RET");

    /// <summary>Comparisons inside IF conditions: equality of fractions, tiny values, and the truth of a bare value.</summary>
    private static readonly string LogicRun =
        "    RULE ne_b = 0.1 * 3;\n" +
        "    RULE ne_a = 0;\n    IF (ne_b == 0.3) { RULE ne_a = 1; }\n" + V("EQ1", "ne_a", "ne_b") +
        "    RULE ne_b = 0.0001;\n" +
        "    RULE ne_a = 0;\n    IF (ne_b > 0) { RULE ne_a = 1; }\n" + V("GT1", "ne_a") +
        "    RULE ne_a = 0;\n    IF (ne_b) { RULE ne_a = 1; }\n" + V("TRUE1", "ne_a") +
        "    RULE ne_b = 0.0005;\n" +
        "    RULE ne_a = 0;\n    IF (ne_b > 0) { RULE ne_a = 1; }\n" + V("GT2", "ne_a") +
        "    RULE ne_b = 0 - 1;\n" +
        "    RULE ne_a = 0;\n    IF (ne_b) { RULE ne_a = 1; }\n" + V("TRUE3", "ne_a") +
        P("RET");

    /// <summary>A case for one operation that may abort: a sub-action prints B, the operation's result, A; the caller prints C after the CALL.</summary>
    private static TraceProbeCase Risky(string label, string expression, string question) => new(
        label,
        question,
        Skills + Action("ne_sub", "    RULE ne_a = 12345;\n" + V("B", "ne_a") + $"    RULE ne_a = {expression};\n" + V("A", "ne_a")),
        "    CALL ne_sub;\n" + V("C", "ne_a") + P("RET"),
        Predict(
            ("a value, the action goes on", "B 12345, A <value>, C <value>, RET"),
            ("the sub-action stops, the caller goes on", "B 12345, C ..., RET (no A)"),
            ("the whole chain stops", "B 12345 only (and no OVEND0.TXT if probe_start stopped)"),
            ("a run-time error message", "an Error line in OVCON.TXT")));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "values",
            "Number format and arithmetic: precision of 1/3, 0.001 and 0.0001, range past 32767/65535/2^21, rounding of negative division, INT, %, bitwise operators on fractions and negatives, SIGN(0), near-singular TAN and EXP overflow.",
            Skills,
            ValuesRun,
            Predict(
                ("IEEE double", "THIRD 0.333, THIRDX 341.333 21845.333, MILLI 0.001 1000.000, BIG* exact"),
                ("22.10 fixed point", "THIRDX 341.000 21824.000 (or 21845 if rounded), MILLI 0.001 1024/1000 steps, BIG3 wraps"),
                ("16.16 fixed point", "THIRDX 341.328 21845.000, BIG2 wraps or saturates"),
                ("C truncation", "DIV2 -3.500, INT2 -2.000, MOD2 -1.000"))),
        new TraceProbeCase(
            "logic",
            "IF conditions: is 0.1*3 == 0.3, are 0.0001 and 0.0005 above 0 (below the format's resolution?), and is a bare non-zero skill true (also a negative one)?",
            Skills,
            LogicRun,
            Predict(
                ("double precision", "EQ1 0 or 1 (IEEE says 0), GT1 1, TRUE1 1, GT2 1, TRUE3 1"),
                ("fixed point with 1/1024 steps", "GT1 0 (0.0001 rounds to 0), TRUE1 0, GT2 0 or 1, EQ1 1"))),
                Risky("div-zero", "1 / 0", "What does 1/0 give: a value (0, the maximum), an abort of the action, or an error?"),
        Risky("mod-zero", "7 % 0", "What does 7 % 0 give: a value, an abort, or an error?"),
        Risky("sqrt-neg", "SQRT(0 - 1)", "What does SQRT(-1) give: a value, an abort, or an error?"),
        Risky("log-zero", "LOG(0)", "What does LOG(0) give: a value, an abort, or an error?"),
        Risky("asin-two", "ASIN(2)", "What does ASIN(2) give: a value, an abort, or an error?"),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "numeric-edge",
        TraceFile,
        "ovnus",
        "Every line is a token followed by the values it reports (three decimals, as `PRINT_VALUE` writes).\n" +
        "`values` lists the precision, range and rounding readings; the other cases each run one operation that\n" +
        "may abort, with `B` before it and `A` after it in a `CALL`ed action and `C` in the caller. Check\n" +
        "`OVCON.TXT` for run-time error messages.",
        All);
}
