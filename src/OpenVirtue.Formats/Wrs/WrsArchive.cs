// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.Text;

namespace OpenVirtue.Formats.Wrs;

/// <summary>
/// Reader for Acknex-3 <c>WRS</c> resource archives — the packed, LZSS-compressed
/// bundles that ship a game's WDL scripts, maps, textures, and sounds.
/// </summary>
/// <remarks>
/// <para>Archive layout (all multi-byte integers are big-endian), reconstructed
/// from the documented structure of the format (see <c>PROVENANCE.md</c>):</para>
/// <code>
/// u32           archiveSize          // total archive length; bounds the entry loop
/// repeat while offset &lt; archiveSize:
///     char[13]  name                 // NUL-padded file name
///     u32       compressedSize        // LZSS payload length
///     u32       uncompressedSize      // size after decompression
///     byte[compressedSize] payload    // LZSS data
/// </code>
/// </remarks>
public sealed class WrsArchive
{
    /// <summary>Length of the fixed-width name field in each entry header.</summary>
    public const int NameFieldLength = 13;

    /// <summary>Total bytes of an entry header: name(13) + compressedSize(4) + uncompressedSize(4).</summary>
    private const int EntryHeaderSize = NameFieldLength + sizeof(uint) + sizeof(uint);

    private WrsArchive(IReadOnlyList<WrsEntry> entries) => Entries = entries;

    /// <summary>The archive's members, in stored order.</summary>
    public IReadOnlyList<WrsEntry> Entries { get; }

    /// <summary>Parses a WRS archive held entirely in memory.</summary>
    /// <param name="data">The complete archive bytes. Entry payloads reference this buffer, so keep it alive.</param>
    /// <exception cref="InvalidDataException">The data is not a structurally valid WRS archive.</exception>
    public static WrsArchive Read(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (span.Length < sizeof(uint))
        {
            throw new InvalidDataException("WRS data is too short to contain an archive-size header.");
        }

        long archiveSize = BinaryPrimitives.ReadUInt32BigEndian(span);
        // Defensively clamp the loop bound to the bytes we actually have.
        long bound = Math.Min(archiveSize, span.Length);

        var entries = new List<WrsEntry>();
        int offset = sizeof(uint); // first entry header follows the archive-size field

        while (offset < bound)
        {
            if (offset + EntryHeaderSize > span.Length)
            {
                throw new InvalidDataException($"Truncated WRS entry header at offset {offset}.");
            }

            string name = ReadFixedName(span.Slice(offset, NameFieldLength));
            uint compressedSize = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset + NameFieldLength, sizeof(uint)));
            uint uncompressedSize = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset + NameFieldLength + sizeof(uint), sizeof(uint)));

            int dataOffset = offset + EntryHeaderSize;
            if (compressedSize > (uint)(span.Length - dataOffset))
            {
                throw new InvalidDataException(
                    $"WRS entry '{name}' claims a compressed size of {compressedSize} bytes that overruns the archive.");
            }

            var payload = data.Slice(dataOffset, (int)compressedSize);
            entries.Add(new WrsEntry(name, (int)compressedSize, (int)uncompressedSize, payload));

            offset = dataOffset + (int)compressedSize;
        }

        return new WrsArchive(entries);
    }

    /// <summary>Reads and parses a WRS archive from a stream (buffered fully into memory).</summary>
    public static WrsArchive Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Read(buffer.ToArray());
    }

    /// <summary>Reads and parses a WRS archive from a file on disk.</summary>
    public static WrsArchive ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Read(File.ReadAllBytes(path));
    }

    private static string ReadFixedName(ReadOnlySpan<byte> field)
    {
        int nul = field.IndexOf((byte)0);
        if (nul >= 0)
        {
            field = field[..nul];
        }

        return Encoding.ASCII.GetString(field).Trim();
    }
}
