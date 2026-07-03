// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Interpreter;

/// <summary>An invalid WDL arithmetic operation that aborts expression evaluation.</summary>
public sealed class WdlExpressionException(string message) : InvalidOperationException(message);

/// <summary>A WDL expression node that can be evaluated against an <see cref="IWdlContext"/>.</summary>
public abstract class WdlExpression
{
    /// <summary>Evaluates the expression. Comparisons yield 1.0 (true) or 0.0 (false).</summary>
    public abstract double Evaluate(IWdlContext context);

    /// <summary>Attempts to evaluate the expression as an object reference.</summary>
    public virtual bool TryEvaluateObject(IWdlContext context, out AcknexObject? value)
    {
        value = null;
        return false;
    }
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

    public override bool TryEvaluateObject(IWdlContext context, out AcknexObject? value)
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

/// <summary>A single-argument mathematical function such as <c>SQRT(x)</c> or <c>LOG(x)</c>.</summary>
public sealed class FunctionExpression(string name, WdlExpression argument) : WdlExpression
{
    public string Name => name;
    public WdlExpression Argument => argument;

    public override double Evaluate(IWdlContext context)
    {
        double value = argument.Evaluate(context);
        return name.ToUpperInvariant() switch
        {
            "SQRT" => value >= 0 ? Math.Sqrt(value) : throw InvalidFunctionDomain("SQRT", value),
            "SIGN" => Math.Sign(value),
            "ABS" => Math.Abs(value),
            "INT" => Math.Truncate(value),
            "EXP" => Math.Exp(value),
            "SIN" => Math.Sin(value),
            "COS" => Math.Cos(value),
            "TAN" => Math.Tan(value),
            "ASIN" => value is >= -1 and <= 1 ? Math.Asin(value) : throw InvalidFunctionDomain("ASIN", value),
            "ACOS" => value is >= -1 and <= 1 ? Math.Acos(value) : throw InvalidFunctionDomain("ACOS", value),
            "LOG" => value > 0 ? Math.Log(value) : throw InvalidFunctionDomain("LOG", value),
            "LOG10" => value > 0 ? Math.Log10(value) : throw InvalidFunctionDomain("LOG10", value),
            "LOG2" => value > 0 ? Math.Log2(value) : throw InvalidFunctionDomain("LOG2", value),
            _ => 0,
        };
    }

    private static WdlExpressionException InvalidFunctionDomain(string function, double value) =>
        new($"{function} is undefined for {value}.");
}

/// <summary>A binary operation: arithmetic (<c>+ - * / %</c>), comparison (<c>&lt; &gt; &lt;= &gt;= == !=</c>), bitwise (<c>&amp; ^ |</c>), or logical (<c>&amp;&amp; ||</c>).</summary>
public sealed class BinaryExpression(string op, WdlExpression left, WdlExpression right) : WdlExpression
{
    public string Operator => op;
    public WdlExpression Left => left;
    public WdlExpression Right => right;

    public override double Evaluate(IWdlContext context)
    {
        if (op is "==" or "!=" &&
            left.TryEvaluateObject(context, out AcknexObject? leftObject) &&
            right.TryEvaluateObject(context, out AcknexObject? rightObject))
        {
            bool equal = ReferenceEquals(leftObject, rightObject);
            return (op == "==" ? equal : !equal) ? 1 : 0;
        }

        double l = left.Evaluate(context);
        double r = right.Evaluate(context);
        return op switch
        {
            "+" => l + r,
            "-" => l - r,
            "*" => l * r,
            "/" => r != 0 ? l / r : throw new WdlExpressionException("Division by zero."),
            "<" => l < r ? 1 : 0,
            ">" => l > r ? 1 : 0,
            "<=" => l <= r ? 1 : 0,
            ">=" => l >= r ? 1 : 0,
            "==" => l == r ? 1 : 0,
            "!=" => l != r ? 1 : 0,
            "&&" => l != 0 && r != 0 ? 1 : 0,
            "||" => l != 0 || r != 0 ? 1 : 0,
            "%" => r != 0 ? l % r : throw new WdlExpressionException("Modulo by zero."),
            "&" => (long)l & (long)r,
            "^" => (long)l ^ (long)r,
            "|" => (long)l | (long)r,
            _ => 0,
        };
    }
}
