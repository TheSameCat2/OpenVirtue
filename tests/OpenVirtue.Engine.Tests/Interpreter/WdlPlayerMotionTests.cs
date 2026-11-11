// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Tests.TestSupport;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Engine.Tests.Interpreter;

public sealed class WdlPlayerMotionTests
{
    private const string Map =
        "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nVERTEX 0 -20 0;#2\n" +
        "REGION a 0 20;#0\nREGION b 0 20;#1\nREGION solid 0 0;#2\n" +
        "WALL portal 1 0 0 1 0 0;#0\nWALL solidw 0 2 0 2 0 0;#1\n" +
        "PLAYER_START 5 -5 0 0;#3";

    [Fact]
    public void Apply_RealApathyStartup_ForceAheadProducesValidPlayerMotion()
    {
        string? apathy = ResearchData.WrsFiles()
            .FirstOrDefault(path => Path.GetFileName(path).Equals("apathy.wrs", StringComparison.OrdinalIgnoreCase));
        if (apathy is null)
        {
            return;
        }

        WrsArchive archive = WrsArchive.ReadFile(apathy);
        Level level = LevelLoader.Load(archive, "APATHY.WDL");
        var runtime = new WdlRuntime(level);
        var player = new Player(level);
        runtime.RegisterObject("player", player);
        WdlPlayerMotion.Synchronize(runtime, player);
        Assert.True(runtime.RunStartup());

        System.Numerics.Vector3 start = player.Position;
        double startAngle = Math.PI / 2 + ((level.PlayerStart?.Angle ?? 0) * Math.PI / 180);
        runtime.SetSkill("PLAYER_ANGLE", startAngle);
        runtime.SetSkill("PLAYER_SIN", Math.Sin(startAngle));
        runtime.SetSkill("PLAYER_COS", Math.Cos(startAngle));
        runtime.TriggerActionHook("IF_ANYKEY");
        Assert.True(runtime.TriggerActionHook("IF_W"));
        for (int frame = 0; frame < 120; frame++)
        {
            runtime.SetSkill("FORCE_AHEAD", runtime.GetSkill("KEY_SENSE"));
            double timeCorrection = runtime.Tick(1.0 / 60.0);
            WdlPlayerMotion.Apply(runtime, player, timeCorrection);
        }

        float distanceSquared = System.Numerics.Vector3.DistanceSquared(start, player.Position);
        Assert.True(
            distanceSquared > 0.25f,
            $"distance2={distanceSquared}, force={runtime.GetSkill("FORCE_AHEAD")}, " +
            $"vx={runtime.GetSkill("PLAYER_VX")}, vy={runtime.GetSkill("PLAYER_VY")}, " +
            $"tc={runtime.GetSkill("TIME_CORR")}, region={player.Region}, " +
            $"eachTick={runtime.ActiveEachTickActionCount}, movement={runtime.ActivePlayerMovementActionCount}, " +
            $"statements={runtime.LastTickGlobalStatementCount}, " +
            $"vStatements={runtime.LastTickPlayerVelocityStatementCount}, " +
            $"vWrites={runtime.LastTickPlayerVelocityWriteCount}, " +
            $"unsupported={runtime.LastTickUnsupportedExpressionCount}, waiting={runtime.WaitingActionCount}, " +
            $"conditions={runtime.LastTickTrueConditionCount}T/{runtime.LastTickFalseConditionCount}F, " +
            $"hgt={runtime.GetSkill("PLAYER_HGT")}, depth={runtime.GetSkill("PLAYER_DEPTH")}, " +
            $"moveMode={runtime.GetSkill("MOVE_MODE")}, friction={runtime.GetSkill("FRICTION")}, " +
            $"inertia={runtime.GetSkill("INERTIA")}");
        Assert.InRange(player.Region, 0, level.Regions.Count - 1);
        Assert.True(float.IsFinite(player.Position.X));
        Assert.True(float.IsFinite(player.Position.Y));
        Assert.True(float.IsFinite(player.Position.Z));
        Assert.Equal(player.Region, runtime.GetSkill("PLAYER_REGION"));
    }

