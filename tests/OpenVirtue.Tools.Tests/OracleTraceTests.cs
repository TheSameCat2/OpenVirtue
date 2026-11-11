// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Tools.Tests;

/// <summary>The oracle-diff harness's trace parsing, timing detection, comparison and replay clock.</summary>
public sealed class OracleTraceTests
{
    [Fact]
    public void Parse_TokensAndFields_SkipsBlankLines()
    {
        IReadOnlyList<OracleTraceLine> lines = OracleTrace.Parse("BEGIN\r\n\nC 16.250 1.000\n");

        Assert.Equal(2, lines.Count);
        Assert.Equal("BEGIN", lines[0].Token);
        Assert.Empty(lines[0].Fields);
        Assert.Equal("C", lines[1].Token);
        Assert.Equal(16.25, lines[1].Value(0));
        Assert.Equal(2, lines[1].Number);
    }

    [Fact]
    public void DetectTimingColumn_TicksBeforeCounter_PicksTheFirstColumn()
    {
        IReadOnlyList<OracleTraceLine> lines = OracleTrace.Parse(
            "BEGIN\nC 24.000 1.000 -7.000\nC 32.000 2.000 -6.000\nC 40.000 3.000 -5.000\nC 48.000 4.000 -4.000\n");

        Assert.Equal(new OracleTimingColumn("C", 0), OracleTrace.DetectTimingColumn(lines));
    }

    [Fact]
    public void DetectTimingColumn_SparseEventToken_GivesWayToTheSamplerFromCycleOne()
    {
        // border-climb climb-1: N prints only while the player pushes (cycles 120 to 136 here), the TK
        // sampler every 62 cycles from cycle 1; N has more distinct TICKS but fills under a quarter.
        var trace = new System.Text.StringBuilder("BEGIN\n");
        for (int n = 1; n <= 311; n += 62)
        {
            trace.Append(System.Globalization.CultureInfo.InvariantCulture, $"TK {13.406 + ((n - 1) * 0.262):0.000} {n}.000 -8.000\n");
            if (n == 125)
            {
                for (int m = 120; m <= 136; m++)
                {
                    trace.Append(System.Globalization.CultureInfo.InvariantCulture, $"N {44.594 + ((m - 120) * 0.262):0.000} {m}.000 1.000\n");
                }
            }
        }

        Assert.Equal(new OracleTimingColumn("TK", 0), OracleTrace.DetectTimingColumn(OracleTrace.Parse(trace.ToString())));
    }

    [Fact]
    public void DetectTimingColumn_SamplerEveryOtherCycle_IsKept()
    {
        // eye-clamp vz-rate-c50000: V every other cycle (cycles 8 to 40) beside a TK every 8 cycles.
        var trace = new System.Text.StringBuilder("BEGIN\n");
        for (int n = 1; n <= 41; n++)
        {
            double ticks = 14.906 + ((n - 1) * 0.5);
            if (n % 8 == 1)
            {
                trace.Append(System.Globalization.CultureInfo.InvariantCulture, $"TK {ticks:0.000} {n}.000 0.000\n");
            }

            if (n >= 8 && n % 2 == 0)
            {
                trace.Append(System.Globalization.CultureInfo.InvariantCulture, $"V {ticks + 0.25:0.000} {n}.000 1.063\n");
            }
        }

        Assert.Equal(new OracleTimingColumn("V", 0), OracleTrace.DetectTimingColumn(OracleTrace.Parse(trace.ToString())));
    }

    [Fact]
    public void DetectTimingColumn_NegativeColumnFirst_SkipsIt()
    {
        IReadOnlyList<OracleTraceLine> lines = OracleTrace.Parse("W -7.000 24.000\nW -6.000 32.000\nW -5.000 40.000\n");

        Assert.Equal(new OracleTimingColumn("W", 1), OracleTrace.DetectTimingColumn(lines));
    }

    [Fact]
    public void DetectTimingColumn_NoRepeatedToken_UsesEveryLineWhenOneCounterRuns()
    {
        IReadOnlyList<OracleTraceLine> lines = OracleTrace.Parse(
            "BEGIN\nGO 13.438 0.000\nW1 13.688 1.000\nW15 13.969 2.000\nW05 13.969 2.000\nW25 14.469 4.000\nRET\n");

        OracleTimingColumn? timing = OracleTrace.DetectTimingColumn(lines);

        Assert.Equal(new OracleTimingColumn(OracleTimingColumn.AnyToken, 0), timing);
        Assert.Equal([13.4375, 13.6875, 13.96875, 14.21875, 14.46875], OracleTrace.CycleTicks(lines, timing!.Value));
    }

