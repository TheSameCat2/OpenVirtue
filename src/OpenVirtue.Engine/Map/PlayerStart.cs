// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Map;

/// <summary>
/// The player's spawn position, facing angle, and starting region. <see cref="Angle"/> is the
/// WMP value in degrees (manual ch.04 p.17), counter-clockwise like <c>PLAYER_ANGLE</c> and the
/// THING/ACTOR placement angle (<see cref="MapEntity.FacingAngleRadians"/>); 0 faces map +x, as
/// every OV-20261009 projection probe used (<c>PLAYER_START 0 0 0 0</c> dumped <c>ANG=0.000</c>
/// with the +x far wall in view). Only a = 0 has been probed.
/// </summary>
public readonly record struct PlayerStart(float X, float Y, float Angle, int Region)
{
    /// <summary>The start heading as a <c>PLAYER_ANGLE</c> value (radians, counter-clockwise, 0 = map +x).</summary>
    public double AngleRadians => Angle * (Math.PI / 180.0);
}
