// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text.Json;
using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Tests.TestSupport;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Engine.Tests.Interpreter;

/// <summary>
/// The 16 global <c>EACH_SEC</c> slots and the whole-second <c>SECS</c> count, as measured in
/// OV-20261015-each-sec-cadence. Time only advances through
/// <see cref="WdlRuntime.Tick"/>/<see cref="WdlRuntime.TickFlic"/>, so one second is exactly 16
/// ticks of 1/16 s (sixteen 1/16 steps sum to exactly 1.0 in binary floating point).
/// </summary>
public class WdlRuntimeEachSecTests
{
    private const double OneTick = 1.0 / 16;

    private const string MinimalMap =
        "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\nPLAYER_START 0 0 0 0;#1";

    [Fact]
    public void Runtime_RunStartup_AssignsEachSecAction_AndRunsOnEverySixteenthTick()
    {
        WdlRuntime runtime = Start(
            "SKILL n { VAL 0; } ACTION boot { SET EACH_SEC.1, sec; } ACTION sec { RULE n += 1; } IF_START boot;");

        Assert.Equal("sec", runtime.GetEachSecAction(1));
        Ticks(runtime, 15);
        Assert.Equal(0, runtime.GetSkill("n"));
        Ticks(runtime, 1);
        Assert.Equal(1, runtime.GetSkill("n"));
        Ticks(runtime, 15);
        Assert.Equal(1, runtime.GetSkill("n"));
        Ticks(runtime, 1);
        Assert.Equal(2, runtime.GetSkill("n"));
        Assert.Equal(32, runtime.GetSkill("TICKS"));
        Assert.Equal(2, runtime.GetSkill("SECS"));
    }

    [Fact]
    public void Tick_SecsRisesAfterResumedWaitsAndRightBeforeTheEachSecSlots()
    {
        // The probe's phase trace: in the frame where the second completes, an action resuming from
        // WAITT still reads the old SECS, and the EACH_SEC action that runs later in that frame
        // reads the new one; an EACH_TICK action after it reads the new one too. Armed in IF_START,
        // the WAITT 17 counts IF_START and the start-up cycle (TIME_CORR 1 each,
        // OV-20261029-scheduler-order), then frames 1-15, and resumes in frame 16.
        WdlRuntime runtime = Start(
            "SKILL resumed_secs { VAL -1; } SKILL sec_secs { VAL -1; } SKILL tick_secs { VAL -1; } " +
            "ACTION boot { SET EACH_SEC.1, sec; WAITT 17; SET resumed_secs, SECS; } " +
            "ACTION sec { SET sec_secs, SECS; SET EACH_SEC.1, NULL; SET EACH_TICK.1, tick; } " +
            "ACTION tick { SET tick_secs, SECS; SET EACH_TICK.1, NULL; } " +
            "IF_START boot;");

        Ticks(runtime, 15);
        Assert.Equal(0, runtime.GetSkill("SECS"));
        Assert.Equal(-1, runtime.GetSkill("resumed_secs"));

        Ticks(runtime, 1);
        Assert.Equal(0, runtime.GetSkill("resumed_secs"));
        Assert.Equal(1, runtime.GetSkill("sec_secs"));
        Assert.Equal(1, runtime.GetSkill("tick_secs"));
        Assert.Equal(1, runtime.GetSkill("SECS"));
    }

    [Fact]
    public void Tick_EachSec_AccumulatesUnevenFrames()
    {
        WdlRuntime runtime = Start(
            "SKILL n { VAL 0; } ACTION boot { SET EACH_SEC.1, sec; } ACTION sec { RULE n += 1; } IF_START boot;");

        Ticks(runtime, 595, 1.0 / 60);
        Assert.Equal(9, runtime.GetSkill("n"));
        Ticks(runtime, 10, 1.0 / 60);
        Assert.Equal(10, runtime.GetSkill("n"));
    }

