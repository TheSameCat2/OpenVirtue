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
        // The manual's 1.2 minimum (p.109) is not enforced: PLAYER_WIDTH 0.5 held the player
        // 0.55 from a wall (OV-20261019-eye-clamp).
        player.Width = (float)Math.Max(0, runtime.GetSkill("PLAYER_WIDTH"));
        player.ActorWidthFactor = (float)Math.Max(0, runtime.GetSkill("ACTOR_WIDTH"));
        player.ThingWidthFactor = (float)Math.Max(0, runtime.GetSkill("THING_WIDTH"));
        player.StepUp = (float)Math.Max(0, runtime.GetSkill("PLAYER_CLIMB"));

        player.PushOutOfObjects();
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

        // PLAYER_VZ moves the eye freely (the feet may sink below the floor) and keeps its value;
        // the eye clamp then holds the eye inside the region and adds its correction, in steps,
        // to IMPACT_VZ, also when a script moved PLAYER_Z out of range (OV-20261019-eye-clamp).
        player.MoveVertical(timeCorrection > 0 ? (float)(runtime.GetSkill("PLAYER_VZ") * VerticalScale(timeCorrection)) : 0);

        runtime.SetSkill("IMPACT_VZ", runtime.GetSkill("IMPACT_VZ") + player.ClampEye());
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

    /// <summary>
    /// How many ticks of <c>PLAYER_VZ</c> one frame applies: patched V3.963 moved the eye by
    /// <c>PLAYER_VZ × TIME_CORR</c> only while <c>TIME_CORR</c> was above 4, and by
    /// <c>4 × PLAYER_VZ</c> at every <c>TIME_CORR</c> from 0.25 to 2.4 (OV-20261019-eye-clamp). The
    /// horizontal velocities scale by <c>TIME_CORR</c> alone.
    /// </summary>
    private static double VerticalScale(double timeCorrection) => Math.Max(timeCorrection, 4);

    private static double NormalizeAngle(double angle)
    {
        angle %= Math.Tau;
        return angle < 0 ? angle + Math.Tau : angle;
    }
}
