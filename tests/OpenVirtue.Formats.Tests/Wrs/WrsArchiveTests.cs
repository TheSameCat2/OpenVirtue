// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.Text;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Formats.Tests.Wrs;

public class WrsArchiveTests
{
    // LZSS streams whose decompressed output is known (see LzssTests for the derivation).
    private static readonly byte[] HelloPayload = [0xFF, .. "ABCDEFGH"u8]; // -> "ABCDEFGH" (8 bytes)
    private static readonly byte[] AaaaPayload = [0x01, 0x41, 0xEE, 0xF0];  // -> "AAAA"     (4 bytes)

    [Fact]
    public void Read_ParsesEntryMetadataInOrder()
    {
        byte[] archive = BuildArchive(
            ("HELLO.WDL", HelloPayload, 8),
            ("X.WAV", AaaaPayload, 4));

        WrsArchive wrs = WrsArchive.Read(archive);

        Assert.Collection(wrs.Entries,
            e =>
            {
                Assert.Equal("HELLO.WDL", e.Name);
                Assert.Equal(HelloPayload.Length, e.CompressedSize);
                Assert.Equal(8, e.UncompressedSize);
            },
            e =>
            {
                Assert.Equal("X.WAV", e.Name);
                Assert.Equal(AaaaPayload.Length, e.CompressedSize);
                Assert.Equal(4, e.UncompressedSize);
            });
    }

    [Fact]
    public void Entry_GetData_DecompressesPayload()
    {
        byte[] archive = BuildArchive(
            ("HELLO.WDL", HelloPayload, 8),
            ("X.WAV", AaaaPayload, 4));

        WrsArchive wrs = WrsArchive.Read(archive);

        Assert.Equal("ABCDEFGH", Encoding.ASCII.GetString(wrs.Entries[0].GetData()));
        Assert.Equal("AAAA", Encoding.ASCII.GetString(wrs.Entries[1].GetData()));
    }

    [Fact]
    public void Read_TooShortForHeader_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WrsArchive.Read(new byte[] { 0x00, 0x01 }));
    }

    [Fact]
    public void Read_EntryOverrunsArchive_Throws()
    {
        // A single entry that claims a compressed size far larger than the buffer.
        byte[] header = new byte[4 + 13 + 4 + 4];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), (uint)header.Length); // archiveSize
        WriteName(header.AsSpan(4), "BAD.WDL");
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4 + 13), 0xFFFF);          // compressedSize (overrun)
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4 + 17), 0x10);            // uncompressedSize

        Assert.Throws<InvalidDataException>(() => WrsArchive.Read(header));
    }

    /// <summary>
    /// Validates the reader against a real user-supplied WRS archive if one is present
    /// under the git-ignored <c>_research/</c> folder. This is the byte-exact oracle for
    /// the LZSS dialect. It is a no-op (and never ships fixtures) when no such file exists,
    /// e.g. in CI.
    /// </summary>
    [Fact]
    public void Read_RealArchives_AllEntriesDecompressToStatedSize()
    {
        string? researchDir = FindResearchDir();
        if (researchDir is null)
        {
            return; // no local game data available — skip
        }

        string[] wrsFiles = Directory.EnumerateFiles(researchDir, "*.wrs", SearchOption.AllDirectories)
            .Where(static p => p.EndsWith(".wrs", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (wrsFiles.Length == 0)
        {
            return; // no WRS files extracted/available — skip
        }

        foreach (string path in wrsFiles)
        {
            WrsArchive wrs = WrsArchive.ReadFile(path);
            Assert.NotEmpty(wrs.Entries);

            foreach (WrsEntry entry in wrs.Entries)
            {
                byte[] data = entry.GetData();
                Assert.Equal(entry.UncompressedSize, data.Length);
            }
        }
    }

    private static byte[] BuildArchive(params (string Name, byte[] Payload, int UncompressedSize)[] entries)
    {
        const int headerSize = WrsArchive.NameFieldLength + sizeof(uint) + sizeof(uint);
        int total = sizeof(uint) + entries.Sum(e => headerSize + e.Payload.Length);

        byte[] buffer = new byte[total];
        Span<byte> span = buffer;

        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)total);
        int offset = sizeof(uint);

        foreach ((string name, byte[] payload, int uncompressedSize) in entries)
        {
            WriteName(span.Slice(offset, WrsArchive.NameFieldLength), name);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(offset + WrsArchive.NameFieldLength, sizeof(uint)), (uint)payload.Length);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(offset + WrsArchive.NameFieldLength + sizeof(uint), sizeof(uint)), (uint)uncompressedSize);
            offset += headerSize;

            payload.CopyTo(span.Slice(offset, payload.Length));
            offset += payload.Length;
        }

        return buffer;
    }

    private static void WriteName(Span<byte> field, string name)
    {
        field.Clear();
        Encoding.ASCII.GetBytes(name).CopyTo(field);
    }

    private static string? FindResearchDir()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OpenVirtue.slnx")))
            {
                string research = Path.Combine(dir.FullName, "_research");
                return Directory.Exists(research) ? research : null;
            }
        }

        return null;
    }
}
