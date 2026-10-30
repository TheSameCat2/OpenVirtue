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

        var images = archive.Entries
            .Where(static e => e.Name.EndsWith(".pcx", StringComparison.OrdinalIgnoreCase))
            .GroupBy(static e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.OrdinalIgnoreCase);
        return LoadCore(
            mainWdlName,
            mainSource,
            name => resources.GetValueOrDefault(Path.GetFileName(name)),
            defines,
            mapOverrideName,
            file => images.TryGetValue(Path.GetFileName(file), out WrsEntry? entry) ? PcxSize(entry.GetData()) : null);
    }

    /// <summary>A PCX image's size from its header (window xmin, ymin, xmax, ymax at bytes 4–11).</summary>
    private static (int Width, int Height)? PcxSize(byte[] pcx)
    {
        if (pcx.Length < 12 || pcx[0] != 0x0A)
        {
            return null;
        }

        int width = BitConverter.ToUInt16(pcx, 8) - BitConverter.ToUInt16(pcx, 4) + 1;
        int height = BitConverter.ToUInt16(pcx, 10) - BitConverter.ToUInt16(pcx, 6) + 1;
        return width > 0 && height > 0 ? (width, height) : null;
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
        string? mapOverrideName = null,
        Func<string, (int Width, int Height)?>? measureImage = null)
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
        var walls = map.Walls.Select(w => BuildWall(w, declarations, placed: true)).ToList();
        var things = map.Things.Select(p => BuildEntity(new Thing(p.Name), p, declarations)).ToList();
        var actors = map.Actors.Select(p => BuildEntity(new Actor(p.Name), p, declarations)).ToList();
        foreach (MapEntity entity in things.Concat<MapEntity>(actors))
        {
            entity.Region = PlacementStorey(regions, entity);
        }

        List<AcknexObject> unplaced = BuildUnplacedObjects(map, declarations);
        things.AddRange(unplaced.OfType<Thing>());
        actors.AddRange(unplaced.OfType<Actor>());

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
            Dictionary<string, SkillRange> skillBounds,
            HashSet<string> localSkills,
            List<string> skillOrder) = GatherSkills(program);
        return new Level(
            name, vertices, regions, walls, things, actors, ways, start,
            FindVideoMode(program),
            skillValues, globalSkills, explicitlyInitializedSkills, skillBounds,
            GatherStrings(program), GatherSynonyms(program), GatherSearchPaths(program),
            GatherSounds(program), GatherPalettes(program), GatherUiElements(program), BuildBitmaps(declarations),
            BuildTextures(declarations, measureImage), declarations.Actions,
            GatherSchedulerSlotActions(program, "EACH_TICK"), GatherActionHooks(program), FindActionName(program, "IF_START"),
            GatherSchedulerSlotActions(program, "EACH_SEC"))
        {
            LocalSkills = localSkills,
            SkillDeclarationOrder = skillOrder,
            ActionOrder = declarations.ActionOrder,
            MapFileName = Path.GetFileName(mapName),
            TextureDeclarations = declarations.TextureDeclarations,
            UnplacedObjects = unplaced,
            SaveTextures = declarations.SaveTextures,
            NumberedDeclarations = declarations.NumberedDeclarations,
            MapObjects = [.. map.ObjectOrder.Select(o => o.Kind switch
            {
                "WALL" => (AcknexObject?)walls[o.Index],
                "THING" => things[o.Index],
                "ACTOR" => actors[o.Index],
                _ => null,
            })],
            PaletteShading = GatherPaletteShading(program),
        };
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
    /// Adds the regions that <c>BELOW</c> stacks under the map's regions (manual p.59–61), and the
    /// declared regions the map does not place. The map lists only the top region of a stack, so
    /// every map region whose type names a <c>BELOW</c> region gets its own instance of that region
    /// type, and of the type's own <c>BELOW</c> and so on, with the heights and properties of its WDL
    /// declaration and the top region's footprint. They follow the map's regions, so map indices stay
    /// valid, ordered as the original's save lists them: by the declaration order of their types,
    /// with a declared type that no stack uses as a region of its own, outside the map
    /// (OV-20261030-save-body round 15). Where one type sits under several top regions, its
    /// instances follow their tops' map order (unmeasured). A map that does list a region of the
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

        var lowers = new List<(Region Upper, Region Lower)>();
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

            Region upper = topRegion;
            for (int depth = 0; depth < MaxBelowDepth && upper.BelowName is { } belowName; depth++)
            {
                if (declarations.GetRegionTextures(belowName) is not { } definition)
                {
                    break;
                }

                // A BELOW region without its own heights hangs under its upper region with no headroom.
                double floor = definition.FloorHeight ?? upper.FloorHeight;
                double ceiling = definition.CeilHeight ?? floor;
                Region lower = BuildRegion(new WmpRegion(belowName, (float)floor, (float)ceiling), declarations, defaultClipDistance);
                lower.FootprintRegion = top;
                lower.Top = topRegion;
                lowers.Add((upper, lower));
                upper = lower;
            }
        }

        var placed = new HashSet<string>(regions.Take(mapRegions).Select(static r => r.Name), StringComparer.OrdinalIgnoreCase);
        var index = new Dictionary<Region, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < mapRegions; i++)
        {
            index[regions[i]] = i;
        }

        foreach (string name in declarations.RegionDeclarations.Where(n => !placed.Contains(n)))
        {
            bool stacked = false;
            foreach ((Region _, Region lower) in lowers.Where(l => l.Lower.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                index[lower] = regions.Count;
                regions.Add(lower);
                stacked = true;
            }

            if (!stacked && declarations.GetRegionTextures(name) is { } definition)
            {
                double floor = definition.FloorHeight ?? 0;
                Region alone = BuildRegion(new WmpRegion(name, (float)floor, (float)(definition.CeilHeight ?? floor)), declarations, defaultClipDistance);
                alone.FootprintRegion = regions.Count;
                alone.Top = alone;
                regions.Add(alone);
            }
        }

        foreach ((Region upper, Region lower) in lowers)
        {
            upper.BelowRegion = index[lower];
            lower.AboveRegion = index[upper];
        }
    }

    private static Wall BuildWall(WmpWall w, WdlDeclarations declarations, bool placed)
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

        // The original starts every placed wall's OFFSET_Y one raw step above the map's or the WDL's
        // value (0 reads 1/1024, −30 reads −29.999); an unplaced wall's and a script's SET are kept as
        // given (OV-20261029-save-body round 13).
        if (placed)
        {
            wall.OffsetY += 1 / 1024.0;
        }

        return wall;
    }

    /// <summary>
    /// The declared walls, things and actors the map does not place, as the original keeps them: with
    /// <c>INVISIBLE</c> set (manual p.70), at 0,0 in region 0 (OV-20261029-save-body round 13).
    /// </summary>
    private static List<AcknexObject> BuildUnplacedObjects(WmpMap map, WdlDeclarations declarations)
    {
        var placed = new HashSet<string>(
            map.Walls.Select(static w => w.Name).Concat(map.Things.Select(static t => t.Name)).Concat(map.Actors.Select(static a => a.Name)),
            StringComparer.OrdinalIgnoreCase);
        var unplaced = new List<AcknexObject>();
        foreach ((string kind, string name) in declarations.ObjectDeclarations)
        {
            if (placed.Contains(name))
            {
                continue;
            }

            AcknexObject obj = kind switch
            {
                "WALL" => BuildWall(new WmpWall(name, 0, 0, 0, 0, 0, 0), declarations, placed: false),
                "ACTOR" => BuildEntity(new Actor(name), new WmpPlacement(name, 0, 0, 0, 0), declarations),
                _ => BuildEntity(new Thing(name), new WmpPlacement(name, 0, 0, 0, 0), declarations),
            };
            obj["INVISIBLE"] = 1;
            obj["VISIBLE"] = 0;
            unplaced.Add(obj);
        }

        return unplaced;
    }

    private static Dictionary<string, LevelTexture> BuildTextures(
        WdlDeclarations declarations,
        Func<string, (int Width, int Height)?>? measureImage)
    {
        var catalog = new Dictionary<string, LevelTexture>(StringComparer.OrdinalIgnoreCase);
        foreach (string textureName in declarations.TextureNames)
        {
            if (declarations.ResolveTexture(textureName) is { } texture)
            {
                catalog[textureName] = measureImage is null ? texture : Measured(texture, measureImage);
            }
        }

        return catalog;
    }

    /// <summary>
    /// A texture whose <c>BMAP</c>s name a whole image (no x, y, width, height; manual p.30) takes its
    /// size from the image, so sprites, shots and contact see it.
    /// </summary>
    private static LevelTexture Measured(LevelTexture texture, Func<string, (int Width, int Height)?> measureImage)
    {
        LevelBitmap? Measure(LevelBitmap? frame) =>
            frame is { } bitmap && (bitmap.Width <= 0 || bitmap.Height <= 0) && measureImage(bitmap.File) is { } size
                ? bitmap with { Width = size.Width - bitmap.X, Height = size.Height - bitmap.Y }
                : frame;

        if ((texture.Width > 0 && texture.Height > 0) || measureImage(texture.File) is not { } whole)
        {
            return texture with { Frames = [.. texture.Frames.Select(Measure)] };
        }

        return texture with
        {
            Width = whole.Width - texture.X,
            Height = whole.Height - texture.Y,
            Frames = [.. texture.Frames.Select(Measure)],
        };
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

    /// <summary>
    /// The storey of a stack a thing or actor starts in (manual p.61). Without <c>GROUND</c> it is
    /// the uppermost region. With <c>GROUND</c> it is the lowest region whose ceiling is above the
    /// object's feet (its <c>HEIGHT</c>, absolute with <c>GROUND</c>).
    /// </summary>
    private static int PlacementStorey(List<Region> regions, MapEntity entity)
    {
        int region = entity.Region;
        if ((uint)region >= (uint)regions.Count)
        {
            return region;
        }

        for (int depth = 0; regions[region].AboveRegion >= 0 && depth < regions.Count; depth++)
        {
            region = regions[region].AboveRegion;
        }

        if (entity["GROUND"] <= 0)
        {
            return region;
        }

        var stack = new List<int>();
        for (int current = region; (uint)current < (uint)regions.Count && stack.Count <= regions.Count; current = regions[current].BelowRegion)
        {
            stack.Add(current);
        }

        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (regions[stack[i]].CeilHeight > entity.Z)
            {
                return stack[i];
            }
        }

        return region;
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
        Dictionary<string, SkillRange> Bounds,
        HashSet<string> Local,
        List<string> Order) GatherSkills(WdlProgram program)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var global = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var local = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
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
            bool isLocal = false;
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
                    else if (property.Keyword.Equals("TYPE", StringComparison.OrdinalIgnoreCase) &&
                        property.Header.Any(token => token.Text.Equals("LOCAL", StringComparison.OrdinalIgnoreCase)))
                    {
                        isLocal = true;
                    }
                }
            }

            string name = item.Header[0].Text;
            values[name] = value;
            // A skill declared again keeps its first place, where the original's saves list it (LEGALISM's
            // fadeVal, declared in its main file and again in dead.wdl; save-body round 16).
            if (!order.Exists(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                order.Add(name);
            }
            if (isLocal)
            {
                local.Add(name);
            }
            else
            {
                local.Remove(name);
            }

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

        return (values, global, explicitlyInitialized, bounds, local, order);
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

    private static Dictionary<string, LevelPaletteShading> GatherPaletteShading(WdlProgram program)
    {
        var shading = new Dictionary<string, LevelPaletteShading>(StringComparer.OrdinalIgnoreCase);
        foreach (WdlItem item in program.Items)
        {
            if (!item.Keyword.Equals("PALETTE", StringComparison.OrdinalIgnoreCase) ||
                item.Header.Count == 0 ||
                item.Body is not { } body)
            {
                continue;
            }

            var ranges = new List<(int, int)>();
            bool autoRange = false;
            bool hard = false;
            foreach (WdlItem property in body.Items)
            {
                if (property.Keyword.Equals("RANGE", StringComparison.OrdinalIgnoreCase))
                {
                    int[] numbers = property.Header
                        .Where(static t => t.Kind == WdlTokenKind.Number)
                        .Select(static t => (int)double.Parse(t.Text, CultureInfo.InvariantCulture))
                        .ToArray();
                    if (numbers.Length >= 2)
                    {
                        ranges.Add((numbers[0], numbers[1]));
                    }
                }
                else if (property.Keyword.Equals("FLAGS", StringComparison.OrdinalIgnoreCase))
                {
                    autoRange |= property.Header.Any(static t => t.Text.Equals("AUTORANGE", StringComparison.OrdinalIgnoreCase));
                    hard |= property.Header.Any(static t => t.Text.Equals("HARD", StringComparison.OrdinalIgnoreCase));
                }
            }

            shading[item.Header[0].Text] = new LevelPaletteShading(ranges, autoRange, hard);
        }

        return shading;
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
