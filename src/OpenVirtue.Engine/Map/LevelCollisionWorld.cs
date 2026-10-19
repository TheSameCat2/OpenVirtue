// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;

namespace OpenVirtue.Engine.Map;

/// <summary>Result of radius-aware horizontal movement through level geometry.</summary>
public readonly record struct CircleMoveResult(
    Vector2 Position,
    int Region,
    Vector2 Displacement,
    bool Collided,
    bool RegionChanged);

/// <summary>Reusable circle collision against walls and live map entities.</summary>
public sealed class LevelCollisionWorld
{
    private readonly Level _level;
    private readonly Dictionary<int, List<int>> _regionWalls = [];

    public LevelCollisionWorld(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _level = level;
        for (int i = 0; i < level.Walls.Count; i++)
        {
            Index(level.Walls[i].LeftRegion, i);
            if (level.Walls[i].RightRegion != level.Walls[i].LeftRegion)
            {
                Index(level.Walls[i].RightRegion, i);
            }
        }
    }

    /// <summary>Moves a circle through portals while sliding along solid walls and entities.</summary>
    public CircleMoveResult Move(
        Vector2 start,
        int startRegion,
        Vector2 requested,
        float radius,
        Func<Wall, int, int, bool> canCross,
        Func<MapEntity, float> entityRadius,
        Func<MapEntity, bool> canCrossEntity,
        MapEntity? ignoredEntity = null)
    {
        ArgumentNullException.ThrowIfNull(canCross);
        ArgumentNullException.ThrowIfNull(entityRadius);
        ArgumentNullException.ThrowIfNull(canCrossEntity);
        if (requested.LengthSquared() <= float.Epsilon)
        {
            return new CircleMoveResult(start, startRegion, Vector2.Zero, false, false);
        }

        radius = Math.Max(0.01f, radius);
        Vector2 position = start;
        int region = startRegion;
        bool collided = false;
        bool regionChanged = false;
        float maximumStep = Math.Max(0.1f, radius * 0.5f);
        int stepCount = Math.Max(1, (int)MathF.Ceiling(requested.Length() / maximumStep));
        Vector2 step = requested / stepCount;
        for (int i = 0; i < stepCount; i++)
        {
            Vector2 from = position;
            Vector2 desired = from + step;
            int nextRegion = CrossPortal(from, desired, region, canCross, out int crossedWallIndex);
            regionChanged |= nextRegion != region;
            region = nextRegion;

            for (int pass = 0; pass < 4; pass++)
            {
                bool adjusted = ResolveWalls(
                    from, region, radius, canCross, crossedWallIndex, ref desired);
                adjusted |= ResolveEntities(
                    from, region, radius, entityRadius, canCrossEntity, ignoredEntity, ref desired);
                collided |= adjusted;
                if (!adjusted)
                {
                    break;
                }
            }

            position = desired;
        }

        return new CircleMoveResult(position, region, position - start, collided, regionChanged);
    }

    private bool ResolveWalls(
        Vector2 from,
        int region,
        float radius,
        Func<Wall, int, int, bool> canCross,
        int ignoredWallIndex,
        ref Vector2 desired)
    {
        if (!_regionWalls.TryGetValue(region, out List<int>? walls))
        {
            return false;
        }

        bool adjusted = false;
        foreach (int wallIndex in walls)
        {
            if (wallIndex == ignoredWallIndex)
            {
                continue;
            }

            Wall wall = _level.Walls[wallIndex];
            int other = OtherRegion(wall, region);
            if (canCross(wall, region, other) ||
                !Vertex(wall.Vertex1, out Vector2 a) ||
                !Vertex(wall.Vertex2, out Vector2 b))
            {
                continue;
            }

            Vector2 closest = ClosestPoint(desired, a, b, out float along);
            if (Vector2.DistanceSquared(desired, closest) >= radius * radius)
            {
                continue;
            }

            desired = closest + (CollisionNormal(from, desired, closest, a, b, along) * radius);
            adjusted = true;
        }

        return adjusted;
    }

    private bool ResolveEntities(
        Vector2 from,
        int region,
        float radius,
        Func<MapEntity, float> entityRadius,
        Func<MapEntity, bool> canCrossEntity,
        MapEntity? ignoredEntity,
        ref Vector2 desired)
    {
        bool adjusted = false;
        foreach (MapEntity entity in _level.Things.Cast<MapEntity>().Concat(_level.Actors))
        {
            if (ReferenceEquals(entity, ignoredEntity) ||
                entity.Region != region ||
                canCrossEntity(entity))
            {
                continue;
            }

            float combinedRadius = radius + Math.Max(0, entityRadius(entity));
            var center = new Vector2((float)entity.X, (float)entity.Y);
            Vector2 offset = desired - center;
            if (offset.LengthSquared() >= combinedRadius * combinedRadius)
            {
                continue;
            }

            Vector2 previousOffset = from - center;
            Vector2 normal = offset.LengthSquared() > float.Epsilon && Vector2.Dot(offset, previousOffset) >= 0
                ? Vector2.Normalize(offset)
                : previousOffset.LengthSquared() > float.Epsilon
                    ? Vector2.Normalize(previousOffset)
                    : Vector2.UnitX;
            desired = center + (normal * combinedRadius);
            adjusted = true;
        }

        return adjusted;
    }