    [Fact]
    public void CycleTicks_ProbeLinesCountACycleBehindTheSampler()
    {
        // OV-20261029-audio: the probe action prints GO and P before the EACH_TICK sampler raises the
        // count, so its lines read a cycle behind C's at the same TICKS.
        IReadOnlyList<OracleTraceLine> lines = OracleTrace.Parse(
            "BEGIN\nGO 13.438 0\nP 13.438 0\nC 13.438 1\nC 14.219 4\nRET 14.719 5\n");

        OracleTimingColumn? timing = OracleTrace.DetectTimingColumn(lines);

        Assert.Equal("C", OracleTrace.LeadingSampler(lines, 0, 1));
        // The two C lines are the sampler (a two-line sampler before every line as a sample); RET's
        // count 5 lands in cycle 6 by the sampled cadence, as the every-line rule's "a cycle behind".
        Assert.Equal(new OracleTimingColumn("C", 0), timing);
        Assert.Equal([13.4375, 13.6875, 13.96875, 14.21875, 14.46875, 14.71875], OracleTrace.CycleTicks(lines, timing!.Value));
        Assert.Equal([13.4375, 13.6875, 13.96875, 14.21875, 14.46875, 14.71875], OracleTrace.CycleTicks(lines, new OracleTimingColumn(OracleTimingColumn.AnyToken, 0)));
    }

    [Fact]
    public void DetectTimingColumn_OneCountWithTwoTicks_IsNotATimingColumn()
    {
        // A line printed before the counting action and one after it carry counters a cycle apart.
        IReadOnlyList<OracleTraceLine> lines = OracleTrace.Parse("A 1.000 1.000\nB 1.250 1.000\nC 1.500 2.000\nD 1.750 3.000\n");

        Assert.Null(OracleTrace.DetectTimingColumn(lines));
    }

    [Fact]
    public void CycleTicks_SparseSamplerWithCounter_FillsEveryCycle()
    {
        IReadOnlyList<OracleTraceLine> lines = OracleTrace.Parse("TK 24.000 1.000\nTK 48.000 4.000\nTK 56.000 5.000\n");

        IReadOnlyList<double> ticks = OracleTrace.CycleTicks(lines, new OracleTimingColumn("TK", 0));

        Assert.Equal([24.0, 32.0, 40.0, 48.0, 56.0], ticks);
    }

    [Fact]
    public void CycleTicks_GapOfWholeThirtySeconds_FillsWithWholeThirtySecondSteps()
    {
        // 26/32 over three cycles: 9, 8 and 9 thirty-seconds (the centre of the cadences that fit), so
        // no cycle lands off the original's grid.
        IReadOnlyList<OracleTraceLine> lines = OracleTrace.Parse("TK 24.000 1.000\nTK 24.813 4.000\n");

        IReadOnlyList<double> ticks = OracleTrace.CycleTicks(lines, new OracleTimingColumn("TK", 0));

        Assert.Equal([24.0, 24.28125, 24.53125, 24.8125], ticks);
    }

    [Fact]
    public void CycleTicks_SparseCountedSampler_FillsAlongTheCadenceOfEverySampleAndItsTimeCorr()
    {
        // flic-scheduler-detail waits-control: TK every 123 cycles with TIME_CORR. One cadence through
        // all of them puts the other tokens' lines where the original printed them: WB16 in cycle 17 at
        // 17.594, S2 in 510 at 146.813, S4 in 584 at 166.219, S6 in 649 at 183.25.
        IReadOnlyList<double> ticks = OracleTrace.CycleTicks(OracleTrace.Parse(WaitsControlSamples), new OracleTimingColumn("TK", 1));

        Assert.Equal(3322, ticks.Count);
        Assert.Equal(17.59375, ticks[16]);
        Assert.Equal(146.8125, ticks[509]);
        Assert.Equal(166.21875, ticks[583]);
        Assert.Equal(183.25, ticks[648]);
        Assert.Equal(0.25, ticks[123] - ticks[122]); // the step TK 124 prints
        Assert.All(ticks.Skip(1).Zip(ticks), p => Assert.True(OracleTrace.IsFrameStep(p.First - p.Second)));
    }

