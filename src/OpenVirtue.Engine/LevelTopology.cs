// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine;

/// <summary>Small topology queries over a loaded level's sector geometry.</summary>
public static class LevelTopology
{
    /// <summary>Finds the first region whose traced wall loop contains the given map-space point.</summary>
    public static int? LocateRegion(Level level, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(level);
        RegionSurfaceCache cache = SurfaceCache(level);
        for (int regionIndex = 0; regionIndex < level.Regions.Count; regionIndex++)
        {
            List<int>? loop = cache.Loop(regionIndex);
            if (loop is not null && ContainsPoint(level, loop, x, y))
            {
                return regionIndex;
            }
        }

        return null;
    }

    /// <summary>
    /// Interpolates the WMP vertex-height offset of a region surface at a map-space point.
    /// The triangulation matches the rendered floor and ceiling surface.
    /// </summary>
    public static bool TryGetSurfaceHeightOffset(Level level, int regionIndex, double x, double y, out double offset)
    {
        ArgumentNullException.ThrowIfNull(level);
        offset = 0;
        RegionSurfaceCache cache = SurfaceCache(level);
        List<int>? loop = cache.Loop(regionIndex);
        if (loop is null)
        {
            return false;
        }

        IReadOnlyList<int> triangles = cache.Triangles(regionIndex, loop);
        return TryGetSurfaceHeightOffset(level, loop, triangles, x, y, out offset);
    }

    internal static bool TryGetSurfaceHeightOffset(
        Level level,
        IReadOnlyList<int> loop,
        IReadOnlyList<int> triangles,
        double x,
        double y,
        out double offset)
    {
        offset = 0;
        for (int i = 0; i < triangles.Count; i += 3)
        {
            int ia = triangles[i];
            int ib = triangles[i + 1];
            int ic = triangles[i + 2];
            Vertex va = level.Vertices[loop[ia]];
            Vertex vb = level.Vertices[loop[ib]];
            Vertex vc = level.Vertices[loop[ic]];
            if (!TryBarycentric(
                    new Vector2(va.X, va.Y),
                    new Vector2(vb.X, vb.Y),
                    new Vector2(vc.X, vc.Y),
                    x,
                    y,
                    out double a,
                    out double b,
                    out double c))
            {
                continue;
            }

            offset = (a * va.Z) + (b * vb.Z) + (c * vc.Z);
            return true;
        }

        return false;
    }

    /// <summary>Traces the boundary of a region into a single ordered vertex loop, if possible.</summary>
    public static List<int>? TraceRegionLoop(Level level, int regionIndex)
    {
        ArgumentNullException.ThrowIfNull(level);

        var adjacency = new Dictionary<int, List<int>>();
        var edges = new HashSet<(int, int)>();

        foreach (Wall wall in level.Walls)
        {
            if ((wall.LeftRegion != regionIndex && wall.RightRegion != regionIndex) || wall.Vertex1 == wall.Vertex2)
            {
                continue;
            }

            (int, int) key = wall.Vertex1 < wall.Vertex2 ? (wall.Vertex1, wall.Vertex2) : (wall.Vertex2, wall.Vertex1);
            if (!edges.Add(key))
            {
                continue;
            }

            Connect(adjacency, wall.Vertex1, wall.Vertex2);
            Connect(adjacency, wall.Vertex2, wall.Vertex1);
        }

        if (edges.Count < 3)
        {
            return null;
        }

        foreach (List<int> neighbours in adjacency.Values)
        {
            if (neighbours.Count != 2)
            {
                return null;
            }
        }

        int start = edges.First().Item1;
        var loop = new List<int> { start };
        int previous = -1;
        int current = start;
        while (true)
        {
            List<int> neighbours = adjacency[current];
            int next = neighbours[0] != previous ? neighbours[0] : neighbours[1];
            if (next == start)
            {
                break;
            }

            loop.Add(next);
            if (loop.Count > edges.Count)
            {
                return null;
            }

            previous = current;
            current = next;
        }

        return loop.Count == edges.Count ? loop : null;
    }

