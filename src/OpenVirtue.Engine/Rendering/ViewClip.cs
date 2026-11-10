// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Map;

namespace OpenVirtue.Engine.Rendering;

/// <summary>
/// What <c>CLIP_DIST</c> does to the picture (OV-20261106-clip-view). Walking a screen column from
/// the eye, a wall seen from a region is out of reach once |dx| + |dy| from the player to its nearer
/// vertex is no longer below that region's <c>CLIP_DIST</c>, unless it has <c>FAR</c>. An out-of-reach
/// wall ends the walk: everything still open in that column, the seeing region's own floor and
/// ceiling too, shows palette entry 1. Nearer pieces and floors stay, because the walk had closed
/// those rows already. A sprite is drawn only while its |dx| + |dy| from the player is below its own
/// region's and the player's region's <c>CLIP_DIST</c>. <c>THING_DIST</c>, <c>ACTOR_DIST</c> and
/// <c>CLIPPING</c> change nothing on the picture, and neither does a <c>CLIP_DIST</c> skill.
/// </summary>
/// <remarks>
/// This engine stays a triangle renderer, so the column rule is built from two passes the renderer
/// draws after the world and before the sprites: <see cref="MeshBuilder.BuildOccluders"/>'s pieces
/// reaching up and down without end, depth only, and <see cref="AddFills"/>'s walls reaching both
/// ways in the clip colour. Floors do not write depth, so a fill covers the floor before its wall;
/// the occluders keep it off rows a nearer step closed, and also hide what stands behind a piece
/// and sticks out above it.
/// </remarks>
public sealed class ViewClip
{
    /// <summary>How far past any level a piece or a fill reaches, in steps.</summary>
    public const float Reach = 100000f;

    private readonly Level _level;
    private readonly HashSet<Wall> _reversed = [];
    private readonly Dictionary<int, List<Wall>> _regionWalls = [];

    private const float NearDepth = 0.5f;