    [Fact]
    public void CycleTicks_GapAcrossAFlicEnd_KeepsTheFlicStepForItsRemainingFrames()
    {
        // flic-scheduler-detail round3 speed: TK 157 prints the flic's 0.625; the flic had 44 frames
        // left (200 − 156), the most that leave the 18 world cycles after it a 60 Hz frame each.
        IReadOnlyList<OracleTraceLine> lines = OracleTrace.Parse(
            "TK 1.000 13.375 1.000 0.250\nTK 52.000 45.875 53.000 0.625\nTK 104.000 78.375 105.000 0.625\n" +
            "TK 156.000 110.875 157.000 0.625\nTK 0.000 142.894 219.000 0.281\n" +
            "TK 0.000 175.112 342.000 0.250\nTK 0.000 207.362 465.000 0.250\n");

        IReadOnlyList<double> ticks = OracleTrace.CycleTicks(lines, new OracleTimingColumn("TK", 1));

        // ticks[i] is cycle i + 1.
        Assert.Equal(110.875 + (44 * 0.625), ticks[200], 9);
        Assert.True(OracleTrace.IsFrameStep(ticks[203] - ticks[202]));
        Assert.Equal(0.28125, ticks[218] - ticks[217], 9); // the step TK 219 prints
    }

    [Fact]
    public void FirstCycleStep_CountedSamplerOfOneTimeCorr_ReadsItWhereTwoSamplesAreACycleApart()
    {
        // scheduler-order round5 waitt-small: every line prints TICKS, the count and TIME_CORR 0.250.
        IReadOnlyList<OracleTraceLine> lines = OracleTrace.Parse(
            "BEGIN\nA0 13.938 2.000 0.250\nR0 13.938 2.000 0.250\nA1 13.938 2.000 0.250\nR1 14.188 3.000 0.250\n" +
            "A13 14.188 3.000 0.250\nR13 14.719 5.000 0.250\n");

        Assert.Equal(0.25, OracleTrace.FirstCycleStep(lines, new OracleTimingColumn(OracleTimingColumn.AnyToken, 0)));
    }

    [Fact]
    public void FitCadence_EightsAndNines_ContinuesTheirPattern()
    {
        // view-activity round8 beam-back's 119 C steps; the original's next frame was 8/32 (QS 44.875
        // after C 44.625), where the run's median step is 8 but a fit to the last 32 steps says 9.
        int[] steps =
        [
            8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 8, 9, 8, 9, 8, 8,
            9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8,
            8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8,
        ];
        List<double> ticks = [13.4375];
        foreach (int step in steps)
        {
            ticks.Add(ticks[^1] + (step / 32.0));
        }

        OracleCadence? cadence = OracleTrace.FitCadence(ticks);

        Assert.NotNull(cadence);
        Assert.Equal(0.25, cadence.StepInto(1));
        Assert.Equal(0.28125, OracleTrace.FitCadence(ticks, maxSteps: 32)!.StepInto(1)); // too short a fit
    }

    [Theory]
    [InlineData(429, 10)] // startup clock-1: TICKS 13.406 in cycle 52, 10/32 into cycle 2
    [InlineData(436, 17)] // push-d005: 13.625 in cycle 52; the cadence's cycle before cycle 1 falls on 0 exactly
    public void StartupFrames_SixtyHertzSamples_RunTheirCadenceBackToCycleOne(int firstUnits, int intoCycleTwo)
    {
        // OV-20261103-startup-clock: the 48 steps after cycle 52 of each launch.
        int[] steps =
        [
            9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8,
            8, 9, 8, 9, 8, 8, 9, 8,
        ];
        List<double> ticks = [firstUnits / 32.0];
        foreach (int step in steps)
        {
            ticks.Add(ticks[^1] + (step / 32.0));
        }

        IReadOnlyList<double>? frames = OracleTrace.StartupFrames(ticks);

        Assert.NotNull(frames);
        Assert.Equal(52, frames.Count); // cycles 1 to 52
        Assert.Equal(intoCycleTwo / 32.0, frames[1]);
        Assert.Equal(firstUnits / 32.0, frames.Skip(1).Sum(), 9);
        Assert.All(frames.Skip(2), static f => Assert.True(f is 0.25 or 0.28125));
    }

