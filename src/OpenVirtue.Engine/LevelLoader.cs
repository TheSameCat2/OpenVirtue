// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Text;
using OpenVirtue.Engine.Map;
using OpenVirtue.Formats.Wdl;
using OpenVirtue.Formats.Wmp;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Engine;

/// <summary>
/// Loads a complete <see cref="Level"/> by combining a level's WDL program (flattened
/// via <see cref="WdlPreprocessor"/>) with the WMP map it names, materializing the
/// geometry and placements as typed engine objects.
/// </summary>
public static class LevelLoader
{
    private const double DefaultRegionClipDistance = 1000;

    /// <summary>
    /// Loads a level from a WRS archive. The main script is <paramref name="mainWdlName"/>
    /// (e.g. <c>APATHY.WDL</c>); the map is whichever file its <c>MAPFILE</c> directive names.
    /// </summary>
    /// <exception cref="InvalidDataException">The archive lacks the main script, the MAPFILE, or the map.</exception>
    public static Level Load(WrsArchive archive, string mainWdlName, IEnumerable<string>? defines = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrEmpty(mainWdlName);

        // Only WDL/WMP entries are text we resolve by name; skip binary assets.
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (WrsEntry entry in archive.Entries)
        {
            if (entry.Name.EndsWith(".wdl", StringComparison.OrdinalIgnoreCase) ||
                entry.Name.EndsWith(".wmp", StringComparison.OrdinalIgnoreCase))
            {
                resources[entry.Name] = Encoding.Latin1.GetString(entry.GetData());
            }
        }

        if (!resources.TryGetValue(mainWdlName, out string? mainSource))
        {
            throw new InvalidDataException($"Main WDL '{mainWdlName}' was not found in the archive.");
        }

        return LoadCore(
            mainWdlName,
            mainSource,
            name => resources.GetValueOrDefault(Path.GetFileName(name)),
            defines);
    }

    /// <summary>
    /// Loads a level from raw sources. <paramref name="resolveResource"/> resolves a file
    /// name (an <c>INCLUDE</c>d module or the <c>MAPFILE</c>) to its text. Decoupled from WRS
    /// so it is fully testable.
    /// </summary>
    /// <exception cref="InvalidDataException">The program lacks a MAPFILE or the map cannot be resolved.</exception>
    public static Level LoadCore(
        string name,
        string mainSource,
        Func<string, string?> resolveResource,
        IEnumerable<string>? defines = null)
    {
        ArgumentNullException.ThrowIfNull(mainSource);
        ArgumentNullException.ThrowIfNull(resolveResource);

        WdlProgram program = WdlPreprocessor.Flatten(mainSource, n => resolveResource(n), defines);

        string mapName = FindFileRef(program.MainItems, "MAPFILE")
            ?? throw new InvalidDataException("Level WDL has no MAPFILE directive.");
        string mapText = resolveResource(mapName)
            ?? throw new InvalidDataException($"Map '{mapName}' could not be resolved.");
        WmpMap map = WmpMap.Read(mapText);

        WdlDeclarations declarations = WdlDeclarations.Index(program);

        double defaultRegionClipDistance = FindNumber(program, "CLIP_DIST") ?? DefaultRegionClipDistance;
        var vertices = map.Vertices.Select(v => new Vertex(v.X, v.Y, v.Z)).ToList();
        var regions = map.Regions.Select(r => BuildRegion(r, declarations, defaultRegionClipDistance)).ToList();
        var walls = map.Walls.Select(w => BuildWall(w, declarations)).ToList();
        var things = map.Things.Select(p => BuildEntity(new Thing(p.Name), p, declarations)).ToList();
        var actors = map.Actors.Select(p => BuildEntity(new Actor(p.Name), p, declarations)).ToList();
        PlayerStart? start = map.PlayerStart is { } ps
            ? new PlayerStart(ps.X, ps.Y, ps.Angle, ps.Region)
            : null;

        (Dictionary<string, double> skillValues, Dictionary<string, SkillRange> skillBounds) = GatherSkills(program);
        return new Level(
            name, vertices, regions, walls, things, actors, start,
            skillValues, skillBounds, GatherStrings(program), GatherSynonyms(program), GatherSearchPaths(program),
            BuildTextures(declarations), declarations.Actions,
            GatherEachTickActions(program), GatherActionHooks(program), FindActionName(program, "IF_START"));
    }