    private int CrossPortal(
        Vector2 from,
        Vector2 to,
        int region,
        Func<Wall, int, int, bool> canCross,
        out int crossedWallIndex)
    {
        crossedWallIndex = -1;
        if (!_regionWalls.TryGetValue(region, out List<int>? walls))
        {
            return region;
        }

        foreach (int wallIndex in walls)
        {
            Wall wall = _level.Walls[wallIndex];
            if (!Vertex(wall.Vertex1, out Vector2 a) ||
                !Vertex(wall.Vertex2, out Vector2 b))
            {
                continue;
            }

            int entered = RegionOnSide(wall, region, a, b, to);
            if (entered == region ||
                entered < 0 ||
                !canCross(wall, region, entered) ||
                !CrossesPortal(from, to, a, b))
            {
                continue;
            }

            crossedWallIndex = wallIndex;
            return entered;
        }

        return region;
    }

    private static int OtherRegion(Wall wall, int region) =>
        wall.LeftRegion == region ? wall.RightRegion : wall.LeftRegion;

    /// <summary>
    /// The region on the side of the wall that <paramref name="point"/> lies on: the WMP's first
    /// region is left of v1→v2 (y-up), the second right of it. Crossing a wall enters that region,
    /// not simply the wall's other region, so a mover whose region went stale (a beam without
    /// LOCATE) keeps it when it crosses onto the stale region's side
    /// (OV-20261019-proximity-events, case region-beam-walk).
    /// </summary>
    private static int RegionOnSide(Wall wall, int region, Vector2 a, Vector2 b, Vector2 point)
    {
        Vector2 edge = b - a;
        Vector2 offset = point - a;
        float cross = (edge.X * offset.Y) - (edge.Y * offset.X);
        return cross > 0 ? wall.LeftRegion
            : cross < 0 ? wall.RightRegion
            : OtherRegion(wall, region);
    }

    private void Index(int region, int wallIndex)
    {
        if (!_regionWalls.TryGetValue(region, out List<int>? walls))
        {
            walls = [];
            _regionWalls[region] = walls;
        }

        walls.Add(wallIndex);
    }

    private bool Vertex(int index, out Vector2 point)
    {
        if (index >= 0 && index < _level.Vertices.Count)
        {
            point = new Vector2(_level.Vertices[index].X, _level.Vertices[index].Y);
            return true;
        }

        point = default;
        return false;
    }

    private static Vector2 ClosestPoint(Vector2 point, Vector2 a, Vector2 b, out float along)
    {
        Vector2 segment = b - a;
        float lengthSquared = segment.LengthSquared();
        along = lengthSquared > float.Epsilon
            ? Math.Clamp(Vector2.Dot(point - a, segment) / lengthSquared, 0, 1)
            : 0;
        return a + (segment * along);
    }

    private static Vector2 CollisionNormal(
        Vector2 from,
        Vector2 desired,
        Vector2 closest,
        Vector2 a,
        Vector2 b,
        float along)
    {
        if (along <= 0.0001f || along >= 0.9999f)
        {
            Vector2 radial = desired - closest;
            Vector2 previousRadial = from - closest;
            if (radial.LengthSquared() > float.Epsilon && Vector2.Dot(radial, previousRadial) >= 0)
            {
                return Vector2.Normalize(radial);
            }

            if (previousRadial.LengthSquared() > float.Epsilon)
            {
                return Vector2.Normalize(previousRadial);
            }
        }

        Vector2 segment = b - a;
        if (segment.LengthSquared() <= float.Epsilon)
        {
            return Vector2.UnitX;
        }

        Vector2 leftNormal = Vector2.Normalize(new Vector2(-segment.Y, segment.X));
        return Orient(a, b, from) >= 0 ? leftNormal : -leftNormal;
    }

    private static bool CrossesPortal(Vector2 from, Vector2 to, Vector2 a, Vector2 b)
    {
        const float Epsilon = 0.00001f;
        float fromSide = Orient(a, b, from);
        float toSide = Orient(a, b, to);
        if (MathF.Abs(fromSide) <= Epsilon ||
            (fromSide > 0 && toSide > Epsilon) ||
            (fromSide < 0 && toSide < -Epsilon))
        {
            return false;
        }

        float endpointSide1 = Orient(from, to, a);
        float endpointSide2 = Orient(from, to, b);
        return !((endpointSide1 > Epsilon && endpointSide2 > Epsilon) ||
                 (endpointSide1 < -Epsilon && endpointSide2 < -Epsilon));
    }

    private static float Orient(Vector2 a, Vector2 b, Vector2 c) =>
        ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
}
