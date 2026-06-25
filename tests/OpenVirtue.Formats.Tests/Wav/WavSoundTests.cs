// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.Text;
using OpenVirtue.Formats.Tests.TestSupport;
using OpenVirtue.Formats.Wav;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Formats.Tests.Wav;

public class WavSoundTests
{
    [Fact]
    public void Read_ParsesPcmFormatAndData()
    {
        byte[] samples = [0x80, 0x81, 0x82, 0x83];
        byte[] wav = Wav(("fmt ", Fmt(channels: 1, rate: 11025, bits: 8)), ("data", samples));

        WavSound sound = WavSound.Read(wav);

        Assert.Equal(WavSound.FormatPcm, sound.AudioFormat);
        Assert.Equal(1, sound.Channels);
        Assert.Equal(11025, sound.SampleRate);
        Assert.Equal(8, sound.BitsPerSample);
        Assert.Equal(samples, sound.Data);
    }

    [Fact]
    public void Read_SkipsUnknownChunksAndOddPadding()
    {
        byte[] samples = [1, 2, 3, 4, 5, 6];
        // An odd-length "junk" chunk (3 bytes) between fmt and data exercises chunk
        // skipping and even-boundary padding.
        byte[] wav = Wav(
            ("fmt ", Fmt(channels: 2, rate: 22050, bits: 16)),
            ("junk", [0xAA, 0xBB, 0xCC]),
            ("data", samples));

        WavSound sound = WavSound.Read(wav);

        Assert.Equal(2, sound.Channels);
        Assert.Equal(22050, sound.SampleRate);
        Assert.Equal(16, sound.BitsPerSample);
        Assert.Equal(samples, sound.Data);
    }

    [Fact]
    public void Read_NotWave_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WavSound.Read(new byte[12]));
    }

    /// <summary>Decodes every sound in the real archives (when present locally). No-op otherwise.</summary>
    [Fact]
    public void Read_RealWavFromArchives_AllPcm()
    {
        IReadOnlyList<string> wrsFiles = ResearchData.WrsFiles();
        if (wrsFiles.Count == 0)
        {
            return; // no local game data — skip
        }

        int sounds = 0;
        foreach (string path in wrsFiles)
        {
            WrsArchive archive = WrsArchive.ReadFile(path);
            foreach (WrsEntry entry in archive.Entries)
            {
                if (!entry.Name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                WavSound sound = WavSound.Read(entry.GetData());

                Assert.Equal(WavSound.FormatPcm, sound.AudioFormat);
                Assert.True(sound.Channels >= 1, $"{entry.Name} has no channels");
                Assert.True(sound.SampleRate > 0, $"{entry.Name} has no sample rate");
                Assert.NotEmpty(sound.Data);
                sounds++;
            }
        }

        Assert.True(sounds > 0, "expected to decode at least one real WAV");
    }

    private static byte[] Fmt(int channels, int rate, int bits)
    {
        int blockAlign = channels * bits / 8;
        byte[] b = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(b, 1); // PCM
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), (ushort)channels);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), (uint)rate);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), (uint)(rate * blockAlign));
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(12), (ushort)blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(14), (ushort)bits);
        return b;
    }

    private static byte[] Wav(params (string Tag, byte[] Body)[] chunks)
    {
        using var ms = new MemoryStream();
        Encoding ascii = Encoding.ASCII;

        void U32(uint v)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, v);
            ms.Write(b);
        }

        ms.Write(ascii.GetBytes("RIFF"));
        long sizePos = ms.Position;
        U32(0); // RIFF size — patched below
        ms.Write(ascii.GetBytes("WAVE"));

        foreach ((string tag, byte[] body) in chunks)
        {
            ms.Write(ascii.GetBytes(tag));
            U32((uint)body.Length);
            ms.Write(body);
            if ((body.Length & 1) == 1)
            {
                ms.WriteByte(0); // pad to even boundary
            }
        }

        long end = ms.Position;
        ms.Position = sizePos;
        U32((uint)(end - sizePos - 4));
        ms.Position = end;
        return ms.ToArray();
    }
}
