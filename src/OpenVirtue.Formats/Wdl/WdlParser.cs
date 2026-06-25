// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Formats.Wdl;

/// <summary>
/// Parses a WDL token stream into a generic syntax tree (<see cref="WdlDocument"/> /
/// <see cref="WdlItem"/>). Each item is <c>keyword header… ( { block } | ; )</c>, or a
/// <c>name:</c> label. The grammar is intentionally structural — it does not interpret
/// statements or expressions, which keeps it small and able to represent the whole
/// language (declarations, ACTION bodies, nested IF blocks) uniformly.
/// </summary>
/// <remarks>Clean-room implementation from the observed language — see <c>PROVENANCE.md</c>.</remarks>
public sealed class WdlParser
{
    private readonly IReadOnlyList<WdlToken> _tokens;
    private int _pos;

    private WdlParser(IReadOnlyList<WdlToken> tokens) => _tokens = tokens;

    /// <summary>Lexes and parses WDL source.</summary>
    /// <exception cref="InvalidDataException">The source is lexically or structurally invalid.</exception>
    public static WdlDocument Parse(string source) => Parse(WdlLexer.Tokenize(source));

    /// <summary>Parses an already-tokenized WDL stream (its final token must be <see cref="WdlTokenKind.EndOfFile"/>).</summary>
    /// <exception cref="InvalidDataException">The token stream is structurally invalid.</exception>
    public static WdlDocument Parse(IReadOnlyList<WdlToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var parser = new WdlParser(tokens);
        IReadOnlyList<WdlItem> items = parser.ParseItems(topLevel: true);
        return new WdlDocument(items);
    }

    private WdlToken Current => _tokens[_pos];

    private WdlToken Peek()
    {
        int next = _pos + 1;
        return next < _tokens.Count ? _tokens[next] : _tokens[^1];
    }

    private void Advance() => _pos++;

    private List<WdlItem> ParseItems(bool topLevel)
    {
        var items = new List<WdlItem>();

        while (true)
        {
            switch (Current.Kind)
            {
                case WdlTokenKind.EndOfFile:
                    if (!topLevel)
                    {
                        throw Error("Unexpected end of input inside a '{' block.");
                    }

                    return items;

                case WdlTokenKind.RBrace:
                    if (topLevel)
                    {
                        throw Error("Unexpected '}'.");
                    }

                    return items;

                case WdlTokenKind.Semicolon:
                    Advance(); // tolerate empty statements (e.g. ';;')
                    break;

                default:
                    items.Add(ParseItem());
                    break;
            }
        }
    }

    private WdlItem ParseItem()
    {
        if (Current.Kind != WdlTokenKind.Identifier)
        {
            throw Error($"Expected a keyword to start an item, got {Current.Kind} '{Current.Text}'.");
        }

        string keyword = Current.Text;

        // A label: an identifier immediately followed by ':'.
        if (Peek().Kind == WdlTokenKind.Colon)
        {
            Advance(); // keyword
            Advance(); // ':'
            return new WdlItem(keyword, [], body: null, isLabel: true);
        }

        Advance(); // consume keyword

        var header = new List<WdlToken>();
        while (Current.Kind is not (WdlTokenKind.LBrace or WdlTokenKind.Semicolon or WdlTokenKind.RBrace or WdlTokenKind.EndOfFile))
        {
            header.Add(Current);
            Advance();
        }

        WdlBlock? body = null;
        if (Current.Kind == WdlTokenKind.LBrace)
        {
            Advance(); // '{'
            List<WdlItem> inner = ParseItems(topLevel: false);
            if (Current.Kind != WdlTokenKind.RBrace)
            {
                throw Error("Unterminated '{' block.");
            }

            Advance(); // '}'
            body = new WdlBlock(inner);

            if (Current.Kind == WdlTokenKind.Semicolon)
            {
                Advance(); // optional ';' after a block
            }
        }
        else if (Current.Kind == WdlTokenKind.Semicolon)
        {
            Advance();
        }

        // Otherwise the item ends at a '}' or EOF without an explicit terminator — tolerated.
        return new WdlItem(keyword, header, body, isLabel: false);
    }

    private InvalidDataException Error(string message) =>
        new($"WDL parse error at {Current.Line}:{Current.Column}: {message}");
}
