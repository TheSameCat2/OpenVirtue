// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text;
using OpenVirtue.Formats.Wrs;
using OpenVirtue.Host;
using OpenVirtue.Host.Tests.TestSupport;

namespace OpenVirtue.Host.Tests;

/// <summary>
/// <see cref="RuntimeHost.FrameClock"/> replaces the wall clock, so the oracle replay can feed the
/// original's per-cycle tick steps, and <c>PRINT_*</c> output lands where the replay reads it.
/// </summary>
public sealed class RuntimeHostFrameClockTests : IDisposable
{
    private const string Map = """
        VERTEX 0 0 0;#0
        VERTEX 40 0 0;#1
        VERTEX 40 40 0;#2
        VERTEX 0 40 0;#3
        REGION room 0 10;#0
        WALL w 0 1 0 -1 0 0;#0
        WALL w 1 2 0 -1 0 0;#1
        WALL w 2 3 0 -1 0 0;#2
        WALL w 3 0 0 -1 0 0;#3
        PLAYER_START 5 5 0 0;#4
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ov-host-frame-clock-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Frame_WithFrameClock_UsesItsDurations()
    {
        using RuntimeHost host = Open("ACTION start { }");
        var steps = new Queue<double>([0.25, 0.28125, 0.25]);
        host.FrameClock = () => steps.Dequeue() / 16;
        host.Boot();

        // The first cycle keeps TIME_CORR 1 and TICKS 0 (OV-20261029-scheduler-order).
        host.Frame(present: false);
        Assert.Equal(1, host.Runtime.GetSkill("TIME_CORR"), 6);
        Assert.Equal(0, host.Runtime.GetSkill("TICKS"), 6);
        host.Frame(present: false);
        Assert.Equal(0.28125, host.Runtime.GetSkill("TIME_CORR"), 6);
        host.Frame(present: false);

        Assert.Equal(0.53125, host.Runtime.GetSkill("TICKS"), 6);
    }

    [Fact]
    public void Frame_PrintInstructions_WriteTheDosFileNameAndLayout()
    {
        using RuntimeHost host = Open("""
            SKILL n { VAL 0; }
            ACTION tick { RULE n = n + 1; PRINT_STRING "C "; PRINT_VALUE n; PRINT_STRING "\n"; }
            ACTION start { PRINTFILE "ovtrc", 0; PRINT_STRING "BEGIN\n"; SET EACH_TICK.1, tick; }
            """);
        host.FrameClock = () => 1.0 / 16;
        host.Boot();
        host.Frame(present: false);
        host.Frame(present: false);

        string trace = File.ReadAllText(Path.Combine(_directory, "openvirtue-prints", "TITLE", "OVTRC0.TXT"));
        Assert.Equal("BEGIN\nC 1.000\nC 2.000\n", trace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private RuntimeHost Open(string body)
    {
        Directory.CreateDirectory(_directory);
        string archive = Path.Combine(_directory, "TITLE.WRS");
        string wdl = """
            VIDEO S640x480;
            MAPFILE <m.wmp>;
            BMAP m,<t.pcx>,0,0,1,1;
            TEXTURE wallTex { BMAPS m; }
            WALL w { TEXTURE wallTex; }
            REGION room { }
            IF_START start;

            """ + body;
        WrsArchive.WriteFile(
            archive,
            [
                new WrsFile("TITLE.WDL", Encoding.ASCII.GetBytes(wdl)),
                new WrsFile("m.wmp", Encoding.ASCII.GetBytes(Map)),
            ]);
        return RuntimeHost.OpenArchive(archive, "TITLE.WDL", new FakeWindow(), new FakeInput(), new NullAudioSink(), new FakeRender());
    }
}
