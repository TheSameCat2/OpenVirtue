// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Map;

namespace OpenVirtue.Engine;

/// <summary>
/// First-person player physics over a level's sector geometry: tracks the region the player
/// stands in, follows its floor with gravity/jump, and moves horizontally using portal
/// crossing — passing through walls into neighbouring rooms, or being blocked by "solid"
/// walls (where the neighbour has no headroom or the step up is too high).
/// </summary>
/// <remarks>
/// Movement is point-based (no body radius yet) and blocks rather than slides on a solid wall —
/// a deliberate first cut. Physics constants are world-unit tunables.
/// </remarks>
public sealed class Player : AcknexObject
{
    private readonly Level _level;
    private readonly Dictionary<int, List<int>> _regionWalls = [];
    private Vector3 _position;
    private int _region;
    private float _velocityY;

    public Vector3 Position
    {
        get => _position;
        set => _position = value;
    }

    public int Region => _region;

    public float VelocityY => _velocityY;

    public bool Grounded { get; private set; }

    /// <summary>Eye height above the floor (capped to the region's headroom).</summary>
    public float EyeHeight { get; set; } = 12f;

    /// <summary>Minimum neighbour headroom required to step through a wall (else it is solid).</summary>
    public float MinHeadroom { get; set; } = 5f;

    /// <summary>Largest floor rise the player can step up through a portal.</summary>
    public float StepUp { get; set; } = 16f;

    /// <summary>Downward acceleration applied per tick.</summary>
    public float Gravity { get; set; } = 2f;

    /// <summary>Upward velocity applied on a jump.</summary>
    public float JumpSpeed { get; set; } = 25f;

    public Player(Level level) : base("player")
    {
        ArgumentNullException.ThrowIfNull(level);
        _level = level;

        for (int i = 0; i < level.Walls.Count; i++)
        {
            Index(level.Walls[i].LeftRegion, i);
            Index(level.Walls[i].RightRegion, i);
        }

        if (level.PlayerStart is { } start)
        {
            _region = start.Region;
            _position = new Vector3(start.X, FloorOf(_region) + EyeOffset(_region), start.Y);
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

    /// <summary>Attempts a horizontal move, crossing portals and blocking on solid walls.</summary>
    public void MoveHorizontal(float dx, float dz)
    {
        var from = new Vector2(_position.X, _position.Z);
        var to = new Vector2(_position.X + dx, _position.Z + dz);

        if (_regionWalls.TryGetValue(_region, out List<int>? walls))
        {
            foreach (int wallIndex in walls)
            {
                Wall wall = _level.Walls[wallIndex];
                if (!Vertex(wall.Vertex1, out Vector2 a) || !Vertex(wall.Vertex2, out Vector2 b) || !Crosses(from, to, a, b))
                {
                    continue;
                }

                int other = wall.LeftRegion == _region ? wall.RightRegion : wall.LeftRegion;
                if (!Enterable(other))
                {
                    return; // solid wall — block the whole move
                }

                _region = other; // step through the portal
                break;
            }
        }

        _position = new Vector3(to.X, _position.Y, to.Y);
    }

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
        _position = new Vector3(x, FloorOf(region) + EyeOffset(region), z);
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

    private bool Enterable(int region)
    {
        if (region < 0 || region >= _level.Regions.Count)
        {
            return false;
        }

        var target = _level.Regions[region];
        float headroom = (float)(target.CeilHeight - target.FloorHeight);
        float step = (float)target.FloorHeight - FloorOf(_region);
        return headroom >= MinHeadroom && step <= StepUp;
    }

    private float FloorOf(int region) =>
        region >= 0 && region < _level.Regions.Count ? (float)_level.Regions[region].FloorHeight : _position.Y - EyeHeight;

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

    // True if segment p1->p2 crosses segment p3->p4.
    private static bool Crosses(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        float d1 = Orient(p3, p4, p1);
        float d2 = Orient(p3, p4, p2);
        float d3 = Orient(p1, p2, p3);
        float d4 = Orient(p1, p2, p4);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static float Orient(Vector2 a, Vector2 b, Vector2 c) =>
        ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
}
