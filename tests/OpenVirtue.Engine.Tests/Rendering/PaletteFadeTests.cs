// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

/// <summary>OV-20261104-fade-pal: the DAC palette a FADE_PAL shows.</summary>
public sealed class PaletteFadeTests
{
    private static byte[] Uniform(byte r, byte g, byte b)
    {
        byte[] palette = new byte[PaletteFade.Length];
        for (int i = 0; i < 256; i++)
        {
            palette[i * 3] = r;
            palette[(i * 3) + 1] = g;
            palette[(i * 3) + 2] = b;
        }

        return palette;
    }

    [Fact]
    public void Show_CutsEachChannelToSixBitsAndWidensLikeTheDac()
    {
        byte[] palette = Uniform(255, 220, 200);

        byte[] shown = PaletteFade.Show(palette);

        // 255 -> 63 -> 255, 220 -> 55 -> 223, 200 -> 50 -> 203 (hold: the red wall read 223, 32, 32).
        Assert.Equal((byte)255, shown[0]);
        Assert.Equal((byte)223, shown[1]);
        Assert.Equal((byte)203, shown[2]);
    }

    [Fact]
    public void Blend_HalfTowardBlack_HalvesEachSixBitLevelRoundedDown()
    {
        // The hold carrier: floor 120 (30) read 60 (15), ceiling 60 (15) read 28 (7), red 220 (55) read 109 (27).
        byte[] level = new byte[PaletteFade.Length];
        level[0] = 120;
        level[3] = 60;
        level[6] = 220;

        byte[] shown = PaletteFade.Blend(level, Uniform(0, 0, 0), 0.5);

        Assert.Equal((byte)60, shown[0]);
        Assert.Equal((byte)28, shown[3]);
        Assert.Equal((byte)109, shown[6]);
    }

    [Fact]
    public void Blend_NineTenthsTowardRed_HoldsTheFactorAs921Over1024()
    {
        // The swatch carrier: 0 toward 50 at 0.9 read 44 (921/1024 · 50 = 44.97), 60 toward 50 read 51.
        byte[] level = new byte[PaletteFade.Length];
        level[3] = 240;

        byte[] shown = PaletteFade.Blend(level, Uniform(200, 30, 15), 0.9);

        Assert.Equal(PaletteFade.Widen(44), shown[0]);
        Assert.Equal(PaletteFade.Widen(51), shown[3]);
    }

    [Fact]
    public void Blend_FactorAtOrBelowZero_ShowsTheCurrentPalette()
    {
        byte[] level = Uniform(120, 120, 120);

        Assert.Equal(PaletteFade.Show(level), PaletteFade.Blend(level, Uniform(0, 0, 0), 0));
        Assert.Equal(PaletteFade.Show(level), PaletteFade.Blend(level, Uniform(0, 0, 0), -0.5));
    }

    [Fact]
    public void Blend_FactorAboveOne_ShowsTheTarget()
    {
        byte[] shown = PaletteFade.Blend(Uniform(120, 120, 120), Uniform(200, 30, 15), 1.5);

        // 200 -> 50 -> 203, 30 -> 7 -> 28, 15 -> 3 -> 12 (the red carrier's screenshot palette read 200, 28, 12 before widening).
        Assert.Equal((byte)203, shown[0]);
        Assert.Equal((byte)28, shown[1]);
        Assert.Equal((byte)12, shown[2]);
    }

    [Fact]
    public void IsUniform_TellsAOneColourPaletteFromARamp()
    {
        Assert.True(PaletteFade.IsUniform(Uniform(16, 16, 16), out (byte R, byte G, byte B) color));
        Assert.Equal(((byte)16, (byte)16, (byte)16), color);

        byte[] ramp = new byte[PaletteFade.Length];
        ramp[3] = 1;
        Assert.False(PaletteFade.IsUniform(ramp, out _));
    }
}
