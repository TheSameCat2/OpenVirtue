// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Formats.Compression;

/// <summary>
/// Decoder for the LZSS variant used inside Acknex-3 <c>WRS</c> resource archives
/// (the codec QuickBMS refers to as <c>comtype lzss</c>).
/// </summary>
/// <remarks>
/// <para>
/// Clean-room implementation of the classic, public-domain LZSS algorithm
/// (Haruhiko Okumura, 1988), reconstructed from its published description — not
/// ported from any third-party source. See <c>PROVENANCE.md</c>.
/// </para>
/// <para>Parameters (the canonical LZSS defaults):</para>
/// <list type="bullet">
///   <item><description>4096-byte ring buffer (window).</description></item>
///   <item><description>Maximum match length 18; minimum encoded match length 3.</description></item>
///   <item><description>Control bits packed least-significant-bit first; a set bit marks a literal.</description></item>
///   <item><description>Match reference: low offset byte, then a byte packing the high offset nibble and the length nibble.</description></item>
///   <item><description>Ring buffer pre-filled with spaces (0x20).</description></item>
/// </list>
/// <para>
/// Byte-exact agreement with the original engine is verified against real WRS
/// data; the points most likely to vary between LZSS dialects (flag-bit order,
/// length bias, dictionary fill) are called out in <c>PROVENANCE.md</c> so they
/// can be adjusted quickly if validation ever disagrees.
/// </para>
/// </remarks>
public static class Lzss
{
    private const int RingSize = 4096;   // N — window size (power of two)
    private const int RingMask = RingSize - 1;
    private const int MaxMatch = 18;     // F — longest encodable match
    private const int Threshold = 2;     // matches shorter than Threshold+1 are stored as literals
    private const byte RingFill = 0x20;  // initial ring-buffer byte (space)

    /// <summary>
    /// Decompresses an LZSS stream into a buffer of exactly
    /// <paramref name="decompressedSize"/> bytes.
    /// </summary>
    /// <param name="input">The compressed bytes.</param>
    /// <param name="decompressedSize">The expected size of the decompressed output (known from the archive header).</param>
    /// <returns>The decompressed bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="decompressedSize"/> is negative.</exception>
    /// <exception cref="InvalidDataException">The stream ended before producing <paramref name="decompressedSize"/> bytes.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> input, int decompressedSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decompressedSize);

        var output = new byte[decompressedSize];
        if (decompressedSize == 0)
        {
            return output;
        }

        Span<byte> ring = new byte[RingSize];
        ring.Fill(RingFill);

        int writePos = RingSize - MaxMatch; // where the next byte enters the ring buffer
        int inPos = 0;
        int outPos = 0;
        int flags = 0;          // remaining control bits for the current group
        int flagsLeft = 0;

        while (outPos < decompressedSize)
        {
            if (flagsLeft == 0)
            {
                if (inPos >= input.Length)
                {
                    ThrowTruncated();
                }

                flags = input[inPos++];
                flagsLeft = 8;
            }

            bool isLiteral = (flags & 1) != 0;
            flags >>= 1;
            flagsLeft--;

            if (isLiteral)
            {
                if (inPos >= input.Length)
                {
                    ThrowTruncated();
                }

                byte value = input[inPos++];
                output[outPos++] = value;
                ring[writePos] = value;
                writePos = (writePos + 1) & RingMask;
            }
            else
            {
                if (inPos + 1 >= input.Length)
                {
                    ThrowTruncated();
                }

                int low = input[inPos++];
                int packed = input[inPos++];
                int sourcePos = ((packed & 0xF0) << 4) | low;   // 12-bit window offset
                int length = (packed & 0x0F) + Threshold + 1;   // 3..18

                for (int k = 0; k < length && outPos < decompressedSize; k++)
                {
                    // Copy through the ring buffer so overlapping (run-length) matches work.
                    byte value = ring[(sourcePos + k) & RingMask];
                    output[outPos++] = value;
                    ring[writePos] = value;
                    writePos = (writePos + 1) & RingMask;
                }
            }
        }

        return output;
    }

    private static void ThrowTruncated() =>
        throw new InvalidDataException(
            "LZSS stream ended before producing the expected number of decompressed bytes.");
}
