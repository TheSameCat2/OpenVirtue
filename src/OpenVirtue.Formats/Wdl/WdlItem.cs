// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Formats.Wdl;

/// <summary>
/// A node in the generic WDL syntax tree: an instruction <see cref="Keyword"/>,
/// the <see cref="Header"/> tokens that follow it (a name, arguments, or an
/// expression), and an optional <see cref="Body"/> when the item has a
/// <c>{ … }</c> block.
/// </summary>
/// <remarks>
/// The parser captures structure, not statement semantics — declarations
/// (<c>REGION name { … }</c>), inline statements (<c>SET x, 1;</c>), nested blocks
/// (<c>IF (…) { … }</c>), and labels (<c>name:</c>) are all represented uniformly.
/// Interpreting what an item <em>means</em> is the job of later layers.
/// </remarks>
public sealed class WdlItem
{
    public WdlItem(string keyword, IReadOnlyList<WdlToken> header, WdlBlock? body, bool isLabel)
    {
        Keyword = keyword;
        Header = header;
        Body = body;
        IsLabel = isLabel;
    }

    /// <summary>The leading instruction/declaration keyword or, for a label, the label name.</summary>
    public string Keyword { get; }

    /// <summary>Tokens between the keyword and the block/terminator (name, args, expression).</summary>
    public IReadOnlyList<WdlToken> Header { get; }

    /// <summary>The <c>{ … }</c> block, or <c>null</c> if the item has none.</summary>
    public WdlBlock? Body { get; }

    /// <summary>True when this item is a <c>name:</c> label rather than an instruction.</summary>
    public bool IsLabel { get; }

    /// <summary>Whether the item has a brace block.</summary>
    public bool HasBody => Body is not null;

    /// <inheritdoc/>
    public override string ToString() =>
        IsLabel ? $"{Keyword}:" : $"{Keyword}[{Header.Count}]{(HasBody ? $" {{{Body!.Items.Count}}}" : string.Empty)}";
}

/// <summary>An ordered list of <see cref="WdlItem"/>s inside a <c>{ … }</c> block (or the file root).</summary>
public sealed class WdlBlock
{
    public WdlBlock(IReadOnlyList<WdlItem> items) => Items = items;

    /// <summary>The items in this block, in source order.</summary>
    public IReadOnlyList<WdlItem> Items { get; }
}

/// <summary>A parsed WDL document — the top-level sequence of items.</summary>
public sealed class WdlDocument
{
    public WdlDocument(IReadOnlyList<WdlItem> items) => Items = items;

    /// <summary>The top-level items, in source order.</summary>
    public IReadOnlyList<WdlItem> Items { get; }
}
