// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Formats.Wdl;

namespace OpenVirtue.Engine.Interpreter;

/// <summary>How a WDL <c>WAIT</c>-family instruction measures its delay.</summary>
public enum WdlWaitMode
{
    /// <summary>Delay by rendered frame cycles.</summary>
    FrameCycles,

    /// <summary>Delay by fixed Acknex ticks.</summary>
    Ticks,
}

/// <summary>A pending delay requested by <c>WAIT</c> or <c>WAITT</c>.</summary>
public readonly record struct WdlWaitRequest(double Amount, WdlWaitMode Mode);

/// <summary>The result of advancing a resumable WDL action.</summary>
public enum WdlExecutionStatus
{
    /// <summary>The action ran to completion.</summary>
    Completed,

    /// <summary>The action suspended on a <c>WAIT</c>-family instruction.</summary>
    Waiting,

    /// <summary>The action suspended while waiting for host text input.</summary>
    InputWaiting,

    /// <summary>The action suspended while a host flic animation is active.</summary>
    FlicWaiting,
}

/// <summary>The result of advancing a resumable WDL action.</summary>
public readonly record struct WdlExecutionResult(
    WdlExecutionStatus Status,
    WdlWaitRequest? Wait,
    WdlInputRequest? Input,
    WdlFlicRequest? Flic)
{
    public WdlExecutionResult(WdlExecutionStatus status, WdlWaitRequest? wait)
        : this(status, wait, Input: null, Flic: null)
    {
    }

    public WdlExecutionResult(WdlExecutionStatus status, WdlWaitRequest? wait, WdlInputRequest? input)
        : this(status, wait, input, Flic: null)
    {
    }
}

/// <summary>Mutable interpreter state for a WDL action that can suspend and resume.</summary>
public sealed class WdlExecutionState
{
    private readonly Stack<WdlExecutionFrame> _frames = new();

    internal WdlExecutionState(WdlBlock block)
    {
        _frames.Push(WdlExecutionFrame.Block(block.Items));
    }

    internal Stack<WdlExecutionFrame> Frames => _frames;

    /// <summary>Whether no more statements remain to execute.</summary>
    public bool IsComplete => _frames.Count == 0;
}

internal enum WdlExecutionFrameKind
{
    Block,
    While,
}

internal sealed class WdlExecutionFrame
{
    private WdlExecutionFrame(
        IReadOnlyList<WdlItem> items,
        WdlExecutionFrameKind kind,
        IReadOnlyList<WdlToken>? condition)
    {
        Items = items;
        Kind = kind;
        Condition = condition;
        Labels = BuildLabelIndex(items);
    }

    public IReadOnlyList<WdlItem> Items { get; }

    public WdlExecutionFrameKind Kind { get; }

    public IReadOnlyList<WdlToken>? Condition { get; }

    public Dictionary<string, int> Labels { get; }

    public int Index { get; set; }

    public static WdlExecutionFrame Block(IReadOnlyList<WdlItem> items) =>
        new(items, WdlExecutionFrameKind.Block, condition: null);

    public static WdlExecutionFrame While(WdlItem item)
    {
        WdlBlock body = item.Body ?? new WdlBlock([]);
        return new WdlExecutionFrame(body.Items, WdlExecutionFrameKind.While, item.Header);
    }

    private static Dictionary<string, int> BuildLabelIndex(IReadOnlyList<WdlItem> items)
    {
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].IsLabel)
            {
                labels[items[i].Keyword] = i;
            }
        }

        return labels;
    }
}
