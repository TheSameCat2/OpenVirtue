// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.Text;
using OpenVirtue.Formats.Wrs;
using OpenVirtue.Host;
using OpenVirtue.Host.Tests.TestSupport;

namespace OpenVirtue.Host.Tests;

/// <summary>
/// Flic playback in the host, as measured in OV-20261015-flic-play-blocking: the action that
/// calls <c>PLAY_FLICFILE</c> goes on, a key during the flic runs its hook, <c>STOP_FLIC</c> ends
/// the flic, and <c>FLIC_FRAME</c> is the 1-based frame on screen and 0 when none plays; and in
/// OV-20261015-flic-scheduler-detail: one scheduler cycle runs per flic frame, with <c>TIME_CORR</c>
/// the frame delay in 64 ms units, and a missing file leaves <c>FLIC_FRAME</c> and a playing flic alone.
/// </summary>
public sealed class RuntimeHostFlicTests : IDisposable
{
    private const string Main = """
        VIDEO S640x480;
        MAPFILE <m.wmp>;
        BMAP m,<t.pcx>,0,0,1,1;
        TEXTURE wallTex { BMAPS m; }
        WALL w { TEXTURE wallTex; }
        SKILL after { VAL 0; }
        SKILL skipped { VAL 0; }
        ACTION start { PLAY_FLICFILE <A.FLC>; SET IF_ANYKEY, skip; SET after, 1; }
        ACTION skip { STOP_FLIC; SET IF_ANYKEY, NULL; SET skipped, 1; }
        IF_START start;
        """;

