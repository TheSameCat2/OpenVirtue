// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text;
using OpenVirtue.Formats.Flic;
using OpenVirtue.Formats.Tests.TestSupport;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Formats.Tests.Flic;

public class FlicAnimationTests
{
    [Fact]
    public void Read_FlcPaletteCopyAndLineDelta_ProducesStatefulFrames()
    {
        byte[] palette = PaletteChunk(
            (1, 255, 0, 0),
            (2, 0, 255, 0),
            (3, 0, 0, 255));
        byte[] copy = [1, 1, 1, 1];
        byte[] delta =
        [
            0, 0, // first changed line
            1, 0, // changed line count
            1,    // packet count
            1,    // skip one pixel
            2,    // two literal pixels
            2, 3,
        ];
        byte[] bytes = BuildFlc(
            width: 4,
            height: 1,
            speedMilliseconds: 55,
            [(4, palette), (16, copy)],
            [(12, delta)]);

        FlicAnimation animation = FlicAnimation.Read(bytes);

        Assert.Equal(4, animation.Width);
        Assert.Equal(1, animation.Height);
        Assert.Equal(TimeSpan.FromMilliseconds(55), animation.FrameDelay);
        Assert.Equal(2, animation.Frames.Count);
        Assert.Equal(
            new byte[]
            {
                255, 0, 0, 255,
                255, 0, 0, 255,
                255, 0, 0, 255,
                255, 0, 0, 255,
            },
            animation.Frames[0].Rgba);
        Assert.Equal(
            new byte[]
            {
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                255, 0, 0, 255,
            },
            animation.Frames[1].Rgba);
    }

    [Fact]
    public void Read_FlcByteRunAndSixBitPalette_DecodesScanline()
    {
        byte[] sixBitPalette = PaletteChunk((2, 63, 32, 0));
        byte[] byteRun = [1, 4, 2];
        byte[] bytes = BuildFlc(
            width: 4,
            height: 1,
            speedMilliseconds: 40,
            [(11, sixBitPalette), (15, byteRun)]);

        FlicFrame frame = Assert.Single(FlicAnimation.Read(bytes).Frames);

        Assert.Equal(
            Enumerable.Repeat(new byte[] { 255, 130, 0, 255 }, 4).SelectMany(pixel => pixel).ToArray(),
            frame.Rgba);
    }

    [Fact]
    public void Read_FlcWordDeltaAndBlack_DecodesControlOpcodesAndClearsRaster()
    {
        byte[] palette = PaletteChunk(
            (1, 255, 0, 0),
            (2, 0, 255, 0),
            (3, 0, 0, 255));
        byte[] wordDelta =
        [
            2, 0,       // two changed lines
            0xFF, 0xFF, // skip one line
            3, 0x80,    // set the last pixel to palette index 3
            1, 0,       // one packet
            0, 1,       // skip zero, copy one literal word
            1, 2,
            1, 0,       // one packet on the next line
            1, 0xFF,    // skip one, repeat one word
            2, 1,
        ];
        byte[] bytes = BuildFlc(
            width: 4,
            height: 3,
            speedMilliseconds: 50,
            [(4, palette), (16, new byte[12])],
            [(7, wordDelta)],
            [(13, [])]);

        FlicAnimation animation = FlicAnimation.Read(bytes);

        Assert.Equal(
            new byte[]
            {
                0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255,
                255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 0, 255, 0, 0, 255, 255,
                0, 0, 0, 255, 0, 255, 0, 255, 255, 0, 0, 255, 0, 0, 0, 255,
            },
            animation.Frames[1].Rgba);
        Assert.All(animation.Frames[2].Rgba, value =>
            Assert.True(value is 0 or 255));
        Assert.All(
            animation.Frames[2].Rgba.Where((_, index) => index % 4 != 3),
            value => Assert.Equal(0, value));
    }

    [Fact]
    public void Read_InvalidMagic_Throws()
    {
        var bytes = new byte[128];
        BitConverter.GetBytes((uint)bytes.Length).CopyTo(bytes, 0);

        Assert.Throws<InvalidDataException>(() => FlicAnimation.Read(bytes));
    }

    [Fact]
    public void Read_TruncatedChunk_Throws()
    {
        byte[] bytes = BuildFlc(2, 1, 50, [(16, new byte[] { 1 })]);

        Assert.Throws<InvalidDataException>(() => FlicAnimation.Read(bytes));
    }

    [Fact]
    public void Read_RealTitleFlic_DecodesDeclaredFrames()
    {
        string? title = ResearchData.WrsFiles()
            .FirstOrDefault(path => Path.GetFileName(path).Equals("TITLE.WRS", StringComparison.OrdinalIgnoreCase));
        if (title is null)
        {
            return;
        }

        WrsArchive archive = WrsArchive.ReadFile(title);
        WrsEntry? entry = archive.Entries
            .FirstOrDefault(candidate => candidate.Name.Equals("logo.flc", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);

        FlicAnimation animation = FlicAnimation.Read(entry!.GetData());

        Assert.Equal(320, animation.Width);
        Assert.Equal(200, animation.Height);
        Assert.Equal(TimeSpan.FromMilliseconds(55), animation.FrameDelay);
        Assert.Equal(239, animation.Frames.Count);
        Assert.All(animation.Frames, frame => Assert.Equal(320 * 200 * 4, frame.Rgba.Length));
    }

    private static byte[] PaletteChunk(params (byte Index, byte R, byte G, byte B)[] colors)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        writer.Write((ushort)colors.Length);
        int previous = 0;
        foreach ((byte index, byte r, byte g, byte b) in colors)
        {
            writer.Write((byte)(index - previous));
            writer.Write((byte)1);
            writer.Write(r);
            writer.Write(g);
            writer.Write(b);
            previous = index + 1;
        }

        return output.ToArray();
    }

    private static byte[] BuildFlc(
        int width,
        int height,
        uint speedMilliseconds,
        params (ushort Type, byte[] Payload)[][] frames)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        writer.Write(new byte[128]);
        foreach ((ushort Type, byte[] Payload)[] frame in frames)
        {
            long frameStart = output.Position;
            writer.Write(0u);
            writer.Write((ushort)0xF1FA);
            writer.Write((ushort)frame.Length);
            writer.Write(new byte[8]);
            foreach ((ushort type, byte[] payload) in frame)
            {
                writer.Write((uint)(payload.Length + 6));
                writer.Write(type);
                writer.Write(payload);
            }

            long end = output.Position;
            output.Position = frameStart;
            writer.Write((uint)(end - frameStart));
            output.Position = end;
        }

        byte[] result = output.ToArray();
        BitConverter.GetBytes((uint)result.Length).CopyTo(result, 0);
        BitConverter.GetBytes((ushort)0xAF12).CopyTo(result, 4);
        BitConverter.GetBytes((ushort)frames.Length).CopyTo(result, 6);
        BitConverter.GetBytes((ushort)width).CopyTo(result, 8);
        BitConverter.GetBytes((ushort)height).CopyTo(result, 10);
        BitConverter.GetBytes((ushort)8).CopyTo(result, 12);
        BitConverter.GetBytes(speedMilliseconds).CopyTo(result, 16);
        return result;
    }
}
