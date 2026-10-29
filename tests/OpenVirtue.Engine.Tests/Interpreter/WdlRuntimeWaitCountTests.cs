// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Interpreter;

namespace OpenVirtue.Engine.Tests.Interpreter;

/// <summary>
/// How <c>WAIT</c> and <c>WAITT</c> count, in world frames and during a flic, as measured in
/// OV-20261015-flic-scheduler-detail: a wait counts the frame it was armed in (or, armed between
/// frames, the next one) and resumes at the start of a later frame. <c>WAIT n</c> resumes n frames
/// on; <c>WAITT n</c> resumes once more than n − 1 ticks are counted. During a flic each scheduler
/// cycle is one flic frame with <c>TIME_CORR</c> = delay / 64 ms.
/// </summary>
public class WdlRuntimeWaitCountTests
{
    private const string MinimalMap =
        "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\nPLAYER_START 0 0 0 0;#1";

    /// <summary>A 60 fps-like frame: TIME_CORR 0.25.</summary>
    private const double QuarterTick = 1.0 / 64;

    [Fact]
    public void Tick_WaitTAtQuarterTicks_ResumesOnceMoreThanNMinusOneTicksAreCounted()
    {
        // The probe's WAITT 32 resumed after 31.19 ticks at 60 fps; here WAITT 2 resumes in the
        // frame after 1.25 ticks are counted (frames 1-5), not after 2.
        WdlRuntime runtime = Runtime("ACTION a { WAITT 2; SET done, 1; }");
        Assert.True(runtime.RunAction("a"));

        Ticks(runtime, 5, QuarterTick);
        Assert.Equal(0, runtime.GetSkill("done"));
        Ticks(runtime, 1, QuarterTick);

        Assert.Equal(1, runtime.GetSkill("done"));
    }

    [Fact]
    public void Tick_WaitT1_ResumesInTheNextFrame()
    {
        // The probe's WAITT 1 resumed one 0.25-tick frame after it was armed.
        WdlRuntime runtime = Runtime("ACTION a { WAIT 1; WAITT 1; SET done, 1; }");
        Assert.True(runtime.RunAction("a"));
        Ticks(runtime, 2, QuarterTick);
        Assert.Equal(0, runtime.GetSkill("done"));
        Assert.Equal(1, runtime.WaitingActionCount);

        Ticks(runtime, 1, QuarterTick);

        Assert.Equal(1, runtime.GetSkill("done"));
    }

    [Fact]
    public void Tick_WaitArmedInAFrame_ResumesNFramesLater()
    {
        // The chain shape of the probe: a resumed action arms the next wait in the same frame.
        WdlRuntime runtime = Runtime(
            "SKILL f { VAL 0; } SKILL at { VAL 0; } EACH_TICK count; ACTION count { RULE f += 1; } " +
            "ACTION a { WAIT 1; SET at, f; WAIT 3; RULE done = f - at; }");
        Assert.True(runtime.RunAction("a"));

        Ticks(runtime, 6, QuarterTick);

        Assert.Equal(3, runtime.GetSkill("done"));
    }

    [Fact]
    public void TickFlic_WaitTInFlicFrames_ResumesAfterTheMeasuredNumberOfFrames()
    {
        // During a 70 ms flic TIME_CORR is 1.09375: the probe's WAITT 32 resumed 29 flic frames
        // after the frame it was armed in, WAITT 64 after 58 and WAITT 16 after 14.
        foreach ((int ticks, int frames) in new[] { (32, 29), (64, 58), (16, 14) })
        {
            WdlRuntime runtime = Runtime($"ACTION a {{ WAITT {ticks}; SET done, 1; }}");
            Assert.True(runtime.RunAction("a"));

            FlicFrames(runtime, frames, 70);
            Assert.Equal(0, runtime.GetSkill("done"));
            FlicFrames(runtime, 1, 70);

            Assert.Equal(1, runtime.GetSkill("done"));
        }
    }

