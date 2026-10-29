// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Map;

namespace OpenVirtue.Engine.Rendering;

/// <summary>Where the player looks from: the eye in map coordinates, <c>PLAYER_ANGLE</c> and the projection.</summary>
/// <param name="X">The eye's map x.</param>
/// <param name="Y">The eye's map y.</param>
/// <param name="EyeHeight">The eye's world height (<c>PLAYER_Z</c>).</param>
/// <param name="Angle">The view direction in radians, 0 along map +x (<c>PLAYER_ANGLE</c>).</param>
/// <param name="Projection">The camera projection: the horizontal cone and the vertical window.</param>
public readonly record struct Viewer(double X, double Y, double EyeHeight, double Angle, ProjectionModel Projection);

/// <summary>
/// A headless model of what the original's renderer shows (ADR-0010, OV-20261020-view-activity):
/// whether some part of a wall, thing or actor is on the screen in the last drawn frame, the
/// <c>#VISIBLE</c> flag (p.53, p.70) that <c>SKIP_FRAMES</c> and <c>BERKELEY</c> walls follow, and
/// whether some of a region is (<see cref="IsRegionVisible"/>). A
/// point is seen when it lies within the horizontal <c>PLAYER_ARC</c> cone of
/// <see cref="ProjectionModel"/>, the sight line from the eye reaches it, and its height falls
/// within the screen's top and bottom (with the <c>PLAYER_TILT</c> shear). Walls with one region on
/// both sides (or none behind them) hide what is behind them unless they are <c>INVISIBLE</c>, and
/// a wall between two regions shows only what is seen through the opening between the higher floor
/// and the lower ceiling. A wall or sprite is visible when any sampled point of it is, so a sprite
/// half behind a wall's end still counts. Slopes and <c>TRANSPARENT</c> holes are not modelled:
/// <c>TRANSPARENT</c> walls are treated as see-through. An instance indexes the walls where they
/// stand in a grid, which <see cref="Update"/> keeps current when vertices move; wall flags and
/// region heights are read when a sight line meets a wall.
/// </summary>
public sealed class ViewTest
{
    private const int SpriteSamples = 5;
    private const int MaxWallSamples = 32;
    private const double Epsilon = 1e-6;

    /// <summary>How thick a floor or ceiling is taken to be when it is sampled for <see cref="IsRegionVisible"/>.</summary>
    private const double SurfaceBand = 0.01;

    /// <summary>The side of a cell of the grid that indexes the walls for the sight lines, in steps.</summary>
    private const double CellSize = 16;

    private readonly Level _level;
    private readonly Segment[] _segments;
    private readonly int[][] _segmentsOfVertex;
    private readonly Dictionary<int, List<int>> _segmentsOfRegion = [];
    private readonly double[] _vertices;
    private readonly double _gridX;
    private readonly double _gridY;
    private readonly int _columns;
    private readonly int _rows;
    private readonly List<int>?[] _cells;

    // Which sight line last tested each wall, so a wall in several cells is tested once.
    private readonly int[] _testedBy;
    private int _sightLine;

    // The walls the current sight line crosses (fraction of the way, segment), and the spans of
    // heights at the target that it reaches, each with the storey it reaches them in.
    private readonly List<(double T, int Segment)> _crossings = [];
    private List<(int Storey, double Low, double High)> _spans = [];
    private List<(int Storey, double Low, double High)> _nextSpans = [];

