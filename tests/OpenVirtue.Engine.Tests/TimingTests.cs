// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine;

namespace OpenVirtue.Engine.Tests;

public class TimingTests
{
    [Theory]
    [InlineData(1.0, 16.0)]          // one second is 16 ticks
    [InlineData(1.0 / 16, 1.0)]      // a 16 fps frame => TIME_CORR 1
    [InlineData(1.0 / 60, 16.0 / 60)] // a 60 fps frame
    [InlineData(0, 0)]
    [InlineData(-1, 0)]              // non-positive delta guarded to 0
    public void TimeCorrection_ScalesByTicksPerSecond(double deltaSeconds, double expected)
    {
        Assert.Equal(expected, Timing.TimeCorrection(deltaSeconds), 9);
    }

    [Fact]
    public void TicksPerSecond_IsSixteen()
    {
        Assert.Equal(16.0, Timing.TicksPerSecond);
    }

    [Fact]
    public void TimeCorrection_CapsPathologicalFrames()
    {
        // A multi-second gap (level load, lag spike, debugger pause) is capped at MaxFrameSeconds
        // so the simulation can't take a giant step.
        double expected = Timing.MaxFrameSeconds * Timing.TicksPerSecond;
        Assert.Equal(expected, Timing.TimeCorrection(10.0), 9);
        Assert.Equal(expected, Timing.TimeCorrection(Timing.MaxFrameSeconds), 9);
    }
}
