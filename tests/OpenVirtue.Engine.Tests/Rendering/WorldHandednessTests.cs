// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

/// <summary>
/// Pushes <see cref="MeshBuilder"/> and <see cref="SpriteBuilder"/> output through the host's
/// view (a right-handed look-at along <see cref="RenderSpace.Forward"/>) and the measured
/// <see cref="ProjectionModel"/> exactly as the renderer does, and checks the screen side
/// against OV-20261009-tilt-model-shear-vs-pitch ("screen handedness": facing map +x, the
/// map's +y side is on the screen LEFT; the far wall's stripes read G R G R G R from map
/// y = +12 down to −12). ADR-0009 records the mapping that makes this hold.
/// </summary>
public sealed class WorldHandednessTests
{
    private const int Width = 640;
    private const int Height = 480;

    [Fact]
    public void Walls_AtMapPlusY_ProjectToTheLeftHalfWhenFacingPlusX()
    {
        // A wall 10 steps to the player's +y side, spanning x = 10..30 ahead of a player at the origin facing +x.
        Level level = LoadWithMap(
            "VERTEX 10 10 0;#0\nVERTEX 30 10 0;#1\nREGION room 0 16;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "MAPFILE <m.wmp>; BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { BMAPS m; } WALL w { TEXTURE wallTex; }");
        MeshBatch wall = Assert.Single(MeshBuilder.BuildWalls(level).Batches);
        Matrix4x4 viewProjection = ViewProjection(level, playerAngle: 0);

        foreach (RenderVertex vertex in wall.Vertices)
        {
            double column = ProjectColumn(new Vector3(vertex.X, vertex.Y, vertex.Z), viewProjection);
            Assert.True(column < Width / 2.0, $"map +y wall vertex landed at column {column:F1}, expected the left half");
        }
    }

    [Fact]
    public void Walls_AtMapMinusY_ProjectToTheRightHalfWhenFacingPlusX()
    {
        Level level = LoadWithMap(
            "VERTEX 10 -10 0;#0\nVERTEX 30 -10 0;#1\nREGION room 0 16;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "MAPFILE <m.wmp>; BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { BMAPS m; } WALL w { TEXTURE wallTex; }");
        MeshBatch wall = Assert.Single(MeshBuilder.BuildWalls(level).Batches);
        Matrix4x4 viewProjection = ViewProjection(level, playerAngle: 0);

        foreach (RenderVertex vertex in wall.Vertices)
        {
            double column = ProjectColumn(new Vector3(vertex.X, vertex.Y, vertex.Z), viewProjection);
            Assert.True(column > Width / 2.0, $"map −y wall vertex landed at column {column:F1}, expected the right half");
        }
    }

    [Fact]
    public void FarWallStripes_ReadInDescendingMapYFromLeftToRight()
    {
        // The tilt probe's far wall at D = 48: segment edges at map y = 12, 8, 4, 0, −4, −8, −12
        // appeared left→right in that order (world-y fit x = 319.6 − 12.2·y).
        Level level = LoadWithMap(
            "VERTEX 48 12 0;#0\nVERTEX 48 -12 0;#1\nREGION room 0 64;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "MAPFILE <m.wmp>;");
        Matrix4x4 viewProjection = ViewProjection(level, playerAngle: 0);
        ProjectionModel model = ProjectionModel.Default(Width, Height);

        double previous = double.NegativeInfinity;
        foreach (float mapY in new[] { 12f, 8f, 4f, 0f, -4f, -8f, -12f })
        {
            double column = ProjectColumn(RenderSpace.FromMap(48, 8, mapY), viewProjection);
            Assert.True(column > previous, $"edge at map y {mapY} (column {column:F1}) is not right of the previous edge");
            Assert.Equal(model.CenterColumn - (model.FocalLengthX * mapY / 48), column, 0.05);
            previous = column;
        }
    }

    [Fact]
    public void Sprite_AtMapMinusY_ProjectsToTheRightHalf_WherePositiveShootXAims()
    {
        // Same geometry as WdlRuntimeTests.Runtime_ShootInstruction_PositiveShootXAimsToScreenRight:
        // the player at (5, 5) facing +x, a THING at (9, 3), i.e. two steps toward map −y.
        Level level = LoadWithMap(
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nREGION room 0 10;#0\n" +
            "THING rightTarget 9 3 0 0;#1\nPLAYER_START 5 5 0 0;#2",
            "MAPFILE <m.wmp>; BMAP targetMap,<target.pcx>,0,0,2,6; " +
            "TEXTURE targetTex { BMAPS targetMap; SCALE_X 1; SCALE_Y 1; } " +
            "THING rightTarget { TEXTURE targetTex; }");
        SpriteBillboard sprite = Assert.Single(SpriteBuilder.Build(level));
        Matrix4x4 viewProjection = ViewProjection(level, playerAngle: 0);

        Assert.Equal(RenderSpace.FromMap(9, 0, 3), sprite.BasePosition);
        double column = ProjectColumn(sprite.BasePosition + new Vector3(0, sprite.HalfHeight, 0), viewProjection);
        Assert.True(column > Width / 2.0, $"the target the hitscan hits with SHOOT_X > 0 landed at column {column:F1}");
    }

    [Fact]
    public void Sprite_AtMapPlusY_ProjectsToTheLeftHalf()
    {
        Level level = LoadWithMap(
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nREGION room 0 10;#0\n" +
            "THING leftTarget 20 5 0 0;#1\nPLAYER_START 0 0 0 0;#2",
            "MAPFILE <m.wmp>; BMAP targetMap,<target.pcx>,0,0,2,6; " +
            "TEXTURE targetTex { BMAPS targetMap; SCALE_X 1; SCALE_Y 1; } " +
            "THING leftTarget { TEXTURE targetTex; }");
        SpriteBillboard sprite = Assert.Single(SpriteBuilder.Build(level));
        Matrix4x4 viewProjection = ViewProjection(level, playerAngle: 0);

        double column = ProjectColumn(sprite.BasePosition + new Vector3(0, sprite.HalfHeight, 0), viewProjection);
        Assert.True(column < Width / 2.0, $"map +y sprite landed at column {column:F1}, expected the left half");
    }

    [Fact]
    public void Wall_TextureUStartsAtTheSecondVertex_AsTheOriginalShowsFromTheLeftRegion()
    {
        // OV-20261009-shoot-mapping: the far-wall face bitmap (left half yellow) showed its
        // yellow half on the screen LEFT, i.e. at the wall's second vertex (map y = +8) for the
        // player in the wall's left region. So u = 0 at v2 and u grows toward v1.
        Level level = LoadWithMap(
            "VERTEX 48 -8 0;#0\nVERTEX 48 8 0;#1\nREGION room 0 16;#0\n" +
            "WALL face 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "MAPFILE <m.wmp>; BMAP m,<t.pcx>,0,0,64,64; TEXTURE faceTex { BMAPS m; SCALE_X 4; SCALE_Y 4; } " +
            "WALL face { TEXTURE faceTex; }");

        MeshBatch wall = Assert.Single(MeshBuilder.BuildWalls(level).Batches);

        RenderVertex atV1 = Assert.Single(wall.Vertices.Distinct(), v => v.Z == RenderSpace.ZFromMapY(-8) && v.Y == 0);
        RenderVertex atV2 = Assert.Single(wall.Vertices.Distinct(), v => v.Z == RenderSpace.ZFromMapY(8) && v.Y == 0);
        Assert.Equal(0f, atV2.U, 1e-6f);
        Assert.Equal(1f, atV1.U, 1e-6f); // 16 steps × (4 px / 64 px) = one repeat

        // And on screen v2 is on the left (map +y), so the bitmap's u = 0 edge is drawn on the left.
        Matrix4x4 viewProjection = ViewProjection(level, playerAngle: 0);
        Assert.True(ProjectColumn(new Vector3(atV2.X, 8, atV2.Z), viewProjection) < ProjectColumn(new Vector3(atV1.X, 8, atV1.Z), viewProjection));
    }

    [Fact]
    public void FloorSurface_KeepsMapTextureAxes_WhileDepthIsNegatedMapY()
    {
        // No deliberate texture flip: v still follows map y; only the position's z is negated.
        Level level = LoadWithMap(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nVERTEX 4 4 0;#2\nVERTEX 0 4 0;#3\n" +
            "REGION room 0 10;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\nWALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\n" +
            "PLAYER_START 1 1 0 0;#4",
            "MAPFILE <m.wmp>; BMAP m,<t.pcx>,0,0,64,64; TEXTURE floorTex { BMAPS m; SCALE_X 4; SCALE_Y 4; } " +
            "REGION room { FLOOR_TEX floorTex; }");

        LevelMesh mesh = MeshBuilder.Build(level);

        MeshBatch floor = Assert.Single(mesh.Batches, batch => batch.Vertices.All(v => v.Y == 0));
        RenderVertex farCorner = Assert.Single(floor.Vertices.Distinct(), v => v.X == 4 && v.Z == -4);
        Assert.Equal(0.25f, farCorner.U, 1e-6f); // 4 steps × (SCALE 4 / 64 px) of a repeat along map x
        Assert.Equal(0.25f, farCorner.V, 1e-6f); // and the same positive amount along map y (v is not negated)
        Assert.DoesNotContain(floor.Vertices, v => v.Z > 0);
    }

    [Fact]
    public void Runtime_SeedsPlayerAngleFromTheWmpStart_SoTheStartSyncReportsIt()
    {
        // PLAYER_START x y a r with a = 0 faces map +x and dumps ANG=0.000 (every OV-20261009 probe);
        // 90 is a quarter turn counter-clockwise like the THING/ACTOR placement angle.
        Level east = LoadWithMap(
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nREGION room 0 10;#0\nPLAYER_START 5 5 0 0;#1",
            "MAPFILE <m.wmp>;");
        Level north = LoadWithMap(
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nREGION room 0 10;#0\nPLAYER_START 5 5 90 0;#1",
            "MAPFILE <m.wmp>;");

        var eastRuntime = new WdlRuntime(east);
        var northRuntime = new WdlRuntime(north);

        Assert.Equal(0, eastRuntime.GetSkill("PLAYER_ANGLE"));
        Assert.Equal(1, eastRuntime.GetSkill("PLAYER_COS"));
        Assert.Equal(Math.PI / 2, northRuntime.GetSkill("PLAYER_ANGLE"), 1e-12);
        Assert.Equal(1, northRuntime.GetSkill("PLAYER_SIN"), 1e-12);

        // The host's start sync goes WMP angle → yaw → PLAYER_ANGLE and must land on the same value.
        double yaw = RenderSpace.YawFromPlayerAngle(north.PlayerStart!.Value.AngleRadians);
        Assert.Equal(Math.PI / 2, RenderSpace.PlayerAngleFromYaw(yaw), 1e-12);
        Assert.Equal(0, RenderSpace.PlayerAngleFromYaw(RenderSpace.YawFromPlayerAngle(east.PlayerStart!.Value.AngleRadians)), 1e-12);
    }

    /// <summary>The renderer's view × projection for a player at the map start facing <paramref name="playerAngle"/>.</summary>
    private static Matrix4x4 ViewProjection(Level level, double playerAngle)
    {
        var player = new Player(level);
        Vector3 position = RenderSpace.FromMap(player.Position);
        Vector3 forward = RenderSpace.ForwardFromPlayerAngle(playerAngle);
        return Matrix4x4.CreateLookAt(position, position + forward, Vector3.UnitY)
            * ProjectionModel.Default(Width, Height).CreateProjectionMatrix(1f, 200000f);
    }

    private static double ProjectColumn(Vector3 renderPosition, Matrix4x4 viewProjection)
    {
        Vector4 clip = Vector4.Transform(new Vector4(renderPosition, 1), viewProjection);
        Assert.True(clip.W > 0, "point must be in front of the eye");
        return ((clip.X / clip.W) + 1) * 0.5 * Width;
    }

    private static Level LoadWithMap(string map, string main)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        return LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }
}
