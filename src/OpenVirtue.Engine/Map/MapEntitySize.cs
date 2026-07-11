// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Map;

/// <summary>Documented world-space dimensions of a thing or actor texture.</summary>
public readonly record struct MapEntitySize(float Width, float Height);

/// <summary>Resolves thing/actor bitmap dimensions through texture pixels-per-step scaling.</summary>
public static class MapEntitySizing
{
    private const double DefaultPixelsPerStep = 16;

    /// <summary>Returns the current entity texture size in world steps, if resolvable.</summary>
    public static bool TryGetSize(Level level, MapEntity entity, out MapEntitySize size)
        => TryGetSize(level, entity, side: 0, out size);

    /// <summary>Returns the current entity texture size for the selected directional side.</summary>
    public static bool TryGetSize(Level level, MapEntity entity, int side, out MapEntitySize size)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.Texture is not { } textureName ||
            !level.Textures.TryGetValue(textureName, out LevelTexture texture))
        {
            size = default;
            return false;
        }

        return TryGetTextureSize(texture, entity["CYCLE"], side, out size);
    }

    /// <summary>Returns a texture phase's world-space size for an entity billboard layer.</summary>
    public static bool TryGetTextureSize(LevelTexture texture, double cycle, int side, out MapEntitySize size)
    {
        if (TextureAnimation.ResolveBitmap(texture, cycle, side) is not { } frame)
        {
            size = default;
            return false;
        }

        int width = frame.Width > 0 ? frame.Width : texture.Width;
        int height = frame.Height > 0 ? frame.Height : texture.Height;
        if (width <= 0 || height <= 0)
        {
            size = default;
            return false;
        }

        double scaleX = texture.ScaleX > 0 ? texture.ScaleX : DefaultPixelsPerStep;
        double scaleY = texture.ScaleY > 0 ? texture.ScaleY : DefaultPixelsPerStep;
        size = new MapEntitySize((float)(width / scaleX), (float)(height / scaleY));
        return size.Width > 0 && size.Height > 0;
    }
}
