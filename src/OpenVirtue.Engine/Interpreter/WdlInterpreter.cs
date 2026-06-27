// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Formats.Wdl;

namespace OpenVirtue.Engine.Interpreter;

/// <summary>
/// Executes WDL statement blocks (ACTION bodies and nested IF blocks) against an
/// <see cref="IWdlContext"/>. This is the first slice of the runtime: <c>SET</c> and
/// <c>RULE</c> assignments (including <c>+=</c>/<c>-=</c>/<c>*=</c>/<c>/=</c>) and
/// <c>IF (expr) { … }</c> conditionals. More statements (loops, IF_* skips, WAIT,
/// function calls) and the fixed-tick scheduler are layered on later.
/// </summary>
public sealed class WdlInterpreter(IWdlContext context)
{
    /// <summary>Executes all statements in a block, in order.</summary>
    public void Execute(WdlBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        ExecuteItems(block.Items);
    }

    /// <summary>Executes the top-level items of a document as statements.</summary>
    public void Execute(WdlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ExecuteItems(document.Items);
    }

    private void ExecuteItems(IReadOnlyList<WdlItem> items)
    {
        foreach (WdlItem item in items)
        {
            if (item.IsLabel)
            {
                continue;
            }

            try
            {
                switch (item.Keyword.ToUpperInvariant())
                {
                    case "SET":
                        ExecuteSet(item);
                        break;
                    case "RULE":
                        ExecuteRule(item);
                        break;
                    case "IF":
                        ExecuteIf(item);
                        break;

                    default:
                        // A bare keyword may name another action to run; the context invokes it
                        // if it exists and ignores it otherwise (unmodelled instructions are no-ops).
                        context.CallAction(item.Keyword);
                        break;
                }
            }
            catch (InvalidDataException)
            {
                // The statement uses an expression form the evaluator doesn't model yet (e.g. a
                // string literal or an unsupported token). Skip it and continue the block rather
                // than aborting the whole action — fidelity grows as the evaluator does.
            }
        }
    }

    private void ExecuteSet(WdlItem item)
    {
        // SET target, expression
        int comma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (comma < 0)
        {
            return;
        }

        double value = Evaluate(item.Header, comma + 1, item.Header.Count);
        Assign(item.Header, 0, comma, "=", value);
    }

    private void ExecuteRule(WdlItem item)
    {
        // RULE target <assign-op> expression
        int op = IndexOfAssignmentOperator(item.Header);
        if (op < 0)
        {
            return;
        }

        double value = Evaluate(item.Header, op + 1, item.Header.Count);
        Assign(item.Header, 0, op, item.Header[op].Text, value);
    }

    private void ExecuteIf(WdlItem item)
    {
        // IF (expr) { body }  — the parenthesised header parses directly as a grouped expression.
        if (item.Body is { } body && WdlExpressionParser.Parse(item.Header).Evaluate(context) != 0)
        {
            Execute(body);
        }
    }

    private double Evaluate(IReadOnlyList<WdlToken> tokens, int start, int end) =>
        WdlExpressionParser.Parse(Slice(tokens, start, end)).Evaluate(context);

    private void Assign(IReadOnlyList<WdlToken> header, int start, int end, string op, double rvalue)
    {
        if (end <= start)
        {
            return;
        }

        string name = header[start].Text;

        // Member target: name '.' property
        if (end - start >= 3 && header[start + 1] is { Kind: WdlTokenKind.Operator, Text: "." })
        {
            string property = header[start + 2].Text;
            if (context.GetObject(name) is { } obj)
            {
                obj[property] = Apply(op, obj[property], rvalue);
            }

            return;
        }

        context.SetSkill(name, Apply(op, context.GetSkill(name), rvalue));
    }

    private static double Apply(string op, double current, double rvalue) => op switch
    {
        "=" => rvalue,
        "+=" => current + rvalue,
        "-=" => current - rvalue,
        "*=" => current * rvalue,
        "/=" => rvalue != 0 ? current / rvalue : current,
        _ => rvalue,
    };

    private static int IndexOfKind(IReadOnlyList<WdlToken> tokens, WdlTokenKind kind)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == kind)
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfAssignmentOperator(IReadOnlyList<WdlToken> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == WdlTokenKind.Operator &&
                tokens[i].Text is "=" or "+=" or "-=" or "*=" or "/=")
            {
                return i;
            }
        }

        return -1;
    }

    private static List<WdlToken> Slice(IReadOnlyList<WdlToken> tokens, int start, int end)
    {
        var slice = new List<WdlToken>(end - start);
        for (int i = start; i < end; i++)
        {
            slice.Add(tokens[i]);
        }

        return slice;
    }
}
