// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Map;

namespace OpenVirtue.Engine.Tests.Interpreter;

/// <summary>
/// What the player's view changes in the runtime (ADR-0010): <c>SKIP_FRAMES</c> for actors and the
/// <c>BERKELEY</c> wall gate, as measured in OV-20261019-border-kinds, OV-20261019-clip-detail and
/// OV-20261020-view-activity. One <see cref="WdlRuntime.Tick"/> of 1/16 s is one cycle with
/// <c>TIME_CORR</c> 1.
/// </summary>
public sealed class WdlViewActivityTests
{
    private const double Cycle = 1.0 / 16;

    // A room 100 x 100, 16 high. A mover at the origin facing +X; the player 40 ahead of it.
    private const string Map =
        "VERTEX -50 -50 0;#0\nVERTEX 50 -50 0;#1\nVERTEX 50 50 0;#2\nVERTEX -50 50 0;#3\n" +
        "REGION room 0 16;#0\n" +
        "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
        "ACTOR mover 0 0 0 0;#4\nPLAYER_START 40 0 0 0;#5";

    private const string Texture = "BMAP moverMap, <m.pcx>, 0, 0, 32, 32; TEXTURE moverTex { BMAPS moverMap; } ";

    [Fact]
    public void Tick_SkipFrames_UnseenActorOutsideDistMovesEverySixthCycleBySixCycles()
    {
        // OV-20261020-view-activity, view-cone: behind the player the actor moves every 6th cycle by
        // six cycles' distance (SKIP_FRAMES 5, p.113); in view it moves every cycle.
        (WdlRuntime runtime, Actor mover, _) = Create("ACTOR mover { TEXTURE moverTex; TARGET MOVE; SPEED 1; DIST 1; }");

        Assert.Equal([0, 0, 0, 0, 0, 6, 6, 6, 6, 6, 6, 12], Positions(runtime, mover, 12));

        runtime.SetSkill("PLAYER_ANGLE", Math.PI);
        Assert.Equal([13, 14, 15], Positions(runtime, mover, 3));
        Assert.Equal(1, mover["VISIBLE"]);
    }

    [Fact]
    public void Tick_SkipFrames_CountStartsAtTheActorsLastMove()
    {
        // OV-20261020-view-activity, skip-phase: an actor the player turns away from moves again six
        // cycles after its last move, not in step with the other skipping actors.
        (WdlRuntime runtime, Actor mover, _) = Create("ACTOR mover { TEXTURE moverTex; TARGET MOVE; SPEED 1; DIST 1; }");
        runtime.SetSkill("PLAYER_ANGLE", Math.PI);
        Assert.Equal([1, 2, 3], Positions(runtime, mover, 3));

        runtime.SetSkill("PLAYER_ANGLE", 0);
        Assert.Equal([3, 3, 3, 3, 3, 9, 9], Positions(runtime, mover, 7));
        Assert.Equal(0, mover["VISIBLE"]);
    }

    [Theory]
    [InlineData("FLAGS FAR;", 5)] // p.71
    [InlineData("DIST 50;", 5)] // within DIST
    [InlineData("DIST 1;", 0)]
    public void Tick_SkipFrames_FarAndDistLiftIt(string extra, double expected)
    {
        // OV-20261020-view-activity, skip-phase: the FAR actor and the one within its DIST 30 moved
        // in every cycle while the player looked away.
        (WdlRuntime runtime, Actor mover, _) = Create($"ACTOR mover {{ TEXTURE moverTex; TARGET MOVE; SPEED 1; {extra} }}");

        Assert.Equal(expected, Positions(runtime, mover, 5)[^1]);
    }