    private const string Map = """
        VERTEX 0 0 0;#0
        VERTEX 20 0 0;#1
        VERTEX 20 20 0;#2
        VERTEX 0 20 0;#3
        REGION room 0 10;#0
        WALL w 0 1 0 -1 0 0;#0
        WALL w 1 2 0 -1 0 0;#1
        WALL w 2 3 0 -1 0 0;#2
        WALL w 3 0 0 -1 0 0;#3
        PLAYER_START 5 5 0 0;#4
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ov-host-flic-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Boot_PlayFlicFile_ActionGoesOnAndTheFlicIsDrawn()
    {
        using RuntimeHost host = Open(frameDelayMs: 10_000, out _, out FakeRender render);
        host.Boot();

        Assert.Equal(1, host.Runtime.GetSkill("after"));
        Assert.Equal("skip", host.Runtime.ActionHooks["IF_ANYKEY"]);
        Assert.Equal(1, host.Runtime.GetSkill("FLIC_FRAME"));

        host.Frame(present: false);

        Assert.True(DrewFlic(render));
        Assert.Equal(1, host.Runtime.GetSkill("FLIC_FRAME"));
    }

    [Fact]
    public void Frame_FlicInATallWindow_FillsTheLetterboxedDisplayOnly()
    {
        // A flic drawn over the whole window left its edges in the letterbox, where nothing paints
        // later (the Cactus logo above and below Apathy's view).
        using RuntimeHost host = Open(frameDelayMs: 10_000, out _, out FakeRender render, window: new FakeWindow { Width = 600, Height = 900 });
        host.Boot();

        host.Frame(present: false);

        HostFrame frame = Assert.IsType<HostFrame>(render.LastFrame);
        DrawBatch flic = Assert.Single(frame.Batches, static batch => batch.DynamicImage is not null);
        Assert.Equal(new PixelRect(0, 225, 600, 450), flic.Viewport);
        Assert.Equal(new PixelRect(0, 225, 600, 450), frame.Display);
    }

    [Fact]
    public void Frame_KeyDuringFlic_RunsTheHookAndStopFlicEndsTheFlic()
    {
        using RuntimeHost host = Open(frameDelayMs: 10_000, out FakeInput input, out FakeRender render);
        host.Boot();
        host.Frame(present: false);
        Assert.True(DrewFlic(render));

        // The key hook runs in the next scheduler cycle, here the flic's second frame
        // (OV-20261029-scheduler-order round 6): let the frame clock bring it due.
        host.FrameClock = static () => 10;
        input.PressedKeys.Add(HostKey.Space);
        host.Frame(present: false);
        input.PressedKeys.Clear();

        Assert.Equal(1, host.Runtime.GetSkill("skipped"));
        Assert.Equal(1, host.Runtime.StopFlicCount);
        Assert.Equal(0, host.Runtime.GetSkill("FLIC_FRAME"));
        Assert.False(DrewFlic(render));
    }

    [Fact]
    public void Frame_DuringFlic_RunsOneSchedulerCyclePerFlicFrame()
    {
        // A 640 ms frame delay is 10 ticks in 64 ms units. Two quick host frames show frame 1 and
        // run its one cycle; the second frame is not due yet.
        const string main = """
            VIDEO S640x480;
            MAPFILE <m.wmp>;
            BMAP m,<t.pcx>,0,0,1,1;
            TEXTURE wallTex { BMAPS m; }
            WALL w { TEXTURE wallTex; }
            SKILL cycles { VAL 0; }
            ACTION start { PLAY_FLICFILE <A.FLC>; SET EACH_TICK.1, count; }
            ACTION count { RULE cycles += 1; }
            IF_START start;
            """;
        using RuntimeHost host = Open(frameDelayMs: 640, out _, out FakeRender render, main);
        host.Boot();
        double ticks = host.Runtime.GetSkill("TICKS");

        host.Frame(present: false);
        host.Frame(present: false);

        // The first frame is the IF_START cycle (round 2 of the probe), the second the flic's first.
        Assert.True(DrewFlic(render));
        Assert.Equal(2, host.Runtime.GetSkill("cycles"));
        Assert.Equal(1, host.Runtime.GetSkill("FLIC_FRAME"));
        Assert.Equal(10, host.Runtime.GetSkill("TIME_CORR"), 9);
        Assert.Equal(ticks + 10, host.Runtime.GetSkill("TICKS"), 9);
    }

    [Fact]
    public void Frame_FlicStartedInIfStart_KeepsTheStartupCycleAndPutsTheFlicsCyclesOnItsGrid()
    {
        // OV-20261015-flic-play-blocking round 2 (start-flic, a 70 ms flic from IF_START): the IF_START
        // cycle reads TIME_CORR 1 and TICKS 0 with frame 1 on screen; the first flic cycle took the
        // host frame that loaded the flic (1.375 ticks) and the second came back on the grid (2.188),
        // then 1.094 a cycle.
        const string main = """
            VIDEO S640x480;
            MAPFILE <m.wmp>;
            BMAP m,<t.pcx>,0,0,1,1;
            TEXTURE wallTex { BMAPS m; }
            WALL w { TEXTURE wallTex; }
            SKILL cycles { VAL 0; }
            SKILL first { VAL -1; }
            ACTION start { PLAY_FLICFILE <A.FLC>; SET EACH_TICK.1, count; }
            ACTION count { RULE cycles += 1; IF (first < 0) { RULE first = FLIC_FRAME + (10 * TIME_CORR) + (100 * TOTAL_FRAMES); } }
            IF_START start;
            """;
        using RuntimeHost host = Open(frameDelayMs: 70, out _, out _, main);
        host.Boot();
        // The Boot frame runs the IF_START cycle; the frame that loaded the flic is the next one's length.
        double[] frames = [0.281 / 16, 1.375 / 16, 0.813 / 16, 1.094 / 16];
        int frame = 0;
        host.FrameClock = () => frames[Math.Min(frame++, frames.Length - 1)];
        var ticks = new List<double>();
        var corrections = new List<double>();
        for (int i = 0; i < 4; i++)
        {
            host.Frame(present: false);
            ticks.Add(host.Runtime.GetSkill("TICKS"));
            corrections.Add(host.Runtime.GetSkill("TIME_CORR"));
        }

        Assert.Equal(11, host.Runtime.GetSkill("first")); // frame 1, TIME_CORR 1, TOTAL_FRAMES 0 in the IF_START cycle
        Assert.Equal(4, host.Runtime.GetSkill("cycles"));
        Assert.Equal([0, 1.375, 2.188, 3.282], ticks.Select(static t => Math.Round(t, 3)));
        Assert.Equal([1, 1.375, 0.813, 1.094], corrections.Select(static c => Math.Round(c, 3)));
        Assert.Equal(3, host.Runtime.GetSkill("FLIC_FRAME"));
    }

    [Fact]
    public void Boot_PlayFlicFileOfMissingFile_KeepsFlicFrameAtZeroAndDrawsTheWorld()
    {
        const string main = """
            VIDEO S640x480;
            MAPFILE <m.wmp>;
            BMAP m,<t.pcx>,0,0,1,1;
            TEXTURE wallTex { BMAPS m; }
            WALL w { TEXTURE wallTex; }
            SKILL seen { VAL -1; }
            SKILL after { VAL 0; }
            ACTION start { PLAY_FLICFILE <NONE.FLC>; SET seen, FLIC_FRAME; SET after, 1; }
            IF_START start;
            """;
        using RuntimeHost host = Open(frameDelayMs: 10_000, out _, out FakeRender render, main);
        host.Boot();
        host.Frame(present: false);

        Assert.Equal(0, host.Runtime.GetSkill("seen"));
        Assert.Equal(1, host.Runtime.GetSkill("after"));
        Assert.Equal(0, host.Runtime.GetSkill("FLIC_FRAME"));
        Assert.False(DrewFlic(render));
    }

    [Fact]
    public void Frame_PlayFlicFileOfMissingFileDuringFlic_KeepsThePlayingFlic()
    {
        const string main = """
            VIDEO S640x480;
            MAPFILE <m.wmp>;
            BMAP m,<t.pcx>,0,0,1,1;
            TEXTURE wallTex { BMAPS m; }
            WALL w { TEXTURE wallTex; }
            SKILL seen { VAL -1; }
            ACTION start { PLAY_FLICFILE <A.FLC>; SET IF_ANYKEY, missing; }
            ACTION missing { PLAY_FLICFILE <NONE.FLC>; SET seen, FLIC_FRAME; SET IF_ANYKEY, NULL; }
            IF_START start;
            """;
        using RuntimeHost host = Open(frameDelayMs: 10_000, out FakeInput input, out FakeRender render, main);
        host.Boot();
        host.Frame(present: false);

        // The Boot frame is the IF_START cycle; the hook runs in the flic's first (scheduler-order round 6).
        host.FrameClock = static () => 10;
        input.PressedKeys.Add(HostKey.Space);
        host.Frame(present: false);
        input.PressedKeys.Clear();

        Assert.Equal(1, host.Runtime.GetSkill("seen"));
        Assert.Equal(1, host.Runtime.GetSkill("FLIC_FRAME"));
        Assert.True(DrewFlic(render));
    }

    [Fact]
    public void Frame_FlicRunsOut_FlicFrameGoesBackToZeroAndTheWorldIsDrawn()
    {
        using RuntimeHost host = Open(frameDelayMs: 1, out _, out FakeRender render);
        host.Boot();
        host.Frame(present: false); // the IF_START cycle
        Thread.Sleep(50);

        host.Frame(present: false);

        Assert.Equal(0, host.Runtime.GetSkill("FLIC_FRAME"));
        Assert.Equal(0, host.Runtime.StopFlicCount);
        Assert.False(DrewFlic(render));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private RuntimeHost Open(int frameDelayMs, out FakeInput input, out FakeRender render, string main = Main, FakeWindow? window = null)
    {
        Directory.CreateDirectory(_directory);
        string archive = Path.Combine(_directory, "TITLE.WRS");
        WrsArchive.WriteFile(
            archive,
            [
                new WrsFile("TITLE.WDL", Encoding.ASCII.GetBytes(main)),
                new WrsFile("m.wmp", Encoding.ASCII.GetBytes(Map)),
                new WrsFile("A.FLC", TinyFlc(frameDelayMs)),
            ]);
        input = new FakeInput();
        render = new FakeRender();
        return RuntimeHost.OpenArchive(archive, "TITLE.WDL", window ?? new FakeWindow(), input, new NullAudioSink(), render);
    }

    private static bool DrewFlic(FakeRender render) =>
        render.LastFrame is { } frame && frame.Batches.Any(static batch => batch.DynamicImage is not null);

    /// <summary>A 4x1, three-frame FLC: a palette and byte-run first frame, then two empty frames.</summary>
    private static byte[] TinyFlc(int frameDelayMs)
    {
        byte[] palette = new byte[4 + (256 * 3)];
        BinaryPrimitives.WriteUInt16LittleEndian(palette, 1); // one packet, skip 0, count 0 = 256
        palette[4 + 3] = 255; // colour 1 is red
        byte[] byteRun = [1, 4, 1]; // one packet: repeat colour 1 four times
        byte[][] frames = [Frame(Chunk(4, palette), Chunk(15, byteRun)), Frame(), Frame()];

        int size = 128 + frames.Sum(static f => f.Length);
        byte[] data = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)size);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 0xAF12);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), (ushort)frames.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), (uint)frameDelayMs);
        int offset = 128;
        foreach (byte[] frame in frames)
        {
            frame.CopyTo(data, offset);
            offset += frame.Length;
        }

        return data;
    }

    private static byte[] Frame(params byte[][] chunks)
    {
        int size = 16 + chunks.Sum(static c => c.Length);
        byte[] frame = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)size);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), 0xF1FA);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6), (ushort)chunks.Length);
        int offset = 16;
        foreach (byte[] chunk in chunks)
        {
            chunk.CopyTo(frame, offset);
            offset += chunk.Length;
        }

        return frame;
    }

    private static byte[] Chunk(ushort type, byte[] payload)
    {
        byte[] chunk = new byte[6 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(chunk, (uint)chunk.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(4), type);
        payload.CopyTo(chunk, 6);
        return chunk;
    }
}
