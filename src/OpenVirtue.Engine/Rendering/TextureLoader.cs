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

        foreach ((string name, LevelTexture texture) in level.Textures)
        {
            PcxImage? pcx = GetPcx(archive, texture.File, level.SearchPaths, pcxByFile);
            if (pcx is not null)
            {
                images[name] = Crop(pcx, texture, colorKey: true);
            }

            for (int frameIndex = 1; frameIndex < texture.Frames.Count; frameIndex++)
            {
                if (texture.Frames[frameIndex] is not { } frame)
                {
                    continue;
                }

                PcxImage? framePcx = GetPcx(archive, frame.File, level.SearchPaths, pcxByFile);
                if (framePcx is not null)
                {
                    images[TextureAnimation.FrameKey(name, frameIndex)] =
                        Crop(framePcx, frame, colorKey: true);
                }
            }
        }

        return images;
    }

    /// <summary>Loads top-level bitmap-like resources used by UI overlays and panels.</summary>
    public static IReadOnlyDictionary<string, TextureImage> LoadBitmaps(WrsArchive archive, Level level)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(level);

        var pcxByFile = new Dictionary<string, PcxImage?>(StringComparer.OrdinalIgnoreCase);
        var images = new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, LevelBitmap bitmap) in level.Bitmaps)
        {
            PcxImage? pcx = GetPcx(archive, bitmap.File, level.SearchPaths, pcxByFile);
            if (pcx is not null)
            {
                images[name] = Crop(pcx, bitmap, colorKey: true);
            }
        }

        return images;
    }

    /// <summary>Loads top-level WDL <c>FONT</c> atlases used by visible text declarations.</summary>
    public static IReadOnlyDictionary<string, TextureImage> LoadFonts(WrsArchive archive, Level level)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(level);

        var pcxByFile = new Dictionary<string, PcxImage?>(StringComparer.OrdinalIgnoreCase);
        var images = new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase);
        foreach (WdlUiElement element in level.UiElements.Values)
        {
            if (!element.Keyword.Equals("FONT", StringComparison.OrdinalIgnoreCase) ||
                FirstFileRef(element.Header) is not { } file)
            {
                continue;
            }

            PcxImage? pcx = GetPcx(archive, file, level.SearchPaths, pcxByFile);
            if (pcx is not null)
            {
                images[element.Name] = Crop(pcx, 0, 0, 0, 0, colorKey: true);
            }
        }

        return images;
    }

    private static PcxImage? GetPcx(
        WrsArchive archive,
        string file,
        IReadOnlyList<string> searchPaths,
        Dictionary<string, PcxImage?> cache)
    {
        List<string> candidates = CandidateEntryNames(file, searchPaths);
        string key = string.Join('\0', candidates);
        if (cache.TryGetValue(key, out PcxImage? cached))
        {
            return cached;
        }

        PcxImage? pcx = null;
        foreach (string candidate in candidates)
        {
            foreach (WrsEntry entry in archive.Entries)
            {
                if (EntryNameEquals(entry.Name, candidate))
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

            if (pcx is not null)
            {
                break;
            }
        }

        cache[key] = pcx;
        return pcx;
    }

    private static List<string> CandidateEntryNames(string file, IReadOnlyList<string> searchPaths)
    {
        var candidates = new List<string>();
        string normalizedFile = NormalizeArchiveName(file);
        if (normalizedFile.Length > 0)
        {
            candidates.Add(normalizedFile);
        }

        string fileName = NormalizeArchiveName(Path.GetFileName(normalizedFile));
        if (fileName.Length > 0 && !EntryNameEquals(fileName, normalizedFile))
        {
            candidates.Add(fileName);
        }

        foreach (string path in searchPaths)
        {
            string normalizedPath = NormalizeArchiveName(path).Trim('/');
            if (normalizedPath.Length == 0 || fileName.Length == 0)
            {
                continue;
            }

            candidates.Add(normalizedPath + "/" + fileName);
        }

        return candidates;
    }

    private static bool EntryNameEquals(string left, string right) =>
        NormalizeArchiveName(left).Equals(NormalizeArchiveName(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeArchiveName(string value) => value.Replace('\\', '/');

    private static string? FirstFileRef(IReadOnlyList<WdlUiValue> values)
    {
        foreach (WdlUiValue value in values)
        {
            if (value.Kind.Equals("FileRef", StringComparison.OrdinalIgnoreCase))
            {
                return value.Text;
            }
        }

        return null;
    }

    private static TextureImage Crop(PcxImage pcx, LevelTexture texture, bool colorKey) =>
        Crop(pcx, texture.X, texture.Y, texture.Width, texture.Height, colorKey);

    private static TextureImage Crop(PcxImage pcx, LevelBitmap bitmap, bool colorKey) =>
        Crop(pcx, bitmap.X, bitmap.Y, bitmap.Width, bitmap.Height, colorKey);

    private static TextureImage Crop(PcxImage pcx, int sourceX, int sourceY, int sourceWidth, int sourceHeight, bool colorKey)
    {
        // Clamp the BMAP rectangle to the image; a non-positive size means "the whole image".
        int x = Math.Clamp(sourceX, 0, pcx.Width);
        int y = Math.Clamp(sourceY, 0, pcx.Height);
        int width = sourceWidth > 0 ? Math.Min(sourceWidth, pcx.Width - x) : pcx.Width - x;
        int height = sourceHeight > 0 ? Math.Min(sourceHeight, pcx.Height - y) : pcx.Height - y;
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
