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
    /// <summary>The token that stands for every line: a trace whose lines all print <c>TICKS</c> and one cycle counter.</summary>
    public const string AnyToken = "*";

    public override string ToString() => Token + ":" + Column.ToString(CultureInfo.InvariantCulture);

    /// <summary>Whether <paramref name="line"/> is one of this column's lines and has a number there.</summary>
    public bool Matches(OracleTraceLine line) =>
        (Token == AnyToken || string.Equals(line.Token, Token, StringComparison.Ordinal)) && line.Value(Column) is not null;

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
/// A fitted run of frames on the 1/32-tick grid: <c>k</c> frames after the fit's origin, <c>TICKS</c>
/// is <c>floor(Phase + k·Rate)</c> thirty-seconds of a tick past the origin's (0 ≤ <c>Phase</c> &lt; 1).
/// </summary>
internal sealed record OracleCadence(double Rate, double Phase)
{
    /// <summary>The whole thirty-seconds of a tick from the origin to frame <paramref name="k"/>.</summary>
    public long Units(long k) => (long)Math.Floor(Phase + (k * Rate));

    /// <summary>The step into frame <paramref name="k"/> after the origin, in ticks.</summary>
    public double StepInto(long k) => (Units(k) - Units(k - 1)) / 32.0;
}

/// <summary>
/// Collects (frame, units) points one at a time while some <see cref="OracleCadence"/> passes through
/// all of them: for a rate R the phases that fit form [max(U − kR), min(U + 1 − kR)), which is open
/// exactly when U_b − U_a − 1 &lt; (k_b − k_a)·R &lt; U_b − U_a + 1 for every pair, so the rates that
/// fit are one interval, narrowed by each point.
/// </summary>
internal sealed class OracleCadenceFitter
{
    private readonly Dictionary<long, long> _points = [];
    private double _low = double.NegativeInfinity;
    private double _high = double.PositiveInfinity;

    /// <summary>Adds a point unless no cadence would then pass through every point; says which.</summary>
    public bool TryAdd(long k, long units)
    {
        if (_points.TryGetValue(k, out long seen))
        {
            return seen == units;
        }

        double low = _low;
        double high = _high;
        foreach ((long pk, long pu) in _points)
        {
            (long dk, long du) = k > pk ? (k - pk, units - pu) : (pk - k, pu - units);
            low = Math.Max(low, (du - 1) / (double)dk);
            high = Math.Min(high, (du + 1) / (double)dk);
        }

        if (low >= high - 1e-12)
        {
            return false;
        }

        (_low, _high) = (low, high);
        _points[k] = units;
        return true;
    }

