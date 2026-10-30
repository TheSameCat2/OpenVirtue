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
            // A wall bounds every region of a BELOW stack, not only the map's top region.
            foreach (int member in LevelTopology.Stack(level, level.Walls[i].LeftRegion))
            {
                Index(member, i);
            }

            if (level.Walls[i].RightRegion != level.Walls[i].LeftRegion)
            {
                foreach (int member in LevelTopology.Stack(level, level.Walls[i].RightRegion))
                {
                    Index(member, i);
                }
            }
        }
    }

    /// <summary>
    /// Moves a circle through portals while sliding along solid walls and entities. When
    /// <paramref name="touched"/> is given, every object the move touched is added to it: a wall the
    /// circle may cross whose segment a step passed through, end points included (a portal between
    /// two regions too, OV-20261019-clip-contact), a solid wall that pushed the circle back, and a
    /// solid thing or actor that pushed it back (OV-20261019-clip-contact). A step that ends on a
    /// wall's line touches it, and so does the next step off it (OV-20261019-wall-pass-clip).
    /// A solid wall holds the circle <paramref name="wallClearance"/> beyond <paramref name="radius"/>.
    /// <paramref name="contactSlackSquared"/> is added to the square of each contact distance with
    /// a thing or actor (<see cref="ContactSlackSquared"/> for the player). With
    /// <paramref name="clearanceOnContact"/> a step is stopped only when it would come closer than
    /// <paramref name="radius"/>, and then held at the clearance: an actor's step may end 1.031 from
    /// a wall, and the next one is pushed back to about 1.05 (OV-20261029-actor-events round 2, bullet).
    /// </summary>
    public CircleMoveResult Move(
        Vector2 start,
        int startRegion,
        Vector2 requested,
        float radius,
        Func<Wall, int, int, bool> canCross,
        Func<MapEntity, float> entityRadius,
        Func<MapEntity, bool> canCrossEntity,
        MapEntity? ignoredEntity = null,
        ICollection<AcknexObject>? touched = null,
        float wallClearance = 0,
        float contactSlackSquared = 0,
        bool clearanceOnContact = false)
    {
        ArgumentNullException.ThrowIfNull(canCross);
        ArgumentNullException.ThrowIfNull(entityRadius);
        ArgumentNullException.ThrowIfNull(canCrossEntity);
        if (requested.LengthSquared() <= float.Epsilon)
        {
            return new CircleMoveResult(start, startRegion, Vector2.Zero, false, false);
        }

        radius = Math.Max(0.01f, radius);
        float wallRadius = radius + Math.Max(0, wallClearance);
        float wallContact = clearanceOnContact ? radius : wallRadius;
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
                    from, region, wallContact, wallRadius, canCross, crossedWallIndex, touched, ref desired);
                adjusted |= ResolveEntities(
                    from, region, radius, contactSlackSquared, entityRadius, canCrossEntity, ignoredEntity, touched, ref desired);
                collided |= adjusted;
                if (!adjusted)
                {
                    break;
                }
            }

            if (touched is not null)
            {
                AddPassedWalls(from, desired, region, canCross, touched);
            }

            position = desired;
        }

        return new CircleMoveResult(position, region, position - start, collided, regionChanged);
    }

    /// <summary>
    /// What the original adds to the square of the player's contact distance with an object (half
    /// the object's width plus <c>PLAYER_WIDTH</c>): the player stops, and is pushed out, about 0.02
    /// beyond a 2.2 contact distance and 0.01 beyond a 5.2 one, √(c² + 0.093) for every width
    /// measured (OV-20261029-overlap). Actors' contacts with things are not measured and use c.
    /// </summary>
    public const float ContactSlackSquared = 0.093f;

    /// <summary>The distance between centres at which the player stops against an object, from their summed radii.</summary>
    public static float ContactDistance(float combinedRadius) =>
        MathF.Sqrt((combinedRadius * combinedRadius) + ContactSlackSquared);

    /// <summary>
    /// Pushes a circle that overlaps a solid thing or actor straight out from its centre to the
    /// contact distance, without a move (a player beamed inside an object,
    /// OV-20261019-actor-contact), and adds the object to <paramref name="touched"/>. Only one
    /// object counts: the first overlapping one in reverse map order, actors before things. On its
    /// exact centre there is no push at all; nor is there one out of the other objects the circle
    /// still overlaps (OV-20261029-overlap).
    /// </summary>
    public Vector2 PushOutOfEntities(
        Vector2 position,
        int region,
        float radius,
        Func<MapEntity, float> entityRadius,
        Func<MapEntity, bool> canCrossEntity,
        ICollection<AcknexObject>? touched = null)
    {
        ArgumentNullException.ThrowIfNull(entityRadius);
        ArgumentNullException.ThrowIfNull(canCrossEntity);
        foreach (MapEntity entity in _level.Actors.Reverse<MapEntity>().Concat(_level.Things.Reverse<MapEntity>()))
        {
            if (entity.Region != region || canCrossEntity(entity))
            {
                continue;
            }

            float contact = ContactDistance(Math.Max(0.01f, radius) + Math.Max(0, entityRadius(entity)));
            var center = new Vector2((float)entity.X, (float)entity.Y);
            Vector2 offset = position - center;
            if (offset.LengthSquared() >= contact * contact)
            {
                continue;
            }

            if (offset.LengthSquared() <= float.Epsilon)
            {
                return position;
            }

            // The push is a step with no part along the other axis, and the original then moves
            // the player 4/1024 along that axis, always positive (OV-20261029-actor-step).
            const float Nudge = 4f / 1024;
            Vector2 end = center + (Vector2.Normalize(offset) * contact);
            if (offset.X == 0)
            {
                end.X += Nudge;
            }
            else if (offset.Y == 0)
            {
                end.Y += Nudge;
            }

            if (touched is not null && !touched.Contains(entity))
            {
                touched.Add(entity);
            }

            return end;
        }

        return position;
    }

    private bool ResolveWalls(
        Vector2 from,
        int region,
        float contact,
        float radius,
        Func<Wall, int, int, bool> canCross,
        int ignoredWallIndex,
        ICollection<AcknexObject>? touched,
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
            if (Vector2.DistanceSquared(desired, closest) >= contact * contact)
            {
                continue;
            }

            desired = closest + (CollisionNormal(from, desired, closest, a, b, along) * radius);
            adjusted = true;
            if (touched is not null && !touched.Contains(wall))
            {
                touched.Add(wall);
            }
        }

        return adjusted;
    }

    /// <summary>
    /// Adds the crossable walls of the step's region (a portal wall is listed in both of its
    /// regions) whose segment the step from <paramref name="from"/> to <paramref name="to"/>
    /// passed through. An <c>INVISIBLE</c> wall with one region on both sides is passed without
    /// a touch; one between two regions still counts (OV-20261019-border-kinds).
    /// </summary>
    private void AddPassedWalls(
        Vector2 from,
        Vector2 to,
        int region,
        Func<Wall, int, int, bool> canCross,
        ICollection<AcknexObject> touched)
    {
        if (!_regionWalls.TryGetValue(region, out List<int>? walls))
        {
            return;
        }

        foreach (int wallIndex in walls)
        {
            Wall wall = _level.Walls[wallIndex];
            int other = OtherRegion(wall, region);
            if (!touched.Contains(wall) &&
                !(wall["INVISIBLE"] > 0 && other == region) &&
                Vertex(wall.Vertex1, out Vector2 a) &&
                Vertex(wall.Vertex2, out Vector2 b) &&
                PassesThrough(from, to, a, b) &&
                canCross(wall, region, other))
            {
                touched.Add(wall);
            }
        }
    }

    /// <summary>
    /// Whether a step changes its side of the line through <paramref name="a"/> and
    /// <paramref name="b"/>, counting "on the line" as a side of its own, within the segment
    /// including its end points.
    /// </summary>
    private static bool PassesThrough(Vector2 from, Vector2 to, Vector2 a, Vector2 b)
    {
        if (Math.Sign(Orient(a, b, from)) == Math.Sign(Orient(a, b, to)))
        {
            return false;
        }

        float endpointSide1 = Orient(from, to, a);
        float endpointSide2 = Orient(from, to, b);
        return !((endpointSide1 > 0 && endpointSide2 > 0) || (endpointSide1 < 0 && endpointSide2 < 0));
    }

    private bool ResolveEntities(
        Vector2 from,
        int region,
        float radius,
        float contactSlackSquared,
        Func<MapEntity, float> entityRadius,
        Func<MapEntity, bool> canCrossEntity,
        MapEntity? ignoredEntity,
        ICollection<AcknexObject>? touched,
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
            combinedRadius = MathF.Sqrt((combinedRadius * combinedRadius) + contactSlackSquared);
            var center = new Vector2((float)entity.X, (float)entity.Y);
            Vector2 offset = desired - center;
            if (offset.LengthSquared() >= combinedRadius * combinedRadius)
            {
                continue;
            }

            Vector2 previousOffset = from - center;
            if (offset.LengthSquared() <= float.Epsilon && previousOffset.LengthSquared() <= float.Epsilon)
            {
                // OV-20261029-overlap: exactly on an object's centre there is no way out, and the
                // original leaves the player where it is.
                continue;
            }

            Vector2 normal = offset.LengthSquared() > float.Epsilon && Vector2.Dot(offset, previousOffset) >= 0
                ? Vector2.Normalize(offset)
                : Vector2.Normalize(previousOffset);
            desired = center + (normal * combinedRadius);
            adjusted = true;
            if (touched is not null && !touched.Contains(entity))
            {
                touched.Add(entity);
            }
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
                (entered >= 0 && LevelTopology.Footprint(_level, entered) == LevelTopology.Footprint(_level, region)) ||
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

    /// <summary>The map region across <paramref name="wall"/> from <paramref name="region"/> or from its <c>BELOW</c> stack.</summary>
    private int OtherRegion(Wall wall, int region) =>
        wall.LeftRegion == LevelTopology.Footprint(_level, region) ? wall.RightRegion : wall.LeftRegion;

    /// <summary>
    /// The region on the side of the wall that <paramref name="point"/> lies on: the WMP's first
    /// region is left of v1→v2 (y-up), the second right of it. Crossing a wall enters that region,
    /// not simply the wall's other region, so a mover whose region went stale (a beam without
    /// LOCATE) keeps it when it crosses onto the stale region's side
    /// (OV-20261019-proximity-events, case region-beam-walk).
    /// </summary>
    private int RegionOnSide(Wall wall, int region, Vector2 a, Vector2 b, Vector2 point)
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

    /// <summary>
    /// Whether a step changes sides of the portal line within the segment. A step that starts
    /// within <c>Epsilon</c> of the line counts when it ends clearly off it: the step before may
    /// have stopped just short of the line, on the old side, and the caller only changes the region
    /// when the end lies on the other region's side.
    /// </summary>
    private static bool CrossesPortal(Vector2 from, Vector2 to, Vector2 a, Vector2 b)
    {
        const float Epsilon = 0.00001f;
        float fromSide = Orient(a, b, from);
        float toSide = Orient(a, b, to);
        if ((MathF.Abs(fromSide) <= Epsilon && MathF.Abs(toSide) <= Epsilon) ||
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
