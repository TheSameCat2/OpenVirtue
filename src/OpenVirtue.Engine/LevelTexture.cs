// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine;

/// <summary>
/// A texture resolved from the WDL declaration chain (<c>TEXTURE</c> → <c>BMAPS</c> →
/// <c>BMAP</c>): the source PCX file, the sub-rectangle within it, and the texture's
/// scale. The renderer loads <see cref="File"/> and samples the given rectangle;
/// non-positive dimensions request the whole image.
/// </summary>
public readonly record struct LevelTexture(
    string Name,
    string File,
    int X,
    int Y,
    int Width,
    int Height,
    double ScaleX,
    double ScaleY,
    double Ambient,
    bool IsSky,
    bool IsLightmap,
    bool IsBehind,
    bool IsShadow,
    IReadOnlyList<LevelBitmap?> Frames,
    int Sides,
    int Cycles,
    IReadOnlyList<double> Delays,
    bool IsOneShot,
    IReadOnlyList<bool> Mirrors,
    IReadOnlyList<double> FrameOffsetsX,
    IReadOnlyList<double> FrameOffsetsY,
    double SoundDistance,
    double SoundVerticalDistance,
    string? SoundName,
    double SoundVolume,
    IReadOnlyList<bool> SoundCycles,
    bool IsSoundLoop,
    double RandomDelayFactor,
    double Albedo = 0);

/// <summary>
/// A catalog <see cref="LevelTexture"/> held by reference (<see cref="Level.FindTexture"/>), so a
/// per-cycle scan reads its fields without copying the whole record.
/// </summary>
public sealed class LevelTextureEntry(LevelTexture texture)
{
    public LevelTexture Texture { get; } = texture;
}

/// <summary>Resolves a logical WDL texture and object cycle/side to a renderable bitmap phase.</summary>
public static class TextureAnimation
{
    private const string FramePrefix = "@openvirtue/frame/";

    /// <summary>Returns the internal renderer key for a non-initial bitmap phase.</summary>
    public static string FrameKey(string textureName, int frameIndex) =>
        $"{FramePrefix}{textureName}/{frameIndex}";

    /// <summary>Returns the logical WDL texture name represented by a renderer frame key.</summary>
    public static string LogicalTextureName(string renderTextureName)
    {
        if (!renderTextureName.StartsWith(FramePrefix, StringComparison.Ordinal))
        {
            return renderTextureName;
        }

        ReadOnlySpan<char> remainder = renderTextureName.AsSpan(FramePrefix.Length);
        int separator = remainder.LastIndexOf('/');
        return separator > 0 ? remainder[..separator].ToString() : renderTextureName;
    }

    /// <summary>
    /// The side of a wall texture seen from (<paramref name="eyeX"/>, <paramref name="eyeY"/>) in map
    /// coordinates (manual p.37; OV-20261030-wall-sides). With four sides or more, the first two are
    /// seen from the face to the left of v1 → v2 (where the cross product of v1 → v2 and v1 → eye is
    /// positive) and the next two from the other face. Of a pair, the first is seen while the eye,
    /// projected on the wall's line, lies before its middle, and the second from the middle on.
    /// With one side, 0.
    /// </summary>
    public static int WallSide(LevelTexture texture, double v1X, double v1Y, double v2X, double v2Y, double eyeX, double eyeY)
    {
        if (texture.Sides < 2)
        {
            return 0;
        }

        double dx = v2X - v1X;
        double dy = v2Y - v1Y;
        double ex = eyeX - v1X;
        double ey = eyeY - v1Y;
        double lengthSquared = (dx * dx) + (dy * dy);
        int half = lengthSquared > 0 && ((ex * dx) + (ey * dy)) / lengthSquared >= 0.5 ? 1 : 0;
        int pair = texture.Sides >= 4 && ((dx * ey) - (dy * ex)) < 0 ? 1 : 0;
        return (2 * pair) + half;
    }

    /// <summary>
    /// Resolves the current bitmap frame key. A null result denotes a documented <c>NULL</c>
    /// phase and must be rendered as invisible.
    /// </summary>
    public static string? ResolveRenderTexture(LevelTexture texture, double cycle, int side = 0)
    {
        if (texture.Frames.Count == 0)
        {
            return texture.Name;
        }

        int frameIndex = ResolveFrameIndex(texture, cycle, side);
        if (frameIndex >= texture.Frames.Count || texture.Frames[frameIndex] is null)
        {
            return null;
        }

        return frameIndex == 0 ? texture.Name : FrameKey(texture.Name, frameIndex);
    }

    /// <summary>Returns the bitmap declaration selected by the current phase, if visible.</summary>
    public static LevelBitmap? ResolveBitmap(LevelTexture texture, double cycle, int side = 0)
    {
        if (texture.Frames.Count == 0)
        {
            return new LevelBitmap(texture.Name, texture.File, texture.X, texture.Y, texture.Width, texture.Height);
        }

        int frameIndex = ResolveFrameIndex(texture, cycle, side);
        return frameIndex < texture.Frames.Count ? texture.Frames[frameIndex] : null;
    }

    /// <summary>Returns the flattened <c>side * cycles + cycle</c> bitmap-list index.</summary>
    public static int ResolveFrameIndex(LevelTexture texture, double cycle, int side = 0)
    {
        int cycles = Math.Max(1, texture.Cycles);
        int sides = Math.Max(1, texture.Sides);
        int cycleIndex = cycle > 0
            ? Math.Clamp((int)Math.Floor(cycle) - 1, 0, cycles - 1)
            : 0;
        int sideIndex = ((side % sides) + sides) % sides;
        return (sideIndex * cycles) + cycleIndex;
    }

    /// <summary>Returns whether the selected directional side is horizontally mirrored.</summary>
    public static bool IsMirrored(LevelTexture texture, int side)
    {
        int sides = Math.Max(1, texture.Sides);
        int sideIndex = ((side % sides) + sides) % sides;
        return sideIndex < texture.Mirrors.Count && texture.Mirrors[sideIndex];
    }

    /// <summary>Returns the selected bitmap's documented pixel offset.</summary>
    public static (double X, double Y) FrameOffset(LevelTexture texture, double cycle, int side = 0)
    {
        int frameIndex = ResolveFrameIndex(texture, cycle, side);
        double x = frameIndex < texture.FrameOffsetsX.Count ? texture.FrameOffsetsX[frameIndex] : 0;
        double y = frameIndex < texture.FrameOffsetsY.Count ? texture.FrameOffsetsY[frameIndex] : 0;
        return (x, y);
    }
}
