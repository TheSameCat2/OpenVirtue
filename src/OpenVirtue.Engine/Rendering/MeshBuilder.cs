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
/// map y becomes −z). Floor/ceiling texture coordinates follow the map axes; a wall's u runs
/// from its second vertex toward its first, as measured in OV-20261009-shoot-mapping for a
/// viewer in the wall's left region.
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
            if ((left?.BelowRegion ?? -1) >= 0 || (right?.BelowRegion ?? -1) >= 0)
            {
                AddStackedWall(level, accumulator, wall, v1, v2);
                continue;
            }

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

    /// <summary>
    /// A wall beside a <c>BELOW</c> stack (manual p.59): each side's column is open over its
    /// regions' spans and solid in between (the slab of a bridge or balcony). The wall covers what is
    /// open on one side and solid on the other, so it shows a slab's edge to a neighbour, closes a
    /// storey that the other column does not continue, and leaves shared openings as portals. A wall
    /// with a column on one side only, or the same column on both, covers each of its storeys.
    /// </summary>
    private static void AddStackedWall(Level level, Accumulator accumulator, Wall wall, Vertex v1, Vertex v2)
    {
        List<(float Floor, float Ceiling)> left = OpenSpans(level, wall.LeftRegion);
        List<(float Floor, float Ceiling)> right = OpenSpans(level, wall.RightRegion);
        if (left.Count == 0 || right.Count == 0 || wall.LeftRegion == wall.RightRegion)
        {
            foreach ((float floor, float ceiling) in left.Count > 0 ? left : right)
            {
                EmitWallSegment(level, accumulator, wall, v1, v2, floor, ceiling);
            }

            return;
        }

        foreach ((float floor, float ceiling) in Uncovered(left, right).Concat(Uncovered(right, left)))
        {
            EmitWallSegment(level, accumulator, wall, v1, v2, floor, ceiling);
        }
    }

    /// <summary>The open spans of the stack at a map region, top first.</summary>
    private static List<(float Floor, float Ceiling)> OpenSpans(Level level, int regionIndex)
    {
        var spans = new List<(float, float)>();
        if (RegionAt(level, regionIndex) is null)
        {
            return spans;
        }

        foreach (int member in LevelTopology.Stack(level, regionIndex))
        {
            Region region = level.Regions[member];
            if (region.CeilHeight > region.FloorHeight)
            {
                spans.Add(((float)region.FloorHeight, (float)region.CeilHeight));
            }
        }

        return spans;
    }

    /// <summary>The parts of <paramref name="spans"/> that none of <paramref name="cover"/> overlaps.</summary>
    private static IEnumerable<(float Floor, float Ceiling)> Uncovered(
        List<(float Floor, float Ceiling)> spans,
        List<(float Floor, float Ceiling)> cover)
    {
        foreach ((float floor, float ceiling) in spans)
        {
            var pieces = new List<(float Floor, float Ceiling)> { (floor, ceiling) };
            foreach ((float coverFloor, float coverCeiling) in cover)
            {
                var next = new List<(float Floor, float Ceiling)>();
                foreach ((float f, float c) in pieces)
                {
                    if (coverCeiling <= f || coverFloor >= c)
                    {
                        next.Add((f, c));
                        continue;
                    }

                    if (coverFloor > f)
                    {
                        next.Add((f, coverFloor));
                    }

                    if (coverCeiling < c)
                    {
                        next.Add((coverCeiling, c));
                    }
                }

                pieces = next;
            }

            foreach ((float Floor, float Ceiling) piece in pieces)
            {
                yield return piece;
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
        // OV-20261009-shoot-mapping: viewed from the wall's left region (the room side of the
        // retail convention), the bitmap's left half sits at the SECOND vertex, so u starts at
        // v2 and grows toward v1; the right-region view and the OFFSET_X sense are unprobed.
        float uAtV2 = (float)wall.OffsetX * mapping.OffsetToU;
        float v0 = (float)wall.OffsetY * mapping.OffsetToV;
        float uAtV1 = uAtV2 + (length * mapping.WorldToU);
        float textureV1 = v0 + (height * mapping.WorldToV);
        float ambient = WallAmbient(level, wall);

        List<RenderVertex> target = accumulator.Bucket(
            wall.Texture,
            AnimationSourceFor(level, wall.Texture, wall),
            alphaTest: wall["TRANSPARENT"] > 0);
        // Render depth is −map y (RenderSpace, ADR-0009); u follows the vertex order unchanged.
        float z1 = RenderSpace.ZFromMapY(v1.Y);
        float z2 = RenderSpace.ZFromMapY(v2.Y);
        var bottomLeft = new RenderVertex(v1.X, floor + v1.Z, z1, uAtV1, v0, ambient);
        var bottomRight = new RenderVertex(v2.X, floor + v2.Z, z2, uAtV2, v0, ambient);
        var topRight = new RenderVertex(v2.X, ceiling + v2.Z, z2, uAtV2, textureV1, ambient);
        var topLeft = new RenderVertex(v1.X, ceiling + v1.Z, z1, uAtV1, textureV1, ambient);
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
            List<int>? loop = LevelTopology.TraceRegionLoop(level, regionIndex);
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
