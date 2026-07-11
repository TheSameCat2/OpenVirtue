// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;

namespace OpenVirtue.Formats.Flic;

/// <summary>A decoded 8-bit Autodesk FLI/FLC frame expanded to RGBA pixels.</summary>
public sealed record FlicFrame(byte[] Rgba);

/// <summary>A decoded 8-bit Autodesk FLI/FLC animation.</summary>
/// <remarks>
/// Clean-room implementation from published FLI/FLC container and chunk descriptions,
/// checked against sanitized metadata from user-supplied game data. See <c>PROVENANCE.md</c>.
/// </remarks>
public sealed class FlicAnimation
{
    private const int FileHeaderSize = 128;
    private const int FrameHeaderSize = 16;
    private const ushort FliMagic = 0xAF11;
    private const ushort FlcMagic = 0xAF12;
    private const ushort FrameMagic = 0xF1FA;
    private const ushort PrefixMagic = 0xF100;

    private const ushort Color256Chunk = 4;
    private const ushort Ss2Chunk = 7;
    private const ushort Color64Chunk = 11;
    private const ushort LineCompressedChunk = 12;
    private const ushort BlackChunk = 13;
    private const ushort ByteRunChunk = 15;
    private const ushort CopyChunk = 16;

    private FlicAnimation(int width, int height, TimeSpan frameDelay, IReadOnlyList<FlicFrame> frames)
    {
        Width = width;
        Height = height;
        FrameDelay = frameDelay;
        Frames = frames;
    }

    /// <summary>Frame width in pixels.</summary>
    public int Width { get; }

    /// <summary>Frame height in pixels.</summary>
    public int Height { get; }

    /// <summary>Nominal delay between displayed frames.</summary>
    public TimeSpan FrameDelay { get; }

    /// <summary>Displayed frames; an optional trailing ring frame is not included.</summary>
    public IReadOnlyList<FlicFrame> Frames { get; }

