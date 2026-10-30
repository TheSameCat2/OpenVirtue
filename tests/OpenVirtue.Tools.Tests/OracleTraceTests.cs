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
        // 26/32 over three cycles: 8, 9 and 9 thirty-seconds, so no cycle lands off the original's grid.
        IReadOnlyList<OracleTraceLine> lines = OracleTrace.Parse("TK 24.000 1.000\nTK 24.813 4.000\n");

        IReadOnlyList<double> ticks = OracleTrace.CycleTicks(lines, new OracleTimingColumn("TK", 0));

        Assert.Equal([24.0, 24.25, 24.53125, 24.8125], ticks);
    }

    [Fact]
    public void CycleTicks_NoCounter_KeepsTheDistinctSamples()
    {
        IReadOnlyList<OracleTraceLine> lines = OracleTrace.Parse("C 1.5 x\nC 1.5 y\nC 2.25 z\n");

        Assert.Equal([1.5, 2.25], OracleTrace.CycleTicks(lines, new OracleTimingColumn("C", 0)));
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
}
