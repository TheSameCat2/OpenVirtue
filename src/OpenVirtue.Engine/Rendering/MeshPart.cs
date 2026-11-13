// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Map;

namespace OpenVirtue.Engine.Rendering;

/// <summary>
/// One part of a level's mesh: the regions a script moves (<c>SHIFT</c>, <c>ROTATE</c>, <c>LIFT</c>) and their
/// neighbours, or everything else. A moving region changes the walls it shares and the floor of the region
/// around it (an island cuts that floor), so those go with it. The host builds the rest once and the moving
/// part on every move: rebuilding all of Apathy for each 0.25 its secret door slid cost a frame a tick.
/// </summary>
/// <param name="moving">The moving regions and their neighbours.</param>
/// <param name="movingPart">True for the moving part, false for the rest.</param>
public sealed class MeshPart(IReadOnlySet<int> moving, bool movingPart)
{
    /// <summary>The regions that make up the moving part.</summary>
    public IReadOnlySet<int> Moving { get; } = moving ?? throw new ArgumentNullException(nameof(moving));

    /// <summary>Whether this is the moving part.</summary>
    public bool MovingPart { get; } = movingPart;

    /// <summary>Whether a wall belongs to this part: the moving one when either of its regions moves.</summary>
    public bool Includes(Wall wall)
    {
        ArgumentNullException.ThrowIfNull(wall);
        return (Moving.Contains(wall.LeftRegion) || Moving.Contains(wall.RightRegion)) == MovingPart;
    }

    /// <summary>Whether a region's floor and ceiling belong to this part.</summary>
    public bool Includes(int region) => Moving.Contains(region) == MovingPart;

    /// <summary>
    /// The regions a move of <paramref name="region"/> changes: itself and every region across one of its
    /// walls, added to <paramref name="into"/>. Returns whether any was new.
    /// </summary>
    public static bool AddMoved(Level level, int region, ISet<int> into)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(into);
        bool grew = into.Add(region);
        foreach (Wall wall in level.Walls)
        {
            if (wall.LeftRegion == region || wall.RightRegion == region)
            {
                grew |= into.Add(wall.LeftRegion);
                grew |= into.Add(wall.RightRegion);
            }
        }

        return grew;
    }
}
