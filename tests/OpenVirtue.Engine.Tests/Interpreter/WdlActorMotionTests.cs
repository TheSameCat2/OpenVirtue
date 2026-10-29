// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Interpreter;

public sealed class WdlActorMotionTests
{
    private const string Map =
        "VERTEX -10 -10 0;#0\nVERTEX 10 -10 0;#1\nVERTEX 10 10 0;#2\nVERTEX -10 10 0;#3\n" +
        "REGION room 0 20;#0\n" +
        "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\n" +
        "WALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
        "ACTOR mover 0 0 0 0;#4\nTHING marker 6 0 0 0;#5\nPLAYER_START 8 0 0 0;#6";

    [Fact]
    public void Tick_TargetMove_AdvancesAlongFacingWithVerticalSlope()
    {
        (WdlRuntime runtime, Level level) = Create(
            "ACTOR mover { TARGET MOVE; SPEED 2; VSPEED 0.5; }");
        Actor actor = Assert.Single(level.Actors);

        runtime.Tick(1.0 / 16);

        Assert.Equal(2, actor.X, 6);
        Assert.Equal(0, actor.Y, 6);
        Assert.Equal(1, actor.Z, 6);
        Assert.Equal(1, actor["MOVED"]);
    }

    [Fact]
    public void Tick_InvisibleActor_IsInactiveUntilScriptMakesItVisible()
    {
        (WdlRuntime runtime, Level level) = Create(
            "SKILL objectTicks { VAL 0; } SKILL hits { VAL 0; } " +
            "ACTION ticked { ADD objectTicks,1; } ACTION hit { ADD hits,1; } " +
            "BMAP p1,<p1.pcx>,0,0,16,16; BMAP p2,<p2.pcx>,0,0,16,16; " +
            "TEXTURE animated { CYCLES 2; BMAPS p1,p2; } " +
            "ACTOR mover { TEXTURE animated; TARGET MOVE; SPEED 2; " +
            "EACH_TICK ticked; IF_HIT hit; FLAGS INVISIBLE; }");
        Actor actor = Assert.Single(level.Actors);

        runtime.Tick(1.0 / 16);

        Assert.Equal(0, actor.X);
        Assert.Equal(0, runtime.GetSkill("objectTicks"));
        Assert.Equal(0, actor["VISIBLE"]);
        Assert.Equal(1, actor["CYCLE"]);
        Assert.False(runtime.TriggerObjectActionHook(actor, "IF_HIT"));
        Assert.Equal(0, runtime.GetSkill("hits"));
        Assert.Empty(SpriteBuilder.Build(level));

        actor["INVISIBLE"] = 0;
        runtime.Tick(1.0 / 16);

        Assert.Equal(2, actor.X, 6);
        Assert.Equal(1, runtime.GetSkill("objectTicks"));
        Assert.Equal(1, actor["VISIBLE"]);
        Assert.Equal(1, actor["CYCLE"]); // its first phase counts from the next cycle (OV-20261029-anim-timing)
        Assert.Single(SpriteBuilder.Build(level));
    }

    [Fact]
    public void Tick_TargetFollow_UpdatesTargetCoordinatesAndMovesTowardPlayer()
    {
        (WdlRuntime runtime, Level level) = Create(
            "ACTOR mover { TARGET FOLLOW; SPEED 2; }");
        var player = new Player(level);
        runtime.RegisterObject("player", player);
        Actor actor = Assert.Single(level.Actors);

        runtime.Tick(1.0 / 16);

        Assert.Equal(2, actor.X, 6);
        Assert.Equal(8, actor["TARGET_X"]);
        Assert.Equal(0, actor["TARGET_Y"]);
    }

    [Fact]
    public void Tick_TargetFollow_AdjustsHeightUsingPositiveVspeedLimit()
    {
        (WdlRuntime runtime, Level level) = Create(
            "ACTOR mover { TARGET FOLLOW; SPEED 2; VSPEED 1; }");
        var player = new Player(level)
        {
            Position = new System.Numerics.Vector3(8, 5, 0),
        };
        runtime.RegisterObject("player", player);
        Actor actor = Assert.Single(level.Actors);

        runtime.Tick(1.0 / 16);

        Assert.Equal(1, actor.Z, 6);
    }