    private static bool ContainsPoint(Level level, List<int> loop, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            Vertex a = level.Vertices[loop[i]];
            Vertex b = level.Vertices[loop[j]];
            if (PointOnSegment(x, y, a.X, a.Y, b.X, b.Y))
            {
                return true;
            }

            if (((a.Y > y) != (b.Y > y)) &&
                x < (((b.X - a.X) * (y - a.Y)) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static bool PointOnSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        const double Epsilon = 1e-6;
        double cross = ((px - ax) * (by - ay)) - ((py - ay) * (bx - ax));
        if (Math.Abs(cross) > Epsilon)
        {
            return false;
        }

        double dot = ((px - ax) * (bx - ax)) + ((py - ay) * (by - ay));
        if (dot < -Epsilon)
        {
            return false;
        }

        double lengthSquared = ((bx - ax) * (bx - ax)) + ((by - ay) * (by - ay));
        return dot <= lengthSquared + Epsilon;
    }

    private static bool TryBarycentric(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        double x,
        double y,
        out double weightA,
        out double weightB,
        out double weightC)
    {
        const double Epsilon = 1e-6;
        double denominator = ((b.Y - c.Y) * (a.X - c.X)) + ((c.X - b.X) * (a.Y - c.Y));
        if (Math.Abs(denominator) <= Epsilon)
        {
            weightA = weightB = weightC = 0;
            return false;
        }

        weightA = (((b.Y - c.Y) * (x - c.X)) + ((c.X - b.X) * (y - c.Y))) / denominator;
        weightB = (((c.Y - a.Y) * (x - c.X)) + ((a.X - c.X) * (y - c.Y))) / denominator;
        weightC = 1 - weightA - weightB;
        return weightA >= -Epsilon && weightB >= -Epsilon && weightC >= -Epsilon;
    }

    private static RegionSurfaceCache SurfaceCache(Level level) =>
        level.SurfaceCache ??= new RegionSurfaceCache(level);

    /// <summary>
    /// Region loops and floor triangulations of one level, so per-frame and per-tick queries do
    /// not rescan every wall. A loop depends only on which walls bound a region, and that never
    /// changes after load. A triangulation also depends on vertex x/y, which SHIFT and ROTATE
    /// move, so each lookup compares the cached footprint with the live vertices and
    /// triangulates again when one has moved. Vertex heights are always read live.
    /// </summary>
    internal sealed class RegionSurfaceCache(Level level)
    {
        private readonly bool[] _traced = new bool[level.Regions.Count];
        private readonly List<int>?[] _loops = new List<int>?[level.Regions.Count];
        private readonly Vector2[]?[] _footprints = new Vector2[]?[level.Regions.Count];
        private readonly IReadOnlyList<int>?[] _triangles = new IReadOnlyList<int>?[level.Regions.Count];

        /// <summary>The traced loop, shared between callers; callers do not modify it.</summary>
        public List<int>? Loop(int regionIndex)
        {
            if ((uint)regionIndex >= (uint)_loops.Length)
            {
                return TraceRegionLoop(level, regionIndex);
            }

            if (!_traced[regionIndex])
            {
                _loops[regionIndex] = TraceRegionLoop(level, regionIndex);
                _traced[regionIndex] = true;
            }

            return _loops[regionIndex];
        }

        public IReadOnlyList<int> Triangles(int regionIndex, List<int> loop)
        {
            Vector2[]? footprint = _footprints[regionIndex];
            if (footprint is null || !MatchesVertices(footprint, loop))
            {
                footprint = new Vector2[loop.Count];
                for (int i = 0; i < loop.Count; i++)
                {
                    Vertex vertex = level.Vertices[loop[i]];
                    footprint[i] = new Vector2(vertex.X, vertex.Y);
                }

                _footprints[regionIndex] = footprint;
                _triangles[regionIndex] = EarClipping.Triangulate(footprint);
            }

            return _triangles[regionIndex]!;
        }

        private bool MatchesVertices(Vector2[] footprint, List<int> loop)
        {
            for (int i = 0; i < footprint.Length; i++)
            {
                Vertex vertex = level.Vertices[loop[i]];
                if (footprint[i].X != vertex.X || footprint[i].Y != vertex.Y)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private static void Connect(Dictionary<int, List<int>> adjacency, int from, int to)
    {
        if (!adjacency.TryGetValue(from, out List<int>? list))
        {
            list = [];
            adjacency[from] = list;
        }

        list.Add(to);
    }
}
