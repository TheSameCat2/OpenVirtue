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
/// the flic, and <c>FLIC_FRAME</c> is the 1-based frame on screen and 0 when none plays.
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
    public void Frame_KeyDuringFlic_RunsTheHookAndStopFlicEndsTheFlic()
    {
        using RuntimeHost host = Open(frameDelayMs: 10_000, out FakeInput input, out FakeRender render);
        host.Boot();
        host.Frame(present: false);
        Assert.True(DrewFlic(render));

        input.PressedKeys.Add(HostKey.Space);
        host.Frame(present: false);
        input.PressedKeys.Clear();

        Assert.Equal(1, host.Runtime.GetSkill("skipped"));
        Assert.Equal(1, host.Runtime.StopFlicCount);
        Assert.Equal(0, host.Runtime.GetSkill("FLIC_FRAME"));
        Assert.False(DrewFlic(render));
    }

    [Fact]
    public void Frame_FlicRunsOut_FlicFrameGoesBackToZeroAndTheWorldIsDrawn()
    {
        using RuntimeHost host = Open(frameDelayMs: 1, out _, out FakeRender render);
        host.Boot();
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

    private RuntimeHost Open(int frameDelayMs, out FakeInput input, out FakeRender render)
    {
        Directory.CreateDirectory(_directory);
        string archive = Path.Combine(_directory, "TITLE.WRS");
        WrsArchive.WriteFile(
            archive,
            [
                new WrsFile("TITLE.WDL", Encoding.ASCII.GetBytes(Main)),
                new WrsFile("m.wmp", Encoding.ASCII.GetBytes(Map)),
                new WrsFile("A.FLC", TinyFlc(frameDelayMs)),
            ]);
        input = new FakeInput();
        render = new FakeRender();
        return RuntimeHost.OpenArchive(archive, "TITLE.WDL", new FakeWindow(), input, new NullAudioSink(), render);
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
