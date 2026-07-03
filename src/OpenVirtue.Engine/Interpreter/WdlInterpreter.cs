// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using OpenVirtue.Formats.Wdl;

namespace OpenVirtue.Engine.Interpreter;

/// <summary>
/// Executes WDL statement blocks (ACTION bodies and nested IF blocks) against an
/// <see cref="IWdlContext"/>. This is the first slice of the runtime: <c>SET</c> and
/// <c>RULE</c> assignments (including <c>+=</c>/<c>-=</c>/<c>*=</c>/<c>/=</c>) and
/// <c>IF (expr) { … }</c> conditionals, synchronous <c>WHILE</c> loops, explicit
/// <c>CALL</c>s, <c>GOTO</c>, <c>END</c>, and basic object-reference iteration via
/// <c>NEXT_MY</c>/<c>NEXT_THERE</c>, plus documented string utility instructions.
/// Optional runtime surfaces also handle documented print-file instructions. More statements
/// (IF_* skips, WAIT) and deeper scheduler semantics are layered on later.
/// </summary>
public sealed class WdlInterpreter(IWdlContext context)
{
    private const int MaxStatementSteps = 100_000;

    /// <summary>Executes all statements in a block, in order.</summary>
    public void Execute(WdlBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        try
        {
            ExecuteItems(block.Items);
        }
        catch (EndActionException)
        {
            // END terminates the current action.
        }
        catch (GotoException)
        {
            // An unresolved GOTO leaves the current action; valid targets are handled by ExecuteItems.
        }
        catch (LoopControlException)
        {
            // BREAK/CONTINUE outside a WHILE terminate the current action.
        }
    }