    [Fact]
    public void StartupAnchors_DateTheStartFilesCyclesByTheCountingColumn()
    {
        // clip-contact round 2 liber-far-walk: the counting EACH_TICK's second run in cycle 2 at
        // 0.156, then every 8th, 56 cycles before the trace's first U line; the sampler's own count
        // (the second column) is 0 before it is armed, so it cannot be the counter.
        IReadOnlyList<OracleTraceLine> start = OracleTrace.Parse(
            "START\nU 0.156 0.000 2.000 65535.999 0.000 0.000\nU 2.250 0.000 10.000 65535.999 0.000 0.000\n" +
            "U 4.344 0.000 18.000 65535.999 0.000 0.000\nU 12.719 0.000 50.000 65535.999 0.000 0.000\n");
        IReadOnlyList<OracleTraceLine> trace = OracleTrace.Parse(
            "BEGIN\nTK 13.531 1.000 0.000 0.000\nU 14.813 6.000 58.000 65535.999 0.000 0.000\nU 16.938 14.000 66.000 65535.999 0.000 0.000\n");

        IReadOnlyList<OracleTrace.StartupAnchor> anchors = OracleTrace.StartupAnchors(start, trace, new OracleTimingColumn("U", 0));

        // The TK line before the first U line is dated by the sampler's count (1 against the U line's 6).
        Assert.Equal(5, anchors.Count);
        Assert.Equal(new OracleTrace.StartupAnchor(5, 13.531, 1, Dated: false), anchors[0]);
        Assert.Equal(new OracleTrace.StartupAnchor(8, 12.719, 50), anchors[1]);
        Assert.Equal(new OracleTrace.StartupAnchor(56, 0.156, 2), anchors[^1]);
        Assert.Empty(OracleTrace.StartupAnchors(start, trace, new OracleTimingColumn("TK", 0)));
    }

    [Fact]
    public void StartupFramesFromAnchors_PassThroughEveryAnchor()
    {
        // liber-far-walk: cycle 58 at 14.8125 with 8/9-unit frames after it; cycle 2 at 5/32 and
        // cycle 50 at 407/32 where the cadence run back alone would give 13/32 and 408/32.
        int[] steps = [9, 8, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9];
        List<double> ticks = [474 / 32.0];
        foreach (int step in steps)
        {
            ticks.Add(ticks[^1] + (step / 32.0));
        }

        OracleTrace.StartupAnchor[] anchors =
        [
            new(56, 5 / 32.0, 2), new(48, 72 / 32.0, 10), new(40, 139 / 32.0, 18), new(32, 206 / 32.0, 26),
            new(24, 273 / 32.0, 34), new(16, 340 / 32.0, 42), new(8, 407 / 32.0, 50), new(5, 433 / 32.0, 1, Dated: false),
        ];

        IReadOnlyList<double>? frames = OracleTrace.StartupFramesFromAnchors(ticks, anchors);

        Assert.NotNull(frames);
        Assert.Equal(58, frames.Count); // cycles 1 to 58
        Assert.Equal(5 / 32.0, frames[1]);
        Assert.Equal(407 / 32.0, frames.Skip(1).Take(49).Sum(), 9); // cycle 50
        Assert.Equal(433 / 32.0, frames.Skip(1).Take(52).Sum(), 9); // cycle 53, the TK line
        Assert.Equal(474 / 32.0, frames.Skip(1).Sum(), 9); // cycle 58
        Assert.All(frames.Skip(2), static f => Assert.True(f is 0.25 or 0.28125 or 0.3125));
        // Without a count-2 anchor the cycles before the earliest keep its pace instead (the undated plan).
        Assert.NotNull(OracleTrace.StartupFramesFromAnchors(ticks, [new OracleTrace.StartupAnchor(8, 407 / 32.0, 50)]));
        Assert.NotNull(OracleTrace.StartupFramesFromAnchors(ticks, [new OracleTrace.StartupAnchor(5, 433 / 32.0, 1, Dated: false)]));
    }

    [Fact]
    public void StartupFramesFromAnchors_UndatedAnchorsKeepTheirPaceBeforeThem()
    {
        // eye-clamp round 3 vz-rate-c12000: V samples 4.6875 ticks apart from 25.75, the resumed
        // action's GO at 17.344 two cycles before; nothing dates cycle 2. The cycles before GO keep
        // GO's pace to the first sample (269 units over 2 cycles), so the one before it lies past 13.
        List<double> ticks = [.. Enumerable.Range(0, 12).Select(static i => 25.75 + (i * 4.6875))];
        OracleTrace.StartupAnchor[] anchors = [new(2, 17.344, 1, Dated: false)];

        IReadOnlyList<double>? frames = OracleTrace.StartupFramesFromAnchors(ticks, anchors);

        Assert.NotNull(frames);
        Assert.Equal(8, frames.Count); // cycles 1 to 8, the first sample cycle 8 and GO cycle 6
        Assert.Equal(555 / 32.0, frames.Skip(1).Take(5).Sum(), 9); // cycle 6 at 17.344
        Assert.True(frames.Skip(1).Take(4).Sum() > 13); // cycle 5 past 13: the WAITT 16 ends at GO
        Assert.Equal(25.75, frames.Skip(1).Sum(), 9);
    }

