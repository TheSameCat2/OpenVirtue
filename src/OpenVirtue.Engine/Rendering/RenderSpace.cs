// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;

namespace OpenVirtue.Engine.Rendering;

/// <summary>
/// The one place where map coordinates become render coordinates (ADR-0009).
/// <para>
/// Gameplay code keeps the map's right-handed, y-up plan view in a <see cref="Vector3"/> as
/// <c>(map x, height, map y)</c>. Renderers use a right-handed look-at with +Y up, so to show
/// the world the way the original does — facing map +x, the map's +y side is on the screen
/// <em>left</em> (OV-20261009-tilt-model-shear-vs-pitch, "screen handedness") — render depth
/// must be the <em>negated</em> map y: render space is <c>(map x, height, −map y)</c>. With
/// that, the look-at's x-axis (<see cref="ScreenRight"/>, <c>cross(forward, up)</c>) is screen
/// right, which keeps billboards, the sprite click test, the audio pan and
/// <see cref="ProjectionModel.Project"/> agreeing without any mirroring.
/// </para>
/// <para>
/// The camera yaw is measured around render +Y with <see cref="Forward"/> =
/// <c>(sin yaw, 0, cos yaw)</c>; <c>PLAYER_ANGLE</c> is the map heading (0 = +x, counter-
/// clockwise), so <c>yaw = PLAYER_ANGLE + π/2</c>: angle 0 → yaw π/2 → forward (1, 0, 0) =
/// map +x; angle π/2 → yaw π → forward (0, 0, −1) = map +y.
/// </para>
/// </summary>
public static class RenderSpace
{
    /// <summary>Render depth for a map y coordinate.</summary>
    public static float ZFromMapY(float mapY) => -mapY;

    /// <summary>Map y for a render depth.</summary>
    public static float MapYFromZ(float z) => -z;

    /// <summary>Converts a gameplay position <c>(map x, height, map y)</c> to render space.</summary>
    public static Vector3 FromMap(Vector3 mapPosition) =>
        new(mapPosition.X, mapPosition.Y, ZFromMapY(mapPosition.Z));

    /// <summary>Converts map coordinates and a height to a render-space position.</summary>
    public static Vector3 FromMap(float mapX, float height, float mapY) =>
        new(mapX, height, ZFromMapY(mapY));

    /// <summary>Converts a render-space position back to <c>(map x, height, map y)</c>.</summary>
    public static Vector3 ToMap(Vector3 renderPosition) =>
        new(renderPosition.X, renderPosition.Y, MapYFromZ(renderPosition.Z));

    /// <summary>The camera yaw (radians around render +Y) that faces a <c>PLAYER_ANGLE</c>.</summary>
    public static double YawFromPlayerAngle(double playerAngle) => playerAngle + (Math.PI * 0.5);

    /// <summary>The <c>PLAYER_ANGLE</c> (normalized to <c>[0, 2π)</c>) a camera yaw faces.</summary>
    public static double PlayerAngleFromYaw(double yaw)
    {
        double angle = (yaw - (Math.PI * 0.5)) % Math.Tau;
        return angle < 0 ? angle + Math.Tau : angle;
    }

    /// <summary>Horizontal line of sight in render space for a camera yaw.</summary>
    public static Vector3 Forward(double yaw) => new((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));

    /// <summary>Horizontal line of sight in render space for a <c>PLAYER_ANGLE</c>: <c>(cos a, 0, −sin a)</c>.</summary>
    public static Vector3 ForwardFromPlayerAngle(double playerAngle) => Forward(YawFromPlayerAngle(playerAngle));

    /// <summary>
    /// Screen right in render space for a horizontal line of sight: the x-axis of a right-handed
    /// look-at view, <c>cross(forward, up)</c>. (<c>cross(up, forward)</c> is the mirror image.)
    /// </summary>
    public static Vector3 ScreenRight(Vector3 forward) => Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
}