    [Theory]
    [InlineData(19.8f, 1)] // the mover 19.8 behind the player
    [InlineData(20f, 0)] // 20 behind
    public void Tick_SkipFrames_ActorsWithin20OfThePlayerMoveEveryCycle(float playerX, double expected)
    {
        // OV-20261020-view-activity, range-d1/d2 and range-fine: behind the player, actors up to 19.8
        // away moved every cycle with DIST 1 or 2, those 20 or more away every 6th; the off-axis ones
        // of range-shape (Euclidean 16.6 and 17.2, |dx| + |dy| 23 and 24) skipped.
        (WdlRuntime runtime, Actor mover, _) = Create("ACTOR mover { TEXTURE moverTex; TARGET MOVE; SPEED 0.2; DIST 1; }");
        ((Player)runtime.Objects["player"]).MoveTo(0, playerX, 0);

        runtime.Tick(Cycle);

        Assert.Equal(expected * 0.2, mover.X, 6);
    }

    [Fact]
    public void Tick_SkipFrames_ZeroMovesUnseenActorsEveryCycle()
    {
        // OV-20261019-border-kinds, climb-skip0.
        (WdlRuntime runtime, Actor mover, _) = Create("SKILL SKIP_FRAMES { VAL 0; } ACTOR mover { TEXTURE moverTex; TARGET MOVE; SPEED 1; DIST 1; }");

        Assert.Equal([1, 2, 3], Positions(runtime, mover, 3));
    }

    [Fact]
    public void Tick_SkipFrames_ActorHiddenBehindAWallInTheConeSkips()
    {
        // OV-20261020-view-activity, view-occlude: in the cone but behind a free-standing wall.
        const string map =
            "VERTEX 20 -10 0;#0\nVERTEX 20 10 0;#1\nREGION room 0 16;#0\nWALL screen 0 1 0 0 0 0;#0\n" +
            "ACTOR mover 30 0 90 0;#1\nPLAYER_START 0 0 0 0;#2";
        (WdlRuntime runtime, Actor mover, _) = Create(
            "WALL screen { } ACTOR mover { TEXTURE moverTex; TARGET MOVE; SPEED 1; DIST 1; }", map);

        runtime.Tick(Cycle);

        Assert.Equal(30, mover.X, 6);
        Assert.Equal(0, mover.Y, 6);
        Assert.Equal(0, mover["VISIBLE"]);
    }

    [Fact]
    public void Tick_SkipFrames_LongCarefulStepIsTestedAtItsEndOnly()
    {
        // OV-20261020-view-activity, long-pad: a skipped CAREFULLY actor's step of 6 jumps a pad two
        // steps up when it lands beyond it, and stops 1.05 short of the pad's wall when it would land
        // inside it.
        const string map =
            "VERTEX -50 -50 0;#0\nVERTEX 50 -50 0;#1\nVERTEX 50 50 0;#2\nVERTEX -50 50 0;#3\n" +
            "VERTEX -10 4 0;#4\nVERTEX -6 4 0;#5\nVERTEX -6 8 0;#6\nVERTEX -10 8 0;#7\n" +
            "REGION room 0 16;#0\nREGION pad 2 16;#1\n" +
            "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
            "WALL rim 4 5 1 0 0 0;#4\nWALL rim 5 6 1 0 0 0;#5\nWALL rim 6 7 1 0 0 0;#6\nWALL rim 7 4 1 0 0 0;#7\n" +
            "ACTOR jumper -9 1 90 0;#8\nACTOR stopper -7 -1 90 0;#9\nPLAYER_START 40 0 0 0;#10";
        (WdlRuntime runtime, _, Level level) = Create(
            "WALL rim { } ACTOR jumper { TEXTURE moverTex; TARGET MOVE; SPEED 1.5; DIST 1; FLAGS CAREFULLY; } " +
            "ACTOR stopper { TEXTURE moverTex; TARGET MOVE; SPEED 1; DIST 1; FLAGS CAREFULLY; }",
            map);
        Actor jumper = level.Actors[0];
        Actor stopper = level.Actors[1];

        for (int i = 0; i < 6; i++)
        {
            runtime.Tick(Cycle);
        }

        Assert.Equal(1 + 9, jumper.Y, 4); // 1.5 x 6 from y 1 to 10, beyond the pad (y 4..8)
        Assert.Equal(0, jumper.Region);
        Assert.Equal(4 - 1 - Player.WallClearance, stopper.Y, 3); // 6 from y -1 would end at 5, inside
    }

