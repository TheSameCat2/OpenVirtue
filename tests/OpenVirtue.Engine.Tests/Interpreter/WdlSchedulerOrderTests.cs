// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Interpreter;

namespace OpenVirtue.Engine.Tests.Interpreter;

/// <summary>
/// The order of a cycle's actions, as measured in OV-20261029-scheduler-order: the global slots in
/// slot order, then the region, the actors, the things and the walls, each kind from the last the
/// map places to the first.
/// </summary>
public class WdlSchedulerOrderTests
{
    // Two walls, two things and two actors, each placed b before a.
    private const string Map =
        "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nVERTEX 10 10 0;#2\nVERTEX 0 10 0;#3\n" +
        "VERTEX 4 2 0;#4\nVERTEX 4 4 0;#5\nVERTEX 6 2 0;#6\nVERTEX 6 4 0;#7\n" +
        "REGION room 0 10;#0\n" +
        "WALL wb 4 5 0 0 0 0;#0\nWALL wa 6 7 0 0 0 0;#1\n" +
        "THING tb 2 2 0 0;#2\nTHING ta 2 8 0 0;#3\nACTOR ab 8 2 0 0;#4\nACTOR aa 8 8 0 0;#5\n" +
        "PLAYER_START 5 5 0 0;#6";

    private const string Main =
        "MAPFILE <m.wmp>; SKILL n { VAL 0; } " +
        "SKILL o1 { VAL 0; } SKILL o2 { VAL 0; } SKILL o3 { VAL 0; } SKILL o4 { VAL 0; } SKILL o5 { VAL 0; } " +
        "SKILL o6 { VAL 0; } SKILL o7 { VAL 0; } SKILL o8 { VAL 0; } SKILL o9 { VAL 0; } " +
        "ACTION g1 { RULE n += 1; RULE o1 = n; } ACTION g2 { RULE n += 1; RULE o2 = n; } " +
        "ACTION r { RULE n += 1; RULE o3 = n; } " +
        "ACTION xaa { RULE n += 1; RULE o4 = n; } ACTION xab { RULE n += 1; RULE o5 = n; } " +
        "ACTION xta { RULE n += 1; RULE o6 = n; } ACTION xtb { RULE n += 1; RULE o7 = n; } " +
        "ACTION xwa { RULE n += 1; RULE o8 = n; } ACTION xwb { RULE n += 1; RULE o9 = n; } " +
        "REGION room { EACH_TICK r; } " +
        "WALL wa { EACH_TICK xwa; } WALL wb { EACH_TICK xwb; } " +
        "THING ta { EACH_TICK xta; } THING tb { EACH_TICK xtb; } " +
        "ACTOR aa { EACH_TICK xaa; } ACTOR ab { EACH_TICK xab; } " +
        "ACTION boot { SET EACH_TICK.2, g2; SET EACH_TICK.1, g1; } IF_START boot;";

    [Fact]
    public void Tick_RunsGlobalsThenRegionActorsThingsWallsLastPlacedFirst()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = Map };
        Level level = LevelLoader.LoadCore("x", Main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
        var runtime = new WdlRuntime(level);
        runtime.RunStartup();

        runtime.Tick(1.0 / 64);

        // g1 g2 room, actors aa ab (placed ab, aa), things ta tb (placed tb, ta), walls wa wb (placed wb, wa).
        Assert.Equal(
            [1.0, 2, 3, 4, 5, 6, 7, 8, 9],
            Enumerable.Range(1, 9).Select(i => runtime.GetSkill("o" + i)));
    }

    [Fact]
    public void Tick_RunsPlacedThingsAndActorsTogetherFromTheLastPlaced()
    {
        // OV-20261019-clip-contact round 2 liber-far-beam: placed actor, thing, actor, thing, the
        // original ran thing, actor, thing, actor: one reverse map order for both kinds, not the
        // actors first.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nVERTEX 10 10 0;#2\nVERTEX 0 10 0;#3\n" +
            "REGION room 0 10;#0\n" +
            "ACTOR aa 8 8 0 0;#0\nTHING ta 2 8 0 0;#1\nACTOR ab 8 2 0 0;#2\nTHING tb 2 2 0 0;#3\n" +
            "PLAYER_START 5 5 0 0;#4";
        const string main =
            "MAPFILE <m.wmp>; SKILL n { VAL 0; } " +
            "SKILL o1 { VAL 0; } SKILL o2 { VAL 0; } SKILL o3 { VAL 0; } SKILL o4 { VAL 0; } " +
            "ACTION xaa { RULE n += 1; RULE o4 = n; } ACTION xab { RULE n += 1; RULE o2 = n; } " +
            "ACTION xta { RULE n += 1; RULE o3 = n; } ACTION xtb { RULE n += 1; RULE o1 = n; } " +
            "THING ta { EACH_TICK xta; } THING tb { EACH_TICK xtb; } " +
            "ACTOR aa { EACH_TICK xaa; } ACTOR ab { EACH_TICK xab; }";
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
        var runtime = new WdlRuntime(level);
        runtime.RunStartup();

        runtime.Tick(1.0 / 64);

        // tb, ab, ta, aa.
        Assert.Equal([1.0, 2, 3, 4], Enumerable.Range(1, 4).Select(i => runtime.GetSkill("o" + i)));
    }
}