    [Fact]
    public void TickFlic_Wait_CountsFlicFrames()
    {
        WdlRuntime runtime = Runtime("ACTION a { WAIT 16; SET done, 1; }");
        Assert.True(runtime.RunAction("a"));

        FlicFrames(runtime, 16, 70);
        Assert.Equal(0, runtime.GetSkill("done"));
        FlicFrames(runtime, 1, 70);

        Assert.Equal(1, runtime.GetSkill("done"));
    }

    [Theory]
    [InlineData(70, 1.09375)]
    [InlineData(40, 0.625)]
    public void TickFlic_FlicCycleSeconds_SetsTimeCorrToTheDelayIn64MsUnits(int delayMs, double timeCorr)
    {
        WdlRuntime runtime = Runtime("");
        double ticksBefore = runtime.GetSkill("TICKS");

        runtime.TickFlic(Timing.FlicCycleSeconds(delayMs));

        Assert.Equal(timeCorr, runtime.GetSkill("TIME_CORR"), 9);
        Assert.Equal(ticksBefore + timeCorr, runtime.GetSkill("TICKS"), 9);
    }

    [Fact]
    public void TickFlic_EachSec_RunsEveryFifteenth70MsFrame()
    {
        // 1 s is 14.6 frames of 70/64 ticks, so the probe's SEC lines came 15 flic frames apart.
        WdlRuntime runtime = Runtime("SKILL n { VAL 0; } EACH_SEC sec; ACTION sec { RULE n += 1; }");
        runtime.SetEachSecAction(1, "sec");

        FlicFrames(runtime, 14, 70);
        Assert.Equal(0, runtime.GetSkill("n"));
        FlicFrames(runtime, 1, 70);
        Assert.Equal(1, runtime.GetSkill("n"));
        FlicFrames(runtime, 14, 70);
        Assert.Equal(1, runtime.GetSkill("n"));
        FlicFrames(runtime, 1, 70);

        Assert.Equal(2, runtime.GetSkill("n"));
    }

    [Fact]
    public void BeginFlicFile_MissingFile_KeepsFlicFrameAndTheActionGoesOn()
    {
        WdlRuntime runtime = Runtime(
            "SKILL seen { VAL -1; } SKILL seen2 { VAL -1; } " +
            "ACTION alone { PLAY_FLICFILE <NONE.FLC>; SET seen, FLIC_FRAME; SET done, 1; } " +
            "ACTION during { PLAY_FLICFILE <NONE.FLC>; SET seen2, FLIC_FRAME; }");
        runtime.FlicFileExists = static name => !name.Contains("NONE", StringComparison.OrdinalIgnoreCase);

        Assert.True(runtime.RunAction("alone"));
        Assert.Equal(0, runtime.GetSkill("seen"));
        Assert.Equal(1, runtime.GetSkill("done"));

        runtime.SetSkill(WdlRuntime.FlicFrameSkill, 44);
        Assert.True(runtime.RunAction("during"));
        Assert.Equal(44, runtime.GetSkill("seen2"));
        Assert.Equal(2, runtime.FlicRequests.Count);
    }

    private static WdlRuntime Runtime(string declarations)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = MinimalMap };
        Level level = LevelLoader.LoadCore(
            "x",
            "MAPFILE <m.wmp>; SKILL done { VAL 0; } " + declarations,
            n => resources.GetValueOrDefault(Path.GetFileName(n)));
        var runtime = new WdlRuntime(level);
        // The start-up cycle (TIME_CORR 1, TICKS stays 0, OV-20261029-scheduler-order), so the
        // frames below are steady ones.
        runtime.Tick(QuarterTick);
        return runtime;
    }

    private static void Ticks(WdlRuntime runtime, int count, double deltaSeconds)
    {
        for (int i = 0; i < count; i++)
        {
            runtime.Tick(deltaSeconds);
        }
    }

    private static void FlicFrames(WdlRuntime runtime, int count, int delayMs)
    {
        for (int i = 0; i < count; i++)
        {
            runtime.TickFlic(Timing.FlicCycleSeconds(delayMs));
        }
    }
}
