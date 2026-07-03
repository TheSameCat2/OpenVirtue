// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Map;

namespace OpenVirtue.Engine.Tests.Interpreter;

public class WdlExpressionTests
{
    [Theory]
    [InlineData("2 + 3 * 4", 14)]      // precedence
    [InlineData("(2 + 3) * 4", 20)]    // parentheses
    [InlineData("-5 + 3", -2)]         // unary minus
    [InlineData("10 / 4", 2.5)]
    [InlineData("2 < 3", 1)]
    [InlineData("3 < 2", 0)]
    [InlineData("5 == 5", 1)]
    [InlineData("4 >= 4", 1)]
    public void Evaluates_Arithmetic(string expression, double expected)
    {
        Assert.Equal(expected, Eval(expression, new TestContext()));
    }

    [Theory]
    [InlineData("1 && 1", 1)]
    [InlineData("1 && 0", 0)]
    [InlineData("0 || 0", 0)]
    [InlineData("1 || 0", 1)]
    [InlineData("10 % 3", 1)]
    [InlineData("6 & 3", 2)]             // bitwise AND
    [InlineData("6 ^ 3", 5)]             // bitwise XOR
    [InlineData("6 | 3", 7)]             // bitwise OR
    [InlineData("1 | 2 & 3", 3)]         // & binds tighter than |
    [InlineData("1 || 2 & 0", 1)]        // bitwise binds tighter than logical
    [InlineData("!0", 1)]
    [InlineData("!5", 0)]
    [InlineData("1 < 2 && 3 < 4", 1)]    // comparison binds tighter than &&
    [InlineData("5 > 3 || 2 > 8", 1)]
    [InlineData("1 || 0 && 0", 1)]       // && binds tighter than ||
    public void Evaluates_LogicalAndModulo(string expression, double expected)
    {
        Assert.Equal(expected, Eval(expression, new TestContext()));
    }

    [Theory]
    [InlineData("SQRT(9)", 3)]
    [InlineData("sqrt(16)", 4)]
    [InlineData("SIGN(-4)", -1)]
    [InlineData("SIGN(0)", 0)]
    [InlineData("SIGN(4)", 1)]
    [InlineData("ABS(-2.5)", 2.5)]
    [InlineData("INT(2.9)", 2)]
    [InlineData("INT(-2.9)", -2)]
    [InlineData("EXP(0)", 1)]
    [InlineData("SQRT(9) + ABS(-2)", 5)]
    public void Evaluates_DocumentedMathFunctions(string expression, double expected)
    {
        Assert.Equal(expected, Eval(expression, new TestContext()));
    }

    [Theory]
    [InlineData("LOG(1)", 0)]
    [InlineData("LOG10(100)", 2)]
    [InlineData("LOG2(8)", 3)]
    public void Evaluates_DocumentedLogFunctions(string expression, double expected)
    {
        Assert.Equal(expected, Eval(expression, new TestContext()), precision: 12);
    }

    [Theory]
    [InlineData("SIN(0)", 0)]
    [InlineData("SIN(1.5707963267948966)", 1)]
    [InlineData("COS(0)", 1)]
    [InlineData("COS(3.141592653589793)", -1)]
    [InlineData("TAN(0)", 0)]
    [InlineData("ASIN(1)", Math.PI / 2)]
    [InlineData("ACOS(0)", Math.PI / 2)]
    public void Evaluates_DocumentedTrigonometricFunctionsInRadians(string expression, double expected)
    {
        Assert.Equal(expected, Eval(expression, new TestContext()), precision: 12);
    }

    [Theory]
    [InlineData("10 / 0")]
    [InlineData("10 % 0")]
    [InlineData("SQRT(-1)")]
    [InlineData("LOG(0)")]
    [InlineData("LOG10(-1)")]
    [InlineData("LOG2(0)")]
    [InlineData("ASIN(2)")]
    [InlineData("ACOS(-2)")]
    public void InvalidArithmeticOperations_Throw(string expression)
    {
        Assert.Throws<WdlExpressionException>(() => Eval(expression, new TestContext()));
    }

    [Fact]
    public void Evaluates_SkillNames_WithPrecedence()
    {
        var context = new TestContext();
        context.SetSkill("gravity", 2);
        context.SetSkill("float_str", 3);
        context.SetSkill("forceJump", 4);

        // 2 + (3 * 4)
        Assert.Equal(14, Eval("gravity + float_str * forceJump", context));
    }

    [Fact]
    public void Evaluates_MemberAccess()
    {
        var context = new TestContext();
        context.Objects["my"] = new Thing("plant") { X = 7 };

        Assert.Equal(7, Eval("my.x", context));
        Assert.Equal(8, Eval("my.x + 1", context));
    }

    [Fact]
    public void UnknownSkill_EvaluatesToZero()
    {
        Assert.Equal(1, Eval("missing + 1", new TestContext()));
    }

    private static double Eval(string expression, IWdlContext context) =>
        WdlExpressionParser.Parse(expression).Evaluate(context);

    private sealed class TestContext : IWdlContext
    {
        private readonly Dictionary<string, double> _skills = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, AcknexObject> Objects { get; } = new(StringComparer.OrdinalIgnoreCase);

        public double GetSkill(string name) => _skills.GetValueOrDefault(name);

        public void SetSkill(string name, double value) => _skills[name] = value;

        public AcknexObject? GetObject(string name) => Objects.GetValueOrDefault(name);

        public void CallAction(string name)
        {
            // Not exercised by expression tests.
        }
    }
}
