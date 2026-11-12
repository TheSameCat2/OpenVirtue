// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Interpreter;

namespace OpenVirtue.Engine.Tests.Interpreter;

public sealed class WdlInputForcesTests
{
    private static readonly int[] WalkRamp = [256, 512, 768, 1024, 1024];
    private static readonly int[] HalfSenseWalkRamp = [256, 512, 512];
    private static readonly int[] TurnRamp = [204, 408, 612, 816, 1020, 1024];
    private static readonly int[] ShiftWalkRamp = [512, 1536, 2048];

    [Theory]
    [InlineData(1, 1, 16)]
    [InlineData(4, 1, 64)]
    [InlineData(64, 1, 1024)]
    [InlineData(128, 1, 2048)] // unbounded
    [InlineData(-8, 1, -128)]
    [InlineData(8, 2, 256)]
    public void Mouse_Mickeys_GiveSixteenPer1024EachTimesMouseSense(double mickeys, double sensitivity, double expectedRaw)
    {
        // OV-20261112-input-forces: FORCE_ROT read −16/1024 per mickey at MOUSE_SENSE 1, −32 at 2; 128 mickeys gave −2.
        Assert.Equal(expectedRaw, WdlInputForces.Mouse(mickeys, sensitivity, mouseMode: 0) * 1024, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Mouse_MouseModeBelowTwo_Steers(double mouseMode) =>
        Assert.Equal(0.125, WdlInputForces.Mouse(8, 1, mouseMode), 9);

    [Fact]
    public void Mouse_MouseMode2_GivesNothing() =>
        Assert.Equal(0, WdlInputForces.Mouse(8, 1, 2));

    [Fact]
    public void KeyRamp_HeldWalkKey_RampsAQuarterACycleToKeySense()
    {
        Assert.Equal(WalkRamp, Ramp(5, WdlInputForces.WalkStep, keySense: 1, shift: 1));
        Assert.Equal(HalfSenseWalkRamp, Ramp(3, WdlInputForces.WalkStep, keySense: 0.5, shift: 1));
    }

    [Fact]
    public void KeyRamp_HeldTurnKey_RampsByTwoHundredFour()
    {
        Assert.Equal(TurnRamp, Ramp(6, WdlInputForces.TurnStep, keySense: 1, shift: 1));
    }

    [Fact]
    public void KeyRamp_ShiftHeld_MultipliesTheRunningForceEachCycle()
    {
        // Shift held before cursor up: 512, 1536, 2048; with SHIFT_SENSE 3 and KEY_SENSE 0.5 a turn went 1224 then 1536.
        Assert.Equal(ShiftWalkRamp, Ramp(3, WdlInputForces.WalkStep, keySense: 1, shift: 2));
        double turn = WdlInputForces.KeyRamp(0, 1, WdlInputForces.TurnStep, 0.5, 1);
        turn = WdlInputForces.KeyRamp(turn, 1, WdlInputForces.TurnStep, 0.5, 3);
        Assert.Equal(1224, turn * 1024, 6);
        Assert.Equal(1536, WdlInputForces.KeyRamp(turn, 1, WdlInputForces.TurnStep, 0.5, 3) * 1024, 6);
    }

    [Fact]
    public void KeyRamp_NegativeDirection_RampsDownAndAReversalStartsFromRest()
    {
        double force = WdlInputForces.KeyRamp(0, -1, WdlInputForces.TurnStep, 1, 1);
        Assert.Equal(-204, force * 1024, 6);
        Assert.Equal(204, WdlInputForces.KeyRamp(force, 1, WdlInputForces.TurnStep, 1, 1) * 1024, 6);
        Assert.Equal(0, WdlInputForces.KeyRamp(force, 0, WdlInputForces.TurnStep, 1, 1));
    }

    private static int[] Ramp(int cycles, double step, double keySense, double shift)
    {
        var values = new int[cycles];
        double force = 0;
        for (int i = 0; i < cycles; i++)
        {
            force = WdlInputForces.KeyRamp(force, 1, step, keySense, shift);
            values[i] = (int)Math.Round(force * 1024);
        }

        return values;
    }
}
