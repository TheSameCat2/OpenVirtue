// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using OpenVirtue.Formats.Wrs;
using OpenVirtue.Host.Tests.TestSupport;

namespace OpenVirtue.Host.Tests;

/// <summary>
/// OV-20261104-fade-pal: the screen shows the last declared palette at the start; FADE_PAL at 1 or
/// above makes the named palette current, a fraction blends toward it from the current palette, and 0
/// shows the current palette again.
/// </summary>
public sealed class RuntimeHostPaletteFadeTests : IDisposable
{
    private const string Map =
        "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nVERTEX 20 20 0;#2\nVERTEX 0 20 0;#3\n" +
        "REGION room 0 10;#0\n" +
        "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\nWALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\n" +
        "PLAYER_START 10 10 0 0 0;\n";

    private const string Main =
        "MAPFILE <m.wmp>;\n" +
        "PALETTE level_pal { PALFILE <LEVEL.PCX>; RANGE 16, 32; }\n" +
        "PALETTE black_pal { PALFILE <BLACK.PCX>; }\n" +
        "BMAP wm,<WALL.PCX>;\nTEXTURE wt { SCALE_X 4; SCALE_Y 4; BMAPS wm; }\n" +
        "WALL w { TEXTURE wt; }\nREGION room { FLOOR_HGT 0; CEIL_HGT 16; FLOOR_TEX wt; CEIL_TEX wt; }\n" +
        "ACTION to_level { FADE_PAL level_pal, 1; }\n" +
        "ACTION half_black { FADE_PAL black_pal, 0.5; }\n" +
        "ACTION no_fade { FADE_PAL black_pal, 0; }\n" +
        "ACTION start { }\nIF_START start;\n";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ov-fade-pal-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Boot_ShowsTheLastDeclaredPalette()
    {
        using RuntimeHost host = Open(out FakeRender render);

        host.Boot();
        host.Frame(present: false);

        // black_pal is declared last, so the screen is black until a FADE_PAL names level_pal.
        byte[] shown = Assert.IsType<byte[]>(host.ShownPalette);
        Assert.All(shown, static b => Assert.Equal((byte)0, b));
        Assert.Equal(shown, render.LastPalette);
        Assert.Equal(1f, render.LastFrame!.Batches.First(static b => b.Solid is null && !b.DepthOnly).Fade.CurrentWeight);
        Assert.Equal(Vector3.Zero, render.LastFrame.Batches.First(static b => b.Solid is null && !b.DepthOnly).Fade.Current);
    }

    [Fact]
    public void FadePal_AtOne_MakesThePaletteCurrent_AndAFractionBlendsTowardTheNext()
    {
        using RuntimeHost host = Open(out FakeRender render);
        host.Boot();

        host.Runtime.RunAction("to_level");
        host.Frame(present: false);
        byte[] level = Assert.IsType<byte[]>(host.ShownPalette);
        Assert.Equal((byte)121, level[32 * 3]); // entry 32 is 120: 30 on the DAC, widened to 121
        Assert.Equal(PaletteFadeTint.None, render.LastFrame!.Batches.First(static b => b.Solid is null && !b.DepthOnly).Fade);

        host.Runtime.RunAction("half_black");
        host.Frame(present: false);
        byte[] half = Assert.IsType<byte[]>(host.ShownPalette);
        Assert.Equal((byte)60, half[32 * 3]); // 30 halves to 15: 60
        Assert.Equal(half, render.LastPalette);
        Assert.Equal(0.5f, render.LastFrame!.Batches.First(static b => b.Solid is null && !b.DepthOnly).Fade.Factor);
        Assert.Equal(Vector3.Zero, render.LastFrame.Batches.First(static b => b.Solid is null && !b.DepthOnly).Fade.Target);

        host.Runtime.RunAction("no_fade");
        host.Frame(present: false);
        Assert.Equal(level, host.ShownPalette);
        Assert.Equal(PaletteFadeTint.None, render.LastFrame!.Batches.First(static b => b.Solid is null && !b.DepthOnly).Fade);
    }

    [Fact]
    public void Frame_ClearsToThePalettesFrameColorEntry()
    {
        // OV-20261104-retail-frames: the border around the 3-D window is palette entry FRAME_COLOR of
        // the shown palette; the level palette's entry 32 is 120 (30 on the DAC, widened to 121).
        using RuntimeHost host = Open(out FakeRender render);
        host.Boot();
        host.Runtime.RunAction("to_level");
        host.Runtime.SetSkill("FRAME_COLOR", 32);
        host.Frame(present: false);

        Assert.Equal(121 / 255f, render.LastFrame!.ClearColor.X, 4);
        Assert.Equal(121 / 255f, render.LastFrame.ClearColor.Z, 4);

        host.Runtime.RunAction("half_black");
        host.Frame(present: false);
        Assert.Equal(60 / 255f, render.LastFrame!.ClearColor.X, 4);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private RuntimeHost Open(out FakeRender render)
    {
        Directory.CreateDirectory(_directory);
        string archive = Path.Combine(_directory, "TITLE.WRS");
        byte[] levelPalette = new byte[256 * 3];
        for (int i = 16; i < 48; i++)
        {
            byte level = (byte)(248 - ((i - 16) * 8));
            levelPalette[i * 3] = level;
            levelPalette[(i * 3) + 1] = level;
            levelPalette[(i * 3) + 2] = level;
        }

        WrsArchive.WriteFile(
            archive,
            [
                new WrsFile("TITLE.WDL", Encoding.ASCII.GetBytes(Main)),
                new WrsFile("m.wmp", Encoding.ASCII.GetBytes(Map)),
                new WrsFile("LEVEL.PCX", Pcx(levelPalette)),
                new WrsFile("BLACK.PCX", Pcx(new byte[256 * 3])),
                new WrsFile("WALL.PCX", Pcx(levelPalette)),
            ]);
        render = new FakeRender();
        return RuntimeHost.OpenArchive(archive, "TITLE.WDL", new FakeWindow(), new FakeInput(), new NullAudioSink(), render);
    }

    /// <summary>A 1x1 PCX of index 16 carrying the given palette.</summary>
    private static byte[] Pcx(byte[] palette)
    {
        byte[] buffer = new byte[128 + 1 + 1 + (256 * 3)];
        buffer[0] = 0x0A;
        buffer[1] = 5;
        buffer[2] = 1;
        buffer[3] = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10), 0);
        buffer[65] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(66), 1);
        buffer[128] = 16;
        buffer[129] = 0x0C;
        palette.CopyTo(buffer, 130);
        return buffer;
    }
}