    /// <summary>Executes the top-level items of a document as statements.</summary>
    public void Execute(WdlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            ExecuteItems(document.Items);
        }
        catch (EndActionException)
        {
            // END terminates the current action/document execution.
        }
        catch (GotoException)
        {
            // An unresolved GOTO leaves the current action/document execution.
        }
        catch (LoopControlException)
        {
            // BREAK/CONTINUE outside a WHILE terminate the current action/document execution.
        }
    }

    private void ExecuteItems(IReadOnlyList<WdlItem> items)
    {
        Dictionary<string, int> labels = BuildLabelIndex(items);
        int steps = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (++steps > MaxStatementSteps)
            {
                throw new WdlExpressionException("WDL action exceeded the statement execution limit.");
            }

            WdlItem item = items[i];
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
                    case "SET_ALL":
                        ExecuteSetAll(item);
                        break;
                    case "SET_STRING":
                        ExecuteSetString(item, append: false);
                        break;
                    case "ADD_STRING":
                        ExecuteSetString(item, append: true);
                        break;
                    case "TO_STRING":
                        ExecuteToString(item);
                        break;
                    case "SET_SKILL":
                        ExecuteSetSkill(item);
                        break;
                    case "PRINTFILE":
                        ExecutePrintFile(item);
                        break;
                    case "PRINT_VALUE":
                        ExecutePrintValue(item);
                        break;
                    case "PRINT_STRING":
                        ExecutePrintString(item);
                        break;
                    case "RULE":
                        ExecuteRule(item);
                        break;
                    case "IF":
                        bool condition = ExecuteIf(item);
                        if (i + 1 < items.Count && IsElse(items[i + 1]))
                        {
                            if (!condition && items[i + 1].Body is { } elseBody)
                            {
                                ExecuteItems(elseBody.Items);
                            }

                            i++;
                        }
                        break;
                    case "ELSE":
                        break;
                    case "WHILE":
                        ExecuteWhile(item);
                        break;
                    case "BREAK":
                        throw new BreakLoopException();
                    case "CONTINUE":
                        throw new ContinueLoopException();
                    case "CALL":
                        ExecuteCall(item);
                        break;
                    case "NEXT_MY":
                        ExecuteNextObject("my", WdlObjectIterationMode.SameName);
                        break;
                    case "NEXT_THERE":
                        ExecuteNextObject("there", WdlObjectIterationMode.SameName);
                        break;
                    case "NEXT_MY_THERE":
                        ExecuteNextObject("my", WdlObjectIterationMode.SameRegion);
                        break;
                    case "GOTO":
                        ExecuteGoto(item);
                        break;
                    case "END":
                        throw new EndActionException();

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
            catch (GotoException jump) when (labels.TryGetValue(jump.Label, out int target))
            {
                i = target;
            }
        }
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

    private void ExecuteSet(WdlItem item)
    {
        // SET target, expression
        int comma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (comma < 0)
        {
            return;
        }

        if (TryExecuteEachTickSet(item.Header, comma))
        {
            return;
        }

        if (TryExecuteReferenceSet(item.Header, comma))
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

    private bool ExecuteIf(WdlItem item)
    {
        // IF (expr) { body }  — the parenthesised header parses directly as a grouped expression.
        bool condition = WdlExpressionParser.Parse(item.Header).Evaluate(context) != 0;
        if (condition && item.Body is { } body)
        {
            ExecuteItems(body.Items);
        }

        return condition;
    }

    private static bool IsElse(WdlItem item) =>
        !item.IsLabel && item.Keyword.Equals("ELSE", StringComparison.OrdinalIgnoreCase);

    private void ExecuteWhile(WdlItem item)
    {
        if (item.Body is not { } body)
        {
            return;
        }

        int iterations = 0;
        while (WdlExpressionParser.Parse(item.Header).Evaluate(context) != 0)
        {
            if (++iterations > MaxStatementSteps)
            {
                throw new WdlExpressionException("WDL WHILE loop exceeded the iteration limit.");
            }

            try
            {
                ExecuteItems(body.Items);
            }
            catch (ContinueLoopException)
            {
                continue;
            }
            catch (BreakLoopException)
            {
                break;
            }
        }
    }

    private void ExecuteCall(WdlItem item)
    {
        if (item.Header.Count > 0 && item.Header[0].Kind == WdlTokenKind.Identifier)
        {
            context.CallAction(item.Header[0].Text);
        }
    }

    private static void ExecuteGoto(WdlItem item)
    {
        if (item.Header.Count > 0 && item.Header[0].Kind == WdlTokenKind.Identifier)
        {
            throw new GotoException(item.Header[0].Text);
        }
    }

    private void ExecuteSetString(WdlItem item, bool append)
    {
        if (context is not IWdlStringContext strings ||
            !TryStringInstruction(item.Header, out string target, out int comma) ||
            !TryEvaluateString(item.Header, comma + 1, item.Header.Count, out string value))
        {
            return;
        }

        if (append && strings.TryGetString(target, out string current))
        {
            strings.SetString(target, current + value);
            return;
        }

        strings.SetString(target, value);
    }

    private void ExecuteToString(WdlItem item)
    {
        if (context is not IWdlStringContext strings ||
            !TryStringInstruction(item.Header, out string target, out int comma))
        {
            return;
        }

        double value = Evaluate(item.Header, comma + 1, item.Header.Count);
        strings.SetString(target, value.ToString("G15", CultureInfo.InvariantCulture));
    }

    private void ExecuteSetSkill(WdlItem item)
    {
        if (context is not IWdlStringContext ||
            !TryStringInstruction(item.Header, out string target, out int comma) ||
            !TryEvaluateString(item.Header, comma + 1, item.Header.Count, out string value) ||
            !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return;
        }

        context.SetSkill(target, parsed);
    }

    private void ExecutePrintFile(WdlItem item)
    {
        int comma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (context is not IWdlPrintContext print ||
            comma < 0 ||
            !TryEvaluateString(item.Header, 0, comma, out string baseName))
        {
            return;
        }

        int number = (int)Math.Truncate(Evaluate(item.Header, comma + 1, item.Header.Count));
        print.SetPrintFile(baseName, number);
    }

    private void ExecutePrintValue(WdlItem item)
    {
        if (context is IWdlPrintContext print && item.Header.Count > 0)
        {
            print.PrintValue(Evaluate(item.Header, 0, item.Header.Count));
        }
    }

    private void ExecutePrintString(WdlItem item)
    {
        if (context is not IWdlPrintContext print ||
            !TryEvaluateString(item.Header, 0, item.Header.Count, out string value))
        {
            return;
        }

        print.PrintString(value.Replace("\\n", Environment.NewLine, StringComparison.Ordinal));
    }

    private void ExecuteNextObject(string referenceName, WdlObjectIterationMode mode)
    {
        if (context is IWdlObjectReferenceContext references)
        {
            references.AdvanceObjectReference(referenceName, mode);
        }
    }

    private bool TryExecuteEachTickSet(IReadOnlyList<WdlToken> header, int comma)
    {
        if (!IsEachTickTarget(header, comma, out int slot))
        {
            return false;
        }

        if (slot is < 1 or > 16 || context is not IWdlSchedulerContext scheduler)
        {
            return true;
        }

        string? actionName = null;
        if (comma + 1 < header.Count && header[comma + 1].Kind == WdlTokenKind.Identifier)
        {
            actionName = header[comma + 1].Text.Equals("NULL", StringComparison.OrdinalIgnoreCase)
                ? null
                : header[comma + 1].Text;
        }

        scheduler.SetEachTickAction(slot, actionName);
        return true;
    }

    private bool TryExecuteReferenceSet(IReadOnlyList<WdlToken> header, int comma)
    {
        if (context is not IWdlObjectReferenceContext references ||
            comma != 1 ||
            header[0].Kind != WdlTokenKind.Identifier ||
            comma + 2 != header.Count ||
            header[comma + 1].Kind != WdlTokenKind.Identifier ||
            !TryResolveObjectReference(header[comma + 1].Text, out AcknexObject? value))
        {
            return false;
        }

        references.SetObjectReference(header[0].Text, value);
        return true;
    }

    private bool TryResolveObjectReference(string name, out AcknexObject? value)
    {
        if (name.Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }

        if (context.GetObject(name) is { } reference)
        {
            value = reference;
            return true;
        }

        if (context is IWdlObjectGroupContext groups)
        {
            value = groups.GetObjectsByName(name).FirstOrDefault();
            return value is not null;
        }

        value = null;
        return false;
    }

    private bool TryEvaluateString(IReadOnlyList<WdlToken> header, int start, int end, out string value)
    {
        value = string.Empty;
        if (end - start != 1)
        {
            return false;
        }

        WdlToken token = header[start];
        if (token.Kind == WdlTokenKind.StringLiteral)
        {
            value = token.Text;
            return true;
        }

        return token.Kind == WdlTokenKind.Identifier &&
               context is IWdlStringContext strings &&
               strings.TryGetString(token.Text, out value);
    }

    private static bool IsEachTickTarget(IReadOnlyList<WdlToken> header, int comma, out int slot)
    {
        slot = 0;
        if (comma <= 0 || header.Count == 0)
        {
            return false;
        }

        if (header[0] is not { Kind: WdlTokenKind.Identifier, Text: var schedulerName } ||
            !schedulerName.Equals("EACH_TICK", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (comma == 2 && header[1].Kind == WdlTokenKind.Number && header[1].Text.StartsWith('.'))
        {
            return int.TryParse(header[1].Text[1..], out slot);
        }

        if (comma == 3 &&
            header[1] is { Kind: WdlTokenKind.Operator, Text: "." } &&
            header[2].Kind == WdlTokenKind.Number)
        {
            return int.TryParse(header[2].Text, out slot);
        }

        return false;
    }

    private static bool TryStringInstruction(
        IReadOnlyList<WdlToken> header,
        out string target,
        out int comma)
    {
        target = string.Empty;
        comma = IndexOfKind(header, WdlTokenKind.Comma);
        if (comma != 1 || header[0].Kind != WdlTokenKind.Identifier || comma + 1 >= header.Count)
        {
            return false;
        }

        target = header[0].Text;
        return true;
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

    private void ExecuteSetAll(WdlItem item)
    {
        int comma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (comma < 0 ||
            context is not IWdlObjectGroupContext groups ||
            !TryMemberTarget(item.Header, 0, comma, out string objectName, out string property) ||
            IsSetAllPositionProperty(property))
        {
            return;
        }

        double value = Evaluate(item.Header, comma + 1, item.Header.Count);
        foreach (AcknexObject obj in groups.GetObjectsByName(objectName))
        {
            obj[property] = value;
        }
    }

    private static bool TryMemberTarget(
        IReadOnlyList<WdlToken> header, int start, int end, out string objectName, out string property)
    {
        objectName = string.Empty;
        property = string.Empty;
        if (end - start < 3 ||
            header[start].Kind != WdlTokenKind.Identifier ||
            header[start + 1] is not { Kind: WdlTokenKind.Operator, Text: "." } ||
            header[start + 2].Kind != WdlTokenKind.Identifier)
        {
            return false;
        }

        objectName = header[start].Text;
        property = header[start + 2].Text;
        return true;
    }

    private static bool IsSetAllPositionProperty(string property) =>
        property.Equals("X", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("Z", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("X1", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("Y1", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("Z1", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("X2", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("Y2", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("Z2", StringComparison.OrdinalIgnoreCase);

    private static double Apply(string op, double current, double rvalue) => op switch
    {
        "=" => rvalue,
        "+=" => current + rvalue,
        "-=" => current - rvalue,
        "*=" => current * rvalue,
        "/=" => rvalue != 0 ? current / rvalue : throw new WdlExpressionException("Division by zero."),
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

    private sealed class EndActionException : Exception
    {
    }

    private sealed class GotoException(string label) : Exception
    {
        public string Label => label;
    }

    private abstract class LoopControlException : Exception
    {
    }

    private sealed class BreakLoopException : LoopControlException
    {
    }

    private sealed class ContinueLoopException : LoopControlException
    {
    }
}
