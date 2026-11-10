// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text;
using OpenVirtue.Formats.Wrs;
using OpenVirtue.Host;
using OpenVirtue.Host.Tests.TestSupport;

namespace OpenVirtue.Host.Tests;

/// <summary>The screen as one persistent picture (OV-20261106-screen-frame).</summary>
public sealed class RuntimeHostScreenFrameTests : IDisposable
{
    private const string Map =
        "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nVERTEX 20 20 0;#2\nVERTEX 0 20 0;#3\n" +
        "REGION room 0 10;#0\n" +
        "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\nWALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\n" +
        "PLAYER_START 10 10 0 0 0;\n";

    private const string Main =
        "MAPFILE <m.wmp>;\n" +
        "PALETTE level_pal { PALFILE <LEVEL.PCX>; RANGE 16, 32; }\n" +
        "SKILL shown { VAL 1; }\n" +
        "FONT dig,<WALL.PCX>,1,1;\n" +
        "BMAP wm,<WALL.PCX>;\nBMAP pm,<PAN.PCX>;\nTEXTURE wt { SCALE_X 4; SCALE_Y 4; BMAPS wm; }\n" +
        "WALL w { TEXTURE wt; }\nREGION room { FLOOR_HGT 0; CEIL_HGT 16; FLOOR_TEX wt; CEIL_TEX wt; }\n" +
        "PANEL plain { PAN_MAP pm; POS_X 4; POS_Y 4; FLAGS VISIBLE; }\n" +
        "PANEL always { PAN_MAP wm; POS_X 40; POS_Y 4; FLAGS VISIBLE, REFRESH; }\n" +
        "ACTION hide { SET plain.VISIBLE, 0; }\nACTION show { SET plain.VISIBLE, 1; }\n" +
        "ACTION move { SET plain.POS_X, 10; }\n" +
        "ACTION paint { SET FRAME_COLOR, 7; }\nACTION unpaint { SET FRAME_COLOR, 0; }\n" +
        "ACTION blank { SET RENDER_MODE, 0; }\n" +
        "ACTION start { }\nIF_START start;\n";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ov-screen-frame-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Frame_FrameColorZero_LeavesTheScreenUnpainted()
    {
        using RuntimeHost host = Open(out FakeRender render);
        host.Boot();

        host.Frame(present: false);
        Assert.False(render.LastFrame!.PaintColor);

        host.Runtime.RunAction("paint");
        host.Frame(present: false);
        Assert.True(render.LastFrame!.PaintColor);

        host.Runtime.RunAction("unpaint");
        host.Frame(present: false);
        Assert.False(render.LastFrame!.PaintColor);
    }

    [Fact]
    public void Frame_PanelWithoutRefresh_DrawsAChangeOnTwoFramesOnly()
    {
        using RuntimeHost host = Open(out FakeRender render);
        host.Boot();

        // The start: two frames, then it lasts on the screen alone.
        Assert.True(Draws(host, render, "pm"));
        Assert.True(Draws(host, render, "pm"));
        Assert.False(Draws(host, render, "pm"));

        // Hidden and shown again counts as a change; hiding itself draws nothing.
        host.Runtime.RunAction("hide");
        Assert.False(Draws(host, render, "pm"));
        host.Runtime.RunAction("show");
        Assert.True(Draws(host, render, "pm"));
        Assert.True(Draws(host, render, "pm"));
        Assert.False(Draws(host, render, "pm"));

        host.Runtime.RunAction("move");
        Assert.True(Draws(host, render, "pm"));
        Assert.True(Draws(host, render, "pm"));
        Assert.False(Draws(host, render, "pm"));
    }

    [Fact]
    public void Frame_PanelWithRefresh_DrawsOnEveryFrame()
    {
        using RuntimeHost host = Open(out FakeRender render);
        host.Boot();

        for (int i = 0; i < 5; i++)
        {
            host.Frame(present: false);
            Assert.Contains(render.LastFrame!.Batches, static batch => batch is { Slot: TextureSlot.Ui, Texture: "wm" });
        }
    }

    [Fact]
    public void Frame_ViewsFirstRow_ReadsPaletteEntryZero()
    {
        using RuntimeHost host = Open(out FakeRender render);
        host.Boot();
        host.Frame(present: false);

        List<DrawBatch> solids = [.. render.LastFrame!.Batches.Where(static batch => batch.Solid is not null)];
        Assert.Contains(solids, static batch => batch.Viewport.Height == 1);
    }

    [Fact]
    public void Frame_RenderModeZero_DrawsNoWorld()
    {
        using RuntimeHost host = Open(out FakeRender render);
        host.Boot();
        host.Runtime.RunAction("blank");

        host.Frame(present: false);

        Assert.DoesNotContain(render.LastFrame!.Batches, static batch => batch.Slot == TextureSlot.World);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static bool Draws(RuntimeHost host, FakeRender render, string texture)
    {
        host.Frame(present: false);
        return render.LastFrame!.Batches.Any(batch => batch.Slot == TextureSlot.Ui && batch.Texture == texture);
    }

    private RuntimeHost Open(out FakeRender render)
    {
        Directory.CreateDirectory(_directory);
        string archive = Path.Combine(_directory, "t.wrs");
        WrsArchive.WriteFile(
            archive,
            [
                new WrsFile("TITLE.WDL", Encoding.ASCII.GetBytes(Main)),
                new WrsFile("m.wmp", Encoding.ASCII.GetBytes(Map)),
                new WrsFile("LEVEL.PCX", Pcx()),
                new WrsFile("WALL.PCX", Pcx()),
                new WrsFile("PAN.PCX", Pcx()),
            ]);
        render = new FakeRender();
        return RuntimeHost.OpenArchive(archive, "TITLE.WDL", new FakeWindow(), new FakeInput(), new NullAudioSink(), render);
    }

    /// <summary>A 1×1 PCX of index 16 with a ramp palette.</summary>
    private static byte[] Pcx()
    {
        byte[] header = new byte[128];
        header[0] = 10;
        header[1] = 5;
        header[2] = 1;
        header[3] = 8;
        header[65] = 1;
        header[66] = 1;
        byte[] palette = new byte[769];
        palette[0] = 12;
        for (int i = 0; i < 256; i++)
        {
            palette[1 + (i * 3)] = (byte)i;
            palette[2 + (i * 3)] = (byte)i;
            palette[3 + (i * 3)] = (byte)i;
        }

        return [.. header, 16, .. palette];
    }
}
