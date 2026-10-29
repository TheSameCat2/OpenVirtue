// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using OpenVirtue.Engine;
using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Formats.Save;
using OpenVirtue.Formats.Wrs;
using OpenVirtue.Host;

/// <summary>
/// The oracle-diff harness. <c>ovtool oracle replay</c> runs a prepared carrier (the loose
/// <c>work/</c> files the original ran) through OpenVirtue's <see cref="RuntimeHost"/> headless and
/// collects the files its <c>PRINTFILE</c> calls wrote; <c>ovtool oracle diff</c> also compares them
/// with the files the original wrote, line by line.
/// <para>
/// The replay runs in step with the original: from the cycle that prints the first sample on, each
/// frame advances <c>TICKS</c> by the original's step to its next sampled value (read from one
/// token's column, detected or given with <c>--timing TOKEN:COL</c>), so both runs see the same
/// <c>TIME_CORR</c> sequence even though DOSBox-X's cycle rate varies from run to run. Before the
/// first and after the last sample, and across a gap of more than four typical cycles, frames take
/// the median cycle. A start-up that reaches the first sample at a different tick is reported once
/// as the start offset, and columns that print <c>TICKS</c> are compared relative to it. Scripted key presses
/// and clicks (DOSBox-X <c>AUTOTYPE</c>, <c>x11_click.py</c>) are not replayed; such carriers are
/// flagged.
/// </para>
/// Everything the replay writes goes under <c>&lt;carrier&gt;/ov-replay/</c>, beside the original's
/// local-only outputs; nothing there may be committed.
/// </summary>
internal static class OracleReplay
{
    private const string ReplayDirectoryName = "ov-replay";
    private const string WorldName = OracleLaunchers.WorldName;

    // Files the autoexec writes, not the probe: the DOS directory listing and the runtime's console.
    private static readonly string[] AutoexecOutputs = ["OVDIR.TXT", "OVCON.TXT"];

