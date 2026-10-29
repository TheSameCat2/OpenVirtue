// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Text;
using OpenVirtue.Engine.Interpreter;
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
    public static Level Load(
        WrsArchive archive,
        string mainWdlName,
        IEnumerable<string>? defines = null,
        string? mapOverrideName = null)
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
            defines,
            mapOverrideName);
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
        IEnumerable<string>? defines = null,
        string? mapOverrideName = null)
    {
        ArgumentNullException.ThrowIfNull(mainSource);
        ArgumentNullException.ThrowIfNull(resolveResource);

        WdlProgram program = WdlPreprocessor.Flatten(mainSource, n => resolveResource(n), defines);

        string mapName = string.IsNullOrWhiteSpace(mapOverrideName)
            ? FindFileRef(program.MainItems, "MAPFILE")
                ?? throw new InvalidDataException("Level WDL has no MAPFILE directive.")
            : mapOverrideName;
        string mapText = resolveResource(mapName)
            ?? throw new InvalidDataException($"Map '{mapName}' could not be resolved.");
        WmpMap map = WmpMap.Read(mapText);

        WdlDeclarations declarations = WdlDeclarations.Index(program);

        double defaultRegionClipDistance = FindNumber(program, "CLIP_DIST") ?? DefaultRegionClipDistance;
        var vertices = map.Vertices.Select(v => new Vertex(v.X, v.Y, v.Z)).ToList();
        var regions = map.Regions.Select(r => BuildRegion(r, declarations, defaultRegionClipDistance)).ToList();
        StackBelowRegions(regions, declarations, defaultRegionClipDistance);
        var walls = map.Walls.Select(w => BuildWall(w, declarations)).ToList();
        var things = map.Things.Select(p => BuildEntity(new Thing(p.Name), p, declarations)).ToList();
        var actors = map.Actors.Select(p => BuildEntity(new Actor(p.Name), p, declarations)).ToList();
        var ways = map.Ways.ToDictionary(
            way => way.Name,
            way => new LevelWay(
                way.Name,
                way.Points.Select(point => new LevelWayPoint(point.X, point.Y)).ToList()),
            StringComparer.OrdinalIgnoreCase);
        PlayerStart? start = map.PlayerStart is { } ps
            ? new PlayerStart(ps.X, ps.Y, ps.Angle, ps.Region)
            : null;

        (Dictionary<string, double> skillValues,
            HashSet<string> globalSkills,
            HashSet<string> explicitlyInitializedSkills,
            Dictionary<string, SkillRange> skillBounds) = GatherSkills(program);
        return new Level(
            name, vertices, regions, walls, things, actors, ways, start,
            FindVideoMode(program),
            skillValues, globalSkills, explicitlyInitializedSkills, skillBounds,
            GatherStrings(program), GatherSynonyms(program), GatherSearchPaths(program),
            GatherSounds(program), GatherPalettes(program), GatherUiElements(program), BuildBitmaps(declarations),
            BuildTextures(declarations), declarations.Actions,
            GatherSchedulerSlotActions(program, "EACH_TICK"), GatherActionHooks(program), FindActionName(program, "IF_START"),
            GatherSchedulerSlotActions(program, "EACH_SEC"));
    }

    private static LevelVideoMode? FindVideoMode(WdlProgram program)
    {
        LevelVideoMode? result = null;
        foreach (WdlItem item in program.Items)
        {
            if (!item.Keyword.Equals("VIDEO", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string mode = string.Concat(item.Header
                .Where(static token => token.Kind is WdlTokenKind.Identifier or WdlTokenKind.Number)
                .Select(static token => token.Text));
            result = mode.ToUpperInvariant() switch
            {
                "320X200" => new LevelVideoMode("320x200", 320, 200),
                "X320X240" => new LevelVideoMode("X320x240", 320, 240),
                "X320X400" => new LevelVideoMode("X320x400", 320, 400),
                "S640X480" => new LevelVideoMode("S640x480", 640, 480),
                "S800X600" => new LevelVideoMode("S800x600", 800, 600),
                _ => result,
            };
        }

        return result;
    }

    private static Region BuildRegion(WmpRegion r, WdlDeclarations declarations, double defaultClipDistance)
    {
        var region = new Region(r.Name) { FloorHeight = r.FloorHeight, CeilHeight = r.CeilHeight };
        region["CLIP_DIST"] = defaultClipDistance;
        if (declarations.GetRegionTextures(r.Name) is { } textures)
        {
            region.FloorTexture = textures.Floor;
            region.CeilTexture = textures.Ceiling;
            region.GeniusName = textures.Genius;
            region.BelowName = textures.Below;
            region.Ambient = textures.Ambient;
            region.FloorOffsetX = textures.FloorOffsetX;
            region.FloorOffsetY = textures.FloorOffsetY;
            region.CeilOffsetX = textures.CeilOffsetX;
            region.CeilOffsetY = textures.CeilOffsetY;
            region.EachTickAction = textures.EachTickAction;
            region.EachCycleAction = textures.EachCycleAction;
            ApplyActionHooks(region, textures.ActionHooks);
            ApplySkills(region, textures.Skills);
        }

        return region;
    }

    /// <summary>
    /// The deepest <c>BELOW</c> chain followed; a longer one is taken for a cycle in the declarations.
    /// </summary>
    private const int MaxBelowDepth = 16;

    /// <summary>
    /// Adds the regions that <c>BELOW</c> stacks under the map's regions (manual p.59–61). The map
    /// lists only the top region of a stack, so every map region whose type names a <c>BELOW</c>
    /// region gets its own instance of that region type, and of the type's own <c>BELOW</c> and so
    /// on, with the heights and properties of its WDL declaration and the top region's footprint.
    /// They follow the map's regions, so map indices stay valid. A map that does list a region of the
    /// <c>BELOW</c> name (the retail maps never do) is linked to that region instead.
    /// </summary>
    private static void StackBelowRegions(List<Region> regions, WdlDeclarations declarations, double defaultClipDistance)
    {
        int mapRegions = regions.Count;
        for (int top = 0; top < mapRegions; top++)
        {
            regions[top].FootprintRegion = top;
            regions[top].Top = regions[top];
        }

        for (int top = 0; top < mapRegions; top++)
        {
            Region topRegion = regions[top];
            if (topRegion.BelowName is { } listedName &&
                regions.FindIndex(0, mapRegions, r => r.Name.Equals(listedName, StringComparison.OrdinalIgnoreCase)) is >= 0 and int listed)
            {
                topRegion.BelowRegion = listed;
                if (regions[listed].AboveRegion < 0)
                {
                    regions[listed].AboveRegion = top;
                }

                continue;
            }

            int upper = top;
            for (int depth = 0; depth < MaxBelowDepth && regions[upper].BelowName is { } belowName; depth++)
            {
                if (declarations.GetRegionTextures(belowName) is not { } definition)
                {
                    break;
                }

                // A BELOW region without its own heights hangs under its upper region with no headroom.
                double floor = definition.FloorHeight ?? regions[upper].FloorHeight;
                double ceiling = definition.CeilHeight ?? floor;
                Region lower = BuildRegion(new WmpRegion(belowName, (float)floor, (float)ceiling), declarations, defaultClipDistance);
                lower.FootprintRegion = top;
                lower.Top = topRegion;
                lower.AboveRegion = upper;
                regions[upper].BelowRegion = regions.Count;
                regions.Add(lower);
                upper = regions.Count - 1;
            }
        }
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
        wall["MAP_COLOR"] = 1;

        if (declarations.GetWall(w.Name) is { } definition)
        {
            wall.Texture = definition.Texture;
            wall.AttachTexture = definition.AttachTexture;
            wall.OffsetX = definition.OffsetX ?? wall.OffsetX;
            wall.OffsetY = definition.OffsetY ?? wall.OffsetY;
            wall.EachTickAction = definition.EachTickAction;
            wall.EachCycleAction = definition.EachCycleAction;
            ApplyActionHooks(wall, definition.ActionHooks);
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

    private static Dictionary<string, LevelBitmap> BuildBitmaps(WdlDeclarations declarations)
    {
        var catalog = new Dictionary<string, LevelBitmap>(StringComparer.OrdinalIgnoreCase);
        foreach (string bitmapName in declarations.BitmapNames)
        {
            if (declarations.ResolveBitmap(bitmapName) is { } bitmap)
            {
                catalog[bitmapName] = bitmap;
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
        entity["MAP_COLOR"] = 1;
        if (entity is Actor)
        {
            entity["ACTOR"] = 1;
            entity["SAVE"] = 1;
        }

        // Dress the placement from its WDL type definition (sprite texture, height, angle offset).
        if (declarations.GetEntity(placement.Name) is { } definition)
        {
            entity.Texture = definition.Texture;
            entity.Height = definition.Height;
            entity.Angle = definition.AngleRadians;
            entity.EachTickAction = definition.EachTickAction;
            entity.EachCycleAction = definition.EachCycleAction;
            ApplyActionHooks(entity, definition.ActionHooks);
            foreach ((string name, string? value) in definition.Strings)
            {
                entity.TrySetString(name, value);
            }
            ApplySkills(entity, definition.Skills);
        }

        return entity;
    }

    private static void ApplyActionHooks(AcknexObject obj, IReadOnlyDictionary<string, string?> actionHooks)
    {
        foreach ((string name, string? actionName) in actionHooks)
        {
            switch (obj)
            {
                case Region region:
                    region.SetActionHook(name, actionName);
                    break;
                case Wall wall:
                    wall.SetActionHook(name, actionName);
                    break;
                case MapEntity entity:
                    entity.SetActionHook(name, actionName);
                    break;
            }
        }
    }

    /// <summary>Applies a definition's numeric properties, held in fixed point like any literal (OV-20261029-numeric-edge).</summary>
    private static void ApplySkills(AcknexObject obj, IReadOnlyDictionary<string, double> skills)
    {
        foreach ((string name, double value) in skills)
        {
            obj[name] = WdlFixed.Literal(value);
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

    /// <summary>
    /// Collects a top-level global scheduler list (<c>EACH_TICK a, NULL, b;</c> or the same with
    /// <c>EACH_SEC</c>) into its 16 slots, in order.
    /// </summary>
    private static string?[] GatherSchedulerSlotActions(WdlProgram program, string keyword)
    {
        string?[] actions = new string?[16];
        foreach (WdlItem item in program.Items)
        {
            if (!item.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase))
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

    private static (
        Dictionary<string, double> Values,
        HashSet<string> Global,
        HashSet<string> ExplicitlyInitialized,
        Dictionary<string, SkillRange> Bounds) GatherSkills(WdlProgram program)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var global = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitlyInitialized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bounds = new Dictionary<string, SkillRange>(StringComparer.OrdinalIgnoreCase);

        foreach (WdlItem item in program.Items)
        {
            if (!item.Keyword.Equals("SKILL", StringComparison.OrdinalIgnoreCase) || item.Header.Count == 0)
            {
                continue;
            }

            double value = 0;
            bool hasExplicitValue = false;
            bool isGlobal = false;
            double? min = null;
            double? max = null;
            if (item.Body is { } body)
            {
                foreach (WdlItem property in body.Items)
                {
                    if (property.Keyword.Equals("VAL", StringComparison.OrdinalIgnoreCase) && ParseNumber(property) is { } parsedVal)
                    {
                        // Held in the original's fixed point, like any literal (OV-20261029-numeric-edge).
                        value = WdlFixed.Literal(parsedVal);
                        hasExplicitValue = true;
                    }
                    else if (property.Keyword.Equals("MIN", StringComparison.OrdinalIgnoreCase) && ParseNumber(property) is { } parsedMin)
                    {
                        min = parsedMin;
                    }
                    else if (property.Keyword.Equals("MAX", StringComparison.OrdinalIgnoreCase) && ParseNumber(property) is { } parsedMax)
                    {
                        max = parsedMax;
                    }
                    else if (property.Keyword.Equals("TYPE", StringComparison.OrdinalIgnoreCase) &&
                        property.Header.Any(token => token.Text.Equals("GLOBAL", StringComparison.OrdinalIgnoreCase)))
                    {
                        isGlobal = true;
                    }
                }
            }

            string name = item.Header[0].Text;
            values[name] = value;
            if (isGlobal)
            {
                global.Add(name);
            }
            else
            {
                global.Remove(name);
            }
            if (hasExplicitValue)
            {
                explicitlyInitialized.Add(name);
            }
            else
            {
                explicitlyInitialized.Remove(name);
            }
            if (min is not null || max is not null)
            {
                bounds[name] = new SkillRange(min ?? double.NegativeInfinity, max ?? double.PositiveInfinity);
            }
        }

        return (values, global, explicitlyInitialized, bounds);
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

    private static Dictionary<string, string> GatherSounds(WdlProgram program)
    {
        var sounds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (WdlItem item in program.Items)
        {
            if (!item.Keyword.Equals("SOUND", StringComparison.OrdinalIgnoreCase) ||
                item.Header.Count == 0 ||
                item.Header[0].Kind != WdlTokenKind.Identifier)
            {
                continue;
            }

            foreach (WdlToken token in item.Header.Skip(1))
            {
                if (token.Kind is WdlTokenKind.FileRef or WdlTokenKind.StringLiteral)
                {
                    sounds[item.Header[0].Text] = token.Text;
                    break;
                }
            }
        }

        return sounds;
    }

    private static Dictionary<string, string> GatherPalettes(WdlProgram program)
    {
        var palettes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (WdlItem item in program.Items)
        {
            if (!item.Keyword.Equals("PALETTE", StringComparison.OrdinalIgnoreCase) ||
                item.Header.Count == 0 ||
                item.Header[0].Kind != WdlTokenKind.Identifier ||
                item.Body is not { } body)
            {
                continue;
            }

            foreach (WdlItem property in body.Items)
            {
                if (!property.Keyword.Equals("PALFILE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (WdlToken token in property.Header)
                {
                    if (token.Kind is WdlTokenKind.FileRef or WdlTokenKind.StringLiteral)
                    {
                        palettes[item.Header[0].Text] = token.Text;
                        break;
                    }
                }
            }
        }

        return palettes;
    }

    private static Dictionary<string, WdlUiElement> GatherUiElements(WdlProgram program)
    {
        var elements = new Dictionary<string, WdlUiElement>(StringComparer.OrdinalIgnoreCase);
        foreach (WdlItem item in program.Items)
        {
            if (!IsUiDeclaration(item.Keyword) ||
                item.Header.Count == 0 ||
                item.Header[0].Kind != WdlTokenKind.Identifier)
            {
                continue;
            }

            string name = item.Header[0].Text;
            string key = item.Keyword.Equals("MSPRITE", StringComparison.OrdinalIgnoreCase)
                ? "$MSPRITE"
                : name;
            elements[key] = new WdlUiElement(
                item.Keyword,
                name,
                item.Header.Skip(1).Select(ToUiValue).ToArray(),
                item.Body?.Items.Select(ToUiProperty).ToArray() ?? []);
        }

        return elements;
    }

    private static WdlUiProperty ToUiProperty(WdlItem item) =>
        new(item.Keyword, item.Header.Select(ToUiValue).ToArray());

    private static WdlUiValue ToUiValue(WdlToken token) =>
        new(token.Kind.ToString(), token.Text);

    private static bool IsUiDeclaration(string keyword) =>
        keyword.Equals("OVERLAY", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("OVLY", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("PANEL", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("TEXT", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("FONT", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("VIDEO", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("DITHER", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("MSPRITE", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("NEXUS", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("SAVEDIR", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("BIND", StringComparison.OrdinalIgnoreCase);

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
