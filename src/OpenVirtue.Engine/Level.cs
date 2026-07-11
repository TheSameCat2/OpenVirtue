// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Map;
using OpenVirtue.Formats.Wdl;

namespace OpenVirtue.Engine;

/// <summary>A documented A3 <c>VIDEO</c> screen mode used as the level's virtual pixel grid.</summary>
public readonly record struct LevelVideoMode(string Name, int Width, int Height);

/// <summary>A point on a named cyclic actor path.</summary>
public readonly record struct LevelWayPoint(double X, double Y);

/// <summary>A named cyclic actor path loaded from the WMP map.</summary>
public sealed record LevelWay(string Name, IReadOnlyList<LevelWayPoint> Points);

/// <summary>
/// A loaded level: the world geometry and placed objects (materialized as the typed
/// engine object model) together with the level's global skills. This is the in-memory
/// result of combining a level's WMP map with its (flattened) WDL declarations.
/// </summary>
public sealed class Level
{
    public Level(
        string name,
        IReadOnlyList<Vertex> vertices,
        IReadOnlyList<Region> regions,
        IReadOnlyList<Wall> walls,
        IReadOnlyList<Thing> things,
        IReadOnlyList<Actor> actors,
        IReadOnlyDictionary<string, LevelWay> ways,
        PlayerStart? playerStart,
        LevelVideoMode? videoMode,
        IReadOnlyDictionary<string, double> skills,
        IReadOnlySet<string> globalSkills,
        IReadOnlySet<string> explicitlyInitializedSkills,
        IReadOnlyDictionary<string, SkillRange> skillBounds,
        IReadOnlyDictionary<string, WdlString> strings,
        IReadOnlyDictionary<string, WdlSynonym> synonyms,
        IReadOnlyList<string> searchPaths,
        IReadOnlyDictionary<string, string> sounds,
        IReadOnlyDictionary<string, string> palettes,
        IReadOnlyDictionary<string, WdlUiElement> uiElements,
        IReadOnlyDictionary<string, LevelBitmap> bitmaps,
        IReadOnlyDictionary<string, LevelTexture> textures,
        IReadOnlyDictionary<string, WdlBlock> actions,
        IReadOnlyList<string?> eachTickActions,
        IReadOnlyDictionary<string, string?> actionHooks,
        string? startupAction)
    {
        Name = name;
        Vertices = vertices;
        Regions = regions;
        Walls = walls;
        Things = things;
        Actors = actors;
        Ways = ways;
        PlayerStart = playerStart;
        VideoMode = videoMode;
        Skills = skills;
        GlobalSkills = globalSkills;
        ExplicitlyInitializedSkills = explicitlyInitializedSkills;
        SkillBounds = skillBounds;
        Strings = strings;
        Synonyms = synonyms;
        SearchPaths = searchPaths;
        Sounds = sounds;
        Palettes = palettes;
        UiElements = uiElements;
        Bitmaps = bitmaps;
        Textures = textures;
        Actions = actions;
        EachTickActions = eachTickActions;
        ActionHooks = actionHooks;
        StartupAction = startupAction;
    }

    /// <summary>The level's name (its main WDL file name).</summary>
    public string Name { get; }

    public IReadOnlyList<Vertex> Vertices { get; }
    public IReadOnlyList<Region> Regions { get; }
    public IReadOnlyList<Wall> Walls { get; }
    public IReadOnlyList<Thing> Things { get; }
    public IReadOnlyList<Actor> Actors { get; }

    /// <summary>Named cyclic actor paths from WMP <c>WAY</c> records.</summary>
    public IReadOnlyDictionary<string, LevelWay> Ways { get; }

    /// <summary>Where the player spawns, if the map defines it.</summary>
    public PlayerStart? PlayerStart { get; }

    /// <summary>The active top-level WDL <c>VIDEO</c> resolution, if declared.</summary>
    public LevelVideoMode? VideoMode { get; }

    /// <summary>Skills declared in the level's WDL, with their initial values.</summary>
    public IReadOnlyDictionary<string, double> Skills { get; }

    /// <summary>Skills declared with <c>TYPE GLOBAL</c>, whose values survive a <c>LEVEL</c> change.</summary>
    public IReadOnlySet<string> GlobalSkills { get; }

    /// <summary>Skills whose declarations supplied an explicit <c>VAL</c> initializer.</summary>
    public IReadOnlySet<string> ExplicitlyInitializedSkills { get; }

    /// <summary>Declared MIN/MAX bounds for the skills that specify them; the runtime clamps assignments to these.</summary>
    public IReadOnlyDictionary<string, SkillRange> SkillBounds { get; }

    /// <summary>Top-level WDL <c>STRING</c> declarations and their fixed capacities.</summary>
    public IReadOnlyDictionary<string, WdlString> Strings { get; }

    /// <summary>Top-level WDL <c>SYNONYM</c> declarations, by synonym keyword.</summary>
    public IReadOnlyDictionary<string, WdlSynonym> Synonyms { get; }

    /// <summary>Top-level WDL <c>PATH</c> declarations used when resolving external assets.</summary>
    public IReadOnlyList<string> SearchPaths { get; }

    /// <summary>Top-level WDL <c>SOUND</c> declarations, by sound keyword.</summary>
    public IReadOnlyDictionary<string, string> Sounds { get; }

    /// <summary>Top-level WDL <c>PALETTE</c> declarations, by palette keyword.</summary>
    public IReadOnlyDictionary<string, string> Palettes { get; }

    /// <summary>Top-level WDL UI declarations such as <c>OVERLAY</c>, <c>PANEL</c>, <c>TEXT</c>, and <c>FONT</c>.</summary>
    public IReadOnlyDictionary<string, WdlUiElement> UiElements { get; }

    /// <summary>Top-level WDL <c>BMAP</c> declarations, by bitmap keyword.</summary>
    public IReadOnlyDictionary<string, LevelBitmap> Bitmaps { get; }

    /// <summary>Resolved textures (name → source PCX file + rectangle + scale) referenced by the geometry.</summary>
    public IReadOnlyDictionary<string, LevelTexture> Textures { get; }

    /// <summary>The level's <c>ACTION</c> bodies, by name — the scripts the interpreter runs.</summary>
    public IReadOnlyDictionary<string, WdlBlock> Actions { get; }

    /// <summary>Initial global <c>EACH_TICK</c> action slots declared by the level WDL.</summary>
    public IReadOnlyList<string?> EachTickActions { get; }

    /// <summary>Initial global <c>IF_*</c> action hooks declared by the level WDL.</summary>
    public IReadOnlyDictionary<string, string?> ActionHooks { get; }

    /// <summary>The action named by <c>IF_START</c> — the level's startup script, if any.</summary>
    public string? StartupAction { get; }
}
