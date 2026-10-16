// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using OpenVirtue.Formats.Flic;

/// <summary>
/// Writes a 320x200 8-bit FLC animation for synthetic oracle fixtures: a solid frame, then one
/// scanline repainted per frame (a sweep that shows the flic is live), then a ring frame back to
/// the first image. The container follows the published Autodesk FLC layout (128-byte header,
/// <c>0xF1FA</c> frames, <c>COLOR_256</c>, <c>BRUN</c> and line-delta chunks). The chunk mix is
/// the one the retail title flic uses (a structural count of its chunk types, no pixel data), so
/// the original runtime is known to play it. Nothing here derives from retail art.
/// </summary>
internal static class SyntheticFlic
{
    public const int Width = 320;
    public const int Height = 200;

    private const ushort FlcMagic = 0xAF12;
    private const ushort FrameMagic = 0xF1FA;
    private const ushort Color256Chunk = 4;
    private const ushort LineDeltaChunk = 12;
    private const ushort ByteRunChunk = 15;

    /// <summary>The background colour index of every frame.</summary>
    public const byte Background = 1;

    /// <summary>The colour index of the sweep lines.</summary>
    public const byte Sweep = 2;

    /// <summary>
    /// Writes an FLC of <paramref name="frames"/> displayed frames (plus the ring frame) at
    /// <paramref name="speedMs"/> milliseconds per frame.
    /// </summary>
    public static byte[] Write(int frames, int speedMs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frames, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(speedMs, 1);

        var frameRecords = new List<byte[]>(frames + 1)
        {
            Frame(Color256(), ByteRunFill(Background)),
        };
        for (int k = 1; k < frames; k++)
        {
            // Frame k paints line (k - 1) % 200; after 200 frames the sweep turns back to the background.
            byte colour = ((k - 1) / Height) % 2 == 0 ? Sweep : Background;
            frameRecords.Add(Frame(LineDeltaFill((k - 1) % Height, 1, colour)));
        }

        frameRecords.Add(Frame(LineDeltaFill(0, Height, Background))); // ring frame: back to frame 1

        int size = 128 + frameRecords.Sum(static f => f.Length);
        byte[] data = new byte[size];
        Span<byte> header = data.AsSpan(0, 128);
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)size);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], FlcMagic);
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], (ushort)frames);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], Width);
        BinaryPrimitives.WriteUInt16LittleEndian(header[10..], Height);
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], 8);
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], 3); // flags: finished and looped
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], (uint)speedMs);
        BinaryPrimitives.WriteUInt32LittleEndian(header[80..], 128);
        BinaryPrimitives.WriteUInt32LittleEndian(header[84..], (uint)(128 + frameRecords[0].Length));

        int offset = 128;
        foreach (byte[] frame in frameRecords)
        {
            frame.CopyTo(data, offset);
            offset += frame.Length;
        }

        return data;
    }

    /// <summary>Writes the FLC and checks that the engine's own reader decodes it as intended.</summary>
    public static byte[] WriteValidated(int frames, int speedMs)
    {
        byte[] data = Write(frames, speedMs);
        FlicAnimation animation = FlicAnimation.Read(data);
        if (animation.Width != Width || animation.Height != Height || animation.Frames.Count != frames
            || animation.FrameDelay != TimeSpan.FromMilliseconds(speedMs))
        {
            throw new InvalidOperationException("The synthetic FLC does not read back as written.");
        }

        return data;
    }

    private static byte[] Frame(params byte[][] chunks)
    {
        int size = 16 + chunks.Sum(static c => c.Length);
        byte[] frame = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)size);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), FrameMagic);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6), (ushort)chunks.Length);
        int offset = 16;
        foreach (byte[] chunk in chunks)
        {
            chunk.CopyTo(frame, offset);
            offset += chunk.Length;
        }

        return frame;
    }

    private static byte[] Chunk(ushort type, byte[] payload)
    {
        int size = 6 + payload.Length;
        size += size % 2; // chunks are padded to an even size
        byte[] chunk = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(chunk, (uint)size);
        BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(4), type);
        payload.CopyTo(chunk, 6);
        return chunk;
    }

    /// <summary>One packet of 256 colours: black, a dark blue background, a yellow sweep, then a grey ramp.</summary>
    private static byte[] Color256()
    {
        byte[] payload = new byte[4 + (256 * 3)];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, 1); // one packet, skip 0, count 0 = 256
        Span<byte> rgb = payload.AsSpan(4);
        for (int i = 0; i < 256; i++)
        {
            rgb[i * 3] = rgb[(i * 3) + 1] = rgb[(i * 3) + 2] = (byte)i;
        }

        rgb[0] = rgb[1] = rgb[2] = 0;
        rgb[3] = 0x10; rgb[4] = 0x20; rgb[5] = 0x80;
        rgb[6] = 0xF0; rgb[7] = 0xE0; rgb[8] = 0x20;
        return Chunk(Color256Chunk, payload);
    }

    /// <summary>Run lengths that cover one 320-pixel line with signed-byte counts.</summary>
    private static readonly int[] LineRuns = [127, 127, 66];

    private static byte[] ByteRunFill(byte colour)
    {
        using var payload = new MemoryStream();
        for (int y = 0; y < Height; y++)
        {
            payload.WriteByte((byte)LineRuns.Length);
            foreach (int run in LineRuns)
            {
                payload.WriteByte((byte)run); // positive count: repeat the next byte
                payload.WriteByte(colour);
            }
        }

        return Chunk(ByteRunChunk, payload.ToArray());
    }

    private static byte[] LineDeltaFill(int firstLine, int lines, byte colour)
    {
        using var payload = new MemoryStream();
        Span<byte> word = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(word, (ushort)firstLine);
        payload.Write(word);
        BinaryPrimitives.WriteUInt16LittleEndian(word, (ushort)lines);
        payload.Write(word);
        for (int line = 0; line < lines; line++)
        {
            payload.WriteByte((byte)LineRuns.Length);
            foreach (int run in LineRuns)
            {
                payload.WriteByte(0); // skip
                payload.WriteByte(unchecked((byte)(sbyte)-run)); // negative count: fill with the next byte
                payload.WriteByte(colour);
            }
        }

        return Chunk(LineDeltaChunk, payload.ToArray());
    }
}
