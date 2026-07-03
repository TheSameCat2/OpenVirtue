// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Tests.TestSupport;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Engine.Tests.Interpreter;

public class WdlRuntimeTests
{
    private const string MinimalMap =
        "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\nPLAYER_START 0 0 0 0;#1";

    [Fact]
    public void Runtime_SeedsSkillsFromLevel_AndRunsActions()
    {
        Level level = Load("MAPFILE <m.wmp>; SKILL health { VAL 100; } ACTION hurt { SET health, 50; }");
        var runtime = new WdlRuntime(level);

        Assert.Equal(100, runtime.GetSkill("health")); // seeded from the level's declared skill

        Assert.True(runtime.RunAction("hurt"));
        Assert.Equal(50, runtime.GetSkill("health"));   // the action mutated it
    }

    [Fact]
    public void Runtime_ActionUsingRuleAndIf()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL x { VAL 10; } SKILL y { VAL 0; } " +
            "ACTION step { RULE x += 5; IF (x > 12) { SET y, 1; } }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("step");

        Assert.Equal(15, runtime.GetSkill("x"));
        Assert.Equal(1, runtime.GetSkill("y"));
    }

    [Fact]
    public void Runtime_ActionCallsAnotherActionByName()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL x { VAL 0; } ACTION outer { inner; } ACTION inner { SET x, 7; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("outer"); // 'outer' body is just the statement `inner;`

        Assert.Equal(7, runtime.GetSkill("x"));
    }

    [Fact]
    public void Runtime_CallInstruction_CallsNamedAction()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL x { VAL 0; } ACTION outer { CALL inner; SET x, x + 1; } ACTION inner { SET x, 7; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("outer");

        Assert.Equal(8, runtime.GetSkill("x"));
    }

    [Fact]
    public void Runtime_EndInstruction_TerminatesAction()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL x { VAL 0; } ACTION stopEarly { SET x, 1; END; SET x, 2; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("stopEarly");

        Assert.Equal(1, runtime.GetSkill("x"));
    }

    [Fact]
    public void Runtime_GotoInstruction_JumpsToLabel()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL x { VAL 0; } ACTION loop { again: RULE x += 1; IF (x < 4) { GOTO again; } }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("loop");

        Assert.Equal(4, runtime.GetSkill("x"));
    }

    [Fact]
    public void Runtime_WhileInstruction_RepeatsUntilFalse()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL x { VAL 0; } ACTION loop { WHILE (x < 4) { RULE x += 1; } }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("loop");

        Assert.Equal(4, runtime.GetSkill("x"));
    }

    [Fact]
    public void Runtime_MutualRecursion_DoesNotStackOverflow()
    {
        // a -> b -> a -> ... is bounded by the call-depth guard.
        Level level = Load("MAPFILE <m.wmp>; ACTION a { b; } ACTION b { a; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("a"); // must return rather than overflow

        Assert.True(runtime.HasAction("a"));
    }

    [Fact]
    public void Runtime_RunStartup_RunsTheIfStartAction()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL booted { VAL 0; } ACTION boot { SET booted, 1; } IF_START boot;");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunStartup());
        Assert.Equal(1, runtime.GetSkill("booted"));
    }

    [Fact]
    public void Runtime_RunStartup_NoIfStart_ReturnsFalse()
    {
        var runtime = new WdlRuntime(Load("MAPFILE <m.wmp>;"));

        Assert.False(runtime.RunStartup());
    }

    [Fact]
    public void Runtime_RunStartup_AssignsEachTickAction_AndDispatchesOnFirstTick()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; " +
            "SKILL order { VAL 0; } SKILL ticks { VAL 0; } " +
            "ACTION boot { SET order, 1; SET EACH_TICK.1, probe_tick; } " +
            "ACTION probe_tick { RULE order = order * 10 + 2; RULE ticks += 1; SET EACH_TICK.1, NULL; } " +
            "IF_START boot;");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunStartup());
        Assert.Equal(1, runtime.GetSkill("order"));
        Assert.Equal(0, runtime.GetSkill("ticks"));

        runtime.Tick(1.0 / 16);
        Assert.Equal(12, runtime.GetSkill("order"));
        Assert.Equal(1, runtime.GetSkill("ticks"));

        runtime.Tick(1.0 / 16);
        Assert.Equal(12, runtime.GetSkill("order"));
        Assert.Equal(1, runtime.GetSkill("ticks"));
    }

    [Fact]
    public void Tick_DispatchesInitialEachTickActionsInOrder()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; " +
            "SKILL order { VAL 0; } " +
            "EACH_TICK first, second; " +
            "ACTION first { RULE order = order * 10 + 1; SET EACH_TICK.1, NULL; } " +
            "ACTION second { RULE order = order * 10 + 2; }");
        var runtime = new WdlRuntime(level);

        runtime.Tick(1.0 / 16);
        Assert.Equal(12, runtime.GetSkill("order"));

        runtime.Tick(1.0 / 16);
        Assert.Equal(122, runtime.GetSkill("order"));
    }

    [Fact]
    public void Runtime_ActionHooks_TriggerAndCanBeReassigned()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; " +
            "SKILL order { VAL 0; } " +
            "IF_F1 first; " +
            "ACTION first { RULE order = order * 10 + 1; } " +
            "ACTION second { RULE order = order * 10 + 2; } " +
            "ACTION rebind { SET IF_F1, second; } " +
            "ACTION clear { SET IF_F1, NULL; }");
        var runtime = new WdlRuntime(level);

        Assert.Equal("first", runtime.ActionHooks["IF_F1"]);
        Assert.True(runtime.TriggerActionHook("IF_F1"));
        Assert.Equal(1, runtime.GetSkill("order"));

        Assert.True(runtime.RunAction("rebind"));
        Assert.Equal("second", runtime.ActionHooks["IF_F1"]);
        Assert.True(runtime.TriggerActionHook("IF_F1"));
        Assert.Equal(12, runtime.GetSkill("order"));

        Assert.True(runtime.RunAction("clear"));
        Assert.Null(runtime.ActionHooks["IF_F1"]);
        Assert.False(runtime.TriggerActionHook("IF_F1"));
        Assert.Equal(12, runtime.GetSkill("order"));
    }

    [Fact]
    public void Runtime_UnknownAction_ReturnsFalse()
    {
        var runtime = new WdlRuntime(Load("MAPFILE <m.wmp>;"));

        Assert.False(runtime.RunAction("nope"));
    }

    [Fact]
    public void Runtime_RegistersUniquePlacedObjectsByName()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING plant 5 6 90 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; ACTION movePlant { SET plant.x, 9; RULE plant.y += 1; }");
        Thing plant = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);

        Assert.Same(plant, runtime.GetObject("plant"));
        Assert.Contains(plant, runtime.LevelObjects);

        Assert.True(runtime.RunAction("movePlant"));
        Assert.Equal(9, plant.X);
        Assert.Equal(7, plant.Y);
    }

    [Fact]
    public void Runtime_DuplicatePlacedObjectNames_ResolveToFirstPlacedObject()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING plant 5 6 90 0;#1\nTHING plant 7 8 90 0;#2\nPLAYER_START 0 0 0 0;#3";
        Level level = LoadWithMap(map, "MAPFILE <m.wmp>; ACTION movePlant { SET plant.X, 11; }");
        var runtime = new WdlRuntime(level);

        Assert.Equal(2, runtime.LevelObjects.Count);
        Assert.Same(level.Things[0], runtime.GetObject("plant"));
        Assert.False(runtime.Objects.ContainsKey("plant"));

        Assert.True(runtime.RunAction("movePlant"));
        Assert.Equal(11, level.Things[0].X);
        Assert.Equal(7, level.Things[1].X);
    }

    [Fact]
    public void Runtime_RegistersRegionsAndWallsByUniqueName()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "WALL gate 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; " +
            "REGION room { AMBIENT 0.25; } " +
            "ACTION updateWorld { RULE room.AMBIENT += 0.25; SET gate.OFFSET_X, 7; }");
        Region room = Assert.Single(level.Regions);
        Wall gate = Assert.Single(level.Walls);
        var runtime = new WdlRuntime(level);

        Assert.Same(room, runtime.GetObject("room"));
        Assert.Same(gate, runtime.GetObject("gate"));
        Assert.Empty(runtime.LevelObjects);

        Assert.True(runtime.RunAction("updateWorld"));
        Assert.Equal(0.5, room.Ambient);
        Assert.Equal(7, gate.OffsetX);
    }

    [Fact]
    public void Runtime_DuplicateWallNames_ResolveToFirstPlacedWall()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nVERTEX 2 0 0;#2\nREGION r 0 10;#0\n" +
            "WALL gate 0 1 0 -1 0 0;#0\nWALL gate 1 2 0 -1 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(map, "MAPFILE <m.wmp>; ACTION moveGate { SET gate.OFFSET_X, 5; }");
        var runtime = new WdlRuntime(level);

        Assert.Same(level.Walls[0], runtime.GetObject("gate"));
        Assert.False(runtime.Objects.ContainsKey("gate"));

        Assert.True(runtime.RunAction("moveGate"));
        Assert.Equal(5, level.Walls[0].OffsetX);
        Assert.Equal(0, level.Walls[1].OffsetX);
    }

    [Fact]
    public void Runtime_SetAll_AppliesToEveryMatchingObjectExceptPositions()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nVERTEX 2 0 0;#2\nREGION r 0 10;#0\n" +
            "WALL gate 0 1 0 -1 0 0;#0\nWALL gate 1 2 0 -1 0 0;#1\n" +
            "THING plant 5 6 90 0;#2\nTHING plant 7 8 90 0;#3\nPLAYER_START 0 0 0 0;#4";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; " +
            "ACTION markAll { SET_ALL plant.FLAG3, 1; SET_ALL plant.DIST, 7; SET_ALL plant.X, 99; SET_ALL gate.FLAG2, 1; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("markAll"));

        Assert.All(level.Things, thing =>
        {
            Assert.Equal(1, thing["flag3"]);
            Assert.Equal(7, thing["dist"]);
        });
        Assert.Equal(5, level.Things[0].X);
        Assert.Equal(7, level.Things[1].X);
        Assert.All(level.Walls, wall => Assert.Equal(1, wall["flag2"]));
    }

    [Fact]
    public void Runtime_Set_AssignsDocumentedTextureKeywords()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nVERTEX 2 0 0;#2\n" +
            "REGION tank 0 10;#0\nREGION tank 1 11;#1\n" +
            "WALL gate 0 1 0 -1 0 0;#0\nTHING plant 5 6 90 0;#2\nPLAYER_START 0 0 0 0;#3";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; " +
            "REGION tank { FLOOR_TEX oldFloor; CEIL_TEX oldCeil; } " +
            "WALL gate { TEXTURE oldWall; } THING plant { TEXTURE oldPlant; } " +
            "ACTION swapTextures { SET tank.CEIL_TEX, water_tex; SET_ALL tank.FLOOR_TEX, lava_tex; " +
            "SET gate.TEXTURE, NULL; SET plant.TEXTURE, fern_tex; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("swapTextures"));

        Assert.Equal("water_tex", level.Regions[0].CeilTexture);
        Assert.Equal("oldCeil", level.Regions[1].CeilTexture);
        Assert.All(level.Regions, region => Assert.Equal("lava_tex", region.FloorTexture));
        Assert.Null(level.Walls[0].Texture);
        Assert.Equal("fern_tex", level.Things[0].Texture);
    }

    [Fact]
    public void Runtime_If_ComparesDocumentedTextureKeywordsAndNull()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\n" +
            "REGION tank 0 10;#0\nTHING plant 5 6 90 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; " +
            "SKILL matched { VAL 0; } SKILL missing { VAL 0; } SKILL notMatched { VAL 0; } " +
            "REGION tank { FLOOR_TEX water_tex; } " +
            "SYNONYM selected { TYPE TEXTURE; DEFAULT water_tex; } " +
            "ACTION checkTextures { IF (tank.FLOOR_TEX == selected) { SET matched, 1; } " +
            "IF (plant.TEXTURE == NULL) { SET missing, 1; } " +
            "IF (tank.FLOOR_TEX == lava_tex) { SET notMatched, 1; } ELSE { SET notMatched, 2; } }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("checkTextures"));

        Assert.Equal(1, runtime.GetSkill("matched"));
        Assert.Equal(1, runtime.GetSkill("missing"));
        Assert.Equal(2, runtime.GetSkill("notMatched"));
    }

    [Fact]
    public void Runtime_TextureSynonym_CanReceiveObjectTextureKeyword()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\n" +
            "REGION lift_1 0 10;#0\nPLAYER_START 0 0 0 0;#1";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; " +
            "REGION lift_1 { FLOOR_TEX floor_tex; CEIL_TEX old_ceil; } " +
            "SYNONYM lift_syn1 { TYPE REGION; } SYNONYM lift_syn2 { TYPE REGION; } " +
            "SYNONYM lift_floor { TYPE TEXTURE; } " +
            "ACTION lift1_to_lift2 { SET lift_syn1, lift_1; SET lift_syn2, lift_syn1; " +
            "SET lift_floor, lift_syn2.FLOOR_TEX; SET lift_1.CEIL_TEX, lift_floor; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("lift1_to_lift2"));

        Assert.Equal("floor_tex", runtime.KeywordReferences["lift_floor"]);
        Assert.Equal("floor_tex", level.Regions[0].CeilTexture);
    }

    [Fact]
    public void Runtime_ObjectSynonym_SeedsDefaultReferenceByType()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\n" +
            "REGION lift_1 0 10;#0\nPLAYER_START 0 0 0 0;#1";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; " +
            "SYNONYM lift_syn { TYPE REGION; DEFAULT lift_1; } " +
            "ACTION raiseLift { RULE lift_syn.FLOOR_HGT += 2; }");
        var runtime = new WdlRuntime(level);

        Assert.Same(level.Regions[0], runtime.GetObject("lift_syn"));
        Assert.True(runtime.RunAction("raiseLift"));

        Assert.Equal(2, level.Regions[0].FloorHeight);
    }

    [Fact]
    public void Runtime_ActionSynonym_CallsDefaultAndAssignedAction()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL result { VAL 0; } " +
            "SYNONYM selected { TYPE ACTION; DEFAULT first; } " +
            "ACTION first { RULE result = result * 10 + 1; } " +
            "ACTION second { RULE result = result * 10 + 2; } " +
            "ACTION drive { CALL selected; SET selected, second; selected; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("drive"));

        Assert.Equal(12, runtime.GetSkill("result"));
    }

    [Fact]
    public void Runtime_StringSynonym_ReadsAndWritesAssignedString()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; " +
            "STRING source,\"alpha\"; STRING target,\".....\"; STRING sink,\".....\"; " +
            "SYNONYM active { TYPE STRING; DEFAULT source; } " +
            "ACTION editStrings { SET_STRING sink, active; SET active, target; " +
            "SET_STRING active, \"beta\"; ADD_STRING active, \"!\"; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("editStrings"));

        Assert.Equal("alpha", runtime.Strings["sink"].Value);
        Assert.Equal("alpha", runtime.Strings["source"].Value);
        Assert.Equal("beta!", runtime.Strings["target"].Value);
    }

    [Fact]
    public void Runtime_NextThere_CyclesThroughRegionsWithSameName()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\n" +
            "REGION lift1 0 10;#0\nREGION lift1 1 11;#1\nREGION lift1 2 12;#2\n" +
            "PLAYER_START 0 0 0 0;#3";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL visits { VAL 0; } " +
            "ACTION liftAll { SET THERE,lift1; loop1: RULE THERE.FLOOR_HGT += 1; RULE visits += 1; " +
            "NEXT_THERE; IF (THERE != lift1) { GOTO loop1; } }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("liftAll"));

        Assert.Equal([1, 2, 3], level.Regions.Select(region => (int)region.FloorHeight));
        Assert.Equal(3, runtime.GetSkill("visits"));
        Assert.Same(level.Regions[0], runtime.GetObject("there"));
    }

    [Fact]
    public void Runtime_NextMy_CyclesThroughObjectsWithSameName()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING plant 5 6 90 0;#1\nTHING plant 7 8 90 0;#2\nPLAYER_START 0 0 0 0;#3";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL visits { VAL 0; } " +
            "ACTION markPlants { SET MY,plant; loop1: RULE MY.SKILL1 += 1; RULE visits += 1; " +
            "NEXT_MY; IF (MY != plant) { GOTO loop1; } }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("markPlants"));

        Assert.All(level.Things, thing => Assert.Equal(1, thing["skill1"]));
        Assert.Equal(2, runtime.GetSkill("visits"));
        Assert.Same(level.Things[0], runtime.GetObject("my"));
    }

    [Fact]
    public void Runtime_NextMyThere_CyclesThroughObjectsInSameRegion()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\n" +
            "REGION r 0 10;#0\nREGION r 0 10;#1\n" +
            "THING first 5 6 90 0;#0\nTHING second 7 8 90 0;#0\nTHING third 9 10 90 1;#1\n" +
            "PLAYER_START 0 0 0 0;#4";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; ACTION hop { SET MY,first; NEXT_MY_THERE; SET seen, MY.x; NEXT_MY_THERE; SET back, MY.x; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("hop"));

        Assert.Equal(7, runtime.GetSkill("seen"));
        Assert.Equal(5, runtime.GetSkill("back"));
    }

    [Fact]
    public void Runtime_StringInstructions_CopyAppendConvertAndParse()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; " +
            "STRING source,\"abcdef\"; STRING target,\".....\"; STRING cat,\".....\"; STRING number,\"....\"; " +
            "SKILL value { VAL 12.5; } SKILL parsed { VAL 0; } " +
            "ACTION editStrings { SET_STRING target,source; SET_STRING cat,\"ab\"; ADD_STRING cat,\"cdef\"; " +
            "TO_STRING number,value; SET_SKILL parsed,number; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("editStrings"));

        Assert.Equal("abcde", runtime.Strings["target"].Value);
        Assert.Equal("abcde", runtime.Strings["cat"].Value);
        Assert.Equal("12.5", runtime.Strings["number"].Value);
        Assert.Equal(12.5, runtime.GetSkill("parsed"));
    }

    [Fact]
    public void Runtime_PrintInstructions_AppendToSelectedTextFile()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "OpenVirtue-print-" + Guid.NewGuid().ToString("N"));
        try
        {
            Level level = Load(
                "MAPFILE <m.wmp>; " +
                "STRING line,\"hello\\nworld\"; SKILL value { VAL 12.3456; } " +
                "ACTION log { PRINTFILE \"ovstate\", 7; PRINT_VALUE value; PRINT_STRING line; PRINT_STRING \"done\\n\"; }");
            var runtime = new WdlRuntime(level, outputDir);

            Assert.True(runtime.RunAction("log"));
            Assert.True(runtime.RunAction("log"));

            string outputPath = Path.Combine(outputDir, "ovsta007.TXT");
            string text = File.ReadAllText(outputPath);
            string expectedRun =
                "12.346" + Environment.NewLine +
                "hello" + Environment.NewLine +
                "worlddone" + Environment.NewLine;
            Assert.Equal(expectedRun + expectedRun, text);
        }
        finally
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Runtime_RegisterObject_AddsExplicitReference()
    {
        Level level = Load("MAPFILE <m.wmp>; ACTION moveMe { SET my.x, 11; }");
        var runtime = new WdlRuntime(level);
        var thing = new Thing("scratch");

        runtime.RegisterObject("my", thing);
        Assert.True(runtime.RunAction("moveMe"));

        Assert.Equal(11, thing.X);
    }

    [Fact]
    public void Runtime_ObjectActions_ReadDocumentedEntityFieldsAndUniversalSkills()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING pickup 2 3 90 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL result { VAL 0; } " +
            "THING pickup { HEIGHT 2; SKILL1 3; DIST 4; MAP_COLOR 5; FLAGS INVISIBLE; } " +
            "ACTION readPickup { RULE result = my.HEIGHT + my.SKILL1 + my.REGION + my.DIST + my.MAP_COLOR + my.THING + my.INVISIBLE; }");
        Thing pickup = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);

        runtime.RegisterObject("my", pickup);

        Assert.True(runtime.RunAction("readPickup"));
        Assert.Equal(16, runtime.GetSkill("result"));
    }

    [Fact]
    public void Runtime_PlayerReference_CanBeRegisteredForScripts()
    {
        Level level = Load("MAPFILE <m.wmp>; ACTION nudgePlayer { SET player.x, 12; RULE player.y += 3; }");
        var runtime = new WdlRuntime(level);
        var player = new Player(level);

        runtime.RegisterObject("player", player);
        Assert.True(runtime.RunAction("nudgePlayer"));

        Assert.Equal(12, player["x"]);
        Assert.Equal(3, player["y"]);
    }

    /// <summary>
    /// Boots and ticks every local retail level when user-supplied game data is present.
    /// This guards the current runtime skeleton without asserting hidden scheduler parity.
    /// </summary>
    [Fact]
    public void Runtime_LocalRetailArchives_BootAndTickWithoutThrowing()
    {
        IReadOnlyList<string> archives = ResearchData.WrsFiles();
        if (archives.Count == 0)
        {
            return;
        }

        foreach (string path in archives)
        {
            Level level = LevelLoader.Load(WrsArchive.ReadFile(path), ResearchData.MainWdlName(path));
            var runtime = new WdlRuntime(level);

            runtime.RunStartup();
            for (int i = 0; i < 3; i++)
            {
                runtime.Tick(1.0 / 16);
            }

            Assert.Equal(1.0, runtime.GetSkill("TIME_CORR"), 9);
        }
    }

    [Fact]
    public void Runtime_RealApathy_HasManyActions()
    {
        string? apathy = ResearchData.WrsFiles()
            .FirstOrDefault(p => Path.GetFileName(p).Equals("apathy.wrs", StringComparison.OrdinalIgnoreCase));
        if (apathy is null)
        {
            return; // no local game data — skip
        }

        Level level = LevelLoader.Load(WrsArchive.ReadFile(apathy), "APATHY.WDL");

        Assert.NotEmpty(level.Actions);
    }

    [Fact]
    public void Tick_SetsTimeCorrectionSkill()
    {
        var runtime = new WdlRuntime(Load("MAPFILE <m.wmp>;"));

        double tc = runtime.Tick(1.0 / 16); // a 16 fps frame

        Assert.Equal(1.0, tc, 9);
        Assert.Equal(1.0, runtime.GetSkill("TIME_CORR"), 9);
    }

    [Fact]
    public void Tick_TimeCorrection_ScalesScriptMovement()
    {
        // pos += speed * TIME_CORR — the canonical frame-rate-independent step.
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL pos { VAL 0; } SKILL speed { VAL 32; } " +
            "ACTION move { RULE pos += speed * TIME_CORR; }");
        var runtime = new WdlRuntime(level);

        runtime.Tick(1.0 / 16); // TIME_CORR = 1   -> full step
        runtime.RunAction("move");
        Assert.Equal(32, runtime.GetSkill("pos"), 9);

        runtime.Tick(1.0 / 32); // TIME_CORR = 0.5 -> half step
        runtime.RunAction("move");
        Assert.Equal(48, runtime.GetSkill("pos"), 9); // 32 + 16
    }

    [Fact]
    public void Tick_DispatchesRegisteredEachTickAction_WithMyAndTimeCorrection()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING runner 5 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL speed { VAL 4; } SKILL cycles { VAL 0; } " +
            "ACTION cycle { RULE my.x += speed * TIME_CORR; RULE cycles += 1; }");
        Thing runner = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);
        var originalMy = new Thing("originalMy");

        runtime.RegisterObject("my", originalMy);
        runtime.RegisterEachTick(runner, "cycle");

        runtime.Tick(1.0 / 16);
        Assert.Equal(9, runner.X);
        Assert.Equal(1, runtime.GetSkill("cycles"));

        runtime.Tick(1.0 / 32);
        Assert.Equal(11, runner.X);
        Assert.Equal(2, runtime.GetSkill("cycles"));
        Assert.Same(originalMy, runtime.GetObject("my"));
    }

    [Fact]
    public void Tick_DispatchesDocumentedEntityEachTickAction_WithMyAndTimeCorrection()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING runner 5 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL speed { VAL 4; } SKILL ticks { VAL 0; } " +
            "THING runner { EACH_TICK object_tick; EACH_CYCLE object_cycle; } " +
            "ACTION object_tick { RULE my.x += speed * TIME_CORR; RULE ticks += 1; } " +
            "ACTION object_cycle { RULE ticks += 100; }");
        Thing runner = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);
        var originalMy = new Thing("originalMy");

        runtime.RegisterObject("my", originalMy);

        runtime.Tick(1.0 / 16);
        Assert.Equal(9, runner.X);
        Assert.Equal(1, runtime.GetSkill("ticks"));

        runtime.Tick(1.0 / 32);
        Assert.Equal(11, runner.X);
        Assert.Equal(2, runtime.GetSkill("ticks"));
        Assert.Same(originalMy, runtime.GetObject("my"));
    }

    [Fact]
    public void SetSkill_ClampsAssignmentsToDeclaredBounds()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL myHealth { VAL 100; MAX 100; MIN 0; } " +
            "ACTION overheal { RULE myHealth += 50; } ACTION drain { RULE myHealth -= 1000; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("overheal");
        Assert.Equal(100, runtime.GetSkill("myHealth")); // pinned at MAX, not 150

        runtime.RunAction("drain");
        Assert.Equal(0, runtime.GetSkill("myHealth"));   // pinned at MIN, not -900
    }

    [Fact]
    public void SetSkill_WithoutDeclaredBounds_DoesNotClamp()
    {
        Level level = Load("MAPFILE <m.wmp>; SKILL freeVal { VAL 0; } ACTION big { SET freeVal, 99999; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("big");
        Assert.Equal(99999, runtime.GetSkill("freeVal"));
    }

    [Fact]
    public void Runtime_RealApathy_BootsAndTicksWithoutThrowing()
    {
        string? apathy = ResearchData.WrsFiles()
            .FirstOrDefault(p => Path.GetFileName(p).Equals("apathy.wrs", StringComparison.OrdinalIgnoreCase));
        if (apathy is null)
        {
            return; // no local game data — skip
        }

        Level level = LevelLoader.Load(WrsArchive.ReadFile(apathy), "APATHY.WDL");
        var runtime = new WdlRuntime(level);

        runtime.RunStartup(); // run the level's real IF_START path — must not throw
        for (int i = 0; i < 3; i++)
        {
            runtime.Tick(1.0 / 16);
        }

        Assert.Equal(1.0, runtime.GetSkill("TIME_CORR"), 9);
    }

    private static Level Load(string main)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = MinimalMap };
        return LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }

    private static Level LoadWithMap(string map, string main)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        return LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }
}
