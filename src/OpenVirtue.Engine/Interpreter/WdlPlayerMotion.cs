// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;

namespace OpenVirtue.Engine.Interpreter;

/// <summary>Applies documented per-tick player velocity skills to the collision-aware player.</summary>
public static class WdlPlayerMotion
{
    /// <summary>
    /// Advances position and facing by the <c>PLAYER_V*</c> skills, scaled by
    /// <c>TIME_CORR</c>, then updates collision and derived skills.
    /// </summary>
    public static void Apply(WdlRuntime runtime, Player player, double timeCorrection)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(player);

        player.EyeHeight = (float)Math.Max(0.01, runtime.GetSkill("PLAYER_SIZE"));
        player.MinHeadroom = player.EyeHeight;
        player.Width = (float)Math.Max(1.2, runtime.GetSkill("PLAYER_WIDTH"));
        player.ActorWidthFactor = (float)Math.Max(0, runtime.GetSkill("ACTOR_WIDTH"));
        player.ThingWidthFactor = (float)Math.Max(0, runtime.GetSkill("THING_WIDTH"));
        player.StepUp = (float)Math.Max(0, runtime.GetSkill("PLAYER_CLIMB"));

        runtime.SetSkill("PLAYER_LAST_X", player.Position.X);
        runtime.SetSkill("PLAYER_LAST_Y", player.Position.Z);

        double requestedVelocityX = runtime.GetSkill("PLAYER_VX");
        double requestedVelocityY = runtime.GetSkill("PLAYER_VY");
        Vector2 displacement = timeCorrection > 0
            ? player.MoveHorizontal(
                (float)(requestedVelocityX * timeCorrection),
                (float)(requestedVelocityY * timeCorrection))
            : Vector2.Zero;

        double actualVelocityX = timeCorrection > 0 ? displacement.X / timeCorrection : requestedVelocityX;
        double actualVelocityY = timeCorrection > 0 ? displacement.Y / timeCorrection : requestedVelocityY;
        runtime.SetSkill("PLAYER_VX", actualVelocityX);
        runtime.SetSkill("PLAYER_VY", actualVelocityY);
        runtime.SetSkill("IMPACT_VX", runtime.GetSkill("IMPACT_VX") + actualVelocityX - requestedVelocityX);
        runtime.SetSkill("IMPACT_VY", runtime.GetSkill("IMPACT_VY") + actualVelocityY - requestedVelocityY);

        double requestedVelocityZ = runtime.GetSkill("PLAYER_VZ");
        float verticalDisplacement = timeCorrection > 0
            ? player.MoveVertical((float)(requestedVelocityZ * timeCorrection))
            : 0;
        double actualVelocityZ = timeCorrection > 0 ? verticalDisplacement / timeCorrection : requestedVelocityZ;
        runtime.SetSkill("PLAYER_VZ", actualVelocityZ);
        runtime.SetSkill("IMPACT_VZ", runtime.GetSkill("IMPACT_VZ") + actualVelocityZ - requestedVelocityZ);
        foreach (PlayerVerticalTransition transition in player.VerticalTransitions)
        {
            runtime.DispatchPlayerVerticalTransition(transition);
        }

        double angle = NormalizeAngle(
            runtime.GetSkill("PLAYER_ANGLE") + (runtime.GetSkill("PLAYER_VROT") * Math.Max(0, timeCorrection)));
        runtime.SetSkill("PLAYER_ANGLE", angle);
        runtime.SetSkill("PLAYER_SIN", Math.Sin(angle));
        runtime.SetSkill("PLAYER_COS", Math.Cos(angle));

        double previousSpeed = runtime.GetSkill("PLAYER_SPEED");
        double speed = (actualVelocityX * Math.Cos(angle)) + (actualVelocityY * Math.Sin(angle));
        runtime.SetSkill("PLAYER_SPEED", speed);
        runtime.SetSkill(
            "ACCELERATION",
            timeCorrection > 0 ? (speed - previousSpeed) / timeCorrection : 0);
        Synchronize(runtime, player);
    }

    /// <summary>Updates position, region, and height-query skills from the live player.</summary>
    public static void Synchronize(WdlRuntime runtime, Player player)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(player);

        runtime.SetSkill("PLAYER_X", player.Position.X);
        runtime.SetSkill("PLAYER_Y", player.Position.Z);
        runtime.SetSkill("PLAYER_Z", player.Position.Y);
        runtime.SetSkill("PLAYER_REGION", player.Region);
        runtime.SetSkill("FLOOR_HGT", player.FloorHeight);
        runtime.SetSkill("CEIL_HGT", player.CeilingHeight);
        Vector2 slope = player.FloorSlope;
        double angle = runtime.GetSkill("PLAYER_ANGLE");
        double sin = Math.Sin(angle);
        double cos = Math.Cos(angle);
        runtime.SetSkill("SLOPE_AHEAD", (slope.X * cos) + (slope.Y * sin));
        runtime.SetSkill("SLOPE_SIDE", (-slope.X * sin) + (slope.Y * cos));
        runtime.SetSkill("PLAYER_HGT", player.Position.Y - runtime.GetSkill("PLAYER_SIZE") - player.FloorHeight);
        runtime.SetSkill("PLAYER_DEPTH", player.WaterDepth);
    }

    private static double NormalizeAngle(double angle)
    {
        angle %= Math.Tau;
        return angle < 0 ? angle + Math.Tau : angle;
    }
}
