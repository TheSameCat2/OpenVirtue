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
        "MAPFILE <m.wmp>; SKILL trace { VAL 0; } " +
        "ACTION g1 { RULE trace = trace * 10 + 1; } ACTION g2 { RULE trace = trace * 10 + 2; } " +
        "ACTION r { RULE trace = trace * 10 + 3; } " +
        "ACTION xaa { RULE trace = trace * 10 + 4; } ACTION xab { RULE trace = trace * 10 + 5; } " +
        "ACTION xta { RULE trace = trace * 10 + 6; } ACTION xtb { RULE trace = trace * 10 + 7; } " +
        "ACTION xwa { RULE trace = trace * 10 + 8; } ACTION xwb { RULE trace = trace * 10 + 9; } " +
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
        Assert.Equal(123456789, runtime.GetSkill("trace"));
    }
}
