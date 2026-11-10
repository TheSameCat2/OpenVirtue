// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.App.Platform;
using OpenVirtue.App.Rendering;
using OpenVirtue.Formats.Wrs;
using OpenVirtue.Host;
using Xunit.Abstractions;

namespace OpenVirtue.App.Tests;

/// <summary>
/// Runs oracle carriers through the real GL host on a fixed 60 Hz clock, and writes each
/// <c>SCREENSHOT</c> they take, to compare with the PCX the original saved. A local measuring tool,
/// not a check: it does nothing unless <c>OV_CARRIER_FRAMES</c> names probe directories
/// (<c>;</c>-separated), and <c>OV_CARRIER_OUT</c> says where the frames go.
/// </summary>
[Collection(GlTestGroup.Name)]
public sealed class CarrierFrameTests
{
    private const int FrameBudget = 6000;

    private readonly ITestOutputHelper _output;

    public CarrierFrameTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Carriers_ListedByTheEnvironment_WriteTheirScreenshots()
    {
        string? probes = Environment.GetEnvironmentVariable("OV_CARRIER_FRAMES");
        if (string.IsNullOrEmpty(probes))
        {
            return;
        }

        string output = Environment.GetEnvironmentVariable("OV_CARRIER_OUT") ?? Path.Combine(Path.GetTempPath(), "ov-carrier-frames");
        foreach (string probe in probes.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string carrier in Directory.EnumerateDirectories(probe).Order(StringComparer.Ordinal))
            {
                string work = Path.Combine(carrier, "work");
                if (!File.Exists(Path.Combine(work, "TITLE.WDL")) || !Directory.EnumerateFiles(work, "OVEND*").Any())
                {
                    continue;
                }

                // A carrier's EXIT closes the window, so each one opens its own.
                if (!GlContext.TryOpen(640, 480, "OpenVirtue carriers", out SdlPlatform? platform, out GlRenderBackend? graphics, out string? error) ||
                    platform is null ||
                    graphics is null)
                {
                    _output.WriteLine($"SKIP: no GL context ({error})");
                    return;
                }

                using (platform)
                using (graphics)
                {
                    Run(work, Path.Combine(output, Path.GetFileName(probe.TrimEnd('/')), Path.GetFileName(carrier)), platform, graphics);
                }
            }
        }
    }

    private static bool RanAtSlowFrames(string work)
    {
        string caseJson = Path.Combine(Path.GetDirectoryName(work) ?? work, "case.json");
        string? traceFile = null;
        if (File.Exists(caseJson))
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(caseJson));
            if (document.RootElement.TryGetProperty("trace_file", out System.Text.Json.JsonElement trace) &&
                trace.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                traceFile = trace.GetString();
            }
        }

        string? path = traceFile is null ? null : Directory.EnumerateFiles(work)
            .FirstOrDefault(file => Path.GetFileName(file).Equals(traceFile, StringComparison.OrdinalIgnoreCase));
        return path is not null &&
            OracleTrace.RanAtSlowFrames(OracleTrace.Parse(File.ReadAllText(path, System.Text.Encoding.Latin1)));
    }

    private void Run(string work, string target, SdlPlatform platform, GlRenderBackend graphics)
    {
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        Directory.CreateDirectory(target);
        string archive = Path.Combine(target, "TITLE.WRS");
        WrsArchive.WriteFile(
            archive,
            Directory.EnumerateFiles(work)
                .Where(static file => !Path.GetFileName(file).StartsWith("OV", StringComparison.OrdinalIgnoreCase) &&
                    !Path.GetExtension(file).Equals(".EXE", StringComparison.OrdinalIgnoreCase))
                .Select(static file => new WrsFile(Path.GetFileName(file), File.ReadAllBytes(file))));
        try
        {
            using RuntimeHost host = RuntimeHost.OpenArchive(archive, "TITLE.WDL", platform, new RetailSmokeTests.ScriptedInput(), new NullAudioSink(), graphics);
            // A carrier the original ran at 8 ticks a frame throughout is run so too, as `ovtool oracle
            // diff` replays it, so its SCREENSHOT comes in the same cycle.
            double frameSeconds = RanAtSlowFrames(work) ? OracleTrace.SlowFrameTicks / 16 : 1.0 / 60;
            host.FrameClock = () => frameSeconds;
            host.Boot();
            int frames = 0;
            while (!host.ExitRequested && frames < FrameBudget)
            {
                host.Frame(present: false);
                frames++;
            }

            _output.WriteLine($"{target}: {frames} frames");
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or ArgumentException)
        {
            _output.WriteLine($"{target}: {ex.GetType().Name} {ex.Message}");
        }
    }
}