    /// <summary>
    /// Reads which of each two-region wall's regions lies on which side. A map's left region is on
    /// the wall's geometric left only by the editor's habit, so each wall is tested with a point
    /// just off its middle.
    /// </summary>
    public ViewClip(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _level = level;
        foreach (Wall wall in level.Walls)
        {
            if (wall.LeftRegion == wall.RightRegion ||
                (uint)wall.Vertex1 >= (uint)level.Vertices.Count ||
                (uint)wall.Vertex2 >= (uint)level.Vertices.Count)
            {
                continue;
            }

            foreach (int region in (ReadOnlySpan<int>)[wall.LeftRegion, wall.RightRegion])
            {
                if (!_regionWalls.TryGetValue(region, out List<Wall>? list))
                {
                    list = [];
                    _regionWalls[region] = list;
                }

                list.Add(wall);
            }

            Vertex v1 = level.Vertices[wall.Vertex1];
            Vertex v2 = level.Vertices[wall.Vertex2];
            double dx = v2.X - v1.X;
            double dy = v2.Y - v1.Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length <= 0)
            {
                continue;
            }

            // A hair to the geometric left of the wall's middle.
            double x = ((v1.X + v2.X) / 2) - (dy / length * 0.01);
            double y = ((v1.Y + v2.Y) / 2) + (dx / length * 0.01);
            bool inRight = LevelTopology.RegionContains(level, wall.RightRegion, x, y);
            bool inLeft = LevelTopology.RegionContains(level, wall.LeftRegion, x, y);
            if (inRight && !inLeft)
            {
                _reversed.Add(wall);
            }
        }
    }

    /// <summary>Whether a wall seen from (<paramref name="x"/>, <paramref name="y"/>) is out of reach.</summary>
    public bool IsWallOutOfReach(Wall wall, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(wall);
        if ((uint)wall.Vertex1 >= (uint)_level.Vertices.Count || (uint)wall.Vertex2 >= (uint)_level.Vertices.Count)
        {
            return false;
        }

        Vertex v1 = _level.Vertices[wall.Vertex1];
        Vertex v2 = _level.Vertices[wall.Vertex2];
        return IsOutOfReach(wall, v1, v2, x, y);
    }

    /// <summary>Appends a fill quad for every out-of-reach wall in front of the eye.</summary>
    public void AddFills(float x, float y, float forwardX, float forwardY, List<RenderVertex> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        IReadOnlyList<Vertex> map = _level.Vertices;
        foreach (Wall wall in _level.Walls)
        {
            if ((uint)wall.Vertex1 >= (uint)map.Count || (uint)wall.Vertex2 >= (uint)map.Count)
            {
                continue;
            }

            Vertex v1 = map[wall.Vertex1];
            Vertex v2 = map[wall.Vertex2];
            if (((v1.X - x) * forwardX) + ((v1.Y - y) * forwardY) <= 0 &&
                ((v2.X - x) * forwardX) + ((v2.Y - y) * forwardY) <= 0)
            {
                continue;
            }

            if (!IsOutOfReach(wall, v1, v2, x, y))
            {
                continue;
            }

            float z1 = RenderSpace.ZFromMapY(v1.Y);
            float z2 = RenderSpace.ZFromMapY(v2.Y);
            var a = new RenderVertex(v1.X, -Reach, z1, 0, 0);
            var b = new RenderVertex(v2.X, -Reach, z2, 0, 0);
            var c = new RenderVertex(v2.X, Reach, z2, 0, 0);
            var d = new RenderVertex(v1.X, Reach, z1, 0, 0);
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(a);
            vertices.Add(c);
            vertices.Add(d);
        }
    }

    /// <summary>
    /// How deep a sprite counts for hiding (OV-20261106-clip-view p-wide): the walls bounding its own
    /// region do not cut it, so a wide sprite beside a wall shows across the wall. It is nearer of its
    /// true depth and those walls within its columns; a free wall, or any wall on the way into the
    /// region, still hides it.
    /// </summary>
    /// <param name="regionIndex">The sprite's region.</param>
    /// <param name="eyeRegion">The player's region.</param>
    /// <param name="x">The eye's map x.</param>
    /// <param name="y">The eye's map y.</param>
    /// <param name="forwardX">The view's map direction, x.</param>
    /// <param name="forwardY">The view's map direction, y.</param>
    /// <param name="depth">The sprite's depth along the view.</param>
    /// <param name="lateral">The sprite's centre across the view, to the right.</param>
    /// <param name="halfWidth">The sprite's half width.</param>
    public float SpriteHidingDepth(int regionIndex, int eyeRegion, float x, float y, float forwardX, float forwardY, float depth, float lateral, float halfWidth)
    {
        // Only from inside the sprite's region do its walls let it through (p-wide); seen from another
        // region, the border it is seen across is the opening that cuts it (below-view room-high).
        if (depth <= 0 || regionIndex != eyeRegion || !_regionWalls.TryGetValue(regionIndex, out List<Wall>? walls))
        {
            return depth;
        }

        float rightX = forwardY;
        float rightY = -forwardX;
        float from = (lateral - halfWidth) / depth;
        float to = (lateral + halfWidth) / depth;
        float nearest = depth;
        foreach (Wall wall in walls)
        {
            Vertex a = _level.Vertices[wall.Vertex1];
            Vertex b = _level.Vertices[wall.Vertex2];

            float da = ((a.X - x) * forwardX) + ((a.Y - y) * forwardY);
            float db = ((b.X - x) * forwardX) + ((b.Y - y) * forwardY);
            if (da <= NearDepth && db <= NearDepth)
            {
                continue;
            }

            float la = ((a.X - x) * rightX) + ((a.Y - y) * rightY);
            float lb = ((b.X - x) * rightX) + ((b.Y - y) * rightY);

            // Cut the wall at the near plane.
            if (da < NearDepth)
            {
                float t = (NearDepth - da) / (db - da);
                la += (lb - la) * t;
                da = NearDepth;
            }
            else if (db < NearDepth)
            {
                float t = (NearDepth - db) / (da - db);
                lb += (la - lb) * t;
                db = NearDepth;
            }

            // Where it crosses the sprite's columns, 1/depth runs straight in the column.
            float ca = la / da;
            float cb = lb / db;
            if (ca > cb)
            {
                (ca, cb) = (cb, ca);
                (da, db) = (db, da);
            }

            float low = MathF.Max(ca, from);
            float high = MathF.Min(cb, to);
            if (low > high)
            {
                continue;
            }

            float span = cb - ca;
            float atLow = span > 1e-6f ? 1f / da + ((low - ca) / span * ((1f / db) - (1f / da))) : 1f / da;
            float atHigh = span > 1e-6f ? 1f / da + ((high - ca) / span * ((1f / db) - (1f / da))) : 1f / db;
            nearest = MathF.Min(nearest, 1f / MathF.Max(atLow, atHigh));
        }

        return nearest < depth ? MathF.Max(NearDepth, nearest - 0.05f) : depth;
    }

    /// <summary>Whether a thing or actor is within its reach of (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static bool IsEntityInReach(Level level, MapEntity entity, Region? playerRegion, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(entity);
        double clip = playerRegion?["CLIP_DIST"] ?? double.PositiveInfinity;
        if ((uint)entity.Region < (uint)level.Regions.Count)
        {
            clip = Math.Min(clip, level.Regions[entity.Region]["CLIP_DIST"]);
        }

        return Math.Abs(entity.X - x) + Math.Abs(entity.Y - y) < clip;
    }

    private bool IsOutOfReach(Wall wall, Vertex v1, Vertex v2, float x, float y)
    {
        if (wall["FAR"] > 0)
        {
            return false;
        }

        Region? seeing = SeeingRegion(wall, v1, v2, x, y);
        if (seeing is null || seeing.BelowRegion >= 0)
        {
            return false;
        }

        // A border between two regions of one height is no stop for the walk, flagged or not: the
        // column goes on across it, and a far region's small CLIP_DIST never applies (pad-small,
        // q-near-color).
        if (!HasStep(_level, wall))
        {
            return false;
        }

        double near = Math.Min(Math.Abs(v1.X - x) + Math.Abs(v1.Y - y), Math.Abs(v2.X - x) + Math.Abs(v2.Y - y));
        return near >= seeing["CLIP_DIST"];
    }

    private static bool HasStep(Level level, Wall wall)
    {
        if (wall.LeftRegion == wall.RightRegion ||
            (uint)wall.LeftRegion >= (uint)level.Regions.Count ||
            (uint)wall.RightRegion >= (uint)level.Regions.Count)
        {
            return true;
        }

        Region left = level.Regions[wall.LeftRegion];
        Region right = level.Regions[wall.RightRegion];
        return left.FloorHeight != right.FloorHeight || left.CeilHeight != right.CeilHeight;
    }

    /// <summary>The region on the eye's side of a wall.</summary>
    private Region? SeeingRegion(Wall wall, Vertex v1, Vertex v2, float x, float y)
    {
        double cross = ((v2.X - v1.X) * (y - v1.Y)) - ((v2.Y - v1.Y) * (x - v1.X));
        bool left = (cross >= 0) != _reversed.Contains(wall);
        int index = left ? wall.LeftRegion : wall.RightRegion;
        if ((uint)index >= (uint)_level.Regions.Count)
        {
            index = left ? wall.RightRegion : wall.LeftRegion;
        }

        return (uint)index < (uint)_level.Regions.Count ? _level.Regions[index] : null;
    }
}
