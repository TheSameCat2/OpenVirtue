// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Map;
using OpenVirtue.Formats.Wdl;

namespace OpenVirtue.Engine;

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
        PlayerStart? playerStart,
        IReadOnlyDictionary<string, double> skills,
        IReadOnlyDictionary<string, SkillRange> skillBounds,
        IReadOnlyDictionary<string, LevelTexture> textures,
        IReadOnlyDictionary<string, WdlBlock> actions,
        string? startupAction)
    {
        Name = name;
        Vertices = vertices;
        Regions = regions;
        Walls = walls;
        Things = things;
        Actors = actors;
        PlayerStart = playerStart;
        Skills = skills;
        SkillBounds = skillBounds;
        Textures = textures;
        Actions = actions;
        StartupAction = startupAction;
    }

    /// <summary>The level's name (its main WDL file name).</summary>
    public string Name { get; }

    public IReadOnlyList<Vertex> Vertices { get; }
    public IReadOnlyList<Region> Regions { get; }
    public IReadOnlyList<Wall> Walls { get; }
    public IReadOnlyList<Thing> Things { get; }
    public IReadOnlyList<Actor> Actors { get; }

    /// <summary>Where the player spawns, if the map defines it.</summary>
    public PlayerStart? PlayerStart { get; }

    /// <summary>Global skills declared in the level's WDL, with their initial values.</summary>
    public IReadOnlyDictionary<string, double> Skills { get; }

    /// <summary>Declared MIN/MAX bounds for the skills that specify them; the runtime clamps assignments to these.</summary>
    public IReadOnlyDictionary<string, SkillRange> SkillBounds { get; }

    /// <summary>Resolved textures (name → source PCX file + rectangle + scale) referenced by the geometry.</summary>
    public IReadOnlyDictionary<string, LevelTexture> Textures { get; }

    /// <summary>The level's <c>ACTION</c> bodies, by name — the scripts the interpreter runs.</summary>
    public IReadOnlyDictionary<string, WdlBlock> Actions { get; }

    /// <summary>The action named by <c>IF_START</c> — the level's startup script, if any.</summary>
    public string? StartupAction { get; }
}
