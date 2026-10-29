// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;

/// <summary>
/// One line of a probe trace: the leading token (e.g. <c>C</c>, <c>BEGIN</c>) and the fields after
/// it, as a carrier's <c>PRINT_STRING</c>/<c>PRINT_VALUE</c> calls wrote them. <c>Number</c> is the
/// 1-based line number in the file, blank lines not counted.
/// </summary>
internal sealed record OracleTraceLine(int Number, string Token, IReadOnlyList<string> Fields)
{
    /// <summary>The field at <paramref name="column"/> as a number, or <c>null</c> if it is missing or not numeric.</summary>
    public double? Value(int column) =>
        column >= 0 && column < Fields.Count &&
        double.TryParse(Fields[column], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;

    public override string ToString() => Fields.Count == 0 ? Token : Token + " " + string.Join(' ', Fields);
}

/// <summary>Where the replay reads the original's per-cycle <c>TICKS</c> from: one token's column.</summary>
internal readonly record struct OracleTimingColumn(string Token, int Column)
{
    public override string ToString() => Token + ":" + Column.ToString(CultureInfo.InvariantCulture);

    /// <summary>Parses <c>TOKEN:COLUMN</c> (column 0 is the first field after the token).</summary>
    public static bool TryParse(string text, out OracleTimingColumn timing)
    {
        timing = default;
        int colon = text.LastIndexOf(':');
        if (colon <= 0 ||
            !int.TryParse(text.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int column))
        {
            return false;
        }

        timing = new OracleTimingColumn(text[..colon], column);
        return true;
    }
}

/// <summary>
/// Columns that hold <c>TICKS</c> values and the tick each run's first sampled cycle fell on. Those
/// columns are compared relative to their run's anchor, so a different start-up time shows once
/// (as the anchor offset) instead of on every line.
/// </summary>
internal sealed record OracleTickShift(IReadOnlySet<(string Token, int Column)> Columns, double OriginalAnchor, double ReplayAnchor)
{
    public bool Applies(string token, int column) => Columns.Contains((token, column));
}

/// <summary>
/// The result of comparing an original trace with a replayed one, line by line. Two lines match
/// when their tokens and field counts agree and every field agrees: numbers within the tolerance,
/// anything else as text (case-insensitive).
/// </summary>
internal sealed record OracleTraceComparison(
    int OriginalLines,
    int ReplayLines,
    int MatchingLines,
    int? FirstDivergence,
    bool TokensMatch,
    bool TokenRunsMatch)
{
    /// <summary>True when the traces have the same length and every line matches.</summary>
    public bool Identical => FirstDivergence is null;
}

/// <summary>
/// Parses the text traces oracle carriers write and compares an original run with an OpenVirtue
/// replay of the same carrier (the oracle-diff harness, <c>ovtool oracle diff</c>).
/// </summary>
internal static class OracleTrace
{
    /// <summary>The default numeric tolerance: half a unit in the third decimal that <c>PRINT_VALUE</c> writes, plus slack.</summary>
    public const double DefaultTolerance = 0.002;

    /// <summary>Splits a trace into lines of whitespace-separated fields; blank lines are skipped.</summary>
    public static IReadOnlyList<OracleTraceLine> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<OracleTraceLine>();
        foreach (string raw in text.Split('\n'))
        {
            string[] fields = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0)
            {
                continue;
            }

            lines.Add(new OracleTraceLine(lines.Count + 1, fields[0], fields[1..]));
        }

