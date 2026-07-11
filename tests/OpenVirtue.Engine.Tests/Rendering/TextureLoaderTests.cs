// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.Text;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Engine.Tests.TestSupport;
using OpenVirtue.Formats.Compression;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Engine.Tests.Rendering;

public class TextureLoaderTests
{
    [Fact]
    public void Load_UsesDocumentedPathSearchForTextures()
    {
        const string map = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\nPLAYER_START 0 0 0 0;#1";
        const string main =
            "MAPFILE <m.wmp>; PATH \"gfx\"; BMAP tile_map, <tile.pcx>; TEXTURE tile_tex { BMAPS tile_map; }";
        Level level = LevelLoader.LoadCore("test.wdl", main, n => Path.GetFileName(n) == "m.wmp" ? map : null);
        WrsArchive archive = BuildArchiveWithRawNames(("gfx/tile.pcx", BuildPcx(1, 1, 1, [0x01])));

        IReadOnlyDictionary<string, TextureImage> images = TextureLoader.Load(archive, level);

        TextureImage image = Assert.Contains("tile_tex", images);
        Assert.Equal(1, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal([1, 2, 3, 0xFF], image.Rgba);
    }

    [Fact]
    public void Load_AnimatedTexture_LoadsEveryNonNullBitmapPhase()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "THING actor 0 0 0 0;#0\nPLAYER_START 0 0 0 0;#1";
        const string main =
            "MAPFILE <m.wmp>; BMAP first,<first.pcx>; BMAP second,<second.pcx>; " +
            "TEXTURE animated { CYCLES 3; BMAPS first,second,NULL; } " +
            "THING actor { TEXTURE animated; }";
        Level level = LevelLoader.LoadCore("test.wdl", main, n => Path.GetFileName(n) == "m.wmp" ? map : null);
        WrsArchive archive = BuildArchiveWithRawNames(
            ("first.pcx", BuildPcx(1, 1, 1, [0x01])),
            ("second.pcx", BuildPcx(1, 1, 1, [0x02])));

        IReadOnlyDictionary<string, TextureImage> images = TextureLoader.Load(archive, level);

        Assert.Equal([1, 2, 3, 0xFF], images["animated"].Rgba);
        Assert.Equal([2, 3, 4, 0xFF], images[TextureAnimation.FrameKey("animated", 1)].Rgba);
        Assert.DoesNotContain(TextureAnimation.FrameKey("animated", 2), images);
    }

    [Fact]
    public void Load_EntityAttachment_UsesSpriteColorKey()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "THING actor 0 0 0 0;#0\nPLAYER_START 0 0 0 0;#1";
        const string main =
            "MAPFILE <m.wmp>; BMAP glowMap,<glow.pcx>; TEXTURE glowTex { BMAPS glowMap; } " +
            "THING actor { ATTACH glowTex; }";
        Level level = LevelLoader.LoadCore("test.wdl", main, n => Path.GetFileName(n) == "m.wmp" ? map : null);
        WrsArchive archive = BuildArchiveWithRawNames(("glow.pcx", BuildPcx(1, 1, 1, [0x00])));

        TextureImage image = Assert.Contains("glowTex", TextureLoader.Load(archive, level));

