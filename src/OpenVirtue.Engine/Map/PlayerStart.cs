// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Map;

/// <summary>The player's spawn position, facing angle, and starting region.</summary>
public readonly record struct PlayerStart(float X, float Y, float Angle, int Region);
