// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Formats.Compression;

namespace OpenVirtue.Formats.Wrs;

/// <summary>
/// A single member of a <see cref="WrsArchive"/>: its name, sizes, and a view over
/// its (still-compressed) bytes. Decompression is performed lazily by
/// <see cref="GetData"/> so that enumerating an archive is cheap.
/// </summary>
public sealed class WrsEntry
{
    private readonly ReadOnlyMemory<byte> _compressed;

    internal WrsEntry(string name, int compressedSize, int uncompressedSize, ReadOnlyMemory<byte> compressed)
    {
        Name = name;
        CompressedSize = compressedSize;
        UncompressedSize = uncompressedSize;
        _compressed = compressed;
    }

    /// <summary>The stored file name (e.g. <c>GLOBALS.WDL</c>), trimmed of its NUL padding.</summary>
    public string Name { get; }

    /// <summary>Size in bytes of the LZSS-compressed payload.</summary>
    public int CompressedSize { get; }

    /// <summary>Size in bytes of the payload after decompression.</summary>
    public int UncompressedSize { get; }

    /// <summary>Decompresses this entry's payload.</summary>
    /// <returns>The decompressed bytes (length <see cref="UncompressedSize"/>).</returns>
    /// <exception cref="InvalidDataException">The payload is not a valid LZSS stream of the expected length.</exception>
    public byte[] GetData() => Lzss.Decompress(_compressed.Span, UncompressedSize);

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({CompressedSize} -> {UncompressedSize} bytes)";
}
