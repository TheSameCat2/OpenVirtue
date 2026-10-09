// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;

namespace OpenVirtue.App;

/// <summary>
/// The player's eye: position + yaw for the view matrix. The view is yaw-only; PLAYER_TILT is
/// applied by <see cref="OpenVirtue.Engine.Rendering.ProjectionModel"/> as a y-shear
/// (OV-20261009-tilt-model-shear-vs-pitch), so <see cref="Pitch"/> only mirrors that skill
/// for the input bridge and never rotates the view.
/// </summary>
internal sealed class Camera
{
    public Vector3 Position;
    public float Yaw;   // radians, around world up
    public float Pitch; // atan(PLAYER_TILT), kept for the runtime round trip only

    /// <summary>Horizontal line of sight.</summary>
    public Vector3 LevelForward => new(MathF.Sin(Yaw), 0, MathF.Cos(Yaw));

    /// <summary>
    /// Screen-right in world space: the x-axis of <see cref="View"/> (a right-handed look-at,
    /// so cross(forward, up), not cross(up, forward)). Sprite billboards, the click test and the
    /// audio pan all use this so they agree with where the renderer draws things.
    /// </summary>
    public Vector3 Right => Vector3.Normalize(Vector3.Cross(LevelForward, Vector3.UnitY));

    /// <summary>View matrix along <see cref="LevelForward"/>; no camera pitch.</summary>
    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Position + LevelForward, Vector3.UnitY);
}