    [Fact]
    public void Tick_TargetVertex_StopsAtTargetAndDispatchesArrivalInEveryCycleThere()
    {
        (WdlRuntime runtime, Level level) = Create(
            "SKILL arrivals { VAL 0; } ACTION arrived { ADD arrivals,1; } " +
            "ACTOR mover { TARGET VERTEX; TARGET_X 3; TARGET_Y 4; SPEED 10; IF_ARRIVED arrived; }");
        Actor actor = Assert.Single(level.Actors);

        runtime.Tick(1.0 / 16);
        runtime.Tick(1.0 / 16);

        // Arrived in the first cycle and still there in the second (OV-20261029-actor-events).
        Assert.Equal(3, actor.X, 6);
        Assert.Equal(4, actor.Y, 6);
        Assert.Equal(2, runtime.GetSkill("arrivals"));
    }

    [Fact]
    public void Tick_AssignedWay_CyclesWaypointsAndDispatchesEveryArrival()
    {
        string map = Map.Replace(
            "PLAYER_START 8 0 0 0;#6",
            "WAY patrol 2 0 4 0;#0\nPLAYER_START 8 0 0 0;#6",
            StringComparison.Ordinal);
        (WdlRuntime runtime, Level level) = Create(
            "SKILL arrivals { VAL 0; } ACTION arrived { ADD arrivals,1; } " +
            "ACTOR mover { WAY patrol; SPEED 2; VSPEED 99; IF_ARRIVED arrived; }",
            map);
        Actor actor = Assert.Single(level.Actors);

        runtime.Tick(1.0 / 16);

        Assert.Equal(2, actor.X, 6);
        Assert.Equal(0, actor.Z, 6);
        Assert.Equal(2, actor["WAYPOINT"]);
        // WAYPOINT moves on before IF_ARRIVED; TARGET_X still shows the point reached until the next
        // step (OV-20261029-actor-events).
        Assert.Equal(2, actor["TARGET_X"]);
        Assert.Equal(1, runtime.GetSkill("arrivals"));

        runtime.Tick(1.0 / 16);
        runtime.Tick(1.0 / 16);

        Assert.Equal(2, actor.X, 6);
        Assert.Equal(2, actor["WAYPOINT"]);
        Assert.Equal(3, runtime.GetSkill("arrivals"));
    }

    [Fact]
    public void Tick_NamedObjectTarget_MovesTowardObjectPosition()
    {
        (WdlRuntime runtime, Level level) = Create(
            "THING marker { } ACTOR mover { TARGET marker; SPEED 2; }");
        Actor actor = Assert.Single(level.Actors);

        runtime.Tick(1.0 / 16);

        Assert.Equal(2, actor.X, 6);
        Assert.Equal(6, actor["TARGET_X"]);
        Assert.Equal(0, actor["TARGET_Y"]);
    }

    [Fact]
    public void Tick_NamedObjectTarget_AdjustsHeightUsingVspeed()
    {
        (WdlRuntime runtime, Level level) = Create(
            "THING marker { HEIGHT 4; } ACTOR mover { TARGET marker; SPEED 2; VSPEED 2; }");
        Actor actor = Assert.Single(level.Actors);

        runtime.Tick(1.0 / 16);

        Assert.Equal(2, actor.Z, 6);
    }

    [Fact]
    public void Tick_NamedObjectTarget_DispatchesArrivalAfterReachingTargetHeight()
    {
        (WdlRuntime runtime, Level level) = Create(
            "SKILL arrivals { VAL 0; } ACTION arrived { ADD arrivals,1; } " +
            "THING marker { HEIGHT 4; } " +
            "ACTOR mover { TARGET marker; SPEED 10; VSPEED 1; IF_ARRIVED arrived; }");

        runtime.Tick(1.0 / 16);

        Assert.Equal(0, runtime.GetSkill("arrivals"));

        runtime.Tick(1.0 / 16);
        runtime.Tick(1.0 / 16);
        runtime.Tick(1.0 / 16);
        runtime.Tick(1.0 / 16);

        Assert.Equal(4, Assert.Single(level.Actors).Z, 6);
        double arrivals = runtime.GetSkill("arrivals");
        Assert.True(arrivals >= 1);
        runtime.Tick(1.0 / 16);
        Assert.Equal(arrivals + 1, runtime.GetSkill("arrivals")); // again in every cycle there
    }

