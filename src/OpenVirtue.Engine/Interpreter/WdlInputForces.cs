// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Interpreter;

/// <summary>Converts host mouse motion into the documented bounded analog movement-force range.</summary>
public static class WdlInputForces
{
    /// <summary>First-pass virtual mouse dots required for full analog force at middle sensitivity.</summary>
    public const double MouseDotsPerFullForce = 16;

    /// <summary>Maps one frame's relative mouse dots and <c>MOUSE_SENSE</c> to approximately -1..1.</summary>
    public static double MouseAxis(double dots, double sensitivity) =>
        Math.Clamp(dots * Math.Max(0, sensitivity) / MouseDotsPerFullForce, -1, 1);

    /// <summary>Returns no steering force when <c>MOUSE_MODE=2</c> decouples the pointer from movement.</summary>
    public static double MouseAxis(double dots, double sensitivity, double mouseMode) =>
        mouseMode < 2 ? MouseAxis(dots, sensitivity) : 0;

    /// <summary>Adds keyboard and mouse input while retaining the documented analog bounds.</summary>
    public static double Combine(double keyboard, double mouse) =>
        Math.Clamp(keyboard + mouse, -1, 1);
}