    /// <summary>The cadence at the centre of the rates and then the phases that fit, origin at frame 0 (which must be a point at 0 units).</summary>
    public OracleCadence? Fit()
    {
        if (_points.Count < 2 || !_points.TryGetValue(0, out long origin) || origin != 0)
        {
            return null;
        }

        double rate = (_low + _high) / 2;
        double low = 0;
        double high = 1;
        foreach ((long k, long units) in _points)
        {
            low = Math.Max(low, units - (k * rate));
            high = Math.Min(high, units + 1 - (k * rate));
        }

        return low < high - 1e-9 ? new OracleCadence(rate, (low + high) / 2) : null;
    }
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
    /// A counted token that fills less than a quarter of the cycles another counted token spans
    /// gives way to the one counting from the earliest cycle: border-climb's <c>N</c> lines print
    /// only while the player pushes (197 lines in cycles 120…2293), its <c>TK</c> sampler every 62
    /// cycles from cycle 1. (wall-pass-clip's <c>W</c>, from cycle 7, is a wall's event, out of phase.)
    /// </summary>
    public static OracleTimingColumn? DetectTimingColumn(IReadOnlyList<OracleTraceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        OracleTimingColumn? best = null;
        (int Column, bool Fractional, int Distinct, int Lines) bestScore = default;
        var spans = new Dictionary<OracleTimingColumn, (int Lines, double First, double Span)>();
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

                var candidate = new OracleTimingColumn(group.Key, column);
                if (CounterColumn(tokenLines, column) is { } counter)
                {
                    double first = tokenLines[0].Value(counter)!.Value;
                    spans[candidate] = (tokenLines.Count, first, tokenLines[^1].Value(counter)!.Value - first + 1);
                }

                (int, bool, int, int) score = (column, fractional, distinct, tokenLines.Count);
                if (best is null || Better(score, bestScore))
                {
                    best = candidate;
                    bestScore = score;
                }
            }
        }

        if (best is { } chosen && spans.TryGetValue(chosen, out (int Lines, double First, double Span) own))
        {
            foreach ((OracleTimingColumn other, (int _, double first, double span)) in spans)
            {
                (int _, double bestFirst, double bestSpan) = spans[best.Value];
                if (other.Column == chosen.Column && span > own.Span && own.Lines < span / 4 &&
                    (first < bestFirst || (first == bestFirst && span > bestSpan)))
                {
                    best = other;
                }
            }
        }

        return best ?? AnyTokenTiming(lines);
    }

    /// <summary>
    /// For a trace where no token repeats as a sampler: every line counts as a sample when every line
    /// prints <c>TICKS</c> first and a cycle counter second, both never going down, at least three
    /// distinct cycles, and each counted cycle with one <c>TICKS</c> only. The last check keeps out
    /// traces that print from both sides of the counting action, whose counters are a cycle apart.
    /// </summary>
    private static OracleTimingColumn? AnyTokenTiming(IReadOnlyList<OracleTraceLine> lines)
    {
        List<OracleTraceLine> numbered = [.. lines.Where(static l => l.Value(0) is not null)];
        if (numbered.Count < 3 || numbered.Count < lines.Count(static l => l.Fields.Count > 0) ||
            numbered.Any(static l => l.Value(1) is not { } n || n != Math.Floor(n) || n < 0))
        {
            return null;
        }

        string? sampler = LeadingSampler(numbered, 0, 1);
        var ticksByCount = new Dictionary<double, double>();
        for (int i = 0; i < numbered.Count; i++)
        {
            double ticks = numbered[i].Value(0)!.Value;
            double count = CycleOf(numbered[i], 1, sampler);
            if (i > 0 && (ticks < numbered[i - 1].Value(0)!.Value || count < CycleOf(numbered[i - 1], 1, sampler)))
            {
                return null;
            }

            if (ticksByCount.TryGetValue(count, out double seen) ? Math.Abs(seen - ticks) > 1e-9 : false)
            {
                return null;
            }

            ticksByCount[count] = ticks;
        }

        // Three printed counts, even where the phase shift folds two of them into one cycle.
        return numbered.Select(static l => l.Value(1)!.Value).Distinct().Count() >= 3
            ? new OracleTimingColumn(OracleTimingColumn.AnyToken, 0)
            : null;
    }

    /// <summary>
    /// The <c>TIME_CORR</c> of the cycle that printed the first sample, when the timing token also
    /// prints it: a column that, scaled by 1 or 1024, equals the step between consecutive samples
    /// on (nearly) every line, or else whose values are all whole 1/32 ticks between 0.1 and 2, or else
    /// a counted sampler's <see cref="TimeCorrectionColumn"/>. <c>null</c> when there is none.
    /// </summary>
    public static double? FirstCycleStep(IReadOnlyList<OracleTraceLine> lines, OracleTimingColumn timing)
    {
        ArgumentNullException.ThrowIfNull(lines);
        List<OracleTraceLine> tokenLines = lines
            .Where(timing.Matches)
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

        // A sampler that prints only every so many cycles never shows one step between two samples;
        // its TIME_CORR column is then the one whose every value is a whole number of 1/32 ticks
        // between 0.1 and 2 (the flic-scheduler-detail TK lines print 0.25, 1.094, 0.281 …) and whose
        // first value is a 60 Hz start-up step, 0.25 or 0.281: other columns of that shape (sizes,
        // heights) start elsewhere.
        for (int column = 0; column < columns; column++)
        {
            if (column != timing.Column &&
                tokenLines[0].Value(column) is { } start && (Math.Abs(start - 0.25) <= 0.0015 || Math.Abs(start - 0.28125) <= 0.0015) &&
                tokenLines.All(l => l.Value(column) is { } v && v is > 0.1 and <= 2 && Math.Abs((v * 32) - Math.Round(v * 32)) <= 0.05) &&
                tokenLines.Select(l => l.Value(column)!.Value).Distinct().Count() > 1)
            {
                return tokenLines[0].Value(column);
            }
        }

        // A counted sampler's TIME_CORR column, checked where two samples are a cycle apart.
        return CounterColumn(tokenLines, timing.Column) is { } counter && TimeCorrectionColumn(tokenLines, timing.Column, counter) is { } counted
            ? tokenLines[0].Value(counted.Column) / counted.Scale
            : null;
    }

    /// <summary>
    /// The <c>TIME_CORR</c> of the first sampled cycle when no column prints it: DOSBox-X's 60 Hz frames
    /// against the 16 Hz tick give a step pattern (0.25 or 0.28125) that repeats every 18 cycles once
    /// start-up is over (the scheduler-order probe's <c>startup</c> trace prints it), so the first
    /// cycle's step is the step 18 cycles later. Used only when the steps 18 and 36 cycles later agree
    /// and the first sampled step is itself a 60 Hz frame's.
    /// </summary>
    public static double? PeriodicFirstStep(IReadOnlyList<double> cycleTicks)
    {
        ArgumentNullException.ThrowIfNull(cycleTicks);
        const int Period = 18;
        if (cycleTicks.Count <= (2 * Period) + 1)
        {
            return null;
        }

        double Step(int cycle) => SnapTicks(cycleTicks[cycle]) - SnapTicks(cycleTicks[cycle - 1]);

        // A start-up that ran slower (8-tick frames before the run sped up: view-activity near-low,
        // occlude-edge) is not in the pattern of the later frames.
        if (!IsFrameStep(Step(1)))
        {
            return null;
        }

        double first = Step(Period);
        return IsFrameStep(first) && Math.Abs(first - Step(2 * Period)) < 1e-9 ? first : null;
    }

    /// <summary>The frame length of a launch that ran at 8 ticks a frame (2 fps; most of the 2026-10-15 to 2026-10-21 launches).</summary>
    public const double SlowFrameTicks = 8;

    /// <summary>
    /// Whether the original ran at <see cref="SlowFrameTicks"/> a frame throughout: every positive
    /// first-column value of <paramref name="lines"/> (where the trace scaffold prints <c>TICKS</c>),
    /// and the start-up <paramref name="stamp"/> if there is one, is a whole multiple of 8, with at
    /// least two distinct values. A 60 Hz run's <c>TICKS</c> fall on 1/32 ticks and a run that sped up
    /// part-way prints fractions, so neither passes. Such a run's samples, often one every few cycles,
    /// cannot place its frames, and a cycle counter it prints (<c>SECS</c>) can pass for the cycle count.
    /// </summary>
    public static bool RanAtSlowFrames(IReadOnlyList<OracleTraceLine> lines, double? stamp = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var values = new HashSet<double>();
        foreach (double value in lines.Select(static l => l.Value(0)).OfType<double>().Append(stamp ?? 0))
        {
            if (value <= 0)
            {
                continue;
            }

            if (Math.Abs((value / SlowFrameTicks) - Math.Round(value / SlowFrameTicks)) > 1e-9)
            {
                return false;
            }

            values.Add(value);
        }

        return values.Count >= 2;
    }

    /// <summary>
    /// The frame cadence at the end of the sampled cycles, with its origin at the last sample. DOSBox-X's
    /// frames against the 16 Hz tick last whole 1/32 ticks, and their running total is
    /// <c>floor(phase + k·rate)</c> units, so the steps take two neighbouring sizes spread as evenly as
    /// the rate allows (at about 61 fps 8s and 9s, never three 8s in a row). Fitted to the longest run of
    /// such steps at the end, at most <paramref name="maxSteps"/> and at least <paramref name="minSteps"/>
    /// of them, that one (rate, phase) reproduces. <c>null</c> when there is no such run.
    /// </summary>
    public static OracleCadence? FitCadence(IReadOnlyList<double> cycleTicks, int maxSteps = 256, int minSteps = 8)
    {
        ArgumentNullException.ThrowIfNull(cycleTicks);
        var fitter = new OracleCadenceFitter();
        fitter.TryAdd(0, 0);
        long total = 0;
        long smallest = long.MaxValue;
        long largest = long.MinValue;
        int steps = 0;
        for (int i = cycleTicks.Count - 1; i >= 1 && steps < maxSteps; i--)
        {
            double units = (SnapTicks(cycleTicks[i]) - SnapTicks(cycleTicks[i - 1])) * 32;
            long whole = (long)Math.Round(units);
            smallest = Math.Min(smallest, whole);
            largest = Math.Max(largest, whole);
            total += whole;
            if (Math.Abs(units - whole) > 1e-6 || whole <= 0 || largest - smallest > 1 || !fitter.TryAdd(-(steps + 1), -total))
            {
                break;
            }

            steps++;
        }

        return steps >= minSteps ? fitter.Fit() : null;
    }

    /// <summary>
    /// The frame cadence at the start of the sampled cycles, with its origin at the first sample: as
    /// <see cref="FitCadence"/>, but over the longest run of 60 Hz frame steps from the first sample on.
    /// <paramref name="firstStep"/>, the step into the first sample when its own <c>TIME_CORR</c> column
    /// shows it, is fitted too where it agrees. <c>null</c> when the run starts slower (8-tick frames)
    /// or the samples are not a cycle apart.
    /// </summary>
    public static OracleCadence? FitLeadingCadence(IReadOnlyList<double> cycleTicks, double? firstStep = null, int maxSteps = 256, int minSteps = 8)
    {
        ArgumentNullException.ThrowIfNull(cycleTicks);
        var fitter = new OracleCadenceFitter();
        fitter.TryAdd(0, 0);
        long total = 0;
        int steps = 0;
        for (int i = 1; i < cycleTicks.Count && steps < maxSteps; i++)
        {
            double units = (SnapTicks(cycleTicks[i]) - SnapTicks(cycleTicks[i - 1])) * 32;
            long whole = (long)Math.Round(units);
            total += whole;
            if (Math.Abs(units - whole) > 1e-6 || !IsFrameStep(whole / 32.0) || !fitter.TryAdd(steps + 1, total))
            {
                break;
            }

            steps++;
        }

        if (steps < minSteps)
        {
            return null;
        }

        if (firstStep is { } step && IsFrameStep(step))
        {
            fitter.TryAdd(-1, -(long)Math.Round(step * 32));
        }

        return fitter.Fit();
    }

    /// <summary>
    /// The start-up frames before the first sample, in ticks, frame 1 first, from the cadence the first
    /// samples keep (<see cref="FitLeadingCadence"/>) run backward. Every launch of the startup probe
    /// (OV-20261103-startup-clock) ran that cadence from its second cycle: cycle 1 reads <c>TICKS</c> 0
    /// and <c>TIME_CORR</c> 1, and is the earliest frame of the cadence whose own time is past 0, so the
    /// step into cycle 2 (9 to 17 thirty-seconds) is the rest of the time since the clock started.
    /// Frame 1's duration is the runtime's to ignore. <c>null</c> when no cadence fits, or when the
    /// first sample is cycle 1's.
    /// </summary>
    public static IReadOnlyList<double>? StartupFrames(IReadOnlyList<double> cycleTicks, double? firstStep = null)
    {
        ArgumentNullException.ThrowIfNull(cycleTicks);
        if (cycleTicks.Count == 0 || FitLeadingCadence(cycleTicks, firstStep) is not { } cadence)
        {
            return null;
        }

        long first = (long)Math.Round(SnapTicks(cycleTicks[0]) * 32);
        long Position(long k) => first + cadence.Units(k);
        long k1 = 0;
        while (Position(k1 - 1) > 0)
        {
            k1--;
        }

        if (k1 == 0 || Position(k1) <= 0)
        {
            return null;
        }

        var frames = new List<double> { (Position(k1) - Position(k1 - 1)) / 32.0, Position(k1 + 1) / 32.0 };
        for (long k = k1 + 2; k <= 0; k++)
        {
            frames.Add((Position(k) - Position(k - 1)) / 32.0);
        }

        return frames;
    }

    /// <summary>
    /// Whether a step is one of DOSBox-X's 60 Hz frames (8 to 10 thirty-seconds of a tick), not a flic's
    /// delay-paced cycle (1.094 ticks in the flic carriers), which says nothing about the start-up.
    /// </summary>
    public static bool IsFrameStep(double ticks) => ticks is >= 0.25 - 1e-9 and <= 0.3125 + 1e-9;

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
    /// <c>TICKS</c> does), a sampler that prints only every few cycles is filled in. (Other tokens'
    /// counters can belong to another phase of the cycle, so only the sampler's count.) Runs of 60 Hz
    /// gaps are filled along the one frame cadence (<see cref="OracleCadence"/>) that passes through
    /// all their samples and the step into each that the sampler's <c>TIME_CORR</c> column prints, if
    /// it has one (DOSBox-X's frames total <c>floor(phase + k·rate)</c> thirty-seconds of a tick). A
    /// gap from inside a flic to after it keeps the flic's step for its remaining frames (see
    /// <c>FillFlicEnd</c>). Any other gap of <c>n</c> counted cycles becomes <c>n</c> steps of whole
    /// 1/32 ticks, as close to equal as they can be, or <c>n</c> equal steps off that grid.
    /// </summary>
    public static IReadOnlyList<double> CycleTicks(IReadOnlyList<OracleTraceLine> lines, OracleTimingColumn timing) =>
        CycleTicks(lines, timing, out _);

    /// <summary>
    /// As <see cref="CycleTicks(IReadOnlyList{OracleTraceLine}, OracleTimingColumn)"/>; <paramref name="counted"/>
    /// tells whether a cycle counter placed every value, so each is one cycle after the last.
    /// </summary>
    public static IReadOnlyList<double> CycleTicks(IReadOnlyList<OracleTraceLine> lines, OracleTimingColumn timing, out bool counted)
    {
        ArgumentNullException.ThrowIfNull(lines);
        List<OracleTraceLine> tokenLines = lines
            .Where(timing.Matches)
            .ToList();
        int? counter = CounterColumn(tokenLines, timing.Column);
        counted = counter is not null;
        if (counter is not { } counterColumn)
        {
            var distinct = new List<double>();
            foreach (OracleTraceLine line in tokenLines)
            {
                double value = SnapTicks(line.Value(timing.Column)!.Value);
                if (distinct.Count == 0 || value > distinct[^1])
                {
                    distinct.Add(value);
                }
            }

            return distinct;
        }

        // Where every line is a sample, those printed before the sampler counts the cycle carry the
        // count a cycle behind. (A named sampler's other tokens are not taken as samples: an event
        // action prints after the sampler has counted, a probe action before.)
        string? sampler = timing.Token == OracleTimingColumn.AnyToken ? LeadingSampler(tokenLines, timing.Column, counterColumn) : null;
        var samples = new SortedDictionary<long, double>();
        var sampleLines = new Dictionary<long, OracleTraceLine>();
        foreach (OracleTraceLine line in tokenLines)
        {
            AddSample(samples, line, timing.Column, counterColumn, sampler, sampleLines);
        }

        (int Column, double Scale)? printedStep = TimeCorrectionColumn(tokenLines, timing.Column, counterColumn);
        long[] counts = [.. samples.Keys];
        double[] values = [.. samples.Values];
        long? StepUnits(int i) =>
            printedStep is { } p && sampleLines[counts[i]].Value(p.Column) is { } printed && Math.Abs((printed / p.Scale * 32) - Math.Round(printed / p.Scale * 32)) < 0.05
                ? (long)Math.Round(printed / p.Scale * 32)
                : null;

        var ticks = new List<double>();
        if (counts.Length > 0)
        {
            ticks.Add(values[0]);
        }

        // The longest run of 60 Hz gaps from sample `from` that one cadence fits, through every sample
        // and the step into it that the sample's own TIME_CORR column prints (origin at `from`).
        (OracleCadence? Cadence, int End) Segment(int from)
        {
            var fitter = new OracleCadenceFitter();
            fitter.TryAdd(0, 0);
            if (StepUnits(from) is { } firstStep)
            {
                fitter.TryAdd(-1, -firstStep);
            }

            int end = from;
            for (int next = from + 1; next < counts.Length; next++)
            {
                double units = (values[next] - values[from]) * 32;
                long whole = (long)Math.Round(units);
                long k = counts[next] - counts[from];
                double gapUnits = (values[next] - values[next - 1]) * 32 / (counts[next] - counts[next - 1]);
                if (Math.Abs(units - whole) > 1e-6 || !IsFrameStep(gapUnits / 32) || !fitter.TryAdd(k, whole) ||
                    (StepUnits(next) is { } step && !fitter.TryAdd(k - 1, whole - step)))
                {
                    break;
                }

                end = next;
            }

            return end > from && fitter.Fit() is { } cadence ? (cadence, end) : (null, from);
        }

        int segment = 0;
        while (segment < counts.Length - 1)
        {
            if (Segment(segment) is ({ } cadence, int end))
            {
                for (int i = segment + 1; i <= end; i++)
                {
                    for (long c = counts[i - 1] + 1; c < counts[i]; c++)
                    {
                        ticks.Add(values[segment] + (cadence.Units(c - counts[segment]) / 32.0));
                    }

                    ticks.Add(values[i]);
                }

                segment = end;
                continue;
            }

            if (!FillFlicEnd(segment))
            {
                // Not a 60 Hz gap (a flic's, or off the 1/32 grid): steps as equal as whole 1/32 ticks allow.
                double start = values[segment];
                double value = values[segment + 1];
                long cycles = counts[segment + 1] - counts[segment];
                double gap = (value - start) * 32;
                long wholeUnits = (long)Math.Round(gap);
                bool onGrid = Math.Abs(gap - wholeUnits) < 1e-6;
                for (long k = 1; k < cycles; k++)
                {
                    ticks.Add(onGrid
                        ? start + ((wholeUnits * k / cycles) / 32.0)
                        : start + ((value - start) * k / cycles));
                }

                ticks.Add(value);
            }

            segment++;
        }

        // A gap that starts during a flic (its sample prints a TIME_CORR that is not a 60 Hz frame's,
        // 1.094 or 0.625) and ends after it: the flic's remaining frames keep that step, as many as
        // leave the world cycles after it at least a 60 Hz frame each, and those cycles follow the
        // cadence the samples after the flic fit, the first absorbing the off-grid rest.
        bool FillFlicEnd(int i)
        {
            if (StepUnits(i) is not { } flicUnits || IsFrameStep(flicUnits / 32.0) || flicUnits <= 0)
            {
                return false;
            }

            double flicStep = flicUnits / 32.0;
            long cycles = counts[i + 1] - counts[i];
            double gap = values[i + 1] - values[i];
            long frames = 0;
            while (frames + 1 < cycles && (gap - ((frames + 1) * flicStep)) / (cycles - frames - 1) >= 0.25 - 1e-9)
            {
                frames++;
            }

            if (frames == 0 || (gap - (frames * flicStep)) / (cycles - frames) > 0.3125 + 1e-9)
            {
                return false;
            }

            var filled = new List<double>();
            for (long k = 1; k <= frames; k++)
            {
                filled.Add(values[i] + (k * flicStep));
            }

            // The world cycles after the flic: back from the next sample along the cadence that fits
            // after it, or, where that does not fit in, evenly.
            double flicEnd = filled[^1];
            long world = cycles - frames;
            var along = new List<double>();
            if (Segment(i + 1).Cadence is { } after)
            {
                for (long c = counts[i] + frames + 1; c < counts[i + 1]; c++)
                {
                    along.Add(values[i + 1] + (after.Units(c - counts[i + 1]) / 32.0));
                }
            }

            if (world > 1 && (along.Count == 0 || along[0] <= flicEnd))
            {
                // Whole 1/32 steps back from the next sample, the last its printed TIME_CORR.
                along.Clear();
                double last = values[i + 1] - ((StepUnits(i + 1) is { } lastUnits && world > 1 ? lastUnits : 0) / 32.0);
                long steps = last < values[i + 1] ? world - 1 : world;
                double rate = (last - flicEnd) * 32 / steps;
                for (long j = 1; j < world; j++)
                {
                    along.Add(j <= steps ? last - (Math.Floor((steps - j) * rate) / 32.0) : last);
                }

                if (along[0] <= flicEnd || along[^1] >= values[i + 1])
                {
                    return false;
                }
            }

            ticks.AddRange(filled);
            ticks.AddRange(along);
            ticks.Add(values[i + 1]);
            return true;
        }

        return ticks;
    }

    /// <summary>
    /// The column of a counted sampler's lines that prints <c>TIME_CORR</c>, and its scale (1, or 1024
    /// for a raw value): every value a whole number of 1/32 ticks between 0.1 and 2, equal to the step
    /// wherever two samples are one cycle apart; with no such pair, more than one value and a 60 Hz
    /// first value (0.25 or 0.281). <c>null</c> when there is none.
    /// </summary>
    private static (int Column, double Scale)? TimeCorrectionColumn(List<OracleTraceLine> tokenLines, int timingColumn, int counterColumn)
    {
        if (tokenLines.Count < 2)
        {
            return null;
        }

        int columns = tokenLines.Min(static l => l.Fields.Count);
        foreach (double scale in new[] { 1.0, 1024.0 })
        {
            for (int column = 0; column < columns; column++)
            {
                if (column == timingColumn || column == counterColumn ||
                    !tokenLines.All(l => l.Value(column) is { } v && v / scale is > 0.1 and <= 2 && Math.Abs((v / scale * 32) - Math.Round(v / scale * 32)) <= 0.05))
                {
                    continue;
                }

                int checks = 0;
                bool hits = true;
                for (int i = 1; i < tokenLines.Count && hits; i++)
                {
                    double dn = tokenLines[i].Value(counterColumn)!.Value - tokenLines[i - 1].Value(counterColumn)!.Value;
                    double step = SnapTicks(tokenLines[i].Value(timingColumn)!.Value) - SnapTicks(tokenLines[i - 1].Value(timingColumn)!.Value);
                    if (dn == 1 && step > 0)
                    {
                        checks++;
                        hits = Math.Abs((tokenLines[i].Value(column)!.Value / scale) - step) <= 0.002;
                    }
                }

                double first = tokenLines[0].Value(column)!.Value / scale;
                if (hits && (checks > 0 ||
                    ((Math.Abs(first - 0.25) <= 0.0015 || Math.Abs(first - 0.28125) <= 0.0015) && tokenLines.Select(l => l.Value(column)!.Value).Distinct().Count() > 1)))
                {
                    return (column, scale);
                }
            }
        }

        return null;
    }

    /// <summary>Adds a sampler line's (cycle count, <c>TICKS</c>) sample when both go up.</summary>
    private static void AddSample(SortedDictionary<long, double> samples, OracleTraceLine line, int timingColumn, int counterColumn, string? sampler, Dictionary<long, OracleTraceLine> sampleLines)
    {
        if (line.Value(counterColumn) is not { } countValue || countValue != Math.Floor(countValue) || countValue < 0 ||
            line.Value(timingColumn) is null)
        {
            return;
        }

        long count = (long)CycleOf(line, counterColumn, sampler);
        double value = SnapTicks(line.Value(timingColumn)!.Value);
        if (!samples.ContainsKey(count) && (samples.Count == 0 || (count > samples.Keys.Last() && value > samples.Values.Last())))
        {
            samples[count] = value;
            sampleLines[count] = line;
        }
    }

    /// <summary>
    /// The token whose counter runs a cycle ahead of the other tokens': the one whose line shares its
    /// <c>TICKS</c> with another token's line counting exactly one less. A trace's probe action prints
    /// before the global <c>EACH_TICK</c> sampler that raises the counter (OV-20261029-audio: <c>GO</c>
    /// and <c>P</c> at 13.438 with count 0 beside <c>C</c> 13.438 with 1). <c>null</c> when no line
    /// pairs so, or when two tokens would lead.
    /// </summary>
    internal static string? LeadingSampler(IReadOnlyList<OracleTraceLine> lines, int ticksColumn, int counterColumn)
    {
        ArgumentNullException.ThrowIfNull(lines);
        string? sampler = null;
        foreach (IGrouping<long, OracleTraceLine> group in lines
            .Where(l => l.Value(ticksColumn) is not null && l.Value(counterColumn) is { } n && n == Math.Floor(n))
            .GroupBy(l => (long)Math.Round(SnapTicks(l.Value(ticksColumn)!.Value) * 1024)))
        {
            foreach (OracleTraceLine ahead in group)
            {
                foreach (OracleTraceLine behind in group)
                {
                    if (ahead.Token != behind.Token && ahead.Value(counterColumn) == behind.Value(counterColumn) + 1)
                    {
                        if (sampler is not null && sampler != ahead.Token)
                        {
                            return null;
                        }

                        sampler = ahead.Token;
                    }
                }
            }
        }

        return sampler;
    }

    /// <summary>The cycle a line was printed in: its count, one more for a token other than the leading <paramref name="sampler"/>.</summary>
    private static double CycleOf(OracleTraceLine line, int counterColumn, string? sampler) =>
        line.Value(counterColumn)!.Value + (sampler is not null && line.Token != sampler ? 1 : 0);

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