    private static Region BuildRegion(WmpRegion r, WdlDeclarations declarations, double defaultClipDistance)
    {
        var region = new Region(r.Name) { FloorHeight = r.FloorHeight, CeilHeight = r.CeilHeight };
        region["CLIP_DIST"] = defaultClipDistance;
        if (declarations.GetRegionTextures(r.Name) is { } textures)
        {
            region.FloorTexture = textures.Floor;
            region.CeilTexture = textures.Ceiling;
            region.Ambient = textures.Ambient;
            region.FloorOffsetX = textures.FloorOffsetX;
            region.FloorOffsetY = textures.FloorOffsetY;
            region.CeilOffsetX = textures.CeilOffsetX;
            region.CeilOffsetY = textures.CeilOffsetY;
            ApplySkills(region, textures.Skills);
        }

        return region;
    }

    private static Wall BuildWall(WmpWall w, WdlDeclarations declarations)
    {
        var wall = new Wall(w.Name)
        {
            Vertex1 = w.Vertex1,
            Vertex2 = w.Vertex2,
            LeftRegion = w.Region1,
            RightRegion = w.Region2,
            OffsetX = w.OffsetX,
            OffsetY = w.OffsetY,
        };

        if (declarations.GetWall(w.Name) is { } definition)
        {
            wall.Texture = definition.Texture;
            wall.OffsetX = definition.OffsetX ?? wall.OffsetX;
            wall.OffsetY = definition.OffsetY ?? wall.OffsetY;
            ApplySkills(wall, definition.Skills);
        }

        return wall;
    }

    private static Dictionary<string, LevelTexture> BuildTextures(WdlDeclarations declarations)
    {
        var catalog = new Dictionary<string, LevelTexture>(StringComparer.OrdinalIgnoreCase);
        foreach (string textureName in declarations.TextureNames)
        {
            if (declarations.ResolveTexture(textureName) is { } texture)
            {
                catalog[textureName] = texture;
            }
        }

        return catalog;
    }

    private static T BuildEntity<T>(T entity, WmpPlacement placement, WdlDeclarations declarations)
        where T : MapEntity
    {
        entity.X = placement.X;
        entity.Y = placement.Y;
        entity.PlacementAngleDegrees = placement.Angle;
        entity.Region = placement.Region;
        entity["THING"] = 1;
        if (entity is Actor)
        {
            entity["ACTOR"] = 1;
        }

        // Dress the placement from its WDL type definition (sprite texture, height, angle offset).
        if (declarations.GetEntity(placement.Name) is { } definition)
        {
            entity.Texture = definition.Texture;
            entity.Height = definition.Height;
            entity.Angle = definition.AngleRadians;
            entity.EachTickAction = definition.EachTickAction;
            entity.EachCycleAction = definition.EachCycleAction;
            ApplySkills(entity, definition.Skills);
        }

        return entity;
    }

    private static void ApplySkills(AcknexObject obj, IReadOnlyDictionary<string, double> skills)
    {
        foreach ((string name, double value) in skills)
        {
            obj[name] = value;
        }
    }

    private static string? FindActionName(WdlProgram program, string keyword)
    {
        foreach (WdlItem item in program.Items)
        {
            if (item.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase) &&
                item.Header.Count > 0 &&
                item.Header[0].Kind == WdlTokenKind.Identifier)
            {
                return item.Header[0].Text;
            }
        }