    [Fact]
    public void StartupFrames_SlowSamplesOrFirstCycleSampled_PlanNothing()
    {
        Assert.Null(OracleTrace.StartupFrames([24, 32, 40, 48, 56, 64, 72, 80, 88, 96]));
        Assert.Null(OracleTrace.StartupFrames([.. Enumerable.Range(0, 20).Select(static i => i * 0.25)]));
    }

    [Fact]
    public void ReplayClock_BeforeTheFirstSample_RunsThePlannedStartUp()
    {
        List<double> ticks = [13.40625];
        for (int i = 0; i < 24; i++)
        {
            ticks.Add(ticks[^1] + ((i % 3 == 1 ? 9 : 8) / 32.0));
        }

        IReadOnlyList<double> planned = OracleTrace.StartupFrames(ticks)!;
        var clock = new OracleReplay.ReplayClock(ticks, fallbackTicks: 0.25);
        double now = 0;
        for (int frame = 0; frame < planned.Count; frame++)
        {
            double step = clock.Next(now) * 16;
            Assert.Equal(planned[frame], step, 9);
            now += frame == 0 ? 0 : step; // cycle 1 reads TICKS 0 whatever it lasted
        }

        Assert.Equal(13.40625, now, 9);
    }

    [Fact]
    public void FitCadence_StepsOfThreeSizes_HasNoCadence()
    {
        List<double> ticks = [.. Enumerable.Range(0, 20).Select(static i => 10 + (i * 0.5) + (i % 2 * 0.0625))];

        Assert.Null(OracleTrace.FitCadence(ticks));
        Assert.Null(OracleTrace.FitCadence([1, 1.25, 1.5])); // too few steps
    }

    [Theory]
    [InlineData("ARM 24.000 1.000\nS 32.000 2.000\nS 48.000 3.000\nRET\n", null, true)] // each-sec cadence
    [InlineData("ARM 24.000 1.000\nS 32.000 2.000\n", null, true)] // slot-16
    [InlineData("ARM 24.000 1.000\nS 32.000 2.000\nS 36.188 3.000\n", null, false)] // sped up
    [InlineData("ARM 24.000 1.000\nS 24.000 2.000\n", null, false)] // one value only
    [InlineData("ARM 24.000 1.000\n", 13.438, false)] // the 60 Hz start-up stamp
    public void RanAtSlowFrames_EveryTicksAMultipleOfEight(string trace, double? stamp, bool slow)
    {
        Assert.Equal(slow, OracleTrace.RanAtSlowFrames(OracleTrace.Parse(trace), stamp));
    }

