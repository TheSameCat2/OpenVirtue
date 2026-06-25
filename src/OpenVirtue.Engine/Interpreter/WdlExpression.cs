// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Interpreter;

/// <summary>A WDL expression node that can be evaluated against an <see cref="IWdlContext"/>.</summary>
public abstract class WdlExpression
{
    /// <summary>Evaluates the expression. Comparisons yield 1.0 (true) or 0.0 (false).</summary>
    public abstract double Evaluate(IWdlContext context);
}

/// <summary>A numeric literal.</summary>
public sealed class NumberExpression(double value) : WdlExpression
{
    public double Value => value;

    public override double Evaluate(IWdlContext context) => value;
}

/// <summary>A bare name resolved as a skill/constant (e.g. <c>gravity</c>).</summary>
public sealed class NameExpression(string name) : WdlExpression
{
    public string Name => name;

    public override double Evaluate(IWdlContext context) => context.GetSkill(name);
}

/// <summary>Member access on an object reference (e.g. <c>my.x</c>, <c>you._x</c>).</summary>
public sealed class MemberExpression(string objectName, string property) : WdlExpression
{
    public string ObjectName => objectName;
    public string Property => property;

    public override double Evaluate(IWdlContext context) =>
        context.GetObject(objectName) is { } obj ? obj[property] : 0;
}

/// <summary>A unary operation: <c>-</c> (negation) or <c>!</c> (logical not).</summary>
public sealed class UnaryExpression(string op, WdlExpression operand) : WdlExpression
{
    public string Operator => op;
    public WdlExpression Operand => operand;

    public override double Evaluate(IWdlContext context)
    {
        double value = operand.Evaluate(context);
        return op switch
        {
            "-" => -value,
            "!" => value == 0 ? 1 : 0,
            _ => value,
        };
    }
}

/// <summary>A binary operation: arithmetic (<c>+ - * /</c>) or comparison (<c>&lt; &gt; &lt;= &gt;= == !=</c>).</summary>
public sealed class BinaryExpression(string op, WdlExpression left, WdlExpression right) : WdlExpression
{
    public string Operator => op;
    public WdlExpression Left => left;
    public WdlExpression Right => right;

    public override double Evaluate(IWdlContext context)
    {
        double l = left.Evaluate(context);
        double r = right.Evaluate(context);
        return op switch
        {
            "+" => l + r,
            "-" => l - r,
            "*" => l * r,
            "/" => r != 0 ? l / r : 0, // Acknex-safe: divide-by-zero yields 0
            "<" => l < r ? 1 : 0,
            ">" => l > r ? 1 : 0,
            "<=" => l <= r ? 1 : 0,
            ">=" => l >= r ? 1 : 0,
            "==" => l == r ? 1 : 0,
            "!=" => l != r ? 1 : 0,
            _ => 0,
        };
    }
}