    // ---- BERKELEY walls ----------------------------------------------------------------------

    // A BERKELEY wall from (10, -4) to (10, 4) with DIST 6, and a plain wall from (5, -6) to (5, 6) in front of it.
    private const string BerkeleyMap =
        "VERTEX 10 -4 0;#0\nVERTEX 10 4 0;#1\nVERTEX 5 -6 0;#2\nVERTEX 5 6 0;#3\nREGION room 0 16;#0\n" +
        "WALL sleeper 0 1 0 0 0 0;#0\nWALL screen 2 3 0 0 0 0;#1\nPLAYER_START 8 3 0 0;#2";

    [Theory]
    [InlineData(8, 3, 0, 1)] // N1: within DIST, in view
    [InlineData(8, 3, Math.PI, 1)] // N2: within DIST, looking away
    [InlineData(16, 0, Math.PI, 1)] // F1: 10 away, seen from behind
    [InlineData(16, 0, 0, 0)] // F2: 10 away, looking away
    [InlineData(0, 0, 0, 0)] // O1: in the cone, behind the plain wall, 14 away
    public void Tick_ObjectEachTick_BerkeleyWallRunsWhileInViewOrWithinDist(float x, float y, double angle, int expected)
    {
        // OV-20261020-view-activity, berkeley-near (p.53: "as long as it is viewed or the player is
        // within its border distance").
        Level level = Load(
            BerkeleyMap,
            "SKILL runs { VAL 0; } ACTION sleeper_tick { RULE runs += 1; } WALL screen { } " +
            "WALL sleeper { DIST 6; EACH_TICK sleeper_tick; FLAGS PASSABLE, BERKELEY; }");
        var runtime = new WdlRuntime(level);
        var player = new Player(level);
        runtime.RegisterObject("player", player);
        player.MoveTo(0, x, y);
        runtime.SetSkill("PLAYER_ANGLE", angle);

        runtime.Tick(Cycle);

        Assert.Equal(expected, runtime.GetSkill("runs"));
    }

    [Fact]
    public void Tick_ObjectEachTick_BerkeleyWallFollowsTheLastDrawnFrame()
    {
        // OV-20261019-clip-detail, berkeley-wall: the wall runs in the cycle the player turns away
        // (the frame drawn before the turn still showed it) and not in the next.
        Level level = Load(
            BerkeleyMap,
            "SKILL runs { VAL 0; } ACTION sleeper_tick { RULE runs += 1; } ACTION turn { SET PLAYER_ANGLE, 0; } " +
            "WALL screen { } WALL sleeper { DIST 1; EACH_TICK sleeper_tick; FLAGS PASSABLE, BERKELEY; }");
        var runtime = new WdlRuntime(level);
        var player = new Player(level);
        runtime.RegisterObject("player", player);
        player.MoveTo(0, 16, 0);
        runtime.SetSkill("PLAYER_ANGLE", Math.PI);
        runtime.SetEachTickAction(1, "turn");

        runtime.Tick(Cycle);
        runtime.Tick(Cycle);

        Assert.Equal(1, runtime.GetSkill("runs"));
        Assert.Equal(0, level.Walls[0]["VISIBLE"]);
    }

    private static List<double> Positions(WdlRuntime runtime, Actor mover, int cycles)
    {
        var positions = new List<double>();
        for (int i = 0; i < cycles; i++)
        {
            runtime.Tick(Cycle);
            positions.Add(Math.Round(mover.X, 6));
        }

        return positions;
    }

    private static (WdlRuntime Runtime, Actor Mover, Level Level) Create(string declarations, string map = Map)
    {
        Level level = Load(map, Texture + declarations);
        var runtime = new WdlRuntime(level);
        var player = new Player(level);
        runtime.RegisterObject("player", player);
        return (runtime, level.Actors[0], level);
    }

    private static Level Load(string map, string declarations)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        return LevelLoader.LoadCore("x", "MAPFILE <m.wmp>; " + declarations, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }
}
