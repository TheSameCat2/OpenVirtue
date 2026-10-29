// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine;

/// <summary>A documented vertical transition through a linked region surface.</summary>
public readonly record struct PlayerVerticalTransition(
    Region Source,
    int FromRegion,
    int ToRegion,
    string HookName);

/// <summary>
/// First-person player physics over a level's sector geometry: tracks the region the player
/// stands in, resolves floor/ceiling motion, and moves horizontally using portal
/// crossing — passing through walls into neighbouring rooms, or being blocked by "solid"
/// walls (where the step up from the feet is too high or the eye would end up outside the
/// neighbour's floor and ceiling, OV-20261019-border-climb).
/// </summary>
/// <remarks>
/// Horizontal movement is point-based until radius-aware collision is applied.
/// </remarks>
public sealed class Player : AcknexObject
{
    private readonly Level _level;
    private readonly LevelCollisionWorld _collisionWorld;
    private readonly Dictionary<int, List<int>> _regionWalls = [];
    private readonly Dictionary<int, RegionSurface?> _regionSurfaces = [];
    private Vector3 _position;
    private int _region;
    private float _velocityY;
    private readonly List<PlayerVerticalTransition> _verticalTransitions = [];
    private readonly List<AcknexObject> _touchedObjects = [];

    public Vector3 Position
    {
        get => _position;
        set => _position = value;
    }

    public int Region => _region;

    public float VelocityY => _velocityY;

    /// <summary>Whether the feet are on or below the floor of a region the player cannot dive through.</summary>
    public bool Grounded { get; private set; }

    /// <summary>Vertical region transitions produced by the latest velocity move.</summary>
    public IReadOnlyList<PlayerVerticalTransition> VerticalTransitions => _verticalTransitions;

    /// <summary>
    /// How close the runtime lets the eye come to the floor and the ceiling of the player's
    /// region (<see cref="ClampEye"/>; OV-20261019-border-climb, OV-20261019-eye-clamp).
    /// </summary>
    public const float EyeMargin = 0.35f;

    /// <summary>Distance between the player's feet and eyes.</summary>
    public float EyeHeight { get; set; } = 3f;

    /// <summary>
    /// <c>PLAYER_WIDTH</c>: the minimum distance kept from solid walls and objects. A solid wall
    /// (or a region border the player cannot enter) holds the player <see cref="WallClearance"/>
    /// further away.
    /// </summary>
    public float Width { get; set; } = 1.2f;

    /// <summary>
    /// How much further than <see cref="Width"/> a solid wall or a blocking region border stops
    /// the player: patched V3.963 stopped walks 0.01–0.05 (mostly 0.045–0.05) beyond
    /// <c>PLAYER_WIDTH</c> 0.5, 1.2, 2 and 3, at any speed (OV-20261019-eye-clamp).
    /// </summary>
    public const float WallClearance = 0.05f;

    /// <summary>Global collision-width multiplier for actors.</summary>
    public float ActorWidthFactor { get; set; } = 1f;

    /// <summary>Global collision-width multiplier for things.</summary>
    public float ThingWidthFactor { get; set; } = 1f;

    /// <summary>
    /// <c>PLAYER_CLIMB</c>: a neighbouring floor must be less than this far above the player's
    /// feet (eye height minus <see cref="EyeHeight"/>) to step onto it (OV-20261019-border-climb).
    /// </summary>
    public float StepUp { get; set; } = 1.5f;

    /// <summary>Downward acceleration applied per tick.</summary>
    public float Gravity { get; set; } = 2f;

    /// <summary>Upward velocity applied on a jump.</summary>
    public float JumpSpeed { get; set; } = 25f;

    /// <summary>Current region floor height at the player's position.</summary>
    public float FloorHeight => FloorOf(_region);

    /// <summary>Current region ceiling height at the player's position.</summary>
    public float CeilingHeight => CeilingOf(_region);

    /// <summary>Floor ascent per map step along the X and Y axes at the player's position.</summary>
    public Vector2 FloorSlope
    {
        get
        {
            const float SampleDistance = 0.01f;
            double sign = (uint)_region < (uint)_level.Regions.Count
                ? LevelTopology.SlopeSign(_level.Regions[_region], ceiling: false)
                : 0;
            if (sign == 0 || !TryGetSurfaceHeightOffset(_region, _position.X, _position.Z, out double center))
            {
                return Vector2.Zero;
            }

            float x = SampleSlopeAxis(_position.X, _position.Z, SampleDistance, 0, center);
            float y = SampleSlopeAxis(_position.X, _position.Z, 0, SampleDistance, center);
            return new Vector2(x, y) * (float)sign;
        }
    }

