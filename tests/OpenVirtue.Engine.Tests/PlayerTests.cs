// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Map;

namespace OpenVirtue.Engine.Tests;

public class PlayerTests
{
    // Region 0 (a) borders region 1 (b, a portal) along y=0, and region 2 (solid, no headroom)
    // along x=0. Player spawns in region 0 at (5, -5). The first region of a WMP wall is left of
    // v1→v2 (y-up), so the portal runs from (10, 0) to (0, 0).
    private const string Map =
        "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nVERTEX 0 -20 0;#2\n" +
        "REGION a 0 20;#0\nREGION b 0 20;#1\nREGION solid 0 0;#2\n" +
        "WALL portal 1 0 0 1 0 0;#0\nWALL solidw 0 2 0 2 0 0;#1\n" +
        "PLAYER_START 5 -5 0 0;#3";

    [Fact]
    public void MoveHorizontal_ThroughPortal_ChangesRegion()
    {
        var player = new Player(Load());
        Assert.Equal(0, player.Region);

        player.MoveHorizontal(0, 10); // cross y=0 into region b

        Assert.Equal(1, player.Region);
        Assert.Equal(5f, player.Position.Z, 3);
    }

    [Fact]
    public void TakeTouchedWalls_ReportsWallsPassedAndPushedAgainstButNotBeams()
    {
        // OV-20261019-wall-pass-clip: a wall's IF_NEAR fires when the player's movement passes
        // through it or pushes against it; a beam touches nothing.
        Level level = Load();
        var player = new Player(level);

        player.MoveHorizontal(-6, 0); // into the solid region's wall at x = 0
        Assert.Equal([level.Walls[1]], player.TakeTouchedWalls());
        Assert.Empty(player.TakeTouchedWalls());

        player.MoveHorizontal(0, 10); // through the portal at y = 0
        Assert.Equal([level.Walls[0]], player.TakeTouchedWalls());

        player.MoveTo(0, 5, -5); // a beam back across the portal
        Assert.Empty(player.TakeTouchedWalls());
    }

    [Fact]
    public void MoveHorizontal_AfterBeamWithoutLocate_KeepsStaleRegionUntilCrossingOntoTheOtherSide()
    {
        // A room (0) with a pad island (1) at x 10…20, y 10…20; both outlines counter-clockwise,
        // so each region is left of its walls (OV-20261019-proximity-events, region-beam-walk).
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 40 0 0;#1\nVERTEX 40 40 0;#2\nVERTEX 0 40 0;#3\n" +
            "VERTEX 10 10 0;#4\nVERTEX 20 10 0;#5\nVERTEX 20 20 0;#6\nVERTEX 10 20 0;#7\n" +
            "REGION room 0 20;#0\nREGION pad 0 20;#1\n" +
            "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\nWALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\n" +
            "WALL p 4 5 1 0 0 0;#4\nWALL p 5 6 1 0 0 0;#5\nWALL p 6 7 1 0 0 0;#6\nWALL p 7 4 1 0 0 0;#7\n" +
            "PLAYER_START 5 5 0 0;#8";
        var player = new Player(Load(map));
        player.MoveTo(0, 15, 15); // beamed onto the pad, region still the room

        player.MoveHorizontal(8, 0); // out across x = 20, onto the room's side
        Assert.Equal(0, player.Region);

        player.MoveHorizontal(-8, 0); // back across x = 20, onto the pad's side
        Assert.Equal(1, player.Region);
        Assert.Equal(15f, player.Position.X, 3);
    }

    [Fact]
    public void MoveHorizontal_DownTallLedge_CrossesPortalWithoutReverseCollision()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\n" +
            "REGION upper 8 20;#0\nREGION lower 0 20;#1\n" +
            "WALL ledge 0 1 0 1 0 0;#0\n" +
            "PLAYER_START 5 -1 0 0;#1";
        var player = new Player(Load(map));

        Vector2 displacement = player.MoveHorizontal(0, 2);