    /// <summary>Decodes an 8-bit FLI or FLC animation.</summary>
    public static FlicAnimation Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < FileHeaderSize)
        {
            throw new InvalidDataException("FLIC data is shorter than its 128-byte header.");
        }

        uint declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(data);
        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        int frameCount = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
        int depth = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        if (magic is not (FliMagic or FlcMagic))
        {
            throw new InvalidDataException($"Not an FLI/FLC file (magic 0x{magic:X4}).");
        }

        if (declaredSize > data.Length || declaredSize < FileHeaderSize)
        {
            throw new InvalidDataException("FLIC declared size lies outside the supplied data.");
        }

        if (width <= 0 || height <= 0 || frameCount <= 0)
        {
            throw new InvalidDataException($"FLIC has invalid dimensions or frame count ({width}x{height}, {frameCount} frames).");
        }

        if (depth != 8)
        {
            throw new NotSupportedException($"Only 8-bit paletted FLI/FLC is supported (got {depth}-bit).");
        }

        int pixelCount = checked(width * height);
        uint speed = magic == FliMagic
            ? BinaryPrimitives.ReadUInt16LittleEndian(data[16..])
            : BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
        double delayMilliseconds = magic == FliMagic ? speed * (1000.0 / 70.0) : speed;
        var frameDelay = TimeSpan.FromMilliseconds(Math.Max(1, delayMilliseconds));
        var pixels = new byte[pixelCount];
        var palette = new byte[256 * 3];
        var frames = new List<FlicFrame>(frameCount);

        int offset = FileHeaderSize;
        int end = checked((int)declaredSize);
        while (frames.Count < frameCount && offset < end)
        {
            EnsureAvailable(data, offset, FrameHeaderSize, end, "FLIC frame header is truncated.");
            int frameSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]));
            ushort frameMagic = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 4)..]);
            int chunkCount = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 6)..]);
            if (frameSize < FrameHeaderSize || offset > end - frameSize)
            {
                throw new InvalidDataException("FLIC frame size lies outside the file.");
            }

            if (frameMagic == PrefixMagic)
            {
                offset += frameSize;
                continue;
            }

            if (frameMagic != FrameMagic)
            {
                throw new InvalidDataException($"Invalid FLIC frame magic 0x{frameMagic:X4}.");
            }

            int frameEnd = offset + frameSize;
            int chunkOffset = offset + FrameHeaderSize;
            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                EnsureAvailable(data, chunkOffset, 6, frameEnd, "FLIC chunk header is truncated.");
                int chunkSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[chunkOffset..]));
                ushort chunkType = BinaryPrimitives.ReadUInt16LittleEndian(data[(chunkOffset + 4)..]);
                if (chunkSize < 6 || chunkOffset > frameEnd - chunkSize)
                {
                    throw new InvalidDataException("FLIC chunk size lies outside its frame.");
                }

                ReadOnlySpan<byte> payload = data.Slice(chunkOffset + 6, chunkSize - 6);
                DecodeChunk(payload, chunkType, pixels, palette, width, height);
                chunkOffset += chunkSize;
            }

            frames.Add(new FlicFrame(ToRgba(pixels, palette)));
            offset = frameEnd;
        }

        if (frames.Count != frameCount)
        {
            throw new InvalidDataException($"FLIC ended after {frames.Count} of {frameCount} declared frames.");
        }

        return new FlicAnimation(width, height, frameDelay, frames);
    }

    private static void DecodeChunk(
        ReadOnlySpan<byte> payload,
        ushort type,
        byte[] pixels,
        byte[] palette,
        int width,
        int height)
    {
        switch (type)
        {
            case Color256Chunk:
                DecodePalette(payload, palette, sixBit: false);
                break;
            case Color64Chunk:
                DecodePalette(payload, palette, sixBit: true);
                break;
            case LineCompressedChunk:
                DecodeLineCompressed(payload, pixels, width, height);
                break;
            case ByteRunChunk:
                DecodeByteRun(payload, pixels, width, height);
                break;
            case Ss2Chunk:
                DecodeWordDelta(payload, pixels, width, height);
                break;
            case BlackChunk:
                Array.Clear(pixels);
                break;
            case CopyChunk:
                if (payload.Length < pixels.Length)
                {
                    throw new InvalidDataException("FLIC copy chunk is shorter than the frame raster.");
                }

                payload[..pixels.Length].CopyTo(pixels);
                break;
        }
    }

    private static void DecodePalette(ReadOnlySpan<byte> payload, byte[] palette, bool sixBit)
    {
        var reader = new ChunkReader(payload);
        int packets = reader.ReadUInt16();
        int colorIndex = 0;
        for (int packet = 0; packet < packets; packet++)
        {
            colorIndex += reader.ReadByte();
            int count = reader.ReadByte();
            count = count == 0 ? 256 : count;
            if (colorIndex > 256 - count)
            {
                throw new InvalidDataException("FLIC palette packet exceeds 256 colors.");
            }

            for (int i = 0; i < count; i++, colorIndex++)
            {
                int target = colorIndex * 3;
                palette[target] = ExpandColor(reader.ReadByte(), sixBit);
                palette[target + 1] = ExpandColor(reader.ReadByte(), sixBit);
                palette[target + 2] = ExpandColor(reader.ReadByte(), sixBit);
            }
        }
    }

    private static byte ExpandColor(byte value, bool sixBit) =>
        sixBit ? (byte)((value << 2) | (value >> 4)) : value;

    private static void DecodeLineCompressed(ReadOnlySpan<byte> payload, byte[] pixels, int width, int height)
    {
        var reader = new ChunkReader(payload);
        int y = reader.ReadUInt16();
        int lineCount = reader.ReadUInt16();
        if (y > height - lineCount)
        {
            throw new InvalidDataException("FLIC line-compressed chunk exceeds frame height.");
        }

        for (int line = 0; line < lineCount; line++, y++)
        {
            int packets = reader.ReadByte();
            int x = 0;
            for (int packet = 0; packet < packets; packet++)
            {
                x += reader.ReadByte();
                int count = reader.ReadSByte();
                if (count >= 0)
                {
                    EnsureRasterRange(x, count, width);
                    reader.ReadBytes(count).CopyTo(pixels.AsSpan((y * width) + x, count));
                    x += count;
                }
                else
                {
                    int run = -count;
                    EnsureRasterRange(x, run, width);
                    pixels.AsSpan((y * width) + x, run).Fill(reader.ReadByte());
                    x += run;
                }
            }
        }
    }

    private static void DecodeByteRun(ReadOnlySpan<byte> payload, byte[] pixels, int width, int height)
    {
        var reader = new ChunkReader(payload);
        for (int y = 0; y < height; y++)
        {
            reader.ReadByte(); // packet count is historically unreliable; width terminates the scanline
            int x = 0;
            while (x < width)
            {
                int count = reader.ReadSByte();
                if (count >= 0)
                {
                    EnsureRasterRange(x, count, width);
                    pixels.AsSpan((y * width) + x, count).Fill(reader.ReadByte());
                    x += count;
                }
                else
                {
                    int literalCount = -count;
                    EnsureRasterRange(x, literalCount, width);
                    reader.ReadBytes(literalCount).CopyTo(pixels.AsSpan((y * width) + x, literalCount));
                    x += literalCount;
                }
            }
        }
    }

    private static void DecodeWordDelta(ReadOnlySpan<byte> payload, byte[] pixels, int width, int height)
    {
        var reader = new ChunkReader(payload);
        int remainingLines = reader.ReadUInt16();
        int y = 0;
        while (remainingLines > 0)
        {
            short control = reader.ReadInt16();
            if (control < 0)
            {
                if ((control & 0x4000) != 0)
                {
                    y += -control;
                    if (y > height)
                    {
                        throw new InvalidDataException("FLIC word-delta line skip exceeds frame height.");
                    }
                }
                else
                {
                    if (y >= height)
                    {
                        throw new InvalidDataException("FLIC word-delta last-pixel opcode exceeds frame height.");
                    }

                    pixels[(y * width) + width - 1] = (byte)(control & 0xFF);
                }

                continue;
            }

            if (y >= height)
            {
                throw new InvalidDataException("FLIC word-delta chunk exceeds frame height.");
            }

            int x = 0;
            for (int packet = 0; packet < control; packet++)
            {
                x += reader.ReadByte();
                int wordCount = reader.ReadSByte();
                int byteCount = Math.Abs(wordCount) * 2;
                EnsureRasterRange(x, byteCount, width);
                if (wordCount >= 0)
                {
                    reader.ReadBytes(byteCount).CopyTo(pixels.AsSpan((y * width) + x, byteCount));
                }
                else
                {
                    ReadOnlySpan<byte> word = reader.ReadBytes(2);
                    for (int i = 0; i < -wordCount; i++)
                    {
                        pixels[(y * width) + x + (i * 2)] = word[0];
                        pixels[(y * width) + x + (i * 2) + 1] = word[1];
                    }
                }

                x += byteCount;
            }

            y++;
            remainingLines--;
        }
    }

    private static byte[] ToRgba(ReadOnlySpan<byte> pixels, ReadOnlySpan<byte> palette)
    {
        var rgba = new byte[pixels.Length * 4];
        int target = 0;
        foreach (byte index in pixels)
        {
            int source = index * 3;
            rgba[target++] = palette[source];
            rgba[target++] = palette[source + 1];
            rgba[target++] = palette[source + 2];
            rgba[target++] = 0xFF;
        }

        return rgba;
    }

    private static void EnsureRasterRange(int x, int count, int width)
    {
        if (count < 0 || x < 0 || x > width - count)
        {
            throw new InvalidDataException("FLIC packet exceeds its scanline.");
        }
    }

    private static void EnsureAvailable(
        ReadOnlySpan<byte> data,
        int offset,
        int count,
        int end,
        string message)
    {
        if (offset < 0 || count < 0 || offset > end - count || end > data.Length)
        {
            throw new InvalidDataException(message);
        }
    }

    private ref struct ChunkReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public ChunkReader(ReadOnlySpan<byte> data) => _data = data;

        public byte ReadByte()
        {
            Ensure(1);
            return _data[_offset++];
        }

        public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

        public ushort ReadUInt16()
        {
            Ensure(2);
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_data[_offset..]);
            _offset += 2;
            return value;
        }

        public short ReadInt16()
        {
            Ensure(2);
            short value = BinaryPrimitives.ReadInt16LittleEndian(_data[_offset..]);
            _offset += 2;
            return value;
        }

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            Ensure(count);
            ReadOnlySpan<byte> value = _data.Slice(_offset, count);
            _offset += count;
            return value;
        }

        private void Ensure(int count)
        {
            if (count < 0 || _offset > _data.Length - count)
            {
                throw new InvalidDataException("FLIC chunk payload is truncated.");
            }
        }
    }
}
