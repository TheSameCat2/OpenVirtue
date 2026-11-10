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

    /// <summary>The texture a floor or ceiling draws when its bitmap is no size a surface can show.</summary>
    public const string BlankSurface = "#blank-surface";

    /// <summary>
    /// Builds walls and floor/ceiling surfaces. <paramref name="blankSurfaces"/> names the textures that
    /// draw as palette entry 0 on a surface (<see cref="TextureLoader.BlankSurfaceTextures"/>).
    /// </summary>
    public static LevelMesh Build(Level level, IReadOnlySet<string>? blankSurfaces = null)
    {
        ArgumentNullException.ThrowIfNull(level);
        var accumulator = new Accumulator();
        IReadOnlySet<string> rowMasked = TextureLoader.RowMaskedWallTextures(level);
        AddWalls(level, accumulator, rowMasked);
        AddSurfaces(level, accumulator, rowMasked, blankSurfaces);
        return accumulator.ToMesh();
    }

    /// <summary>Builds wall geometry only (a vertical textured quad per wall).</summary>
    public static LevelMesh BuildWalls(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        var accumulator = new Accumulator();
        AddWalls(level, accumulator, TextureLoader.RowMaskedWallTextures(level));
        return accumulator.ToMesh();
    }

    /// <summary>
    /// The walls as a sprite and a clip fill see them (OV-20261106-clip-view): a step's lower piece
    /// reaching down without end, its upper piece reaching up without end, a wall of one region both
    /// ways, depth only. So a sprite behind a wall is hidden even where it sticks out above the
    /// wall's top. A see-through wall hides nothing, and a <c>BELOW</c> stack's walls keep to what
    /// they draw.
    /// </summary>
    public static RenderVertex[] BuildOccluders(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        var vertices = new List<RenderVertex>();
        foreach (Wall wall in level.Walls)
        {
            if (!TryVertex(level, wall.Vertex1, out Vertex v1) || !TryVertex(level, wall.Vertex2, out Vertex v2))
            {
                continue;
            }

            Region? left = RegionAt(level, wall.LeftRegion);
            Region? right = RegionAt(level, wall.RightRegion);
            if ((left?.BelowRegion ?? -1) >= 0 || (right?.BelowRegion ?? -1) >= 0 || wall["TRANSPARENT"] > 0)
            {
                continue;
            }

            List<WallPiece> pieces = WallPieces(level, wall, v1, v2, left, right);
            if (pieces.Count == 1)
            {
                if (wall["INVISIBLE"] <= 0)
                {
                    AddOccluder(vertices, v1, v2, -ViewClip.Reach, -ViewClip.Reach, ViewClip.Reach, ViewClip.Reach);
                }

                continue;
            }

            if (pieces.Count == 2 && left is not null && right is not null)
            {
                // No opening between the two regions at either end (retail's closed "border"
                // regions, floor on ceiling): the column stops here whatever piece it shows.
                if (Math.Min(Ceiling(left, v1), Ceiling(right, v1)) <= Math.Max(Floor(left, v1), Floor(right, v1)) &&
                    Math.Min(Ceiling(left, v2), Ceiling(right, v2)) <= Math.Max(Floor(left, v2), Floor(right, v2)))
                {
                    AddOccluder(vertices, v1, v2, -ViewClip.Reach, -ViewClip.Reach, ViewClip.Reach, ViewClip.Reach);
                    continue;
                }

                WallPiece lower = pieces[0];
                WallPiece upper = pieces[1];
                if (lower.Ceiling1 > lower.Floor1 || lower.Ceiling2 > lower.Floor2)
                {
                    AddOccluder(vertices, v1, v2, -ViewClip.Reach, -ViewClip.Reach, lower.Ceiling1, lower.Ceiling2);
                }

                if (upper.Ceiling1 > upper.Floor1 || upper.Ceiling2 > upper.Floor2)
                {
                    AddOccluder(vertices, v1, v2, upper.Floor1, upper.Floor2, ViewClip.Reach, ViewClip.Reach);
                }
            }
        }

        return vertices.ToArray();
    }

    private static void AddOccluder(List<RenderVertex> vertices, Vertex v1, Vertex v2, float low1, float low2, float high1, float high2)
    {
        // A hair longer than the wall, so no fill or far surface shows through the crack where two
        // walls meet at a vertex.
        float dx = v2.X - v1.X;
        float dy = v2.Y - v1.Y;
        float length = MathF.Max(MathF.Sqrt((dx * dx) + (dy * dy)), 0.001f);
        float along = 0.01f / length;
        float x1 = v1.X - (dx * along);
        float x2 = v2.X + (dx * along);
        float z1 = RenderSpace.ZFromMapY(v1.Y - (dy * along));
        float z2 = RenderSpace.ZFromMapY(v2.Y + (dy * along));
        var a = new RenderVertex(x1, low1, z1, 0, 0);
        var b = new RenderVertex(x2, low2, z2, 0, 0);
        var c = new RenderVertex(x2, high2, z2, 0, 0);
        var d = new RenderVertex(x1, high1, z1, 0, 0);
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(a);
        vertices.Add(c);
        vertices.Add(d);
    }

    private static void AddWalls(Level level, Accumulator accumulator, IReadOnlySet<string> rowMasked)
    {
        foreach (Wall wall in level.Walls)
        {
            if (!TryVertex(level, wall.Vertex1, out Vertex v1) ||
                !TryVertex(level, wall.Vertex2, out Vertex v2) ||
                IsSkyTexture(level, wall.Texture))
            {
                continue;
            }

            // OV-20261106-clip-view p-top: an INVISIBLE wall shows no face, a step's piece included.
            if (wall["INVISIBLE"] > 0)
            {
                continue;
            }

            Region? left = RegionAt(level, wall.LeftRegion);
            Region? right = RegionAt(level, wall.RightRegion);
            if ((left?.BelowRegion ?? -1) >= 0 || (right?.BelowRegion ?? -1) >= 0)
            {
                AddStackedWall(level, accumulator, wall, v1, v2, rowMasked);
                continue;
            }

            List<WallPiece> pieces = WallPieces(level, wall, v1, v2, left, right);
            foreach (WallPiece piece in pieces)
            {
                EmitWallSegment(level, accumulator, wall, v1, v2, piece, rowMasked);
            }

            // OV-20261105-wall-pieces: a wall's ATTACH is drawn on every wall, cut to the pieces it
            // shows (before this only a one-region wall called for it, so nearly every retail attach
            // was missing). A sky wall's attach is not drawn, as before: the wall itself is skipped
            // above and the attach goes with it.
            EmitWallAttachment(level, accumulator, wall, v1, v2, pieces);
        }
    }

    /// <summary>
    /// The pieces a wall shows. Between two regions it shows the lower piece, between the two floors,
    /// and the upper piece, between the two ceilings; a wall of one region shows the whole floor to
    /// ceiling. Each piece's ends follow each region's surfaces at that vertex's height when the
    /// region's slope flags make it (OV-20261029-slope), and each carries the region-facing
    /// <c>AMBIENT</c> of OV-20261105-wall-light.
    /// </summary>
    /// <remarks>
    /// OV-20261105-wall-pieces round 6: a piece is limited by the region it faces. The lower piece
    /// faces the lower-floor region F and ends at <c>min(higher floor, F's ceiling)</c>, so a room
    /// 0…16 against a solid 41…41 shows 0…16 and no upper piece at all; the upper piece faces the
    /// higher-ceiling region C and starts at <c>max(lower ceiling, C's floor)</c>, so a room 0…16
    /// against a pad 19…24 shows 0…16 and 19…24. The limits are per vertex, on F's and C's own
    /// surfaces at that vertex (measured flat only).
    /// </remarks>
    private static List<WallPiece> WallPieces(Level level, Wall wall, Vertex v1, Vertex v2, Region? left, Region? right)
    {
        var pieces = new List<WallPiece>(2);
        if (left is not null && right is not null && !ReferenceEquals(left, right))
        {
            // OV-20261105-wall-light: the lower piece faces the region with the lower floor and the
            // upper piece the region with the higher ceiling; a tie keeps the left region's.
            Region lowerFloor = LowerFloorRegion(left, right, v1, v2);
            Region higherCeiling = HigherCeilingRegion(left, right, v1, v2);
            pieces.Add(new WallPiece(
                Math.Min(Floor(left, v1), Floor(right, v1)),
                Math.Min(Math.Max(Floor(left, v1), Floor(right, v1)), Ceiling(lowerFloor, v1)),
                Math.Min(Floor(left, v2), Floor(right, v2)),
                Math.Min(Math.Max(Floor(left, v2), Floor(right, v2)), Ceiling(lowerFloor, v2)),
                WallAmbient(level, wall, lowerFloor)));
            pieces.Add(new WallPiece(
                Math.Max(Math.Min(Ceiling(left, v1), Ceiling(right, v1)), Floor(higherCeiling, v1)),
                Math.Max(Ceiling(left, v1), Ceiling(right, v1)),
                Math.Max(Math.Min(Ceiling(left, v2), Ceiling(right, v2)), Floor(higherCeiling, v2)),
                Math.Max(Ceiling(left, v2), Ceiling(right, v2)),
                WallAmbient(level, wall, higherCeiling)));
            return pieces;
        }

        if ((left ?? right) is { } region)
        {
            pieces.Add(new WallPiece(
                Floor(region, v1),
                Ceiling(region, v1),
                Floor(region, v2),
                Ceiling(region, v2),
                WallAmbient(level, wall)));
        }

        return pieces;
    }

    /// <summary>
    /// OV-20261105-wall-light: the region whose floor is lower at the wall's ends, the side a wall's
    /// lower piece faces (a room and a pad stepped up to, measured: the room's). A tie is the left
    /// region's.
    /// </summary>
    private static Region LowerFloorRegion(Region left, Region right, Vertex v1, Vertex v2) =>
        Math.Min(Floor(left, v1), Floor(left, v2)) <= Math.Min(Floor(right, v1), Floor(right, v2)) ? left : right;

    /// <summary>
    /// OV-20261105-wall-light: the region whose ceiling is higher at the wall's ends, the side a wall's
    /// upper piece faces (a dropped pad ceiling, measured: the room's). A tie is the left region's.
    /// </summary>
    private static Region HigherCeilingRegion(Region left, Region right, Vertex v1, Vertex v2) =>
        Math.Max(Ceiling(left, v1), Ceiling(left, v2)) >= Math.Max(Ceiling(right, v1), Ceiling(right, v2)) ? left : right;

    /// <summary>
    /// The wall's <c>ATTACH</c> texture (OV-20261104-attach, OV-20261105-wall-pieces): a quad of the
    /// attach's own size (pixels over its <c>SCALE</c>) whose upper left corner lies <c>POS_X</c> pixels
    /// of the wall texture (at the wall texture's scale) along the wall from its u origin at the second
    /// vertex, and <c>POS_Y</c> such pixels below the top of a repeat measured from a fixed anchor: 0 on
    /// a free wall whatever its region's floor, or the edge a <c>PORTCULLIS</c> position names. The
    /// repeat is the wall bitmap's real height over <c>SCALE_Y</c>, not the masked row period. It is
    /// drawn once for each piece the wall shows, cut to that piece, and lit as it. The wall is seen
    /// from both sides, so each quad is laid a hair off the wall on each.
    /// </summary>
    private static void EmitWallAttachment(
        Level level,
        Accumulator accumulator,
        Wall wall,
        Vertex v1,
        Vertex v2,
        IReadOnlyList<WallPiece> pieces)
    {
        if (wall.AttachTexture is null ||
            !level.Textures.TryGetValue(wall.AttachTexture, out LevelTexture attach) ||
            attach.Width <= 0 ||
            attach.Height <= 0)
        {
            return;
        }

        List<WallPiece> shown = [.. pieces.Where(static piece => !piece.IsEmpty)];
        if (shown.Count == 0)
        {
            return;
        }

        float dx = v1.X - v2.X;
        float dz = v1.Y - v2.Y;
        float length = MathF.Sqrt((dx * dx) + (dz * dz));
        TextureMapping mapping = TextureMappingFor(level, wall.Texture);
        if (length <= 0 || mapping.WorldToV <= 0 || mapping.WorldToU <= 0)
        {
            return;
        }

        double wallScaleX = wall.Texture is not null && level.Textures.TryGetValue(wall.Texture, out LevelTexture wallTexture) && wallTexture.ScaleX > 0
            ? wallTexture.ScaleX
            : DefaultScale;
        double wallScaleY = wall.Texture is not null && level.Textures.TryGetValue(wall.Texture, out wallTexture) && wallTexture.ScaleY > 0
            ? wallTexture.ScaleY
            : DefaultScale;
        float width = (float)(attach.Width / (attach.ScaleX > 0 ? attach.ScaleX : DefaultScale));
        float height = (float)(attach.Height / (attach.ScaleY > 0 ? attach.ScaleY : DefaultScale));
        // The attach hangs on the wall texture's own repeat, its real bitmap height over SCALE_Y: 60
        // rows at 4 px a step repeat every 15 steps, not the 16 the masked row period would give.
        float repeatHeight = 1f / mapping.WorldToV;
        float anchor = AttachAnchor(wall, shown);
        // OV-20261105-wall-pieces: the wall's offsets slide the attach as they slide its texture,
        // OFFSET_X back along u and OFFSET_Y further down.
        float s0 = (float)((attach.PosX - wall.OffsetX) / wallScaleX);
        float s1 = s0 + width;
        float top = anchor + repeatHeight - (float)((attach.PosY + wall.OffsetY) / wallScaleY);
        float bottom = top - height;

        List<RenderVertex> target = accumulator.Bucket(
            wall.AttachTexture, AnimationSourceFor(level, wall.AttachTexture, wall), alphaTest: true);
        float dirX = dx / length;
        float dirY = dz / length;
        float wallZ1 = RenderSpace.ZFromMapY(v1.Y);
        float wallZ2 = RenderSpace.ZFromMapY(v2.Y);
        const float lift = 0.02f;
        // OV-20261105-wall-pieces: one quad a piece, each cut to the piece's own span and lit as it —
        // the pieces cannot overlap now that each is limited by the region it faces. On a sloped piece
        // the cut uses the piece's bounding heights, `Low` and `High` (unmeasured: every carrier's
        // piece ends were flat).
        foreach (WallPiece piece in shown)
        {
            float cs0 = Math.Max(s0, 0);
            float cs1 = Math.Min(s1, length);
            float ct = Math.Min(top, piece.High);
            float cb = Math.Max(bottom, piece.Low);
            if (cs1 <= cs0 || ct <= cb)
            {
                continue;
            }

            float ambient = piece.Ambient;
            float u0 = (cs0 - s0) / width;
            float u1 = (cs1 - s0) / width;
            float vTop = (top - ct) / height;
            float vBottom = (top - cb) / height;
            foreach (float side in (ReadOnlySpan<float>)[lift, -lift])
            {
                float nx = -dirY * side;
                float ny = dirX * side;
                (float X, float Z) At(float s) => (v2.X + (dirX * s) + nx, RenderSpace.ZFromMapY(v2.Y + (dirY * s) + ny));
                (float x0, float z0) = At(cs0);
                (float x1, float z1) = At(cs1);
                // An attach is shaded as its wall, ends and all: unmeasured, but the original's attach
                // moves with the wall's texture, so the same stepping is the natural choice.
                var bottomLeft = new RenderVertex(x0, cb, z0, u0, vBottom, ambient, v1.X, wallZ1, v2.X, wallZ2);
                var bottomRight = new RenderVertex(x1, cb, z1, u1, vBottom, ambient, v1.X, wallZ1, v2.X, wallZ2);
                var topRight = new RenderVertex(x1, ct, z1, u1, vTop, ambient, v1.X, wallZ1, v2.X, wallZ2);
                var topLeft = new RenderVertex(x0, ct, z0, u0, vTop, ambient, v1.X, wallZ1, v2.X, wallZ2);
                target.Add(bottomLeft);
                target.Add(bottomRight);
                target.Add(topRight);
                target.Add(bottomLeft);
                target.Add(topRight);
                target.Add(topLeft);
            }
        }
    }

    /// <summary>
    /// OV-20261105-wall-pieces: the height an attach's repeats are measured from. A free wall's is 0,
    /// a fixed height rather than its region's floor (measured: a free wall in a region with floor 9
    /// and a repeat of 8 hides an attach at POS_Y 8 and shows one at POS_Y −24 at z 10–11). A
    /// <c>PORTCULLIS</c> wall's is the edge its <c>POSITION</c> names, taken from the pieces the wall
    /// shows and so from their limited edges (round 6: a room 0…16 against a solid 41…41 anchors
    /// POSITION 1 at 16, where the attach's top is 12 at POS_Y 80 and −13 at POS_Y 180): the lowest
    /// lower edge, position 0, or the highest upper edge, position 1. "Upper" is only measured for
    /// <c>POSITION</c> 0 and 1, and the manual's <c>FENCE</c> (which implies <c>PORTCULLIS</c>, p.54)
    /// is neither implemented nor used by retail.
    /// </summary>
    private static float AttachAnchor(Wall wall, IReadOnlyList<WallPiece> pieces) =>
        wall["PORTCULLIS"] <= 0
            ? 0f
            : wall["POSITION"] > 0
                ? pieces.Max(static piece => piece.High)
                : pieces.Min(static piece => piece.Low);

    /// <summary>
    /// A wall beside a <c>BELOW</c> stack (manual p.59): each side's column is open over its
    /// regions' spans and solid in between (the slab of a bridge or balcony). The wall covers what is
    /// open on one side and solid on the other, so it shows a slab's edge to a neighbour, closes a
    /// storey that the other column does not continue, and leaves shared openings as portals. A wall
    /// with a column on one side only, or the same column on both, covers each of its storeys. A
    /// piece facing storey k shows the texture's sides 4k… when it has that many (manual p.38: "the
    /// first four sides are assigned to the uppermost region, the BELOW region has the next four").
    /// </summary>
    private static void AddStackedWall(
        Level level, Accumulator accumulator, Wall wall, Vertex v1, Vertex v2, IReadOnlySet<string> rowMasked)
    {
        List<(float Floor, float Ceiling)> left = OpenSpans(level, wall.LeftRegion);
        List<(float Floor, float Ceiling)> right = OpenSpans(level, wall.RightRegion);
        var pieces = new List<WallPiece>();
        if (left.Count == 0 || right.Count == 0 || wall.LeftRegion == wall.RightRegion)
        {
            List<(float Floor, float Ceiling)> spans = left.Count > 0 ? left : right;
            for (int storey = 0; storey < spans.Count; storey++)
            {
                pieces.Add(StackedPiece(level, wall, spans[storey].Floor, spans[storey].Ceiling));
                EmitWallSegment(
                    level, accumulator, wall, v1, v2, spans[storey].Floor, spans[storey].Ceiling, rowMasked, storey);
            }
        }
        else
        {
            foreach ((float floor, float ceiling, int storey) in Uncovered(left, right).Concat(Uncovered(right, left)))
            {
                pieces.Add(StackedPiece(level, wall, floor, ceiling));
                EmitWallSegment(level, accumulator, wall, v1, v2, floor, ceiling, rowMasked, storey);
            }
        }

        // OV-20261105-wall-pieces: the attach cuts to each span as to a piece. Unprobed on a stack:
        // none of the attach measurements stood beside a BELOW region.
        EmitWallAttachment(level, accumulator, wall, v1, v2, pieces);
    }

    /// <summary>
    /// A <c>BELOW</c> stack's span as a piece: flat, and lit as the wall's left region is (unmeasured
    /// beside a stack).
    /// </summary>
    private static WallPiece StackedPiece(Level level, Wall wall, float floor, float ceiling) =>
        new(floor, ceiling, floor, ceiling, WallAmbient(level, wall));

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

    /// <summary>A wall piece: its lower and upper edge at each vertex, which differ on a sloped edge.</summary>
    private readonly record struct WallPiece(float Floor1, float Ceiling1, float Floor2, float Ceiling2, float Ambient)
    {
        /// <summary>The piece's lowest edge, over both ends.</summary>
        public float Low => MathF.Min(Floor1, Floor2);

        /// <summary>The piece's highest edge, over both ends.</summary>
        public float High => MathF.Max(Ceiling1, Ceiling2);

        /// <summary>Whether the piece shows nothing (the two regions' surfaces meet all along it).</summary>
        public bool IsEmpty => Ceiling1 <= Floor1 && Ceiling2 <= Floor2;
    }

    /// <summary>A flat-topped wall piece between two heights (a <c>BELOW</c> stack's spans).</summary>
    private static void EmitWallSegment(
        Level level,
        Accumulator accumulator,
        Wall wall,
        Vertex v1,
        Vertex v2,
        float floor,
        float ceiling,
        IReadOnlySet<string> rowMasked,
        int storey) =>
        EmitWallSegment(
            level, accumulator, wall, v1, v2, StackedPiece(level, wall, floor, ceiling), rowMasked, storey);

    private static void EmitWallSegment(
        Level level,
        Accumulator accumulator,
        Wall wall,
        Vertex v1,
        Vertex v2,
        WallPiece piece,
        IReadOnlySet<string> rowMasked,
        int storey = 0)
    {
        if (piece.IsEmpty)
        {
            return;
        }

        float dx = v2.X - v1.X;
        float dz = v2.Y - v1.Y;
        float length = MathF.Sqrt((dx * dx) + (dz * dz));
        TextureMapping mapping = RowMaskedTextureMappingFor(level, wall.Texture, rowMasked);
        // OV-20261009-shoot-mapping: viewed from the wall's left region (the room side of the
        // retail convention), the bitmap's left half sits at the SECOND vertex, so u starts at
        // v2 and grows toward v1. OV-20261030-wall-sides: from the other face it is the same, the
        // bitmap fixed to the wall and so seen mirrored. The OFFSET_X sense is unprobed.
        float uAtV2 = (float)wall.OffsetX * mapping.OffsetToU;
        float v0 = (float)wall.OffsetY * mapping.OffsetToV;
        float uAtV1 = uAtV2 + (length * mapping.WorldToU);
        float ambient = piece.Ambient;

        List<RenderVertex> target = StoreySide(level, wall.Texture, storey) is { } sideTexture
            ? accumulator.Bucket(sideTexture, animationSource: null, alphaTest: wall["TRANSPARENT"] > 0)
            : accumulator.Bucket(wall.Texture, AnimationSourceFor(level, wall.Texture, wall), alphaTest: wall["TRANSPARENT"] > 0);
        // Render depth is −map y (RenderSpace, ADR-0009); u follows the vertex order unchanged.
        float z1 = RenderSpace.ZFromMapY(v1.Y);
        float z2 = RenderSpace.ZFromMapY(v2.Y);
        // OV-20261104-texmap: the bitmap's rows run downward from the top of each repeat, the repeats
        // stacked from height 0 whatever the regions' floors (raised, pit), so v is −height at the
        // texture's scale; OFFSET_Y slides the texture down (wall-offset), so it is taken off v. A
        // sloped edge thus cuts the texture rather than stretching it.
        // OV-20261105-wall-pieces: a PORTCULLIS wall stacks its repeats from the edge its POSITION
        // names instead, each vertex's own edge of this piece, so a sloped edge keeps one row along
        // it; OFFSET_Y still slides the texture on top of that anchor.
        float worldToV = mapping.WorldToV;
        float anchor1 = WallAnchor(wall, piece.Floor1, piece.Ceiling1);
        float anchor2 = WallAnchor(wall, piece.Floor2, piece.Ceiling2);
        // OV-20261105-shade-global: every vertex carries the wall's own ends (not this piece's), so the
        // renderer can step the shade zones across the screen from them.
        var bottomLeft = new RenderVertex(v1.X, piece.Floor1, z1, uAtV1, -v0 + ((anchor1 - piece.Floor1) * worldToV), ambient, v1.X, z1, v2.X, z2);
        var bottomRight = new RenderVertex(v2.X, piece.Floor2, z2, uAtV2, -v0 + ((anchor2 - piece.Floor2) * worldToV), ambient, v1.X, z1, v2.X, z2);
        var topRight = new RenderVertex(v2.X, piece.Ceiling2, z2, uAtV2, -v0 + ((anchor2 - piece.Ceiling2) * worldToV), ambient, v1.X, z1, v2.X, z2);
        var topLeft = new RenderVertex(v1.X, piece.Ceiling1, z1, uAtV1, -v0 + ((anchor1 - piece.Ceiling1) * worldToV), ambient, v1.X, z1, v2.X, z2);
        target.Add(bottomLeft);
        target.Add(bottomRight);
        target.Add(topRight);
        target.Add(bottomLeft);
        target.Add(topRight);
        target.Add(topLeft);
    }

    /// <summary>
    /// OV-20261105-wall-pieces: the height a vertex's texture rows are measured from. A wall without
    /// the <c>PORTCULLIS</c> flag stacks from 0, as before; with it, POSITION 0 anchors at the vertex's
    /// lower edge of its piece and POSITION 1 at the upper one. Only 0 and 1 are measured, so any
    /// value above 0 is taken as "upper"; the manual's <c>FENCE</c>, which implies <c>PORTCULLIS</c>
    /// (p.54), is not implemented and retail uses neither.
    /// </summary>
    private static float WallAnchor(Wall wall, float floor, float ceiling) =>
        wall["PORTCULLIS"] <= 0 ? 0f : wall["POSITION"] > 0 ? ceiling : floor;

    private static void AddSurfaces(Level level, Accumulator accumulator, IReadOnlySet<string> rowMasked, IReadOnlySet<string>? blankSurfaces)
    {
        string? Shown(string? texture) => texture is not null && blankSurfaces?.Contains(texture) == true ? BlankSurface : texture;

        // OV-20261104-attach: a plain floor or ceiling hides no sprite (the batch is a Surface the host
        // draws without writing depth); the slabs of a BELOW stack do, since a sight line never leaves
        // its storey within the stack's footprint (OV-20261029-below-view), so those regions' surfaces
        // stay ordinary batches.
        var stacked = new HashSet<int>();
        for (int regionIndex = 0; regionIndex < level.Regions.Count; regionIndex++)
        {
            int below = level.Regions[regionIndex].BelowRegion;
            if (below >= 0)
            {
                stacked.Add(regionIndex);
                stacked.Add(below);
            }
        }

        for (int regionIndex = 0; regionIndex < level.Regions.Count; regionIndex++)
        {
            // OV-20261106-clip-view: a closed region (retail's "border", floor on ceiling) is never
            // entered, so nothing shows its surfaces: drawn, they lay over a neighbour's sky.
            if (level.Regions[regionIndex] is { } closed && closed.CeilHeight <= closed.FloorHeight && closed.BelowRegion < 0 && !stacked.Contains(regionIndex))
            {
                continue;
            }

            List<int>? loop = LevelTopology.TraceRegionLoop(level, regionIndex);
            if (loop is null)
            {
                AddOutlineSurfaces(level, accumulator, regionIndex, rowMasked, stacked.Contains(regionIndex), Shown);
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
                Shown(region.FloorTexture),
                region,
                (float)region.FloorOffsetX,
                (float)region.FloorOffsetY,
                rowMasked,
                flip: false,
                stacked: stacked.Contains(regionIndex));
            EmitSurface(
                level,
                accumulator,
                loop,
                triangles,
                (float)region.CeilHeight,
                (float)LevelTopology.SlopeSign(region, ceiling: true),
                Shown(region.CeilTexture),
                region,
                (float)region.CeilOffsetX,
                (float)region.CeilOffsetY,
                rowMasked,
                flip: true,
                stacked: stacked.Contains(regionIndex));
        }
    }

    /// <summary>
    /// A region with no single wall loop, an island inside it or a vertex three walls meet at: 158
    /// regions of Apathy's 1073 and as many elsewhere had no floor or ceiling at all. Its surfaces come
    /// from the even-odd trapezoids of its bounding walls, one quad a span.
    /// </summary>
    private static void AddOutlineSurfaces(Level level, Accumulator accumulator, int regionIndex, IReadOnlySet<string> rowMasked, bool stacked, Func<string?, string?> shown)
    {
        List<RegionSpan> spans = LevelTopology.RegionSpans(level, regionIndex);
        if (spans.Count == 0)
        {
            return;
        }

        Region region = level.Regions[regionIndex];
        EmitSpans(level, accumulator, spans, (float)region.FloorHeight, (float)LevelTopology.SlopeSign(region, ceiling: false), shown(region.FloorTexture), region, (float)region.FloorOffsetX, (float)region.FloorOffsetY, rowMasked, stacked);
        EmitSpans(level, accumulator, spans, (float)region.CeilHeight, (float)LevelTopology.SlopeSign(region, ceiling: true), shown(region.CeilTexture), region, (float)region.CeilOffsetX, (float)region.CeilOffsetY, rowMasked, stacked);
    }

    private static void EmitSpans(
        Level level,
        Accumulator accumulator,
        List<RegionSpan> spans,
        float height,
        float slopeSign,
        string? texture,
        Region region,
        float offsetX,
        float offsetY,
        IReadOnlySet<string> rowMasked,
        bool stacked)
    {
        if (string.IsNullOrEmpty(texture) || IsSkyTexture(level, texture))
        {
            return;
        }

        TextureMapping mapping = RowMaskedTextureMappingFor(level, texture, rowMasked);
        List<RenderVertex> target = accumulator.Bucket(texture, AnimationSourceFor(level, texture, region), surface: !stacked);
        var ambient = (float)region.Ambient;
        foreach (RegionSpan span in spans)
        {
            RenderVertex a = SurfaceVertex(span.A, height, slopeSign, mapping, offsetX, offsetY, ambient);
            RenderVertex b = SurfaceVertex(span.B, height, slopeSign, mapping, offsetX, offsetY, ambient);
            RenderVertex c = SurfaceVertex(span.C, height, slopeSign, mapping, offsetX, offsetY, ambient);
            RenderVertex d = SurfaceVertex(span.D, height, slopeSign, mapping, offsetX, offsetY, ambient);
            target.Add(a);
            target.Add(b);
            target.Add(c);
            target.Add(a);
            target.Add(c);
            target.Add(d);
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
        IReadOnlySet<string> rowMasked,
        bool flip,
        bool stacked = false)
    {
        if (IsSkyTexture(level, texture))
        {
            return;
        }

        // OV-20261105-wall-pieces round 6: a surface reading a bitmap the loader expanded to its row
        // period spans the period's rows too, since the image it samples has them. The mask is a wall
        // rule, so a surface of such a bitmap is unmeasured; this keeps a shared bitmap consistent
        // with the walls that read it.
        TextureMapping mapping = RowMaskedTextureMappingFor(level, texture, rowMasked);
        List<RenderVertex> target = accumulator.Bucket(
            texture,
            AnimationSourceFor(level, texture, region),
            surface: !stacked);

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
        Level level, int vertexIndex, float height, float slopeSign, TextureMapping mapping, float offsetX, float offsetY, float ambient) =>
        SurfaceVertex(level.Vertices[vertexIndex], height, slopeSign, mapping, offsetX, offsetY, ambient);

    private static RenderVertex SurfaceVertex(
        Vertex vertex, float height, float slopeSign, TextureMapping mapping, float offsetX, float offsetY, float ambient)
    {
        // A surface follows its vertices' heights only with a slope flag (OV-20261029-slope).
        // OV-20261104-texmap (floor-cell, ceil-cell): the texture's columns run along −x and its rows
        // along +y from the origin, at SCALE pixels a step; OV-20261104-blend (floor-offs, ceil-offs):
        // the region's OFFS_X comes off the column and its OFFS_Y adds to the row.
        return new RenderVertex(
            vertex.X,
            height + (slopeSign * vertex.Z),
            RenderSpace.ZFromMapY(vertex.Y),
            (-vertex.X * mapping.WorldToU) - (offsetX * mapping.OffsetToU),
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
    /// A wall piece's region <c>AMBIENT</c> (OV-20261105-wall-light): the region the piece faces, given
    /// as <paramref name="facing"/> — the lower piece the region with the lower floor, the upper piece
    /// the region with the higher ceiling — and, for a one-region wall or a tie, the left region's, the
    /// room side of the retail convention. A room with <c>AMBIENT</c> −0.25 darkened its walls to a
    /// solid region by 8 levels (OV-20261029-shading round 3), so the brighter side does not win. The
    /// <c>GENIUS</c> light stays the left region's whatever the piece (unmeasured).
    /// </summary>
    private static float WallAmbient(Level level, Wall wall, Region? facing = null)
    {
        Region? region = facing ?? RegionAt(level, wall.LeftRegion) ?? RegionAt(level, wall.RightRegion);
        return (region is null ? 0f : (float)region.Ambient) + (float)GeniusLight(level, wall);
    }

    /// <summary>
    /// The light a region's <c>GENIUS</c> object gives a wall of it (OV-20261104-genius): a thing or
    /// actor whose texture has <c>AMBIENT</c> &gt; 0 brightens the whole wall by 16 shade levels per 1.0
    /// of the wall texture's <c>ALBEDO</c> — half what an <c>AMBIENT</c> gives (32 a 1.0,
    /// <see cref="PaletteShading"/>) — times the cosine of the angle at the wall's midpoint between
    /// the wall's left normal and the lamp, whatever the distance or the lamp's <c>AMBIENT</c>: one
    /// value a wall, negative (darkening) with the lamp behind the wall.
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
        double normalX = -(b.Y - a.Y);
        double normalY = b.X - a.X;
        double toLampX = lamp.X - ((a.X + b.X) * 0.5);
        double toLampY = lamp.Y - ((a.Y + b.Y) * 0.5);
        double lengths = Math.Sqrt((normalX * normalX) + (normalY * normalY)) * Math.Sqrt((toLampX * toLampX) + (toLampY * toLampY));
        if (lengths <= 0)
        {
            return 0;
        }

        double cosine = ((normalX * toLampX) + (normalY * toLampY)) / lengths;
        return texture.Albedo * 0.5 * cosine;
    }

    /// <summary>
    /// OV-20261105-wall-pieces: the mapping of a bitmap <see cref="TextureLoader"/> expanded to its
    /// row period P, whose rows then span P rows rather than the bitmap's H: one repeat of the rows is
    /// <c>SCALE_Y / P</c> of <c>v</c> and <c>OFFSET_Y</c> is one pixel of the expanded image, 1/P. The
    /// columns are a plain modulo of the width, so they are unchanged. Only the bitmaps
    /// <see cref="TextureLoader.RowMaskedWallTextures"/> names are expanded, so a wall texture shared
    /// with a sprite or used as an attach keeps its real image and its plain mapping; anything else
    /// (<see cref="TextureMappingFor"/>), an attach's own coordinates and its real repeat height
    /// included, is untouched.
    /// </summary>
    private static TextureMapping RowMaskedTextureMappingFor(
        Level level, string? texture, IReadOnlySet<string> rowMasked)
    {
        TextureMapping mapping = TextureMappingFor(level, texture);
        if (texture is not null &&
            rowMasked.Contains(texture) &&
            level.Textures.TryGetValue(texture, out LevelTexture resolved) &&
            resolved.ScaleY > 0)
        {
            int period = TextureLoader.WallRowPeriod(resolved.Height);
            if (period > 0)
            {
                return mapping with
                {
                    WorldToV = (float)(resolved.ScaleY / period),
                    OffsetToV = 1f / period,
                };
            }
        }

        return mapping;
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
            bool alphaTest = false,
            bool surface = false)
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

                return animatedBuckets.For(alphaTest, surface);
            }

            if (!_byTexture.TryGetValue(texture, out TextureBuckets? buckets))
            {
                buckets = new TextureBuckets();
                _byTexture[texture] = buckets;
            }

            return buckets.For(alphaTest, surface);
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
            private readonly List<RenderVertex> _surfaces = [];

            public List<RenderVertex> For(bool alphaTest, bool surface) =>
                surface ? _surfaces : alphaTest ? _alphaTested : _opaque;

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

                if (_surfaces.Count > 0)
                {
                    batches.Add(new MeshBatch(texture, _surfaces, animationSource, alphaTest: false, surface: true));
                }
            }
        }
    }
}