        Assert.Equal([0, 1, 2, 0], image.Rgba);
    }

    [Fact]
    public void Load_WorldTexturePreservesColorKeyAlphaForMaterialSelection()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "WALL wall 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1";
        const string main =
            "MAPFILE <m.wmp>; BMAP wallMap,<wall.pcx>; TEXTURE wallTex { BMAPS wallMap; } " +
            "WALL wall { TEXTURE wallTex; }";
        Level level = LevelLoader.LoadCore("test.wdl", main, n => Path.GetFileName(n) == "m.wmp" ? map : null);
        WrsArchive archive = BuildArchiveWithRawNames(("wall.pcx", BuildPcx(1, 1, 1, [0x00])));

        TextureImage image = Assert.Contains("wallTex", TextureLoader.Load(archive, level));

        Assert.Equal([0, 1, 2, 0], image.Rgba);
    }

    /// <summary>
    /// Decodes referenced textures for every local retail archive when user-supplied game
    /// data is present. No-op otherwise.
    /// </summary>
    [Fact]
    public void Load_LocalRetailArchiveTextures_DecodeToRgba()
    {
        IReadOnlyList<string> archives = ResearchData.WrsFiles();
        if (archives.Count == 0)
        {
            return;
        }

        foreach (string path in archives)
        {
            WrsArchive archive = WrsArchive.ReadFile(path);
            Level level = LevelLoader.Load(archive, ResearchData.MainWdlName(path));

            IReadOnlyDictionary<string, TextureImage> images = TextureLoader.Load(archive, level);

            Assert.NotEmpty(images);
            foreach ((_, TextureImage image) in images)
            {
                Assert.True(image.Width > 0 && image.Height > 0, $"{Path.GetFileName(path)}: image should have pixels");
                Assert.Equal(image.Width * image.Height * 4, image.Rgba.Length);
            }
        }
    }

    /// <summary>
    /// Loads the real apathy level's textures (when present): decodes each referenced PCX and
    /// crops to its BMAP rectangle. No-op when game data is absent.
    /// </summary>
    [Fact]
    public void Load_RealApathyTextures_DecodeToRgba()
    {
        string? apathy = ResearchData.WrsFiles()
            .FirstOrDefault(p => Path.GetFileName(p).Equals("apathy.wrs", StringComparison.OrdinalIgnoreCase));
        if (apathy is null)
        {
            return; // no local game data — skip
        }

        WrsArchive archive = WrsArchive.ReadFile(apathy);
        Level level = LevelLoader.Load(archive, "APATHY.WDL");

        IReadOnlyDictionary<string, TextureImage> images = TextureLoader.Load(archive, level);

        Assert.NotEmpty(images);
        foreach (TextureImage image in images.Values)
        {
            Assert.True(image.Width > 0 && image.Height > 0);
            Assert.Equal(image.Width * image.Height * 4, image.Rgba.Length);
        }
    }

    private static WrsArchive BuildArchiveWithRawNames(params (string Name, byte[] Data)[] files)
    {
        using var output = new MemoryStream();
        Span<byte> sizeBuffer = stackalloc byte[sizeof(uint)];
        Span<byte> nameField = stackalloc byte[WrsArchive.NameFieldLength];

        foreach ((string name, byte[] data) in files)
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes(name);
            if (nameBytes.Length > WrsArchive.NameFieldLength)
            {
                throw new ArgumentException($"WRS entry name '{name}' exceeds the fixed name field.");
            }

            nameField.Clear();
            nameBytes.CopyTo(nameField);
            output.Write(nameField);

            byte[] compressed = Lzss.Compress(data);
            BinaryPrimitives.WriteUInt32BigEndian(sizeBuffer, checked((uint)compressed.Length));
            output.Write(sizeBuffer);
            BinaryPrimitives.WriteUInt32BigEndian(sizeBuffer, checked((uint)data.Length));
            output.Write(sizeBuffer);
            output.Write(compressed);
        }

        return WrsArchive.Read(output.ToArray());
    }

    private static byte[] BuildPcx(int width, int height, int bytesPerLine, byte[] rle)
    {
        const int headerSize = 128;
        const int paletteTrailer = 1 + (256 * 3);
        byte[] buffer = new byte[headerSize + rle.Length + paletteTrailer];
        Span<byte> span = buffer;

        span[0] = 0x0A;
        span[1] = 5;
        span[2] = 1;
        span[3] = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], (ushort)(width - 1));
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], (ushort)(height - 1));
        span[65] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(span[66..], (ushort)bytesPerLine);

        rle.CopyTo(span[headerSize..]);

        int p = headerSize + rle.Length;
        span[p++] = 0x0C;
        for (int i = 0; i < 256; i++)
        {
            span[p++] = (byte)i;
            span[p++] = (byte)(i + 1);
            span[p++] = (byte)(i + 2);
        }

        return buffer;
    }
}