    /// <summary>Depth of the current region's documented underwater <c>BELOW</c> link.</summary>
    public float WaterDepth => TryDiveRegion(_region, out int below)
        ? FloorOf(_region) - FloorOf(below)
        : 0;

    public Player(Level level) : base("player")
    {
        ArgumentNullException.ThrowIfNull(level);
        _level = level;
        _collisionWorld = new LevelCollisionWorld(level);

        for (int i = 0; i < level.Walls.Count; i++)
        {
            foreach (int member in LevelTopology.Stack(level, level.Walls[i].LeftRegion))
            {
                Index(member, i);
            }

            foreach (int member in LevelTopology.Stack(level, level.Walls[i].RightRegion))
            {
                Index(member, i);
            }
        }

        if (level.PlayerStart is { } start)
        {
            _region = start.Region;
            _position = new Vector3(start.X, FloorOf(_region, start.X, start.Y) + EyeOffset(_region), start.Y);
        }
    }

    /// <summary>Reflective WDL-style pose fields: <c>x/y</c> are map axes, <c>z</c> is height.</summary>
    protected override bool TryGetTyped(string name, out double value)
    {
        switch (name.ToLowerInvariant())
        {
            case "x": value = _position.X; return true;
            case "y": value = _position.Z; return true;
            case "z": value = _position.Y; return true;
            case "region": value = _region; return true;
            default: value = 0; return false;
        }
    }

    protected override bool TrySetTyped(string name, double value)
    {
        switch (name.ToLowerInvariant())
        {
            case "x": _position.X = (float)value; return true;
            case "y": _position.Z = (float)value; return true;
            case "z": _position.Y = (float)value; return true;
            case "region": _region = (int)value; return true;
            default: return false;
        }
    }

    /// <summary>
    /// Returns the walls that the horizontal moves since the last call passed through or pushed
    /// against, and the things and actors they pushed against (see
    /// <see cref="LevelCollisionWorld.Move"/>), and starts a new list. A placement with
    /// <see cref="MoveTo"/> or a position write touches nothing.
    /// </summary>
    public AcknexObject[] TakeTouchedObjects()
    {
        AcknexObject[] touched = [.. _touchedObjects];
        _touchedObjects.Clear();
        return touched;
    }

    /// <summary>Moves horizontally with portal crossing, body-radius collision, and wall sliding.</summary>
    public Vector2 MoveHorizontal(float dx, float dz) => MoveHorizontal(dx, dz, ignored: null);

    private Vector2 MoveHorizontal(float dx, float dz, MapEntity? ignored)
    {
        var start = new Vector2(_position.X, _position.Z);
        CircleMoveResult result = _collisionWorld.Move(
            start,
            _region,
            new Vector2(dx, dz),
            Width,
            CanCross,
            EntityCollisionRadius,
            static entity => entity["PASSABLE"] > 0 || entity["INVISIBLE"] > 0,
            ignored,
            _touchedObjects,
            WallClearance);
        _region = result.Region == _region ? _region : StoreyFor(result.Region);
        _position = new Vector3(result.Position.X, _position.Y, result.Position.Y);
        return result.Displacement;
    }

    /// <summary>
    /// Pushes the player out of any solid thing or actor it overlaps, straight away from the
    /// object's centre to the contact distance (half the object's width plus <see cref="Width"/>),
    /// and counts the object as touched. The original does this after a beam into an object
    /// (OV-20261019-actor-contact).
    /// </summary>
    public void PushOutOfObjects()
    {
        var start = new Vector2(_position.X, _position.Z);
        Vector2 end = _collisionWorld.PushOutOfEntities(
            start,
            _region,
            Width,
            EntityCollisionRadius,
            static entity => entity["PASSABLE"] > 0 || entity["INVISIBLE"] > 0,
            _touchedObjects);
        _position = new Vector3(end.X, _position.Y, end.Y);
    }