    [Fact]
    public void Apply_FreeMotion_ScalesVelocityByTimeCorrectionAndUpdatesDerivedSkills()
    {
        (WdlRuntime runtime, Player player) = Create();
        runtime.SetSkill("PLAYER_VX", 2);
        runtime.SetSkill("PLAYER_VY", 4);
        runtime.SetSkill("PLAYER_ANGLE", 0);

        WdlPlayerMotion.Apply(runtime, player, 0.5);

        Assert.Equal(6, player.Position.X);
        Assert.Equal(-3, player.Position.Z);
        Assert.Equal(5, runtime.GetSkill("PLAYER_LAST_X"));
        Assert.Equal(-5, runtime.GetSkill("PLAYER_LAST_Y"));
        Assert.Equal(2, runtime.GetSkill("PLAYER_VX"));
        Assert.Equal(4, runtime.GetSkill("PLAYER_VY"));
        Assert.Equal(2, runtime.GetSkill("PLAYER_SPEED"), 6);
        Assert.Equal(1, runtime.GetSkill("PLAYER_COS"), 6);
        Assert.Equal(0, runtime.GetSkill("PLAYER_SIN"), 6);
    }

    [Fact]
    public void Apply_SolidWallCollision_KeepsVelocityAndAccumulatesImpact()
    {
        // OV-20261110-retail-play round 4: a wall stop keeps the script's velocity; the cut step still
        // counts toward IMPACT_VX.
        (WdlRuntime runtime, Player player) = Create();
        runtime.SetSkill("PLAYER_VX", -10);

        WdlPlayerMotion.Apply(runtime, player, 1);

        Assert.Equal(player.Width + Player.WallClearance, player.Position.X, 3);
        Assert.Equal(-10, runtime.GetSkill("PLAYER_VX"));
        Assert.Equal(6.25, runtime.GetSkill("IMPACT_VX"), 2); // the step cut to −3.75: PLAYER_WIDTH is 1228/1024
    }

    [Theory]
    [InlineData(0, 12, 4, 3)] // a y part of 3/1024 is taken as it is; the x part of 0 becomes +4
    [InlineData(-12, 40, -4, 10)] // an x part of −3/1024 becomes −4
    [InlineData(0, 1, 0, 0)] // a step that rounds to nothing on both axes moves nothing
    public void Apply_SmallStep_RaisesOnlyTheXPartAndAZeroPartTo4(int vxRaw, int vyRaw, int dxRaw, int dyRaw)
    {
        // OV-20261110-retail-play: New Age's player coming to rest stepped 3, 2 and 1 along y, x +4;
        // Apathy's x parts of −3 to −1 were made −4; PLAYER_VY 1/1024 left the player where it stood.
        (WdlRuntime runtime, Player player) = Create();
        player.Position = new System.Numerics.Vector3(5, player.Position.Y, -5);
        runtime.SetSkill("PLAYER_VX", vxRaw / 1024.0);
        runtime.SetSkill("PLAYER_VY", vyRaw / 1024.0);

        WdlPlayerMotion.Apply(runtime, player, 0.25);

        Assert.Equal(5 + (dxRaw / 1024.0), player.Position.X, 6);
        Assert.Equal(-5 + (dyRaw / 1024.0), player.Position.Z, 6);
    }

    [Fact]
    public void Apply_HeldAtABorderItCannotClimb_KeepsTheVelocity()
    {
        // OV-20261110-retail-play round 4: Apathy's walk, held at a 2-step border, read PLAYER_VY 0.902 on.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nVERTEX 0 -20 0;#2\n" +
            "REGION a 0 20;#0\nREGION b 4 20;#1\nREGION solid 0 0;#2\n" +
            "WALL portal 1 0 0 1 0 0;#0\nWALL solidw 0 2 0 2 0 0;#1\n" +
            "PLAYER_START 5 -5 0 0;#3";
        (WdlRuntime runtime, Player player) = Create(map, "MAPFILE <m.wmp>;");
        runtime.SetSkill("PLAYER_SIZE", 5);
        player.Position = new System.Numerics.Vector3(5, 5, -5); // the feet on the floor, the eye above b's
        runtime.SetSkill("PLAYER_VY", 10);

        WdlPlayerMotion.Apply(runtime, player, 1);

        Assert.Equal(0, player.Region);
        Assert.Equal(-(player.Width + Player.WallClearance), player.Position.Z, 3);
        Assert.Equal(10, runtime.GetSkill("PLAYER_VY"));
    }

