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
    private const double DefaultTextureScale = 16;
    private static readonly HashSet<string> RegionNumericProperties = new(Ci)
    {
        "CLIP_DIST",
        "FLOOR_ANGLE",
        "CEIL_ANGLE",
    };
    private static readonly HashSet<string> WallNumericProperties = new(Ci)
    {
        "CYCLE",
        "POSITION",
        "MAP_COLOR",
        "DIST",
    };
    private static readonly HashSet<string> EntityNumericProperties = new(Ci)
    {
        "CYCLE",
        "WAYPOINT",
        "TARGET_X",
        "TARGET_Y",
        "REL_ANGLE",
        "REL_DIST",
        "SPEED",
        "VSPEED",
        "ASPEED",
        "MAP_COLOR",
        "DIST",
    };

    private readonly Dictionary<string, BitmapDef> _bitmaps = new(Ci);
    private readonly Dictionary<string, TextureDef> _textures = new(Ci);
    private readonly Dictionary<string, RegionTextures> _regions = new(Ci);
    private readonly Dictionary<string, WallDef> _walls = new(Ci);
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
    public string? GetWallTexture(string name) => GetWall(name)?.Texture;

    /// <summary>The texture and optional placement overrides for a wall type, if defined.</summary>
    public WallDef? GetWall(string name) =>
        _walls.TryGetValue(name, out WallDef value) ? value : null;

    /// <summary>The sprite texture, height, and WDL angle offset for a THING/ACTOR type, if defined.</summary>
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
        // BMAP name, <file.pcx>[, x, y, width, height]
        if (item.Header.Count == 0)
        {
            return;
        }

        string name = item.Header[0].Text;
        string? file = FirstFileRef(item.Header);
        int[] numbers = Numbers(item.Header);
        if (file is null)
        {
            return;
        }

        _bitmaps[name] = numbers.Length >= 4
            ? new BitmapDef(file, numbers[0], numbers[1], numbers[2], numbers[3])
            : new BitmapDef(file, 0, 0, 0, 0);
    }

    private void IndexTexture(WdlItem item)
    {
        // TEXTURE name { SCALE_XY sx, sy; BMAPS bmap; }
        if (item.Header.Count == 0 || item.Body is not { } body)
        {
            return;
        }

        string? bitmap = null;
        double scaleX = DefaultTextureScale, scaleY = DefaultTextureScale;
        double ambient = 0;
        bool isSky = false;
        foreach (WdlItem property in body.Items)
        {
            if (property.Keyword.Equals("BMAPS", StringComparison.OrdinalIgnoreCase) && property.Header.Count > 0)
            {
                bitmap = property.Header[0].Text;
            }
            else if (property.Keyword.Equals("SCALE_XY", StringComparison.OrdinalIgnoreCase))
            {
                double[] scale = NumberValues(property.Header);
                if (scale.Length >= 2)
                {
                    scaleX = scale[0];
                    scaleY = scale[1];
                }
            }
            else if (property.Keyword.Equals("SCALE_X", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedScaleX)
            {
                scaleX = parsedScaleX;
            }
            else if (property.Keyword.Equals("SCALE_Y", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedScaleY)
            {
                scaleY = parsedScaleY;
            }
            else if (property.Keyword.Equals("AMBIENT", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedAmbient)
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
        double ambient = 0;
        double floorOffsetX = 0, floorOffsetY = 0, ceilOffsetX = 0, ceilOffsetY = 0;
        var skills = new Dictionary<string, double>(Ci);
        foreach (WdlItem property in item.Body!.Items)
        {
            if (property.Keyword.Equals("FLOOR_TEX", StringComparison.OrdinalIgnoreCase))
            {
                floor = property.Header.Count > 0 ? property.Header[0].Text : null;
            }
            else if (property.Keyword.Equals("CEIL_TEX", StringComparison.OrdinalIgnoreCase))
            {
                ceiling = property.Header.Count > 0 ? property.Header[0].Text : null;
            }
            else if (property.Keyword.Equals("AMBIENT", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedAmbient)
            {
                ambient = parsedAmbient;
            }
            else if (property.Keyword.Equals("FLOOR_OFFS_X", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedFloorOffsetX)
            {
                floorOffsetX = parsedFloorOffsetX;
            }
            else if (property.Keyword.Equals("FLOOR_OFFS_Y", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedFloorOffsetY)
            {
                floorOffsetY = parsedFloorOffsetY;
            }
            else if (property.Keyword.Equals("CEIL_OFFS_X", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedCeilOffsetX)
            {
                ceilOffsetX = parsedCeilOffsetX;
            }
            else if (property.Keyword.Equals("CEIL_OFFS_Y", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedCeilOffsetY)
            {
                ceilOffsetY = parsedCeilOffsetY;
            }
            else if (TryUniversalSkill(property, out string skillName, out double skillValue))
            {
                skills[skillName] = skillValue;
            }
            else if (TryNumericProperty(property, RegionNumericProperties, out string propertyName, out double propertyValue))
            {
                skills[propertyName] = propertyValue;
            }
        }

        _regions[item.Header[0].Text] = new RegionTextures(
            floor, ceiling, ambient, floorOffsetX, floorOffsetY, ceilOffsetX, ceilOffsetY, skills);
    }

    private void IndexWall(WdlItem item)
    {
        string? texture = null;
        double? offsetX = null;
        double? offsetY = null;
        var skills = new Dictionary<string, double>(Ci);
        foreach (WdlItem property in item.Body!.Items)
        {
            if (property.Keyword.Equals("TEXTURE", StringComparison.OrdinalIgnoreCase) && property.Header.Count > 0)
            {
                texture = property.Header[0].Text;
            }
            else if (property.Keyword.Equals("OFFSET_X", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedOffsetX)
            {
                offsetX = parsedOffsetX;
            }
            else if (property.Keyword.Equals("OFFSET_Y", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedOffsetY)
            {
                offsetY = parsedOffsetY;
            }
            else if (TryUniversalSkill(property, out string skillName, out double skillValue))
            {
                skills[skillName] = skillValue;
            }
            else if (TryNumericProperty(property, WallNumericProperties, out string propertyName, out double propertyValue))
            {
                skills[propertyName] = propertyValue;
            }
        }

        _walls[item.Header[0].Text] = new WallDef(texture, offsetX, offsetY, skills);
    }

    private void IndexEntity(WdlItem item)
    {
        if (item.Header.Count == 0)
        {
            return;
        }

        string? texture = null;
        double height = 1;
        double angle = 0;
        var skills = new Dictionary<string, double>(Ci);
        foreach (WdlItem property in item.Body!.Items)
        {
            if (property.Keyword.Equals("TEXTURE", StringComparison.OrdinalIgnoreCase) && property.Header.Count > 0)
            {
                texture = property.Header[0].Text;
            }
            else if (property.Keyword.Equals("HEIGHT", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsed)
            {
                height = parsed;
            }
            else if (property.Keyword.Equals("ANGLE", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedAngle)
            {
                angle = parsedAngle;
            }
            else if (TryUniversalSkill(property, out string skillName, out double skillValue))
            {
                skills[skillName] = skillValue;
            }
            else if (TryNumericProperty(property, EntityNumericProperties, out string propertyName, out double propertyValue))
            {
                skills[propertyName] = propertyValue;
            }
        }

        _entities[item.Header[0].Text] = new EntityDef(texture, height, angle, skills);
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

    private static double? FirstNumber(IReadOnlyList<WdlToken> tokens)
    {
        if (tokens.Count == 0)
        {
            return null;
        }

        int sign = 1;
        int index = 0;
        if (tokens.Count > 1 &&
            tokens[0].Kind == WdlTokenKind.Operator &&
            (tokens[0].Text == "-" || tokens[0].Text == "+"))
        {
            sign = tokens[0].Text == "-" ? -1 : 1;
            index = 1;
        }

        return tokens[index].Kind == WdlTokenKind.Number &&
               double.TryParse(tokens[index].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? sign * value
            : null;
    }

    private static int[] Numbers(IReadOnlyList<WdlToken> tokens)
    {
        double[] values = NumberValues(tokens);
        var numbers = new int[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            numbers[i] = (int)values[i];
        }

        return numbers;
    }

    private static double[] NumberValues(IReadOnlyList<WdlToken> tokens)
    {
        var numbers = new List<double>();
        for (int i = 0; i < tokens.Count; i++)
        {
            WdlToken token = tokens[i];
            int sign = 1;
            if (token.Kind == WdlTokenKind.Operator &&
                (token.Text == "-" || token.Text == "+") &&
                i + 1 < tokens.Count &&
                tokens[i + 1].Kind == WdlTokenKind.Number)
            {
                sign = token.Text == "-" ? -1 : 1;
                token = tokens[++i];
            }

            if (token.Kind == WdlTokenKind.Number &&
                double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                numbers.Add(sign * value);
            }
        }

        return [.. numbers];
    }

    private static bool TryUniversalSkill(WdlItem item, out string name, out double value)
    {
        name = item.Keyword;
        if (!IsUniversalSkill(name) || FirstNumber(item.Header) is not { } parsed)
        {
            value = 0;
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryNumericProperty(
        WdlItem item, HashSet<string> propertyNames, out string name, out double value)
    {
        name = item.Keyword;
        if (!propertyNames.Contains(name) || FirstNumber(item.Header) is not { } parsed)
        {
            value = 0;
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool IsUniversalSkill(string keyword)
    {
        if (!keyword.StartsWith("SKILL", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> suffix = keyword.AsSpan("SKILL".Length);
        return suffix.Length == 1 && suffix[0] >= '1' && suffix[0] <= '8';
    }

    private readonly record struct BitmapDef(string File, int X, int Y, int Width, int Height);

    private readonly record struct TextureDef(string? Bitmap, double ScaleX, double ScaleY, double Ambient, bool IsSky);

    /// <summary>The texture name and optional WDL pixel offsets declared for a wall type.</summary>
    public readonly record struct WallDef(
        string? Texture, double? OffsetX, double? OffsetY, IReadOnlyDictionary<string, double> Skills);

    /// <summary>The surface texture names and ambient brightness declared for a region type.</summary>
    public readonly record struct RegionTextures(
        string? Floor,
        string? Ceiling,
        double Ambient,
        double FloorOffsetX,
        double FloorOffsetY,
        double CeilOffsetX,
        double CeilOffsetY,
        IReadOnlyDictionary<string, double> Skills);

    /// <summary>The sprite texture name, height, and WDL radian angle offset declared for a THING/ACTOR type.</summary>
    public readonly record struct EntityDef(
        string? Texture, double Height, double AngleRadians, IReadOnlyDictionary<string, double> Skills);
}
