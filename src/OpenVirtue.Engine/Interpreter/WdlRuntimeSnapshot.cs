// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Map;

namespace OpenVirtue.Engine.Interpreter;

/// <summary>
/// First-pass OpenVirtue runtime save state. This is a harness snapshot, not the legacy Acknex save format.
/// </summary>
public sealed record WdlRuntimeSnapshot(
    Dictionary<string, double> Skills,
    Dictionary<string, string> Strings,
    Dictionary<string, string?> KeywordReferences,
    List<string?> EachTickActions,
    Dictionary<string, string?> ActionHooks,
    List<Vertex> Vertices,
    List<WdlObjectSnapshot> LevelObjects,
    WdlPlayerSnapshot? Player);

/// <summary>A saved runtime object state for a region, wall, thing, or actor.</summary>
public sealed record WdlObjectSnapshot(
    Dictionary<string, double> Skills,
    Dictionary<string, double> Numbers,
    Dictionary<string, string?> Strings);

/// <summary>A saved runtime player pose and dynamic skill state.</summary>
public sealed record WdlPlayerSnapshot(
    Dictionary<string, double> Skills,
    double X,
    double Y,
    double Z,
    int Region);
