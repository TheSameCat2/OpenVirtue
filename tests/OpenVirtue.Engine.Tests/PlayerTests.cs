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
    public void MoveHorizontal_StepEndingJustShortOfPortalLine_StillCrossesOnTheNextStep()
    {
        // Eight steps of 8/14 from y = -4 put the seventh step within float rounding of the portal
        // line at y = 0; the move must still enter region b (OV-20261019-clip-contact walked the
        // player across region borders in steps like these).
        Level level = Load();
        var player = new Player(level);
        player.MoveTo(0, 5, -4);

        player.MoveHorizontal(0, 8);

        Assert.Equal(1, player.Region);
        Assert.Contains(level.Walls[0], player.TakeTouchedObjects());
    }

    [Fact]
    public void TakeTouchedObjects_ReportsWallsPassedAndPushedAgainstButNotBeams()
    {
        // OV-20261019-wall-pass-clip: a wall's IF_NEAR fires when the player's movement passes
        // through it or pushes against it; a beam touches nothing.
        Level level = Load();
        var player = new Player(level);

        player.MoveHorizontal(-6, 0); // into the solid region's wall at x = 0
        Assert.Equal<AcknexObject>([level.Walls[1]], player.TakeTouchedObjects());
        Assert.Empty(player.TakeTouchedObjects());

        player.MoveHorizontal(0, 10); // through the portal at y = 0
        Assert.Equal<AcknexObject>([level.Walls[0]], player.TakeTouchedObjects());

        player.MoveTo(0, 5, -5); // a beam back across the portal
        Assert.Empty(player.TakeTouchedObjects());
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

    [Theory]
    [InlineData(0.9f, 8f, 0)]
    [InlineData(1f, 8f, 0)] // a step equal to PLAYER_CLIMB blocks
    [InlineData(1.1f, 8f, 1)]
    [InlineData(0.5f, 9.5f, 1)] // the feet are 1.5 up: the step is taken from them, not the floor
    public void MoveHorizontal_IntoRegionBorder_EntersWhenTheFloorIsLessThanClimbAboveTheFeet(
        float climb, float eye, int expectedRegion)
    {
        // OV-20261019-border-climb, climb-1 and edge-1: a pad with its floor at 1, PLAYER_SIZE 8.
        (Player player, _) = PadPlayer(padFloor: 1, padCeiling: 16, eye, eyeHeight: 8, climb);

        player.MoveHorizontal(0, 10);

        Assert.Equal(expectedRegion, player.Region);
        Assert.Equal(expectedRegion == 0 ? 10 - player.Width : 15, player.Position.Z, 3);
        Assert.Equal(eye, player.Position.Y); // the step itself does not move the eye
    }

    [Theory]
    [InlineData(10, 16, 9.9f, 1f, 1.5f, 0)] // the eye must be above the new floor
    [InlineData(10, 16, 10.5f, 1f, 1.5f, 1)]
    [InlineData(10, 16, 11f, 8f, 7.5f, 1)] // taller than the headroom of 6, but the eye fits
    [InlineData(0, 4, 4.1f, 3f, 1.5f, 0)] // the eye must be below the new ceiling
    [InlineData(0, 4, 3.9f, 3f, 1.5f, 1)]
    [InlineData(0, 4, 3.6f, 5f, 1.5f, 1)] // PLAYER_SIZE 5 under a ceiling of 4
    [InlineData(16, 16, 8f, 8f, 20f, 0)] // a closed region holds whatever PLAYER_CLIMB
    public void MoveHorizontal_IntoRegionBorder_EntersOnlyWhenTheEyeLiesBetweenTheNewFloorAndCeiling(
        int padFloor, int padCeiling, float eye, float eyeHeight, float climb, int expectedRegion)
    {
        // OV-20261019-border-climb, eye-10, ceil-4b, ceil-4c and pad-solid.
        (Player player, _) = PadPlayer(padFloor, padCeiling, eye, eyeHeight, climb);

        player.MoveHorizontal(0, 10);

        Assert.Equal(expectedRegion, player.Region);
    }

    [Fact]
    public void MoveHorizontal_PassableRegionBorder_IgnoresStepAndCeiling()
    {
        // OV-20261019-border-climb, pass-4; OV-20261019-clip-contact, border-ceil.
        (Player player, _) = PadPlayer(padFloor: 16, padCeiling: 16, eye: 8, eyeHeight: 8, climb: 0.5f, padFlags: "PASSABLE");

        player.MoveHorizontal(0, 10);

        Assert.Equal(1, player.Region);
        Assert.Equal(15, player.Position.Z, 3);
    }

    [Fact]
    public void MoveHorizontal_AfterSteppingDownWithoutSinking_WalksBackUpButNotFromThePadFloor()
    {
        // OV-20261019-border-climb, step-down: a pad 4 below the room. The eye stays at 8 on the
        // way down, so the feet are still level with the room; with the feet on the pad floor the
        // 4-step rise blocks.
        (Player player, _) = PadPlayer(padFloor: -4, padCeiling: 16, eye: 8, eyeHeight: 8, climb: 1.5f);

        player.MoveHorizontal(0, 10);
        Assert.Equal(1, player.Region);
        player.MoveHorizontal(0, -10);
        Assert.Equal(0, player.Region);

        player.MoveTo(1, 15, 15);
        player.Position = new Vector3(15, 4, 15);
        player.MoveHorizontal(0, -10);
        Assert.Equal(1, player.Region);
        Assert.Equal(10 + player.Width, player.Position.Z, 3);
    }

    [Fact]
    public void TakeTouchedObjects_PushingAgainstAnUnenterableRegionBorder_ReportsItOnEveryMove()
    {
        // OV-20261019-border-climb: the original fired the border wall's IF_NEAR in every pushing
        // cycle (95 in a row in climb-1).
        (Player player, Level level) = PadPlayer(padFloor: 4, padCeiling: 16, eye: 8, eyeHeight: 8, climb: 1.5f);

        player.MoveHorizontal(0, 10);
        Assert.Contains(level.Walls[4], player.TakeTouchedObjects());

        player.MoveHorizontal(0, 0.2f);
        Assert.Contains(level.Walls[4], player.TakeTouchedObjects());
        Assert.Equal(0, player.Region);
    }

    [Fact]
    public void MoveHorizontal_DownTallLedge_CrossesPortalWithoutReverseCollision()
    {
        // The upper region is left of the ledge (y > 0), the lower one right of it (y < 0).
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\n" +
            "REGION upper 8 20;#0\nREGION lower 0 20;#1\n" +
            "WALL ledge 0 1 0 1 0 0;#0\n" +
            "PLAYER_START 5 1 0 0;#1";
        var player = new Player(Load(map));

        Vector2 displacement = player.MoveHorizontal(0, -2);

        Assert.Equal(1, player.Region);
        Assert.True(displacement.Y < 0);
        Assert.True(player.Position.Z < 0);
    }

    [Fact]
    public void MoveHorizontal_UpTallLedge_RemainsBlocked()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\n" +
            "REGION upper 8 20;#0\nREGION lower 0 20;#1\n" +
            "WALL ledge 0 1 0 1 0 0;#0\n" +
            "PLAYER_START 5 -1 0 1;#1";
        var player = new Player(Load(map));

        player.MoveHorizontal(0, 2);

        Assert.Equal(1, player.Region);
        Assert.Equal(-player.Width, player.Position.Z, 3);
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

    /// <summary>
    /// A room (region 0, floor 0, ceiling 16, x and y 0…40) with a pad island (region 1, x and y
    /// 10…20) like the oracle carriers' pad. Both outlines are counter-clockwise, so each region is
    /// left of its walls; the pad's south wall (y = 10) is wall 4. The player stands in the room at
    /// (15, 5) with the given eye height, <c>PLAYER_SIZE</c> and <c>PLAYER_CLIMB</c>.
    /// </summary>
    private static (Player Player, Level Level) PadPlayer(
        int padFloor, int padCeiling, float eye, float eyeHeight, float climb, string padFlags = "")
    {
        string map =
            "VERTEX 0 0 0;#0\nVERTEX 40 0 0;#1\nVERTEX 40 40 0;#2\nVERTEX 0 40 0;#3\n" +
            "VERTEX 10 10 0;#4\nVERTEX 20 10 0;#5\nVERTEX 20 20 0;#6\nVERTEX 10 20 0;#7\n" +
            $"REGION room 0 16;#0\nREGION pad {padFloor} {padCeiling};#1\n" +
            "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\nWALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\n" +
            "WALL p 4 5 1 0 0 0;#4\nWALL p 5 6 1 0 0 0;#5\nWALL p 6 7 1 0 0 0;#6\nWALL p 7 4 1 0 0 0;#7\n" +
            "PLAYER_START 15 5 0 0;#8";
        string main = "MAPFILE <m.wmp>;\n" + (padFlags.Length > 0 ? $"WALL p {{ FLAGS {padFlags}; }}\n" : "");
        Level level = Load(map, main);
        var player = new Player(level) { EyeHeight = eyeHeight, StepUp = climb };
        player.Position = new Vector3(15, eye, 5);
        return (player, level);
    }

    private static Level Load(string map = Map, string main = "MAPFILE <m.wmp>;")
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        return LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }
}
