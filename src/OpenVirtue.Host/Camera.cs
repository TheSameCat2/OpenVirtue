// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Host;

/// <summary>
/// The player's eye: position and yaw in render space
/// (<see cref="RenderSpace"/>: map y → −z, yaw = PLAYER_ANGLE + π/2; ADR-0009). The view is
/// yaw-only. <c>PLAYER_TILT</c> is a y-shear inside <see cref="ProjectionModel"/>, so
/// <see cref="Pitch"/> only mirrors that skill for the input bridge.
/// </summary>
public sealed class Camera
{
    /// <summary>Eye position in render space.</summary>
    public Vector3 Position { get; set; }

    /// <summary>Radians around render +Y. Forward is (sin yaw, 0, cos yaw).</summary>
    public float Yaw { get; set; }

    /// <summary>atan(PLAYER_TILT). Kept for the runtime round trip; it does not rotate the view.</summary>
    public float Pitch { get; set; }

    /// <summary>Horizontal line of sight.</summary>
    public Vector3 LevelForward => RenderSpace.Forward(Yaw);

    /// <summary>Screen-right in render space: cross(forward, up), the look-at view's x-axis.</summary>
    public Vector3 Right => RenderSpace.ScreenRight(LevelForward);

    /// <summary>Yaw-only view matrix along <see cref="LevelForward"/>.</summary>
    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Position + LevelForward, Vector3.UnitY);
}