    [Fact]
    public void Tick_EachSec_StallFrameCountsAtMostOneSecond()
    {
        WdlRuntime runtime = Start(
            "SKILL n { VAL 0; } ACTION boot { SET EACH_SEC.1, sec; } ACTION sec { RULE n += 1; } IF_START boot;");

        runtime.Tick(3.0);
        Assert.Equal(1, runtime.GetSkill("n"));
        Ticks(runtime, 15);
        Assert.Equal(1, runtime.GetSkill("n"));
        Ticks(runtime, 1);
        Assert.Equal(2, runtime.GetSkill("n"));
    }

    [Fact]
    public void Tick_EachSec_NullClearsSlot()
    {
        WdlRuntime runtime = Start(
            "SKILL n { VAL 0; } ACTION boot { SET EACH_SEC.1, sec; } " +
            "ACTION sec { RULE n += 1; SET EACH_SEC.1, NULL; } IF_START boot;");

        Ticks(runtime, 64);

        Assert.Equal(1, runtime.GetSkill("n"));
        Assert.Null(runtime.GetEachSecAction(1));
        Assert.Equal(0, runtime.ActiveEachSecActionCount);
    }

    [Fact]
    public void Tick_EachSec_RunsSlotsInOrderBeforeTheGlobalEachTickSlots()
    {
        // OV-20261015-each-sec-cadence: in the frame where both ran, the EACH_SEC line came first.
        WdlRuntime runtime = Start(
            "SKILL order { VAL 0; } EACH_TICK tick; EACH_SEC first, second; " +
            "ACTION tick { IF (TICKS >= 16) { RULE order = order * 10 + 3; SET EACH_TICK.1, NULL; } } " +
            "ACTION first { RULE order = order * 10 + 1; SET EACH_SEC.1, NULL; } " +
            "ACTION second { RULE order = order * 10 + 2; SET EACH_SEC.2, NULL; }");

        Ticks(runtime, 16);

        Assert.Equal(123, runtime.GetSkill("order"));
    }

    [Fact]
    public void Runtime_SetEachSec_OutOfRangeSlotIsIgnored()
    {
        WdlRuntime runtime = Start(
            "ACTION boot { SET EACH_SEC.17, sec; SET EACH_SEC.0, sec; } ACTION sec { } IF_START boot;");

        Assert.Equal(0, runtime.ActiveEachSecActionCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.GetEachSecAction(17));
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.GetEachSecAction(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.SetEachSecAction(17, "sec"));
    }

    [Fact]
    public void Runtime_GetSchedulerSlots_ReadBackAssignedActions()
    {
        WdlRuntime runtime = Start(
            "ACTION boot { SET EACH_SEC.9, trapped; SET EACH_SEC.10, frustrated; SET EACH_TICK.14, talk; } " +
            "ACTION trapped { } ACTION frustrated { } ACTION talk { } IF_START boot;");

        Assert.Equal("trapped", runtime.GetEachSecAction(9));
        Assert.Equal("frustrated", runtime.GetEachSecAction(10));
        Assert.Null(runtime.GetEachSecAction(1));
        Assert.Equal("talk", runtime.GetEachTickAction(14));
        Assert.Null(runtime.GetEachTickAction(1));
        Assert.Equal(2, runtime.ActiveEachSecActionCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.GetEachTickAction(17));
    }

    [Fact]
    public void TickFlic_KeepsEachSecRunningLikeTheGlobalEachTickSlots()
    {
        // The EACH_SEC slots run once per second during a FLIC (OV-20261015-flic-play-blocking).
        WdlRuntime runtime = Start(
            "SKILL n { VAL 0; } ACTION boot { SET EACH_SEC.1, sec; } ACTION sec { RULE n += 1; } IF_START boot;");

        for (int i = 0; i < 16; i++)
        {
            runtime.TickFlic(OneTick);
        }

        Assert.Equal(1, runtime.GetSkill("n"));
        Assert.Equal(1, runtime.GetSkill("SECS"));
    }

