// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.Text;

/// <summary>
/// Writes the probes' sound files: 8-bit unsigned mono PCM <c>.WAV</c> files (the only kind the DOS
/// engine accepts, manual p.25) holding a sine tone, so a sound's length is known to the sample.
/// </summary>
internal static class SyntheticWav
{
    /// <summary>A <paramref name="samples"/>-sample tone of <paramref name="toneHz"/> at <paramref name="rate"/> Hz.</summary>
    public static byte[] Tone(int rate, int samples, int toneHz)
    {
        byte[] file = new byte[44 + samples];
        Span<byte> span = file;
        Encoding.ASCII.GetBytes("RIFF").CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + samples);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1); // mono
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], rate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], rate); // bytes per second
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 1); // block align
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 8); // bits per sample
        Encoding.ASCII.GetBytes("data").CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], samples);
        for (int i = 0; i < samples; i++)
        {
            file[44 + i] = (byte)(128 + Math.Round(96 * Math.Sin(2 * Math.PI * toneHz * i / rate)));
        }

        return file;
    }
}
