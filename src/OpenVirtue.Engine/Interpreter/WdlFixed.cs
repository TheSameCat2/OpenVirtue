// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Interpreter;

/// <summary>
/// The original's number format, measured in OV-20261029-numeric-edge: a skill is a signed 32-bit
/// fixed-point value with 10 fractional bits (the compiler calls the type FIXED), so it holds steps
/// of 1/1024 between −2097152 and 2097151.999. Script arithmetic works on the raw values:
/// <list type="bullet">
/// <item>a literal is truncated toward zero and saturates at the limits;</item>
/// <item><c>+</c>, <c>−</c> and <c>*</c> wrap around in 32 bits (the product keeps its full width
/// first and drops the low 10 bits);</item>
/// <item><c>/</c> truncates toward zero;</item>
/// <item><c>%</c> takes the dividend's raw bits as unsigned and the divisor's magnitude;</item>
/// <item><c>&amp;</c>, <c>|</c> and <c>^</c> act on the raw bits, fraction included;</item>
/// <item><c>INT</c> rounds down;</item>
/// <item>a function computes from the fixed argument and truncates its result, and a result that
/// is undefined or out of range reads −2097151.999.</item>
/// </list>
/// Values are carried as <see cref="double"/>s that are exact multiples of 1/1024.
/// </summary>
public static class WdlFixed
{
    /// <summary>Raw units per step.</summary>
    public const double Scale = 1024;

    /// <summary>The largest value, 2097151.999.</summary>
    public const double MaxValue = int.MaxValue / Scale;

    /// <summary>What an undefined or overflowing function result reads: −2097151.999.</summary>
    public const double Invalid = -int.MaxValue / Scale;

    /// <summary>
    /// A value the engine computed (a position, a distance, <c>TIME_CORR</c>) as a script reads it:
    /// rounded to the nearest 1/1024, since the original computes it in fixed point to begin with and
    /// OpenVirtue's floating-point rounding errors (11.99999 for 12) must not cost a whole step,
    /// then wrapped to 32 bits. Script results are already exact steps and pass unchanged.
    /// </summary>
    public static double Quantize(double value) => FromRaw(ToRaw(Math.Round(value * Scale) / Scale));

    /// <summary>A numeric literal: truncated toward zero to 1/1024, saturating at the limits.</summary>
    public static double Literal(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        double raw = Math.Truncate(value * Scale);
        return raw >= int.MaxValue ? MaxValue : raw <= int.MinValue ? int.MinValue / Scale : raw / Scale;
    }

    public static double Add(double left, double right) => FromRaw((long)ToRaw(left) + ToRaw(right));

    public static double Subtract(double left, double right) => FromRaw((long)ToRaw(left) - ToRaw(right));

    public static double Negate(double value) => FromRaw(-(long)ToRaw(value));

    /// <summary>
    /// The full 64-bit raw product with the low 10 bits dropped toward zero: −0.5·0.3 is −153/1024,
    /// not −154 (OV-20261029-numeric-edge round 2).
    /// </summary>
    public static double Multiply(double left, double right) => FromRaw(((long)ToRaw(left) * ToRaw(right)) / 1024);

    /// <exception cref="WdlExpressionException">The divisor is 0 (the DOS runtime crashes).</exception>
    public static double Divide(double left, double right)
    {
        int divisor = ToRaw(right);
        return divisor == 0
            ? throw new WdlExpressionException("Division by zero.")
            : FromRaw(((long)ToRaw(left) << 10) / divisor);
    }

    /// <exception cref="WdlExpressionException">The divisor is 0 (the DOS runtime crashes).</exception>
    public static double Modulo(double left, double right)
    {
        long divisor = Math.Abs((long)ToRaw(right));
        return divisor == 0
            ? throw new WdlExpressionException("Modulo by zero.")
            : FromRaw((uint)ToRaw(left) % divisor);
    }

    public static double And(double left, double right) => FromRaw(ToRaw(left) & ToRaw(right));

    public static double Or(double left, double right) => FromRaw(ToRaw(left) | ToRaw(right));

    public static double Xor(double left, double right) => FromRaw(ToRaw(left) ^ ToRaw(right));

    /// <summary><c>INT</c>: the value rounded down to a whole number.</summary>
    public static double Floor(double value) => FromRaw((long)(ToRaw(value) >> 10) << 10);

    /// <summary>A function result: truncated toward zero to 1/1024, or <see cref="Invalid"/> if undefined or out of range.</summary>
    public static double Result(double value)
    {
        double raw = Math.Truncate(value * Scale);
        return double.IsNaN(raw) || raw >= int.MaxValue || raw <= int.MinValue ? Invalid : raw / Scale;
    }

    /// <summary>The raw 32-bit value: truncated toward zero, wrapped modulo 2^32 (NaN and infinities read as 0).</summary>
    public static int ToRaw(double value)
    {
        double raw = Math.Truncate(value * Scale);
        if (double.IsNaN(raw) || double.IsInfinity(raw))
        {
            return 0;
        }

        return Math.Abs(raw) < 9e18
            ? unchecked((int)(long)raw)
            : unchecked((int)(long)Math.IEEERemainder(raw, 4294967296.0));
    }

    private static double FromRaw(long raw) => unchecked((int)raw) / Scale;
}