    [Fact]
    public void RuntimeSnapshot_RestoresEachSecSlotsAndTheirPhase()
    {
        WdlRuntime runtime = Start(
            "SKILL n { VAL 0; } EACH_SEC sec; ACTION sec { RULE n += 1; } ACTION other { }");
        Ticks(runtime, 8);
        WdlRuntimeSnapshot snapshot = JsonSerializer.Deserialize<WdlRuntimeSnapshot>(
            JsonSerializer.Serialize(runtime.CaptureSnapshot()))!;

        Ticks(runtime, 8);
        Assert.Equal(1, runtime.GetSkill("n"));
        runtime.SetEachSecAction(1, "other");

        runtime.RestoreSnapshot(snapshot);

        Assert.Equal("sec", runtime.GetEachSecAction(1));
        Assert.Equal(0, runtime.GetSkill("n"));
        Ticks(runtime, 7);
        Assert.Equal(0, runtime.GetSkill("n"));
        Ticks(runtime, 1);
        Assert.Equal(1, runtime.GetSkill("n"));
    }

    [Fact]
    public void RuntimeSnapshot_WithoutEachSecData_ClearsTheSlots()
    {
        WdlRuntime runtime = Start("EACH_SEC sec; ACTION sec { }");
        WdlRuntimeSnapshot snapshot = runtime.CaptureSnapshot() with { EachSecActions = null, SecondFraction = null };

        runtime.RestoreSnapshot(snapshot);

        Assert.Equal(0, runtime.ActiveEachSecActionCount);
    }

    [Fact]
    public void Tick_InitialEachSecActionsFromTheLevelDeclarationRun()
    {
        WdlRuntime runtime = Start(
            "SKILL a_runs { VAL 0; } SKILL b_runs { VAL 0; } EACH_SEC a, NULL, b; " +
            "ACTION a { RULE a_runs += 1; } ACTION b { RULE b_runs += 1; }");

        Assert.Equal("a", runtime.GetEachSecAction(1));
        Assert.Null(runtime.GetEachSecAction(2));
        Assert.Equal("b", runtime.GetEachSecAction(3));
        Ticks(runtime, 16);

        Assert.Equal(1, runtime.GetSkill("a_runs"));
        Assert.Equal(1, runtime.GetSkill("b_runs"));
    }

    [Fact]
    public void Tick_EachSecCountdownThatBranches_RunsItsTargetOnTheTwelfthSecondOnly()
    {
        // The shape the retail title and levels use for their timeouts: a countdown skill, a slot
        // action that ENDs until it reaches zero, then clears its slot and BRANCHes (a tail jump,
        // OV-20261015-branch-tail-jump, so nothing after the BRANCH runs).
        WdlRuntime runtime = Start(
            "SKILL count { VAL 0; } SKILL reached { VAL 0; } SKILL fell_through { VAL 0; } " +
            "ACTION boot { SET count, 12; SET EACH_SEC.1, countdown; } " +
            "ACTION countdown { ADD count, -1; IF (count > 0) { END; } SET EACH_SEC.1, NULL; BRANCH advance; SET fell_through, 1; } " +
            "ACTION advance { RULE reached += 1; } " +
            "IF_START boot;");

        Ticks(runtime, 191);
        Assert.Equal(1, runtime.GetSkill("count"));
        Assert.Equal(0, runtime.GetSkill("reached"));

        Ticks(runtime, 1);
        Assert.Equal(0, runtime.GetSkill("count"));
        Assert.Equal(1, runtime.GetSkill("reached"));
        Assert.Null(runtime.GetEachSecAction(1));

        Ticks(runtime, 64);
        Assert.Equal(1, runtime.GetSkill("reached"));
        Assert.Equal(0, runtime.GetSkill("fell_through"));
    }

