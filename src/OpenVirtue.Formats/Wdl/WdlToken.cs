// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Formats.Wdl;

/// <summary>The lexical categories of the WDL scripting language.</summary>
public enum WdlTokenKind
{
    /// <summary>An identifier or instruction keyword (e.g. <c>REGION</c>, <c>FLOOR_HGT</c>, <c>start_game</c>).</summary>
    Identifier,

    /// <summary>A numeric literal (integer or fixed-point), held as text.</summary>
    Number,

    /// <summary>A double-quoted string literal (content only; quotes stripped, no escape processing).</summary>
    StringLiteral,

    /// <summary>An angle-bracket file reference such as <c>&lt;start.wmp&gt;</c> (content only).</summary>
    FileRef,

    LBrace,
    RBrace,
    LParen,
    RParen,
    Comma,
    Semicolon,

    /// <summary>A colon — used as a label marker (e.g. <c>doHideStuff:</c>).</summary>
    Colon,

    /// <summary>An operator such as <c>=</c>, <c>+=</c>, <c>&lt;</c>, <c>==</c>, <c>.</c> (member access).</summary>
    Operator,

    /// <summary>End-of-input marker.</summary>
    EndOfFile,
}

/// <summary>A single WDL token with its source position (1-based line/column).</summary>
public readonly record struct WdlToken(WdlTokenKind Kind, string Text, int Line, int Column)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Kind}('{Text}') @ {Line}:{Column}";
}
