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
    WdlPlayerSnapshot? Player)
{
    /// <summary>The global <c>EACH_SEC</c> slots; <c>null</c> in snapshots written before they existed.</summary>
    public List<string?>? EachSecActions { get; init; }

    /// <summary>
    /// The elapsed part of the runtime's current second (when <c>SECS</c> next goes up and the
    /// <c>EACH_SEC</c> slots run), so a restored countdown keeps its phase; <c>null</c> in snapshots
    /// written before it existed.
    /// </summary>
    public double? SecondFraction { get; init; }
}

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