    /// <summary>Indexes the walls of <paramref name="level"/> where they stand.</summary>
    public ViewTest(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _level = level;
        _vertices = new double[level.Vertices.Count * 2];
        for (int i = 0; i < level.Vertices.Count; i++)
        {
            _vertices[2 * i] = level.Vertices[i].X;
            _vertices[(2 * i) + 1] = level.Vertices[i].Y;
        }

        var segments = new List<Segment>(level.Walls.Count);
        var segmentsOfVertex = new List<int>?[level.Vertices.Count];
        foreach (Wall wall in level.Walls)
        {
            if (TryGetSegment(level, wall, out double ax, out double ay, out double bx, out double by))
            {
                (segmentsOfVertex[wall.Vertex1] ??= []).Add(segments.Count);
                (segmentsOfVertex[wall.Vertex2] ??= []).Add(segments.Count);
                AddRegionSegment(wall.LeftRegion, segments.Count);
                if (wall.RightRegion != wall.LeftRegion)
                {
                    AddRegionSegment(wall.RightRegion, segments.Count);
                }

                segments.Add(new Segment(wall, ax, ay, bx, by));
            }
        }

        _segments = [.. segments];
        _segmentsOfVertex = [.. segmentsOfVertex.Select(static list => list is null ? [] : list.ToArray())];
        _testedBy = new int[_segments.Length];
        if (_segments.Length == 0)
        {
            _cells = [];
            return;
        }

        _gridX = _segments.Min(static o => Math.Min(o.Ax, o.Bx));
        _gridY = _segments.Min(static o => Math.Min(o.Ay, o.By));
        _columns = (int)((_segments.Max(static o => Math.Max(o.Ax, o.Bx)) - _gridX) / CellSize) + 1;
        _rows = (int)((_segments.Max(static o => Math.Max(o.Ay, o.By)) - _gridY) / CellSize) + 1;
        _cells = new List<int>?[_columns * _rows];
        for (int i = 0; i < _segments.Length; i++)
        {
            Index(i, add: true);
        }
    }

    /// <summary>
    /// Re-indexes the walls whose vertices moved since the last call (doors and lifts that shift
    /// vertices). Returns <c>false</c> when this instance belongs to another level or its vertex
    /// table changed size, and a new instance is needed.
    /// </summary>
    public bool Update(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (!ReferenceEquals(level, _level) || level.Vertices.Count * 2 != _vertices.Length)
        {
            return false;
        }

        for (int v = 0; v < level.Vertices.Count; v++)
        {
            Vertex vertex = level.Vertices[v];
            if (vertex.X == _vertices[2 * v] && vertex.Y == _vertices[(2 * v) + 1])
            {
                continue;
            }

            _vertices[2 * v] = vertex.X;
            _vertices[(2 * v) + 1] = vertex.Y;
            foreach (int i in _segmentsOfVertex[v])
            {
                Index(i, add: false);
                Wall wall = _segments[i].Wall;
                _ = TryGetSegment(level, wall, out double ax, out double ay, out double bx, out double by);
                _segments[i] = new Segment(wall, ax, ay, bx, by);
                Index(i, add: true);
            }
        }

        return true;
    }

    /// <summary>Adds a wall to, or removes it from, the cells its bounding box covers (clamped to the grid).</summary>
    private void Index(int i, bool add)
    {
        Segment o = _segments[i];
        int column0 = Column(Math.Min(o.Ax, o.Bx) - Epsilon);
        int column1 = Column(Math.Max(o.Ax, o.Bx) + Epsilon);
        int row0 = Row(Math.Min(o.Ay, o.By) - Epsilon);
        int row1 = Row(Math.Max(o.Ay, o.By) + Epsilon);
        for (int row = row0; row <= row1; row++)
        {
            for (int column = column0; column <= column1; column++)
            {
                int cell = (row * _columns) + column;
                if (add)
                {
                    (_cells[cell] ??= []).Add(i);
                }
                else
                {
                    _cells[cell]?.Remove(i);
                }
            }
        }
    }

    private int Column(double x) => Math.Clamp((int)Math.Floor((x - _gridX) / CellSize), 0, _columns - 1);

    private int Row(double y) => Math.Clamp((int)Math.Floor((y - _gridY) / CellSize), 0, _rows - 1);

    /// <summary>A wall's segment where it was last indexed.</summary>
    private readonly record struct Segment(Wall Wall, double Ax, double Ay, double Bx, double By);

