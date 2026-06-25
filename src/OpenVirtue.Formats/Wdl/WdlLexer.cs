// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text;

namespace OpenVirtue.Formats.Wdl;

/// <summary>
/// Tokenizer for the WDL (Wad Definition Language) scripts that drive Acknex-3
/// games. Produces a flat token stream for the parser; it classifies lexemes only
/// and does not recognize keywords (the parser does that, case-insensitively).
/// </summary>
/// <remarks>
/// Clean-room implementation built from observation of the language's surface
/// syntax (see <c>PROVENANCE.md</c>): <c>//</c> and <c>#</c> line comments and
/// <c>/* */</c> block comments; identifiers; numeric literals; <c>"strings"</c>
/// (no escape processing — the DOS engine treats backslashes literally);
/// <c>&lt;file&gt;</c> references; braces/parens/commas/semicolons; and arithmetic
/// and comparison operators. <c>&lt;</c> is disambiguated from a file reference by
/// look-ahead: it is a file reference only when a run of filename characters is
/// immediately followed by <c>&gt;</c>.
/// </remarks>
public sealed class WdlLexer
{
    private readonly string _source;
    private int _pos;
    private int _line = 1;
    private int _column = 1;

    private WdlLexer(string source) => _source = source;

    /// <summary>Tokenizes a WDL source string. The final token is always <see cref="WdlTokenKind.EndOfFile"/>.</summary>
    /// <exception cref="InvalidDataException">An unterminated string or unexpected character was encountered.</exception>
    public static IReadOnlyList<WdlToken> Tokenize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WdlLexer(source).Run();
    }

    private bool AtEnd => _pos >= _source.Length;
    private char Current => _source[_pos];

    private List<WdlToken> Run()
    {
        var tokens = new List<WdlToken>();
        while (true)
        {
            SkipTrivia();
            if (AtEnd)
            {
                break;
            }

            tokens.Add(NextToken());
        }

        tokens.Add(new WdlToken(WdlTokenKind.EndOfFile, string.Empty, _line, _column));
        return tokens;
    }

    private char Peek(int ahead = 1)
    {
        int index = _pos + ahead;
        return index < _source.Length ? _source[index] : '\0';
    }

    private void Advance()
    {
        if (_source[_pos] == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }

        _pos++;
    }

    private void SkipTrivia()
    {
        while (!AtEnd)
        {
            char c = Current;
            if (c is ' ' or '\t' or '\r' or '\n')
            {
                Advance();
            }
            else if ((c == '/' && Peek() == '/') || c == '#')
            {
                while (!AtEnd && Current != '\n')
                {
                    Advance();
                }
            }
            else if (c == '/' && Peek() == '*')
            {
                Advance();
                Advance();
                while (!AtEnd && !(Current == '*' && Peek() == '/'))
                {
                    Advance();
                }

                if (!AtEnd)
                {
                    Advance();
                    Advance();
                }
            }
            else
            {
                break;
            }
        }
    }

    private WdlToken NextToken()
    {
        int line = _line;
        int column = _column;
        char c = Current;

        if (c == '"')
        {
            return ReadString(line, column);
        }

        if (c == '<' && TryReadFileRef(line, column) is { } fileRef)
        {
            return fileRef;
        }

        if (IsIdentifierStart(c))
        {
            return ReadIdentifier(line, column);
        }

        if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek())))
        {
            return ReadNumber(line, column);
        }

        switch (c)
        {
            case '{': Advance(); return new WdlToken(WdlTokenKind.LBrace, "{", line, column);
            case '}': Advance(); return new WdlToken(WdlTokenKind.RBrace, "}", line, column);
            case '(': Advance(); return new WdlToken(WdlTokenKind.LParen, "(", line, column);
            case ')': Advance(); return new WdlToken(WdlTokenKind.RParen, ")", line, column);
            case ',': Advance(); return new WdlToken(WdlTokenKind.Comma, ",", line, column);
            case ';': Advance(); return new WdlToken(WdlTokenKind.Semicolon, ";", line, column);
            case ':': Advance(); return new WdlToken(WdlTokenKind.Colon, ":", line, column);
            default: return ReadOperator(line, column);
        }
    }

    private WdlToken ReadString(int line, int column)
    {
        Advance(); // opening quote
        var sb = new StringBuilder();

        // WDL string literals may span physical lines (e.g. multi-line sign/scroll
        // text), so newlines are part of the content. Advance() keeps line tracking
        // correct. Only a missing closing quote (EOF) is an error.
        while (!AtEnd && Current != '"')
        {
            sb.Append(Current);
            Advance();
        }

        if (AtEnd)
        {
            throw Error("Unterminated string literal.", line, column);
        }

        Advance(); // closing quote
        return new WdlToken(WdlTokenKind.String, sb.ToString(), line, column);
    }

    private WdlToken? TryReadFileRef(int line, int column)
    {
        int savedPos = _pos, savedLine = _line, savedColumn = _column;

        Advance(); // consume '<'
        var sb = new StringBuilder();
        while (!AtEnd && IsFileNameChar(Current))
        {
            sb.Append(Current);
            Advance();
        }

        if (sb.Length > 0 && !AtEnd && Current == '>')
        {
            Advance(); // consume '>'
            return new WdlToken(WdlTokenKind.FileRef, sb.ToString(), line, column);
        }

        // Not a file reference — rewind and let '<' be read as an operator.
        _pos = savedPos;
        _line = savedLine;
        _column = savedColumn;
        return null;
    }

    private WdlToken ReadIdentifier(int line, int column)
    {
        int start = _pos;
        while (!AtEnd && IsIdentifierPart(Current))
        {
            Advance();
        }

        return new WdlToken(WdlTokenKind.Identifier, _source[start.._pos], line, column);
    }

    private WdlToken ReadNumber(int line, int column)
    {
        int start = _pos;
        bool seenDot = false;
        while (!AtEnd && (char.IsDigit(Current) || (Current == '.' && !seenDot && char.IsDigit(Peek()))))
        {
            if (Current == '.')
            {
                seenDot = true;
            }

            Advance();
        }

        return new WdlToken(WdlTokenKind.Number, _source[start.._pos], line, column);
    }

    private WdlToken ReadOperator(int line, int column)
    {
        char c = Current;
        char next = Peek();

        // Two-character operators.
        if ((c is '+' or '-' or '*' or '/' or '=' or '!' or '<' or '>') && next == '=')
        {
            Advance();
            Advance();
            return new WdlToken(WdlTokenKind.Operator, new string([c, '=']), line, column);
        }

        if ((c == '&' && next == '&') || (c == '|' && next == '|'))
        {
            Advance();
            Advance();
            return new WdlToken(WdlTokenKind.Operator, new string([c, next]), line, column);
        }

        // '.' is the member-access operator (e.g. `my.x`). A '.' that begins a number
        // (e.g. `.5`) is handled earlier by ReadNumber.
        if (c is '=' or '+' or '-' or '*' or '/' or '<' or '>' or '!' or '&' or '|' or '%' or '.')
        {
            Advance();
            return new WdlToken(WdlTokenKind.Operator, c.ToString(), line, column);
        }

        throw Error($"Unexpected character '{c}' (0x{(int)c:X2}).", line, column);
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsFileNameChar(char c) =>
        char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '\\' or '/' or ':';

    private static InvalidDataException Error(string message, int line, int column) =>
        new($"WDL lex error at {line}:{column}: {message}");
}