        return lines;
    }

    /// <summary>
    /// Picks the column that most likely holds <c>TICKS</c> once per cycle: among the tokens with at
    /// least three lines, a column present on every line whose values are never negative and never go
    /// down. The trace scaffold prints <c>TICKS</c> right after the token, so the first column wins
    /// over later ones; then a column with fractional values (a cycle counter is whole), then the one
    /// with more distinct values, then the token with more lines. <c>null</c> if none qualifies.
    /// </summary>
    public static OracleTimingColumn? DetectTimingColumn(IReadOnlyList<OracleTraceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        OracleTimingColumn? best = null;
        (int Column, bool Fractional, int Distinct, int Lines) bestScore = default;
        foreach (IGrouping<string, OracleTraceLine> group in lines.GroupBy(static l => l.Token, StringComparer.Ordinal))
        {
            List<OracleTraceLine> tokenLines = group.ToList();
            if (tokenLines.Count < 3)
            {
                continue;
            }

            int columns = tokenLines.Min(static l => l.Fields.Count);
            for (int column = 0; column < columns; column++)
            {
                if (!TryMonotonic(tokenLines, column, out int distinct, out bool fractional) || distinct < 3)
                {
                    continue;
                }

                (int, bool, int, int) score = (column, fractional, distinct, tokenLines.Count);
                if (best is null || Better(score, bestScore))
                {
                    best = new OracleTimingColumn(group.Key, column);
                    bestScore = score;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// The <c>TIME_CORR</c> of the cycle that printed the first sample, when the timing token also
    /// prints it: a column that, scaled by 1 or 1024, equals the step between consecutive samples
    /// on (nearly) every line. <c>null</c> when there is none.
    /// </summary>
    public static double? FirstCycleStep(IReadOnlyList<OracleTraceLine> lines, OracleTimingColumn timing)
    {
        ArgumentNullException.ThrowIfNull(lines);
        List<OracleTraceLine> tokenLines = lines
            .Where(l => string.Equals(l.Token, timing.Token, StringComparison.Ordinal) && l.Value(timing.Column) is not null)
            .ToList();
        if (tokenLines.Count < 4)
        {
            return null;
        }

        int columns = tokenLines.Min(static l => l.Fields.Count);
        foreach (double scale in new[] { 1.0, 1024.0 })
        {
            for (int column = 0; column < columns; column++)
            {
                if (column == timing.Column)
                {
                    continue;
                }

                int checks = 0;
                int hits = 0;
                for (int i = 1; i < tokenLines.Count; i++)
                {
                    double step = SnapTicks(tokenLines[i].Value(timing.Column)!.Value) - SnapTicks(tokenLines[i - 1].Value(timing.Column)!.Value);
                    if (step <= 0 || tokenLines[i].Value(column) is not { } printed)
                    {
                        continue;
                    }

                    checks++;
                    hits += Math.Abs((printed / scale) - step) <= 0.002 ? 1 : 0;
                }

                if (checks >= 3 && hits >= checks * 0.9 && tokenLines[0].Value(column) is { } first && first > 0)
                {
                    return first / scale;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A printed <c>TICKS</c> value back on the 22.10 grid, so the replay's <c>TIME_CORR</c> is the
    /// original's exactly (0.28125, not 0.282). Three decimals leave two raw values possible; the
    /// original's frames last whole 1/32 ticks (0.25, 0.28125, 1.09375), so a multiple of 1/32 wins.
    /// </summary>
    public static double SnapTicks(double printed)
    {
        long low = (long)Math.Ceiling((printed - 0.0005) * 1024);
        long high = (long)Math.Floor((printed + 0.0005) * 1024);
        for (long raw = low; raw <= high; raw++)
        {
            if (raw % 32 == 0)
            {
                return raw / 1024.0;
            }
        }

        return Math.Round(printed * 1024) / 1024;
    }

    /// <summary>
    /// The original's <c>TICKS</c> at each cycle, from a timing column. When the same token also
    /// prints a cycle counter (a whole-number column that goes up by at least one whenever
    /// <c>TICKS</c> does), a sampler that prints only every few cycles is filled in: a gap of
    /// <c>n</c> counted cycles becomes <c>n</c> equal steps.
    /// </summary>
    public static IReadOnlyList<double> CycleTicks(IReadOnlyList<OracleTraceLine> lines, OracleTimingColumn timing)
    {
        ArgumentNullException.ThrowIfNull(lines);
        List<OracleTraceLine> tokenLines = lines
            .Where(l => string.Equals(l.Token, timing.Token, StringComparison.Ordinal) && l.Value(timing.Column) is not null)
            .ToList();
        int? counter = CounterColumn(tokenLines, timing.Column);
        var ticks = new List<double>();
        double? lastCount = null;
        foreach (OracleTraceLine line in tokenLines)
        {
            double value = SnapTicks(line.Value(timing.Column)!.Value);
            if (ticks.Count > 0 && value <= ticks[^1])
            {
                continue;
            }

            if (counter is { } c && line.Value(c) is { } count && lastCount is { } previousCount && ticks.Count > 0)
            {
                int cycles = (int)Math.Round(count - previousCount);
                double start = ticks[^1];
                for (int k = 1; k < cycles; k++)
                {
                    ticks.Add(start + ((value - start) * k / cycles));
                }
            }

            ticks.Add(value);
            if (counter is { } column)
            {
                lastCount = line.Value(column);
            }
        }

        return ticks;
    }

    /// <summary>
    /// A column of <paramref name="tokenLines"/> other than the timing column that counts cycles:
    /// whole numbers that never go down and go up whenever the timing column does.
    /// </summary>
    private static int? CounterColumn(List<OracleTraceLine> tokenLines, int timingColumn)
    {
        if (tokenLines.Count < 2)
        {
            return null;
        }

        int columns = tokenLines.Min(static l => l.Fields.Count);
        for (int column = 0; column < columns; column++)
        {
            if (column == timingColumn)
            {
                continue;
            }

            bool counts = true;
            for (int i = 0; i < tokenLines.Count && counts; i++)
            {
                if (tokenLines[i].Value(column) is not { } n || n != Math.Floor(n) || n < 0)
                {
                    counts = false;
                    break;
                }

                if (i == 0)
                {
                    continue;
                }

                double dn = n - tokenLines[i - 1].Value(column)!.Value;
                double dt = tokenLines[i].Value(timingColumn)!.Value - tokenLines[i - 1].Value(timingColumn)!.Value;
                counts = dn >= 0 && (dt <= 0 || dn >= 1);
            }

            if (counts)
            {
                return column;
            }
        }

        return null;
    }

    /// <summary>
    /// The (token, column) pairs whose every value in <paramref name="lines"/> is one of the sampled
    /// cycle ticks: the columns that print <c>TICKS</c>.
    /// </summary>
    public static HashSet<(string Token, int Column)> TickColumns(IReadOnlyList<OracleTraceLine> lines, IReadOnlyList<double> cycleTicks)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(cycleTicks);
        var columns = new HashSet<(string, int)>();
        if (cycleTicks.Count == 0)
        {
            return columns;
        }

        foreach (IGrouping<string, OracleTraceLine> group in lines.GroupBy(static l => l.Token, StringComparer.Ordinal))
        {
            int count = group.Min(static l => l.Fields.Count);
            for (int column = 0; column < count; column++)
            {
                if (group.All(l => l.Value(column) is { } v && IsSample(cycleTicks, v)))
                {
                    columns.Add((group.Key, column));
                }
            }
        }

        return columns;
    }

    /// <summary>Compares an original trace with a replay, line by line in order.</summary>
    public static OracleTraceComparison Compare(
        IReadOnlyList<OracleTraceLine> original,
        IReadOnlyList<OracleTraceLine> replay,
        double tolerance = DefaultTolerance,
        OracleTickShift? shift = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replay);
        int common = Math.Min(original.Count, replay.Count);
        int matching = 0;
        int? first = null;
        for (int i = 0; i < common; i++)
        {
            if (LinesMatch(original[i], replay[i], tolerance, shift))
            {
                matching++;
            }
            else
            {
                first ??= i;
            }
        }

        if (first is null && original.Count != replay.Count)
        {
            first = common;
        }

        bool tokensMatch = original.Select(static l => l.Token).SequenceEqual(replay.Select(static l => l.Token), StringComparer.Ordinal);
        bool runsMatch = TokenRuns(original).SequenceEqual(TokenRuns(replay), StringComparer.Ordinal);
        return new OracleTraceComparison(original.Count, replay.Count, matching, first, tokensMatch, runsMatch);
    }

    /// <summary>
    /// How far the carrier template's start-up stamp (<c>TCK=</c>, <c>TICKS</c> when the probe action
    /// starts) may differ: the start-up cycles are not printed, so the replay can only estimate their
    /// <c>TIME_CORR</c>, and one cycle either way is the start-up offset, not a finding.
    /// </summary>
    public const double StartupTickTolerance = 0.3;

    /// <summary>
    /// Keys that print <c>TICKS</c> in a carrier with no per-cycle timing, so they carry the start-up
    /// offset: the template's stamp and the shoot-mapping probe's <c>TICKS</c> at the shot.
    /// </summary>
    private static readonly string[] StartupStampKeys = ["TCK", "HIN_T", "HOUT_T"];

    /// <summary>Whether two <c>KEY=value</c> tokens name the same key with numbers within the tolerance.</summary>
    private static bool KeyedValuesMatch(string original, string replay, double tolerance)
    {
        int a = original.IndexOf('=', StringComparison.Ordinal);
        int b = replay.IndexOf('=', StringComparison.Ordinal);
        if (a <= 0 || b <= 0 || !original.AsSpan(0, a).SequenceEqual(replay.AsSpan(0, b)) ||
            !double.TryParse(original.AsSpan(a + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
            !double.TryParse(replay.AsSpan(b + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
        {
            return false;
        }

        string key = original[..a];
        double allowed = StartupStampKeys.Contains(key) ? StartupTickTolerance : tolerance;
        return Math.Abs(x - y) <= allowed;
    }

    /// <summary>True when two lines carry the same token and fields, numbers within <paramref name="tolerance"/>.</summary>
    public static bool LinesMatch(OracleTraceLine original, OracleTraceLine replay, double tolerance, OracleTickShift? shift = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replay);
        if (!string.Equals(original.Token, replay.Token, StringComparison.Ordinal) &&
            !KeyedValuesMatch(original.Token, replay.Token, tolerance))
        {
            return false;
        }

        if (original.Fields.Count != replay.Fields.Count)
        {
            return false;
        }

        for (int i = 0; i < original.Fields.Count; i++)
        {
            if (original.Value(i) is { } a && replay.Value(i) is { } b)
            {
                if (shift is not null && shift.Applies(original.Token, i))
                {
                    a -= shift.OriginalAnchor;
                    b -= shift.ReplayAnchor;
                }

                if (Math.Abs(a - b) > tolerance)
                {
                    return false;
                }
            }
            else if (!string.Equals(original.Fields[i], replay.Fields[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The token sequence with consecutive repeats collapsed, so a different frame rate does not count as a divergence.</summary>
    public static IEnumerable<string> TokenRuns(IReadOnlyList<OracleTraceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        string? previous = null;
        foreach (OracleTraceLine line in lines)
        {
            if (!string.Equals(line.Token, previous, StringComparison.Ordinal))
            {
                yield return line.Token;
                previous = line.Token;
            }
        }
    }

    private static bool IsSample(IReadOnlyList<double> sorted, double value)
    {
        int lo = 0;
        int hi = sorted.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            double d = sorted[mid] - SnapTicks(value);
            if (Math.Abs(d) <= 0.0005)
            {
                return true;
            }

            if (d < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return false;
    }

    private static bool TryMonotonic(List<OracleTraceLine> lines, int column, out int distinct, out bool fractional)
    {
        distinct = 0;
        fractional = false;
        double? previous = null;
        foreach (OracleTraceLine line in lines)
        {
            if (line.Value(column) is not { } value || value < 0 || (previous is { } p && value < p))
            {
                return false;
            }

            if (previous is null || value > previous)
            {
                distinct++;
            }

            fractional |= value != Math.Floor(value);
            previous = value;
        }

        return true;
    }

    private static bool Better((int Column, bool Fractional, int Distinct, int Lines) a, (int Column, bool Fractional, int Distinct, int Lines) b)
    {
        if (a.Column != b.Column)
        {
            return a.Column < b.Column;
        }

        if (a.Fractional != b.Fractional)
        {
            return a.Fractional;
        }

        if (a.Distinct != b.Distinct)
        {
            return a.Distinct > b.Distinct;
        }

        return a.Lines > b.Lines;
    }
}
