// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Map;

namespace OpenVirtue.Engine.Rendering;

/// <summary>A camera-facing static sprite billboard resolved from a placed THING/ACTOR.</summary>
public readonly record struct SpriteBillboard(string Texture, Vector3 BasePosition, float HalfWidth, float HalfHeight);

/// <summary>
/// Builds static billboard sprites from the level's placed THINGs/ACTORs. This is the
/// renderer-agnostic layout step; API-specific drawing and facing remain in the app layer.
/// </summary>
public static class SpriteBuilder
{
    /// <summary>
    /// World units per source bitmap pixel. This is a first-pass viewer calibration, not
    /// Acknex parity sizing.
    /// </summary>
    public const float DefaultWorldUnitsPerPixel = 0.04f;

    /// <summary>Builds all resolvable static sprite billboards in level order.</summary>
    public static IReadOnlyList<SpriteBillboard> Build(Level level, float worldUnitsPerPixel = DefaultWorldUnitsPerPixel)
    {
        ArgumentNullException.ThrowIfNull(level);

        var billboards = new List<SpriteBillboard>(level.Things.Count + level.Actors.Count);
        foreach (Thing thing in level.Things)
        {
            Add(level, thing, worldUnitsPerPixel, billboards);
        }

        foreach (Actor actor in level.Actors)
        {
            Add(level, actor, worldUnitsPerPixel, billboards);
        }

        return billboards;
    }

    /// <summary>Returns sprites ordered far-to-near from <paramref name="cameraPosition"/>.</summary>
    public static IReadOnlyList<SpriteBillboard> OrderBackToFront(
        IEnumerable<SpriteBillboard> billboards,
        Vector3 cameraPosition)
    {
        ArgumentNullException.ThrowIfNull(billboards);
        return billboards
            .OrderByDescending(b => Vector3.DistanceSquared(b.BasePosition, cameraPosition))
            .ToArray();
    }

    private static void Add(Level level, MapEntity entity, float worldUnitsPerPixel, List<SpriteBillboard> billboards)
    {
        if (worldUnitsPerPixel <= 0 ||
            entity.Texture is not { } texture ||
            !level.Textures.TryGetValue(texture, out LevelTexture levelTexture) ||
            levelTexture.Width <= 0 ||
            levelTexture.Height <= 0)
        {
            return;
        }

        float worldWidth = levelTexture.Width * worldUnitsPerPixel;
        float worldHeight = levelTexture.Height * worldUnitsPerPixel;
        float floor = entity.Region >= 0 && entity.Region < level.Regions.Count
            ? (float)level.Regions[entity.Region].FloorHeight
            : 0f;

        billboards.Add(new SpriteBillboard(
            texture,
            new Vector3((float)entity.X, floor, (float)entity.Y),
            worldWidth * 0.5f,
            worldHeight * 0.5f));
    }
}
