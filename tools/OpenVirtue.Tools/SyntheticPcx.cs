// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;

/// <summary>
/// Writes small 8-bit paletted PCX images for synthetic oracle fixtures (solid colors,
/// checkers, stripes, palette swatches). Layout follows the published ZSoft PCX header
/// (128-byte header, RLE raster, 0x0C + 768-byte palette trailer); nothing here derives
/// from retail art.
/// </summary>
internal static class SyntheticPcx
{
    /// <summary>A 256-entry RGB palette as (r,g,b) triplets.</summary>
    public sealed class Palette
    {
        private readonly byte[] _rgb = new byte[256 * 3];

        public void Set(int index, byte r, byte g, byte b)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(index, 255);
            _rgb[index * 3] = r;
            _rgb[(index * 3) + 1] = g;
            _rgb[(index * 3) + 2] = b;
        }

        public ReadOnlySpan<byte> Rgb => _rgb;
    }

    public static byte[] Write(int width, int height, Func<int, int, byte> pixelAt, Palette palette)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "PCX dimensions must be positive.");
        }

        int bytesPerLine = width % 2 == 0 ? width : width + 1;
        using var raster = new MemoryStream();
        byte[] row = new byte[bytesPerLine];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                row[x] = pixelAt(x, y);
            }

            if (bytesPerLine > width)
            {
                row[width] = row[width - 1];
            }

            EncodeRow(raster, row);
        }

        byte[] rasterBytes = raster.ToArray();
        byte[] data = new byte[128 + rasterBytes.Length + 1 + (256 * 3)];
        Span<byte> span = data;
        span[0] = 0x0A;          // manufacturer
        span[1] = 5;             // version: 3.0 with palette
        span[2] = 1;             // RLE
        span[3] = 8;             // bits per pixel
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], checked((ushort)(width - 1)));
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], checked((ushort)(height - 1)));
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], checked((ushort)width));
        BinaryPrimitives.WriteUInt16LittleEndian(span[14..], checked((ushort)height));
        span[65] = 1;            // planes
        BinaryPrimitives.WriteUInt16LittleEndian(span[66..], checked((ushort)bytesPerLine));
        BinaryPrimitives.WriteUInt16LittleEndian(span[68..], 1); // palette info: color

        rasterBytes.CopyTo(span[128..]);
        int paletteStart = 128 + rasterBytes.Length;
        span[paletteStart] = 0x0C;
        palette.Rgb.CopyTo(span[(paletteStart + 1)..]);
        return data;
    }

    private static void EncodeRow(Stream output, ReadOnlySpan<byte> row)
    {
        int i = 0;
        while (i < row.Length)
        {
            byte value = row[i];
            int run = 1;
            while (i + run < row.Length && row[i + run] == value && run < 63)
            {
                run++;
            }

            if (run > 1 || value >= 0xC0)
            {
                output.WriteByte((byte)(0xC0 | run));
            }

            output.WriteByte(value);
            i += run;
        }
    }

    public static byte[] Solid(int width, int height, byte index, Palette palette) =>
        Write(width, height, (_, _) => index, palette);

    /// <summary>A checker of <paramref name="cell"/>-pixel squares alternating two indices.</summary>
    public static byte[] Checker(int width, int height, int cell, byte a, byte b, Palette palette) =>
        Write(width, height, (x, y) => ((x / cell) + (y / cell)) % 2 == 0 ? a : b, palette);

    /// <summary>Left half (x &lt; width/2) one index, right half another: an orientation marker.</summary>
    public static byte[] HalfHalf(int width, int height, byte left, byte right, Palette palette) =>
        Write(width, height, (x, _) => x < width / 2 ? left : right, palette);

    /// <summary>A 16x16 swatch holding every palette index once (used as a PALFILE carrier).</summary>
    public static byte[] Swatch(Palette palette) =>
        Write(16, 16, (x, y) => (byte)((y * 16) + x), palette);
}
