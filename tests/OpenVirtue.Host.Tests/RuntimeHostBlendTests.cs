// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.Text;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Formats.Wrs;
using OpenVirtue.Host.Tests.TestSupport;

namespace OpenVirtue.Host.Tests;

/// <summary>
/// OV-20261104-blend: a DIAPHANOUS sprite blends with a BLUR palette's table and draws palette entry
/// 0 (black) without one; a GHOST sprite is a checkerboard either way.
/// </summary>
public sealed class RuntimeHostBlendTests : IDisposable
{
    private const string Map =
        "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nVERTEX 20 20 0;#2\nVERTEX 0 20 0;#3\n" +
        "REGION room 0 10;#0\n" +
        "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\nWALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\n" +
        "THING ghost 12 10 0 0;#4\nTHING glass 12 6 0 0;#5\nPLAYER_START 4 8 0 0;#6\n";

    private const string Main =
        "MAPFILE <m.wmp>;\n" +
        "PALETTE level_pal { PALFILE <LEVEL.PCX>; RANGE 16, 32; {FLAGS} }\n" +
        "BMAP wm,<WALL.PCX>,0,0,64,64;\nBMAP sm,<WALL.PCX>,0,0,16,16;\n" +
        "TEXTURE wt { SCALE_X 4; SCALE_Y 4; BMAPS wm; }\n" +
        "TEXTURE ghostTex { BMAPS sm; FLAGS GHOST; }\nTEXTURE glassTex { BMAPS sm; FLAGS DIAPHANOUS; }\n" +
        "WALL w { TEXTURE wt; }\nREGION room { FLOOR_HGT 0; CEIL_HGT 16; FLOOR_TEX wt; CEIL_TEX wt; }\n" +
        "THING ghost { TEXTURE ghostTex; }\nTHING glass { TEXTURE glassTex; }\n" +
        "ACTION start { }\nIF_START start;\n";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ov-blend-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("FLAGS BLUR;", BatchBlend.Diaphanous)]
    [InlineData("", BatchBlend.Black)]
    public void Frame_DiaphanousSprite_BlendsOnlyWithABlurPalette(string paletteFlags, BatchBlend expected)
    {
        using RuntimeHost host = Open(paletteFlags, out FakeRender render);
        host.Boot();
        host.Frame(present: false);

        List<DrawBatch> sprites = render.LastFrame!.Batches.Where(batch => batch.Texture is "ghostTex" or "glassTex").ToList();
        Assert.Equal(2, sprites.Count);
        Assert.Equal(BatchBlend.Ghost, sprites.Single(batch => batch.Texture == "ghostTex").Blend);
        Assert.Equal(expected, sprites.Single(batch => batch.Texture == "glassTex").Blend);

        // OV-20261105-shade-global: a sprite's batch is marked so the renderer floors its zones, and its
        // vertices carry no wall ends (only a wall's do).
        Assert.All(sprites, batch => Assert.Equal(PaletteShading.SpriteMarker, batch.SpriteMarker));
        Assert.All(sprites.SelectMany(batch => batch.Vertices), vertex => Assert.Equal(0, vertex.WallAX + vertex.WallAZ + vertex.WallBX + vertex.WallBZ));
        Assert.All(render.LastFrame.Batches.Where(batch => batch.Texture == "wt"), batch => Assert.Equal(0, batch.SpriteMarker));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private RuntimeHost Open(string paletteFlags, out FakeRender render)
    {
        Directory.CreateDirectory(_directory);
        string archive = Path.Combine(_directory, "TITLE.WRS");
        byte[] palette = new byte[256 * 3];
        for (int i = 16; i < 48; i++)
        {
            byte level = (byte)(248 - ((i - 16) * 8));
            palette[i * 3] = level;
            palette[(i * 3) + 1] = level;
            palette[(i * 3) + 2] = level;
        }

        WrsArchive.WriteFile(
            archive,
            [
                new WrsFile("TITLE.WDL", Encoding.ASCII.GetBytes(Main.Replace("{FLAGS}", paletteFlags, StringComparison.Ordinal))),
                new WrsFile("m.wmp", Encoding.ASCII.GetBytes(Map)),
                new WrsFile("LEVEL.PCX", Pcx(palette)),
                new WrsFile("WALL.PCX", Pcx(palette)),
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
