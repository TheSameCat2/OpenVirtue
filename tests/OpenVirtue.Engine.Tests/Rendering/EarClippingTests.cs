// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

public class EarClippingTests
{
    [Fact]
    public void Triangulate_Square_ProducesTwoTrianglesCoveringTheArea()
    {
        Vector2[] square = [new(0, 0), new(4, 0), new(4, 4), new(0, 4)];

        IReadOnlyList<int> indices = EarClipping.Triangulate(square);

        Assert.Equal(2 * 3, indices.Count);                 // n - 2 = 2 triangles
        Assert.Equal(16f, TriangulatedArea(square, indices), 3); // 4 x 4
    }

    [Fact]
    public void Triangulate_ConcaveLShape_PreservesArea()
    {
        // An L: a 4x4 square with a 2x2 bite taken out of the top-right (area 12).
        Vector2[] l =
        [
            new(0, 0), new(4, 0), new(4, 2), new(2, 2), new(2, 4), new(0, 4),
        ];

        IReadOnlyList<int> indices = EarClipping.Triangulate(l);

        Assert.Equal(4 * 3, indices.Count);                 // n - 2 = 4 triangles
        Assert.Equal(12f, TriangulatedArea(l, indices), 3);
    }

    [Fact]
    public void Triangulate_DegenerateInput_ReturnsEmpty()
    {
        Assert.Empty(EarClipping.Triangulate([new(0, 0), new(1, 1)]));
    }

    private static float TriangulatedArea(IReadOnlyList<Vector2> polygon, IReadOnlyList<int> indices)
    {
        float area = 0;
        for (int i = 0; i < indices.Count; i += 3)
        {
            Vector2 a = polygon[indices[i]];
            Vector2 b = polygon[indices[i + 1]];
            Vector2 c = polygon[indices[i + 2]];
            area += MathF.Abs((((b.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (b.Y - a.Y))) * 0.5f);
        }

        return area;
    }
}
