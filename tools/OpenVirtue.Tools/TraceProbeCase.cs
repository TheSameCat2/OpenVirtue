// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Text;

/// <summary>
/// One case of a trace probe: a synthetic carrier whose actions print short tokens to one
/// <c>PRINTFILE</c> (appended by every <c>PRINT_STRING</c>/<c>PRINT_VALUE</c>,
/// OV-20261009-shoot-mapping), so the token order shows which statements ran and when.
/// <c>Declarations</c> go before <c>probe_start</c>; <c>Run</c> is inserted into <c>probe_start</c>
/// after the trace file is selected and <c>BEGIN</c> written. <c>Predictions</c> map each reading
/// under test to the trace it would produce, written down before the run. <c>Autotype</c>, when
/// set, is the argument list of a DOSBox-X <c>AUTOTYPE</c> line run before the runtime (scripted
/// key presses, e.g. <c>-w 15 space</c>).
/// </summary>
internal sealed record TraceProbeCase(
    string Label,
    string Question,
    string Declarations,
    string Run,
    IReadOnlyDictionary<string, string> Predictions,
    string? Autotype = null);

/// <summary>
/// A trace probe: its name, the <c>PRINTFILE</c> base name of its trace (written as
/// <c>&lt;NAME&gt;0.TXT</c>), the screenshot base name, a README paragraph, and one carrier per case.
/// </summary>
internal sealed record TraceProbeSet(
    string ProbeName,
    string TraceFile,
    string ScreenshotName,
    string Intro,
    IReadOnlyList<TraceProbeCase> Cases)
{
    /// <summary>The trace file name as DOS writes it.</summary>
    public string TraceFileName => TraceFile.ToUpperInvariant() + "0.TXT";
}

/// <summary>WDL snippets shared by the trace probe case tables.</summary>
internal static class TraceProbeWdl
{
    /// <summary>The trace instruction for one token: <c>PRINT_STRING "TOKEN\n";</c> (labels stay ≤ 12 characters).</summary>
    public static string P(string token, int indent = 4) =>
        new string(' ', indent) + $"PRINT_STRING \"{token}\\n\";\n";

    /// <summary>
    /// A token followed by the current values of some skills on one line, e.g. <c>S 32.533 2.000</c>
    /// (<c>PRINT_VALUE</c> prints three decimals).
    /// </summary>
    public static string V(string token, params string[] skills)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"    PRINT_STRING \"{token}\";\n");
        foreach (string skill in skills)
        {
            sb.Append(CultureInfo.InvariantCulture, $"    PRINT_STRING \" \";\n    PRINT_VALUE {skill};\n");
        }

        sb.Append("    PRINT_STRING \"\\n\";\n");
        return sb.ToString();
    }

    public static string Action(string name, string body) =>
        new StringBuilder().Append("ACTION ").Append(name).Append("\n{\n").Append(body).Append("}\n").ToString();

    public static Dictionary<string, string> Predict(params (string Reading, string Trace)[] predictions) =>
        predictions.ToDictionary(static p => p.Reading, static p => p.Trace);
}
