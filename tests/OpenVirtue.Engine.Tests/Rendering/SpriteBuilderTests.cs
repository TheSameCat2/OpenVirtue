// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

public class SpriteBuilderTests
{
    [Fact]
    public void Build_UsesTextureSizeAndRegionFloor()
    {
        Level level = Load(
            "THING plant 2 3 0 0;#1\nPLAYER_START 0 0 0 0;#2",
            """
            MAPFILE <m.wmp>;
            BMAP plant_map, <plant.pcx>, 0, 0, 50, 100;
            TEXTURE plantTex { BMAPS plant_map; }
            THING plant { TEXTURE plantTex; }
            """);

        SpriteBillboard sprite = Assert.Single(SpriteBuilder.Build(level));

        Assert.Equal("plantTex", sprite.Texture);
        Assert.Equal(new Vector3(2, 4, 3), sprite.BasePosition);
        Assert.Equal(1, sprite.HalfWidth);
        Assert.Equal(2, sprite.HalfHeight);
    }

    [Fact]
    public void Build_SkipsSpritesWithoutResolvedTextures()
    {
        Level level = Load(
            "THING plant 2 3 0 0;#1\nACTOR ghost 4 5 0 0;#2\nPLAYER_START 0 0 0 0;#3",
            """
            MAPFILE <m.wmp>;
            THING plant { TEXTURE missingTex; }
            """);

        Assert.Empty(SpriteBuilder.Build(level));
    }

    [Fact]
    public void OrderBackToFront_SortsByCameraDistance()
    {
        var near = new SpriteBillboard("a", new Vector3(0, 0, 4), 1, 1);
        var far = new SpriteBillboard("b", new Vector3(0, 0, 20), 1, 1);
        var middle = new SpriteBillboard("c", new Vector3(0, 0, 10), 1, 1);

        IReadOnlyList<SpriteBillboard> sorted = SpriteBuilder.OrderBackToFront([near, far, middle], Vector3.Zero);

        Assert.Equal(["b", "c", "a"], sorted.Select(s => s.Texture).ToArray());
    }

    private static Level Load(string placements, string main)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] =
                "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 4 12;#0\n" +
                placements,
        };
        return LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }
}