    [Fact]
    public void Tick_SetGround_PreservesWorldHeightInBothDirections()
    {
        (WdlRuntime runtime, Level level) = Create(
            "ACTION toggleGround { IF (my.GROUND == 0) { SET my.GROUND,1; } " +
            "ELSE { SET my.GROUND,0; } } " +
            "ACTOR mover { HEIGHT 2; EACH_TICK toggleGround; }");
        level.Regions[0].FloorHeight = 4;
        Actor actor = Assert.Single(level.Actors);

        runtime.Tick(1.0 / 16);

        Assert.Equal(1, actor["GROUND"]);
        Assert.Equal(6, actor.Z, 6);

        runtime.Tick(1.0 / 16);

        Assert.Equal(0, actor["GROUND"]);
        Assert.Equal(2, actor.Z, 6);
    }

    [Fact]
    public void Tick_CarefulBullet_AvoidsThingAndDispatchesHitWithImpactSkills()
    {
        (WdlRuntime runtime, Level level) = Create(
            "SKILL hits { VAL 0; } SKILL observedImpact { VAL 0; } " +
            "ACTION hit { ADD hits,1; } ACTION observe { SET observedImpact,ACTOR_IMPACT_VX; } " +
            "THING marker { } " +
            "ACTOR mover { TARGET BULLET; SPEED 10; FLAGS CAREFULLY,SENSITIVE; " +
            "IF_HIT hit; EACH_TICK observe; }");
        Actor actor = Assert.Single(level.Actors);

        runtime.Tick(1.0 / 16);

        Assert.Equal(5, actor.X, 3);
        Assert.Equal(-5, actor["IMPACT_VX"], 3);
        Assert.Equal(1, runtime.GetSkill("hits"));
        Assert.Equal(-5, runtime.GetSkill("observedImpact"), 3);
        Assert.Equal(1, actor["MOVED"]);
    }

    [Fact]
    public void Tick_CarefulActor_PublishesInterpolatedFloorAndCeilingHeights()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 10;#1\nVERTEX 10 10 10;#2\nVERTEX 0 10 0;#3\n" +
            "REGION ramp 2 12;#0\n" +
            "WALL a 0 1 0 -1 0 0;#0\nWALL b 1 2 0 -1 0 0;#1\n" +
            "WALL c 2 3 0 -1 0 0;#2\nWALL d 3 0 0 -1 0 0;#3\n" +
            "ACTOR mover 5 5 0 0;#4\nPLAYER_START 5 5 0 0;#5";
        (WdlRuntime runtime, _) = Create(
            "SKILL observedFloor { VAL 0; } SKILL observedCeiling { VAL 0; } " +
            "ACTION observe { SET observedFloor,ACTOR_FLOOR_HGT; SET observedCeiling,ACTOR_CEIL_HGT; } " +
            "REGION ramp { FLAGS FLOOR_ASCEND, CEIL_ASCEND; } " +
            "ACTOR mover { TARGET MOVE; SPEED 0; FLAGS CAREFULLY; EACH_TICK observe; }",
            map);

        runtime.Tick(1.0 / 16);

