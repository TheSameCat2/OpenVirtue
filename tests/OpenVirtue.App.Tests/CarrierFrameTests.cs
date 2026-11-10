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
            host.FrameClock = static () => 1.0 / 60;
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
