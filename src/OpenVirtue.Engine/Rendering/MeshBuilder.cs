// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Map;

namespace OpenVirtue.Engine.Rendering;

/// <summary>
/// Builds renderer-ready geometry from a loaded <see cref="Level"/>. The first pass emits
/// the walls as vertical textured quads (two triangles each), grouped by texture — enough
/// for a recognizable first render. Floor/ceiling polygons (which require triangulating each
/// region's wall loop) come later.
/// </summary>
public static class MeshBuilder
{
    /// <summary>Builds wall geometry: a vertical quad per wall, spanning its region's floor to ceiling.</summary>
    public static LevelMesh BuildWalls(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);

        var byTexture = new Dictionary<string, List<RenderVertex>>(StringComparer.OrdinalIgnoreCase);
        var untextured = new List<RenderVertex>();

        foreach (Wall wall in level.Walls)
        {
            if (!TryVertex(level, wall.Vertex1, out Vertex v1) ||
                !TryVertex(level, wall.Vertex2, out Vertex v2) ||
                !TryHeights(level, wall, out float floor, out float ceiling) ||
                ceiling <= floor)
            {
                continue;
            }

            float dx = v2.X - v1.X;
            float dz = v2.Y - v1.Y;
            float length = MathF.Sqrt((dx * dx) + (dz * dz));
            float height = ceiling - floor;

            // Texture coordinates in tile units: world distance / texture scale (so the
            // texture repeats with a wrap sampler). Default scale when the texture is unknown.
            (float scaleX, float scaleY) = TextureScale(level, wall.Texture);
            float u = length / scaleX;
            float v = height / scaleY;

            // Quad corners in world space (x, y-up, z); WMP (x, y) -> world (x, z).
            var bottomLeft = new RenderVertex(v1.X, floor, v1.Y, 0, 0);
            var bottomRight = new RenderVertex(v2.X, floor, v2.Y, u, 0);
            var topRight = new RenderVertex(v2.X, ceiling, v2.Y, u, v);
            var topLeft = new RenderVertex(v1.X, ceiling, v1.Y, 0, v);

            List<RenderVertex> target = wall.Texture is { } texture ? Bucket(byTexture, texture) : untextured;
            target.Add(bottomLeft);
            target.Add(bottomRight);
            target.Add(topRight);
            target.Add(bottomLeft);
            target.Add(topRight);
            target.Add(topLeft);
        }

        var batches = new List<MeshBatch>(byTexture.Count + 1);
        foreach ((string texture, List<RenderVertex> vertices) in byTexture)
        {
            batches.Add(new MeshBatch(texture, vertices));
        }

        if (untextured.Count > 0)
        {
            batches.Add(new MeshBatch(null, untextured));
        }

        return new LevelMesh(batches);
    }

    private static bool TryVertex(Level level, int index, out Vertex vertex)
    {
        if (index >= 0 && index < level.Vertices.Count)
        {
            vertex = level.Vertices[index];
            return true;
        }

        vertex = default;
        return false;
    }

    private static bool TryHeights(Level level, Wall wall, out float floor, out float ceiling)
    {
        // Span whichever adjacent region is valid (a solid wall has a real region on one side only).
        Region? region = RegionAt(level, wall.LeftRegion) ?? RegionAt(level, wall.RightRegion);
        if (region is null)
        {
            floor = ceiling = 0;
            return false;
        }

        floor = (float)region.FloorHeight;
        ceiling = (float)region.CeilHeight;
        return true;
    }

    private static Region? RegionAt(Level level, int index) =>
        index >= 0 && index < level.Regions.Count ? level.Regions[index] : null;

    private static (float ScaleX, float ScaleY) TextureScale(Level level, string? texture)
    {
        const float defaultScale = 16f;
        if (texture is not null &&
            level.Textures.TryGetValue(texture, out LevelTexture resolved) &&
            resolved.ScaleX > 0 && resolved.ScaleY > 0)
        {
            return ((float)resolved.ScaleX, (float)resolved.ScaleY);
        }

        return (defaultScale, defaultScale);
    }

    private static List<RenderVertex> Bucket(Dictionary<string, List<RenderVertex>> map, string key)
    {
        if (!map.TryGetValue(key, out List<RenderVertex>? list))
        {
            list = [];
            map[key] = list;
        }

        return list;
    }
}