    /// <summary>
    /// Moves the player straight away from <paramref name="center"/> to
    /// <paramref name="contactDistance"/> when it is closer, as an actor walking into the player
    /// does (OV-20261019-actor-contact). Walls and other objects stop the push; the move ignores
    /// <paramref name="pusher"/>, whose contact hook is the caller's.
    /// </summary>
    /// <returns>Whether the player was within <paramref name="contactDistance"/>.</returns>
    public bool PushAway(MapEntity pusher, Vector2 center, float contactDistance)
    {
        ArgumentNullException.ThrowIfNull(pusher);
        var position = new Vector2(_position.X, _position.Z);
        Vector2 offset = position - center;
        float distance = offset.Length();
        if (distance >= contactDistance)
        {
            return false;
        }

        Vector2 normal = distance > 1e-6f ? offset / distance : Vector2.UnitX;
        Vector2 push = center + (normal * contactDistance) - position;
        MoveHorizontal(push.X, push.Y, pusher);
        return true;
    }

    /// <summary>
    /// A wall with one region on both sides is crossable only when <c>PASSABLE</c>. A wall between
    /// two regions is crossable when <c>PASSABLE</c>, whatever the heights, or when the player can
    /// enter the other region (OV-20261019-border-climb). An <c>INVISIBLE</c> wall is always
    /// crossable, even <c>IMPASSABLE</c>, and an <c>IMPASSABLE</c> wall that is not <c>PASSABLE</c>
    /// never is (OV-20261019-border-kinds).
    /// </summary>
    private bool CanCross(Wall wall, int currentRegion, int otherRegion)
    {
        if (wall["PASSABLE"] > 0 || wall["INVISIBLE"] > 0)
        {
            return true;
        }

        if (wall["IMPASSABLE"] > 0 ||
            otherRegion < 0 || otherRegion >= _level.Regions.Count ||
            LevelTopology.Footprint(_level, otherRegion) == LevelTopology.Footprint(_level, currentRegion))
        {
            return false;
        }

        return Enterable(StoreyFor(otherRegion));
    }

    /// <summary>
    /// The region of <paramref name="region"/>'s <c>BELOW</c> stack the player walks into: the
    /// uppermost storey it may enter (<see cref="Enterable"/>), else the one that holds its feet.
    /// A region without a stack is its own storey.
    /// </summary>
    private int StoreyFor(int region)
    {
        if ((uint)region >= (uint)_level.Regions.Count ||
            (_level.Regions[region].BelowRegion < 0 && _level.Regions[region].AboveRegion < 0))
        {
            return region;
        }

        foreach (int member in LevelTopology.Stack(_level, region))
        {
            if (Enterable(member))
            {
                return member;
            }
        }

        return LevelTopology.StackRegionAt(_level, region, _position.Y - EyeHeight);
    }

    private float EntityCollisionRadius(MapEntity entity)
    {
        if (!MapEntitySizing.TryGetSize(_level, entity, out MapEntitySize size))
        {
            return 0;
        }

        float factor = entity is Actor ? ActorWidthFactor : ThingWidthFactor;
        return size.Width * 0.5f * Math.Max(0, factor);
    }

    private void MoveHorizontalStep(Vector2 step)
    {
        var from = new Vector2(_position.X, _position.Z);
        Vector2 desired = from + step;
        CrossPortals(from, desired);

        for (int pass = 0; pass < 4; pass++)
        {
            bool adjusted = false;
            if (_regionWalls.TryGetValue(_region, out List<int>? walls))
            {
                foreach (int wallIndex in walls)
                {
                    Wall wall = _level.Walls[wallIndex];
                    if (CanCross(wall, out _) ||
                        !Vertex(wall.Vertex1, out Vector2 a) ||
                        !Vertex(wall.Vertex2, out Vector2 b))
                    {
                        continue;
                    }

                    Vector2 closest = ClosestPoint(desired, a, b, out float along);
                    float distanceSquared = Vector2.DistanceSquared(desired, closest);
                    if (distanceSquared >= Width * Width)
                    {
                        continue;
                    }

                    Vector2 normal = CollisionNormal(from, desired, closest, a, b, along);
                    desired = closest + (normal * Width);
                    adjusted = true;
                }
            }

            adjusted |= ResolveEntityCollisions(from, ref desired);

            if (!adjusted)
            {
                break;
            }
        }

        _position = new Vector3(desired.X, _position.Y, desired.Y);
    }

