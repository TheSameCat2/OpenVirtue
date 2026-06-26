// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Formats.Pcx;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Engine.Rendering;

/// <summary>A CPU-side 32-bit RGBA image ready to upload as a GPU texture.</summary>
public readonly record struct TextureImage(int Width, int Height, byte[] Rgba);

/// <summary>
/// Decodes a level's referenced textures (via its <see cref="Level.Textures"/> catalog)
/// into CPU RGBA images: it reads each source PCX from the archive once and crops to the
/// texture's BMAP rectangle. The renderer uploads these to the GPU.
/// </summary>
public static class TextureLoader
{
    /// <summary>Loads every resolvable texture in the level into a name → image map.</summary>
    public static IReadOnlyDictionary<string, TextureImage> Load(WrsArchive archive, Level level)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(level);

        var pcxByFile = new Dictionary<string, PcxImage?>(StringComparer.OrdinalIgnoreCase);
        var images = new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase);

        // Textures used by things/actors are sprites: palette index 0 is their transparent color key.
        var spriteTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var thing in level.Things)
        {
            if (thing.Texture is { } t)
            {
                spriteTextures.Add(t);
            }
        }

        foreach (var actor in level.Actors)
        {
            if (actor.Texture is { } t)
            {
                spriteTextures.Add(t);
            }
        }

        foreach ((string name, LevelTexture texture) in level.Textures)
        {
            PcxImage? pcx = GetPcx(archive, texture.File, pcxByFile);
            if (pcx is not null)
            {
                images[name] = Crop(pcx, texture, colorKey: spriteTextures.Contains(name));
            }
        }

        return images;
    }

    private static PcxImage? GetPcx(WrsArchive archive, string file, Dictionary<string, PcxImage?> cache)
    {
        string key = Path.GetFileName(file);
        if (cache.TryGetValue(key, out PcxImage? cached))
        {
            return cached;
        }

        PcxImage? pcx = null;
        foreach (WrsEntry entry in archive.Entries)
        {
            if (entry.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    pcx = PcxImage.Read(entry.GetData());
                }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
                {
                    pcx = null; // skip a texture we can't decode rather than failing the whole load
                }

                break;
            }
        }

        cache[key] = pcx;
        return pcx;
    }

    private static TextureImage Crop(PcxImage pcx, LevelTexture texture, bool colorKey)
    {
        // Clamp the BMAP rectangle to the image; a non-positive size means "the whole image".
        int x = Math.Clamp(texture.X, 0, pcx.Width);
        int y = Math.Clamp(texture.Y, 0, pcx.Height);
        int width = texture.Width > 0 ? Math.Min(texture.Width, pcx.Width - x) : pcx.Width - x;
        int height = texture.Height > 0 ? Math.Min(texture.Height, pcx.Height - y) : pcx.Height - y;
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        byte[] rgba = new byte[width * height * 4];
        int dst = 0;
        for (int row = 0; row < height; row++)
        {
            int srcRow = (y + row) * pcx.Width;
            for (int col = 0; col < width; col++)
            {
                byte index = pcx.PixelIndices[srcRow + x + col];
                Rgb24 color = pcx.Palette[index];
                rgba[dst++] = color.R;
                rgba[dst++] = color.G;
                rgba[dst++] = color.B;
                rgba[dst++] = colorKey && index == 0 ? (byte)0 : (byte)0xFF; // sprite color-key on palette index 0
            }
        }

        return new TextureImage(width, height, rgba);
    }
}
