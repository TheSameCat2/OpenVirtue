// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using OpenVirtue.Formats.Pcx;
using OpenVirtue.Formats.Tests.TestSupport;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Formats.Tests.Pcx;

public class PcxImageTests
{
    [Fact]
    public void Read_DecodesRleScanlinesAndPalette()
    {
        // 4x2 image. Row 0 = [1,2,3,4] (literals); row 1 = [5,5,5,5] (one RLE run).
        byte[] rle = [0x01, 0x02, 0x03, 0x04, 0xC4, 0x05];
        byte[] pcx = BuildPcx(width: 4, height: 2, bytesPerLine: 4, rle);

        PcxImage image = PcxImage.Read(pcx);

        Assert.Equal(4, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 5, 5, 5 }, image.PixelIndices);
        // BuildPcx sets palette[i] = (i, i+1, i+2).
        Assert.Equal(new Rgb24(1, 2, 3), image.Palette[1]);
        Assert.Equal(new Rgb24(5, 6, 7), image.Palette[5]);
    }

    [Fact]
    public void ToRgba32_MapsIndicesThroughPalette()
    {
        byte[] rle = [0x01, 0x02, 0x03, 0x04, 0xC4, 0x05];
        PcxImage image = PcxImage.Read(BuildPcx(4, 2, 4, rle));

        byte[] rgba = image.ToRgba32();

        Assert.Equal(4 * 2 * 4, rgba.Length);
        // First pixel has index 1 -> palette (1,2,3), alpha 255.
        Assert.Equal(new byte[] { 1, 2, 3, 0xFF }, rgba[..4]);
    }

    [Fact]
    public void Read_NotPcx_Throws()
    {
        Assert.Throws<InvalidDataException>(() => PcxImage.Read(new byte[128]));
    }

    [Fact]
    public void Read_UnsupportedVariant_Throws()
    {
        byte[] header = new byte[128];
        header[0] = 0x0A;  // PCX manufacturer
        header[3] = 8;     // bpp
        header[65] = 3;    // 3 planes (24-bit) — unsupported
        Assert.Throws<NotSupportedException>(() => PcxImage.Read(header));
    }

    /// <summary>
    /// Decodes every PCX stored in the real archives (when present locally). Saints'
    /// graphics are all 8-bit RLE PCX, so this exercises the reader against hundreds
    /// of real images. No-op when game data is absent.
    /// </summary>
    [Fact]
    public void Read_RealPcxFromArchives_AllDecode()
    {
        IReadOnlyList<string> wrsFiles = ResearchData.WrsFiles();
        if (wrsFiles.Count == 0)
        {
            return; // no local game data — skip
        }

        int decoded = 0;
        foreach (string path in wrsFiles)
        {
            WrsArchive archive = WrsArchive.ReadFile(path);
            foreach (WrsEntry entry in archive.Entries)
            {
                if (!entry.Name.EndsWith(".pcx", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                PcxImage image = PcxImage.Read(entry.GetData());

                Assert.True(image.Width > 0 && image.Height > 0, $"{entry.Name} has non-positive dimensions");
                Assert.Equal(image.Width * image.Height, image.PixelIndices.Length);
                Assert.Equal(256, image.Palette.Length);
                decoded++;
            }
        }

        Assert.True(decoded > 0, "expected to decode at least one real PCX");
    }

    private static byte[] BuildPcx(int width, int height, int bytesPerLine, byte[] rle)
    {
        const int headerSize = 128;
        const int paletteTrailer = 1 + (256 * 3);
        byte[] buffer = new byte[headerSize + rle.Length + paletteTrailer];
        Span<byte> span = buffer;

        span[0] = 0x0A;                                                  // manufacturer (ZSoft)
        span[1] = 5;                                                     // version 3.0 w/ palette
        span[2] = 1;                                                     // RLE encoding
        span[3] = 8;                                                     // bits per pixel
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 0);          // xMin
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], 0);          // yMin
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], (ushort)(width - 1));  // xMax
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], (ushort)(height - 1)); // yMax
        span[65] = 1;                                                    // planes
        BinaryPrimitives.WriteUInt16LittleEndian(span[66..], (ushort)bytesPerLine);

        rle.CopyTo(span[headerSize..]);

        int p = headerSize + rle.Length;
        span[p++] = 0x0C;                                               // palette marker
        for (int i = 0; i < 256; i++)
        {
            span[p++] = (byte)i;
            span[p++] = (byte)(i + 1);
            span[p++] = (byte)(i + 2);
        }

        return buffer;
    }
}
