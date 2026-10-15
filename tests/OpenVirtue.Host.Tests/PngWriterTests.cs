// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using OpenVirtue.Host;

namespace OpenVirtue.Host.Tests;

public sealed class PngWriterTests
{
    [Fact]
    public void Encode_RoundTripsRgbaPixels()
    {
        byte[] pixels =
        [
            255, 0, 0, 255,
            0, 255, 0, 255,
            0, 0, 255, 255,
            255, 255, 255, 128,
        ];
        byte[] png = PngWriter.Encode(2, 2, pixels);

        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        Assert.Equal(8, png[24]);
        Assert.Equal(6, png[25]);

        byte[] raw = Inflate(png);
        Assert.Equal(2 * 9, raw.Length);
        Assert.Equal(pixels.AsSpan(0, 8).ToArray(), raw.AsSpan(1, 8).ToArray());
        Assert.Equal(pixels.AsSpan(8, 8).ToArray(), raw.AsSpan(10, 8).ToArray());
        Assert.Equal(0, raw[0]);
        Assert.Equal(0, raw[9]);
    }

    private static byte[] Inflate(byte[] png)
    {
        using var idat = new MemoryStream();
        int offset = 8;
        while (offset + 12 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IDAT")
            {
                idat.Write(png, offset + 8, length);
            }

            if (type == "IEND")
            {
                break;
            }

            offset += 12 + length;
        }

        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        return raw.ToArray();
    }
}