    private bool ResolveEntityCollisions(Vector2 from, ref Vector2 desired)
    {
        bool adjusted = false;
        foreach (Thing thing in _level.Things)
        {
            adjusted |= ResolveEntityCollision(thing, ThingWidthFactor, from, ref desired);
        }

        foreach (Actor actor in _level.Actors)
        {
            adjusted |= ResolveEntityCollision(actor, ActorWidthFactor, from, ref desired);
        }

        return adjusted;
    }

    private bool ResolveEntityCollision(MapEntity entity, float widthFactor, Vector2 from, ref Vector2 desired)
    {
        if (entity.Region != _region ||
            entity["passable"] > 0 ||
            entity["invisible"] > 0 ||
            !MapEntitySizing.TryGetSize(_level, entity, out MapEntitySize size))
        {
            return false;
        }

        float radius = Width + (size.Width * 0.5f * Math.Max(0, widthFactor));
        var center = new Vector2((float)entity.X, (float)entity.Y);
        Vector2 offset = desired - center;
        if (offset.LengthSquared() >= radius * radius)
        {
            return false;
        }

        Vector2 previousOffset = from - center;
        Vector2 normal = offset.LengthSquared() > float.Epsilon && Vector2.Dot(offset, previousOffset) >= 0
            ? Vector2.Normalize(offset)
            : previousOffset.LengthSquared() > float.Epsilon
                ? Vector2.Normalize(previousOffset)
                : Vector2.UnitX;
        desired = center + (normal * radius);
        return true;
    }

    private void CrossPortals(Vector2 from, Vector2 to)
    {
        if (!_regionWalls.TryGetValue(_region, out List<int>? walls))
        {
            return;
        }

        foreach (int wallIndex in walls)
        {
            Wall wall = _level.Walls[wallIndex];
            if (!CanCross(wall, out int other) ||
                other == _region ||
                other < 0 ||
                !Vertex(wall.Vertex1, out Vector2 a) ||
                !Vertex(wall.Vertex2, out Vector2 b) ||
                !CrossesPortal(from, to, a, b))
            {
                continue;
            }

            _region = StoreyFor(other);
            return;
        }
    }

