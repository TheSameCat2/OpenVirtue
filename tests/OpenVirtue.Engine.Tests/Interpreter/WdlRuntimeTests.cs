// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Interpreter;
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
    public void Runtime_UnknownAction_ReturnsFalse()
    {
        var runtime = new WdlRuntime(Load("MAPFILE <m.wmp>;"));

        Assert.False(runtime.RunAction("nope"));
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

    private static Level Load(string main)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = MinimalMap };
        return LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }
}
