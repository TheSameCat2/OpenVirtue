// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Rendering;

/// <summary>
/// A renderer-ready vertex: a render-space position (x, y up, z), texture coordinate, and local
/// ambient light. WMP map coordinates (x, y) map to render (x, −z) and region heights to render y
/// (<see cref="RenderSpace"/>, ADR-0009), so that facing map +x puts map +y on the screen left
/// as the original does.
/// </summary>
public readonly record struct RenderVertex(float X, float Y, float Z, float U, float V, float Ambient = 0);
