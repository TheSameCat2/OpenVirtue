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
    public void Tick_SkipFrames_ActorInViewBeyondClipDistCountsAsUnseen()
    {
        // OV-20261019-clip-contact clip-move-liber: a LIBER actor in the view cone but beyond its
        // CLIP_DIST border is not drawn, so it moves every 6th cycle by six cycles' distance.
        (WdlRuntime runtime, Actor mover, _) = Create(
            "REGION room { CLIP_DIST 10; } ACTOR mover { TEXTURE moverTex; TARGET MOVE; SPEED 1; DIST 1; FLAGS LIBER; }");
        runtime.SetSkill("PLAYER_ANGLE", Math.PI);

        Assert.Equal([0, 0, 0, 0, 0, 6, 6], Positions(runtime, mover, 7));
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

    // The room of Map with the mover's way leading away from the player.
    private const string WayMap =
        "VERTEX -50 -50 0;#0\nVERTEX 50 -50 0;#1\nVERTEX 50 50 0;#2\nVERTEX -50 50 0;#3\n" +
        "REGION room 0 16;#0\n" +
        "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
        "ACTOR mover 0 0 0 0;#4\nWAY path 0 0 -30 0;#5\nPLAYER_START 40 0 0 0;#6";

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(50)]
    public void Tick_SkipFrames_StandingActorWithoutASpeedStaysDueAndMovesOnceGivenOne(int standingCycles)
    {
        // OV-20261020-view-activity rounds 6-9 (dist-in, ab-noread, pair-*): way actors declared SPEED 0
        // and given a speed in the trace window's first cycle took their first skipped step in its 7th
        // cycle, whatever the start-up before it: an actor without a speed makes no move, so its skip
        // count is never consumed; given a speed, it moves at once (arriving at the point it stands on)
        // and skips from there.
        (WdlRuntime runtime, Actor mover, _) = Create("ACTOR mover { TEXTURE moverTex; TARGET path; SPEED 0; DIST 1; }", WayMap);
        Assert.All(Positions(runtime, mover, standingCycles), x => Assert.Equal(0, x));

        mover["SPEED"] = 1;

        Assert.Equal([0, 0, 0, 0, 0, 0, -6, -6, -6, -6, -6, -6, -12], Positions(runtime, mover, 13));
    }

    [Fact]
    public void Tick_SkipFrames_FirstMoveAfterSkippingCoversTheSkippedCycles()
    {
        // OV-20261021-distance-refresh, turn-refresh: an actor that stops skipping between two of its
        // skipped moves covers the cycles skipped since its last move in its first move, then one a cycle.
        (WdlRuntime runtime, Actor mover, _) = Create("ACTOR mover { TEXTURE moverTex; TARGET MOVE; SPEED 1; DIST 1; }");
        Assert.Equal([0, 0, 0], Positions(runtime, mover, 3));

        runtime.SetSkill("PLAYER_ANGLE", Math.PI);
        Assert.Equal([4, 5, 6], Positions(runtime, mover, 3));
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
    [InlineData(20f, 1)] // 20 behind
    [InlineData(20.2f, 0)]
    public void Tick_SkipFrames_ActorsWithin20OfThePlayerMoveEveryCycle(float playerX, double expected)
    {
        // OV-20261020-view-activity, range-d1/d2, range-fine, edge-along and edge-across: behind the
        // player, actors up to 20 away moved every cycle with DIST 1 or 2, those farther every 6th;
        // the off-axis ones of range-shape (Euclidean 16.6 and 17.2, |dx| + |dy| 23 and 24) skipped.
        (WdlRuntime runtime, Actor mover, _) = Create("ACTOR mover { TEXTURE moverTex; TARGET MOVE; SPEED 0.2; DIST 1; }");
        ((Player)runtime.Objects["player"]).MoveTo(0, playerX, 0);

        runtime.Tick(Cycle);

        Assert.Equal(expected * WdlFixed.Literal(0.2), mover.X, 6); // SPEED 0.2 is held as 0.1992
    }

    // The player at the origin facing +X; a walker 22 behind it walking straight at it, and a pacer
    // in front of the player's plane, out of the cone and 40 away (both skip while far).
    private const string WalkerMap =
        "VERTEX -50 -50 0;#0\nVERTEX 50 -50 0;#1\nVERTEX 50 50 0;#2\nVERTEX -50 50 0;#3\n" +
        "REGION room 0 16;#0\n" +
        "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
        "ACTOR walker -22 0 0 0;#4\nACTOR pacer 10 30 90 0;#5\nPLAYER_START 0 0 0 0;#6";

    [Fact]
    public void Tick_SkipFrames_WalkerBehindThePlayerKeepsItsLastRefreshedDistance()
    {
        // OV-20261020-view-activity rounds 6-9, walk-in and solo-radial: an actor walking in from
        // behind skips all the way, its distance (22) never refreshed while nothing in front moves
        // and the player stands.
        (WdlRuntime runtime, Actor walker, _) = Create(
            "ACTOR walker { TEXTURE moverTex; TARGET MOVE; SPEED 0.5; DIST 1; } ACTOR pacer { TEXTURE moverTex; DIST 1; }",
            WalkerMap);

        List<double> positions = Positions(runtime, walker, 18);

        Assert.Equal(-22 + 9, positions[^1], 6); // three moves of 3, the last two from within 20
        Assert.Equal(-22 + 6, positions[^2], 6);
    }

    [Fact]
    public void Tick_SkipFrames_AMoveInFrontOfThePlayerRefreshesEveryDistance()
    {
        // OV-20261020-view-activity rounds 8-9, pair-offline and beam-back: when an actor in front of
        // the player's plane moves (even far off and unseen), every actor's distance is refreshed, and
        // the walker moves every cycle once within 20.
        (WdlRuntime runtime, Actor walker, _) = Create(
            "ACTOR walker { TEXTURE moverTex; TARGET MOVE; SPEED 0.5; DIST 1; } ACTOR pacer { TEXTURE moverTex; TARGET MOVE; SPEED 0.5; DIST 1; }",
            WalkerMap);

        List<double> positions = Positions(runtime, walker, 9);

        // A move of 3 to -19 at cycle 6, with the pacer's; refreshed, the walker moves every cycle from then on.
        Assert.Equal([-22, -19, -18.5, -18, -17.5], positions[4..]);
    }

    [Fact]
    public void Tick_SkipFrames_AWalkerOnThePlayersPlaneRefreshesNothing()
    {
        // OV-20261020-view-activity round 6, walk-in, and OV-20261021-distance-refresh near-hook: actors
        // walking in on the player's own plane (x = 0, the player facing +X) never refreshed the
        // distances; a far walker kept skipping past 20. The depth is a 22.10 value, so the float
        // angle's few hundred-millionths of a radian leave such a walker on the plane.
        (WdlRuntime runtime, Actor walker, _) = Create(
            "ACTOR walker { TEXTURE moverTex; TARGET MOVE; SPEED 0.5; DIST 1; } ACTOR pacer { TEXTURE moverTex; TARGET MOVE; SPEED 0.5; DIST 1; }",
            PlaneWalkerMap);
        runtime.SetSkill("PLAYER_ANGLE", 4.4e-8);

        List<double> positions = Positions(runtime, walker, 18);

        Assert.Equal([-22, -22, -22, -22, -22, -19, -19, -19, -19, -19, -19, -16, -16, -16, -16, -16, -16, -13], positions);
    }

    [Fact]
    public void Tick_SkipFrames_AStandingActorsFirstMoveInFrontRefreshesEveryDistance()
    {
        // OV-20261021-distance-refresh round 2, thing-refresh FW: the standing way actor in front of the
        // player was given a speed at cycle 82, every DISTANCE refreshed at 84, and it first stepped at
        // 90: its move of cycle 83, which only arrived at the point it stood on, counts as a move in
        // front, like the step itself.
        (WdlRuntime runtime, Actor walker, Level level) = Create(
            "ACTOR walker { TEXTURE moverTex; TARGET MOVE; SPEED 0.5; DIST 1; } ACTOR pacer { TEXTURE moverTex; TARGET post; SPEED 0; DIST 1; }",
            FrontWayMap);
        Assert.Equal(-19, Positions(runtime, walker, 6)[^1]); // one skipped move of 3; refreshed distance still 22

        level.Actors[1]["SPEED"] = 1;

        // The pacer arrives at its point in the next cycle (no step); the refresh shows in the cycle after,
        // where the walker, now 19 as refreshed, covers its skipped cycle and then moves every cycle.
        Assert.Equal([-19, -18, -17.5], Positions(runtime, walker, 3));
    }

    // WalkerMap with the pacer on the player's plane at (0, 30), walking along -Y toward it.
    private const string PlaneWalkerMap =
        "VERTEX -50 -50 0;#0\nVERTEX 50 -50 0;#1\nVERTEX 50 50 0;#2\nVERTEX -50 50 0;#3\n" +
        "REGION room 0 16;#0\n" +
        "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
        "ACTOR walker -22 0 0 0;#4\nACTOR pacer 0 30 270 0;#5\nPLAYER_START 0 0 0 0;#6";

    // WalkerMap with the pacer standing on the first point of a way in front of the player's plane.
    private const string FrontWayMap =
        "VERTEX -50 -50 0;#0\nVERTEX 50 -50 0;#1\nVERTEX 50 50 0;#2\nVERTEX -50 50 0;#3\n" +
        "REGION room 0 16;#0\n" +
        "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
        "ACTOR walker -22 0 0 0;#4\nACTOR pacer 10 30 90 0;#5\nWAY post 10 30 10 26;#6\nPLAYER_START 0 0 0 0;#7";

    [Fact]
    public void Tick_SkipFrames_APlayerMoveRefreshesEveryDistance()
    {
        // OV-20261020-view-activity round 8, walk-aside and locate-aside: the player's own move (a
        // walk or a beam) refreshes the distances too.
        (WdlRuntime runtime, Actor walker, _) = Create(
            "ACTOR walker { TEXTURE moverTex; TARGET MOVE; SPEED 0.5; DIST 1; } ACTOR pacer { TEXTURE moverTex; DIST 1; }",
            WalkerMap);
        Positions(runtime, walker, 6); // one move of 3, to -19: still 22 as last refreshed
        ((Player)runtime.Objects["player"]).MoveTo(0, 0.01f, 0);

        Assert.Equal([-18.5, -18, -17.5], Positions(runtime, walker, 3));
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
        // OV-20261020-view-activity, long-pad: a skipped CAREFULLY actor's step of 9 jumps a pad two
        // steps up when it lands beyond it. A step of 6 that would end half a step before the pad, within
        // the actor's half width, goes to the 1074-unit hold (OV-20261103-careful-stop: the walker from
        // 2.000 whose step ended 0.4 before the border stood at 10.951).
        const string map =
            "VERTEX -50 -50 0;#0\nVERTEX 50 -50 0;#1\nVERTEX 50 50 0;#2\nVERTEX -50 50 0;#3\n" +
            "VERTEX -10 4 0;#4\nVERTEX -6 4 0;#5\nVERTEX -6 8 0;#6\nVERTEX -10 8 0;#7\n" +
            "REGION room 0 16;#0\nREGION pad 2 16;#1\n" +
            "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
            "WALL rim 4 5 1 0 0 0;#4\nWALL rim 5 6 1 0 0 0;#5\nWALL rim 6 7 1 0 0 0;#6\nWALL rim 7 4 1 0 0 0;#7\n" +
            "ACTOR jumper -9 1 90 0;#8\nACTOR stopper -7 -2.5 90 0;#9\nPLAYER_START 40 0 0 0;#10";
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

        // 1.5 x 6 from y 1 to about 10, beyond the pad (y 4..8); sin 90° is 1023/1024 in 22.10 (OV-20261029-actor-step)
        Assert.Equal(1 + (9 * 1023.0 / 1024), jumper.Y, 4);
        Assert.Equal(0, jumper.Region);
        // 6 from y -2.5 would end at 3.5, within 1 of the pad's wall at 4: held 1074/1024 before it
        Assert.Equal(4 - (1074.0 / 1024), stopper.Y, 4);
    }

    [Fact]
    public void Tick_SkipFrames_LongCarefulStepFailingAtHalfLengthStopsAtTheFirstWall()
    {
        // OV-20261020-view-activity, long-pad: from 1 short of the pad both the step of 6 and its
        // half end inside the pad, so the step is swept and stops 1.05 short of the pad's wall.
        const string map =
            "VERTEX -50 -50 0;#0\nVERTEX 50 -50 0;#1\nVERTEX 50 50 0;#2\nVERTEX -50 50 0;#3\n" +
            "VERTEX -10 4 0;#4\nVERTEX -6 4 0;#5\nVERTEX -6 12 0;#6\nVERTEX -10 12 0;#7\n" +
            "REGION room 0 16;#0\nREGION pad 2 16;#1\n" +
            "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
            "WALL rim 4 5 1 0 0 0;#4\nWALL rim 5 6 1 0 0 0;#5\nWALL rim 6 7 1 0 0 0;#6\nWALL rim 7 4 1 0 0 0;#7\n" +
            "ACTOR stopper -7 1 90 0;#8\nPLAYER_START 40 0 0 0;#9";
        (WdlRuntime runtime, Actor stopper, _) = Create(
            "WALL rim { } ACTOR stopper { TEXTURE moverTex; TARGET MOVE; SPEED 1; DIST 1; FLAGS CAREFULLY; }",
            map);

        for (int i = 0; i < 6; i++)
        {
            runtime.Tick(Cycle);
        }

        Assert.InRange(stopper.Y, 4 - 1 - Player.WallClearance - (2.0 / 1024), 4 - 1 - Player.WallClearance + (2.0 / 1024));
    }

    [Theory]
    [InlineData(8, 4.8, 10.4)] // 12.8 is 0.8 beyond the wall: the half step
    [InlineData(4, 9.6, 13.6)] // 1.6 beyond it: through
    public void Tick_SkipFrames_LongCarefulStepEndingNearAFreeStandingWallIsRefused(double startY, double step, double expectedY)
    {
        // OV-20261020-view-activity, long-wall: an IMPASSABLE wall within the actor's half width of
        // the step's end refuses the step, behind the end as well as ahead of it; one the step jumps
        // and leaves further behind does not.
        string map =
            "VERTEX -50 -50 0;#0\nVERTEX 50 -50 0;#1\nVERTEX 50 50 0;#2\nVERTEX -50 50 0;#3\n" +
            "VERTEX -14 12 0;#4\nVERTEX -2 12 0;#5\n" +
            "REGION room 0 16;#0\n" +
            "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
            "WALL fence 4 5 0 0 0 0;#4\n" +
            "ACTOR walker -8 " + startY.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 90 0;#5\nPLAYER_START 40 0 0 0;#6";
        (WdlRuntime runtime, Actor walker, _) = Create(
            "WALL fence { FLAGS IMPASSABLE; } ACTOR walker { TEXTURE moverTex; TARGET MOVE; SPEED " +
            (step / 6).ToString(System.Globalization.CultureInfo.InvariantCulture) + "; DIST 1; FLAGS CAREFULLY; }",
            map);

        for (int i = 0; i < 6; i++)
        {
            runtime.Tick(Cycle);
        }

        Assert.Equal(expectedY, walker.Y, 1);
    }

    [Fact]
    public void Tick_SkipFrames_LongCarefulStepPastAnImpassableBorderPasses()
    {
        // OV-20261019-border-kinds imp-border, re-read 2026-11-03: the walker's six-cycle step from
        // 10.955 over the pad, whose walls are IMPASSABLE borders, lands 0.511 past the north border
        // (within its half width) at 20.511, as over the plain borders of actor-climb-2: only a
        // free-standing wall behind the end point refuses a step.
        const string map =
            "VERTEX -50 -50 0;#0\nVERTEX 50 -50 0;#1\nVERTEX 50 50 0;#2\nVERTEX -50 50 0;#3\n" +
            "VERTEX -12 12 0;#4\nVERTEX -4 12 0;#5\nVERTEX -4 20 0;#6\nVERTEX -12 20 0;#7\n" +
            "REGION room 0 16;#0\nREGION pad 0 16;#1\n" +
            "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
            "WALL border 4 5 1 0 0 0;#4\nWALL border 5 6 1 0 0 0;#5\nWALL border 6 7 1 0 0 0;#6\nWALL border 7 4 1 0 0 0;#7\n" +
            "ACTOR walker -10.5 10.955 90 0;#8\nPLAYER_START 40 0 0 0;#9";
        (WdlRuntime runtime, Actor walker, _) = Create(
            "WALL border { FLAGS IMPASSABLE; } ACTOR walker { TEXTURE moverTex; TARGET MOVE; SPEED 1.59375; DIST 1; FLAGS CAREFULLY; }",
            map);

        for (int i = 0; i < 6; i++)
        {
            runtime.Tick(Cycle);
        }

        // Six steps of 1632 units along the cut sine of 90 degrees (1023/1024): 20.507, not the half step's 15.7.
        Assert.InRange(walker.Y, 20.49, 20.53);
    }

    [Theory]
    [InlineData(2.0, 21.524)] // the quarter step still ends inside the pad: half the way to the hold
    [InlineData(2.5, 21.049)] // the quarter ends before the border within the half width: the hold
    [InlineData(3.0, 21.049)]
    [InlineData(4.0, 21.602)] // the quarter (2.398 along the cut sine) is clear
    [InlineData(4.5, 22.102)]
    public void Tick_SkipFrames_RefusedLongStep_StopsAsMeasured(double gap, double expectedY)
    {
        // OV-20261103-careful-stop north-*/south-*: a skipped CAREFULLY walker's six-cycle step toward a
        // pad 2 steps up is tried whole, half and quarter; a clear end is taken, one within the half
        // width before the border goes to the 1074-unit hold, and one still past the border at the
        // quarter moves half the way to the hold.
        string map =
            "VERTEX -50 -50 0;#0\nVERTEX 50 -50 0;#1\nVERTEX 50 50 0;#2\nVERTEX -50 50 0;#3\n" +
            "VERTEX -12 12 0;#4\nVERTEX -4 12 0;#5\nVERTEX -4 20 0;#6\nVERTEX -12 20 0;#7\n" +
            "REGION room 0 16;#0\nREGION pad 2 16;#1\n" +
            "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
            "WALL border 4 5 1 0 0 0;#4\nWALL border 5 6 1 0 0 0;#5\nWALL border 6 7 1 0 0 0;#6\nWALL border 7 4 1 0 0 0;#7\n" +
            "ACTOR walker -10.5 " + (20 + gap).ToString(System.Globalization.CultureInfo.InvariantCulture) + " 270 0;#8\nPLAYER_START 40 0 0 0;#9";
        (WdlRuntime runtime, Actor walker, _) = Create(
            "WALL border { } ACTOR walker { TEXTURE moverTex; TARGET MOVE; SPEED 1.6; DIST 1; FLAGS CAREFULLY; }",
            map);

        for (int i = 0; i < 6; i++)
        {
            runtime.Tick(Cycle);
        }

        Assert.Equal(expectedY, walker.Y, 2);
    }

    [Theory]
    [InlineData(49.5, 50 - (1074.0 / 1024))] // 0.5 from the edge: put at the hold, no step
    [InlineData(50.0, 50 - (1074.0 / 1024))] // on the edge
    [InlineData(49.0, 49 - (6 * 1.6 * 1023 / 1024))] // exactly its half width from it: its long step as usual
    public void Tick_SkipFrames_CarefulActorStandingWithinItsHalfWidthOfASolidWall_IsPutAtTheHoldInPlaceOfItsMove(double startY, double expectedY)
    {
        // OV-20261103-careful-stop north-3-35, north-4-45, south-2-3: walkers placed 0.5 from and on the
        // room's north edge stood 1.049 from it after their first move, with the 4/1024 nudge along x;
        // one exactly 1.0 from it took its step.
        string map =
            "VERTEX -50 -50 0;#0\nVERTEX 50 -50 0;#1\nVERTEX 50 50 0;#2\nVERTEX -50 50 0;#3\nREGION room 0 16;#0\n" +
            "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
            "ACTOR walker -10.5 " + startY.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 270 0;#4\nPLAYER_START 40 0 0 0;#5";
        (WdlRuntime runtime, Actor walker, _) = Create("ACTOR walker { TEXTURE moverTex; TARGET MOVE; SPEED 1.6; DIST 1; FLAGS CAREFULLY; }", map);

        for (int i = 0; i < 6; i++)
        {
            runtime.Tick(Cycle);
        }

        Assert.Equal(expectedY, walker.Y, 2);
        Assert.Equal(-10.5 + (4.0 / 1024), walker.X, 6);

        for (int i = 0; i < 6; i++)
        {
            runtime.Tick(Cycle);
        }

        // The next long step goes on from there (north-3-35's pushed walker stepped to the pad's hold six cycles later).
        Assert.Equal(expectedY - (6 * 1.6 * 1023 / 1024), walker.Y, 2);
    }

    [Fact]
    public void Tick_SkipFrames_PushedWalkerThenTakesItsRefusedStepToTheHold()
    {
        // OV-20261103-careful-stop north-3-35: the walker placed 0.5 from the room's north edge (y 24)
        // stood at 22.951 after its first move and at the pad's hold, 21.049, six cycles later.
        const string map =
            "VERTEX -16 -24 0;#0\nVERTEX 48 -24 0;#1\nVERTEX 48 24 0;#2\nVERTEX -16 24 0;#3\n" +
            "VERTEX -12 12 0;#4\nVERTEX -4 12 0;#5\nVERTEX -4 20 0;#6\nVERTEX -12 20 0;#7\n" +
            "REGION room 0 16;#0\nREGION pad 2 16;#1\n" +
            "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
            "WALL border 4 5 1 0 0 0;#4\nWALL border 5 6 1 0 0 0;#5\nWALL border 6 7 1 0 0 0;#6\nWALL border 7 4 1 0 0 0;#7\n" +
            "ACTOR walker -5.5 23.5 270 0;#8\nPLAYER_START 40 0 0 0;#9";
        (WdlRuntime runtime, Actor walker, _) = Create(
            "WALL border { } ACTOR walker { TEXTURE moverTex; TARGET MOVE; SPEED 1.6; DIST 1; FLAGS CAREFULLY; }",
            map);

        for (int i = 0; i < 6; i++)
        {
            runtime.Tick(Cycle);
        }

        Assert.Equal(24 - (1074.0 / 1024), walker.Y, 3);

        for (int i = 0; i < 6; i++)
        {
            runtime.Tick(Cycle);
        }

        Assert.Equal(20 + (1074.0 / 1024), walker.Y, 3);
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

    // ---- VISIBLE and SEEN of plain walls, things and actors -----------------------------------

    private static readonly string[] ProbeSkills = ["vf", "ef", "vh", "eh", "vw", "ew"];

    [Fact]
    public void Tick_PlainWallsAndActors_GetVisibleAndKeepSeen()
    {
        // OV-20261020-view-activity round 6, wall-seen: a plain wall ahead (front), one right behind
        // it (hidden), and an actor behind the player. VISIBLE follows the last drawn frame, SEEN
        // stays once set (p.53, p.71).
        const string map =
            "VERTEX 30 -4 0;#0\nVERTEX 30 4 0;#1\nVERTEX 40 -2 0;#2\nVERTEX 40 2 0;#3\nREGION room 0 16;#0\n" +
            "WALL front 0 1 0 0 0 0;#0\nWALL hidden 2 3 0 0 0 0;#1\nACTOR watcher -40 5 0 0;#2\nPLAYER_START 0 0 0 0;#3";
        (WdlRuntime runtime, _, _) = Create(
            "WALL front { } WALL hidden { } ACTOR watcher { TEXTURE moverTex; DIST 1; } " +
            "SYNONYM sf { TYPE WALL; } SYNONYM sh { TYPE WALL; } SYNONYM sw { TYPE ACTOR; } " +
            "SKILL vf { VAL 0; } SKILL ef { VAL 0; } SKILL vh { VAL 0; } SKILL eh { VAL 0; } SKILL vw { VAL 0; } SKILL ew { VAL 0; } " +
            "ACTION probe { SET sf, front; SET sh, hidden; SET sw, watcher; " +
            "RULE vf = sf.VISIBLE; RULE ef = sf.SEEN; RULE vh = sh.VISIBLE; RULE eh = sh.SEEN; RULE vw = sw.VISIBLE; RULE ew = sw.SEEN; }",
            map);
        var player = (Player)runtime.Objects["player"];
        runtime.SetEachTickAction(1, "probe");

        Assert.Equal([1, 1, 0, 0, 0, 0], Probe(runtime)); // S1: facing +X
        runtime.SetSkill("PLAYER_ANGLE", Math.PI);
        Assert.Equal([0, 1, 0, 0, 1, 1], Probe(runtime)); // S2: facing -X, the actor in view
        player.MoveTo(0, 35, 0);
        runtime.SetSkill("PLAYER_ANGLE", 0);
        Assert.Equal([0, 1, 1, 1, 0, 1], Probe(runtime)); // S3: between the walls
        player.MoveTo(0, 0, 0);
        Assert.Equal([1, 1, 0, 1, 0, 1], Probe(runtime)); // S4: back at the start

        static double[] Probe(WdlRuntime runtime)
        {
            runtime.Tick(Cycle);
            return [.. ProbeSkills.Select(runtime.GetSkill)];
        }
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
