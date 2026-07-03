// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine;

/// <summary>A top-level WDL <c>SYNONYM</c> declaration.</summary>
public readonly record struct WdlSynonym(string Type, string? DefaultKeyword);