    [Theory]
    [InlineData(0.2, 0.28125, 57)] // 204 raw x 288/1024 = 57.375
    [InlineData(0.2, 0.25, 51)]
    [InlineData(0.05, 0.25, 13)] // 51 x 256/1024 = 12.75
    [InlineData(0.05, 0.28125, 14)]
    [InlineData(-0.1, 0.25, -25)] // -25.5, half up (OV-20261110-retail-play apathy-walk: -46.5 stepped -46)
    public void Apply_HorizontalStep_IsTheProductOfVelocityAndTimeCorrectionRoundedTo1024ths(
        double velocity, double timeCorrection, int expectedRaw)
    {
        // OV-20261019-eye-clamp stop-border: the walk at 0.2 stepped 57 and 51 raw at TIME_CORR
        // 0.281 and 0.25, the one at 0.05 stepped 14 and 13.
        (WdlRuntime runtime, Player player) = Create();
        runtime.SetSkill("PLAYER_VX", velocity);

        WdlPlayerMotion.Apply(runtime, player, timeCorrection);

        Assert.Equal(5 + (expectedRaw / 1024.0), player.Position.X, 6);
    }

    [Fact]
    public void Apply_PushingIntoASolidWall_HoldsWidthPlus51UnitsAndAlternatesByFour()
    {
        // OV-20261019-eye-clamp and OV-20261019-border-climb ceil-0 (2026-11-03 addenda): PLAYER_WIDTH
        // 1.2 (1228 raw) walking into a wall is held 1279 raw from it; a step held where it stands
        // moves 4 into the wall and the next stop pulls it back by 4.
        (WdlRuntime runtime, Player player) = Create();
        var raw = new List<int>();
        for (int cycle = 0; cycle < 6; cycle++)
        {
            runtime.SetSkill("PLAYER_VX", -0.2); // -204 raw: -1632 per cycle at TIME_CORR 8
            WdlPlayerMotion.Apply(runtime, player, 8);
            raw.Add((int)Math.Round(player.Position.X * 1024));
        }

        Assert.Equal([3488, 1856, 1279, 1283, 1279, 1283], raw);
        Assert.Equal(-5 + (6 * 4 / 1024.0), player.Position.Z, 6); // the y creep goes on
    }

    [Theory]
    [InlineData(1290, 1265, false)] // a step of −25 (−25.5 half up) ends 1265 from the wall: inside the 1279 hold, not stopped
    [InlineData(1253, 1279, true)] // ends exactly PLAYER_WIDTH (1228) from it: stopped
    public void Apply_StepTowardASolidWall_IsStoppedOnlyWithinPlayerWidth(int startRaw, int expectedRaw, bool stopped)
    {
        // OV-20261019-eye-clamp stop-border: the 0.05 walk came on to 11049 (1239 from the wall) before
        // a step was stopped, and from 11009 a step of 51 to exactly 1228 was stopped.
        (WdlRuntime runtime, Player player) = Create();
        player.Position = player.Position with { X = startRaw / 1024f };
        runtime.SetSkill("PLAYER_VX", -0.1); // -26 raw at TIME_CORR 0.25

        WdlPlayerMotion.Apply(runtime, player, 0.25);

        Assert.Equal(expectedRaw, (int)Math.Round(player.Position.X * 1024));
        Assert.Equal(stopped, player.TakeTouchedEntities().OfType<OpenVirtue.Engine.Map.Wall>().Any());
    }

    [Fact]
    public void Apply_PortalCrossing_UpdatesRegionAndPositionSkills()
    {
        (WdlRuntime runtime, Player player) = Create();
        runtime.SetSkill("PLAYER_VY", 10);

        WdlPlayerMotion.Apply(runtime, player, 1);

        Assert.Equal(1, player.Region);
        Assert.Equal(5, player.Position.Z, 3);
        Assert.Equal(1, runtime.GetSkill("PLAYER_REGION"));
        Assert.Equal(5, runtime.GetSkill("PLAYER_Y"), 3);
    }

