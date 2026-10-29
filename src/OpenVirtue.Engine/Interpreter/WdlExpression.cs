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

    /// <summary>Attempts to evaluate the expression as a keyword-valued parameter.</summary>
    public virtual bool TryEvaluateKeyword(IWdlContext context, out string? value)
    {
        value = null;
        return false;
    }

    /// <summary>Attempts to evaluate the expression as a keyword value or literal.</summary>
    public virtual bool TryEvaluateKeywordLiteral(IWdlContext context, out string? value) =>
        TryEvaluateKeyword(context, out value);
}

/// <summary>A numeric literal, held as the original's fixed-point value (<see cref="WdlFixed.Literal"/>).</summary>
public sealed class NumberExpression(double value) : WdlExpression
{
    private readonly double _fixed = WdlFixed.Literal(value);

    public double Value => value;

    public override double Evaluate(IWdlContext context) => _fixed;
}

/// <summary>A bare name resolved as a skill/constant (e.g. <c>gravity</c>).</summary>
public sealed class NameExpression(string name) : WdlExpression
{
    public string Name => name;

    public override double Evaluate(IWdlContext context) => WdlFixed.Quantize(context.GetSkill(name));

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

    public override bool TryEvaluateKeyword(IWdlContext context, out string? value)
    {
        if (name.Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }

        if (context is IWdlKeywordReferenceContext keywords &&
            keywords.TryGetKeywordReference(name, out string? referencedKeyword))
        {
            value = referencedKeyword;
            return true;
        }

        value = null;
        return false;
    }

    public override bool TryEvaluateKeywordLiteral(IWdlContext context, out string? value)
    {
        if (TryEvaluateKeyword(context, out value))
        {
            return true;
        }

        value = name;
        return true;
    }
}

/// <summary>Member access on an object reference (e.g. <c>my.x</c>, <c>you._x</c>).</summary>
public sealed class MemberExpression(string objectName, string property) : WdlExpression
{
    public string ObjectName => objectName;
    public string Property => property;

    public override double Evaluate(IWdlContext context)
    {
        if (context.GetObject(objectName) is { } obj)
        {
            return WdlFixed.Quantize(context is IWdlObjectReadContext reads ? reads.GetObjectNumber(obj, property) : obj[property]);
        }

        // PANEL / TEXT / OVERLAY members (POS_Y, VISIBLE, ...) read the same way SET writes them.
        return context is IWdlUiContext ui && ui.TryGetUiNumber(objectName, property, out double value) ? WdlFixed.Quantize(value) : 0;
    }

    public override bool TryEvaluateKeyword(IWdlContext context, out string? value)
    {
        if (context.GetObject(objectName) is { } obj &&
            obj.TryGetString(property, out string? memberValue))
        {
            value = memberValue;
            return true;
        }

        value = null;
        return false;
    }
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
            "-" => WdlFixed.Negate(value),
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

    /// <remarks>
    /// The function works on the fixed-point argument and its result is truncated to 1/1024; an
    /// undefined or out-of-range result (<c>SQRT(−1)</c>, <c>LOG(0)</c>, <c>EXP(20)</c>) reads
    /// <see cref="WdlFixed.Invalid"/> and the action goes on, and <c>ASIN</c>/<c>ACOS</c> clamp their
    /// argument to −1…1 (OV-20261029-numeric-edge).
    /// </remarks>
    public override double Evaluate(IWdlContext context)
    {
        double value = argument.Evaluate(context);
        return name.ToUpperInvariant() switch
        {
            "SQRT" => WdlFixed.Result(Math.Sqrt(value)),
            "SIGN" => Math.Sign(value),
            "ABS" => value < 0 ? WdlFixed.Negate(value) : value,
            "INT" => WdlFixed.Floor(value),
            "EXP" => WdlFixed.Result(Math.Exp(value)),
            "SIN" => WdlFixed.Result(Math.Sin(value)),
            "COS" => WdlFixed.Result(Math.Cos(value)),
            "TAN" => WdlFixed.Result(Math.Tan(value)),
            "ASIN" => WdlFixed.Result(Math.Asin(Math.Clamp(value, -1, 1))),
            "ACOS" => WdlFixed.Result(Math.Acos(Math.Clamp(value, -1, 1))),
            "LOG" => WdlFixed.Result(value > 0 ? Math.Log(value) : double.NaN),
            "LOG10" => WdlFixed.Result(value > 0 ? Math.Log10(value) : double.NaN),
            "LOG2" => WdlFixed.Result(value > 0 ? Math.Log2(value) : double.NaN),
            "RANDOM" => WdlFixed.Quantize(context is IWdlRandomContext random ? random.Random(value) : Random.Shared.NextDouble() * value),
            _ => 0,
        };
    }
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

        if (op is "==" or "!=" && TryEvaluateKeywordComparison(context, out bool keywordEqual))
        {
            return (op == "==" ? keywordEqual : !keywordEqual) ? 1 : 0;
        }

        double l = left.Evaluate(context);
        double r = right.Evaluate(context);
        return op switch
        {
            "+" => WdlFixed.Add(l, r),
            "-" => WdlFixed.Subtract(l, r),
            "*" => WdlFixed.Multiply(l, r),
            "/" => WdlFixed.Divide(l, r),
            "<" => l < r ? 1 : 0,
            ">" => l > r ? 1 : 0,
            "<=" => l <= r ? 1 : 0,
            ">=" => l >= r ? 1 : 0,
            "==" => l == r ? 1 : 0,
            "!=" => l != r ? 1 : 0,
            "&&" => l != 0 && r != 0 ? 1 : 0,
            "||" => l != 0 || r != 0 ? 1 : 0,
            "%" => WdlFixed.Modulo(l, r),
            "&" => WdlFixed.And(l, r),
            "^" => WdlFixed.Xor(l, r),
            "|" => WdlFixed.Or(l, r),
            _ => 0,
        };
    }

    private bool TryEvaluateKeywordComparison(IWdlContext context, out bool equal)
    {
        bool leftKeyword = left.TryEvaluateKeyword(context, out string? leftValue);
        bool rightKeyword = right.TryEvaluateKeyword(context, out string? rightValue);

        if (leftKeyword && right.TryEvaluateKeywordLiteral(context, out string? rightLiteral))
        {
            equal = KeywordEquals(leftValue, rightLiteral);
            return true;
        }

        if (rightKeyword && left.TryEvaluateKeywordLiteral(context, out string? leftLiteral))
        {
            equal = KeywordEquals(leftLiteral, rightValue);
            return true;
        }

        if (leftKeyword || rightKeyword)
        {
            equal = false;
            return true;
        }

        equal = false;
        return false;
    }

    private static bool KeywordEquals(string? leftValue, string? rightValue) =>
        leftValue is null || rightValue is null
            ? leftValue is null && rightValue is null
            : leftValue.Equals(rightValue, StringComparison.OrdinalIgnoreCase);
}
