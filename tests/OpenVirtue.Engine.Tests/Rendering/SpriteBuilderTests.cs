// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

public class SpriteBuilderTests
{
    [Fact]
    public void Build_UsesTextureScaleRegionFloorAndEntityVerticalOffset()
    {
        Level level = Load(
            "THING plant 2 3 0 0;#1\nPLAYER_START 0 0 0 0;#2",
            """
            MAPFILE <m.wmp>;
            BMAP plant_map, <plant.pcx>, 0, 0, 50, 100;
            TEXTURE plantTex { BMAPS plant_map; }
            THING plant { TEXTURE plantTex; }
            """);
        Assert.Single(level.Things).Z = 2;

        SpriteBillboard sprite = Assert.Single(SpriteBuilder.Build(level));

        Assert.Equal("plantTex", sprite.Texture);
        Assert.Equal(new Vector3(2, 6, -3), sprite.BasePosition); // render space: z = −map y (ADR-0009)
        Assert.Equal(1.5625f, sprite.HalfWidth);
        Assert.Equal(3.125f, sprite.HalfHeight);
        Assert.Same(Assert.Single(level.Things), sprite.Source);
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
    public void Build_GroundEntity_UsesAbsoluteHeightInsteadOfRegionFloorOffset()
    {
        Level level = Load(
            "THING plant 2 3 0 0;#1\nPLAYER_START 0 0 0 0;#2",
            """
            MAPFILE <m.wmp>;
            BMAP plant_map, <plant.pcx>, 0, 0, 16, 16;
            TEXTURE plantTex { BMAPS plant_map; }
            THING plant { TEXTURE plantTex; HEIGHT 2; FLAGS GROUND; }
            """);

        SpriteBillboard sprite = Assert.Single(SpriteBuilder.Build(level));

        Assert.Equal(2, sprite.BasePosition.Y);
    }

    [Fact]
    public void Build_EntityAndShadowFollowInterpolatedSlopedRegionFloor()
    {
        Level level = LoadMap(
            "VERTEX 0 0 0;#0\nVERTEX 10 0 10;#1\nVERTEX 10 10 10;#2\nVERTEX 0 10 0;#3\n" +
            "REGION room 4 12;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\n" +
            "WALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\n" +
            "THING plant 5 5 0 0;#4\nPLAYER_START 0 0 0 0;#5",
            "MAPFILE <m.wmp>; " +
            "BMAP plantMap,<plant.pcx>,0,0,16,32; BMAP shadowMap,<shadow.pcx>,0,0,16,16; " +
            "TEXTURE plantTex { BMAPS plantMap; } " +
            "TEXTURE shadowTex { BMAPS shadowMap; FLAGS SHADOW; } " +
            "REGION room { FLAGS FLOOR_ASCEND; } " +
            "THING plant { TEXTURE plantTex; ATTACH shadowTex; }");

        IReadOnlyList<SpriteBillboard> sprites = SpriteBuilder.Build(level);

        Assert.Equal(9, sprites.Single(sprite => !sprite.IsAttachment).BasePosition.Y, 5);
        Assert.Equal(9.01, sprites.Single(sprite => sprite.IsAttachment).BasePosition.Y, 5);
    }

    [Fact]
    public void Build_AnimatedDirectionalEntity_SelectsCycleAndViewerSide()
    {
        Level level = Load(
            "THING plant 2 3 0 0;#1\nPLAYER_START 0 0 0 0;#2",
            "MAPFILE <m.wmp>; " +
            "BMAP f1,<f1.pcx>,0,0,16,16; BMAP f2,<f2.pcx>,0,0,32,16; " +
            "BMAP b1,<b1.pcx>,0,0,24,16; " +
            "TEXTURE plantTex { SIDES 2; CYCLES 2; BMAPS f1,f2,b1,NULL; " +
            "MIRROR 0,1; OFFSET_X 0,16,32,0; OFFSET_Y 0,8,-16,0; } " +
            "THING plant { TEXTURE plantTex; CYCLE 2; }");

        SpriteBillboard front = Assert.Single(SpriteBuilder.Build(level, new Vector2(10, 3)));

        Assert.Equal(TextureAnimation.FrameKey("plantTex", 1), front.Texture);
        Assert.Equal(1.0f, front.HalfWidth);
        Assert.False(front.Mirror);
        Assert.Equal(new Vector2(1, -0.5f), front.CenterOffset);

        Assert.Empty(SpriteBuilder.Build(level, new Vector2(-10, 3)));

        Assert.Single(level.Things)["CYCLE"] = 1;
        SpriteBillboard back = Assert.Single(SpriteBuilder.Build(level, new Vector2(-10, 3)));
        Assert.Equal(TextureAnimation.FrameKey("plantTex", 2), back.Texture);
        Assert.Equal(0.75f, back.HalfWidth);
        Assert.True(back.Mirror);
        Assert.Equal(new Vector2(2, 1), back.CenterOffset);
    }

    [Fact]
    public void Build_AttachTexture_AddsLayerUsingItsOwnSizeAndBehindOrdering()
    {
        Level level = Load(
            "THING plant 2 3 0 0;#1\nPLAYER_START 0 0 0 0;#2",
            "MAPFILE <m.wmp>; " +
            "BMAP baseMap,<base.pcx>,0,0,16,32; BMAP glowMap,<glow.pcx>,0,0,32,16; " +
            "TEXTURE baseTex { BMAPS baseMap; } " +
            "TEXTURE glowTex { BMAPS glowMap; FLAGS BEHIND; } " +
            "TEXTURE frontGlowTex { BMAPS glowMap; } " +
            "THING plant { TEXTURE baseTex; ATTACH glowTex; }");

        IReadOnlyList<SpriteBillboard> sprites = SpriteBuilder.Build(level, new Vector2(10, 3));

        Assert.Equal(2, sprites.Count);
        Assert.Equal("baseTex", sprites[0].Texture);
        SpriteBillboard attachment = sprites[1];
        Assert.Equal("glowTex", attachment.Texture);
        Assert.Equal(1, attachment.HalfWidth);
        Assert.Equal(0.5f, attachment.HalfHeight);
        Assert.Equal(SpriteOrientation.Upright, attachment.Orientation);
        Assert.Equal(0.01f, attachment.CameraDepthOffset);
        Assert.True(attachment.IsAttachment);
        Assert.Same(level.Things[0], attachment.Source);
        Assert.Equal("baseTex", Assert.Single(SpriteBuilder.SelectClickableLayers(sprites)).Texture);

        level.Things[0].AttachTexture = "frontGlowTex";
        IReadOnlyList<SpriteBillboard> frontLayers = SpriteBuilder.Build(level, new Vector2(10, 3));
        Assert.Equal("frontGlowTex", Assert.Single(SpriteBuilder.SelectClickableLayers(frontLayers)).Texture);

        level.Things[0]["IMMATERIAL"] = 1;
        Assert.Empty(SpriteBuilder.SelectClickableLayers(frontLayers));
    }

    [Fact]
    public void Build_ShadowAttachTexture_LiesOnRegionFloor()
    {
        Level level = Load(
            "ACTOR guard 2 3 0 0;#1\nPLAYER_START 0 0 0 0;#2",
            "MAPFILE <m.wmp>; " +
            "BMAP actorMap,<actor.pcx>,0,0,16,32; BMAP shadowMap,<shadow.pcx>,0,0,32,16; " +
            "TEXTURE actorTex { BMAPS actorMap; } " +
            "TEXTURE shadowTex { BMAPS shadowMap; FLAGS SHADOW,BEHIND; } " +
            "ACTOR guard { TEXTURE actorTex; ATTACH shadowTex; HEIGHT 2; }");

        SpriteBillboard shadow = Assert.Single(
            SpriteBuilder.Build(level),
            sprite => sprite.Texture == "shadowTex");

        Assert.Equal(SpriteOrientation.Ground, shadow.Orientation);
        Assert.Equal(4.01f, shadow.BasePosition.Y);
        Assert.Equal(0, shadow.CameraDepthOffset);
        Assert.Equal("actorTex", Assert.Single(
            SpriteBuilder.SelectClickableLayers(SpriteBuilder.Build(level))).Texture);
    }

    [Fact]
    public void IsOpaque_UsesAlphaAndMirroredHorizontalCoordinate()
    {
        var image = new TextureImage(2, 1, [10, 20, 30, 0, 40, 50, 60, 255]);

        Assert.False(SpriteBuilder.IsOpaque(image, 0.25f, 0.5f));
        Assert.True(SpriteBuilder.IsOpaque(image, 0.75f, 0.5f));
        Assert.True(SpriteBuilder.IsOpaque(image, 0.25f, 0.5f, mirror: true));
    }

    [Fact]
    public void TrySamplePixel_ReturnsMirroredPixelCoordinatesAndAlpha()
    {
        var image = new TextureImage(2, 1, [10, 20, 30, 0, 40, 50, 60, 255]);

        Assert.True(SpriteBuilder.TrySamplePixel(
            image, 0.25f, 0.5f, mirror: true, out int x, out int y, out bool opaque));

        Assert.Equal(1, x);
        Assert.Equal(0, y);
        Assert.True(opaque);
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
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 4 12;#0\n" + placements,
        };
        return LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }

    private static Level LoadMap(string map, string main)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        return LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }
}
