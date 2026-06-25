// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text;
using OpenVirtue.Formats.Compression;

namespace OpenVirtue.Formats.Tests.Compression;

public class LzssTests
{
    [Fact]
    public void Decompress_AllLiterals_ReturnsLiteralBytes()
    {
        // One flag byte 0xFF (eight "literal" bits, LSB-first) followed by eight literal bytes.
        byte[] compressed = [0xFF, .. "ABCDEFGH"u8];

        byte[] result = Lzss.Decompress(compressed, decompressedSize: 8);

        Assert.Equal("ABCDEFGH", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void Decompress_OverlappingMatch_ExpandsAsRunLength()
    {
        // Hand-built stream:
        //   flag 0x01 -> item 0 = literal, item 1 = match
        //   literal 'A' (0x41)
        //   match  -> low=0xEE, packed=0xF0  =>  offset 0xFEE (=4078, the slot 'A' was written to),
        //             length nibble 0 => length 3, copied through the ring buffer (overlapping).
        // Expected output: "A" + "AAA" = "AAAA".
        byte[] compressed = [0x01, 0x41, 0xEE, 0xF0];

        byte[] result = Lzss.Decompress(compressed, decompressedSize: 4);

        Assert.Equal("AAAA", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void Decompress_ZeroLength_ReturnsEmpty()
    {
        byte[] result = Lzss.Decompress([0xFF, 0x41], decompressedSize: 0);

        Assert.Empty(result);
    }

    [Fact]
    public void Decompress_TruncatedStream_Throws()
    {
        // Flag says "two literals" but only one literal byte is present.
        byte[] compressed = [0xFF, 0x41];

        Assert.Throws<InvalidDataException>(() => Lzss.Decompress(compressed, decompressedSize: 8));
    }

    [Fact]
    public void Decompress_NegativeSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Lzss.Decompress([0xFF], decompressedSize: -1));
    }
}
