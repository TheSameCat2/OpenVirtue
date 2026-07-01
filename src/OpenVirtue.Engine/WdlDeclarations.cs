// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using OpenVirtue.Formats.Wdl;

namespace OpenVirtue.Engine;

/// <summary>
/// Indexes a flattened WDL program's declarations needed to dress the geometry:
/// <c>BMAP</c> bitmaps, <c>TEXTURE</c> definitions, and the <c>REGION</c>/<c>WALL</c>
/// type definitions that name textures. Resolves a texture name through
/// <c>TEXTURE → BMAPS → BMAP</c> to a concrete <see cref="LevelTexture"/>.
/// </summary>
public sealed class WdlDeclarations
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, BitmapDef> _bitmaps = new(Ci);
    private readonly Dictionary<string, TextureDef> _textures = new(Ci);
    private readonly Dictionary<string, RegionTextures> _regions = new(Ci);
    private readonly Dictionary<string, string?> _walls = new(Ci);
    private readonly Dictionary<string, EntityDef> _entities = new(Ci);
    private readonly Dictionary<string, WdlBlock> _actions = new(Ci);

    /// <summary>The names of all defined textures.</summary>
    public IReadOnlyCollection<string> TextureNames => _textures.Keys;

    /// <summary>The body of each <c>ACTION</c> declaration, by name.</summary>
    public IReadOnlyDictionary<string, WdlBlock> Actions => _actions;

    /// <summary>Builds the index from a flattened program.</summary>
    public static WdlDeclarations Index(WdlProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var declarations = new WdlDeclarations();

        foreach (WdlItem item in program.Items)
        {
            switch (item.Keyword.ToUpperInvariant())
            {
                case "BMAP":
                    declarations.IndexBitmap(item);
                    break;
                case "TEXTURE":
                    declarations.IndexTexture(item);
                    break;
                case "REGION" when item.HasBody:
                    declarations.IndexRegion(item);
                    break;
                case "WALL" when item.HasBody:
                    declarations.IndexWall(item);
                    break;
                case "THING" when item.HasBody:
                case "ACTOR" when item.HasBody:
                    declarations.IndexEntity(item);
                    break;
                case "ACTION" when item.HasBody && item.Header.Count > 0:
                    declarations._actions[item.Header[0].Text] = item.Body!;
                    break;
            }
        }

        return declarations;
    }

    /// <summary>The floor/ceiling texture names for a region type, if defined.</summary>
    public RegionTextures? GetRegionTextures(string name) =>
        _regions.TryGetValue(name, out RegionTextures value) ? value : null;

    /// <summary>The texture name for a wall type, if defined.</summary>
    public string? GetWallTexture(string name) =>
        _walls.TryGetValue(name, out string? value) ? value : null;

    /// <summary>The sprite texture and height for a THING/ACTOR type, if defined.</summary>
    public EntityDef? GetEntity(string name) =>
        _entities.TryGetValue(name, out EntityDef value) ? value : null;

    /// <summary>Resolves a texture name to its source PCX file, sub-rectangle, and scale.</summary>
    public LevelTexture? ResolveTexture(string? textureName)
    {
        if (textureName is null ||
            !_textures.TryGetValue(textureName, out TextureDef texture) ||
            texture.Bitmap is null ||
            !_bitmaps.TryGetValue(texture.Bitmap, out BitmapDef bitmap))
        {
            return null;
        }

        return new LevelTexture(
            textureName, bitmap.File, bitmap.X, bitmap.Y, bitmap.Width, bitmap.Height,
            texture.ScaleX, texture.ScaleY, texture.Ambient, texture.IsSky);
    }

    private void IndexBitmap(WdlItem item)
    {
        // BMAP name, <file.pcx>, x, y, width, height
        if (item.Header.Count == 0)
        {
            return;
        }

        string name = item.Header[0].Text;
        string? file = FirstFileRef(item.Header);
        int[] numbers = Numbers(item.Header);
        if (file is not null && numbers.Length >= 4)
        {
            _bitmaps[name] = new BitmapDef(file, numbers[0], numbers[1], numbers[2], numbers[3]);
        }
    }

    private void IndexTexture(WdlItem item)
    {
        // TEXTURE name { SCALE_XY sx, sy; BMAPS bmap; }
        if (item.Header.Count == 0 || item.Body is not { } body)
        {
            return;
        }

        string? bitmap = null;
        double scaleX = 1, scaleY = 1;
        double ambient = 1;
        bool isSky = false;
        foreach (WdlItem property in body.Items)
        {
            if (property.Keyword.Equals("BMAPS", StringComparison.OrdinalIgnoreCase) && property.Header.Count > 0)
            {
                bitmap = property.Header[0].Text;
            }
            else if (property.Keyword.Equals("SCALE_XY", StringComparison.OrdinalIgnoreCase))
            {
                int[] scale = Numbers(property.Header);
                if (scale.Length >= 2)
                {
                    scaleX = scale[0];
                    scaleY = scale[1];
                }
            }
            else if (property.Keyword.Equals("AMBIENT", StringComparison.OrdinalIgnoreCase) &&
                     property.Header.Count > 0 &&
                     double.TryParse(property.Header[0].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedAmbient))
            {
                ambient = parsedAmbient;
            }
            else if (property.Keyword.Equals("FLAGS", StringComparison.OrdinalIgnoreCase) &&
                     property.Header.Any(static t => t.Text.Equals("SKY", StringComparison.OrdinalIgnoreCase)))
            {
                isSky = true;
            }
        }

        _textures[item.Header[0].Text] = new TextureDef(bitmap, scaleX, scaleY, ambient, isSky);
    }

    private void IndexRegion(WdlItem item)
    {
        string? floor = null, ceiling = null;
        foreach (WdlItem property in item.Body!.Items)
        {
            if (property.Header.Count == 0)
            {
                continue;
            }

            if (property.Keyword.Equals("FLOOR_TEX", StringComparison.OrdinalIgnoreCase))
            {
                floor = property.Header[0].Text;
            }
            else if (property.Keyword.Equals("CEIL_TEX", StringComparison.OrdinalIgnoreCase))
            {
                ceiling = property.Header[0].Text;
            }
        }

        _regions[item.Header[0].Text] = new RegionTextures(floor, ceiling);
    }

    private void IndexWall(WdlItem item)
    {
        foreach (WdlItem property in item.Body!.Items)
        {
            if (property.Keyword.Equals("TEXTURE", StringComparison.OrdinalIgnoreCase) && property.Header.Count > 0)
            {
                _walls[item.Header[0].Text] = property.Header[0].Text;
                return;
            }
        }

        _walls[item.Header[0].Text] = null;
    }

    private void IndexEntity(WdlItem item)
    {
        if (item.Header.Count == 0)
        {
            return;
        }

        string? texture = null;
        double height = 1;
        foreach (WdlItem property in item.Body!.Items)
        {
            if (property.Keyword.Equals("TEXTURE", StringComparison.OrdinalIgnoreCase) && property.Header.Count > 0)
            {
                texture = property.Header[0].Text;
            }
            else if (property.Keyword.Equals("HEIGHT", StringComparison.OrdinalIgnoreCase) &&
                     property.Header.Count > 0 &&
                     double.TryParse(property.Header[0].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                height = parsed;
            }
        }

        _entities[item.Header[0].Text] = new EntityDef(texture, height);
    }

    private static string? FirstFileRef(IReadOnlyList<WdlToken> tokens)
    {
        foreach (WdlToken token in tokens)
        {
            if (token.Kind == WdlTokenKind.FileRef)
            {
                return token.Text;
            }
        }

        return null;
    }

    private static int[] Numbers(IReadOnlyList<WdlToken> tokens)
    {
        var numbers = new List<int>();
        foreach (WdlToken token in tokens)
        {
            if (token.Kind == WdlTokenKind.Number &&
                double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                numbers.Add((int)value);
            }
        }

        return [.. numbers];
    }

    private readonly record struct BitmapDef(string File, int X, int Y, int Width, int Height);

    private readonly record struct TextureDef(string? Bitmap, double ScaleX, double ScaleY, double Ambient, bool IsSky);

    /// <summary>The floor and ceiling texture names declared for a region type.</summary>
    public readonly record struct RegionTextures(string? Floor, string? Ceiling);

    /// <summary>The sprite texture name and height declared for a THING/ACTOR type.</summary>
    public readonly record struct EntityDef(string? Texture, double Height);
}
