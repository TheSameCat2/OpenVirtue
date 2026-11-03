// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;

namespace OpenVirtue.Engine.Map;

/// <summary>
/// Result of radius-aware horizontal movement through level geometry. <see cref="ObjectPush"/> is
/// the part of <see cref="Displacement"/> by which things and actors pushed a raw-contact mover back.
/// </summary>
public readonly record struct CircleMoveResult(
    Vector2 Position,
    int Region,
    Vector2 Displacement,
    bool Collided,
    bool RegionChanged,
    Vector2 ObjectPush = default);

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
    /// With <paramref name="stopAtContact"/> a step that would end exactly <paramref name="radius"/>
    /// from a wall is stopped too (the player's, OV-20261019-eye-clamp stop-border). When
    /// <paramref name="blocked"/> is given, the solid walls that pushed the circle back go there
    /// instead of into <paramref name="touched"/>. With <paramref name="rawContact"/> (the player's)
    /// a thing or actor is touched when the step ends with L² &lt; (c − 1)² in 22.10 (c in 1/1024
    /// units), and the step is then pushed out to C² as in <see cref="PushOutOfEntities(Vector2, int, float, Func{MapEntity, float}, Func{MapEntity, bool}, ICollection{AcknexObject}?)"/>:
    /// a player creeping 4/1024 a step into a 2-wide actor was left alone at L² 4951 and put back to
    /// C² 4948 + 102 at 4946 and 4939, below (c − 1)² 4948 (OV-20261029-overlap still-walk).
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
        bool clearanceOnContact = false,
        bool stopAtContact = false,
        ICollection<AcknexObject>? blocked = null,
        bool rawContact = false)
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
        if (stopAtContact)
        {
            // Positions are on the 1/1024 grid, so half a unit takes in exactly the contact distance.
            wallContact += 0.5f / 1024;
        }

        Vector2 position = start;
        int region = startRegion;
        Vector2 objectPush = Vector2.Zero;
        bool endOnly = rawContact && requested.Length() <= 2 * radius;
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
                    from, region, wallContact, wallRadius, canCross, crossedWallIndex, blocked ?? touched, ref desired);
                // The player's step meets things and actors once, at its end: steps of 0.4 to 1.6 into a
                // 2-wide thing were put back from where the whole step ended (OV-20261103-contact-clock).
                // A step longer than the player's width is not measured that way (3.2 did not pass
                // through), so it keeps testing each sub-step.
                if (!endOnly || i == stepCount - 1)
                {
                    adjusted |= ResolveEntities(
                        endOnly ? start : from, region, radius, contactSlackSquared, rawContact, entityRadius, canCrossEntity, ignoredEntity, touched, ref desired, ref objectPush);
                }
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

        return new CircleMoveResult(position, region, position - start, collided, regionChanged, objectPush);
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
    /// What the original's start-up push-out adds to the square of the contact distance, in 22.10
    /// raw units of a squared length, on top of the square of one unit less than the contact:
    /// 102, about 0.1 (OV-20261103-startup-clock, 2026-11-03 width addendum).
    /// </summary>
    public const int PushSlackSquaredRaw = 102;

    /// <summary>
    /// The squared length, in 22.10 raw units, below which a step or a start-up position counts as
    /// inside an object at contact distance <paramref name="contact"/> (raw): (c − 1)²/1024, cut. The
    /// original tests one unit inside the summed radii (OV-20261029-overlap still-walk).
    /// </summary>
    private static long ContactTestSquared(long contact) => (contact - 1) * (contact - 1) / 1024;

    /// <summary>
    /// The squared length, in 22.10 raw units, to which the start-up (or a beam's) push-out puts the
    /// player: (c − 1)²/1024 + <see cref="PushSlackSquaredRaw"/>. It is the only such term that gives
    /// all 21 measured push-outs of 1- to 16-wide objects; c²/1024 + 98 missed the two 4-wide ones off
    /// the axes by a unit (OV-20261103-startup-clock).
    /// </summary>
    private static long PushTargetSquared(long contact) => ContactTestSquared(contact) + PushSlackSquaredRaw;

    /// <summary>
    /// The squared length, in 22.10 raw units, to which a step into a thing or actor is pushed back:
    /// c²/1024 + <see cref="WalkSlackSquaredRaw"/>. Walking into 4-wide things and actors fits 10578,
    /// where the start-up push-out gives 10576, and the term is the best of every form tried over 78
    /// pushes with player widths 0.5, 1.2 and 2 (65 exact; OV-20261103-contact-clock).
    /// </summary>
    private static long WalkTargetSquared(long contact) => (contact * contact / 1024) + WalkSlackSquaredRaw;

    /// <summary>What a step's push-back adds to c²/1024: 98 (OV-20261103-contact-clock).</summary>
    public const int WalkSlackSquaredRaw = 98;

    /// <summary>
    /// The squared length, in 22.10 raw units, from which the push-out no longer finds an object:
    /// 8 steps. A 16-wide actor (contact 9.2) 8.25 away left the player alone; 3 away it pushed it
    /// out to 9.2 (OV-20261103-startup-clock).
    /// </summary>
    public const long PushReachSquaredRaw = 64 * 1024;

    /// <summary>
    /// Pushes a circle that overlaps a solid thing or actor straight out from its centre to the
    /// contact distance, without a move (a player beamed inside an object,
    /// OV-20261019-actor-contact), and adds the object to <paramref name="touched"/>. Only one
    /// object counts: the first overlapping one in reverse map order, actors before things. On its
    /// exact centre there is no push at all; nor is there one out of the other objects the circle
    /// still overlaps (OV-20261029-overlap).
    /// <para>
    /// The arithmetic is the original's 22.10 (OV-20261103-startup-clock): coordinates cut toward zero;
    /// the squared length L² = (dx² + dy²)/1024 and C² = (c − 1)²/1024 + <see cref="PushSlackSquaredRaw"/>
    /// (c the summed radii) cut; the object counts when L² &lt; C² and L² &lt; 8²; the offset is then
    /// scaled by √(C²/L²), the ratio and its root each cut to 1/1024, and the product rounded. That
    /// gives the measured 2.272 out of a 2-wide actor 2.1 away, 2.274 from 1.0 away and 2.292 from
    /// 0.1 away.
    /// </para>
    /// </summary>
    public Vector2 PushOutOfEntities(
        Vector2 position,
        int region,
        float radius,
        Func<MapEntity, float> entityRadius,
        Func<MapEntity, bool> canCrossEntity,
        ICollection<AcknexObject>? touched = null) =>
        PushOutOfEntities(position, region, radius, entityRadius, canCrossEntity, out _, touched);

    /// <summary>
    /// Pushes out as <see cref="PushOutOfEntities(Vector2, int, float, Func{MapEntity, float}, Func{MapEntity, bool}, ICollection{AcknexObject}?)"/>,
    /// and tells whether the push fell short. Very near the centre the root's argument, the ratio
    /// times 1024, no longer fits in 32 bits; the original then moved the player only a quarter of
    /// <paramref name="radius"/> (the player's <c>PLAYER_WIDTH</c>) away from the centre, and pushed
    /// it out in full the cycle after (OV-20261103-startup-clock push-d005: 0.05 from a 2-wide actor's
    /// centre, to −307/1024 in cycle 2 and −2224/1024 in cycle 3).
    /// </summary>
    public Vector2 PushOutOfEntities(
        Vector2 position,
        int region,
        float radius,
        Func<MapEntity, float> entityRadius,
        Func<MapEntity, bool> canCrossEntity,
        out bool unfinished,
        ICollection<AcknexObject>? touched = null)
    {
        ArgumentNullException.ThrowIfNull(entityRadius);
        ArgumentNullException.ThrowIfNull(canCrossEntity);
        unfinished = false;
        long px = Raw(position.X);
        long py = Raw(position.Y);
        foreach (MapEntity entity in _level.Actors.Reverse<MapEntity>().Concat(_level.Things.Reverse<MapEntity>()))
        {
            if (entity.Region != region || canCrossEntity(entity))
            {
                continue;
            }

            long contact = Raw(Math.Max(0.01f, radius)) + Raw(Math.Max(0, entityRadius(entity)));
            long contactSquared = PushTargetSquared(contact);
            long ax = Raw(entity.X);
            long ay = Raw(entity.Y);
            long dx = px - ax;
            long dy = py - ay;
            long lengthSquared = ((dx * dx) + (dy * dy)) / 1024;
            if (lengthSquared >= contactSquared || lengthSquared >= PushReachSquaredRaw)
            {
                continue;
            }

            if (lengthSquared == 0)
            {
                return position;
            }

            double ex;
            double ey;
            if (ScaleToContact(dx, dy, lengthSquared, contactSquared) is (double ox, double oy))
            {
                ex = ax + ox;
                ey = ay + oy;
            }
            else
            {
                long step = Raw(Math.Max(0.01f, radius)) / 4;
                double length = Math.Sqrt((dx * dx) + (dy * dy));
                ex = px + Math.Floor((step * dx / length) + 0.5);
                ey = py + Math.Floor((step * dy / length) + 0.5);
                unfinished = true;
            }

            // The push is a step with no part along the other axis, and the original then moves
            // the player 4/1024 along that axis, always positive (OV-20261029-actor-step).
            if (dx == 0)
            {
                ex += 4;
            }
            else if (dy == 0)
            {
                ey += 4;
            }

            Vector2 end = new((float)(ex / 1024), (float)(ey / 1024));
            if (touched is not null && !touched.Contains(entity))
            {
                touched.Add(entity);
            }

            return end;
        }

        return position;
    }

    /// <summary>
    /// The 22.10 offset (dx, dy) scaled from its squared length L² to the squared contact C², in raw
    /// units: the ratio C²·1024/L² and its root each cut, the product rounded
    /// (OV-20261103-startup-clock). Null when the root's argument overflows 32 bits.
    /// </summary>
    private static (double X, double Y)? ScaleToContact(long dx, long dy, long lengthSquared, long contactSquared)
    {
        long ratio = contactSquared * 1024 / lengthSquared;
        if (ratio * 1024 > int.MaxValue)
        {
            return null;
        }

        double scale = Math.Floor(Math.Sqrt(ratio * 1024.0));
        return (Math.Floor((dx * scale / 1024) + 0.5), Math.Floor((dy * scale / 1024) + 0.5));
    }

    /// <summary>A coordinate in 1/1024 steps, cut toward zero.</summary>
    private static long Raw(double value) => (long)Math.Truncate(value * 1024);

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
        bool rawContact,
        Func<MapEntity, float> entityRadius,
        Func<MapEntity, bool> canCrossEntity,
        MapEntity? ignoredEntity,
        ICollection<AcknexObject>? touched,
        ref Vector2 desired,
        ref Vector2 objectPush)
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

            if (rawContact)
            {
                long contact = Raw(radius) + Raw(Math.Max(0, entityRadius(entity)));
                long ax = Raw(entity.X);
                long ay = Raw(entity.Y);
                // The step's end is on the 1/1024 grid; rounding undoes the sub-steps' float error.
                long dx = (long)Math.Round(desired.X * 1024.0) - ax;
                long dy = (long)Math.Round(desired.Y * 1024.0) - ay;
                long lengthSquared = ((dx * dx) + (dy * dy)) / 1024;
                if (lengthSquared >= ContactTestSquared(contact))
                {
                    continue;
                }

                // A step past the centre, onto it or too near it for the 22.10 root keeps the plain rule.
                bool ahead = (dx * ((long)Math.Round(from.X * 1024.0) - ax)) + (dy * ((long)Math.Round(from.Y * 1024.0) - ay)) >= 0;
                if (lengthSquared > 0 && ahead &&
                    ScaleToContact(dx, dy, lengthSquared, WalkTargetSquared(contact)) is (double ox, double oy))
                {
                    var pushed = new Vector2((float)((ax + ox) / 1024), (float)((ay + oy) / 1024));
                    objectPush += pushed - desired;
                    desired = pushed;
                    adjusted = true;
                    if (touched is not null && !touched.Contains(entity))
                    {
                        touched.Add(entity);
                    }

                    continue;
                }
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