    [Fact]
    public void Tick_EachSecActionWithWaitt_SuspendsAndFinishesLater()
    {
        WdlRuntime runtime = Start(
            "SKILL started { VAL 0; } SKILL finished { VAL 0; } " +
            "ACTION boot { SET EACH_SEC.1, sec; } " +
            "ACTION sec { SET EACH_SEC.1, NULL; RULE started += 1; WAITT 8; RULE finished += 1; } " +
            "IF_START boot;");

        Ticks(runtime, 16);
        Assert.Equal(1, runtime.GetSkill("started"));
        Assert.Equal(0, runtime.GetSkill("finished"));

        Ticks(runtime, 15);
        Assert.Equal(1, runtime.GetSkill("started"));
        Assert.Equal(1, runtime.GetSkill("finished"));
    }

    [Fact]
    public void Tick_EachSecArmedMidSecond_RunsOnTheSharedSecondBoundary()
    {
        // OV-20261015-each-sec-cadence: every slot keeps the runtime's second, so a slot armed at
        // tick 8 first runs at tick 16, when SECS goes up, not one second after the SET (tick 24).
        WdlRuntime runtime = Start(
            "SKILL n { VAL 0; } EACH_TICK arm; " +
            "ACTION arm { IF (TICKS >= 8) { SET EACH_SEC.1, sec; SET EACH_TICK.1, NULL; } } " +
            "ACTION sec { RULE n += 1; }");

        Ticks(runtime, 8);
        Assert.Equal("sec", runtime.GetEachSecAction(1));
        Ticks(runtime, 7);
        Assert.Equal(0, runtime.GetSkill("n"));
        Ticks(runtime, 1);
        Assert.Equal(1, runtime.GetSkill("n"));
        Ticks(runtime, 16);
        Assert.Equal(2, runtime.GetSkill("n"));
    }

    [Fact]
    public void RetailWorlds_WithAnEachSecFadeIn_ShowTheirControlPanelAfterIt()
    {
        // Guarded on the user's own archives under _research/ (a no-op otherwise). HEART, LEGALISM
        // and NEWAGE start with a three-second EACH_SEC.1 countdown whose last run CALLs init_game
        // and shows the control panel; without EACH_SEC they never left the fade-in.
        string[] worlds = ["HEART.WRS", "LEGALISM.WRS", "NEWAGE.WRS"];
        IReadOnlyList<string> archives = ResearchData.WrsFiles()
            .Where(path => worlds.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            .ToArray();
        foreach (string path in archives)
        {
            Level level = LevelLoader.Load(WrsArchive.ReadFile(path), ResearchData.MainWdlName(path));
            var runtime = new WdlRuntime(level);
            Assert.True(runtime.RunStartup(), path);

            Ticks(runtime, 16);
            Assert.True(runtime.TryGetUiNumber("ControlPanel", "VISIBLE", out double early), path);
            Assert.Equal(0, early);

            Ticks(runtime, 16 * 5);
            Assert.Null(runtime.GetEachSecAction(1));
            Assert.True(runtime.TryGetUiNumber("ControlPanel", "VISIBLE", out double visible), path);
            Assert.Equal(1, visible);
        }
    }

    private static WdlRuntime Start(string declarations)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = MinimalMap };
        Level level = LevelLoader.LoadCore(
            "x",
            "MAPFILE <m.wmp>; " + declarations,
            n => resources.GetValueOrDefault(Path.GetFileName(n)));
        var runtime = new WdlRuntime(level);
        runtime.RunStartup();
        // The start-up cycle: TIME_CORR stays 1 and TICKS and the second stay at 0
        // (OV-20261029-scheduler-order), so the ticks below count from 0.
        runtime.Tick(OneTick);
        return runtime;
    }

    private static void Ticks(WdlRuntime runtime, int count, double deltaSeconds = OneTick)
    {
        for (int i = 0; i < count; i++)
        {
            runtime.Tick(deltaSeconds);
        }
    }
}