    public static int Run(string verb, string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"oracle {verb} requires a carrier or probe directory.");
            return 2;
        }

        var options = ReplayOptions.Parse(args[1..]);
        if (options.Error is { } error)
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        List<string> carriers = FindCarriers(args[0]);
        if (carriers.Count == 0)
        {
            Console.Error.WriteLine($"No carrier (a folder with work/{WorldName}.WDL) under {args[0]}.");
            return 1;
        }

        bool diff = verb == "diff";
        (int ran, int failures, _) = Execute(args[0], carriers, options, diff);
        return ran == 0 && diff ? 1 : failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Diffs every run carrier under <paramref name="path"/> without printing each one: the number
    /// that ran and the number that match (for <c>oracle summary</c>).
    /// </summary>
    internal static (int Ran, int Matched) Tally(string path)
    {
        List<string> carriers = FindCarriers(path);
        TextWriter console = Console.Out;
        Console.SetOut(TextWriter.Null);
        try
        {
            (int ran, int failures, _) = Execute(path, carriers, ReplayOptions.Parse([]), diff: true);
            return (ran, ran - failures);
        }
        finally
        {
            Console.SetOut(console);
        }
    }

    private static (int Ran, int Failures, int Skipped) Execute(string path, List<string> carriers, ReplayOptions options, bool diff)
    {
        string root = Path.GetFullPath(path);
        int failures = 0;
        int skipped = 0;
        foreach (string carrier in carriers)
        {
            string label = Path.GetRelativePath(Directory.GetParent(root)?.FullName ?? root, carrier);
            if (diff && !HasOriginalOutputs(carrier))
            {
                Console.WriteLine($"{label}: not run (no original OV*.TXT in work/), skipped");
                skipped++;
                continue;
            }

            try
            {
                CarrierResult result = ReplayCarrier(carrier, options);
                if (diff)
                {
                    failures += Report(label, result, options) ? 0 : 1;
                }
                else
                {
                    Console.WriteLine($"{label}: {result.Frames} frames to TICKS {result.FinalTicks:F3}, wrote {string.Join(' ', result.ReplayFiles.Keys)} under {result.ReplayDirectory}");
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or ArgumentException)
            {
                Console.WriteLine($"{label}: ERROR {ex.Message}");
                failures++;
            }
        }

        int ran = carriers.Count - skipped;
        if (diff && carriers.Count > 1)
        {
            Console.WriteLine($"{ran - failures}/{ran} carriers match ({skipped} not run).");
        }

        return (ran, failures, skipped);
    }

    /// <summary>The carrier itself, or every carrier below a probe folder (round sub-folders included, <c>_stale</c> skipped).</summary>
    internal static List<string> FindCarriers(string path)
    {
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(full))
        {
            return [];
        }

        if (IsCarrier(full))
        {
            return [full];
        }

        return Directory.EnumerateDirectories(full, "*", SearchOption.AllDirectories)
            .Where(d => !Path.GetRelativePath(full, d).Split(Path.DirectorySeparatorChar).Any(static part => part.StartsWith('_') || part is ReplayDirectoryName or "work"))
            .Where(IsCarrier)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    internal static bool HasOriginalOutputs(string carrier) =>
        Directory.EnumerateFiles(Path.Combine(carrier, "work"), "OV*.TXT")
            .Any(static f => !AutoexecOutputs.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));

    private static bool IsCarrier(string directory) =>
        File.Exists(Path.Combine(directory, "work", WorldName + ".WDL"));

    private static CarrierResult ReplayCarrier(string carrier, ReplayOptions options)
    {
        string work = Path.Combine(carrier, "work");
        CarrierCase carrierCase = CarrierCase.Read(carrier);
        Dictionary<string, string> originalFiles = Directory.EnumerateFiles(work, "OV*.TXT")
            .Where(static f => !AutoexecOutputs.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            .ToDictionary(static f => Path.GetFileName(f).ToUpperInvariant(), static f => f, StringComparer.Ordinal);

        string? primary = carrierCase.TraceFile?.ToUpperInvariant();
        IReadOnlyList<OracleTraceLine> timingSource = primary is not null && originalFiles.TryGetValue(primary, out string? primaryPath)
            ? OracleTrace.Parse(File.ReadAllText(primaryPath, Encoding.Latin1))
            : [];
        OracleTimingColumn? timing = options.Timing ?? (options.FixedTimeCorrection is null ? OracleTrace.DetectTimingColumn(timingSource) : null);
        IReadOnlyList<double> cycleTicks = timing is { } t ? OracleTrace.CycleTicks(timingSource, t) : [];

        string replayDirectory = Path.Combine(carrier, ReplayDirectoryName);
        if (Directory.Exists(replayDirectory))
        {
            Directory.Delete(replayDirectory, recursive: true);
        }

        Directory.CreateDirectory(replayDirectory);
        string archive = Path.Combine(replayDirectory, WorldName + ".WRS");
        WrsArchive.WriteFile(archive, CarrierFiles(work));

        double? firstStep = timing is { } ft ? OracleTrace.FirstCycleStep(timingSource, ft) : null;
        var clock = new ReplayClock(cycleTicks, options.FixedTimeCorrection ?? 0.25, firstStep);
        double stopTicks = cycleTicks.Count > 0 ? cycleTicks[^1] + options.ExtraTicks : options.MaxTicks;
        var window = new HeadlessWindow();
        if (carrierCase.Autotype is { } typed)
        {
            foreach ((InkeyKey, char) key in HeadlessWindow.ParseAutotype(typed))
            {
                window.TypedKeys.Enqueue(key);
            }
        }

        int frames = 0;
        double ticks;
        var audio = new TimedAudioSink();
        using (RuntimeHost host = RuntimeHost.OpenArchive(archive, WorldName + ".WDL", window, new HeadlessInput(), audio, new HeadlessRender()))
        {
            host.FrameClock = () => clock.Next(host.Runtime.GetSkill(Timing.TicksSkill));
            audio.Now = () => host.Runtime.GetSkill(Timing.TicksSkill) / 16;
            host.Boot();
            string? replayPrimary = primary is null ? null : Path.Combine(replayDirectory, "openvirtue-prints", WorldName, primary);
            while (!host.ExitRequested && frames < options.MaxFrames && host.Runtime.GetSkill(Timing.TicksSkill) <= stopTicks + clock.Offset)
            {
                host.Frame(present: false);
                frames++;
                if (timing is { } anchorColumn && !clock.Anchored && replayPrimary is not null && File.Exists(replayPrimary) &&
                    FirstValue(OracleTrace.Parse(File.ReadAllText(replayPrimary, Encoding.Latin1)), anchorColumn) is { } anchor)
                {
                    clock.Anchor(anchor);
                }
            }

            ticks = host.Runtime.GetSkill(Timing.TicksSkill);
        }

        string printDirectory = Path.Combine(replayDirectory, "openvirtue-prints", WorldName);
        Dictionary<string, string> replayFiles = Directory.Exists(printDirectory)
            ? Directory.EnumerateFiles(printDirectory, "*.TXT").ToDictionary(static f => Path.GetFileName(f).ToUpperInvariant(), static f => f, StringComparer.Ordinal)
            : [];
        return new CarrierResult(carrierCase, primary, timing, cycleTicks, clock.Anchored ? clock.Offset : null, frames, ticks, replayDirectory, originalFiles, replayFiles);
    }

    /// <summary>
    /// For a carrier without a per-cycle timing column, the start offset read off the carrier
    /// template's start-up stamp (<c>TCK=</c> in <c>OVPRB0.TXT</c>, <c>TICKS</c> as the probe action
    /// starts), applied to each token's first column when it reads like <c>TICKS</c>: never below
    /// the stamp and never going down.
    /// </summary>
    private static OracleTickShift? StampShift(CarrierResult result, IReadOnlyList<OracleTraceLine> original)
    {
        const string Probe = "OVPRB0.TXT";
        double? originalStamp = result.OriginalFiles.TryGetValue(Probe, out string? originalProbe) ? Stamp(originalProbe) : null;
        double? replayStamp = result.ReplayFiles.TryGetValue(Probe, out string? replayProbe) ? Stamp(replayProbe) : null;
        if ((originalStamp is null || replayStamp is null) && result.Primary is { } primary &&
            result.ReplayFiles.TryGetValue(primary, out string? replayPrimary))
        {
            // No stamp (trace carriers before 2026-10-29): the trace's first line with a number first,
            // usually the GO line printed as the probe starts, stands in for it.
            IReadOnlyList<OracleTraceLine> replay = OracleTrace.Parse(File.ReadAllText(replayPrimary, Encoding.Latin1));
            int first = original.ToList().FindIndex(static l => l.Value(0) is not null);
            if (first >= 0 && first < replay.Count && replay[first].Token == original[first].Token &&
                original[first].Value(0) is { } a && replay[first].Value(0) is { } b && Math.Abs(a - b) <= 1)
            {
                (originalStamp, replayStamp) = (a, b);
            }
        }

        if (originalStamp is null || replayStamp is null)
        {
            return null;
        }

        var columns = new HashSet<(string, int)>();
        foreach (IGrouping<string, OracleTraceLine> group in original.GroupBy(static l => l.Token, StringComparer.Ordinal))
        {
            double previous = originalStamp.Value - 1e-3;
            bool ticks = true;
            foreach (OracleTraceLine line in group)
            {
                if (line.Value(0) is not { } value || value < previous)
                {
                    ticks = false;
                    break;
                }

                previous = value;
            }

            if (ticks)
            {
                columns.Add((group.Key, 0));
            }
        }

        return new OracleTickShift(columns, originalStamp.Value, replayStamp.Value);

        static double? Stamp(string path) =>
            File.ReadLines(path, Encoding.Latin1)
                .Where(static l => l.StartsWith("TCK=", StringComparison.Ordinal))
                .Select(static l => double.TryParse(l.AsSpan(4), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : (double?)null)
                .FirstOrDefault();
    }

    private static double? FirstValue(IReadOnlyList<OracleTraceLine> lines, OracleTimingColumn timing) =>
        lines.FirstOrDefault(l => string.Equals(l.Token, timing.Token, StringComparison.Ordinal) && l.Value(timing.Column) is not null)
            ?.Value(timing.Column);

    private static IEnumerable<WrsFile> CarrierFiles(string work)
    {
        foreach (string file in Directory.EnumerateFiles(work).Order(StringComparer.Ordinal))
        {
            // The carrier's inputs are DOS 8.3 names; the original's outputs (OV*.TXT/PCX/SAV) and
            // anything else left beside them are not packed.
            string name = Path.GetFileName(file);
            bool output = name.StartsWith("OV", StringComparison.OrdinalIgnoreCase) &&
                (name.EndsWith(".TXT", StringComparison.OrdinalIgnoreCase) ||
                 name.EndsWith(".PCX", StringComparison.OrdinalIgnoreCase) ||
                 name.EndsWith(".SAV", StringComparison.OrdinalIgnoreCase));
            string stem = Path.GetFileNameWithoutExtension(name);
            bool dosName = stem.Length is > 0 and <= 8 && Path.GetExtension(name).Length <= 4;
            if (output || !dosName || name.EndsWith(".EXE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new WrsFile(name.ToUpperInvariant(), File.ReadAllBytes(file));
        }
    }

    /// <returns><c>true</c> when every file the original wrote has a matching replay file.</returns>
    private static bool Report(string label, CarrierResult result, ReplayOptions options)
    {
        var sb = new StringBuilder();
        bool ok = true;
        string timing = result.Timing is { } t ? $"timing {t} ({result.CycleTicks.Count} cycles)" : "fixed timing";
        sb.Append(CultureInfo.InvariantCulture, $"{label}: {timing}, {result.Frames} frames to TICKS {result.FinalTicks:F3}");
        if (result.AnchorOffset is { } offset && result.CycleTicks.Count > 0)
        {
            string flag = Math.Abs(offset) > options.Tolerance ? " START OFFSET" : "";
            sb.Append(CultureInfo.InvariantCulture, $", first cycle at TICKS {result.CycleTicks[0]:F3} orig / {result.CycleTicks[0] + offset:F3} ov ({offset:+0.000;-0.000;0}){flag}");
        }
        else if (result.Timing is not null)
        {
            sb.Append(", the replay never printed the timing token");
        }
        if (result.Case.Autotype is not null)
        {
            sb.Append(", AUTOTYPE keys typed into INKEY at once (their wall-clock timing and other key hooks not replayed)");
        }
        else if (result.Case.ScriptedInput)
        {
            sb.Append(", scripted input NOT replayed");
        }

        sb.AppendLine();
        IEnumerable<string> names = result.OriginalFiles.Keys.Order(StringComparer.Ordinal);
        if (result.Primary is { } primary)
        {
            names = names.OrderBy(n => n == primary ? 0 : 1);
        }

        foreach (string name in names)
        {
            if (!result.ReplayFiles.TryGetValue(name, out string? replayPath))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {name}: MISSING in the replay");
                ok = false;
                continue;
            }

            IReadOnlyList<OracleTraceLine> original = OracleTrace.Parse(File.ReadAllText(result.OriginalFiles[name], Encoding.Latin1));
            IReadOnlyList<OracleTraceLine> replay = OracleTrace.Parse(File.ReadAllText(replayPath, Encoding.Latin1));
            OracleTickShift? shift = result.AnchorOffset is { } anchorOffset && result.CycleTicks.Count > 0
                ? new OracleTickShift(OracleTrace.TickColumns(original, result.CycleTicks), result.CycleTicks[0], result.CycleTicks[0] + anchorOffset)
                : StampShift(result, original);
            OracleTraceComparison comparison = OracleTrace.Compare(original, replay, options.Tolerance, shift);
            if (comparison.Identical)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {name}: match ({comparison.OriginalLines} lines)");
                continue;
            }

            ok = false;
            string tokens = comparison.TokensMatch ? "tokens match" : comparison.TokenRunsMatch ? "token runs match" : "tokens differ";
            int at = comparison.FirstDivergence!.Value;
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {name}: DIFFERS at line {at + 1} ({comparison.MatchingLines}/{comparison.OriginalLines} original lines match, replay has {comparison.ReplayLines}; {tokens})");
            for (int i = Math.Max(0, at - options.Context); i <= at + options.Context; i++)
            {
                string o = i < original.Count ? original[i].ToString() : "(end)";
                string r = i < replay.Count ? replay[i].ToString() : "(end)";
                string mark = i < original.Count && i < replay.Count && OracleTrace.LinesMatch(original[i], replay[i], options.Tolerance, shift) ? " " : "!";
                sb.AppendLine(CultureInfo.InvariantCulture, $"   {mark}{i + 1,5}  orig: {o}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"    {"",5}  ov:   {r}");
            }
        }

        foreach (string extra in result.ReplayFiles.Keys.Except(result.OriginalFiles.Keys).Order(StringComparer.Ordinal))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {extra}: only in the replay");
        }

        ok &= CompareSaves(result, sb);

        Console.Write(sb.ToString());
        return ok;
    }

    /// <summary>
    /// Compares every save file the original wrote (<c>OV*.SAV</c> in <c>work/</c>) with the replay's
    /// (in its content root), by their unpacked bodies, since the two runtimes compress differently.
    /// </summary>
    private static bool CompareSaves(CarrierResult result, StringBuilder sb)
    {
        bool ok = true;
        string work = Path.Combine(Path.GetDirectoryName(result.ReplayDirectory)!, "work");
        foreach (string original in Directory.EnumerateFiles(work, "OV*.SAV").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(original).ToUpperInvariant();
            string? replay = Directory.EnumerateFiles(result.ReplayDirectory, "*.SAV")
                .FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (replay is null)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {name}: MISSING in the replay");
                ok = false;
                continue;
            }

            byte[] a = AcknexSaveFile.Unpack(File.ReadAllBytes(original));
            byte[] b = AcknexSaveFile.Unpack(File.ReadAllBytes(replay));
            int first = a.AsSpan().CommonPrefixLength(b);
            if (first == a.Length && a.Length == b.Length)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {name}: match ({a.Length}-byte body)");
            }
            else
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {name}: DIFFERS at body byte {first} ({a.Length} original, {b.Length} replay)");
                ok = false;
            }
        }

        return ok;
    }

    private sealed record CarrierResult(
        CarrierCase Case,
        string? Primary,
        OracleTimingColumn? Timing,
        IReadOnlyList<double> CycleTicks,
        double? AnchorOffset,
        int Frames,
        double FinalTicks,
        string ReplayDirectory,
        IReadOnlyDictionary<string, string> OriginalFiles,
        IReadOnlyDictionary<string, string> ReplayFiles);

    /// <summary>The parts of a carrier's <c>case.json</c> the replay needs.</summary>
    private sealed record CarrierCase(string? TraceFile, bool ScriptedInput, string? Autotype = null)
    {
        /// <summary>Reads <c>case.json</c>; a click shows only in <c>run.sh</c>, which starts <c>x11_click.py</c>.</summary>
        public static CarrierCase Read(string carrier)
        {
            string runScript = Path.Combine(carrier, "run.sh");
            bool click = File.Exists(runScript) && File.ReadAllText(runScript).Contains("x11_click", StringComparison.Ordinal);
            string path = Path.Combine(carrier, "case.json");
            if (!File.Exists(path))
            {
                return new CarrierCase(null, click);
            }

            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = json.RootElement;
            string? trace = root.TryGetProperty("trace_file", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            string? autotype = root.TryGetProperty("autotype", out JsonElement a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
            return new CarrierCase(trace, autotype is not null || click, autotype);
        }
    }

    private sealed record ReplayOptions(
        OracleTimingColumn? Timing,
        double? FixedTimeCorrection,
        double Tolerance,
        double ExtraTicks,
        double MaxTicks,
        int MaxFrames,
        int Context,
        string? Error)
    {
        public static ReplayOptions Parse(string[] args)
        {
            OracleTimingColumn? timing = null;
            double? fixedCorrection = null;
            double tolerance = OracleTrace.DefaultTolerance;
            double extraTicks = 64;
            double maxTicks = 16 * 300;
            int maxFrames = 200_000;
            int context = 2;
            for (int i = 0; i < args.Length; i++)
            {
                string? value = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "--timing" when value is not null && OracleTimingColumn.TryParse(value, out OracleTimingColumn t):
                        timing = t;
                        break;
                    case "--time-corr" when TryNumber(value, out double v) && v > 0:
                        fixedCorrection = v;
                        break;
                    case "--tolerance" when TryNumber(value, out double v) && v >= 0:
                        tolerance = v;
                        break;
                    case "--extra-ticks" when TryNumber(value, out double v) && v >= 0:
                        extraTicks = v;
                        break;
                    case "--max-ticks" when TryNumber(value, out double v) && v > 0:
                        maxTicks = v;
                        break;
                    case "--context" when TryNumber(value, out double v) && v >= 0:
                        context = (int)v;
                        break;
                    default:
                        return new ReplayOptions(null, null, 0, 0, 0, 0, 0, $"Unknown or malformed option '{args[i]}'.");
                }

                i++;
            }

            return new ReplayOptions(timing, fixedCorrection, tolerance, extraTicks, maxTicks, maxFrames, context, null);
        }

        private static bool TryNumber(string? text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Supplies frame durations. Until the replay prints its first sampled cycle every frame lasts the
    /// original's first sampled cycles; then <see cref="Anchor"/> shifts the samples so that cycle lines up and
    /// the following frames land <c>TICKS</c> on the original's sampled values, whatever the start-up
    /// took. With no samples every frame lasts <c>fallbackTicks</c>.
    /// </summary>
    internal sealed class ReplayClock(IReadOnlyList<double> cycleTicks, double fallbackTicks, double? firstStep = null)
    {
        private const double Epsilon = 0.0005;
        private readonly double _typical = Median(cycleTicks) ?? fallbackTicks;

        // DOSBox-X's rate drifts within a run, so the start-up runs at the first sampled cycles' rate,
        // or at the step the first sample's own TIME_CORR column gives (FirstCycleStep).
        private readonly double _startup = firstStep ?? (cycleTicks.Count >= 3 ? Median([cycleTicks[0], cycleTicks[1], cycleTicks[2]]) : null) ?? Median(cycleTicks) ?? fallbackTicks;
        private int _next;

        /// <summary>True once <see cref="Anchor"/> has run.</summary>
        public bool Anchored { get; private set; }

        /// <summary>How far the replay's ticks run ahead of the original's (0 before the anchor).</summary>
        public double Offset { get; private set; }

        /// <summary>Aligns the original's first sampled cycle with the replay tick it was printed at.</summary>
        public void Anchor(double replayTicks)
        {
            if (Anchored || cycleTicks.Count == 0)
            {
                return;
            }

            // Both runs' TICKS are 22.10 values printed to three decimals: keep the offset on the
            // 1/1024 grid so every later step is one of the original's exactly.
            Offset = Math.Round((OracleTrace.SnapTicks(replayTicks) - cycleTicks[0]) * 1024) / 1024;
            Anchored = true;
            _next = 0;
        }

        /// <summary>The next frame's duration in seconds, given the replay's current <c>TICKS</c>.</summary>
        public double Next(double ticks)
        {
            if (!Anchored)
            {
                // Before the first sampled cycle the original's frame times are unknown. Run steady
                // cycles at the first sampled rate, but plan them to reach the original's first sample
                // exactly: arrive at its TICKS less the first cycle's step, then take that step. The
                // carriers' start-up WAITT resumes in the first cycle after TICKS passes 13, so landing
                // there makes the replay print its first sample at the original's TICKS.
                if (cycleTicks.Count > 0)
                {
                    double last = firstStep ?? _startup;
                    double remaining = cycleTicks[0] - last - ticks;
                    if (remaining > Epsilon)
                    {
                        return PlannedStep(remaining) / Timing.TicksPerSecond;
                    }

                    if (remaining > -Epsilon)
                    {
                        return last / Timing.TicksPerSecond;
                    }
                }

                return _startup / Timing.TicksPerSecond;
            }

            while (_next < cycleTicks.Count && cycleTicks[_next] + Offset <= ticks + Epsilon)
            {
                _next++;
            }

            double step = _typical;
            if (_next < cycleTicks.Count)
            {
                double gap = cycleTicks[_next] + Offset - ticks;
                if (gap <= 4 * _typical)
                {
                    step = gap;
                }
            }

            return step / Timing.TicksPerSecond;
        }

        /// <summary>
        /// The next start-up step toward a target <paramref name="remaining"/> ticks away: whole 1/32
        /// ticks around the start-up rate (the original's frames last 8/32 or 9/32 at 60 Hz), split
        /// evenly so they add up exactly; an off-grid remainder goes into the last step.
        /// </summary>
        private double PlannedStep(double remaining)
        {
            double unit = 1.0 / 32;
            int typical = Math.Max(1, (int)Math.Floor((_startup / unit) + 1e-9));
            int left = (int)Math.Round(remaining / unit);
            if (Math.Abs((left * unit) - remaining) > 1e-6 || left < typical)
            {
                return remaining < 1.5 * _startup ? remaining : _startup;
            }

            // n steps of `typical` or `typical + 1` units: the fewest steps that can reach `left`.
            int steps = Math.Max(1, (int)Math.Ceiling(left / (double)(typical + 1)));
            while (steps * typical > left)
            {
                steps--;
            }

            steps = Math.Max(1, steps);
            int step = left / steps; // the first steps take the floor, the last ones the extra unit
            return step * unit;
        }

        private static double? Median(IReadOnlyList<double> ticks)
        {
            if (ticks.Count < 2)
            {
                return null;
            }

            double[] gaps = new double[ticks.Count - 1];
            for (int i = 1; i < ticks.Count; i++)
            {
                gaps[i - 1] = ticks[i] - ticks[i - 1];
            }

            Array.Sort(gaps);
            return gaps[gaps.Length / 2];
        }
    }

    private sealed class HeadlessWindow : IPlatformWindow
    {
        public int Width => 640;

        public int Height => 480;

        public string Title { get; set; } = "";

        public bool IsOpen { get; private set; } = true;

        public bool Focused => false;

        public bool PointerInside => false;

        public void Close() => IsOpen = false;

        public void SetCursorVisible(bool visible)
        {
        }

        public string? Prompt(string label, string initialValue, int capacity) => null;

        /// <summary>The carrier's AUTOTYPE keys, typed into the first INKEY the replay opens.</summary>
        public Queue<(InkeyKey Key, char Character)> TypedKeys { get; } = new();

        public bool BeginTextInput() => true;

        public IReadOnlyList<(InkeyKey Key, char Character)> ConsumeTextKeys()
        {
            var keys = TypedKeys.ToArray();
            TypedKeys.Clear();
            return keys;
        }

        /// <summary>
        /// The keys of a DOSBox-X <c>AUTOTYPE</c> argument list (options such as <c>-w 10</c> skipped), as
        /// INKEY keys: single characters, <c>space</c>, <c>enter</c>, <c>esc</c>, <c>bspace</c>, <c>up</c>,
        /// <c>down</c>, <c>left</c>, <c>right</c>, <c>pageup</c>, <c>pagedown</c>.
        /// </summary>
        public static IEnumerable<(InkeyKey Key, char Character)> ParseAutotype(string autotype)
        {
            string[] words = autotype.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                if (word.StartsWith('-'))
                {
                    i++; // the option's value
                    continue;
                }

                (InkeyKey, char)? key = word.ToLowerInvariant() switch
                {
                    "space" => (InkeyKey.Character, ' '),
                    "enter" => (InkeyKey.Return, '\0'),
                    "esc" => (InkeyKey.Escape, '\0'),
                    "bspace" => (InkeyKey.Backspace, '\0'),
                    "up" => (InkeyKey.Up, '\0'),
                    "down" => (InkeyKey.Down, '\0'),
                    "left" => (InkeyKey.Left, '\0'),
                    "right" => (InkeyKey.Right, '\0'),
                    "pageup" => (InkeyKey.PageUp, '\0'),
                    "pagedown" => (InkeyKey.PageDown, '\0'),
                    { Length: 1 } single => (InkeyKey.Character, single[0]),
                    _ => null,
                };
                if (key is { } k)
                {
                    yield return k;
                }
            }
        }
    }

    private sealed class HeadlessInput : IInputSource
    {
        public IReadOnlyCollection<HostKey> Pressed => [];

        public Vector2 PointerPosition => Vector2.Zero;

        public bool IsDown(HostKey key) => false;

        public bool IsDown(MouseButton button) => false;

        public bool WasPressed(MouseButton button) => false;

        public bool WasReleased(MouseButton button) => false;

        public Vector2 ConsumeMotion() => Vector2.Zero;
    }

    /// <summary>
    /// Plays nothing but keeps each voice busy for its length in engine time (16 <c>TICKS</c> a
    /// second), so channels free up when the original's would (OV-20261029-audio).
    /// </summary>
    private sealed class TimedAudioSink : IAudioSink
    {
        private readonly double[] _ends = new double[8];

        public Func<double> Now { get; set; } = static () => 0;

        public void StartVoice(int channel, ReadOnlyMemory<float> monoSamples, int sampleRate, float volume, float pan, bool looping)
        {
            if (channel is >= 0 and < 8 && sampleRate > 0)
            {
                _ends[channel] = looping ? double.PositiveInfinity : Now() + (monoSamples.Length / (double)sampleRate);
            }
        }

        public void UpdateVoice(int channel, float volume, float pan)
        {
        }

        public void StopChannel(int channel)
        {
            if (channel is >= 0 and < 8)
            {
                _ends[channel] = 0;
            }
        }

        public void StopAll() => Array.Clear(_ends);

        public bool IsChannelPlaying(int channel) => channel is >= 0 and < 8 && Now() < _ends[channel];

        public void PlayBeep()
        {
        }
    }

    private sealed class HeadlessRender : IRenderBackend
    {
        public void UploadWorld(IReadOnlyDictionary<string, TextureImage> images)
        {
        }

        public void UploadUi(IReadOnlyDictionary<string, TextureImage> images)
        {
        }

        public void UploadFonts(IReadOnlyDictionary<string, TextureImage> images)
        {
        }

        public void Draw(HostFrame frame)
        {
        }

        public RgbaImage ReadRgba() => new(1, 1, [0, 0, 0, 255]);

        public void Present()
        {
        }
    }
}
