// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Interpreter;

namespace OpenVirtue.Engine.Tests.Interpreter;

public sealed class WdlInputForcesTests
{
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(8, 1, 0.5)]
    [InlineData(-8, 1, -0.5)]
    [InlineData(8, 2, 1)]
    [InlineData(32, 1, 1)]
    [InlineData(-32, 1, -1)]
    [InlineData(8, -1, 0)]
    public void MouseAxis_DotsAndSensitivity_ReturnBoundedAnalogForce(
        double dots,
        double sensitivity,
        double expected)
    {
        Assert.Equal(expected, WdlInputForces.MouseAxis(dots, sensitivity));
    }

    [Theory]
    [InlineData(0.7, 0.5, 1)]
    [InlineData(-0.7, -0.5, -1)]
    [InlineData(0.25, -0.5, -0.25)]
    public void Combine_KeyboardAndMouse_ClampsAnalogRange(
        double keyboard,
        double mouse,
        double expected)
    {
        Assert.Equal(expected, WdlInputForces.Combine(keyboard, mouse));
    }

    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(1, 0.5)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    public void MouseAxis_MouseMode2OrHigher_DecouplesPointerFromSteering(
        double mouseMode,
        double expected)
    {
        Assert.Equal(expected, WdlInputForces.MouseAxis(8, 1, mouseMode));
    }
}