        Assert.Equal(7, runtime.GetSkill("observedFloor"), 3);
        Assert.Equal(17, runtime.GetSkill("observedCeiling"), 3);
    }

    [Fact]
    public void Tick_CarefulActorCrossingRegionBorder_DispatchesArrivalForAnyTargetMode()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nVERTEX 10 10 0;#2\nVERTEX 0 10 0;#3\n" +
            "VERTEX 20 0 0;#4\nVERTEX 20 10 0;#5\n" +
            "REGION left 0 20;#0\nREGION right 0 20;#1\n" +
            "WALL edge 0 1 0 -1 0 0;#0\nWALL portal 1 2 0 1 0 0;#1\n" +
            "WALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
            "WALL edge 1 4 1 -1 0 0;#4\nWALL edge 4 5 1 -1 0 0;#5\n" +
            "WALL edge 5 2 1 -1 0 0;#6\n" +
            "ACTOR mover 5 5 0 0;#7\nPLAYER_START 2 5 0 0;#8";
        (WdlRuntime runtime, Level level) = Create(
            "SKILL arrivals { VAL 0; } ACTION arrived { ADD arrivals,1; } " +
            "ACTOR mover { TARGET VERTEX; TARGET_X 15; TARGET_Y 5; SPEED 6; " +
            "FLAGS CAREFULLY; IF_ARRIVED arrived; }",
            map);

        runtime.Tick(1.0 / 16);

        Actor actor = Assert.Single(level.Actors);
        Assert.Equal(1, actor.Region);
        Assert.Equal(11, actor.X, 3);
        Assert.Equal(1, runtime.GetSkill("arrivals"));
    }

    [Fact]
    public void Tick_CarefulMove_StopsAtSolidWallWhenObstacleIsInvisible()
    {
        (WdlRuntime runtime, Level level) = Create(
            "THING marker { FLAGS INVISIBLE; } " +
            "ACTOR mover { TARGET MOVE; SPEED 20; FLAGS CAREFULLY; }");
        Actor actor = Assert.Single(level.Actors);

        runtime.Tick(1.0 / 16);

        // OV-20261019-border-kinds: a CAREFULLY actor stops 0.05 beyond its radius, like the player.
        Assert.Equal(Math.Truncate((9.45) * 1024) / 1024, actor.X, 3); // cut to 1/1024 at the wall (OV-20261029-actor-step)
        Assert.Equal(-10.55, actor["IMPACT_VX"], 3);
        Assert.Equal(0, runtime.GetSkill("hits"));
    }

    [Fact]
    public void Tick_CarefulMove_AvoidsPassableThingThatOnlyPlayerCanCross()
    {
        (WdlRuntime runtime, Level level) = Create(
            "THING marker { FLAGS PASSABLE; } " +
            "ACTOR mover { TARGET MOVE; SPEED 10; FLAGS CAREFULLY; }");
        Actor actor = Assert.Single(level.Actors);

        runtime.Tick(1.0 / 16);

        Assert.Equal(5, actor.X, 3);
        Assert.Equal(-5, actor["IMPACT_VX"], 3);
    }

    private const string StepMap =
        "VERTEX -10 -10 0;#0\nVERTEX 30 -10 0;#1\nVERTEX 30 10 0;#2\nVERTEX -10 10 0;#3\n" +
        "VERTEX 10 -5 0;#4\nVERTEX 20 -5 0;#5\nVERTEX 20 5 0;#6\nVERTEX 10 5 0;#7\n" +
        "REGION room 0 20;#0\nREGION pad {0} 20;#1\n" +
        "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
        "WALL rim 4 5 1 0 0 0;#4\nWALL rim 5 6 1 0 0 0;#5\nWALL rim 6 7 1 0 0 0;#6\nWALL rim 7 4 1 0 0 0;#7\n" +
        "ACTOR mover 0 0 0 0;#8\nPLAYER_START -8 -8 0 0;#9";

    private static string StepMapWithPad(int padFloor) =>
        StepMap.Replace("{0}", padFloor.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    [Theory]
    [InlineData(1, "", 32, true)] // the default ACTOR_CLIMB is 1.1 (climb-1)
    [InlineData(1, "SKILL ACTOR_CLIMB { VAL 1; } ", 32, false)] // a rise equal to it blocks (climb-eq)
    [InlineData(1, "SKILL ACTOR_CLIMB { VAL 0.5; } ", 32, false)] // climb-actor-05
    [InlineData(2, "SKILL ACTOR_CLIMB { VAL 3; } ", 32, false)] // not as high as the 2-high actor (actor-climb-2)
    [InlineData(2, "SKILL ACTOR_CLIMB { VAL 3; } ", 128, true)] // an 8-high actor climbs it (climb-tall)
    public void Tick_CarefulMove_ClimbsARiseBelowActorClimbAndItsOwnHeight(int padFloor, string climb, int bitmapHeight, bool climbs)
    {
        // OV-20261019-border-kinds: a CAREFULLY actor enters a raised region only when the floor rises
        // less than ACTOR_CLIMB and less than the actor's height; otherwise the region border holds it.
        (WdlRuntime runtime, Level level) = Create(
            climb +
            $"BMAP stepMap, <step.pcx>, 0, 0, 32, {bitmapHeight}; TEXTURE stepTex {{ BMAPS stepMap; }} " +
            "ACTOR mover { TEXTURE stepTex; TARGET MOVE; SPEED 1; FLAGS CAREFULLY; }",
            StepMapWithPad(padFloor));
        Actor actor = Assert.Single(level.Actors);

        for (int i = 0; i < 14; i++)
        {
            runtime.Tick(1.0 / 16);
        }

        if (climbs)
        {
            Assert.Equal(14, actor.X, 3);
            Assert.Equal(1, actor.Region);
        }
        else
        {
            Assert.Equal(Math.Truncate((10 - 1 - Player.WallClearance) * 1024) / 1024, actor.X, 3); // cut to 1/1024 at the wall (OV-20261029-actor-step)
            Assert.Equal(0, actor.Region);
        }
    }

    [Fact]
    public void Tick_CarefulMove_ImpassableWallStopsTheActorEvenWhenPassable()
    {
        // OV-20261019-border-kinds, imp-pass-border.
        (WdlRuntime runtime, Level level) = Create(
            "WALL rim { FLAGS IMPASSABLE, PASSABLE; } " +
            "BMAP stepMap, <step.pcx>, 0, 0, 32, 32; TEXTURE stepTex { BMAPS stepMap; } " +
            "ACTOR mover { TEXTURE stepTex; TARGET MOVE; SPEED 1; FLAGS CAREFULLY; }",
            StepMapWithPad(0));
        Actor actor = Assert.Single(level.Actors);

        for (int i = 0; i < 14; i++)
        {
            runtime.Tick(1.0 / 16);
        }

        Assert.Equal(Math.Truncate((10 - 1 - Player.WallClearance) * 1024) / 1024, actor.X, 3); // cut to 1/1024 at the wall (OV-20261029-actor-step)
    }

    [Fact]
    public void Tick_TargetStick_CapturesInitialRelationshipAndFollowsPlayerMovement()
    {
        (WdlRuntime runtime, Level level) = Create(
            "ACTOR mover { TARGET STICK; SPEED 2; }");
        var player = new Player(level);
        runtime.RegisterObject("player", player);
        Actor actor = Assert.Single(level.Actors);

        runtime.Tick(1.0 / 16);
        player.MoveTo(0, 6, 0);
        runtime.Tick(1.0 / 16);

        Assert.Equal(8, actor["REL_DIST"], 6);
        Assert.Equal(Math.PI, actor["REL_ANGLE"], 6);
        Assert.Equal(-2, actor.X, 6);
    }

    [Fact]
    public void Tick_TargetStick_HonorsExplicitRelativePosition()
    {
        (WdlRuntime runtime, Level level) = Create(
            "ACTOR mover { TARGET STICK; REL_DIST 2; REL_ANGLE 0; SPEED 3; }");
        var player = new Player(level);
        runtime.RegisterObject("player", player);
        Actor actor = Assert.Single(level.Actors);

        runtime.Tick(1.0 / 16);

        Assert.Equal(3, actor.X, 6);
        Assert.Equal(10, actor["TARGET_X"], 6);
        Assert.Equal(0, actor["TARGET_Y"], 6);
    }

    [Theory]
    [InlineData(1, 0.25, 69, 108)]
    [InlineData(1, 0.28125, 78, 121)]
    [InlineData(0.78515625, 0.25, 91, 90)]
    [InlineData(2, 0.28125, -60, 131)]
    [InlineData(1, 1.6875, 467, 726)] // six skipped cycles at once: exact trig would give 727
    [InlineData(0.78515625, 1.5, 543, 542)]
    [InlineData(1.5703125, 1.5, 0, 767)]
    public void Tick_Bullet_StepsByRoundedCosineAndSineOfItsAngle(double angle, double timeCorrection, int dxRaw, int dyRaw)
    {
        // OV-20261029-actor-step: SPEED 0.5 · TIME_CORR, split by cos and sin of the 22.10 angle,
        // themselves cut toward zero to 22.10, each part rounded to a whole 1/1024 step (the
        // measured raw steps).
        (WdlRuntime runtime, Level level) = Create("ACTOR mover { SPEED 0.5; }");
        Actor actor = Assert.Single(level.Actors);
        actor.Angle = angle - (actor.PlacementAngleDegrees * Math.PI / 180);
        actor.Target = "BULLET";
        runtime.Tick(1.0 / 16); // the first cycle runs at TIME_CORR 1
        (double x, double y) = (actor.X, actor.Y);

        runtime.Tick(timeCorrection / 16);

        Assert.Equal(dxRaw, Math.Round((actor.X - x) * 1024), 6);
        Assert.Equal(dyRaw, Math.Round((actor.Y - y) * 1024), 6);
    }

    private static (WdlRuntime Runtime, Level Level) Create(string declarations, string map = Map)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = map,
        };
        Level level = LevelLoader.LoadCore(
            "test.wdl",
            "MAPFILE <m.wmp>; " + declarations,
            name => resources.GetValueOrDefault(Path.GetFileName(name)));
        var runtime = new WdlRuntime(level);
        // The mover starts behind the player start; SKIP_FRAMES would hold it (OV-20261020-view-activity).
        runtime.SetSkill("SKIP_FRAMES", 0);
        return (runtime, level);
    }
}
