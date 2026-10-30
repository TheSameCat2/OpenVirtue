// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Diagnostics;
using System.Globalization;

/// <summary>
/// The rest of the oracle workflow around <see cref="OracleReplay"/>, so a probe goes from its case
/// table to a verdict with one tool:
/// <list type="bullet">
/// <item><c>oracle run &lt;dir&gt;</c> launches every prepared carrier under a folder that has not run
/// yet (all of them with <c>--rerun</c>), one after another, through its own launcher
/// (<c>run.sh</c>, or <c>run.bat</c> on Windows), with SDL's dummy video driver unless
/// <c>--visible</c>: with a visible window DOSBox-X can drop to two engine frames a second
/// (OV-20261029-audio).</item>
/// <item><c>oracle check &lt;probe&gt; &lt;output-dir&gt; [prepare options]</c> prepares the probe's carriers, runs
/// them and diffs them.</item>
/// <item><c>oracle summary [oracle-runs-dir]</c> lists, for every probe folder, how many run carriers
/// OpenVirtue's replay matches.</item>
/// </list>
/// The first DOSBox-X launch of a session still needs the maintainer's go (see the oracle protocol).
/// </summary>
internal static class OracleRunner
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("oracle run requires a carrier or probe directory.");
            return 2;
        }

        bool rerun = args.Contains("--rerun");
        bool visible = args.Contains("--visible");
        int timeout = 150;
        int at = Array.IndexOf(args, "--timeout");
        if (at >= 0 && (at + 1 >= args.Length || !int.TryParse(args[at + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out timeout)))
        {
            Console.Error.WriteLine("--timeout takes a number of seconds.");
            return 2;
        }

        List<string> carriers = OracleReplay.FindCarriers(args[0]);
        if (carriers.Count == 0)
        {
            Console.Error.WriteLine($"No carrier under {args[0]}.");
            return 1;
        }

        int failed = 0;
        foreach (string carrier in carriers)
        {
            if (!rerun && OracleReplay.HasOriginalOutputs(carrier))
            {
                continue;
            }

            int code = Launch(carrier, timeout, visible);
            Console.WriteLine($"{Path.GetFileName(carrier)}: rc {code}");
            failed += code == 0 ? 0 : 1;
        }

        return failed == 0 ? 0 : 1;
    }

    public static int Check(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("oracle check requires a probe name and an output directory.");
            return 2;
        }

        int prepared = OracleProbeGenerator.Prepare(args);
        if (prepared != 0)
        {
            return prepared;
        }

        int ran = Run([args[1]]);
        int diffed = OracleReplay.Run("diff", [args[1]]);
        return ran != 0 ? ran : diffed;
    }

    public static int Summary(string[] args)
    {
        string root = args.Length > 0 ? args[0] : Path.Combine("_research", "oracle-runs");
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"No oracle runs at {root}.");
            return 1;
        }

        int ranTotal = 0;
        int matchedTotal = 0;
        foreach (string probe in Directory.EnumerateDirectories(root, "OV-*").Order(StringComparer.Ordinal))
        {
            (int ran, int matched) = OracleReplay.Tally(probe);
            if (ran == 0)
            {
                continue;
            }

            ranTotal += ran;
            matchedTotal += matched;
            Console.WriteLine($"{Path.GetFileName(probe),-48} {matched,4}/{ran,-4} match");
        }

        Console.WriteLine($"{"all probes",-48} {matchedTotal,4}/{ranTotal,-4} match");
        return 0;
    }

    private static int Launch(string carrier, int timeoutSeconds, bool visible)
    {
        bool windows = OperatingSystem.IsWindows();
        string launcher = Path.Combine(carrier, windows ? "run.bat" : "run.sh");
        if (!File.Exists(launcher))
        {
            Console.Error.WriteLine($"{carrier}: no {Path.GetFileName(launcher)}");
            return 127;
        }

        var start = new ProcessStartInfo(windows ? "cmd.exe" : "sh") { UseShellExecute = false };
        if (windows)
        {
            start.ArgumentList.Add("/c");
        }

        start.ArgumentList.Add(launcher);
        start.Environment["OV_TIMEOUT"] = timeoutSeconds.ToString(CultureInfo.InvariantCulture);
        if (!visible)
        {
            start.Environment["SDL_VIDEODRIVER"] = "dummy";
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {launcher}.");
        process.WaitForExit();
        return process.ExitCode;
    }
}