    private bool CanCross(Wall wall, out int other)
    {
        other = wall.LeftRegion == LevelTopology.Footprint(_level, _region) ? wall.RightRegion : wall.LeftRegion;
        return CanCross(wall, _region, other);
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
    /// Moves the player's eye vertically by <paramref name="dy"/>, through linked <c>BELOW</c>
    /// surfaces (<c>IF_DIVE</c>/<c>IF_ARISE</c>). Nothing else holds the eye: the feet may sink
    /// below the floor, and only <see cref="ClampEye"/> keeps the eye inside the region
    /// (OV-20261019-eye-clamp).
    /// </summary>
    /// <returns>The vertical displacement, <paramref name="dy"/>.</returns>
    public float MoveVertical(float dy)
    {
        _verticalTransitions.Clear();
        if (dy == 0)
        {
            UpdateGrounded();
            return 0;
        }

        float requested = _position.Y + dy;
        for (int pass = 0; pass < 4; pass++)
        {
            if (dy < 0 &&
                TryDiveRegion(_region, out int below) &&
                requested < FloorOf(_region))
            {
                int previous = _region;
                Region source = _level.Regions[previous];
                _region = below;
                _verticalTransitions.Add(new PlayerVerticalTransition(source, previous, below, "IF_DIVE"));
                continue;
            }

            if (dy > 0 &&
                TryAriseRegion(_region, out int above) &&
                requested > CeilingOf(_region))
            {
                int previous = _region;
                Region source = _level.Regions[previous];
                _region = above;
                _verticalTransitions.Add(new PlayerVerticalTransition(source, previous, above, "IF_ARISE"));
                continue;
            }

            break;
        }

        _position.Y = requested;
        UpdateGrounded();
        return dy;
    }

    /// <summary>
    /// Keeps the eye at least <see cref="EyeMargin"/> above the floor and below the ceiling of the
    /// player's region, at the player's position, as patched V3.963 does after every movement
    /// (OV-20261019-border-climb, OV-20261019-eye-clamp). When the two overlap (headroom below
    /// twice the margin) the ceiling wins. An eye already inside is left alone, whatever
    /// <c>PLAYER_SIZE</c>: the feet may hang above or sink below the floor.
    /// </summary>
    /// <returns>The correction applied to the eye height (positive when it was raised).</returns>
    public float ClampEye()
    {
        float before = _position.Y;
        float eye = Math.Max(before, FloorOf(_region) + EyeMargin);
        eye = Math.Min(eye, CeilingOf(_region) - EyeMargin);
        _position.Y = eye;
        UpdateGrounded();
        return eye - before;
    }

    private void UpdateGrounded() =>
        Grounded = !TryDiveRegion(_region, out _) &&
                   _position.Y - EyeHeight <= FloorOf(_region) + 0.0001f;

    /// <summary>Advances vertical physics one tick: gravity, then land on the current region's floor.</summary>
    public void Tick()
    {
        float groundY = FloorOf(_region) + EyeOffset(_region);
        _velocityY -= Gravity;
        _position.Y += _velocityY;

        if (_position.Y <= groundY)
        {
            _position.Y = groundY;
            _velocityY = 0;
            Grounded = true;
        }
        else
        {
            Grounded = false;
        }
    }

    /// <summary>Places the player in a region at the given horizontal position, at eye level.</summary>
    public void MoveTo(int region, float x, float z)
    {
        _region = region;
        _position = new Vector3(x, FloorOf(region, x, z) + EyeOffset(region), z);
        _velocityY = 0;
        Grounded = true;
    }

    /// <summary>Jumps if standing on the ground.</summary>
    public void Jump()
    {
        if (Grounded)
        {
            _velocityY = JumpSpeed;
        }
    }

    private void Index(int region, int wallIndex)
    {
        if (!_regionWalls.TryGetValue(region, out List<int>? list))
        {
            list = [];
            _regionWalls[region] = list;
        }

        list.Add(wallIndex);
    }

    /// <summary>
    /// Whether the player may walk into <paramref name="region"/> through a wall that is not
    /// <c>PASSABLE</c>, measured in patched V3.963 (OV-20261019-border-climb): the region's floor
    /// must be less than <see cref="StepUp"/> above the player's feet (a step equal to it blocks),
    /// and the eye must lie above its floor and below its ceiling. The step is taken from the feet,
    /// not from the current floor, so a raised eye climbs higher and a stepping-down player who has
    /// not sunk walks back up. Neither <c>PLAYER_SIZE</c> nor the region's headroom count otherwise:
    /// a player taller than the headroom enters as long as the eye fits.
    /// </summary>
    private bool Enterable(int region)
    {
        if (region < 0 || region >= _level.Regions.Count)
        {
            return false;
        }

        float eye = _position.Y;
        float feet = eye - EyeHeight;
        float floor = FloorOf(region);
        return floor - feet < StepUp && eye > floor && eye < CeilingOf(region);
    }

    private bool TryDiveRegion(int region, out int below)
    {
        below = -1;
        if (region < 0 || region >= _level.Regions.Count)
        {
            return false;
        }

        Region upper = _level.Regions[region];
        if (upper.BelowName is not { } belowName ||
            !HasAction(upper, "IF_DIVE"))
        {
            return false;
        }

        below = upper.BelowRegion >= 0
            ? upper.BelowRegion
            : FindRegionIndex(candidate => candidate.Name.Equals(belowName, StringComparison.OrdinalIgnoreCase));
        return below >= 0 &&
               HasAction(_level.Regions[below], "IF_ARISE") &&
               Math.Abs(upper.FloorHeight - _level.Regions[below].CeilHeight) <= 0.001;
    }

    private bool TryAriseRegion(int region, out int above)
    {
        above = -1;
        if (region < 0 || region >= _level.Regions.Count)
        {
            return false;
        }

        Region lower = _level.Regions[region];
        if (!HasAction(lower, "IF_ARISE"))
        {
            return false;
        }

        if (lower.AboveRegion >= 0)
        {
            above = lower.AboveRegion;
            Region upper = _level.Regions[above];
            return HasAction(upper, "IF_DIVE") && Math.Abs(upper.FloorHeight - lower.CeilHeight) <= 0.001;
        }

        above = FindRegionIndex(candidate =>
            candidate.BelowName is { } belowName &&
            belowName.Equals(lower.Name, StringComparison.OrdinalIgnoreCase) &&
            HasAction(candidate, "IF_DIVE") &&
            Math.Abs(candidate.FloorHeight - lower.CeilHeight) <= 0.001);
        return above >= 0;
    }

    private static bool HasAction(Region region, string hookName) =>
        region.TryGetActionHook(hookName, out string? actionName) && !string.IsNullOrWhiteSpace(actionName);

    private int FindRegionIndex(Func<Region, bool> predicate)
    {
        for (int i = 0; i < _level.Regions.Count; i++)
        {
            if (predicate(_level.Regions[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private float FloorOf(int region) => FloorOf(region, _position.X, _position.Z);

    private float FloorOf(int region, float x, float z)
    {
        if (region < 0 || region >= _level.Regions.Count)
        {
            return _position.Y - EyeHeight;
        }

        double sign = LevelTopology.SlopeSign(_level.Regions[region], ceiling: false);
        double offset = sign != 0 && TryGetSurfaceHeightOffset(region, x, z, out double value) ? sign * value : 0;
        return (float)(_level.Regions[region].FloorHeight + offset);
    }

    private float CeilingOf(int region)
    {
        if (region < 0 || region >= _level.Regions.Count)
        {
            return _position.Y;
        }

        double sign = LevelTopology.SlopeSign(_level.Regions[region], ceiling: true);
        double offset = sign != 0 && TryGetSurfaceHeightOffset(region, _position.X, _position.Z, out double value) ? sign * value : 0;
        return (float)(_level.Regions[region].CeilHeight + offset);
    }

    private bool TryGetSurfaceHeightOffset(int region, float x, float z, out double offset)
    {
        if (!_regionSurfaces.TryGetValue(region, out RegionSurface? surface))
        {
            List<int>? loop = LevelTopology.TraceRegionLoop(_level, region);
            if (loop is not null)
            {
                Vector2[] footprint = loop
                    .Select(index => new Vector2(_level.Vertices[index].X, _level.Vertices[index].Y))
                    .ToArray();
                IReadOnlyList<int> triangles = EarClipping.Triangulate(footprint);
                surface = triangles.Count > 0 ? new RegionSurface(loop, triangles) : null;
            }

            _regionSurfaces[region] = surface;
        }

        if (surface is null)
        {
            offset = 0;
            return false;
        }

        return LevelTopology.TryGetSurfaceHeightOffset(
            _level,
            surface.Loop,
            surface.Triangles,
            x,
            z,
            out offset);
    }

    private float SampleSlopeAxis(float x, float z, float dx, float dz, double center)
    {
        const float SampleDistance = 0.01f;
        if (TryGetSurfaceHeightOffset(_region, x + dx, z + dz, out double forward))
        {
            return (float)((forward - center) / SampleDistance);
        }

        if (TryGetSurfaceHeightOffset(_region, x - dx, z - dz, out double backward))
        {
            return (float)((center - backward) / SampleDistance);
        }

        return 0;
    }

    private float EyeOffset(int region)
    {
        if (region < 0 || region >= _level.Regions.Count)
        {
            return EyeHeight;
        }

        var r = _level.Regions[region];
        float headroom = (float)(r.CeilHeight - r.FloorHeight);
        return MathF.Min(EyeHeight, MathF.Max(1f, headroom * 0.6f));
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

    private static bool CrossesPortal(Vector2 from, Vector2 to, Vector2 a, Vector2 b)
    {
        const float epsilon = 0.00001f;
        float fromSide = Orient(a, b, from);
        float toSide = Orient(a, b, to);
        if (MathF.Abs(fromSide) <= epsilon ||
            (fromSide > 0 && toSide > epsilon) ||
            (fromSide < 0 && toSide < -epsilon))
        {
            return false;
        }

        float endpointSide1 = Orient(from, to, a);
        float endpointSide2 = Orient(from, to, b);
        return !((endpointSide1 > epsilon && endpointSide2 > epsilon) ||
                 (endpointSide1 < -epsilon && endpointSide2 < -epsilon));
    }

    private static float Orient(Vector2 a, Vector2 b, Vector2 c) =>
        ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));

    private sealed record RegionSurface(List<int> Loop, IReadOnlyList<int> Triangles);
}
