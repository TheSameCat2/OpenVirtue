// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Map;

namespace OpenVirtue.Engine.Rendering;

/// <summary>
/// Builds renderer-ready geometry from a loaded <see cref="Level"/>: wall quads, plus
/// floor and ceiling surfaces for regions whose boundary forms a clean polygon loop
/// (traced from their walls and triangulated by <see cref="EarClipping"/>). Output is
/// GPU-agnostic and grouped by texture.
/// </summary>
public static class MeshBuilder
{
    private const float DefaultScale = 16f;

    /// <summary>Builds walls and floor/ceiling surfaces.</summary>
    public static LevelMesh Build(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        var accumulator = new Accumulator();
        AddWalls(level, accumulator);
        AddSurfaces(level, accumulator);
        return accumulator.ToMesh();
    }

    /// <summary>Builds wall geometry only (a vertical textured quad per wall).</summary>
    public static LevelMesh BuildWalls(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        var accumulator = new Accumulator();
        AddWalls(level, accumulator);
        return accumulator.ToMesh();
    }

    private static void AddWalls(Level level, Accumulator accumulator)
    {
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
            (float scaleX, float scaleY) = TextureScale(level, wall.Texture);
            float u = length / scaleX;
            float v = height / scaleY;

            List<RenderVertex> target = accumulator.Bucket(wall.Texture);
            var bottomLeft = new RenderVertex(v1.X, floor, v1.Y, 0, 0);
            var bottomRight = new RenderVertex(v2.X, floor, v2.Y, u, 0);
            var topRight = new RenderVertex(v2.X, ceiling, v2.Y, u, v);
            var topLeft = new RenderVertex(v1.X, ceiling, v1.Y, 0, v);
            target.Add(bottomLeft);
            target.Add(bottomRight);
            target.Add(topRight);
            target.Add(bottomLeft);
            target.Add(topRight);
            target.Add(topLeft);
        }
    }

    private static void AddSurfaces(Level level, Accumulator accumulator)
    {
        for (int regionIndex = 0; regionIndex < level.Regions.Count; regionIndex++)
        {
            List<int>? loop = TraceRegionLoop(level, regionIndex);
            if (loop is null)
            {
                continue;
            }

            var footprint = new Vector2[loop.Count];
            for (int i = 0; i < loop.Count; i++)
            {
                Vertex vertex = level.Vertices[loop[i]];
                footprint[i] = new Vector2(vertex.X, vertex.Y);
            }

            IReadOnlyList<int> triangles = EarClipping.Triangulate(footprint);
            if (triangles.Count == 0)
            {
                continue;
            }

            Region region = level.Regions[regionIndex];
            EmitSurface(level, accumulator, loop, triangles, (float)region.FloorHeight, region.FloorTexture, flip: false);
            EmitSurface(level, accumulator, loop, triangles, (float)region.CeilHeight, region.CeilTexture, flip: true);
        }
    }

    private static void EmitSurface(
        Level level, Accumulator accumulator, List<int> loop, IReadOnlyList<int> triangles, float height, string? texture, bool flip)
    {
        (float scaleX, float scaleY) = TextureScale(level, texture);
        List<RenderVertex> target = accumulator.Bucket(texture);

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int a = loop[triangles[i]];
            int b = loop[triangles[flip ? i + 2 : i + 1]];
            int c = loop[triangles[flip ? i + 1 : i + 2]];
            target.Add(SurfaceVertex(level, a, height, scaleX, scaleY));
            target.Add(SurfaceVertex(level, b, height, scaleX, scaleY));
            target.Add(SurfaceVertex(level, c, height, scaleX, scaleY));
        }
    }

    private static RenderVertex SurfaceVertex(Level level, int vertexIndex, float height, float scaleX, float scaleY)
    {
        Vertex vertex = level.Vertices[vertexIndex];
        return new RenderVertex(vertex.X, height, vertex.Y, vertex.X / scaleX, vertex.Y / scaleY);
    }

    /// <summary>
    /// Traces the boundary of a region into a single ordered vertex loop, or returns null
    /// when the region's walls don't form one clean simple polygon (it is then skipped).
    /// </summary>
    private static List<int>? TraceRegionLoop(Level level, int regionIndex)
    {
        var adjacency = new Dictionary<int, List<int>>();
        var edges = new HashSet<(int, int)>();

        foreach (Wall wall in level.Walls)
        {
            if ((wall.LeftRegion != regionIndex && wall.RightRegion != regionIndex) || wall.Vertex1 == wall.Vertex2)
            {
                continue;
            }

            (int, int) key = wall.Vertex1 < wall.Vertex2 ? (wall.Vertex1, wall.Vertex2) : (wall.Vertex2, wall.Vertex1);
            if (!edges.Add(key))
            {
                continue;
            }

            Connect(adjacency, wall.Vertex1, wall.Vertex2);
            Connect(adjacency, wall.Vertex2, wall.Vertex1);
        }

        if (edges.Count < 3)
        {
            return null;
        }

        foreach (List<int> neighbours in adjacency.Values)
        {
            if (neighbours.Count != 2)
            {
                return null; // a junction or dangling edge — not a simple loop
            }
        }

        int start = edges.First().Item1;
        var loop = new List<int> { start };
        int previous = -1;
        int current = start;
        while (true)
        {
            List<int> neighbours = adjacency[current];
            int next = neighbours[0] != previous ? neighbours[0] : neighbours[1];
            if (next == start)
            {
                break;
            }

            loop.Add(next);
            if (loop.Count > edges.Count)
            {
                return null;
            }

            previous = current;
            current = next;
        }

        return loop.Count == edges.Count ? loop : null; // must be one loop covering every edge
    }

    private static void Connect(Dictionary<int, List<int>> adjacency, int from, int to)
    {
        if (!adjacency.TryGetValue(from, out List<int>? list))
        {
            list = [];
            adjacency[from] = list;
        }

        list.Add(to);
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
        if (texture is not null &&
            level.Textures.TryGetValue(texture, out LevelTexture resolved) &&
            resolved.ScaleX > 0 && resolved.ScaleY > 0)
        {
            return ((float)resolved.ScaleX, (float)resolved.ScaleY);
        }

        return (DefaultScale, DefaultScale);
    }

    private sealed class Accumulator
    {
        private readonly Dictionary<string, List<RenderVertex>> _byTexture = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<RenderVertex> _untextured = [];

        public List<RenderVertex> Bucket(string? texture)
        {
            if (texture is null)
            {
                return _untextured;
            }

            if (!_byTexture.TryGetValue(texture, out List<RenderVertex>? list))
            {
                list = [];
                _byTexture[texture] = list;
            }

            return list;
        }

        public LevelMesh ToMesh()
        {
            var batches = new List<MeshBatch>(_byTexture.Count + 1);
            foreach ((string texture, List<RenderVertex> vertices) in _byTexture)
            {
                batches.Add(new MeshBatch(texture, vertices));
            }

            if (_untextured.Count > 0)
            {
                batches.Add(new MeshBatch(null, _untextured));
            }

            return new LevelMesh(batches);
        }
    }
}