        return null;
    }

    private static string?[] GatherEachTickActions(WdlProgram program)
    {
        string?[] actions = new string?[16];
        foreach (WdlItem item in program.Items)
        {
            if (!item.Keyword.Equals("EACH_TICK", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int slot = 0;
            foreach (WdlToken token in item.Header)
            {
                if (token.Kind != WdlTokenKind.Identifier)
                {
                    continue;
                }

                if (slot == actions.Length)
                {
                    break;
                }

                actions[slot++] = token.Text.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : token.Text;
            }
        }

        return actions;
    }

    private static Dictionary<string, string?> GatherActionHooks(WdlProgram program)
    {
        var hooks = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (WdlItem item in program.Items)
        {
            if (!IsActionHookKeyword(item.Keyword))
            {
                continue;
            }

            hooks[item.Keyword] = FirstIdentifierOrNull(item.Header);
        }

        return hooks;
    }

    private static bool IsActionHookKeyword(string keyword) =>
        keyword.StartsWith("IF_", StringComparison.OrdinalIgnoreCase) &&
        !keyword.Equals("IF_START", StringComparison.OrdinalIgnoreCase);

    private static string? FirstIdentifierOrNull(IReadOnlyList<WdlToken> tokens)
    {
        foreach (WdlToken token in tokens)
        {
            if (token.Kind == WdlTokenKind.Identifier)
            {
                return token.Text.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : token.Text;
            }
        }

        return null;
    }

    private static string? FindFileRef(IReadOnlyList<WdlItem> items, string keyword)
    {
        foreach (WdlItem item in items)
        {
            if (!item.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (WdlToken token in item.Header)
            {
                if (token.Kind == WdlTokenKind.FileRef)
                {
                    return token.Text;
                }
            }
        }

        return null;
    }

    private static double? FindNumber(WdlProgram program, string keyword)
    {
        foreach (WdlItem item in program.Items)
        {
            if (item.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase) &&
                ParseNumber(item) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    private static (Dictionary<string, double> Values, Dictionary<string, SkillRange> Bounds) GatherSkills(WdlProgram program)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var bounds = new Dictionary<string, SkillRange>(StringComparer.OrdinalIgnoreCase);

        foreach (WdlItem item in program.Items)
        {
            if (!item.Keyword.Equals("SKILL", StringComparison.OrdinalIgnoreCase) || item.Header.Count == 0)
            {
                continue;
            }

            double value = 0;
            double? min = null;
            double? max = null;
            if (item.Body is { } body)
            {
                foreach (WdlItem property in body.Items)
                {
                    if (property.Keyword.Equals("VAL", StringComparison.OrdinalIgnoreCase) && ParseNumber(property) is { } parsedVal)
                    {
                        value = parsedVal;
                    }
                    else if (property.Keyword.Equals("MIN", StringComparison.OrdinalIgnoreCase) && ParseNumber(property) is { } parsedMin)
                    {
                        min = parsedMin;
                    }
                    else if (property.Keyword.Equals("MAX", StringComparison.OrdinalIgnoreCase) && ParseNumber(property) is { } parsedMax)
                    {
                        max = parsedMax;
                    }
                }
            }

            string name = item.Header[0].Text;
            values[name] = value;
            if (min is not null || max is not null)
            {
                bounds[name] = new SkillRange(min ?? double.NegativeInfinity, max ?? double.PositiveInfinity);
            }
        }

        return (values, bounds);
    }

    private static Dictionary<string, WdlString> GatherStrings(WdlProgram program)
    {
        var strings = new Dictionary<string, WdlString>(StringComparer.OrdinalIgnoreCase);

        foreach (WdlItem item in program.Items)
        {
            if (!item.Keyword.Equals("STRING", StringComparison.OrdinalIgnoreCase) ||
                item.Header.Count == 0 ||
                item.Header[0].Kind != WdlTokenKind.Identifier)
            {
                continue;
            }

            string value = string.Empty;
            foreach (WdlToken token in item.Header.Skip(1))
            {
                if (token.Kind == WdlTokenKind.StringLiteral)
                {
                    value = token.Text;
                    break;
                }
            }

            strings[item.Header[0].Text] = new WdlString(value);
        }

        return strings;
    }

    private static Dictionary<string, WdlSynonym> GatherSynonyms(WdlProgram program)
    {
        var synonyms = new Dictionary<string, WdlSynonym>(StringComparer.OrdinalIgnoreCase);

        foreach (WdlItem item in program.Items)
        {
            if (!item.Keyword.Equals("SYNONYM", StringComparison.OrdinalIgnoreCase) ||
                item.Header.Count == 0 ||
                item.Header[0].Kind != WdlTokenKind.Identifier ||
                item.Body is not { } body)
            {
                continue;
            }

            string? type = null;
            string? defaultKeyword = null;
            foreach (WdlItem property in body.Items)
            {
                if (property.Header.Count == 0 || property.Header[0].Kind != WdlTokenKind.Identifier)
                {
                    continue;
                }

                if (property.Keyword.Equals("TYPE", StringComparison.OrdinalIgnoreCase))
                {
                    type = property.Header[0].Text;
                }
                else if (property.Keyword.Equals("DEFAULT", StringComparison.OrdinalIgnoreCase))
                {
                    defaultKeyword = property.Header[0].Text;
                }
            }

            if (type is not null)
            {
                synonyms[item.Header[0].Text] = new WdlSynonym(type, defaultKeyword);
            }
        }

        return synonyms;
    }

    private static string[] GatherSearchPaths(WdlProgram program)
    {
        var paths = new List<string>(capacity: 16);
        foreach (WdlItem item in program.Items)
        {
            if (!item.Keyword.Equals("PATH", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (WdlToken token in item.Header)
            {
                if (token.Kind == WdlTokenKind.StringLiteral)
                {
                    paths.Add(token.Text);
                    break;
                }
            }

            if (paths.Count == 16)
            {
                break;
            }
        }

        return paths.ToArray();
    }

    private static double? ParseNumber(WdlItem property)
    {
        if (property.Header.Count == 0)
        {
            return null;
        }

        int sign = 1;
        int index = 0;
        if (property.Header.Count > 1 &&
            property.Header[0].Kind == WdlTokenKind.Operator &&
            (property.Header[0].Text == "-" || property.Header[0].Text == "+"))
        {
            sign = property.Header[0].Text == "-" ? -1 : 1;
            index = 1;
        }

        return property.Header[index].Kind == WdlTokenKind.Number &&
               double.TryParse(property.Header[index].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? sign * value
            : null;
    }
}
