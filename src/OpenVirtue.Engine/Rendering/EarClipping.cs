// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;

namespace OpenVirtue.Engine.Rendering;

/// <summary>
/// Ear-clipping triangulation for simple polygons (convex or concave, no holes).
/// Used to fill region floor/ceiling surfaces from their boundary loop.
/// </summary>
public static class EarClipping
{
    /// <summary>
    /// Triangulates a simple polygon, returning triangle index triples into
    /// <paramref name="polygon"/>. Returns an empty list if it cannot be triangulated.
    /// </summary>
    public static IReadOnlyList<int> Triangulate(IReadOnlyList<Vector2> polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        int n = polygon.Count;
        var triangles = new List<int>();
        if (n < 3)
        {
            return triangles;
        }

        // Work on a mutable index ring, oriented counter-clockwise.
        var ring = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            ring.Add(i);
        }

        if (SignedArea(polygon) < 0)
        {
            ring.Reverse();
        }

        int guard = n * n;
        while (ring.Count > 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < ring.Count; i++)
            {
                int previous = ring[(i - 1 + ring.Count) % ring.Count];
                int current = ring[i];
                int next = ring[(i + 1) % ring.Count];

                Vector2 a = polygon[previous];
                Vector2 b = polygon[current];
                Vector2 c = polygon[next];

                // Convex corner (CCW) and no other ring vertex inside the candidate ear.
                if (Cross(b - a, c - b) <= 0)
                {
                    continue;
                }

                bool containsVertex = false;
                foreach (int j in ring)
                {
                    if (j != previous && j != current && j != next && PointInTriangle(polygon[j], a, b, c))
                    {
                        containsVertex = true;
                        break;
                    }
                }

                if (containsVertex)
                {
                    continue;
                }

                triangles.Add(previous);
                triangles.Add(current);
                triangles.Add(next);
                ring.RemoveAt(i);
                clipped = true;
                break;
            }

            if (!clipped)
            {
                return []; // not a simple polygon we can handle — caller skips it
            }
        }

        if (ring.Count == 3)
        {
            triangles.Add(ring[0]);
            triangles.Add(ring[1]);
            triangles.Add(ring[2]);
        }

        return triangles;
    }

    private static float SignedArea(IReadOnlyList<Vector2> polygon)
    {
        float area = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 u = polygon[i];
            Vector2 v = polygon[(i + 1) % polygon.Count];
            area += (u.X * v.Y) - (v.X * u.Y);
        }

        return area * 0.5f;
    }

    private static float Cross(Vector2 u, Vector2 v) => (u.X * v.Y) - (u.Y * v.X);

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = EdgeSign(p, a, b);
        float d2 = EdgeSign(p, b, c);
        float d3 = EdgeSign(p, c, a);
        bool hasNegative = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPositive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNegative && hasPositive);
    }

    private static float EdgeSign(Vector2 p, Vector2 a, Vector2 b) =>
        ((p.X - b.X) * (a.Y - b.Y)) - ((a.X - b.X) * (p.Y - b.Y));
}
