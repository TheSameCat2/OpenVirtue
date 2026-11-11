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
    /// <summary>
    /// How near a move's float displacement must come to its 1/1024 step to count as the whole step: a
    /// quarter of 1/1024, well above a float's resolution at map coordinates (0.5/1024 at ±512).
    /// </summary>
    private const double WholeStepTolerance = 0.25 / 1024;

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
        // Each axis steps by the engine's own product of the speed and TIME_CORR, rounded to 1/1024:
        // PLAYER_VY 0.2 (204 raw) stepped 57 and 51 units at TIME_CORR 0.281 and 0.25, and 0.05
        // stepped 14 and 13 (OV-20261019-eye-clamp stop-border).
        var start = new Vector2(player.Position.X, player.Position.Z);
        double stepX = WdlRuntime.EngineProduct(requestedVelocityX, timeCorrection);
        double stepY = WdlRuntime.EngineProduct(requestedVelocityY, timeCorrection);

        // OV-20261029-actor-step: like a CAREFULLY actor, a moving player's step part under 4/1024
        // becomes 4/1024 with its sign, and a part of 0 becomes +4/1024 (the border-climb walker went
        // from x −8 to −7.992 in two steps along +y). It is part of the step the walls and objects
        // test: PLAYER_VX 0.01 (about 3/1024 a step) crept 4/1024 a step into an actor, and was
        // pushed back from the nudged position (OV-20261029-overlap still-walk).
        const float Nudge = 4f / 1024;
        double nudgeX = 0;
        double nudgeY = 0;
        // OV-20261110-retail-play New Age walk: a y part under 4/1024 but not 0 is taken as it is (the
        // player coming to rest stepped 3, 2 and 1 along y with its x part of 0 made +4), where Apathy's
        // x parts of −3 to −1 were made −4; and a step that rounds to nothing on both axes moves nothing
        // (PLAYER_VY 1/1024 left the player where it stood).
        if (timeCorrection > 0 && (requestedVelocityX != 0 || requestedVelocityY != 0) && (stepX != 0 || stepY != 0))
        {
            nudgeX = MinimumStep(stepX, Nudge) - stepX;
            nudgeY = (stepY != 0 ? stepY : MinimumStep(stepY, Nudge)) - stepY;
            stepX += nudgeX;
            stepY += nudgeY;
        }

        // An actor that stepped into the player this cycle holds a step toward it, and otherwise
        // pushes the player out from where its step ends (WdlRuntime.ResolveActorPushes).
        bool held = timeCorrection > 0 && runtime.IsHeldByActorPush(player, new Vector2((float)stepX, (float)stepY));
        // OV-20261110-retail-play round 4: the climb test sees the eye this frame's PLAYER_VZ moves it
        // to. Apathy's walk, its eye rising 0.031 a frame, climbed a 2-step border with the feet 2.497
        // below it after that move (2.521 before it) and was held at 2.502; standing still, a step
        // 1/1024 under PLAYER_CLIMB enters (border-climb edge-2).
        player.ClimbEyeRise = player.FeetPlaced && timeCorrection > 0
            ? (float)(runtime.GetSkill("PLAYER_VZ") * VerticalScale(timeCorrection))
            : 0;
        bool wallStopped = false;
        Vector2 displacement = timeCorrection > 0 && !held
            ? player.MoveHorizontal((float)stepX, (float)stepY, out wallStopped)
            : Vector2.Zero;
        if (timeCorrection > 0 && !held)
        {
            // As for a CAREFULLY actor (OV-20261029-actor-events), a part a wall stopped takes the
            // same minimum from what is left of it: a step held at the wall (0) moves 4/1024 into it,
            // and the next stop pulls it back by 4, so the player alternates 1279 and 1275 from a
            // wall ahead (OV-20261019-eye-clamp and OV-20261019-border-climb, 2026-11-03 addenda).
            if (wallStopped)
            {
                player.Position = new Vector3(
                    MinimumPart(player.Position.X, start.X, stepX, Nudge),
                    player.Position.Y,
                    MinimumPart(player.Position.Z, start.Y, stepY, Nudge));
            }
        }

        player.ClimbEyeRise = 0;
        bool pushed = runtime.ResolveActorPushes(player, held, start);

        // Neither the minimum's addition nor an object's push-back counts toward the speed the player moved.
        displacement -= held ? Vector2.Zero : player.LastObjectPush;
        // A step taken whole keeps the script's velocity to the last 1/1024: Apathy's walk set −15/1024
        // and read it back, where dividing the step by TIME_CORR again gave −16 (OV-20261110-retail-play
        // apathy-walk). A wall stop keeps it as well: held at a 2-step border and sliding along a closed
        // region's wall, the walk read the script's PLAYER_VX and PLAYER_VY on, on all 600 lines
        // (round 4). The step a wall cut still counts toward IMPACT_VX and IMPACT_VY, the manual's
        // "change of speed by collision" (chapter 11, the player skills); a step an object cut changes the velocity.
        bool whole = Math.Abs(displacement.X - stepX) < WholeStepTolerance && Math.Abs(displacement.Y - stepY) < WholeStepTolerance;
        bool keep = whole || (wallStopped && !pushed);
        double cutVelocityX = timeCorrection > 0 && !pushed && !held && !whole ? (displacement.X - nudgeX) / timeCorrection : requestedVelocityX;
        double cutVelocityY = timeCorrection > 0 && !pushed && !held && !whole ? (displacement.Y - nudgeY) / timeCorrection : requestedVelocityY;
        double actualVelocityX = keep ? requestedVelocityX : cutVelocityX;
        double actualVelocityY = keep ? requestedVelocityY : cutVelocityY;
        runtime.SetSkill("PLAYER_VX", actualVelocityX);
        runtime.SetSkill("PLAYER_VY", actualVelocityY);
        runtime.SetSkill("IMPACT_VX", runtime.GetSkill("IMPACT_VX") + cutVelocityX - requestedVelocityX);
        runtime.SetSkill("IMPACT_VY", runtime.GetSkill("IMPACT_VY") + cutVelocityY - requestedVelocityY);

        // PLAYER_VZ moves the eye freely (the feet may sink below the floor) and keeps its value;
        // the eye clamp then holds the eye inside the region and adds its correction, in steps,
        // to IMPACT_VZ, also when a script moved PLAYER_Z out of range (OV-20261019-eye-clamp).
        // OV-20261110-retail-play: the first frame stands the player's feet on the floor, the eye
        // PLAYER_SIZE above it, before PLAYER_VZ moves it: Apathy's stage (floor −2, size 5) read
        // PLAYER_Z 0 through IF_START and 3 + 4·(−0.079) = 2.684 after the first frame. A PLAYER_Z the
        // script set before then is kept.
        if (!player.FeetPlaced)
        {
            player.FeetPlaced = true;
            if (runtime.GetSkill("PLAYER_Z") == 0)
            {
                player.Position = new Vector3(player.Position.X, player.FloorHeight + player.EyeHeight, player.Position.Z);
            }
        }

        runtime.PlayerPlaced = true;
        player.MoveVertical(timeCorrection > 0 ? (float)(runtime.GetSkill("PLAYER_VZ") * VerticalScale(timeCorrection)) : 0);

        runtime.SetSkill("IMPACT_VZ", runtime.GetSkill("IMPACT_VZ") + player.ClampEye());
        foreach (PlayerVerticalTransition transition in player.VerticalTransitions)
        {
            runtime.DispatchPlayerVerticalTransition(transition);
        }

        // The turn steps by the engine's rounded product like the move (OV-20261110-retail-play round 4:
        // cut toward zero, Apathy's turn fell 0.006 behind in 32 cycles at the same PLAYER_VROT).
        double heading = runtime.GetSkill("PLAYER_ANGLE");
        double angle = NormalizeAngle(
            heading + WdlRuntime.EngineProduct(runtime.GetSkill("PLAYER_VROT"), Math.Max(0, timeCorrection)));
        runtime.SetSkill("PLAYER_ANGLE", angle);
        if (timeCorrection > 0)
        {
            SetHeading(runtime);
        }

        double previousSpeed = runtime.GetSkill("PLAYER_SPEED");
        // OV-20261110-retail-play round 9: the velocity seen along the heading before this frame's turn, its
        // cosine and sine cut toward zero to 1/1024 and each product rounded as the engine's are: 2057 of the four
        // walks' 2158 moving lines, where the turned heading and exact products fit 1766 at best.
        double speed = WdlRuntime.EngineProduct(actualVelocityX, CutTowardZero(Math.Cos(heading))) +
            WdlRuntime.EngineProduct(actualVelocityY, CutTowardZero(Math.Sin(heading)));
        runtime.SetSkill("PLAYER_SPEED", speed);
        runtime.SetSkill(
            "ACCELERATION",
            timeCorrection > 0 ? (speed - previousSpeed) / timeCorrection : 0);
        player.Settle();
        Synchronize(runtime, player);
    }

    /// <summary>Updates position, region, and height-query skills from the live player.</summary>
    public static void Synchronize(WdlRuntime runtime, Player player)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(player);

        runtime.SetSkill("PLAYER_X", player.Position.X);
        runtime.SetSkill("PLAYER_Y", player.Position.Z);
        if (!player.FeetPlaced)
        {
            // Before the first frame the original has computed no heights (OV-20261110-retail-play):
            // PLAYER_Z, FLOOR_HGT, CEIL_HGT and PLAYER_HGT read 0 through IF_START. A script's own
            // PLAYER_Z stands.
            runtime.SetSkill("PLAYER_REGION", player.Region);
            return;
        }

        runtime.SetSkill("PLAYER_Z", player.Position.Y);
        runtime.SetSkill("PLAYER_REGION", player.Region);
        // While a flic shows, the view's heights are not refreshed: CEIL_HGT stayed 0 from PLAY_FLICFILE
        // through a SET PLAYER_Z and twelve flic cycles (OV-20261015-flic-play-blocking round 2;
        // FLOOR_HGT read 0 either way there and is taken to follow).
        if (runtime.GetSkill(WdlRuntime.FlicFrameSkill) <= 0)
        {
            runtime.SetSkill("FLOOR_HGT", player.FloorHeight);
            runtime.SetSkill("CEIL_HGT", player.CeilingHeight);
        }

        runtime.UpdateSlopeExtents(player);
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

    /// <summary>A step's part raised to <paramref name="minimum"/> with its sign, positive for none.</summary>
    private static double MinimumStep(double part, float minimum)
    {
        if (Math.Abs(part) >= minimum - 1e-9)
        {
            return part;
        }

        return part < 0 ? -minimum : minimum;
    }

    /// <summary>
    /// A wall-stopped step's part along one axis: a part requested at <paramref name="minimum"/> or
    /// more that came out under it becomes <paramref name="minimum"/> with its sign, positive for none.
    /// </summary>
    private static float MinimumPart(float position, float start, double requested, float minimum)
    {
        float part = position - start;
        if (Math.Abs(requested) < minimum - 1e-6 || Math.Abs(part) >= minimum - 1e-6f)
        {
            return position;
        }

        return start + (part < -1e-6f ? -minimum : minimum);
    }

    /// <summary>
    /// Sets <c>PLAYER_SIN</c> and <c>PLAYER_COS</c> from <c>PLAYER_ANGLE</c> as stored, in 1/1024, each
    /// cut toward zero: Apathy's start at 2412/1024 read <c>PLAYER_COS</c> −723/1024 (−723.7), not −724
    /// (OV-20261110-retail-play round 4).
    /// </summary>
    public static void SetHeading(WdlRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        double angle = runtime.GetSkill("PLAYER_ANGLE");
        runtime.SetSkill("PLAYER_SIN", CutTowardZero(Math.Sin(angle)));
        runtime.SetSkill("PLAYER_COS", CutTowardZero(Math.Cos(angle)));
    }

    // A value within 1/10000 of a 1/1024 step below the next is the next: the angle 6434/1024, a
    // float's 2π from the camera, keeps PLAYER_COS 1 as the original's saves hold it at heading 0
    // (OV-20261029-save-body).
    private static double CutTowardZero(double value) =>
        Math.Truncate((value * 1024) + Math.CopySign(1e-4, value)) / 1024;

    private static double NormalizeAngle(double angle)
    {
        angle %= Math.Tau;
        return angle < 0 ? angle + Math.Tau : angle;
    }
}