    [Fact]
    public void Apply_RotationVelocity_NormalizesAngleAndUpdatesDirection()
    {
        (WdlRuntime runtime, Player player) = Create();
        runtime.SetSkill("PLAYER_ANGLE", Math.Tau - 0.25);
        runtime.SetSkill("PLAYER_VROT", 1);

        WdlPlayerMotion.Apply(runtime, player, 0.5);

        // The angle is held in 1/1024 and the turn steps by the engine's rounded product; the sine and
        // cosine of the stored angle are cut toward zero (OV-20261110-retail-play round 4).
        double angle = runtime.GetSkill("PLAYER_ANGLE");
        Assert.Equal(0.25, angle, 2);
        Assert.Equal(Math.Truncate(Math.Sin(angle) * 1024) / 1024, runtime.GetSkill("PLAYER_SIN"));
        Assert.Equal(Math.Truncate(Math.Cos(angle) * 1024) / 1024, runtime.GetSkill("PLAYER_COS"));
    }

    [Theory]
    [InlineData(0.25, 4)] // vz-move: 1.000 per cycle for PLAYER_VZ 0.25 at TIME_CORR 0.25–0.281
    [InlineData(2.375, 4)] // vz-rate at cycles=fixed 25000
    [InlineData(4.688, 4.688)] // vz-rate at cycles=fixed 12000
    [InlineData(8, 8)] // vz-slow and vz-rate: TIME_CORR 8
    public void Apply_VerticalVelocity_MovesByPlayerVzTimesTimeCorrectionButAtLeastFourTicks(
        double timeCorrection, double ticks)
    {
        // OV-20261019-eye-clamp: the eye moves by PLAYER_VZ × max(TIME_CORR, 4) per frame.
        (WdlRuntime runtime, Player player) = Create();
        runtime.SetSkill("PLAYER_VZ", 0.25);

        WdlPlayerMotion.Apply(runtime, player, timeCorrection);

        Assert.Equal(3 + (0.25 * ticks), player.Position.Y, 4);
        Assert.Equal(0.25, runtime.GetSkill("PLAYER_VZ"));
        Assert.Equal(3 + (0.25 * ticks), runtime.GetSkill("PLAYER_Z"), 4);
        Assert.Equal(0, runtime.GetSkill("FLOOR_HGT"));
        Assert.Equal(20, runtime.GetSkill("CEIL_HGT"));
        Assert.Equal(0.25 * ticks, runtime.GetSkill("PLAYER_HGT"), 4);
        Assert.Equal(0, runtime.GetSkill("IMPACT_VZ"));
    }

    [Fact]
    public void Apply_DownwardVelocity_SinksTheFeetAndStopsTheEyeAtTheFloorClamp()
    {
        // OV-20261019-eye-clamp, vz-move A: the eye fell past 3 (feet on the floor) to 0.35,
        // PLAYER_HGT −2.65; PLAYER_VZ kept its −0.25; IMPACT_VZ gained 0.35, then 1 per cycle.
        (WdlRuntime runtime, Player player) = Create();
        runtime.SetSkill("PLAYER_VZ", -0.25);

        WdlPlayerMotion.Apply(runtime, player, 0.25);
        WdlPlayerMotion.Apply(runtime, player, 0.25);
        Assert.Equal(1, player.Position.Y, 4);
        Assert.Equal(-2, runtime.GetSkill("PLAYER_HGT"), 4);
        Assert.True(player.Grounded);

        WdlPlayerMotion.Apply(runtime, player, 0.25);
        Assert.Equal(0.35, player.Position.Y, 4);
        Assert.Equal(0.35, runtime.GetSkill("IMPACT_VZ"), 4);

        WdlPlayerMotion.Apply(runtime, player, 0.25);
        Assert.Equal(0.35, player.Position.Y, 4);
        Assert.Equal(-0.25, runtime.GetSkill("PLAYER_VZ"));
        Assert.Equal(1.35, runtime.GetSkill("IMPACT_VZ"), 4);
        Assert.Equal(-2.65, runtime.GetSkill("PLAYER_HGT"), 4);
    }

    [Fact]
    public void Apply_UpwardVelocity_StopsTheEyeAtTheCeilingClamp()
    {
        // OV-20261019-eye-clamp, vz-move B: 15.650 under a ceiling of 16, IMPACT_VZ −0.35.
        (WdlRuntime runtime, Player player) = Create();
        player.Position = new System.Numerics.Vector3(5, 19, -5);
        runtime.SetSkill("PLAYER_VZ", 0.25);

        WdlPlayerMotion.Apply(runtime, player, 0.25);

        Assert.Equal(19.65f, player.Position.Y, 4);
        Assert.Equal(0.25, runtime.GetSkill("PLAYER_VZ"));
        Assert.Equal(-0.35, runtime.GetSkill("IMPACT_VZ"), 4);
        Assert.False(player.Grounded);
    }

