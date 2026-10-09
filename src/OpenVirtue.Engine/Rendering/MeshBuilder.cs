// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Map;

namespace OpenVirtue.Engine.Rendering;

/// <summary>
/// Builds renderer-ready geometry from a loaded <see cref="Level"/>: wall quads, plus
/// floor and ceiling surfaces for regions whose boundary forms a clean polygon loop
/// (traced from their walls and triangulated by <see cref="EarClipping"/>). Output is
/// GPU-agnostic, grouped by texture, and in render space (<see cref="RenderSpace"/>:
/// map y becomes −z). Texture coordinates still follow the map axes; no deliberate flip is
/// added because the original's wall texture orientation is unprobed.
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
                IsSkyTexture(level, wall.Texture))
            {
                continue;
            }

            Region? left = RegionAt(level, wall.LeftRegion);
            Region? right = RegionAt(level, wall.RightRegion);
            if (left is not null && right is not null && !ReferenceEquals(left, right))
            {
                float leftFloor = (float)left.FloorHeight;
                float rightFloor = (float)right.FloorHeight;
                EmitWallSegment(level, accumulator, wall, v1, v2,
                    Math.Min(leftFloor, rightFloor), Math.Max(leftFloor, rightFloor));

                float leftCeiling = (float)left.CeilHeight;
                float rightCeiling = (float)right.CeilHeight;
                EmitWallSegment(level, accumulator, wall, v1, v2,
                    Math.Min(leftCeiling, rightCeiling), Math.Max(leftCeiling, rightCeiling));
                continue;
            }

            Region? region = left ?? right;
            if (region is not null)
            {
                EmitWallSegment(level, accumulator, wall, v1, v2,
                    (float)region.FloorHeight, (float)region.CeilHeight);
            }
        }
    }

    private static void EmitWallSegment(
        Level level,
        Accumulator accumulator,
        Wall wall,
        Vertex v1,
        Vertex v2,
        float floor,
        float ceiling)
    {
        if (ceiling <= floor)
        {
            return;
        }

        float dx = v2.X - v1.X;
        float dz = v2.Y - v1.Y;
        float length = MathF.Sqrt((dx * dx) + (dz * dz));
        float height = ceiling - floor;
        TextureMapping mapping = TextureMappingFor(level, wall.Texture);
        float u0 = (float)wall.OffsetX * mapping.OffsetToU;
        float v0 = (float)wall.OffsetY * mapping.OffsetToV;
        float u1 = u0 + (length * mapping.WorldToU);
        float textureV1 = v0 + (height * mapping.WorldToV);
        float ambient = WallAmbient(level, wall);

        List<RenderVertex> target = accumulator.Bucket(
            wall.Texture,
            AnimationSourceFor(level, wall.Texture, wall),
            alphaTest: wall["TRANSPARENT"] > 0);
        // Render depth is −map y (RenderSpace, ADR-0009); u follows the vertex order unchanged.
        float z1 = RenderSpace.ZFromMapY(v1.Y);
        float z2 = RenderSpace.ZFromMapY(v2.Y);
        var bottomLeft = new RenderVertex(v1.X, floor + v1.Z, z1, u0, v0, ambient);
        var bottomRight = new RenderVertex(v2.X, floor + v2.Z, z2, u1, v0, ambient);
        var topRight = new RenderVertex(v2.X, ceiling + v2.Z, z2, u1, textureV1, ambient);
        var topLeft = new RenderVertex(v1.X, ceiling + v1.Z, z1, u0, textureV1, ambient);
        target.Add(bottomLeft);
        target.Add(bottomRight);
        target.Add(topRight);
        target.Add(bottomLeft);
        target.Add(topRight);
        target.Add(topLeft);
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
            EmitSurface(
                level,
                accumulator,
                loop,
                triangles,
                (float)region.FloorHeight,
                region.FloorTexture,
                region,
                (float)region.FloorOffsetX,
                (float)region.FloorOffsetY,
                flip: false);
            EmitSurface(
                level,
                accumulator,
                loop,
                triangles,
                (float)region.CeilHeight,
                region.CeilTexture,
                region,
                (float)region.CeilOffsetX,
                (float)region.CeilOffsetY,
                flip: true);
        }
    }

    private static void EmitSurface(
        Level level,
        Accumulator accumulator,
        List<int> loop,
        IReadOnlyList<int> triangles,
        float height,
        string? texture,
        Region region,
        float offsetX,
        float offsetY,
        bool flip)
    {
        if (IsSkyTexture(level, texture))
        {
            return;
        }

        TextureMapping mapping = TextureMappingFor(level, texture);
        List<RenderVertex> target = accumulator.Bucket(
            texture,
            AnimationSourceFor(level, texture, region));

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int a = loop[triangles[i]];
            int b = loop[triangles[flip ? i + 2 : i + 1]];
            int c = loop[triangles[flip ? i + 1 : i + 2]];
            target.Add(SurfaceVertex(level, a, height, mapping, offsetX, offsetY, (float)region.Ambient));
            target.Add(SurfaceVertex(level, b, height, mapping, offsetX, offsetY, (float)region.Ambient));
            target.Add(SurfaceVertex(level, c, height, mapping, offsetX, offsetY, (float)region.Ambient));
        }
    }

    private static RenderVertex SurfaceVertex(
        Level level, int vertexIndex, float height, TextureMapping mapping, float offsetX, float offsetY, float ambient)
    {
        Vertex vertex = level.Vertices[vertexIndex];
        return new RenderVertex(
            vertex.X,
            height + vertex.Z,
            RenderSpace.ZFromMapY(vertex.Y),
            (vertex.X * mapping.WorldToU) + (offsetX * mapping.OffsetToU),
            (vertex.Y * mapping.WorldToV) + (offsetY * mapping.OffsetToV),
            ambient);
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

    private static Region? RegionAt(Level level, int index) =>
        index >= 0 && index < level.Regions.Count ? level.Regions[index] : null;

    private static float WallAmbient(Level level, Wall wall)
    {
        float left = RegionAt(level, wall.LeftRegion) is { } leftRegion ? (float)leftRegion.Ambient : 0;
        float right = RegionAt(level, wall.RightRegion) is { } rightRegion ? (float)rightRegion.Ambient : 0;
        return Math.Max(left, right);
    }

    private static TextureMapping TextureMappingFor(Level level, string? texture)
    {
        if (texture is not null &&
            level.Textures.TryGetValue(texture, out LevelTexture resolved) &&
            resolved.ScaleX > 0 && resolved.ScaleY > 0)
        {
            if (resolved.Width > 0 && resolved.Height > 0)
            {
                return new TextureMapping(
                    (float)(resolved.ScaleX / resolved.Width),
                    (float)(resolved.ScaleY / resolved.Height),
                    1f / resolved.Width,
                    1f / resolved.Height);
            }

            return new TextureMapping(1f / (float)resolved.ScaleX, 1f / (float)resolved.ScaleY, 0, 0);
        }

        return new TextureMapping(1f / DefaultScale, 1f / DefaultScale, 0, 0);
    }

    private static AcknexObject? AnimationSourceFor(
        Level level,
        string? textureName,
        AcknexObject source)
    {
        return textureName is not null &&
               level.Textures.TryGetValue(textureName, out LevelTexture texture) &&
               (texture.Cycles > 1 || texture.IsOneShot)
            ? source
            : null;
    }

    private static bool IsSkyTexture(Level level, string? textureName) =>
        textureName is not null &&
        level.Textures.TryGetValue(textureName, out LevelTexture texture) &&
        texture.IsSky;

    private readonly record struct TextureMapping(float WorldToU, float WorldToV, float OffsetToU, float OffsetToV);

    private sealed class Accumulator
    {
        private readonly Dictionary<string, TextureBuckets> _byTexture = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<AcknexObject, Dictionary<string, TextureBuckets>> _animated = [];
        private readonly List<RenderVertex> _untextured = [];

        public List<RenderVertex> Bucket(
            string? texture,
            AcknexObject? animationSource = null,
            bool alphaTest = false)
        {
            if (texture is null)
            {
                return _untextured;
            }

            if (animationSource is not null)
            {
                if (!_animated.TryGetValue(animationSource, out Dictionary<string, TextureBuckets>? byTexture))
                {
                    byTexture = new Dictionary<string, TextureBuckets>(StringComparer.OrdinalIgnoreCase);
                    _animated[animationSource] = byTexture;
                }

                if (!byTexture.TryGetValue(texture, out TextureBuckets? animatedBuckets))
                {
                    animatedBuckets = new TextureBuckets();
                    byTexture[texture] = animatedBuckets;
                }

                return animatedBuckets.For(alphaTest);
            }

            if (!_byTexture.TryGetValue(texture, out TextureBuckets? buckets))
            {
                buckets = new TextureBuckets();
                _byTexture[texture] = buckets;
            }

            return buckets.For(alphaTest);
        }

        public LevelMesh ToMesh()
        {
            var batches = new List<MeshBatch>(_byTexture.Count + _animated.Count + 1);
            foreach ((string texture, TextureBuckets buckets) in _byTexture)
            {
                buckets.AddBatches(batches, texture, animationSource: null);
            }

            foreach ((AcknexObject source, Dictionary<string, TextureBuckets> byTexture) in _animated)
            {
                foreach ((string texture, TextureBuckets buckets) in byTexture)
                {
                    buckets.AddBatches(batches, texture, source);
                }
            }

            if (_untextured.Count > 0)
            {
                batches.Add(new MeshBatch(null, _untextured));
            }

            return new LevelMesh(batches);
        }

        private sealed class TextureBuckets
        {
            private readonly List<RenderVertex> _opaque = [];
            private readonly List<RenderVertex> _alphaTested = [];

            public List<RenderVertex> For(bool alphaTest) => alphaTest ? _alphaTested : _opaque;

            public void AddBatches(List<MeshBatch> batches, string texture, AcknexObject? animationSource)
            {
                if (_opaque.Count > 0)
                {
                    batches.Add(new MeshBatch(texture, _opaque, animationSource));
                }

                if (_alphaTested.Count > 0)
                {
                    batches.Add(new MeshBatch(texture, _alphaTested, animationSource, alphaTest: true));
                }
            }
        }
    }
}
