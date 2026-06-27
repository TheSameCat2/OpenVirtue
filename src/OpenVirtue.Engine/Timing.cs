// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine;

/// <summary>
/// Acknex-3 fixed-tick timing. The engine's logic is frame-coupled around a baseline of
/// <see cref="TicksPerSecond"/> ticks per second: at that rate the per-frame time-correction
/// factor (<c>TIME_CORR</c>) is exactly 1, and scripts scale movement/animation by it so
/// behaviour tracks frame time. Reproducing this is parity-critical — see
/// <see href="../../docs/recon/01-engine-and-game.md">the engine notes</see>.
/// </summary>
public static class Timing
{
    /// <summary>The fixed-tick baseline: 16 ticks per second (<c>TIME_CORR</c> == 1 at 16 fps).</summary>
    public const double TicksPerSecond = 16.0;

    /// <summary>The global skill scripts read for the per-frame time-correction factor.</summary>
    public const string TimeCorrectionSkill = "TIME_CORR";

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
        deltaSeconds > 0 ? Math.Min(deltaSeconds, MaxFrameSeconds) * TicksPerSecond : 0;
}
