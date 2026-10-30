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

    /// <summary>
    /// The actions waiting at the save, which resume after a load in place of the ones waiting then
    /// (OV-20261029-save-body round 7); <c>null</c> in snapshots written before they were kept, which
    /// leave the waiting actions alone.
    /// </summary>
    public List<WdlWaitingActionSnapshot>? WaitingActions { get; init; }
}

/// <summary>
/// A waiting action: the action its outermost block belongs to, its open blocks from the outermost in,
/// how it waits and how long it still has, and the objects it runs for (as places in the level's list
/// of regions, walls, things and actors; −1 for none).
/// </summary>
public sealed record WdlWaitingActionSnapshot(
    string Action,
    List<WdlFrameSnapshot> Frames,
    bool FrameCycles,
    double Remaining,
    int My,
    int There,
    int Target);

/// <summary>
/// One open block of a waiting action: the item indices leading to it from the action's own block
/// (each into the previous block, ending at the item whose body it is), whether it is a <c>WHILE</c>
/// body, and the index of the next item to run.
/// </summary>
public sealed record WdlFrameSnapshot(List<int> Path, bool While, int Index);

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