    [Fact]
    public void LastTicks_RisingFirstColumns_TakesTheLargest()
    {
        string file = Path.GetTempFileName();
        try
        {
            // wall-pass-clip clip-wall: the W sampler stops at 292.875, Y11S prints at 356.531; the X
            // column of P goes down, so it is not TICKS.
            File.WriteAllText(file, "BEGIN\nW 290.000 1\nW 292.875 2\nY11S 356.531 0\nP 900 1\nP 3 2\n");

            Assert.Equal(356.531, OracleReplay.LastTicks([file]));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ReplayClock_AfterTheLastSample_ContinuesTheCadence()
    {
        // view-activity round6 sprite-occlude's 35 C steps end 9 8 8 (… 22.0625, 22.3125, 22.5625): its
        // next frame, H3S at 22.844, is 9/32 later, where the run's median step gave 8/32.
        int[] steps = [8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 8];
        List<double> ticks = [13.40625];
        foreach (int step in steps)
        {
            ticks.Add(ticks[^1] + (step / 32.0));
        }

        var clock = new OracleReplay.ReplayClock(ticks, fallbackTicks: 0.25);
        clock.Anchor(ticks[0]);
        double now = ticks[0];
        for (int i = 1; i < ticks.Count; i++)
        {
            now += clock.Next(now) * 16;
        }

        Assert.Equal(ticks[^1], now, 9);
        Assert.Equal(0.28125 / 16, clock.Next(now), 9);
    }

    [Theory]
    [InlineData(200, 8.125 - 1.3125)] // a sampler that printed every cycle: the gap is one long frame
    [InlineData(20, 0.25)] // too few samples to tell: a skipped stretch, run at the typical step
    public void ReplayClock_RareLongGapInAnEveryCycleSampler_IsOneLongFrame(int samples, double expectedStep)
    {
        // OV-20261110-retail-play heart-idle: C lines a quarter tick apart, but 1.3125 → 8.125 across a disk load.
        List<double> ticks = [];
        for (int i = 0; i < samples; i++)
        {
            ticks.Add(i < 6 ? i * 0.25 + 0.0625 : 8.125 + ((i - 6) * 0.25));
        }

        var clock = new OracleReplay.ReplayClock(ticks, fallbackTicks: 0.25);
        clock.Anchor(ticks[0]);
        double now = ticks[0];
        for (int i = 1; i < 6; i++)
        {
            now += clock.Next(now) * 16;
        }

        Assert.Equal(1.3125, now, 9);
        Assert.Equal(expectedStep / 16, clock.Next(now), 9);
    }

    [Fact]
    public void ReplayClock_AfterTheLastSample_WithoutACadence_TakesTheRecentMedian()
    {
        // Steps of 2, then 1.5 and 1.625 and 1.875: no cadence of two neighbouring sizes, and the run's
        // median (2) is not what the last steps ran.
        var clock = new OracleReplay.ReplayClock([10, 12, 14, 16, 18, 20, 22, 24, 25.5, 27.125, 29, 30.5, 32.125], fallbackTicks: 0.25);
        clock.Anchor(10);

        Assert.Equal(1.625 / 16, clock.Next(32.125), 9);
    }

    [Fact]
    public void PeriodicFirstStep_SlowStartUp_IsNotRead()
    {
        // view-activity near-low: 24, 32 at 8-tick frames, then 60 Hz frames repeating every 18 cycles.
        int[] pattern = [8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8];
        List<double> ticks = [24, 32];
        for (int i = 0; i < 40; i++)
        {
            ticks.Add(ticks[^1] + (pattern[i % 18] / 32.0));
        }

        Assert.Null(OracleTrace.PeriodicFirstStep(ticks));
    }

    private const string WaitsControlSamples =
        "TK 0.000 13.375 1.000 0.250\nTK 0.000 45.625 124.000 0.250\nTK 0.000 77.875 247.000 0.281\nTK 0.000 110.125 370.000 0.281\n" +
        "TK 0.000 142.344 493.000 0.250\nTK 0.000 174.594 616.000 0.250\nTK 0.000 206.844 739.000 0.281\nTK 0.000 239.063 862.000 0.250\n" +
        "TK 0.000 271.313 985.000 0.250\nTK 0.000 303.563 1108.000 0.250\nTK 0.000 335.813 1231.000 0.281\nTK 0.000 368.031 1354.000 0.250\n" +
        "TK 0.000 400.281 1477.000 0.250\nTK 0.000 432.531 1600.000 0.250\nTK 0.000 464.781 1723.000 0.281\nTK 0.000 497.000 1846.000 0.250\n" +
        "TK 0.000 529.250 1969.000 0.250\nTK 0.000 561.500 2092.000 0.281\nTK 0.000 593.750 2215.000 0.281\nTK 0.000 625.969 2338.000 0.250\n" +
        "TK 0.000 658.219 2461.000 0.250\nTK 0.000 690.469 2584.000 0.281\nTK 0.000 722.719 2707.000 0.281\nTK 0.000 754.938 2830.000 0.250\n" +
        "TK 0.000 787.188 2953.000 0.250\nTK 0.000 819.438 3076.000 0.281\nTK 0.000 851.688 3199.000 0.281\nTK 0.000 883.906 3322.000 0.250\n";

    [Fact]
    public void CycleTicks_NoCounter_KeepsTheDistinctSamples()
    {
        IReadOnlyList<OracleTraceLine> lines = OracleTrace.Parse("C 1.5 x\nC 1.5 y\nC 2.25 z\n");

        Assert.Equal([1.5, 2.25], OracleTrace.CycleTicks(lines, new OracleTimingColumn("C", 0)));
    }

    [Fact]
    public void FirstCycleStep_SparseSampler_ReadsTheColumnOfWholeThirtySecondTicks()
    {
        // flic-scheduler-detail replace: TK prints FLIC_FRAME, TICKS, a cycle count and TIME_CORR every
        // 32 ticks, so no two samples are one cycle apart; TIME_CORR is the column of whole 1/32 ticks.
        IReadOnlyList<OracleTraceLine> lines = OracleTrace.Parse(
            "TK 1.000 13.344 1.000 0.250\nTK 30.000 46.156 31.000 1.094\nTK 16.000 78.969 61.000 1.094\n" +
            "TK 0.000 209.425 230.000 0.281\nTK 0.000 241.644 353.000 0.250\n");

        Assert.Equal(0.25, OracleTrace.FirstCycleStep(lines, new OracleTimingColumn("TK", 1)));
    }

    [Fact]
    public void Compare_NumbersWithinTolerance_Match()
    {
        IReadOnlyList<OracleTraceLine> original = OracleTrace.Parse("BEGIN\nC 1.000 2.000\nEND\n");
        IReadOnlyList<OracleTraceLine> replay = OracleTrace.Parse("BEGIN\nC 1.001 2.000\nEND\n");

        OracleTraceComparison comparison = OracleTrace.Compare(original, replay);

        Assert.True(comparison.Identical);
        Assert.Equal(3, comparison.MatchingLines);
    }

    [Fact]
    public void Compare_ExtraRepeatedLine_ReportsTheFirstDivergenceAndMatchingRuns()
    {
        IReadOnlyList<OracleTraceLine> original = OracleTrace.Parse("BEGIN\nC 1\nC 2\nN 3\n");
        IReadOnlyList<OracleTraceLine> replay = OracleTrace.Parse("BEGIN\nC 1\nC 2\nC 3\nN 4\n");

        OracleTraceComparison comparison = OracleTrace.Compare(original, replay);

        Assert.Equal(3, comparison.FirstDivergence);
        Assert.False(comparison.TokensMatch);
        Assert.True(comparison.TokenRunsMatch);
    }

    [Fact]
    public void Compare_TickColumnsShifted_ComparesRelativeToTheAnchors()
    {
        IReadOnlyList<OracleTraceLine> original = OracleTrace.Parse("C 13.375 1\nC 13.625 2\n");
        IReadOnlyList<OracleTraceLine> replay = OracleTrace.Parse("C 15.500 1\nC 15.750 2\n");
        var shift = new OracleTickShift(
            OracleTrace.TickColumns(original, [13.375, 13.625]),
            OriginalAnchor: 13.375,
            ReplayAnchor: 15.5);

        Assert.False(OracleTrace.Compare(original, replay).Identical);
        Assert.True(OracleTrace.Compare(original, replay, shift: shift).Identical);
    }

    [Fact]
    public void TickColumns_OnlyColumnsWhoseValuesAreSamples()
    {
        IReadOnlyList<OracleTraceLine> lines = OracleTrace.Parse("C 1.5 7\nTK 2.25 1.5\n");

        HashSet<(string, int)> columns = OracleTrace.TickColumns(lines, [1.5, 2.25]);

        Assert.Equal([("C", 0), ("TK", 0), ("TK", 1)], columns.Order());
    }

    [Fact]
    public void ReplayClock_BeforeAnchor_RunsTheFirstSampledRate()
    {
        var clock = new OracleReplay.ReplayClock([24, 32, 40, 42, 44, 46], fallbackTicks: 0.25);

        Assert.Equal(8.0 / 16, clock.Next(0), 9);
        Assert.Equal(8.0 / 16, clock.Next(23), 9);
    }

    [Fact]
    public void ReplayClock_AfterAnchor_LandsOnShiftedSamples()
    {
        var clock = new OracleReplay.ReplayClock([13.375, 13.625, 13.906, 14.156], fallbackTicks: 0.25);

        clock.Anchor(15.5);

        Assert.Equal(2.125, clock.Offset, 9);
        Assert.Equal(0.25 / 16, clock.Next(15.5), 9);
        Assert.Equal(0.281 / 16, clock.Next(15.75), 9);
    }

    [Theory]
    [InlineData(13.438, 0.25)] // below-beam: the beam frame 8/32 before the first cycle at 13.688
    [InlineData(13.406, 0.28125)]
    [InlineData(13.188, null)] // two frames before: the first cycles' rate decides
    [InlineData(null, null)]
    public void StampStep_OnlyWhenTheStampIsOneFrameBeforeTheFirstSample(double? stamp, double? step)
    {
        Assert.Equal(step, OracleReplay.StampStep(stamp, [13.688, 13.969, 14.219]));
    }

    [Theory]
    [InlineData(false, 0.5)]
    [InlineData(true, 8.0)]
    public void ReplayClock_LongGap_IsOneFrameOnlyWhenACounterPlacedEverySample(bool consecutive, double step)
    {
        // view-activity near-low: a per-cycle counter shows the original's frames at 24, 32 (one frame of
        // 8 ticks), then half a tick each; a sampler without a counter may have skipped cycles there.
        var clock = new OracleReplay.ReplayClock([24, 32, 32.5, 33, 33.5, 34, 34.5], fallbackTicks: 0.25, consecutive: consecutive);

        clock.Anchor(24);

        Assert.Equal(step / 16, clock.Next(24), 9);
    }

    [Fact]
    public void TimingColumn_TryParse_TokenAndColumn()
    {
        Assert.True(OracleTimingColumn.TryParse("TK:1", out OracleTimingColumn timing));
        Assert.Equal(new OracleTimingColumn("TK", 1), timing);
        Assert.False(OracleTimingColumn.TryParse("TK", out _));
    }

    [Theory]
    [InlineData("HIN_D=44.000", "HIN_D=44.001", true)]
    [InlineData("HIN_D=44.000", "HIN_D=45.000", false)]
    [InlineData("TCK=13.375", "TCK=13.500", true)] // the start-up stamp, within one cycle
    [InlineData("TCK=13.375", "TCK=14.000", false)]
    [InlineData("HOUT_T=13.375", "HOUT_T=13.500", true)] // TICKS at the shot, in an untimed carrier
    [InlineData("HIN_D=44.000", "HIN_X=44.000", false)]
    public void LinesMatch_KeyedValues_CompareTheirNumbers(string original, string replay, bool match)
    {
        OracleTraceLine a = Assert.Single(OracleTrace.Parse(original));
        OracleTraceLine b = Assert.Single(OracleTrace.Parse(replay));

        Assert.Equal(match, OracleTrace.LinesMatch(a, b, OracleTrace.DefaultTolerance));
    }

    [Theory]
    [InlineData(13.688, 13.6875)]
    [InlineData(13.406, 13.40625)]
    [InlineData(13.438, 13.4375)]
    [InlineData(0.001, 0.0009765625)]
    public void SnapTicks_PrefersWholeThirtySecondsWithinThePrintedRounding(double printed, double raw) =>
        Assert.Equal(raw, OracleTrace.SnapTicks(printed));

    [Fact]
    public void PeriodicFirstStep_ReadsTheStepEighteenCyclesOn()
    {
        // DOSBox-X's 60 Hz steps repeat every 18 cycles (scheduler-order `startup`): 8 and 9 thirty-seconds.
        int[] pattern = [8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8, 9, 8, 8, 9, 8];
        var ticks = new List<double> { 13.40625 };
        for (int i = 0; i < 40; i++)
        {
            ticks.Add(ticks[^1] + (pattern[(i + 1) % 18] / 32.0));
        }

        Assert.Equal(pattern[0] / 32.0, OracleTrace.PeriodicFirstStep(ticks));
        Assert.Null(OracleTrace.PeriodicFirstStep(ticks.Take(30).ToList())); // too short to check
    }

    [Fact]
    public void PeriodicFirstStep_FlicPacedSamples_SayNothingAboutTheStartUp()
    {
        // A trace sampled only during a flic steps 1.094 ticks a cycle; its start-up still ran 60 Hz frames.
        List<double> ticks = [.. Enumerable.Range(0, 40).Select(static i => 13.4375 + (i * 1.09375))];

        Assert.Null(OracleTrace.PeriodicFirstStep(ticks));
        Assert.False(OracleTrace.IsFrameStep(1.09375));
        Assert.True(OracleTrace.IsFrameStep(0.28125));
    }

    [Fact]
    public void SecondsSteps_WaitLoopTracer_GivesTheCycleOfTheLastOldSecond()
    {
        // OV-20261110-retail-play round 6: a WAIT 1 tracer prints before SECS rises in its cycle, so the new second
        // started in the cycle of the last line still showing the old one. Other tokens are skipped.
        IReadOnlyList<OracleTraceLine> lines = OracleTrace.Parse(
            "BEGIN\nC 15.219 0.000\nX 15.300 7.000\nC 15.469 1.000\nC 15.750 1.000\nC 31.469 1.000\nC 31.719 3.000\n");

        IReadOnlyList<double> steps = OracleReplay.SecondsSteps(lines, new OracleTimingColumn("C", 1), new OracleTimingColumn("C", 0));

        Assert.Equal([15.219, 31.469, 31.469], steps);
    }
}
