// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Map;

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
        IReadOnlyDictionary<string, double> skills)
    {
        Name = name;
        Vertices = vertices;
        Regions = regions;
        Walls = walls;
        Things = things;
        Actors = actors;
        PlayerStart = playerStart;
        Skills = skills;
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
}