    /// <summary>
    /// Tests a billboard sprite standing at (<paramref name="x"/>, <paramref name="y"/>) and facing the
    /// viewer, <paramref name="halfWidth"/> to either side, from <paramref name="bottom"/> to
    /// <paramref name="top"/> in world height. <paramref name="region"/> is the region (storey) it stands
    /// in, or −1; it matters only in a <c>BELOW</c> stack.
    /// </summary>
    public bool IsSpriteVisible(Viewer viewer, double x, double y, double halfWidth, double bottom, double top, int region = -1)
    {
        double dx = x - viewer.X;
        double dy = y - viewer.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        // Across the line of sight (to the viewer's right when looking at the sprite).
        double sideX = length > Epsilon ? dy / length : 0;
        double sideY = length > Epsilon ? -dx / length : 0;
        for (int i = 0; i < SpriteSamples; i++)
        {
            double offset = halfWidth * ((2.0 * i / (SpriteSamples - 1)) - 1);
            double px = x + (sideX * offset);
            double py = y + (sideY * offset);
            if (InCone(viewer, px, py) && Sees(viewer, px, py, bottom, top, null, region))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tests a wall, sampled along the part of it inside the cone, between its regions' lowest floor and highest ceiling.</summary>
    public bool IsWallVisible(Viewer viewer, Wall wall)
    {
        ArgumentNullException.ThrowIfNull(wall);
        if (!TryGetSegment(_level, wall, out double ax, out double ay, out double bx, out double by) ||
            !TryClipToCone(viewer, ax, ay, bx, by, out double u0, out double u1))
        {
            return false;
        }

        (double bottom, double top) = WallSpan(_level, wall);
        double length = Math.Sqrt(((bx - ax) * (bx - ax)) + ((by - ay) * (by - ay))) * (u1 - u0);
        int samples = Math.Clamp((int)Math.Ceiling(length), 2, MaxWallSamples);
        for (int i = 0; i < samples; i++)
        {
            double u = u0 + ((u1 - u0) * i / (samples - 1));
            double px = ax + ((bx - ax) * u);
            double py = ay + ((by - ay) * u);
            if (Sees(viewer, px, py, bottom, top, wall, -1))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tests a region, visible "as long as a floor, ceiling or wall of the region is visible" (p.53,
    /// OV-20261021-distance-refresh): its floor and ceiling are sampled along its border walls, the
    /// part of them inside the cone, and so are the faces of its border walls that are drawn (solid
    /// walls and steps, not <c>INVISIBLE</c> ones). Sampling the border finds the farthest, and so
    /// the highest on the screen, points of a convex floor; a region the viewer stands in is not
    /// handled here.
    /// </summary>
    public bool IsRegionVisible(Viewer viewer, int region)
    {
        if (!TryGetRegion(_level, region, out Region? surfaces) || !_segmentsOfRegion.TryGetValue(region, out List<int>? border))
        {
            return false;
        }

        foreach (int index in border)
        {
            Segment segment = _segments[index];
            Wall wall = segment.Wall;
            if (!TryClipToCone(viewer, segment.Ax, segment.Ay, segment.Bx, segment.By, out double u0, out double u1))
            {
                continue;
            }

            if (wall["INVISIBLE"] <= 0 && HasFace(_level, wall) && IsWallVisible(viewer, wall))
            {
                return true;
            }

            double length = Math.Sqrt(((segment.Bx - segment.Ax) * (segment.Bx - segment.Ax)) + ((segment.By - segment.Ay) * (segment.By - segment.Ay))) * (u1 - u0);
            int samples = Math.Clamp((int)Math.Ceiling(length), 2, MaxWallSamples);
            for (int i = 0; i < samples; i++)
            {
                double u = u0 + ((u1 - u0) * i / (samples - 1));
                double px = segment.Ax + ((segment.Bx - segment.Ax) * u);
                double py = segment.Ay + ((segment.By - segment.Ay) * u);
                if (IsSurfaceVisible(viewer, px, py, surfaces.FloorHeight, surfaces.FloorHeight + SurfaceBand, wall) ||
                    IsSurfaceVisible(viewer, px, py, surfaces.CeilHeight - SurfaceBand, surfaces.CeilHeight, wall))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsSurfaceVisible(Viewer viewer, double px, double py, double bottom, double top, Wall border) =>
        Sees(viewer, px, py, bottom, top, border, -1);

    /// <summary>Whether the eye sees some of heights <paramref name="bottom"/>…<paramref name="top"/> at the map point, on the screen.</summary>
    private bool Sees(Viewer viewer, double px, double py, double bottom, double top, Wall? target, int region)
    {
        if (!TryVisibleSpans(viewer, px, py, bottom, top, target, region))
        {
            return false;
        }

        foreach ((_, double low, double high) in _spans)
        {
            if (IntersectsScreen(viewer, px, py, low, high))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a wall shows a face: a solid wall, or a step in the floor or ceiling between its regions.</summary>
    private static bool HasFace(Level level, Wall wall) =>
        !TryGetRegion(level, wall.LeftRegion, out Region? left) ||
        !TryGetRegion(level, wall.RightRegion, out Region? right) ||
        wall.LeftRegion == wall.RightRegion ||
        left.FloorHeight != right.FloorHeight ||
        left.CeilHeight != right.CeilHeight;

    private void AddRegionSegment(int region, int segment)
    {
        if (!_segmentsOfRegion.TryGetValue(region, out List<int>? list))
        {
            _segmentsOfRegion[region] = list = [];
        }

        list.Add(segment);
    }

    /// <summary>Whether a map point lies in front of the viewer within the <c>PLAYER_ARC</c> cone.</summary>
    public static bool InCone(Viewer viewer, double x, double y)
    {
        double dx = x - viewer.X;
        double dy = y - viewer.Y;
        double half = viewer.Projection.HorizontalFieldOfView * 0.5;
        return LeftOfRightEdge(viewer, half, dx, dy) >= -Epsilon && RightOfLeftEdge(viewer, half, dx, dy) >= -Epsilon;
    }

    // The cone is the wedge between its right edge (angle − arc/2) and its left edge (angle + arc/2);
    // PLAYER_ARC never exceeds 2 radians, so the wedge is convex.
    private static double LeftOfRightEdge(Viewer viewer, double half, double dx, double dy)
    {
        double edge = viewer.Angle - half;
        return (Math.Cos(edge) * dy) - (Math.Sin(edge) * dx);
    }

    private static double RightOfLeftEdge(Viewer viewer, double half, double dx, double dy)
    {
        double edge = viewer.Angle + half;
        return (dx * Math.Sin(edge)) - (dy * Math.Cos(edge));
    }

    /// <summary>The part [<paramref name="u0"/>, <paramref name="u1"/>] of the segment A→B inside the cone.</summary>
    private static bool TryClipToCone(Viewer viewer, double ax, double ay, double bx, double by, out double u0, out double u1)
    {
        double half = viewer.Projection.HorizontalFieldOfView * 0.5;
        u0 = 0;
        u1 = 1;
        double sx = bx - ax;
        double sy = by - ay;
        double rx = ax - viewer.X;
        double ry = ay - viewer.Y;
        return Clip(LeftOfRightEdge(viewer, half, rx, ry), LeftOfRightEdge(viewer, half, rx + sx, ry + sy), ref u0, ref u1) &&
               Clip(RightOfLeftEdge(viewer, half, rx, ry), RightOfLeftEdge(viewer, half, rx + sx, ry + sy), ref u0, ref u1);

        // Keeps the u where the linear function from f0 (u = 0) to f1 (u = 1) is not negative.
        static bool Clip(double f0, double f1, ref double low, ref double high)
        {
            if (f0 < 0 && f1 < 0)
            {
                return false;
            }

            if (f0 < 0 || f1 < 0)
            {
                double cross = f0 / (f0 - f1);
                if (f0 < 0)
                {
                    low = Math.Max(low, cross);
                }
                else
                {
                    high = Math.Min(high, cross);
                }
            }

            return low <= high;
        }
    }

    /// <summary>
    /// The spans of heights between <paramref name="bottom"/> and <paramref name="top"/> at the map point
    /// that the eye sees past every wall the sight line crosses (in <see cref="_spans"/>), or
    /// <c>false</c> when none. The walls are taken in order from the eye. At each one the sight line
    /// must pass an opening between a storey on its near side and one on its far side. Across a
    /// <c>BELOW</c> stack's footprint it stays in one storey, so the slab between two storeys hides
    /// what lies beyond it. Without stacks this is one span narrowed by each wall's opening.
    /// </summary>
    private bool TryVisibleSpans(Viewer viewer, double px, double py, double bottom, double top, Wall? target, int region)
    {
        _spans.Clear();
        _crossings.Clear();
        if (_segments.Length > 0)
        {
            CollectCrossings(viewer, px, py, target);
        }

        double eye = viewer.EyeHeight;
        if (_crossings.Count == 0)
        {
            // The eye and the point share a footprint: in a stack, they share the eye's storey too.
            (double low, double high) = (bottom, top);
            if (region >= 0 && EyeStorey(region, eye) is { } storey)
            {
                low = Math.Max(low, _level.Regions[storey].FloorHeight);
                high = Math.Min(high, _level.Regions[storey].CeilHeight);
            }

            if (low >= high)
            {
                return false;
            }

            _spans.Add((-1, low, high));
            return true;
        }

        _crossings.Sort(static (a, b) => a.T.CompareTo(b.T));
        bool first = true;
        int lastFar = -1;
        foreach ((double t, int index) in _crossings)
        {
            Segment segment = _segments[index];
            Wall wall = segment.Wall;
            bool eyeOnLeft = (((segment.Bx - segment.Ax) * (viewer.Y - segment.Ay)) - ((segment.By - segment.Ay) * (viewer.X - segment.Ax))) > 0;
            int near = eyeOnLeft ? wall.LeftRegion : wall.RightRegion;
            int far = eyeOnLeft ? wall.RightRegion : wall.LeftRegion;
            if (wall.LeftRegion == wall.RightRegion ||
                !TryGetRegion(_level, near, out _) ||
                !TryGetRegion(_level, far, out _))
            {
                return false;
            }

            List<int> nearStack = [.. LevelTopology.Stack(_level, near)];
            List<int> farStack = [.. LevelTopology.Stack(_level, far)];
            if (first)
            {
                // The eye starts in the storey that holds it (any, when none does).
                int? eyeStorey = nearStack.Count > 1 ? EyeStorey(near, eye) : null;
                foreach (int storey in nearStack)
                {
                    if (eyeStorey is null || storey == eyeStorey)
                    {
                        _spans.Add((storey, bottom, top));
                    }
                }

                first = false;
            }
            else if (!_spans.Exists(span => nearStack.Contains(span.Storey)))
            {
                // A sight line that left one footprint and meets a wall of another: carry the spans on.
                List<(int, double, double)> carried = [.. _spans.SelectMany(span => nearStack.Select(storey => (storey, span.Low, span.High)))];
                _spans.Clear();
                _spans.AddRange(carried);
            }

            _nextSpans.Clear();
            foreach ((int storey, double low, double high) in _spans)
            {
                if (!nearStack.Contains(storey))
                {
                    continue;
                }

                Region from = _level.Regions[storey];
                foreach (int beyond in farStack)
                {
                    Region to = _level.Regions[beyond];
                    double floor = Math.Max(from.FloorHeight, to.FloorHeight);
                    double ceiling = Math.Min(from.CeilHeight, to.CeilHeight);
                    if (ceiling <= floor)
                    {
                        continue;
                    }

                    // The sight line to height h passes the wall at eye + (h − eye)·t, which must lie in the opening.
                    double nextLow = Math.Max(low, eye + ((floor - eye) / t));
                    double nextHigh = Math.Min(high, eye + ((ceiling - eye) / t));
                    if (nextLow < nextHigh)
                    {
                        _nextSpans.Add((beyond, nextLow, nextHigh));
                    }
                }
            }

            (_spans, _nextSpans) = (_nextSpans, _spans);
            if (_spans.Count == 0)
            {
                return false;
            }

            lastFar = far;
        }

        // In a stack, the point must lie in the storey the sight line ends in.
        if (lastFar >= 0 && _level.Regions[lastFar].BelowRegion >= 0)
        {
            _nextSpans.Clear();
            foreach ((int storey, double low, double high) in _spans)
            {
                double clippedLow = Math.Max(low, _level.Regions[storey].FloorHeight);
                double clippedHigh = Math.Min(high, _level.Regions[storey].CeilHeight);
                if (clippedLow < clippedHigh)
                {
                    _nextSpans.Add((storey, clippedLow, clippedHigh));
                }
            }

            (_spans, _nextSpans) = (_nextSpans, _spans);
        }

        return _spans.Count > 0;
    }

    /// <summary>The storey of <paramref name="region"/>'s stack that holds the eye, if it is a stack.</summary>
    private int? EyeStorey(int region, double eye)
    {
        int? found = null;
        int members = 0;
        foreach (int storey in LevelTopology.Stack(_level, region))
        {
            members++;
            Region r = _level.Regions[storey];
            if (found is null && r.FloorHeight <= eye && eye <= r.CeilHeight)
            {
                found = storey;
            }
        }

        return members > 1 ? found : null;
    }

    /// <summary>Collects the walls the sight line from the eye to P crosses into <see cref="_crossings"/>.</summary>
    private void CollectCrossings(Viewer viewer, double px, double py, Wall? target)
    {
        // Walk the grid cells the sight line passes (in grid units, t from 0 at the eye to 1 at P).
        _sightLine++;
        double x0 = (viewer.X - _gridX) / CellSize;
        double y0 = (viewer.Y - _gridY) / CellSize;
        double x1 = (px - _gridX) / CellSize;
        double y1 = (py - _gridY) / CellSize;
        int column = (int)Math.Floor(x0);
        int row = (int)Math.Floor(y0);
        int lastColumn = (int)Math.Floor(x1);
        int lastRow = (int)Math.Floor(y1);
        int stepColumn = Math.Sign(x1 - x0);
        int stepRow = Math.Sign(y1 - y0);
        double spanX = Math.Abs(x1 - x0);
        double spanY = Math.Abs(y1 - y0);
        double nextX = spanX > 0 ? (stepColumn > 0 ? column + 1 - x0 : x0 - column) / spanX : double.PositiveInfinity;
        double nextY = spanY > 0 ? (stepRow > 0 ? row + 1 - y0 : y0 - row) / spanY : double.PositiveInfinity;
        double deltaX = spanX > 0 ? 1 / spanX : double.PositiveInfinity;
        double deltaY = spanY > 0 ? 1 / spanY : double.PositiveInfinity;
        int remaining = Math.Abs(lastColumn - column) + Math.Abs(lastRow - row) + 1;
        while (remaining-- > 0)
        {
            // Outside the grid, the edge cells hold the walls that moved out of it (see Index).
            int cellIndex = (Math.Clamp(row, 0, _rows - 1) * _columns) + Math.Clamp(column, 0, _columns - 1);
            if (_cells[cellIndex] is { } cell)
            {
                foreach (int index in cell)
                {
                    if (_testedBy[index] == _sightLine)
                    {
                        continue;
                    }

                    _testedBy[index] = _sightLine;
                    Segment segment = _segments[index];
                    Wall wall = segment.Wall;
                    if (!ReferenceEquals(wall, target) &&
                        wall["INVISIBLE"] <= 0 &&
                        wall["TRANSPARENT"] <= 0 &&
                        TryCrossing(viewer.X, viewer.Y, px, py, segment.Ax, segment.Ay, segment.Bx, segment.By, out double t))
                    {
                        _crossings.Add((t, index));
                    }
                }
            }

            if (nextX < nextY)
            {
                column += stepColumn;
                nextX += deltaX;
            }
            else
            {
                row += stepRow;
                nextY += deltaY;
            }
        }
    }

    /// <summary>Whether heights <paramref name="low"/>…<paramref name="high"/> at the map point fall within the screen's vertical window.</summary>
    private static bool IntersectsScreen(Viewer viewer, double px, double py, double low, double high)
    {
        double depth = ((px - viewer.X) * Math.Cos(viewer.Angle)) + ((py - viewer.Y) * Math.Sin(viewer.Angle));
        if (depth <= Epsilon)
        {
            return false;
        }

        // A positive tilt moves the eye line up the screen (ProjectionModel.ShearNdc), showing more below it.
        double tangent = viewer.Projection.VerticalHalfTangent;
        double shear = 2 * viewer.Projection.PlayerTilt * tangent;
        double screenLow = viewer.EyeHeight + (depth * (-tangent - shear));
        double screenHigh = viewer.EyeHeight + (depth * (tangent - shear));
        return low < screenHigh && high > screenLow;
    }

    private static (double Bottom, double Top) WallSpan(Level level, Wall wall)
    {
        bool hasLeft = TryGetRegion(level, wall.LeftRegion, out Region? left);
        bool hasRight = TryGetRegion(level, wall.RightRegion, out Region? right);
        return (hasLeft, hasRight) switch
        {
            (true, true) => (Math.Min(left!.FloorHeight, right!.FloorHeight), Math.Max(left.CeilHeight, right.CeilHeight)),
            (true, false) => (left!.FloorHeight, left.CeilHeight),
            (false, true) => (right!.FloorHeight, right.CeilHeight),
            _ => (double.NegativeInfinity, double.PositiveInfinity),
        };
    }

    private static bool TryGetRegion(Level level, int index, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Region? region)
    {
        region = index >= 0 && index < level.Regions.Count ? level.Regions[index] : null;
        return region is not null;
    }

    private static bool TryGetSegment(Level level, Wall wall, out double ax, out double ay, out double bx, out double by)
    {
        if (wall.Vertex1 < 0 || wall.Vertex1 >= level.Vertices.Count ||
            wall.Vertex2 < 0 || wall.Vertex2 >= level.Vertices.Count)
        {
            ax = ay = bx = by = 0;
            return false;
        }

        Vertex a = level.Vertices[wall.Vertex1];
        Vertex b = level.Vertices[wall.Vertex2];
        ax = a.X;
        ay = a.Y;
        bx = b.X;
        by = b.Y;
        return true;
    }

    /// <summary>
    /// Where the sight line from E to P crosses the segment A→B, as a fraction <paramref name="t"/> of
    /// the way to P; crossings at the eye or at P itself do not count.
    /// </summary>
    private static bool TryCrossing(double ex, double ey, double px, double py, double ax, double ay, double bx, double by, out double t)
    {
        t = 0;
        double rx = px - ex;
        double ry = py - ey;
        double sx = bx - ax;
        double sy = by - ay;
        // A cheap reject before the exact test.
        if (Math.Max(ax, bx) < Math.Min(ex, px) || Math.Min(ax, bx) > Math.Max(ex, px) ||
            Math.Max(ay, by) < Math.Min(ey, py) || Math.Min(ay, by) > Math.Max(ey, py))
        {
            return false;
        }

        double denominator = (rx * sy) - (ry * sx);
        if (Math.Abs(denominator) < 1e-12)
        {
            return false;
        }

        double qx = ax - ex;
        double qy = ay - ey;
        t = ((qx * sy) - (qy * sx)) / denominator;
        double u = ((qx * ry) - (qy * rx)) / denominator;
        return t > Epsilon && t < 1 - Epsilon && u >= 0 && u <= 1;
    }
}
