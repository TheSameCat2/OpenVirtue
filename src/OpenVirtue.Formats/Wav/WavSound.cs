// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.Text;

namespace OpenVirtue.Formats.Wav;

/// <summary>
/// A decoded RIFF/WAVE sound. Saints of Virtue's sounds are all uncompressed PCM
/// (mostly 8-bit mono at 11025 Hz).
/// </summary>
/// <remarks>
/// Clean-room implementation of the public Microsoft RIFF/WAVE container: a
/// <c>RIFF</c>…<c>WAVE</c> wrapper enclosing a <c>fmt </c> chunk (format parameters)
/// and a <c>data</c> chunk (samples); any other chunks are skipped. See
/// <c>PROVENANCE.md</c>.
/// </remarks>
public sealed class WavSound
{
    /// <summary>WAVE format tag; 1 means uncompressed PCM.</summary>
    public const int FormatPcm = 1;

    private WavSound(int audioFormat, int channels, int sampleRate, int bitsPerSample, byte[] data)
    {
        AudioFormat = audioFormat;
        Channels = channels;
        SampleRate = sampleRate;
        BitsPerSample = bitsPerSample;
        Data = data;
    }

    /// <summary>The WAVE format tag (<see cref="FormatPcm"/> for PCM).</summary>
    public int AudioFormat { get; }

    /// <summary>Number of channels (1 = mono, 2 = stereo).</summary>
    public int Channels { get; }

    /// <summary>Sample rate in hertz.</summary>
    public int SampleRate { get; }

    /// <summary>Bits per sample (e.g. 8 or 16).</summary>
    public int BitsPerSample { get; }

    /// <summary>Raw sample bytes from the <c>data</c> chunk (8-bit PCM is unsigned; 16-bit is signed little-endian).</summary>
    public byte[] Data { get; }

    /// <summary>Parses a RIFF/WAVE sound from its bytes.</summary>
    /// <exception cref="InvalidDataException">The data is not a valid WAVE file or lacks a fmt/data chunk.</exception>
    public static WavSound Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !Matches(data, 0, "RIFF") || !Matches(data, 8, "WAVE"))
        {
            throw new InvalidDataException("Not a RIFF/WAVE file.");
        }

        int audioFormat = 0, channels = 0, sampleRate = 0, bitsPerSample = 0;
        byte[]? samples = null;
        bool haveFmt = false;

        int pos = 12;
        while (pos + 8 <= data.Length)
        {
            ReadOnlySpan<byte> id = data.Slice(pos, 4);
            long size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos + 4, 4));
            int body = pos + 8;
            if (body + size > data.Length)
            {
                size = data.Length - body; // tolerate a truncated final chunk
            }

            if (Matches(id, "fmt ") && size >= 16)
            {
                ReadOnlySpan<byte> fmt = data.Slice(body, (int)size);
                audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
                sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
                haveFmt = true;
            }
            else if (Matches(id, "data"))
            {
                samples = data.Slice(body, (int)size).ToArray();
            }

            // Chunks are padded to an even number of bytes.
            pos = body + (int)size + ((size & 1) == 1 ? 1 : 0);
        }

        if (!haveFmt)
        {
            throw new InvalidDataException("WAVE file is missing its 'fmt ' chunk.");
        }

        if (samples is null)
        {
            throw new InvalidDataException("WAVE file is missing its 'data' chunk.");
        }

        return new WavSound(audioFormat, channels, sampleRate, bitsPerSample, samples);
    }

    /// <summary>Reads and parses a WAVE sound from a file on disk.</summary>
    public static WavSound ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Read(File.ReadAllBytes(path));
    }

    private static bool Matches(ReadOnlySpan<byte> data, int offset, string tag) =>
        Matches(data.Slice(offset, 4), tag);

    private static bool Matches(ReadOnlySpan<byte> id, string tag) =>
        id.SequenceEqual(Encoding.ASCII.GetBytes(tag));
}
