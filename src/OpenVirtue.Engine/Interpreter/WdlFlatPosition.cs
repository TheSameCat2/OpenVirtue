// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Formats.Wdl;

namespace OpenVirtue.Engine.Interpreter;

/// <summary>
/// Places in an action counted the way the original's saves count them (OV-20261029-save-body round 8):
/// every instruction takes one, <c>IF</c> and <c>ELSE</c> bodies follow their item in line, and a
/// <c>WHILE</c> body is followed by one more place for its end.
/// </summary>
internal static class WdlFlatPosition
{
    /// <summary>
    /// The execution state of an action waiting at the instruction in place <paramref name="position"/>,
    /// ready to go on after it, and that instruction; <c>false</c> when the action has no such place.
    /// </summary>
    public static bool TryResumeAfter(WdlBlock action, int position, out WdlExecutionState state, out WdlItem item)
    {
        ArgumentNullException.ThrowIfNull(action);
        state = new WdlExecutionState(action);
        item = null!;
        int counter = 0;
        var path = new List<(IReadOnlyList<WdlItem> Items, int Index)>();
        if (!Find(action.Items, position, ref counter, path))
        {
            return false;
        }

        (IReadOnlyList<WdlItem> items, int index) = path[^1];
        item = items[index];
        state.Frames.Clear();
        for (int depth = 0; depth < path.Count; depth++)
        {
            (IReadOnlyList<WdlItem> frameItems, int frameIndex) = path[depth];
            WdlExecutionFrame frame;
            if (depth == 0)
            {
                frame = WdlExecutionFrame.Block(frameItems);
            }
            else
            {
                (IReadOnlyList<WdlItem> parentItems, int parentIndex) = path[depth - 1];
                frame = IsWhile(parentItems[parentIndex])
                    ? WdlExecutionFrame.While(parentItems[parentIndex])
                    : WdlExecutionFrame.Block(frameItems);
            }

            frame.Index = depth == path.Count - 1 ? frameIndex + 1 : ParentResume(frameItems, frameIndex);
            state.Frames.Push(frame);
        }

        return true;
    }

    /// <summary>
    /// The place of the instruction a waiting action waits in (the one before its innermost block's
    /// next), and the place after it, or 0 when that is the action's last; <c>false</c> when the state
    /// does not lie in <paramref name="action"/>.
    /// </summary>
    public static bool TryPositionOf(WdlBlock action, WdlExecutionState state, out int position, out int next)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(state);
        position = next = 0;
        if (state.Frames.Count == 0 || state.Frames.Peek() is not { Index: > 0 } top)
        {
            return false;
        }

        int counter = 0;
        int found = -1;
        Count(action.Items, top.Items, top.Index - 1, ref counter, ref found);
        if (found < 0)
        {
            return false;
        }

        position = found;
        next = found + 1 < counter ? found + 1 : 0;
        return true;
    }

    private static void Count(IReadOnlyList<WdlItem> items, IReadOnlyList<WdlItem> target, int targetIndex, ref int counter, ref int found)
    {
        for (int i = 0; i < items.Count; i++)
        {
            WdlItem item = items[i];
            if (item.IsLabel)
            {
                continue;
            }

            if (found < 0 && ReferenceEquals(items, target) && i == targetIndex)
            {
                found = counter;
            }

            counter++;
            if (item.Body is { } body && (Keyword(item, "IF") || Keyword(item, "ELSE") || IsWhile(item)))
            {
                Count(body.Items, target, targetIndex, ref counter, ref found);
                if (IsWhile(item))
                {
                    counter++;
                }
            }
        }
    }

    /// <summary>Where a block goes on after the body of its item <paramref name="index"/>, as the interpreter leaves it.</summary>
    private static int ParentResume(IReadOnlyList<WdlItem> items, int index) =>
        Keyword(items[index], "IF") && index + 1 < items.Count && Keyword(items[index + 1], "ELSE") ? index + 2 : index + 1;

    private static bool Find(IReadOnlyList<WdlItem> items, int target, ref int counter, List<(IReadOnlyList<WdlItem>, int)> path)
    {
        for (int i = 0; i < items.Count; i++)
        {
            WdlItem item = items[i];
            if (item.IsLabel)
            {
                continue;
            }

            path.Add((items, i));
            if (counter == target)
            {
                return true;
            }

            counter++;
            if (item.Body is { } body && (Keyword(item, "IF") || Keyword(item, "ELSE") || IsWhile(item)))
            {
                if (Find(body.Items, target, ref counter, path))
                {
                    return true;
                }

                if (IsWhile(item))
                {
                    counter++;
                }
            }

            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    private static bool IsWhile(WdlItem item) => Keyword(item, "WHILE");

    private static bool Keyword(WdlItem item, string keyword) => item.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase);
}
