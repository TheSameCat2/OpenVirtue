// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Rendering;

/// <summary>
/// The original's <c>FADE_PAL</c>, as measured in OV-20261104-fade-pal. The screen shows the last
/// declared <c>PALETTE</c> at a level's start. <c>FADE_PAL palette, factor</c> with a factor of 1 or
/// above makes that palette the current one; a factor between 0 and 1 leaves the current palette and
/// shows every entry blended toward the named palette's by the factor (held in 22.10), in the 6-bit
/// units of the VGA DAC and rounded down; a factor of 0 or below shows the current palette again. The blend is from
/// the current palette, not from the blend before it, and each <c>FADE_PAL</c> replaces the last.
/// <c>SCREENSHOT</c> saves the current palette, not the blend.
/// </summary>
public static class PaletteFade
{
    /// <summary>The number of bytes in a 256-entry RGB palette.</summary>
    public const int Length = 256 * 3;

    /// <summary>
    /// The DAC palette the screen shows: <paramref name="current"/> blended toward
    /// <paramref name="target"/> by <paramref name="factor"/>, each 8-bit channel first cut to the
    /// DAC's 6 bits, the factor cut toward zero to 1/1024 as the script holds it (0.9 is 921/1024:
    /// the swatch carrier read 44 for 0 toward 50, not 45), the blend rounded down, and the 6-bit
    /// result widened back to 8 bits the way an emulated DAC shows it (<c>d·4 + d/16</c>, so 63
    /// reads 255).
    /// </summary>
    public static byte[] Blend(ReadOnlySpan<byte> current, ReadOnlySpan<byte> target, double factor)
    {
        byte[] result = new byte[Length];
        factor = Math.Truncate(factor * 1024) / 1024;
        bool blending = target.Length >= Length && factor > 0;
        for (int i = 0; i < Length; i++)
        {
            int c = i < current.Length ? current[i] >> 2 : 0;
            int d = c;
            if (blending)
            {
                int t = target[i] >> 2;
                d = (int)Math.Floor(c + (Math.Min(factor, 1) * (t - c)));
            }

            result[i] = Widen(Math.Clamp(d, 0, 63));
        }

        return result;
    }

    /// <summary>The palette the screen shows with no blend: every channel cut to 6 bits and widened.</summary>
    public static byte[] Show(ReadOnlySpan<byte> current) => Blend(current, [], 0);

    /// <summary>An 8-bit channel value for a 6-bit DAC level.</summary>
    public static byte Widen(int level) => (byte)((level << 2) | (level >> 4));

    /// <summary>
    /// Whether every entry of <paramref name="palette"/> is the same colour (the retail fade targets:
    /// black, red, blue, grey), and that colour as the DAC shows it.
    /// </summary>
    public static bool IsUniform(ReadOnlySpan<byte> palette, out (byte R, byte G, byte B) color)
    {
        color = default;
        if (palette.Length < Length)
        {
            return false;
        }

        byte r = palette[0];
        byte g = palette[1];
        byte b = palette[2];
        for (int i = 3; i < Length; i += 3)
        {
            if (palette[i] != r || palette[i + 1] != g || palette[i + 2] != b)
            {
                return false;
            }
        }

        color = (Widen(r >> 2), Widen(g >> 2), Widen(b >> 2));
        return true;
    }
}
