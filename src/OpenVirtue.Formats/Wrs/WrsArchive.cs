// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.Text;
using OpenVirtue.Formats.Compression;

namespace OpenVirtue.Formats.Wrs;

/// <summary>
/// Reader for Acknex-3 <c>WRS</c> resource archives — the packed, LZSS-compressed
/// bundles that ship a game's WDL scripts, maps, textures, and sounds.
/// </summary>
/// <remarks>
/// <para>Archive layout (all multi-byte integers are big-endian), reconstructed
/// from the documented recipe and confirmed against real archives (see
/// <c>PROVENANCE.md</c>). There is <b>no file header</b>: fixed-size records run
/// from offset 0 to the end of the file.</para>
/// <code>
/// repeat while offset &lt; fileSize:
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

        // The format has no file header: records run from offset 0 to end of file.
        // (In the QuickBMS recipe, `get asize asize` reads the file-size pseudo-value
        // and consumes no bytes — it only bounds the loop.)
        var entries = new List<WrsEntry>();
        int offset = 0;

        while (offset < span.Length)
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

    /// <summary>
    /// Builds a WRS archive from uncompressed files.
    /// </summary>
    /// <remarks>
    /// Payloads are encoded with a literal-only LZSS stream. The result is larger
    /// than a size-optimized archive but remains valid and easy to audit.
    /// </remarks>
    public static byte[] Write(IEnumerable<WrsFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        using var output = new MemoryStream();
        Span<byte> sizeBuffer = stackalloc byte[sizeof(uint)];

        foreach (WrsFile file in files)
        {
            byte[] data = file.Data.ToArray();
            byte[] compressed = Lzss.Compress(data);

            WriteFixedName(output, file.Name);
            BinaryPrimitives.WriteUInt32BigEndian(sizeBuffer, checked((uint)compressed.Length));
            output.Write(sizeBuffer);
            BinaryPrimitives.WriteUInt32BigEndian(sizeBuffer, checked((uint)data.Length));
            output.Write(sizeBuffer);
            output.Write(compressed);
        }

        return output.ToArray();
    }

    /// <summary>Writes a WRS archive to disk from uncompressed files.</summary>
    public static void WriteFile(string path, IEnumerable<WrsFile> files)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        File.WriteAllBytes(path, Write(files));
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

    private static void WriteFixedName(Stream output, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new ArgumentException($"WRS entry name must not contain a path separator: '{name}'.", nameof(name));
        }

        if (name.Any(c => c > 0x7F))
        {
            throw new ArgumentException($"WRS entry name must be ASCII: '{name}'.", nameof(name));
        }

        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        if (nameBytes.Length > NameFieldLength)
        {
            throw new ArgumentException(
                $"WRS entry name '{name}' is {nameBytes.Length} bytes; maximum is {NameFieldLength}.",
                nameof(name));
        }

        Span<byte> field = stackalloc byte[NameFieldLength];
        field.Clear();
        nameBytes.CopyTo(field);
        output.Write(field);
    }
}
