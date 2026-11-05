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

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, 1)]
    [InlineData(12, 16)]
    [InlineData(60, 64)]
    [InlineData(64, 64)]
    [InlineData(120, 128)]
    [InlineData(128, 128)]
    public void WallRowPeriod_MaskedHeight_ReturnsTheNextPowerOfTwo(int height, int expected)
    {
        // OV-20261105-wall-pieces: the original reads a wall row as v & (H - 1), so a 12-row bitmap
        // spans 16 rows before the rows repeat, and the retail 120-row curtain 128.
        Assert.Equal(expected, TextureLoader.WallRowPeriod(height));
    }

    [Fact]
    public void Load_NonPowerOfTwoWallTexture_RepeatsItsRowsUpToThePeriod()
    {
        // OV-20261105-wall-pieces: 12 rows read 0…11 and then 8…11 again — period 16 — in the RGBA and
        // the palette-index data alike, so the renderer's plain v wrapping shows the same rows.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "WALL wall 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1";
        const string main =
            "MAPFILE <m.wmp>; BMAP wallMap,<wall.pcx>,0,0,2,12; TEXTURE wallTex { BMAPS wallMap; } " +
            "WALL wall { TEXTURE wallTex; }";
        Level level = LevelLoader.LoadCore("test.wdl", main, n => Path.GetFileName(n) == "m.wmp" ? map : null);
        WrsArchive archive = BuildArchiveWithRawNames(("wall.pcx", BuildPcx(2, 12, 2, Rows(2, 12))));

        TextureImage image = Assert.Contains("wallTex", TextureLoader.Load(archive, level));

        Assert.Equal(2, image.Width);
        Assert.Equal(16, image.Height);
        for (int row = 0; row < 16; row++)
        {
            int source = row & 11; // the masked read of the 12-row bitmap
            Assert.Equal(source + 1, image.Indices![row * 2]);
            Assert.Equal(source + 1, image.Rgba[row * 2 * 4]);
            Assert.Equal(source + 2, image.Rgba[(row * 2 * 4) + 1]);
            Assert.Equal(0xFF, image.Rgba[(row * 2 * 4) + 3]);
        }
    }

    [Fact]
    public void Load_SpriteAndAttachTextures_KeepTheirRealRows()
    {
        // OV-20261105-wall-pieces: only a placed wall's own bitmaps are read masked; the same bitmap on
        // a sprite or attached to a wall keeps its 12 rows.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "WALL wall 0 1 0 -1 0 0;#0\nTHING sprite 0 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        const string main =
            "MAPFILE <m.wmp>; BMAP spriteMap,<sprite.pcx>,0,0,2,12; BMAP wallMap,<wall.pcx>,0,0,2,12; " +
            "TEXTURE spriteTex { BMAPS spriteMap; } TEXTURE attachTex { BMAPS spriteMap; } " +
            "TEXTURE wallTex { BMAPS wallMap; } " +
            "WALL wall { TEXTURE wallTex; ATTACH attachTex; } THING sprite { TEXTURE spriteTex; }";
        Level level = LevelLoader.LoadCore("test.wdl", main, n => Path.GetFileName(n) == "m.wmp" ? map : null);
        WrsArchive archive = BuildArchiveWithRawNames(
            ("sprite.pcx", BuildPcx(2, 12, 2, Rows(2, 12))),
            ("wall.pcx", BuildPcx(2, 12, 2, Rows(2, 12))));

        IReadOnlyDictionary<string, TextureImage> images = TextureLoader.Load(archive, level);

        Assert.Equal(12, images["spriteTex"].Height);
        Assert.Equal(12, images["attachTex"].Height);
        Assert.Equal(16, images["wallTex"].Height); // the placed wall's own bitmap is the one expanded
    }

    [Fact]
    public void RowMaskedWallTextures_NamesOnlyAPlacedWallsOwnBitmaps()
    {
        // OV-20261105-wall-pieces round 6 (round 5 review finding): the loader expands exactly the
        // bitmaps the mesh maps with the row period, so the two agree. A placed wall's own bitmap that
        // is not a power of two tall qualifies; a power-of-two one does not need it, and one a sprite
        // or an attach draws, or that only a surface reads, keeps its real rows.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "WALL masked 0 1 0 -1 0 0;#0\nWALL powered 0 1 0 -1 0 0;#1\n" +
            "WALL shared 0 1 0 -1 0 0;#2\nWALL attached 0 1 0 -1 0 0;#3\n" +
            "THING sprite 0 0 0 0;#4\nTHING lamp 0 0 0 0;#5\nPLAYER_START 0 0 0 0;#6";
        const string main =
            "MAPFILE <m.wmp>; " +
            "BMAP a,<a.pcx>,0,0,2,12; BMAP b,<b.pcx>,0,0,2,16; BMAP c,<c.pcx>,0,0,2,12; " +
            "BMAP d,<d.pcx>,0,0,2,12; BMAP e,<e.pcx>,0,0,2,12; " +
            "TEXTURE masked { BMAPS a; } TEXTURE powered { BMAPS b; } TEXTURE sharedSprite { BMAPS c; } " +
            "TEXTURE attached { BMAPS d; } TEXTURE surfaceTex { BMAPS e; } " +
            "WALL masked { TEXTURE masked; } WALL powered { TEXTURE powered; } " +
            "WALL shared { TEXTURE sharedSprite; } WALL attached { TEXTURE attached; } " +
            "THING sprite { TEXTURE sharedSprite; } THING lamp { ATTACH attached; } " +
            "REGION room { FLOOR_TEX surfaceTex; }";
        Level level = LevelLoader.LoadCore("test.wdl", main, n => Path.GetFileName(n) == "m.wmp" ? map : null);

        IReadOnlySet<string> masked = TextureLoader.RowMaskedWallTextures(level);

        Assert.Contains("masked", masked);
        Assert.DoesNotContain("powered", masked); // already a power of two
        Assert.DoesNotContain("sharedSprite", masked); // a thing draws it too
        Assert.DoesNotContain("attached", masked); // a thing attaches it
        Assert.DoesNotContain("surfaceTex", masked); // no wall names it
    }

    [Fact]
    public void Load_WallTextureSharedWithASurface_ExpandsTheImageItReads()
    {
        // OV-20261105-wall-pieces round 6: a floor sharing a placed wall's 12-row bitmap reads the same
        // expanded image, so it is expanded as before and the mesh maps both with the period.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "WALL wall 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1";
        const string main =
            "MAPFILE <m.wmp>; BMAP sharedMap,<wall.pcx>,0,0,2,12; TEXTURE shared { BMAPS sharedMap; } " +
            "WALL wall { TEXTURE shared; } REGION room { FLOOR_TEX shared; }";
        Level level = LevelLoader.LoadCore("test.wdl", main, n => Path.GetFileName(n) == "m.wmp" ? map : null);
        WrsArchive archive = BuildArchiveWithRawNames(("wall.pcx", BuildPcx(2, 12, 2, Rows(2, 12))));

        IReadOnlyDictionary<string, TextureImage> images = TextureLoader.Load(archive, level);

        Assert.Contains("shared", TextureLoader.RowMaskedWallTextures(level));
        Assert.Equal(16, images["shared"].Height);
        Assert.Equal(9, images["shared"].Indices![12 * 2]); // row 12 reads source row 8 (which holds 9)
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

    /// <summary>A PCX raster whose pixels hold 1, 2, 3… by row, so a row is recognisable.</summary>
    private static byte[] Rows(int width, int height)
    {
        byte[] rle = new byte[width * height];
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                rle[(row * width) + col] = (byte)(row + 1);
            }
        }

        return rle;
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