    [Theory]
    [InlineData(0.1f, 0.35f)] // vz-move G: IMPACT_VZ +0.25
    [InlineData(19.95f, 19.65f)] // vz-move H (ceiling 16 there): IMPACT_VZ −0.3
    public void Apply_ScriptedEyeOutsideTheRegion_IsClampedAndCountedInImpact(float eye, float expected)
    {
        (WdlRuntime runtime, Player player) = Create();
        player.Position = new System.Numerics.Vector3(5, eye, -5);

        WdlPlayerMotion.Apply(runtime, player, 0.25);

        Assert.Equal(expected, player.Position.Y, 4);
        Assert.Equal(expected, runtime.GetSkill("PLAYER_Z"), 4);
        Assert.Equal(expected - eye, runtime.GetSkill("IMPACT_VZ"), 4);
    }

    [Fact]
    public void Synchronize_DirectVerticalAssignmentBelowFloor_PreservesNegativeHeight()
    {
        (WdlRuntime runtime, Player player) = Create();
        player.Position = new System.Numerics.Vector3(5, 1, -5);

        WdlPlayerMotion.Synchronize(runtime, player);

        Assert.Equal(1, player.Position.Y);
        Assert.Equal(-2, runtime.GetSkill("PLAYER_HGT"));
    }

    [Fact]
    public void Synchronize_SlopedRegionPublishesInterpolatedFloorAndCeilingHeights()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 10;#1\nVERTEX 10 10 10;#2\nVERTEX 0 10 0;#3\n" +
            "REGION ramp 2 12;#0\n" +
            "WALL a 0 1 0 -1 0 0;#0\nWALL b 1 2 0 -1 0 0;#1\n" +
            "WALL c 2 3 0 -1 0 0;#2\nWALL d 3 0 0 -1 0 0;#3\n" +
            "PLAYER_START 5 5 0 0;#4";
        // The vertex heights tilt the region only with its slope flags (OV-20261029-slope).
        (WdlRuntime runtime, Player player) = Create(map, "MAPFILE <m.wmp>; REGION ramp { FLAGS FLOOR_ASCEND, CEIL_ASCEND; }");

        WdlPlayerMotion.Synchronize(runtime, player);

