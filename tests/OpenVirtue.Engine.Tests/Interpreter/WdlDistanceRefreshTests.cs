// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Map;

namespace OpenVirtue.Engine.Tests.Interpreter;

/// <summary>
/// When things' and actors' <c>DISTANCE</c> is refreshed and who reads the stale value
/// (OV-20261020-view-activity rule 5, OV-20261021-distance-refresh), and regions' <c>VISIBLE</c> and
/// <c>SEEN</c>. One <see cref="WdlRuntime.Tick"/> of 1/16 s is one cycle with <c>TIME_CORR</c> 1.
/// </summary>
public sealed class WdlDistanceRefreshTests
{
    private const double Cycle = 1.0 / 16;

    // A room 100 x 100, 16 high, the player at the origin facing +X. A post beside the player
    // (22 away, out of view), a thing behind it and an actor in front of its plane, each 11 away.
    private const string Map =
        "VERTEX -50 -50 0;#0\nVERTEX 50 -50 0;#1\nVERTEX 50 50 0;#2\nVERTEX -50 50 0;#3\n" +
        "REGION room 0 16;#0\n" +
        "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
        "ACTOR post 0 -22 0 0;#4\nTHING crate -7 4 0 0;#5\nACTOR guard 7 4 0 0;#6\nPLAYER_START 0 0 0 0;#7";

    private const string Declarations =
        "BMAP map, <m.pcx>, 0, 0, 32, 32; TEXTURE tex { BMAPS map; } " +
        "SKILL near_n { VAL 0; } SKILL far_n { VAL 0; } " +
        "ACTION on_near { RULE near_n = near_n + 1; } ACTION on_far { RULE far_n = far_n + 1; } " +
        "ACTOR post { TEXTURE tex; DIST 10; IF_NEAR on_near; IF_FAR on_far; } " +
        "THING crate { TEXTURE tex; DIST 10; } ACTOR guard { TEXTURE tex; DIST 10; }";

    [Fact]
    public void Tick_IfNearAndIfFar_ReadTheStaleDistanceUntilTheViewChanges()
    {
        // OV-20261021-distance-refresh, near-hook: actors that walked from 22 to 5.4 beside the
        // player fired no IF_NEAR until a beam refreshed their DISTANCE, nor IF_FAR on the way back
        // out until the beam back.
        (WdlRuntime runtime, Level level) = Create();
        Actor post = level.Actors[0];
        Tick(runtime, 2);

        post.Y = -5;
        Tick(runtime, 3);
        Assert.Equal(0, runtime.GetSkill("near_n"));
        Assert.Equal(22, post["DISTANCE"]);

        ((Player)runtime.Objects["player"]).MoveTo(0, 0.5f, 0);
        Tick(runtime, 1);
        Assert.Equal(1, runtime.GetSkill("near_n"));
        Assert.Equal(5.5, post["DISTANCE"], 6);

        post.Y = -22;
        Tick(runtime, 3);
        Assert.Equal(0, runtime.GetSkill("far_n"));

        runtime.SetSkill("PLAYER_ANGLE", 0.3);
        Tick(runtime, 1);
        Assert.Equal(1, runtime.GetSkill("far_n"));
    }

    [Theory]
    [InlineData("PLAYER_ANGLE", 0.3)] // turn-refresh
    [InlineData("PLAYER_TILT", 0.3)] // turn-refresh
    [InlineData("PLAYER_ARC", 0.8)] // thing-refresh
    public void Tick_AViewChangeRefreshesEveryDistance(string skill, double value)
    {
        // OV-20261021-distance-refresh, turn-refresh and thing-refresh: turning, tilting, lowering the
        // eye and narrowing PLAYER_ARC each refreshed every thing's and actor's DISTANCE once.
        (WdlRuntime runtime, Level level) = Create();
        Tick(runtime, 1);
        level.Actors[0].Y = -12;
        level.Things[0].X = -5;
        Tick(runtime, 2);
        Assert.Equal([22, 11], Distances(level));

        runtime.SetSkill(skill, value);
        Tick(runtime, 1);

        Assert.Equal([12, 9], Distances(level));
    }

    [Fact]
    public void Tick_AnEyeHeightChangeRefreshesEveryDistance()
    {
        // OV-20261021-distance-refresh, thing-refresh: lowering the eye (PLAYER_Z) refreshed too.
        (WdlRuntime runtime, Level level) = Create();
        Tick(runtime, 1);
        level.Actors[0].Y = -12;
        Tick(runtime, 1);
        var player = (Player)runtime.Objects["player"];
        player.EyeHeight = 2;
        player.MoveTo(0, 0, 0);

        Tick(runtime, 1);

        Assert.Equal(12, level.Actors[0]["DISTANCE"]);
    }

    [Fact]
    public void Tick_AScriptMoveRefreshesNothing()
    {
        // OV-20261021-distance-refresh, thing-move and set-actor: a script's SET of a thing's or an
        // actor's position, behind the player or in front of it, refreshed no DISTANCE, its own included.
        (WdlRuntime runtime, Level level) = Create();
        Tick(runtime, 1);

        level.Actors[0].Y = -12;
        level.Things[0].X = -5;
        level.Actors[1].X = 5;
        Tick(runtime, 3);

        Assert.Equal([22, 11, 11], [.. Distances(level), level.Actors[1]["DISTANCE"]]);
    }

    // The player at the origin facing +X; a walker 22 behind it walking straight at it with DIST 10.
    private const string WalkerMap =
        "VERTEX -50 -50 0;#0\nVERTEX 50 -50 0;#1\nVERTEX 50 50 0;#2\nVERTEX -50 50 0;#3\n" +
        "REGION room 0 16;#0\n" +
        "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
        "ACTOR walker -22 0 0 0;#4\nPLAYER_START 0 0 0 0;#5";

