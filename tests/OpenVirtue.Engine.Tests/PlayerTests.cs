// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Tests.TestSupport;
using OpenVirtue.Formats.Wrs;

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
    public void TakeTouchedEntities_WallThatStoppedTheMove_IsReportedAtOnceAndAPassedWallWaits()
    {
        // OV-20261019-border-climb (2026-11-03 addendum): a blocking wall's IF_NEAR runs in the cycle
        // of the move, a wall passed through still waits for the next cycle.
        Level level = Load();
        var player = new Player(level);

        player.MoveHorizontal(-5, 0, out bool stopped);

        Assert.True(stopped);
        Assert.Equal<AcknexObject>([level.Walls[1]], player.TakeTouchedEntities());
        Assert.Empty(player.TakeTouchedObjects());

        player.MoveTo(0, 5, -1);
        player.MoveHorizontal(0, 2, out stopped);

        Assert.False(stopped);
        Assert.Empty(player.TakeTouchedEntities());
        Assert.Equal<AcknexObject>([level.Walls[0]], player.TakeTouchedObjects());
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
        Assert.Equal(expectedRegion == 0 ? 10 - player.Width - Player.WallClearance : 15, player.Position.Z, 3);
        Assert.Equal(eye, player.Position.Y); // the step itself does not move the eye
    }

    [Theory]
    [InlineData(0f, 0)] // a step equal to PLAYER_CLIMB blocks
    [InlineData(1f / 1024, 1)] // the eye this frame rises to puts the feet 1/1024 up
    public void MoveHorizontal_IntoRegionBorder_MeasuresTheFeetFromTheEyeThisFrameRisesTo(float rise, int expectedRegion)
    {
        // OV-20261110-retail-play round 4: the climb test sees the eye after this frame's PLAYER_VZ.
        (Player player, _) = PadPlayer(padFloor: 1, padCeiling: 16, eye: 8, eyeHeight: 8, climb: 1);
        player.ClimbEyeRise = rise;

        player.MoveHorizontal(0, 10);

        Assert.Equal(expectedRegion, player.Region);
        Assert.Equal(8f, player.Position.Y); // the rise itself is the vertical move's
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
    public void MoveHorizontal_ImpassableRegionBorder_BlocksEvenALevelPad()
    {
        // OV-20261019-border-kinds, imp-border: the player stops at PLAYER_WIDTH + 0.05.
        (Player player, _) = PadPlayer(padFloor: 0, padCeiling: 16, eye: 8, eyeHeight: 8, climb: 1.5f, padFlags: "IMPASSABLE");

        player.MoveHorizontal(0, 10);

        Assert.Equal(0, player.Region);
        Assert.Equal(10 - player.Width - Player.WallClearance, player.Position.Z, 3);
        Assert.Contains(player.TakeTouchedObjects(), static touched => touched is Wall);
    }

    [Fact]
    public void MoveHorizontal_ImpassableAndPassableRegionBorder_LetsThePlayerThrough()
    {
        // OV-20261019-border-kinds, imp-pass-border (p.53: passable for the player, not for actors).
        (Player player, _) = PadPlayer(padFloor: 0, padCeiling: 16, eye: 8, eyeHeight: 8, climb: 1.5f, padFlags: "IMPASSABLE, PASSABLE");

        player.MoveHorizontal(0, 10);

        Assert.Equal(1, player.Region);
    }

    [Fact]
    public void MoveHorizontal_InvisibleRegionBorder_IgnoresTheClimbAndCountsTheCrossing()
    {
        // OV-20261019-border-kinds, invisible-border: a pad 4 above the room behind INVISIBLE walls.
        (Player player, _) = PadPlayer(padFloor: 4, padCeiling: 16, eye: 8, eyeHeight: 8, climb: 1.5f, padFlags: "INVISIBLE");

        player.MoveHorizontal(0, 10);

        Assert.Equal(1, player.Region);
        Assert.Contains(player.TakeTouchedObjects(), static touched => touched is Wall);
    }

    [Fact]
    public void MoveHorizontal_InvisibleFreeStandingWall_IsPassedWithoutATouchEvenWhenImpassable()
    {
        // OV-20261019-border-kinds, invisible: no IF_NEAR on passing, INVISIBLE wins over IMPASSABLE.
        string map =
            "VERTEX 0 0 0;#0\nVERTEX 40 0 0;#1\nVERTEX 40 40 0;#2\nVERTEX 0 40 0;#3\n" +
            "VERTEX 20 10 0;#4\nVERTEX 20 30 0;#5\nREGION room 0 16;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\nWALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\n" +
            "WALL ghost 4 5 0 0 0 0;#4\nPLAYER_START 15 20 0 0;#5";
        var player = new Player(Load(map, "MAPFILE <m.wmp>;\nWALL ghost { FLAGS INVISIBLE, IMPASSABLE; }\n"));
        player.MoveTo(0, 15, 20);

        player.MoveHorizontal(10, 0);

        Assert.Equal(25, player.Position.X, 3);
        Assert.Empty(player.TakeTouchedObjects());
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
        Assert.Equal(10 + player.Width + Player.WallClearance, player.Position.Z, 3);
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
        Assert.Equal(-player.Width - Player.WallClearance, player.Position.Z, 3);
    }

    [Fact]
    public void PushOutOfObjects_BeamedWithinPlayerWidthOfASolidWall_IsHeldAtTheClearanceWithANudge()
    {
        // OV-20261020-view-activity view-pillar (2026-11-03 addendum): beamed to (-15, 21), 1.0 from the
        // room's west wall at x = -16, the player stood at (-14.751, 21.004) in the next cycle: held
        // PLAYER_WIDTH + 51/1024 from the wall, the other axis nudged 4/1024 as in a push.
        var player = new Player(Load()) { Width = 1228 / 1024f }; // PLAYER_WIDTH 1.2 as the runtime holds it
        player.MoveTo(0, 1.0f, -5f); // 1.0 from the solid region across x = 0

        player.PushOutOfObjects();

        Assert.Equal(1279 / 1024f, player.Position.X, 6);
        Assert.Equal(-5 + (4 / 1024f), player.Position.Z, 6);

        player.Settle();
        player.PushOutOfObjects(); // standing outside the width now: left alone
        Assert.Equal(1279 / 1024f, player.Position.X, 6);
        Assert.Equal(-5 + (4 / 1024f), player.Position.Z, 6);
    }

    [Fact]
    public void MoveHorizontal_IntoSolidWall_StopsPlayerWidthPlusClearanceAway()
    {
        // OV-20261019-eye-clamp, stop-solid and stop-border: 1.245–1.249 from the wall with
        // PLAYER_WIDTH 1.2.
        var player = new Player(Load());

        player.MoveHorizontal(-10, 0); // toward the solid region across x=0

        Assert.Equal(0, player.Region);
        Assert.Equal(1.25f, player.Position.X, 3);
    }

    [Theory]
    [InlineData(0.5f, 0.55f)]
    [InlineData(2f, 2.05f)]
    [InlineData(3f, 3.05f)]
    public void MoveHorizontal_IntoSolidWall_StopDistanceFollowsPlayerWidth(float width, float expected)
    {
        // OV-20261019-eye-clamp, stop-solid: 0.550, 2.046 and 3.050 from the wall.
        var player = new Player(Load()) { Width = width };

        player.MoveHorizontal(-10, 0);

        Assert.Equal(expected, player.Position.X, 3);
    }

    [Theory]
    [InlineData(10, 16, 8f, 8f, 10.35f)] // pass-10: a PASSABLE step onto floor 10
    [InlineData(1, 16, 1.1f, 1f, 1.35f)] // edge-1
    [InlineData(10, 16, 15.9f, 8f, 15.65f)] // eye-10
    [InlineData(0, 4, 3.9f, 3f, 3.65f)] // ceil-4b
    [InlineData(16, 16, 8f, 8f, 15.65f)] // border-ceil: floor = ceiling, the ceiling wins
    [InlineData(10, 16, 12f, 8f, 12f)] // inside: left alone, the feet hang below the floor
    public void ClampEye_KeepsTheEyeAMarginInsideTheRegion(int padFloor, int padCeiling, float eye, float eyeHeight, float expected)
    {
        // OV-20261019-border-climb, rule 6.
        (Player player, _) = PadPlayer(padFloor, padCeiling, eye, eyeHeight, climb: 1.5f, padFlags: "PASSABLE");
        player.MoveHorizontal(0, 10);
        Assert.Equal(1, player.Region);

        float correction = player.ClampEye();

        Assert.Equal(expected, player.Position.Y, 4);
        Assert.Equal(expected - eye, correction, 4);
    }

    [Fact]
    public void MoveVertical_PastTheFloor_LeavesTheEyeToTheClamp()
    {
        // OV-20261019-eye-clamp, vz-move: PLAYER_VZ carried the eye past the feet-on-floor height
        // (3 with PLAYER_SIZE 3) down to the floor + 0.35, PLAYER_HGT −2.65.
        var player = new Player(Load());
        Assert.Equal(3, player.Position.Y);

        Assert.Equal(-2.8f, player.MoveVertical(-2.8f), 4);
        Assert.Equal(0.2f, player.Position.Y, 4);
        Assert.True(player.Grounded);

        Assert.Equal(0.15f, player.ClampEye(), 4);
        Assert.Equal(0.35f, player.Position.Y, 4);
    }

    [Theory]
    [InlineData(4, -8, 20, 8, 10854, 376, 10958, 479)] // wall-slide w45-s02: y = x − 12, steps (204, 4)
    [InlineData(4, -8, 20, 8, 10958, 479, 11061, 583)]
    [InlineData(-4, -12, 28, 4, 18520, 450, 18684, 532)] // w27-s02: y = x/2 − 10
    [InlineData(4, -8, 20, 8, 19238, 8759, 19325, 8846)] // w45-s02 cycle 188: the wall's end, then the line
    [InlineData(0, -9, 24, 9, 13169, 2260, 13367, 2408, 307)] // w37-s03: y = 3x/4 − 9, the unit normal not rounded
    public void MoveHorizontal_AlongDiagonalWall_SlidesAsTheOriginalIn1024ths(
        int x1, int y1, int x2, int y2, int fromX, int fromY, int toX, int toY, int stepX = 204)
    {
        // OV-20261110-wall-slide: the whole step, its end's distance measured with the wall's normal in
        // 1/1024 and cut down, pushed back to PLAYER_WIDTH + 51/1024 with each axis's part cut down.
        string map =
            "VERTEX -16 -24 0;#0\nVERTEX 48 -24 0;#1\nVERTEX 48 24 0;#2\nVERTEX -16 24 0;#3\n" +
            $"VERTEX {x1} {y1} 0;#4\nVERTEX {x2} {y2} 0;#5\n" +
            "REGION room 0 16;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\nWALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\n" +
            "WALL d 4 5 0 0 0 0;#4\n" +
            "PLAYER_START 0 0 0 0;#6";
        var player = new Player(Load(map)) { Width = 1228f / 1024 };
        player.Position = new Vector3(fromX / 1024f, 8, fromY / 1024f);

        player.MoveHorizontal(stepX / 1024f, 4f / 1024);

        Assert.Equal(toX, player.Position.X * 1024, 3);
        Assert.Equal(toY, player.Position.Z * 1024, 3);
    }

    [Theory]
    [InlineData(-144, 428, 1976, 2612)] // the step would cross the line: halved, x −72
    [InlineData(-300, 428, 1898, 2612)]
    [InlineData(-144, 4, 1904, 2612)] // it stays on the player's side: the whole x
    [InlineData(200, 100, 2248, 2612)]
    public void MoveHorizontal_FromInsideTheHold_HalvesAStepThatWouldCrossTheLine(int stepX, int stepY, int toX, int toY)
    {
        // OV-20261110-wall-slide round 7: beamed to (2048, 3686), 410/1024 below a free wall along y = 4, PLAYER_WIDTH 1.4.
        const string map =
            "VERTEX -16 -24 0;#0\nVERTEX 48 -24 0;#1\nVERTEX 48 24 0;#2\nVERTEX -16 24 0;#3\n" +
            "VERTEX -20 4 0;#4\nVERTEX 20 4 0;#5\n" +
            "REGION room 0 16;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\nWALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\n" +
            "WALL d 4 5 0 0 0 0;#4\n" +
            "PLAYER_START 0 0 0 0;#6";
        var player = new Player(Load(map)) { Width = 1433f / 1024 };
        player.Position = new Vector3(2048f / 1024, 8, 3686f / 1024);

        player.MoveHorizontal(stepX / 1024f, stepY / 1024f);

        Assert.Equal(toX, player.Position.X * 1024, 3);
        Assert.Equal(toY, player.Position.Z * 1024, 3);
    }

    [Fact]
    public void MoveHorizontal_IntoACornerOfTwoWalls_HoldsOffTheNearerFirst()
    {
        // OV-20261110-retail-play round 6: Heart's walk into the corner of two swamp walls, 160 steps lower. Both are
        // within PLAYER_WIDTH of the step's end; the original held it off the nearer (1339/1024 from it), and the
        // other then let it be. Held off the other first, the player came out at (−7120, 22045).
        string map =
            "VERTEX -16 -24 0;#0\nVERTEX 48 -24 0;#1\nVERTEX 48 48 0;#2\nVERTEX -16 48 0;#3\n" +
            "VERTEX -6.472 36.141 0;#4\nVERTEX -8.481 21.039 0;#5\nVERTEX 4.15 4.202 0;#6\n" +
            "REGION room 0 16;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\nWALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\n" +
            "WALL a 4 5 0 0 0 0;#4\nWALL b 5 6 0 0 0 0;#5\n" +
            "PLAYER_START 0 0 0 0;#7";
        var player = new Player(Load(map)) { Width = 1433f / 1024 }; // PLAYER_WIDTH 1.4 in 22.10
        player.Position = new Vector3(-7099f / 1024, 8, 21846f / 1024);

        player.MoveHorizontal(-171f / 1024, 171f / 1024);

        Assert.Equal(-7127, player.Position.X * 1024, 3);
        Assert.Equal(21997, player.Position.Z * 1024, 3);
    }

    // Four regions meet at the origin: sw, se and ne are rooms of one height, nw is closed.
    private const string CrossMap =
        "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nVERTEX 0 20 0;#2\nVERTEX -20 0 0;#3\nVERTEX 0 -20 0;#4\n" +
        "REGION sw 0 20;#0\nREGION se 0 20;#1\nREGION ne 0 20;#2\nREGION nw 0 0;#3\n" +
        "WALL w 0 1 2 1 0 0;#0\nWALL w 0 2 3 2 0 0;#1\nWALL w 0 3 0 3 0 0;#2\nWALL w 0 4 1 0 0 0;#3\n" +
        "PLAYER_START -2 -2 0 0;#3";

    [Fact]
    public void MoveHorizontal_PastAVertexWhereRegionsMeet_FollowsBothBorders()
    {
        // One short step from sw crosses the border into se and, a hair on, the one into ne. The region
        // stayed se, so ne's walls were not tested and the player walked on through them.
        var player = new Player(Load(CrossMap)) { Width = 0.01f };
        player.MoveTo(0, -0.015f, -0.03f);

        player.MoveHorizontal(0.045f, 0.06f);

        Assert.Equal(2, player.Region);
    }

    [Fact]
    public void MoveHorizontal_HeldBackAcrossTheBorderItCrossed_KeepsTheRegionItStandsIn()
    {
        // A step from sw across the border at x = 0 into se, 1 wide here, ends within PLAYER_WIDTH of se's
        // far wall, and the hold off that wall lies back in sw.
        const string map =
            "VERTEX 0 -20 0;#0\nVERTEX 0 0 0;#1\nVERTEX 1 -20 0;#2\nVERTEX 1 0 0;#3\n" +
            "REGION sw 0 20;#0\nREGION se 0 20;#1\nREGION solid 0 0;#2\n" +
            "WALL w 0 1 0 1 0 0;#0\nWALL w 2 3 1 2 0 0;#1\n" +
            "PLAYER_START -0.3 -10 0 0;#3";
        var player = new Player(Load(map));

        player.MoveHorizontal(0.5f, 0, out bool stopped);

        Assert.True(stopped);
        Assert.True(player.Position.X < 0, $"x {player.Position.X}");
        Assert.Equal(0, player.Region);
    }

    // The room of the wall-slide probe, x -16 to 48 and y -24 to 24, with free walls added by each test.
    private static Level LoadProbeRoom(string walls, string pad = "") => Load(
        "VERTEX -16 -24 0;#0\nVERTEX 48 -24 0;#1\nVERTEX 48 24 0;#2\nVERTEX -16 24 0;#3\n" +
        "VERTEX -12 12 0;#4\nVERTEX -4 12 0;#5\nVERTEX -4 20 0;#6\nVERTEX -12 20 0;#7\n" +
        "VERTEX 4 -2 0;#8\nVERTEX 24 -2 0;#9\nVERTEX 4 4 0;#10\nVERTEX -11 13 0;#11\nVERTEX -5 13 0;#12\n" +
        "REGION room 0 16;#0\nREGION solid 16 16;#1\nREGION pad 0 16;#2\n" +
        "WALL w 0 1 0 1 0 0;#0\nWALL w 1 2 0 1 0 0;#1\nWALL w 2 3 0 1 0 0;#2\nWALL w 3 0 0 1 0 0;#3\n" +
        pad + walls + "PLAYER_START 0 0 0 0;#3",
        "MAPFILE <m.wmp>;\nWALL open { FLAGS INVISIBLE, PASSABLE; }");

    [Fact]
    public void MoveHorizontal_TowardAWallOfTheNextRegion_IsHeldBeforeTheBorder()
    {
        // OV-20261110-wall-slide round 12 behind-1: a wall 1 behind a border the player may cross, at y = 13.
        // The original held the player at y 11828/1024, 1484 from the wall, without crossing the border at 12.
        Level level = LoadProbeRoom(
            "WALL w 11 12 2 2 0 0;#8\n",
            "WALL open 4 5 2 0 0 0;#4\nWALL open 5 6 2 0 0 0;#5\nWALL open 6 7 2 0 0 0;#6\nWALL open 7 4 2 0 0 0;#7\n");
        var player = new Player(level) { Width = 1433f / 1024 };
        player.MoveTo(0, -8, 6);

        bool stopped = false;
        string trail = "y:";
        for (int step = 0; step < 40 && !stopped; step++)
        {
            player.MoveHorizontal(4f / 1024, 204f / 1024, out stopped);
            trail += $" {player.Position.Z * 1024:0.###}";
        }

        Assert.True(stopped, trail);
        Assert.Equal(0, player.Region);
        Assert.True(MathF.Round(player.Position.Z * 1024) == 11828, trail);
    }

    [Fact]
    public void MoveHorizontal_IntoAWedge_LandsMidwayAndThenStays()
    {
        // OV-20261110-wall-slide round 12 wedge-flat: a wall along y = -2 and one from (4, 4) falling to meet it at
        // (24, -2). From (14620, -612)/1024 the original stepped (204, 4) to (14698, -588), midway to where two
        // rounds of holds end, and then did not move again.
        Level level = LoadProbeRoom("WALL w 8 9 0 0 0 0;#4\nWALL w 10 9 0 0 0 0;#5\n");
        var player = new Player(level) { Width = 1433f / 1024 };
        player.MoveTo(0, 14620f / 1024, -612f / 1024);

        player.MoveHorizontal(204f / 1024, 4f / 1024, out bool stopped);

        Assert.True(stopped);
        Assert.False(player.StepRefused);
        Assert.Equal(14698, MathF.Round(player.Position.X * 1024));
        Assert.Equal(-588, MathF.Round(player.Position.Z * 1024));

        player.MoveHorizontal(204f / 1024, 4f / 1024, out stopped);

        Assert.True(stopped);
        Assert.True(player.StepRefused);
        Assert.Equal(14698, MathF.Round(player.Position.X * 1024));
        Assert.Equal(-588, MathF.Round(player.Position.Z * 1024));
    }

    [Fact]
    public void MoveHorizontal_IntoAWedgeOfTwoWalls_NeverComesThroughEither()
    {
        // Two walls onto a closed region close in on each other toward (20, 0). The hold off one pressed the
        // player into the other, a little further each step, and after some 55 steps through it.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nVERTEX 0 3 0;#2\n" +
            "REGION room 0 20;#0\nREGION solid 0 0;#1\n" +
            "WALL w 0 1 1 0 0 0;#0\nWALL w 1 2 1 0 0 0;#1\nWALL w 2 0 1 0 0 0;#2\n" +
            "PLAYER_START 3 1.5 0 0;#3";
        Level level = Load(map);
        var player = new Player(level);

        for (int step = 0; step < 800; step++)
        {
            float dy = step / 7 % 2 == 0 ? 0.11f : -0.11f;
            Vector3 before = player.Position;
            player.MoveHorizontal(0.3f, dy, out bool stopped);
            if (stopped)
            {
                // The 4/1024 minimum WdlPlayerMotion gives a part a wall stopped.
                player.ShiftTo(
                    MathF.Abs(player.Position.X - before.X) < 3.9f / 1024 ? before.X + (4f / 1024) : player.Position.X,
                    MathF.Abs(player.Position.Z - before.Z) < 3.9f / 1024 ? before.Z + ((dy < 0 ? -4f : 4f) / 1024) : player.Position.Z);
            }

            Assert.True(
                LevelTopology.RegionContains(level, 0, player.Position.X, player.Position.Z),
                $"step {step}: ({player.Position.X}, {player.Position.Z})");
        }
    }

    [Fact]
    public void MoveHorizontal_FromExactlyOnALedgesLine_IsHeldOnItsOwnSide()
    {
        // The player stands on the line of the border it came down; the border, too high to climb, holds a
        // step back onto its own side and not across it.
        const string map =
            "VERTEX 0 -20 0;#0\nVERTEX 0 20 0;#1\nVERTEX 20 -20 0;#2\nVERTEX 20 20 0;#3\nVERTEX -20 -20 0;#4\nVERTEX -20 20 0;#5\n" +
            "REGION low 0 40;#0\nREGION high 8 40;#1\nREGION solid 0 0;#2\n" +
            "WALL w 0 1 1 0 0 0;#0\n" +
            "WALL w 0 2 0 2 0 0;#1\nWALL w 2 3 0 2 0 0;#2\nWALL w 3 1 0 2 0 0;#3\n" +
            "WALL w 1 5 1 2 0 0;#4\nWALL w 5 4 1 2 0 0;#5\nWALL w 4 0 1 2 0 0;#6\n" +
            "PLAYER_START 5 0 0 0;#3";
        var player = new Player(Load(map));
        player.MoveTo(0, 0, 0);

        player.MoveHorizontal(-0.5f, 0.1f, out bool stopped);

        Assert.True(stopped);
        Assert.Equal(0, player.Region);
        Assert.True(player.Position.X > 1, $"x {player.Position.X}");
    }

    [Fact]
    public void MoveHorizontal_RandomWalksThroughRetailLevels_StayInTheRegionTheyAreCountedIn()
    {
        // Guarded: runs only with the user's own archives under _research/. Before 2026-11-13 about half of
        // such walks left the player in a region it did not stand in, and from there through walls
        // (OV-20261110-wall-slide, addendum).
        foreach (string archive in ResearchData.WrsFiles()
            .Where(static path => !Path.GetFileName(path).StartsWith("START", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(path).StartsWith("TITLE", StringComparison.OrdinalIgnoreCase)))
        {
            Level level = LevelLoader.Load(WrsArchive.ReadFile(archive), ResearchData.MainWdlName(archive).ToUpperInvariant());
            for (int seed = 0; seed < 12; seed++)
            {
                var random = new Random(seed);
                var player = new Player(level) { Width = 1.4f, EyeHeight = 5f, StepUp = 2.5f };
                double heading = random.NextDouble() * Math.Tau;
                float speed = 0.2f + (float)random.NextDouble();
                for (int step = 0; step < 2500; step++)
                {
                    if (random.Next(40) == 0)
                    {
                        heading = random.NextDouble() * Math.Tau;
                    }

                    heading += (random.NextDouble() - 0.5) * 0.15;
                    if (random.Next(100) == 0)
                    {
                        speed = 0.1f + ((float)random.NextDouble() * 1.2f);
                    }

                    float dx = MathF.Round((float)(Math.Cos(heading) * speed) * 1024) / 1024;
                    float dy = MathF.Round((float)(Math.Sin(heading) * speed) * 1024) / 1024;
                    Vector3 before = player.Position;
                    player.MoveHorizontal(dx, dy, out bool stopped);
                    if (stopped)
                    {
                        // The 4/1024 minimum WdlPlayerMotion gives a part a wall stopped.
                        player.ShiftTo(
                            MathF.Abs(dx) >= 4f / 1024 && MathF.Abs(player.Position.X - before.X) < 3.9f / 1024 ? before.X + 4f / 1024 : player.Position.X,
                            MathF.Abs(dy) >= 4f / 1024 && MathF.Abs(player.Position.Z - before.Z) < 3.9f / 1024 ? before.Z + 4f / 1024 : player.Position.Z);
                    }

                    // Feet on the floor at once, as a fall would put them in a few cycles.
                    Vector3 at = player.Position;
                    player.Position = new Vector3(at.X, player.FloorHeight + player.EyeHeight, at.Z);
                    player.ClampEye();

                    // A point on a border belongs to both sides.
                    bool inside = false;
                    for (int ox = -1; ox <= 1 && !inside; ox++)
                    {
                        for (int oy = -1; oy <= 1 && !inside; oy++)
                        {
                            inside = LevelTopology.RegionContains(level, player.Region, at.X + (ox * 0.02), at.Z + (oy * 0.02));
                        }
                    }

                    Assert.True(
                        inside,
                        $"{Path.GetFileName(archive)} walk {seed} step {step}: ({at.X * 1024:0}, {at.Z * 1024:0})/1024 is outside region {player.Region}");
                }
            }
        }
    }

    [Fact]
    public void MoveHorizontal_DiagonallyIntoSolidWall_SlidesAlongWall()
    {
        var player = new Player(Load());

        Vector2 displacement = player.MoveHorizontal(-10, 4);

        Assert.Equal(player.Width + Player.WallClearance, player.Position.X, 3);
        Assert.Equal(-1, player.Position.Z, 3);
        Assert.True(displacement.Y > 3.9f);
    }

    [Fact]
    public void MoveHorizontal_ApproachingSolidWallWithoutCrossing_MaintainsPlayerWidthPlusClearance()
    {
        var player = new Player(Load());

        player.MoveHorizontal(-4.5f, 0);

        Assert.Equal(player.Width + Player.WallClearance, player.Position.X, 3);
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

        Assert.Equal(blocked.Width + Player.WallClearance, blocked.Position.X, 3);

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

        // The original stops the player about 0.02 beyond (OV-20261029-overlap): the step into the
        // thing is pushed back to C² = 2252²/1024 + 98 in 22.10 (still-walk).
        Assert.Equal(LevelCollisionWorld.ContactDistance(2.2f), blocked.Position.X, 2);
        Assert.Equal(2273 / 1024f, blocked.Position.X, 6);

        level.Things[0]["passable"] = 1;
        var passable = new Player(level);
        passable.MoveHorizontal(-10, 0);

        Assert.Equal(-5, passable.Position.X, 3);
    }

    [Fact]
    public void MoveHorizontal_StepIntoAThing_IsPushedBackOnceFromTheWholeStepsEnd()
    {
        // OV-20261103-contact-clock step16-off: from (13104, 32) a 1638-unit step and the 4/1024 nudge
        // end at (14742, 36), 20 above a 2-wide thing at (16, -20/1024); pushed to C² in 22.10 from
        // there, the player stands at (14112, 57). Pushing on a first sub-step gave (14113, 55).
        const string map =
            "VERTEX -20 -20 0;#0\nVERTEX 40 -20 0;#1\n" +
            "REGION room 0 20;#0\nTHING crate 16 -0.01953125 0 0;#0\n" +
            "PLAYER_START 12.796875 0.03125 0 0;#1";
        const string main =
            "MAPFILE <m.wmp>; " +
            "BMAP crateMap, <crate.pcx>, 0, 0, 32, 16; " +
            "TEXTURE crateTex { BMAPS crateMap; } " +
            "THING crate { TEXTURE crateTex; }";
        var player = new Player(Load(map, main));

        Vector2 moved = player.MoveHorizontal(1638 / 1024f, 4 / 1024f);

        Assert.Equal(14112, Math.Round(player.Position.X * 1024));
        Assert.Equal(57, Math.Round(player.Position.Z * 1024));
        Assert.Equal(14112 - 13104, Math.Round(moved.X * 1024));
        Assert.Equal(14112 - 14742, Math.Round(player.LastObjectPush.X * 1024)); // the push-back from the step's end
        Assert.Equal(57 - 36, Math.Round(player.LastObjectPush.Y * 1024));
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
        var player = new Player(Load(map, "MAPFILE <m.wmp>; REGION ramp { FLAGS FLOOR_ASCEND, CEIL_ASCEND; }"));

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
