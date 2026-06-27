// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine;

/// <summary>
/// The inclusive value bounds an Acknex <c>SKILL</c> may declare via its <c>MIN</c>/<c>MAX</c>
/// properties. The runtime clamps assignments into this range, reproducing the engine's
/// bounded-skill behaviour (e.g. <c>myHealth</c> is pinned to <c>[0, 100]</c>). An unspecified
/// bound is ±infinity — no limit on that side.
/// </summary>
public readonly record struct SkillRange(double Min, double Max)
{
    /// <summary>Clamps <paramref name="value"/> into <c>[<see cref="Min"/>, <see cref="Max"/>]</c>.</summary>
    public double Clamp(double value) => value < Min ? Min : value > Max ? Max : value;
}