        Assert.Equal(1, player.Region);
        Assert.True(displacement.Y > 0);
        Assert.True(player.Position.Z > 0);
    }

    [Fact]
    public void MoveHorizontal_UpTallLedge_RemainsBlocked()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\n" +
            "REGION upper 8 20;#0\nREGION lower 0 20;#1\n" +
            "WALL ledge 0 1 0 1 0 0;#0\n" +
            "PLAYER_START 5 1 0 1;#1";
        var player = new Player(Load(map));

        player.MoveHorizontal(0, -2);

        Assert.Equal(1, player.Region);
        Assert.Equal(player.Width, player.Position.Z, 3);
    }

    [Fact]
    public void MoveHorizontal_IntoSolidWall_StopsAtPlayerWidth()
    {
        var player = new Player(Load());

        player.MoveHorizontal(-10, 0); // toward the solid region across x=0

        Assert.Equal(0, player.Region);
        Assert.Equal(player.Width, player.Position.X, 3);
    }

    [Fact]
    public void MoveHorizontal_DiagonallyIntoSolidWall_SlidesAlongWall()
    {
        var player = new Player(Load());

        Vector2 displacement = player.MoveHorizontal(-10, 4);

        Assert.Equal(player.Width, player.Position.X, 3);
        Assert.Equal(-1, player.Position.Z, 3);
        Assert.True(displacement.Y > 3.9f);
    }

    [Fact]
    public void MoveHorizontal_ApproachingSolidWallWithoutCrossing_MaintainsPlayerWidth()
    {
        var player = new Player(Load());

        player.MoveHorizontal(-4.5f, 0);

        Assert.Equal(player.Width, player.Position.X, 3);
    }

    [Fact]
    public void MoveHorizontal_InteriorWallBecomesCrossableWhenPassableChangesAtRuntime()
    {
        const string map =
            "VERTEX 0 -20 0;#0\nVERTEX 0 20 0;#1\n" +
            "REGION room 0 20;#0\nWALL divider 0 1 0 0 0 0;#0\n" +
            "PLAYER_START 5 0 0 0;#1";
        Level level = Load(map);
        var blocked = new Player(level);

        blocked.MoveHorizontal(-10, 0);

        Assert.Equal(blocked.Width, blocked.Position.X, 3);

        level.Walls[0]["passable"] = 1;
        var passable = new Player(level);
        passable.MoveHorizontal(-10, 0);

        Assert.Equal(-5, passable.Position.X, 3);
        Assert.Equal(0, passable.Region);
    }

    [Fact]
    public void MoveHorizontal_ThingCollisionUsesTextureScaleAndLivePassableFlag()
    {
        const string map =
            "VERTEX -20 -20 0;#0\nVERTEX 20 -20 0;#1\n" +
            "REGION room 0 20;#0\nTHING crate 0 0 0 0;#0\n" +
            "PLAYER_START 5 0 0 0;#1";
        const string main =
            "MAPFILE <m.wmp>; " +
            "BMAP crateMap, <crate.pcx>, 0, 0, 32, 16; " +
            "TEXTURE crateTex { BMAPS crateMap; } " +
            "THING crate { TEXTURE crateTex; }";
        Level level = Load(map, main);
        Thing thing = Assert.Single(level.Things);
        Assert.Equal("crateTex", thing.Texture);
        Assert.True(MapEntitySizing.TryGetSize(level, thing, out MapEntitySize size));
        Assert.Equal(2, size.Width);
        Assert.Equal(0, thing["passable"]);
        Assert.Equal(0, thing["invisible"]);
        var blocked = new Player(level);

        blocked.MoveHorizontal(-10, 0);

        Assert.Equal(2.2f, blocked.Position.X, 3);

        level.Things[0]["passable"] = 1;
        var passable = new Player(level);
        passable.MoveHorizontal(-10, 0);

        Assert.Equal(-5, passable.Position.X, 3);
    }

    [Fact]
    public void Tick_AppliesGravityAndLandsOnFloor()
    {
        var player = new Player(Load());
        float groundY = player.Position.Y; // spawned on the floor (eye height)
        Vector3 position = player.Position;
        position.Y = groundY + 1f;         // nudge up
        player.Position = position;

        player.Tick();

        Assert.Equal(groundY, player.Position.Y); // fell back and landed
        Assert.True(player.Grounded);
    }

    [Fact]
    public void SurfaceHeights_SlopedRegionInterpolateVertexOffsetsAtPlayerPosition()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 10;#1\nVERTEX 10 10 10;#2\nVERTEX 0 10 0;#3\n" +
            "REGION ramp 2 12;#0\n" +
            "WALL a 0 1 0 -1 0 0;#0\nWALL b 1 2 0 -1 0 0;#1\n" +
            "WALL c 2 3 0 -1 0 0;#2\nWALL d 3 0 0 -1 0 0;#3\n" +
            "PLAYER_START 5 5 0 0;#4";
        var player = new Player(Load(map));

        Assert.Equal(7, player.FloorHeight, 3);
        Assert.Equal(17, player.CeilingHeight, 3);
        Assert.Equal(10, player.Position.Y, 3);

        player.MoveTo(0, 2, 5);

        Assert.Equal(4, player.FloorHeight, 3);
        Assert.Equal(14, player.CeilingHeight, 3);
        Assert.Equal(7, player.Position.Y, 3);
    }

    [Fact]
    public void Jump_RaisesThePlayer()
    {
        var player = new Player(Load());
        player.Tick(); // ensure grounded
        Assert.True(player.Grounded);

        player.Jump();
        player.Tick();

        Assert.False(player.Grounded);          // airborne after jumping
        Assert.True(player.VelocityY > 0 || player.Position.Y > 0);
    }

    [Fact]
    public void ReflectivePoseFields_UseMapAxisConvention()
    {
        var player = new Player(Load());
        player.MoveTo(0, 2, -3);

        Assert.Equal(2, player["x"]);
        Assert.Equal(-3, player["y"]);
        Assert.Equal(player.Position.Y, player["z"]);
        Assert.Equal(0, player["region"]);

        player["x"] = 4;
        player["y"] = 5;
        player["z"] = 6;
        player["region"] = 1;

        Assert.Equal(4, player.Position.X);
        Assert.Equal(5, player.Position.Z);
        Assert.Equal(6, player.Position.Y);
        Assert.Equal(1, player.Region);
    }

    private static Level Load(string map = Map, string main = "MAPFILE <m.wmp>;")
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        return LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }
}
