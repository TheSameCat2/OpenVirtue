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
    [InlineData("10 / 0", 0)]          // Acknex-safe divide-by-zero
    [InlineData("2 < 3", 1)]
    [InlineData("3 < 2", 0)]
    [InlineData("5 == 5", 1)]
    [InlineData("4 >= 4", 1)]
    public void Evaluates_Arithmetic(string expression, double expected)
    {
        Assert.Equal(expected, Eval(expression, new TestContext()));
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
