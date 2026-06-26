// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using OpenVirtue.Formats.Wdl;

namespace OpenVirtue.Engine.Interpreter;

/// <summary>
/// Parses WDL expressions (the token runs that appear in statement headers, e.g.
/// <c>gravity + float_str*forceJump</c> or <c>tempVal &lt; 0.8</c>) into an evaluable
/// <see cref="WdlExpression"/> tree, using standard precedence: logical-or &lt;
/// logical-and &lt; comparison &lt; additive &lt; multiplicative &lt; unary &lt; primary.
/// </summary>
public static class WdlExpressionParser
{
    /// <summary>Tokenizes and parses an expression from source text.</summary>
    public static WdlExpression Parse(string source) => Parse(WdlLexer.Tokenize(source));

    /// <summary>Parses an expression from a token list (a trailing <c>EndOfFile</c> is allowed).</summary>
    /// <exception cref="InvalidDataException">The tokens are not a valid expression.</exception>
    public static WdlExpression Parse(IReadOnlyList<WdlToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return new Cursor(tokens).ParseExpression();
    }

    private sealed class Cursor(IReadOnlyList<WdlToken> tokens)
    {
        private static readonly WdlToken EndOfInput = new(WdlTokenKind.EndOfFile, string.Empty, 0, 0);

        private int _pos;

        // Bounds-safe: callers may pass a header slice with no trailing EndOfFile token.
        private WdlToken Current => _pos < tokens.Count ? tokens[_pos] : EndOfInput;

        private void Advance() => _pos++;

        public WdlExpression ParseExpression() => ParseLogicalOr();

        private WdlExpression ParseLogicalOr()
        {
            WdlExpression left = ParseLogicalAnd();
            while (IsOperator(out string op) && op == "||")
            {
                Advance();
                left = new BinaryExpression(op, left, ParseLogicalAnd());
            }

            return left;
        }

        private WdlExpression ParseLogicalAnd()
        {
            WdlExpression left = ParseComparison();
            while (IsOperator(out string op) && op == "&&")
            {
                Advance();
                left = new BinaryExpression(op, left, ParseComparison());
            }

            return left;
        }

        private WdlExpression ParseComparison()
        {
            WdlExpression left = ParseAdditive();
            while (IsOperator(out string op) && op is "<" or ">" or "<=" or ">=" or "==" or "!=")
            {
                Advance();
                left = new BinaryExpression(op, left, ParseAdditive());
            }

            return left;
        }

        private WdlExpression ParseAdditive()
        {
            WdlExpression left = ParseMultiplicative();
            while (IsOperator(out string op) && op is "+" or "-")
            {
                Advance();
                left = new BinaryExpression(op, left, ParseMultiplicative());
            }

            return left;
        }

        private WdlExpression ParseMultiplicative()
        {
            WdlExpression left = ParseUnary();
            while (IsOperator(out string op) && op is "*" or "/" or "%")
            {
                Advance();
                left = new BinaryExpression(op, left, ParseUnary());
            }

            return left;
        }

        private WdlExpression ParseUnary()
        {
            if (IsOperator(out string op) && op is "-" or "!")
            {
                Advance();
                return new UnaryExpression(op, ParseUnary());
            }

            return ParsePrimary();
        }

        private WdlExpression ParsePrimary()
        {
            WdlToken token = Current;
            switch (token.Kind)
            {
                case WdlTokenKind.Number:
                    Advance();
                    return new NumberExpression(double.Parse(token.Text, CultureInfo.InvariantCulture));

                case WdlTokenKind.Identifier:
                    Advance();
                    if (IsOperator(out string dot) && dot == ".")
                    {
                        Advance(); // '.'
                        if (Current.Kind != WdlTokenKind.Identifier)
                        {
                            throw Error("Expected a property name after '.'.");
                        }

                        string property = Current.Text;
                        Advance();
                        return new MemberExpression(token.Text, property);
                    }

                    return new NameExpression(token.Text);

                case WdlTokenKind.LParen:
                    Advance();
                    WdlExpression inner = ParseExpression();
                    if (Current.Kind != WdlTokenKind.RParen)
                    {
                        throw Error("Expected ')'.");
                    }

                    Advance();
                    return inner;

                default:
                    throw Error($"Unexpected token {token.Kind} '{token.Text}' in expression.");
            }
        }

        private bool IsOperator(out string text)
        {
            if (Current.Kind == WdlTokenKind.Operator)
            {
                text = Current.Text;
                return true;
            }

            text = string.Empty;
            return false;
        }

        private InvalidDataException Error(string message) =>
            new($"WDL expression error at {Current.Line}:{Current.Column}: {message}");
    }
}
