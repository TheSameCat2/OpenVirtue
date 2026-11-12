// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Interpreter;

/// <summary>
/// The movement forces the original runtime derives from held keys and mouse motion, as measured on patched
/// V3.963 (OV-20261112-input-forces): a held key ramps its force by a fixed step each cycle up to
/// <c>KEY_SENSE</c> (times <c>SHIFT_SENSE</c> with [Shift]), and the mouse gives its mickeys times 16/1024 times
/// <c>MOUSE_SENSE</c>, unbounded, on an axis no key holds.
/// </summary>
public static class WdlInputForces
{
    /// <summary>The force one mickey gives at <c>MOUSE_SENSE</c> 1: 64 mickeys in a cycle make a force of 1.</summary>
    public const double MouseForcePerMickey = 16.0 / 1024;

    /// <summary>The per-cycle ramp of cursor up/down, [,]/[.] and [Home]/[End]: 256/1024.</summary>
    public const double WalkStep = 256.0 / 1024;

    /// <summary>The per-cycle ramp of cursor left/right (turning, or strafing with [Alt]) and [Pg up]/[Pg dn]: 204/1024.</summary>
    public const double TurnStep = 204.0 / 1024;

    /// <summary>
    /// The mouse's force for one cycle's mickeys: <c>mickeys·MOUSE_SENSE·16/1024</c>, unbounded (128 mickeys gave 2), or
    /// none with <c>MOUSE_MODE</c> 2, which frees the pointer from movement (manual p.107).
    /// </summary>
    public static double Mouse(double mickeys, double sensitivity, double mouseMode) =>
        mouseMode < 2 ? mickeys * sensitivity * MouseForcePerMickey : 0;

    /// <summary>
    /// One cycle of a held key's force: <c>(previous + step)·shift</c>, at most <c>KEY_SENSE·shift</c>, where
    /// <paramref name="previous"/> is the force this ramp gave last cycle (0 when it ran the other way), not the skill
    /// as a script left it. Without [Shift] that is 256, 512, 768, 1024 (/1024); with it, held first, 512, 1536, 2048,
    /// and pressed while walking the force doubles at once. <paramref name="direction"/> 0 (no key) gives 0.
    /// </summary>
    public static double KeyRamp(double previous, int direction, double step, double keySense, double shift)
    {
        if (direction == 0)
        {
            return 0;
        }

        double magnitude = Math.Max(0, previous * direction);
        double target = keySense * shift;
        return direction * Math.Min((magnitude + step) * shift, target);
    }
}