        Assert.Equal(7, runtime.GetSkill("FLOOR_HGT"), 3);
        Assert.Equal(17, runtime.GetSkill("CEIL_HGT"), 3);
        Assert.Equal(0, runtime.GetSkill("PLAYER_HGT"), 3);
        Assert.Equal(1, runtime.GetSkill("SLOPE_AHEAD"), 3);
        Assert.Equal(0, runtime.GetSkill("SLOPE_SIDE"), 3);
    }

    [Fact]
    public void Apply_LivePlayerDimensions_UpdateCollisionConfiguration()
    {
        (WdlRuntime runtime, Player player) = Create();
        runtime.SetSkill("PLAYER_SIZE", 4);
        runtime.SetSkill("PLAYER_WIDTH", 2);
        runtime.SetSkill("PLAYER_CLIMB", 2.5);
        runtime.SetSkill("ACTOR_WIDTH", 1.5);
        runtime.SetSkill("THING_WIDTH", 0.75);

        WdlPlayerMotion.Apply(runtime, player, 0);

        Assert.Equal(4, player.EyeHeight);
        Assert.Equal(2, player.Width);

        runtime.SetSkill("PLAYER_WIDTH", 0.5); // below the manual's minimum of 1.2 (p.109)
        WdlPlayerMotion.Apply(runtime, player, 0);
        Assert.Equal(0.5, player.Width);
        Assert.Equal(2.5f, player.StepUp);
        Assert.Equal(1.5f, player.ActorWidthFactor);
        Assert.Equal(0.75f, player.ThingWidthFactor);
    }

    [Fact]
    public void Apply_DiveThroughLinkedSurface_ChangesRegionDispatchesHookAndUpdatesHere()
    {
        (WdlRuntime runtime, Player player) = CreateStackedRegions();
        Assert.Equal("under", RegionObject(runtime, 0).BelowName);
        WdlPlayerMotion.Synchronize(runtime, player);
        Assert.Equal(6, runtime.GetSkill("PLAYER_DEPTH"));
        runtime.SetSkill("PLAYER_VZ", -4);

        WdlPlayerMotion.Apply(runtime, player, 1);
        runtime.Tick(0);

        Assert.Equal(1, player.Region);
        Assert.Equal(1, runtime.GetSkill("dives"));
        Assert.Equal(0, runtime.GetSkill("enters"));
        Assert.Equal(0, runtime.GetSkill("leaves"));
        Assert.Same(RegionObject(runtime, 1), runtime.GetObject("here"));
        Assert.Equal(0, runtime.GetSkill("PLAYER_DEPTH"));
    }

    [Fact]
    public void Apply_AriseThroughLinkedSurface_ChangesRegionAndDispatchesLowerHook()
    {
        (WdlRuntime runtime, Player player) = CreateStackedRegions();
        player.MoveTo(1, 0, 0);
        player.Position = new System.Numerics.Vector3(0, 0, 0);
        WdlPlayerMotion.Synchronize(runtime, player);
        runtime.SetSkill("PLAYER_VZ", 2);

        WdlPlayerMotion.Apply(runtime, player, 1);

        Assert.Equal(0, player.Region);
        Assert.Equal(1, runtime.GetSkill("arises"));
        Assert.Same(RegionObject(runtime, 0), runtime.GetObject("here"));
        Assert.Equal(6, runtime.GetSkill("PLAYER_DEPTH"));
    }

    [Fact]
    public void Apply_LinkWithoutBothVerticalHooks_RemainsSolid()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\n" +
            "REGION water 1 20;#0\nREGION under -5 1;#1\nPLAYER_START 0 0 0 0;#2";
        const string main =
            "MAPFILE <m.wmp>; ACTION dive { } " +
            "REGION under { } REGION water { BELOW under; IF_DIVE dive; }";
        (WdlRuntime runtime, Player player) = Create(map, main);
        runtime.SetSkill("PLAYER_VZ", -10);

        WdlPlayerMotion.Apply(runtime, player, 1);

        Assert.Equal(0, player.Region);
        Assert.Equal(1.35f, player.Position.Y, 4);
        Assert.Equal(-10, runtime.GetSkill("PLAYER_VZ"));
    }

    private static (WdlRuntime Runtime, Player Player) Create()
        => Create(Map, "MAPFILE <m.wmp>;");

    private static (WdlRuntime Runtime, Player Player) CreateStackedRegions()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\n" +
            "REGION water 1 20;#0\nREGION under -5 1;#1\nPLAYER_START 0 0 0 0;#2";
        const string main =
            "MAPFILE <m.wmp>; " +
            "SKILL dives { VAL 0; } SKILL arises { VAL 0; } " +
            "SKILL enters { VAL 0; } SKILL leaves { VAL 0; } " +
            "ACTION dive { ADD dives,1; } ACTION arise { ADD arises,1; } " +
            "ACTION enter { ADD enters,1; } ACTION leave { ADD leaves,1; } " +
            "REGION under { IF_ARISE arise; IF_ENTER enter; } " +
            "REGION water { BELOW under; IF_DIVE dive; IF_LEAVE leave; }";
        return Create(map, main);
    }

    private static (WdlRuntime Runtime, Player Player) Create(string map, string main)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = map,
        };
        Level level = LevelLoader.LoadCore(
            "test.wdl",
            main,
            name => resources.GetValueOrDefault(Path.GetFileName(name)));
        var runtime = new WdlRuntime(level);
        runtime.SetSkill("PLAYER_SIZE", 3); // the scenarios were worked out for a 3-step player (V3.963 starts at 3.5)
        var player = new Player(level);
        runtime.RegisterObject("player", player);
        return (runtime, player);
    }

    private static OpenVirtue.Engine.Map.Region RegionObject(WdlRuntime runtime, int index) =>
        Assert.IsType<OpenVirtue.Engine.Map.Region>(runtime.GetObject(index == 0 ? "water" : "under"));
}
