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
                // Each end of the wall meets each region's surfaces at that vertex's height, if the
                // region's slope flags make it follow them (OV-20261029-slope).
                EmitWallSegment(level, accumulator, wall, v1, v2,
                    Math.Min(Floor(left, v1), Floor(right, v1)), Math.Max(Floor(left, v1), Floor(right, v1)),
                    Math.Min(Floor(left, v2), Floor(right, v2)), Math.Max(Floor(left, v2), Floor(right, v2)));
                EmitWallSegment(level, accumulator, wall, v1, v2,
                    Math.Min(Ceiling(left, v1), Ceiling(right, v1)), Math.Max(Ceiling(left, v1), Ceiling(right, v1)),
                    Math.Min(Ceiling(left, v2), Ceiling(right, v2)), Math.Max(Ceiling(left, v2), Ceiling(right, v2)));
                continue;
            }

            Region? region = left ?? right;
            if (region is not null)
            {
                EmitWallSegment(level, accumulator, wall, v1, v2,
                    Floor(region, v1), Ceiling(region, v1), Floor(region, v2), Ceiling(region, v2));
            }
        }
    }

    /// <summary>
    /// A wall beside a <c>BELOW</c> stack (manual p.59): each side's column is open over its
    /// regions' spans and solid in between (the slab of a bridge or balcony). The wall covers what is
    /// open on one side and solid on the other, so it shows a slab's edge to a neighbour, closes a
    /// storey that the other column does not continue, and leaves shared openings as portals. A wall
    /// with a column on one side only, or the same column on both, covers each of its storeys. A
    /// piece facing storey k shows the texture's sides 4k… when it has that many (manual p.38: "the
    /// first four sides are assigned to the uppermost region, the BELOW region has the next four").
    /// </summary>
    private static void AddStackedWall(Level level, Accumulator accumulator, Wall wall, Vertex v1, Vertex v2)
    {
        List<(float Floor, float Ceiling)> left = OpenSpans(level, wall.LeftRegion);
        List<(float Floor, float Ceiling)> right = OpenSpans(level, wall.RightRegion);
        if (left.Count == 0 || right.Count == 0 || wall.LeftRegion == wall.RightRegion)
        {
            List<(float Floor, float Ceiling)> spans = left.Count > 0 ? left : right;
            for (int storey = 0; storey < spans.Count; storey++)
            {
                EmitWallSegment(level, accumulator, wall, v1, v2, spans[storey].Floor, spans[storey].Ceiling, storey);
            }

            return;
        }

        foreach ((float floor, float ceiling, int storey) in Uncovered(left, right).Concat(Uncovered(right, left)))
        {
            EmitWallSegment(level, accumulator, wall, v1, v2, floor, ceiling, storey);
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
    private static IEnumerable<(float Floor, float Ceiling, int Storey)> Uncovered(
        List<(float Floor, float Ceiling)> spans,
        List<(float Floor, float Ceiling)> cover)
    {
        for (int storey = 0; storey < spans.Count; storey++)
        {
            (float floor, float ceiling) = spans[storey];
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

            foreach ((float pieceFloor, float pieceCeiling) in pieces)
            {
                yield return (pieceFloor, pieceCeiling, storey);
            }
        }
    }

    /// <summary>The render texture of a lower storey's own sides (4·storey on), or null to use the wall's texture.</summary>
    private static string? StoreySide(Level level, string? texture, int storey) =>
        storey > 0 &&
        texture is not null &&
        level.Textures.TryGetValue(texture, out LevelTexture resolved) &&
        resolved.Sides > 4 * storey
            ? TextureAnimation.ResolveRenderTexture(resolved, 1, 4 * storey)
            : null;

    private static float Floor(Region region, Vertex vertex) =>
        (float)(region.FloorHeight + (LevelTopology.SlopeSign(region, ceiling: false) * vertex.Z));

    private static float Ceiling(Region region, Vertex vertex) =>
        (float)(region.CeilHeight + (LevelTopology.SlopeSign(region, ceiling: true) * vertex.Z));

    /// <summary>A flat-topped wall piece between two heights (a <c>BELOW</c> stack's spans).</summary>
    private static void EmitWallSegment(
        Level level,
        Accumulator accumulator,
        Wall wall,
        Vertex v1,
        Vertex v2,
        float floor,
        float ceiling,
        int storey) =>
        EmitWallSegment(level, accumulator, wall, v1, v2, floor, ceiling, floor, ceiling, storey);

    private static void EmitWallSegment(
        Level level,
        Accumulator accumulator,
        Wall wall,
        Vertex v1,
        Vertex v2,
        float floor1,
        float ceiling1,
        float floor2,
        float ceiling2,
        int storey = 0)
    {
        if (ceiling1 <= floor1 && ceiling2 <= floor2)
        {
            return;
        }

        float floor = Math.Min(floor1, floor2);
        float ceiling = Math.Max(ceiling1, ceiling2);

        float dx = v2.X - v1.X;
        float dz = v2.Y - v1.Y;
        float length = MathF.Sqrt((dx * dx) + (dz * dz));
        TextureMapping mapping = TextureMappingFor(level, wall.Texture);
        // OV-20261009-shoot-mapping: viewed from the wall's left region (the room side of the
        // retail convention), the bitmap's left half sits at the SECOND vertex, so u starts at
        // v2 and grows toward v1. OV-20261030-wall-sides: from the other face it is the same, the
        // bitmap fixed to the wall and so seen mirrored. The OFFSET_X sense is unprobed.
        float uAtV2 = (float)wall.OffsetX * mapping.OffsetToU;
        float v0 = (float)wall.OffsetY * mapping.OffsetToV;
        float uAtV1 = uAtV2 + (length * mapping.WorldToU);
        float ambient = WallAmbient(level, wall);

        List<RenderVertex> target = StoreySide(level, wall.Texture, storey) is { } sideTexture
            ? accumulator.Bucket(sideTexture, animationSource: null, alphaTest: wall["TRANSPARENT"] > 0)
            : accumulator.Bucket(wall.Texture, AnimationSourceFor(level, wall.Texture, wall), alphaTest: wall["TRANSPARENT"] > 0);
        // Render depth is −map y (RenderSpace, ADR-0009); u follows the vertex order unchanged.
        float z1 = RenderSpace.ZFromMapY(v1.Y);
        float z2 = RenderSpace.ZFromMapY(v2.Y);
        // v runs from the piece's lowest point, so a sloped edge cuts the texture rather than stretching it.
        float worldToV = mapping.WorldToV;
        var bottomLeft = new RenderVertex(v1.X, floor1, z1, uAtV1, v0 + ((floor1 - floor) * worldToV), ambient);
        var bottomRight = new RenderVertex(v2.X, floor2, z2, uAtV2, v0 + ((floor2 - floor) * worldToV), ambient);
        var topRight = new RenderVertex(v2.X, ceiling2, z2, uAtV2, v0 + ((ceiling2 - floor) * worldToV), ambient);
        var topLeft = new RenderVertex(v1.X, ceiling1, z1, uAtV1, v0 + ((ceiling1 - floor) * worldToV), ambient);
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
                (float)LevelTopology.SlopeSign(region, ceiling: false),
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
                (float)LevelTopology.SlopeSign(region, ceiling: true),
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
        float slopeSign,
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
            target.Add(SurfaceVertex(level, a, height, slopeSign, mapping, offsetX, offsetY, (float)region.Ambient));
            target.Add(SurfaceVertex(level, b, height, slopeSign, mapping, offsetX, offsetY, (float)region.Ambient));
            target.Add(SurfaceVertex(level, c, height, slopeSign, mapping, offsetX, offsetY, (float)region.Ambient));
        }
    }

    private static RenderVertex SurfaceVertex(
        Level level, int vertexIndex, float height, float slopeSign, TextureMapping mapping, float offsetX, float offsetY, float ambient)
    {
        // A surface follows its vertices' heights only with a slope flag (OV-20261029-slope).
        Vertex vertex = level.Vertices[vertexIndex];
        return new RenderVertex(
            vertex.X,
            height + (slopeSign * vertex.Z),
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

    /// <summary>
    /// A wall's region <c>AMBIENT</c>: its left region's, the room side of the retail convention. A
    /// room with <c>AMBIENT</c> −0.25 darkened its walls to a solid region by 8 levels
    /// (OV-20261029-shading round 3), so the brighter side does not win. One mesh serves both sides;
    /// the view from the right region is unmeasured.
    /// </summary>
    private static float WallAmbient(Level level, Wall wall) =>
        (RegionAt(level, wall.LeftRegion) is { } leftRegion ? (float)leftRegion.Ambient
        : RegionAt(level, wall.RightRegion) is { } rightRegion ? (float)rightRegion.Ambient
        : 0) + (float)GeniusLight(level, wall);

    /// <summary>
    /// The light a region's <c>GENIUS</c> object gives a wall of it: a thing or actor whose texture has
    /// <c>AMBIENT</c> &gt; 0 brightens the face it stands in front of by 16 shade levels per 1.0 of the
    /// wall texture's <c>ALBEDO</c> — half what an <c>AMBIENT</c> gives (32 a 1.0, <see cref="PaletteShading"/>) —
    /// as a facing wall gains in OV-20261029-shading rounds 4 and 5. A wall seen at a grazing angle
    /// gains less there; that is not modelled.
    /// </summary>
    private static double GeniusLight(Level level, Wall wall)
    {
        if (wall.Texture is not { } textureName ||
            !level.Textures.TryGetValue(textureName, out LevelTexture texture) ||
            texture.Albedo <= 0 ||
            RegionAt(level, wall.LeftRegion) is not { GeniusName: { } geniusName } ||
            level.Things.Cast<MapEntity>().Concat(level.Actors).FirstOrDefault(e => e.Name.Equals(geniusName, StringComparison.OrdinalIgnoreCase)) is not { } lamp ||
            lamp.Texture is not { } lampTexture ||
            !level.Textures.TryGetValue(lampTexture, out LevelTexture lampLevelTexture) ||
            lampLevelTexture.Ambient <= 0 ||
            (uint)wall.Vertex1 >= (uint)level.Vertices.Count ||
            (uint)wall.Vertex2 >= (uint)level.Vertices.Count)
        {
            return 0;
        }

        Vertex a = level.Vertices[wall.Vertex1];
        Vertex b = level.Vertices[wall.Vertex2];
        double side = ((b.X - a.X) * (lamp.Y - a.Y)) - ((b.Y - a.Y) * (lamp.X - a.X));
        return side > 0 ? texture.Albedo * 0.5 : 0;
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
        // A wall with a sided texture also needs its own batch: the side it shows depends on the
        // viewpoint (OV-20261030-wall-sides).
        return textureName is not null &&
               level.Textures.TryGetValue(textureName, out LevelTexture texture) &&
               (texture.Cycles > 1 || texture.IsOneShot || (source is Wall && texture.Sides >= 2))
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