    [Fact]
    public void Tick_SkipFrames_TheDistLiftReadsTheStaleDistance()
    {
        // OV-20261021-distance-refresh, near-hook: an unseen DIST 10 actor kept skipping frames all the
        // way in to 5.4, its DISTANCE still 22.
        (WdlRuntime runtime, Level level) = Create(
            "BMAP map, <m.pcx>, 0, 0, 32, 32; TEXTURE tex { BMAPS map; } ACTOR walker { TEXTURE tex; TARGET MOVE; SPEED 0.5; DIST 10; }",
            WalkerMap);
        Actor walker = level.Actors[0];
        var positions = new List<double>();
        for (int i = 0; i < 36; i++)
        {
            runtime.Tick(Cycle);
            positions.Add(Math.Round(walker.X, 6));
        }

        // Moves of 3 every 6th cycle: within DIST from -7 on, still every 6th.
        Assert.Equal([-7, -7, -7, -7, -7, -4], positions[30..]);
        Assert.Equal(22, walker["DISTANCE"]);
    }

    // The room with the pad (x -12..-4, y 12..20) behind the player's left, both 16 high, and a
    // free-standing wall from (-10, -6) to (-6, -6) that hides the pad from (-8, -8).
    private const string RegionMap =
        "VERTEX -50 -50 0;#0\nVERTEX 50 -50 0;#1\nVERTEX 50 50 0;#2\nVERTEX -50 50 0;#3\n" +
        "VERTEX -12 12 0;#4\nVERTEX -4 12 0;#5\nVERTEX -4 20 0;#6\nVERTEX -12 20 0;#7\nVERTEX -10 -6 0;#8\nVERTEX -6 -6 0;#9\n" +
        "REGION room 0 16;#0\nREGION pad 0 16;#1\n" +
        "WALL edge 0 1 0 -1 0 0;#0\nWALL edge 1 2 0 -1 0 0;#1\nWALL edge 2 3 0 -1 0 0;#2\nWALL edge 3 0 0 -1 0 0;#3\n" +
        "WALL rim 4 5 1 0 0 0;#4\nWALL rim 5 6 1 0 0 0;#5\nWALL rim 6 7 1 0 0 0;#6\nWALL rim 7 4 1 0 0 0;#7\n" +
        "WALL block 8 9 0 0 0 0;#8\nPLAYER_START 0 0 0 0;#9";

    private static readonly string[] RegionProbeSkills = ["vp", "ep", "vr", "er"];

    [Fact]
    public void Tick_Regions_GetVisibleAndKeepSeen()
    {
        // OV-20261021-distance-refresh, region-seen, with the eye at 8: the pad is visible while some
        // of its floor or ceiling is on the screen and not behind a wall; at its nearest corner, inside
        // the cone but too near, the floor and ceiling lie beyond the screen's bottom and top. The room
        // the player stands in is visible throughout, and SEEN stays once set (p.53).
        (WdlRuntime runtime, _) = Create(
            "WALL rim { FLAGS INVISIBLE, PASSABLE; } WALL block { } " +
            "SYNONYM sp { TYPE REGION; } SYNONYM sr { TYPE REGION; } " +
            "SKILL vp { VAL 0; } SKILL ep { VAL 0; } SKILL vr { VAL 0; } SKILL er { VAL 0; } " +
            "ACTION probe { SET sp, pad; SET sr, room; RULE vp = sp.VISIBLE; RULE ep = sp.SEEN; RULE vr = sr.VISIBLE; RULE er = sr.SEEN; }",
            RegionMap);
        var player = (Player)runtime.Objects["player"];
        player.EyeHeight = 8;
        player.MoveTo(0, 0, 0);
        runtime.SetEachTickAction(1, "probe");

        Assert.Equal([0, 0, 1, 1], Probe(runtime, 0)); // R1: facing +X
        Assert.Equal([1, 1, 1, 1], Probe(runtime, 2.0344)); // R2: facing the pad
        Assert.Equal([0, 1, 1, 1], Probe(runtime, 0)); // R3
        Assert.Equal([0, 1, 1, 1], Probe(runtime, 1.35)); // R5: its nearest corner in the cone
        player.MoveTo(0, -8, -8);
        Assert.Equal([0, 1, 1, 1], Probe(runtime, Math.PI / 2)); // R6: behind the wall
        player.MoveTo(0, -3, -8);
        Assert.Equal([1, 1, 1, 1], Probe(runtime, Math.PI / 2)); // R7: past the wall's end

        static double[] Probe(WdlRuntime runtime, double angle)
        {
            runtime.SetSkill("PLAYER_ANGLE", angle);
            runtime.Tick(Cycle);
            return [.. RegionProbeSkills.Select(runtime.GetSkill)];
        }
    }

    private static double[] Distances(Level level) => [level.Actors[0]["DISTANCE"], level.Things[0]["DISTANCE"]];

    private static void Tick(WdlRuntime runtime, int cycles)
    {
        for (int i = 0; i < cycles; i++)
        {
            runtime.Tick(Cycle);
        }
    }

    private static (WdlRuntime Runtime, Level Level) Create(string declarations = Declarations, string map = Map)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        Level level = LevelLoader.LoadCore("x", "MAPFILE <m.wmp>; " + declarations, n => resources.GetValueOrDefault(Path.GetFileName(n)));
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", new Player(level));
        return (runtime, level);
    }
}
