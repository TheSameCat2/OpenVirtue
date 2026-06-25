// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;

namespace OpenVirtue.Formats.Pcx;

/// <summary>
/// A decoded ZSoft <c>PCX</c> image. Saints of Virtue stores all of its graphics
/// (textures, sprites, fonts) as 8-bit, RLE-encoded, paletted PCX with a 256-color
/// palette appended at the end of the file.
/// </summary>
/// <remarks>
/// Clean-room implementation of the public ZSoft PCX format. Only the 8-bit,
/// single-plane variant the game uses is supported; other variants throw
/// <see cref="NotSupportedException"/>. See <c>PROVENANCE.md</c>.
/// </remarks>
public sealed class PcxImage
{
    private const int HeaderSize = 128;
    private const int PaletteMarker = 0x0C;
    private const int PaletteEntries = 256;
    private const int PaletteTrailerSize = 1 + (PaletteEntries * 3); // 0x0C marker + 256 RGB triples

    private PcxImage(int width, int height, byte[] pixelIndices, Rgb24[] palette)
    {
        Width = width;
        Height = height;
        PixelIndices = pixelIndices;
        Palette = palette;
    }

    /// <summary>Image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Image height in pixels.</summary>
    public int Height { get; }

    /// <summary>One palette index per pixel, row-major, length <see cref="Width"/> * <see cref="Height"/>.</summary>
    public byte[] PixelIndices { get; }

    /// <summary>The 256-entry color palette.</summary>
    public Rgb24[] Palette { get; }

    /// <summary>Decodes a PCX image from its bytes.</summary>
    /// <exception cref="InvalidDataException">The data is not a structurally valid PCX image.</exception>
    /// <exception cref="NotSupportedException">The PCX uses a variant other than 8-bit single-plane.</exception>
    public static PcxImage Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("PCX data is shorter than its 128-byte header.");
        }

        if (data[0] != 0x0A)
        {
            throw new InvalidDataException($"Not a PCX file (manufacturer byte 0x{data[0]:X2}, expected 0x0A).");
        }

        byte encoding = data[2];
        byte bitsPerPixel = data[3];
        int xMin = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        int yMin = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        int xMax = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        int yMax = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
        byte planes = data[65];
        int bytesPerLine = BinaryPrimitives.ReadUInt16LittleEndian(data[66..]);

        if (bitsPerPixel != 8 || planes != 1)
        {
            throw new NotSupportedException(
                $"Only 8-bit single-plane PCX is supported (got {bitsPerPixel} bpp, {planes} plane(s)).");
        }

        int width = xMax - xMin + 1;
        int height = yMax - yMin + 1;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException($"PCX has invalid dimensions {width}x{height}.");
        }

        (Rgb24[] palette, int rasterEnd) = ReadPalette(data);

        ReadOnlySpan<byte> rasterBytes = data[HeaderSize..rasterEnd];
        int stride = bytesPerLine;
        byte[] raster = encoding == 1
            ? DecodeRle(rasterBytes, stride * height)
            : ReadUncompressed(rasterBytes, stride * height);

        // Each scanline is padded to `bytesPerLine`; keep only the first `width` bytes.
        byte[] pixels = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            raster.AsSpan(y * stride, width).CopyTo(pixels.AsSpan(y * width, width));
        }

        return new PcxImage(width, height, pixels, palette);
    }

    /// <summary>Expands the paletted image to 32-bit RGBA (R, G, B, A=255), row-major.</summary>
    public byte[] ToRgba32()
    {
        byte[] rgba = new byte[Width * Height * 4];
        int d = 0;
        foreach (byte index in PixelIndices)
        {
            Rgb24 color = Palette[index];
            rgba[d++] = color.R;
            rgba[d++] = color.G;
            rgba[d++] = color.B;
            rgba[d++] = 0xFF;
        }

        return rgba;
    }

    private static (Rgb24[] Palette, int RasterEnd) ReadPalette(ReadOnlySpan<byte> data)
    {
        var palette = new Rgb24[PaletteEntries];
        int trailer = data.Length - PaletteTrailerSize;

        if (trailer >= HeaderSize && data[trailer] == PaletteMarker)
        {
            int p = trailer + 1;
            for (int i = 0; i < PaletteEntries; i++, p += 3)
            {
                palette[i] = new Rgb24(data[p], data[p + 1], data[p + 2]);
            }

            return (palette, trailer); // raster ends where the palette trailer begins
        }

        // No appended palette: fall back to a grayscale ramp; raster runs to EOF.
        for (int i = 0; i < PaletteEntries; i++)
        {
            palette[i] = new Rgb24((byte)i, (byte)i, (byte)i);
        }

        return (palette, data.Length);
    }

    private static byte[] DecodeRle(ReadOnlySpan<byte> input, int outputLength)
    {
        byte[] output = new byte[outputLength];
        int o = 0;
        int i = 0;

        while (o < outputLength)
        {
            if (i >= input.Length)
            {
                throw new InvalidDataException("PCX RLE stream ended before the raster was complete.");
            }

            byte b = input[i++];
            if ((b & 0xC0) == 0xC0)
            {
                int count = b & 0x3F;
                if (i >= input.Length)
                {
                    throw new InvalidDataException("PCX RLE run is missing its value byte.");
                }

                byte value = input[i++];
                for (int k = 0; k < count && o < outputLength; k++)
                {
                    output[o++] = value;
                }
            }
            else
            {
                output[o++] = b;
            }
        }

        return output;
    }

    private static byte[] ReadUncompressed(ReadOnlySpan<byte> input, int outputLength)
    {
        if (input.Length < outputLength)
        {
            throw new InvalidDataException("PCX raster is shorter than the declared image size.");
        }

        return input[..outputLength].ToArray();
    }
}
