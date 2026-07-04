// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine;

/// <summary>
/// Acknex-3 fixed-tick timing. The engine's logic is frame-coupled around a baseline of
/// <see cref="TicksPerSecond"/> ticks per second: at that rate the per-frame time-correction
/// factor (<c>TIME_CORR</c>) and inverse factor (<c>TIME_FAC</c>) are exactly 1, and
/// scripts scale movement/animation by them so behaviour tracks frame time. Reproducing this is parity-critical — see
/// <see href="../../docs/recon/01-engine-and-game.md">the engine notes</see>.
/// </summary>
public static class Timing
{
    /// <summary>The fixed-tick baseline: 16 ticks per second (<c>TIME_CORR</c> == 1 at 16 fps).</summary>
    public const double TicksPerSecond = 16.0;

    /// <summary>The global skill scripts read for the per-frame time-correction factor.</summary>
    public const string TimeCorrectionSkill = "TIME_CORR";

    /// <summary>The global skill scripts read for the inverse of <c>TIME_CORR</c>.</summary>
    public const string TimeFactorSkill = "TIME_FAC";

    /// <summary>The global skill that counts elapsed 1/16-second ticks.</summary>
    public const string TicksSkill = "TICKS";

    /// <summary>The global skill that counts elapsed seconds.</summary>
    public const string SecondsSkill = "SECS";

    /// <summary>
    /// The largest frame delta (seconds) <see cref="TimeCorrection"/> honours. A coarse stability
    /// guard so a pathological frame — the multi-second gap on level load, a debugger pause, or a
    /// lag spike — can't produce a giant step that teleports objects or tunnels collision. It only
    /// triggers below 1 fps, so timing at any playable frame rate is unaffected; the exact threshold
    /// is subject to oracle calibration.
    /// </summary>
    public const double MaxFrameSeconds = 1.0;

    /// <summary>
    /// The time-correction factor for a frame lasting <paramref name="deltaSeconds"/>: the
    /// elapsed time expressed in ticks (<paramref name="deltaSeconds"/> * <see cref="TicksPerSecond"/>),
    /// with the delta capped at <see cref="MaxFrameSeconds"/>. 1.0 at 16 fps, ~0.267 at 60 fps.
    /// A non-positive delta yields 0.
    /// </summary>
    public static double TimeCorrection(double deltaSeconds) =>
        EffectiveFrameSeconds(deltaSeconds) * TicksPerSecond;

    /// <summary>The inverse time-correction factor for a frame, or 0 for a non-positive delta.</summary>
    public static double TimeFactor(double deltaSeconds)
    {
        double correction = TimeCorrection(deltaSeconds);
        return correction > 0 ? 1 / correction : 0;
    }

    /// <summary>The frame duration after applying the same cap used for timing skills.</summary>
    public static double EffectiveFrameSeconds(double deltaSeconds) =>
        deltaSeconds > 0 ? Math.Min(deltaSeconds, MaxFrameSeconds) : 0;
}
