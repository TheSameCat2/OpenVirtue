// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using System.Runtime.CompilerServices;
using OpenVirtue.Engine.Map;
using OpenVirtue.Formats.Pcx;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Engine.Rendering;

/// <summary>
/// A CPU-side 32-bit RGBA image ready to upload as a GPU texture, with the palette indices it was
/// decoded from when it came from a PCX (the renderer shades by index, OV-20261105-shade-global).
/// </summary>
public readonly record struct TextureImage(int Width, int Height, byte[] Rgba, byte[]? Indices = null);

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

        MaskWallRows(level, images, RowMaskedWallTextures(level));
        return images;
    }

    /// <summary>
    /// OV-20261105-wall-pieces: the rows a wall's <c>v</c> spans before they repeat. The original reads
    /// a wall bitmap's row as <c>v &amp; (H - 1)</c> (two's complement), so a bitmap <paramref name="height"/>
    /// rows tall occupies the rows of the next power of two: a 12-row bitmap reads rows 0…11 and then
    /// 8…11 again, a period of 16, and the retail 120-row curtain a period of 128. A power-of-two
    /// height is its own period; a height of 0 or less has none (0).
    /// </summary>
    public static int WallRowPeriod(int height) =>
        height <= 0 ? 0 : (int)BitOperations.RoundUpToPowerOf2((uint)height);

    private static readonly ConditionalWeakTable<Level, IReadOnlySet<string>> MaskedByLevel = new();

    /// <summary>
    /// OV-20261105-wall-pieces: the textures <see cref="Load"/> expands to their row period, and so the
    /// ones <see cref="MeshBuilder"/> maps a wall or a surface with. A texture qualifies when a placed
    /// wall or a declared (unplaced) wall names it, it is not a sky, its height is not a power of two,
    /// and nothing draws it as a real image — not a thing's or actor's texture, not an attach of a wall
    /// or a sprite. Those last keep their pixels, and expanding them would show the repeated rows
    /// there too; a wall sharing such a bitmap stays plain and so consistent with the image it
    /// samples.
    /// </summary>
    /// <remarks>
    /// Cached per level and worked out from the walls as they stand when the bitmaps are loaded: a
    /// run-time <c>SET</c> of a wall's <c>TEXTURE</c> must not move a bitmap into the set afterwards,
    /// because its image was loaded unexpanded. So the loader and every later mesh agree on the set.
    /// </remarks>
    public static IReadOnlySet<string> RowMaskedWallTextures(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        return MaskedByLevel.GetValue(level, ComputeRowMaskedWallTextures);
    }

    private static IReadOnlySet<string> ComputeRowMaskedWallTextures(Level level)
    {
        // The wall types the level places, and the declared walls it keeps unplaced: both are WDL WALL
        // definitions, and either may be given a texture at run time.
        Wall[] walls = [.. level.Walls, .. level.UnplacedObjects.OfType<Wall>()];
        var wallTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var realImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Wall wall in walls)
        {
            if (wall.Texture is { } name)
            {
                wallTextures.Add(name);
            }

            if (wall.AttachTexture is { } attach)
            {
                realImages.Add(attach);
            }
        }

        foreach (MapEntity entity in level.Things.Cast<MapEntity>().Concat(level.Actors))
        {
            if (entity.Texture is { } texture)
            {
                realImages.Add(texture);
            }

            if (entity.AttachTexture is { } attach)
            {
                realImages.Add(attach);
            }
        }

        var masked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (LevelTexture texture in level.Textures.Values)
        {
            if (!wallTextures.Contains(texture.Name) ||
                texture.IsSky ||
                realImages.Contains(texture.Name) ||
                WallRowPeriod(texture.Height) <= texture.Height)
            {
                continue;
            }

            masked.Add(texture.Name);
        }

        return masked;
    }

    /// <summary>
    /// OV-20261105-wall-pieces: expands each named bitmap (and each of its frames) to <c>W × period</c>,
    /// destination row <c>y</c> from source row <c>y &amp; (H - 1)</c>, for the RGBA and the
    /// palette-index data alike. The renderer's plain <c>v</c> wrapping then reads the rows the
    /// original read, and <see cref="MeshBuilder"/> maps that bitmap with the period. The names are
    /// <see cref="RowMaskedWallTextures"/>'s, so sprites, attaches and UI keep their real images.
    /// </summary>
    private static void MaskWallRows(
        Level level, Dictionary<string, TextureImage> images, IReadOnlySet<string> rowMasked)
    {
        foreach (string name in rowMasked)
        {
            if (!level.Textures.TryGetValue(name, out LevelTexture texture))
            {
                continue;
            }

            MaskRows(images, name);
            for (int frameIndex = 1; frameIndex < texture.Frames.Count; frameIndex++)
            {
                MaskRows(images, TextureAnimation.FrameKey(name, frameIndex));
            }
        }
    }

    private static void MaskRows(Dictionary<string, TextureImage> images, string key)
    {
        if (!images.TryGetValue(key, out TextureImage image) || image.Height <= 0)
        {
            return;
        }

        int period = WallRowPeriod(image.Height);
        if (period <= image.Height)
        {
            return; // already a power of two tall: the mask changes nothing
        }

        int width = image.Width;
        int height = image.Height;
        var rgba = new byte[width * period * 4];
        byte[]? indices = image.Indices is null ? null : new byte[width * period];
        for (int row = 0; row < period; row++)
        {
            int sourceRow = row & (height - 1);
            Array.Copy(image.Rgba, sourceRow * width * 4, rgba, row * width * 4, width * 4);
            if (indices is not null)
            {
                Array.Copy(image.Indices!, sourceRow * width, indices, row * width, width);
            }
        }

        images[key] = new TextureImage(width, period, rgba, indices);
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
        byte[] indices = new byte[width * height];
        int dst = 0;
        int dstIndex = 0;
        for (int row = 0; row < height; row++)
        {
            int srcRow = (y + row) * pcx.Width;
            for (int col = 0; col < width; col++)
            {
                byte index = pcx.PixelIndices[srcRow + x + col];
                indices[dstIndex++] = index;
                Rgb24 color = pcx.Palette[index];
                rgba[dst++] = color.R;
                rgba[dst++] = color.G;
                rgba[dst++] = color.B;
                rgba[dst++] = colorKey && index == 0 ? (byte)0 : (byte)0xFF; // sprite color-key on palette index 0
            }
        }

        return new TextureImage(width, height, rgba, indices);
    }
}
