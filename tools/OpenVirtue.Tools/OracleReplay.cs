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
/// first sample, and across a gap of more than four typical cycles, frames take the median cycle;
/// after the last they continue the last samples' frame cadence (<see cref="OracleTrace.FitCadence"/>).
/// A launch that ran at 8 ticks a frame throughout gets fixed 8-tick frames, and <c>RANDOM</c> draws
/// from a fixed seed. A start-up that reaches the first sample at a different tick is reported once
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

    // The original's generator is not modelled, so any fixed seed will do: it only makes a carrier
    // that draws RANDOM give the same replay every time (round2 anim-random gave a different one per run).
    private const int RandomSeed = 1;

    /// <summary>The original's seconds clock against <c>TICKS</c> at 60 frames a second (OV-20261103-contact-clock).</summary>
    private const double SecondsClockRate = 0.991;

    /// <summary>
    /// Where that clock stood at the first cycle when a carrier does not say: 1.28 ticks puts every
    /// second of contact-clock clock-sec and scheduler-order anim-clock, order and order-wmp where the
    /// original's are.
    /// </summary>
    private const double SecondsClockStartTicks = 1.28;

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
        double? stamp = originalFiles.TryGetValue("OVPRB0.TXT", out string? probe) ? StartupStamp(probe) : null;
        bool asked = options.Timing is not null || carrierCase.Timing is not null || options.FixedTimeCorrection is not null || carrierCase.TimeCorrection is not null;

        // A launch that ran at 8 ticks a frame throughout is replayed with frames of 8 ticks.
        double? slowFrames = !asked && OracleTrace.RanAtSlowFrames(timingSource, stamp) ? OracleTrace.SlowFrameTicks : null;
        OracleTimingColumn? timing = options.Timing ?? carrierCase.Timing ??
            (!asked && slowFrames is null ? OracleTrace.DetectTimingColumn(timingSource) ?? (carrierCase.Autotype is not null ? OracleTrace.AutotypeTiming(timingSource) : null) : null);
        bool counted = false;
        IReadOnlyList<double> cycleTicks = timing is { } t ? OracleTrace.CycleTicks(timingSource, t, out counted) : [];
        if (options.DumpClock)
        {
            // --dump-clock: the sampled cycles' TICKS as the replay's clock will follow them.
            Console.Error.WriteLine($"clock {carrier}: {cycleTicks.Count} cycles, counted={counted}, timing={timing}");
            for (int i = 0; i < cycleTicks.Count; i++)
            {
                Console.Error.WriteLine($"clock[{i}] {cycleTicks[i]:F4}");
            }
        }

        string replayDirectory = ReplayDirectoryOf(carrier);
        if (Directory.Exists(replayDirectory))
        {
            Directory.Delete(replayDirectory, recursive: true);
        }

        Directory.CreateDirectory(replayDirectory);
        string archive = Path.Combine(replayDirectory, WorldName + ".WRS");
        WrsArchive.WriteFile(archive, CarrierFiles(work));

        double? firstStep = carrierCase.StartupStep ??
            (timing is { } ft ? OracleTrace.FirstCycleStep(timingSource, ft) : null) ??
            StampStep(stamp, cycleTicks) ??
            OracleTrace.PeriodicFirstStep(cycleTicks);
        // A carrier with no timing samples but an original full save: the save's TICKS and TOTAL_FRAMES
        // say how many frames the original ran to the save and how long they were in all, so the
        // start-up is planned as that many frames of whole 1/32 ticks spread evenly (the save-body
        // carriers: 44 frames to 13.875, about 10/32 each, where the replay ran 56 of 8/32).
        (IReadOnlyList<double> Frames, double Rate, string Note)? savePlan = !asked && cycleTicks.Count == 0 && slowFrames is null ? SaveClockPlan(work) : null;
        var clock = new ReplayClock(
            cycleTicks,
            options.FixedTimeCorrection ?? carrierCase.TimeCorrection ?? slowFrames ?? savePlan?.Rate ?? 0.25,
            firstStep,
            counted,
            savePlan?.Frames);
        // Run on past the last sample to the last TICKS any original file printed: a sampler switched
        // off before the probe's last lines (wall-pass-clip clip-wall, border-climb climb-4) would
        // otherwise stop the replay before them and before OVEND.
        double stopTicks = cycleTicks.Count > 0
            ? Math.Max(cycleTicks[^1], Math.Min(LastTicks(originalFiles.Values), options.MaxTicks)) + options.ExtraTicks
            : options.MaxTicks;
        var window = new HeadlessWindow();
        if (carrierCase.Autotype is { } typed)
        {
            foreach ((InkeyKey, char) key in HeadlessWindow.ParseAutotype(typed))
            {
                window.TypedKeys.Enqueue(key);
            }

            // DOSBox-X typed the keys on its wall clock; the original's trace says in which cycle the
            // input ended (the inkey carriers' R line) against the cycle before it (B): the replay
            // holds the keys that many polls, one a cycle, so the INKEY completes in the same cycle.
            window.KeyDelayPolls = InputEndDelay(timingSource);
        }

        int frames = 0;
        double ticks;
        var audio = new TimedAudioSink();
        using (RuntimeHost host = RuntimeHost.OpenArchive(archive, WorldName + ".WDL", window, new HeadlessInput(), audio, new HeadlessRender(), RandomSeed))
        {
            host.FrameClock = () => clock.Next(host.Runtime.GetSkill(Timing.TicksSkill));
            host.WriteOriginalSaves = true; // full SAVEs in the original's format too, to hand to the DOS runtime
            host.FatalErrors = true; // a division by zero shuts the original down (OV-20261029-numeric-edge)

            // The original's seconds clock: TICKS gains 0.9% on it at 60 frames a second, and it had run
            // 1.0 to 1.35 ticks by the first cycle (OV-20261103-contact-clock).
            if (slowFrames is null)
            {
                host.Runtime.SecondsClockRate = SecondsClockRate;
                host.Runtime.SecondsClockStartTicks = carrierCase.SecondsOffset ?? SecondsClockStartTicks;
            }
            audio.Now = () => host.Runtime.GetSkill(Timing.TicksSkill) / 16;
            host.Boot();
            string? replayPrimary = primary is null ? null : Path.Combine(replayDirectory, "openvirtue-prints", WorldName, primary);
            while (!host.ExitRequested && frames < options.MaxFrames && host.Runtime.GetSkill(Timing.TicksSkill) <= stopTicks + clock.Offset)
            {
                window.Frame = frames;
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
        return new CarrierResult(carrierCase, primary, timing, cycleTicks, clock.Anchored ? clock.Offset : null, frames, ticks, replayDirectory, originalFiles, replayFiles, carrier, savePlan?.Note);
    }

    /// <summary>
    /// Where a carrier's replay goes: <c>&lt;carrier&gt;/ov-replay/</c>, or, with <c>OV_REPLAY_ROOT</c>
    /// set, the same path below that folder, so runs from several checkouts at once do not overwrite
    /// each other's replays.
    /// </summary>
    private static string ReplayDirectoryOf(string carrier)
    {
        string? root = Environment.GetEnvironmentVariable("OV_REPLAY_ROOT");
        string full = Path.GetFullPath(carrier);
        return string.IsNullOrWhiteSpace(root)
            ? Path.Combine(full, ReplayDirectoryName)
            : Path.Combine(Path.GetFullPath(root), full.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace(':', '_'), ReplayDirectoryName);
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
        double? originalStamp = result.OriginalFiles.TryGetValue(Probe, out string? originalProbe) ? StartupStamp(originalProbe) : null;
        double? replayStamp = result.ReplayFiles.TryGetValue(Probe, out string? replayProbe) ? StartupStamp(replayProbe) : null;
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

            // A token printed once shows no rise: take it for TICKS only near the stamp (the GO line),
            // not a lone position such as wall-sides' V 40.000.
            if (ticks && (group.Count() > 1 || Math.Abs(group.First().Value(0)!.Value - originalStamp.Value) <= 1))
            {
                columns.Add((group.Key, 0));
            }
        }

        return new OracleTickShift(columns, originalStamp.Value, replayStamp.Value);
    }

    /// <summary>
    /// The largest <c>TICKS</c> the original's files print: the last first-column value of each token
    /// whose first column never goes down, where the trace scaffold prints <c>TICKS</c>.
    /// </summary>
    internal static double LastTicks(IEnumerable<string> files)
    {
        double last = 0;
        foreach (string file in files)
        {
            foreach (IGrouping<string, OracleTraceLine> group in OracleTrace.Parse(File.ReadAllText(file, Encoding.Latin1)).GroupBy(static l => l.Token, StringComparer.Ordinal))
            {
                double? previous = null;
                bool rising = true;
                foreach (OracleTraceLine line in group)
                {
                    if (line.Value(0) is not { } value || value < (previous ?? 0))
                    {
                        rising = false;
                        break;
                    }

                    previous = value;
                }

                if (rising && previous is { } end)
                {
                    last = Math.Max(last, end);
                }
            }
        }

        return last;
    }

    /// <summary>The carrier template's start-up stamp: <c>TCK=</c> in <c>OVPRB0.TXT</c>, <c>TICKS</c> as the probe action starts.</summary>
    /// <summary>
    /// The cycles from the trace line before the first <c>R</c> line to that line, the original's input
    /// span for a carrier with typed keys (0 without such lines).
    /// </summary>
    private static int InputEndDelay(IReadOnlyList<OracleTraceLine> lines)
    {
        for (int i = 1; i < lines.Count; i++)
        {
            if (lines[i].Token != "R")
            {
                continue;
            }

            for (int j = i - 1; j >= 0; j--)
            {
                if (lines[i].Value(1) is { } end && lines[j].Value(1) is { } start && end >= start)
                {
                    return (int)Math.Round(end - start);
                }
            }

            return 0;
        }

        return 0;
    }

    /// <summary>
    /// The start-up plan an original full save under <paramref name="work"/> gives: its <c>TOTAL_FRAMES</c>
    /// frames (the earliest save's) summing to its <c>TICKS</c>, as whole 1/32 ticks spread along
    /// floor(k·units/frames), with a first entry for cycle 1 (whose duration the runtime ignores), and
    /// the frames' mean rate in ticks. Null without such a save.
    /// </summary>
    private static (IReadOnlyList<double> Frames, double Rate, string Note)? SaveClockPlan(string work)
    {
        (int Frames, long Units, long LastUnits)? best = null;
        foreach (string save in Directory.EnumerateFiles(work, "OVSAV*.SAV"))
        {
            IReadOnlyList<SaveSkill> skills;
            try
            {
                skills = AcknexSaveFile.ReadSkills(AcknexSaveFile.Unpack(File.ReadAllBytes(save)));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                continue;
            }

            SaveSkill? ticks = skills.FirstOrDefault(static k => k.Name.Equals("TICKS", StringComparison.OrdinalIgnoreCase));
            SaveSkill? frames = skills.FirstOrDefault(static k => k.Name.Equals("TOTAL_FRAMES", StringComparison.OrdinalIgnoreCase));
            SaveSkill? last = skills.FirstOrDefault(static k => k.Name.Equals("TIME_CORR", StringComparison.OrdinalIgnoreCase));
            if (ticks is null || frames is null)
            {
                continue;
            }

            int count = (int)Math.Round(frames.RawValue / 1024.0);
            long units = (long)Math.Round(ticks.RawValue / 32.0);
            long lastUnits = last is null ? 0 : (long)Math.Round(last.RawValue / 32.0);
            if (count > 0 && units > 0 && (best is null || count < best.Value.Frames))
            {
                best = (count, units, lastUnits);
            }
        }

        if (best is not { } anchor)
        {
            return null;
        }

        // The save's TIME_CORR is a 60 Hz frame's (8 or 9 units) in the save-body carriers, so the run
        // ended on the 60 Hz cadence and its surplus over that cadence lies in the first frames: the
        // last frames run the cadence, phased to end on the save's own frame, and the first frame takes
        // the rest. A run with no surplus (or no 60 Hz frame at the save) is spread evenly instead.
        const double SixtyHertzUnits = 128.0 / 15;
        var plan = new List<double>();
        int cadenceFrames = anchor.Frames - 1;
        long cadenceSum = -1;
        double phase = 0;
        if (anchor.LastUnits is 8 or 9 && cadenceFrames > 0)
        {
            for (int p = 0; p < 15 && cadenceSum < 0; p++)
            {
                double candidate = p / 15.0;
                long before = (long)Math.Floor(candidate + ((cadenceFrames - 1) * SixtyHertzUnits) + 1e-9);
                long end = (long)Math.Floor(candidate + (cadenceFrames * SixtyHertzUnits) + 1e-9);
                if (end - before == anchor.LastUnits && anchor.Units - end >= 8)
                {
                    cadenceSum = end;
                    phase = candidate;
                }
            }
        }

        double rate = anchor.Units / (double)anchor.Frames / 32;
        if (cadenceSum >= 0)
        {
            plan.Add(SixtyHertzUnits / 32);
            plan.Add((anchor.Units - cadenceSum) / 32.0);
            long previous = 0;
            for (int k = 1; k <= cadenceFrames; k++)
            {
                long at = (long)Math.Floor(phase + (k * SixtyHertzUnits) + 1e-9);
                plan.Add((at - previous) / 32.0);
                previous = at;
            }

            return (plan, SixtyHertzUnits / 32, $"save-anchored {anchor.Frames} frames to TICKS {anchor.Units / 32.0:F3} (first {plan[1] * 32:F0}, cadence from phase {phase:F2}, last {anchor.LastUnits})");
        }

        // Spread evenly: floor(phase + k·units/frames), the phase chosen so the last frame is the save's
        // (a 61 fps run of 54 frames to 14.219 ends on 8, not the 9 of phase 0).
        plan.Add(rate);
        double evenRate = anchor.Units / (double)anchor.Frames;
        double evenPhase = 0;
        for (int p = 0; p < 15 && anchor.LastUnits > 0; p++)
        {
            double candidate = p / 15.0;
            long before = (long)Math.Floor(candidate + ((anchor.Frames - 1) * evenRate) + 1e-9);
            long end = (long)Math.Floor(candidate + (anchor.Frames * evenRate) + 1e-9);
            if (end - before == anchor.LastUnits)
            {
                evenPhase = candidate;
                break;
            }
        }

        long sum = (long)Math.Floor(evenPhase + 1e-9);
        for (int k = 1; k <= anchor.Frames; k++)
        {
            long at = (long)Math.Floor(evenPhase + (k * evenRate) + 1e-9);
            plan.Add((at - sum) / 32.0);
            sum = at;
        }

        return (plan, rate, $"save-anchored {anchor.Frames} even frames to TICKS {anchor.Units / 32.0:F3} (phase {evenPhase:F2}, last {anchor.LastUnits})");
    }

    private static double? StartupStamp(string path) =>
        File.ReadLines(path, Encoding.Latin1)
            .Where(static l => l.StartsWith("TCK=", StringComparison.Ordinal))
            .Select(static l => double.TryParse(l.AsSpan(4), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : (double?)null)
            .FirstOrDefault();

    /// <summary>
    /// The step into the first sampled cycle when the original's start-up stamp is one frame before
    /// it: the probe action starts a frame ahead of the first sample in most carriers, and that frame
    /// lands 8/32 or 9/32 of a tick before it, which the first cycles' rate does not tell apart.
    /// </summary>
    internal static double? StampStep(double? stamp, IReadOnlyList<double> cycleTicks)
    {
        if (cycleTicks.Count == 0 || stamp is null)
        {
            return null;
        }

        double step = OracleTrace.SnapTicks(cycleTicks[0]) - OracleTrace.SnapTicks(stamp.Value);
        return OracleTrace.IsFrameStep(step) ? step : null;
    }

    private static double? FirstValue(IReadOnlyList<OracleTraceLine> lines, OracleTimingColumn timing) =>
        lines.FirstOrDefault(timing.Matches)
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
        string timing = result.Timing is { } t ? $"timing {t} ({result.CycleTicks.Count} cycles)" : result.ClockNote ?? "fixed timing";
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
            sb.Append(", AUTOTYPE keys typed into INKEY, delivered in the original's R cycle (other key hooks not replayed)");
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

            // A run the original did not finish (no END line: DOSBox-X faulted, flic-scheduler-detail
            // missing-during) leaves a trace the replay goes on past; every original line matching is all
            // there is to compare.
            bool originalEnded = result.OriginalFiles.TryGetValue("OVEND0.TXT", out string? endFile) && new FileInfo(endFile).Length > 0;
            if (!originalEnded && comparison.OriginalLines > 0 && comparison.MatchingLines == comparison.OriginalLines && comparison.ReplayLines > comparison.OriginalLines)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {name}: match ({comparison.OriginalLines} lines; the original's run ended there, the replay has {comparison.ReplayLines})");
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
        string work = Path.Combine(result.Carrier, "work");
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
            else if (CompareSaveSections(a, b) is { } sections)
            {
                bool matches = sections.Differing.Count == 0;
                string set = sections.SetAside.Count == 0 ? "" : $"; set aside, not modelled: {string.Join(", ", sections.SetAside)}";
                string line = matches
                    ? $"  {name}: match ({a.Length}-byte body{set})"
                    : $"  {name}: DIFFERS in {string.Join("; ", sections.Differing)}{set}";
                sb.AppendLine(line);
                ok &= matches;
            }
            else
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {name}: DIFFERS at body byte {first} ({a.Length} original, {b.Length} replay)");
                ok = false;
            }
        }

        return ok;
    }

    /// <summary>The sections of a full save, as OV-20261029-save-body names them.</summary>
    private static readonly string[] SaveSectionNames =
        ["first", "SAVE textures", "SAVE regions", "objects", "screen elements", "5", "slots", "synonyms", "skills", "actions", "10", "11", "12"];

    /// <summary>
    /// Engine skills a save holds that OpenVirtue does not model: the renderer's run-time statistics and
    /// the random generator's last value (save-body round 16).
    /// </summary>
    private static readonly HashSet<string> UnmodelledSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "TIME_TOTAL", "TIME_FBUFFER", "TIME_DRAW", "TIME_RENDER", "TIME_SLICES", "TIME_ACTIONS", "TIME_SOUND",
        "VISIBLES", "ACTIONS", "WAIT_ACTIONS", "ACTIVE_NEXUS", "ACTIVE_TARGETS", "ACTIVE_OBJECTS", "ACTIVE_OBJTICKS",
        "ACTIVE_REGTICKS", "MAX_DIST", "VESA_MODE", "RANDOM",
    };

    /// <summary>
    /// Compares two full save bodies section by section: the sections and skills that differ, and what
    /// was set aside: the first section's words at +36 and +40, which follow <c>TICKS</c> at the save
    /// (round 20) and so differ wherever the replay's clock does, and <see cref="UnmodelledSkills"/>.
    /// Null when either body has no sections.
    /// </summary>
    internal static (List<string> Differing, List<string> SetAside)? CompareSaveSections(byte[] a, byte[] b)
    {
        if (AcknexSaveFile.ReadSections(a) is not { } sa || AcknexSaveFile.ReadSections(b) is not { } sb ||
            sa.Count != sb.Count)
        {
            return null;
        }

        var differing = new List<string>();
        var setAside = new List<string>();
        if (!a.AsSpan(0, sa[0].Payload).SequenceEqual(b.AsSpan(0, sb[0].Payload)))
        {
            differing.Add("the header");
        }

        for (int i = 0; i < sa.Count; i++)
        {
            ReadOnlySpan<byte> x = a.AsSpan(sa[i].Payload, sa[i].Size);
            ReadOnlySpan<byte> y = b.AsSpan(sb[i].Payload, sb[i].Size);
            string section = $"section {i} ({(i < SaveSectionNames.Length ? SaveSectionNames[i] : "?")})";
            if (x.SequenceEqual(y))
            {
                continue;
            }

            if (i == 0 && x.Length == y.Length && x.Length >= 44 && x[..36].SequenceEqual(y[..36]) && x[44..].SequenceEqual(y[44..]))
            {
                setAside.Add("section 0 +36/+40");
            }
            else if (i == 3 && x.Length == y.Length && WallPointersAside(x, y, out byte[] mx, out byte[] my))
            {
                // Each wall record's +28/+30 is a far pointer into the runtime's heap (−23568, 90 for the
                // first wall of the probe's room in every save), the same in every launch and nothing
                // the replay models.
                setAside.Add("walls' +28..+39 and unplaced objects' +36 pointers");
                if (!mx.AsSpan().SequenceEqual(my))
                {
                    int at = mx.AsSpan().CommonPrefixLength(my) & ~1;
                    differing.Add($"{section} at +{at} ({Words(mx, at)} / {Words(my, at)})");
                }
            }
            else if (i == 8)
            {
                List<string> skills = CompareSkills(a, b, setAside);
                if (skills.Count > 0)
                {
                    differing.Add($"{section}: {string.Join(" ", skills.Take(12))}{(skills.Count > 12 ? $" and {skills.Count - 12} more" : "")}");
                }
            }
            else if (x.Length == y.Length)
            {
                int at = x.CommonPrefixLength(y) & ~1;
                differing.Add($"{section} at +{at} ({Words(x, at)} / {Words(y, at)})");
            }
            else
            {
                differing.Add($"{section} ({x.Length} original, {y.Length} replay bytes)");
            }
        }

        return (differing, setAside);
    }

    /// <summary>
    /// Copies an object section with each wall record's +28..+39 zeroed, when the section's size splits
    /// into 170-byte entity and 178-byte wall records one way; false otherwise.
    /// </summary>
    private static bool WallPointersAside(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y, out byte[] mx, out byte[] my)
    {
        mx = x.ToArray();
        my = y.ToArray();
        (int Entities, int Walls)? split = null;
        for (int entities = 0; entities * 170 <= x.Length; entities++)
        {
            int rest = x.Length - (entities * 170);
            if (rest % 178 == 0)
            {
                if (split is not null)
                {
                    return false;
                }

                split = (entities, rest / 178);
            }
        }

        if (split is not { } s || s.Walls == 0)
        {
            return false;
        }

        for (int i = 0; i < s.Walls; i++)
        {
            // The pointer at +28/+30, the same again at +32/+34 from the third wall on, and an unplaced
            // wall's at +36/+38 (round 13 unplaced: 25792, 91).
            int at = (s.Entities * 170) + (i * 178) + 28;
            Array.Clear(mx, at, 12);
            Array.Clear(my, at, 12);
        }

        // An unplaced thing's or actor's +36/+38 holds such a pointer too, where a placed one's HEIGHT
        // is (round 13 unplaced: 25792, 91 in the thing's record): a segment word within one of the
        // walls' pointers' (90), against the replay's 0.
        var segments = new HashSet<int>();
        for (int i = 0; i < s.Walls; i++)
        {
            for (int word = 28; word <= 36; word += 4)
            {
                int at = (s.Entities * 170) + (i * 178) + word;
                if (BitConverter.ToInt32(x[at..]) != 0)
                {
                    segments.Add(BitConverter.ToUInt16(x[(at + 2)..]));
                }
            }
        }

        for (int i = 0; i < s.Entities; i++)
        {
            int at = (i * 170) + 36;
            int high = BitConverter.ToUInt16(x[(at + 2)..]);
            if (segments.Any(segment => Math.Abs(high - segment) <= 1) && BitConverter.ToInt32(y[at..]) == 0 && BitConverter.ToInt32(x[at..]) != 0)
            {
                Array.Clear(mx, at, 4);
                Array.Clear(my, at, 4);
            }
        }

        return true;
    }

    /// <summary>Up to four little-endian 16-bit words of a section from <paramref name="at"/>, for the diff's eye.</summary>
    private static string Words(ReadOnlySpan<byte> section, int at)
    {
        var words = new List<string>();
        for (int i = at; i + 1 < section.Length && words.Count < 4; i += 2)
        {
            words.Add(BitConverter.ToInt16(section.Slice(i, 2)).ToString(CultureInfo.InvariantCulture));
        }

        return string.Join(' ', words);
    }

    private static List<string> CompareSkills(byte[] a, byte[] b, List<string> setAside)
    {
        IReadOnlyList<SaveSkill> x = AcknexSaveFile.ReadSkills(a);
        IReadOnlyList<SaveSkill> y = AcknexSaveFile.ReadSkills(b);
        if (!x.Select(static k => k.Name).SequenceEqual(y.Select(static k => k.Name), StringComparer.OrdinalIgnoreCase))
        {
            return ["(the skill list's names or order)"];
        }

        var differing = new List<string>();
        for (int i = 0; i < x.Count; i++)
        {
            if (x[i] == y[i] || (x[i].Name.Equals(y[i].Name, StringComparison.OrdinalIgnoreCase) && x[i] with { Name = y[i].Name } == y[i]))
            {
                continue;
            }

            if (UnmodelledSkills.Contains(x[i].Name))
            {
                setAside.Add(x[i].Name);
            }
            else
            {
                differing.Add(string.Create(CultureInfo.InvariantCulture, $"{x[i].Name} {x[i].RawValue / 1024.0:0.###}/{y[i].RawValue / 1024.0:0.###}"));
            }
        }

        return differing;
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
        IReadOnlyDictionary<string, string> ReplayFiles,
        string Carrier,
        string? ClockNote = null);

    /// <summary>
    /// The parts of a carrier's <c>case.json</c> the replay needs. A carrier may name its timing column
    /// (<c>"timing": "TK:1"</c>) when detection would pick the wrong one: the flic carriers print
    /// <c>FLIC_FRAME</c>, whole and never falling, before <c>TICKS</c>. It may name the first sampled
    /// cycle's step (<c>"startup_step": 0.28125</c>) where the samples cannot show it: a trace sampled
    /// only during a flic gives 1.094-tick gaps, though its start-up ran 60 Hz frames. It may ask for
    /// fixed frames (<c>"time_corr": 0.28125</c>) when no column prints <c>TICKS</c> and detection
    /// would take a coordinate for it. It may give where the original's seconds clock stood at the first
    /// cycle (<c>"seconds_offset": 1.3</c>, ticks), read off its <c>SECS</c> or <c>EACH_SEC</c> lines, which
    /// differs from launch to launch (OV-20261103-contact-clock).
    /// </summary>
    private sealed record CarrierCase(
        string? TraceFile, bool ScriptedInput, string? Autotype = null, OracleTimingColumn? Timing = null, double? StartupStep = null,
        double? TimeCorrection = null, double? SecondsOffset = null)
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
            OracleTimingColumn? timing = root.TryGetProperty("timing", out JsonElement tm) && tm.ValueKind == JsonValueKind.String &&
                OracleTimingColumn.TryParse(tm.GetString() ?? "", out OracleTimingColumn parsed)
                ? parsed
                : null;
            double? startup = root.TryGetProperty("startup_step", out JsonElement st) && st.ValueKind == JsonValueKind.Number
                ? st.GetDouble()
                : null;
            double? timeCorrection = root.TryGetProperty("time_corr", out JsonElement tc) && tc.ValueKind == JsonValueKind.Number
                ? tc.GetDouble()
                : null;
            double? secondsOffset = root.TryGetProperty("seconds_offset", out JsonElement so) && so.ValueKind == JsonValueKind.Number
                ? so.GetDouble()
                : null;
            return new CarrierCase(trace, autotype is not null || click, autotype, timing, startup, timeCorrection, secondsOffset);
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
        string? Error,
        bool DumpClock = false)
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
            bool dumpClock = false;
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
                    case "--dump-clock":
                        dumpClock = true;
                        i--; // takes no value
                        break;
                    default:
                        return new ReplayOptions(null, null, 0, 0, 0, 0, 0, $"Unknown or malformed option '{args[i]}'.");
                }

                i++;
            }

            return new ReplayOptions(timing, fixedCorrection, tolerance, extraTicks, maxTicks, maxFrames, context, null, dumpClock);
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
    internal sealed class ReplayClock(IReadOnlyList<double> cycleTicks, double fallbackTicks, double? firstStep = null, bool consecutive = false, IReadOnlyList<double>? plannedStartup = null)
    {
        private const double Epsilon = 0.0005;
        private readonly double _typical = Median(cycleTicks) ?? fallbackTicks;

        // The shortest sampled cycle: a counter that counts every cycle has frame-long ones somewhere
        // in the run, even where its rate changed and the median step grew (border-climb ceil-4 ran
        // at 8 ticks a cycle, then at 60 Hz).
        private readonly double _shortest = ShortestGap(cycleTicks) ?? fallbackTicks;

        // DOSBox-X's rate drifts within a run, so the start-up runs at the first sampled cycles' rate,
        // or at the step the first sample's own TIME_CORR column gives (FirstCycleStep).
        private readonly double _startup = firstStep ?? (cycleTicks.Count >= 3 ? Median([cycleTicks[0], cycleTicks[1], cycleTicks[2]]) : null) ?? Median(cycleTicks) ?? fallbackTicks;

        // Past the last sample the frames continue the last samples' cadence (at 60 Hz the original's
        // 8/9 thirty-seconds pattern), or the last five steps' median where no cadence fits them.
        private readonly OracleCadence? _tail = OracleTrace.FitCadence(cycleTicks);
        private readonly double _tailStep = RecentMedian(cycleTicks, 5) ?? Median(cycleTicks) ?? fallbackTicks;

        // Before the first sample the frames run the first samples' cadence backward, cycle 1 the
        // earliest of it past TICKS 0 (OV-20261103-startup-clock).
        private readonly IReadOnlyList<double>? _startupFrames = plannedStartup ?? OracleTrace.StartupFrames(cycleTicks, firstStep);
        private int _startupFrame;
        private int _tailFrames;
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
            if (!Anchored && _startupFrames is { } planned && _startupFrame < planned.Count)
            {
                return planned[_startupFrame++] / Timing.TicksPerSecond;
            }

            if (!Anchored)
            {
                // Where no cadence fits the first samples, the original's start-up frames are unknown. Run steady
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
                // A gap of more than a few typical steps is a sampler that skipped cycles, unless a
                // counter placed every sample one frame apart (its shortest step a frame's, at most a
                // tick; a counter of WAITT 8 loops steps 8): then it is one long frame of the original's.
                double gap = cycleTicks[_next] + Offset - ticks;
                if (gap <= 4 * _typical || (consecutive && _shortest <= 1))
                {
                    step = gap;
                }
            }
            else if (cycleTicks.Count > 0)
            {
                _tailFrames++;
                step = _tail is { } tail ? tail.StepInto(_tailFrames) : _tailStep;
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

        private static double? ShortestGap(IReadOnlyList<double> ticks)
        {
            double? shortest = null;
            for (int i = 1; i < ticks.Count; i++)
            {
                double gap = ticks[i] - ticks[i - 1];
                if (gap > 0 && (shortest is null || gap < shortest))
                {
                    shortest = gap;
                }
            }

            return shortest;
        }

        /// <summary>The median of the last <paramref name="gaps"/> steps between samples.</summary>
        private static double? RecentMedian(IReadOnlyList<double> ticks, int gaps) =>
            ticks.Count < 2 ? null : Median([.. ticks.Skip(Math.Max(0, ticks.Count - gaps - 1))]);

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

    internal sealed class HeadlessWindow : IPlatformWindow
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

        /// <summary>
        /// The cycles from the line before the original's R line to it: the keys are delivered that many
        /// frames less one after the input opens, so it completes in the cycle the original's did.
        /// </summary>
        public int KeyDelayPolls { get; set; }

        /// <summary>The replay's frame, set by the loop (the host polls the keys more than once a frame).</summary>
        public int Frame { get; set; }

        private int _openedAt = -1;

        public bool BeginTextInput()
        {
            _openedAt = Frame;
            return true;
        }

        public IReadOnlyList<(InkeyKey Key, char Character)> ConsumeTextKeys()
        {
            if (_openedAt >= 0 && Frame - _openedAt < KeyDelayPolls - 1)
            {
                return [];
            }

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

    internal sealed class HeadlessInput : IInputSource
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
    internal sealed class TimedAudioSink : IAudioSink
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

    internal sealed class HeadlessRender : IRenderBackend
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
