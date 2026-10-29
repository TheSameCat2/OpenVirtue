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
    [InlineData("INT(-2.9)", -3)] // INT rounds down (OV-20261029-numeric-edge)
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
        // Arguments and results are fixed point: within a 1/1024 step (OV-20261029-numeric-edge).
        Assert.Equal(expected, Eval(expression, new TestContext()), 1.0 / 1024);
    }

    [Theory]
    [InlineData("1 / 3 * 1024", 341)]
    [InlineData("0.001 * 1000000", 976.5625)]
    [InlineData("0.0001 * 1000000", 0)]
    [InlineData("2097151 + 2", -2097151)]
    [InlineData("1000000 * 1000", 1755648)]
    [InlineData("0 - 2097153", -2097151.9990234375)]
    [InlineData("INT(0 - 2.5)", -3)]
    [InlineData("(0 - 7) % 3", 0)]
    [InlineData("7 % (0 - 3)", 1)]
    [InlineData("7.9 % 3", 1.8994140625)]
    [InlineData("5.7 & 3", 1)]
    [InlineData("(0 - 1) & 255", 255)]
    [InlineData("4 | 2.9", 6.8994140625)]
    [InlineData("TAN(1.5707)", 2066.8544921875)]
    [InlineData("SIN(0.5) * 1000000", 478515.625)]
    [InlineData("EXP(20)", WdlFixed.Invalid)]
    [InlineData("0.1 * 3", 0.298828125)]
    public void Evaluates_TheOriginalsFixedPointArithmetic(string expression, double expected)
    {
        // The readings of OV-20261029-numeric-edge (case `values`).
        Assert.Equal(expected, Eval(expression, new TestContext()), 9);
    }

    [Fact]
    public void Evaluates_DocumentedRandomFunction()
    {
        var context = new TestContext { NextRandomUnit = 0.25 };

        Assert.Equal(2.5, Eval("RANDOM(10)", context));
        Assert.Equal(3, Eval("RANDOM(8) + 1", context));
    }

    [Theory]
    [InlineData("10 / 0")]
    [InlineData("10 % 0")]
    public void DivisionByZero_Throws(string expression)
    {
        // The DOS runtime crashes (OV-20261029-numeric-edge); OpenVirtue stops the action.
        Assert.Throws<WdlExpressionException>(() => Eval(expression, new TestContext()));
    }

    [Theory]
    [InlineData("SQRT(-1)", WdlFixed.Invalid)]
    [InlineData("LOG(0)", WdlFixed.Invalid)]
    [InlineData("LOG10(-1)", WdlFixed.Invalid)]
    [InlineData("LOG2(0)", WdlFixed.Invalid)]
    [InlineData("ASIN(2)", 1.5703125)]
    [InlineData("ACOS(-2)", 3.140625)]
    public void UndefinedFunctionResults_ReadAsTheOriginalsValues(string expression, double expected)
    {
        // OV-20261029-numeric-edge: SQRT(-1) and LOG(0) read -2097151.999 and the action goes on;
        // ASIN(2) reads pi/2 (the argument is clamped).
        Assert.Equal(expected, Eval(expression, new TestContext()), 9);
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
    public void Evaluates_DocumentedKeywordComparisons()
    {
        var context = new TestContext();
        context.Objects["room"] = new Region("room") { FloorTexture = "water_tex" };
        context.Objects["plant"] = new Thing("plant");
        context.SetKeywordReference("selected_tex", "water_tex");

        Assert.Equal(1, Eval("room.FLOOR_TEX == water_tex", context));
        Assert.Equal(1, Eval("room.FLOOR_TEX == selected_tex", context));
        Assert.Equal(0, Eval("room.FLOOR_TEX == lava_tex", context));
        Assert.Equal(1, Eval("room.FLOOR_TEX != NULL", context));
        Assert.Equal(1, Eval("plant.TEXTURE == NULL", context));
        Assert.Equal(0, Eval("plant.TEXTURE == water_tex", context));
    }

    [Fact]
    public void Evaluates_SkillEqualityNumerically()
    {
        var context = new TestContext();
        context.SetSkill("x", 3);
        context.SetSkill("y", 3);

        Assert.Equal(1, Eval("x == y", context));
    }

    [Fact]
    public void UnknownSkill_EvaluatesToZero()
    {
        Assert.Equal(1, Eval("missing + 1", new TestContext()));
    }

    private static double Eval(string expression, IWdlContext context) =>
        WdlExpressionParser.Parse(expression).Evaluate(context);

    private sealed class TestContext : IWdlContext, IWdlKeywordReferenceContext, IWdlRandomContext
    {
        private readonly Dictionary<string, double> _skills = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _keywordReferences = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, AcknexObject> Objects { get; } = new(StringComparer.OrdinalIgnoreCase);

        public double NextRandomUnit { get; set; } = 0.5;

        public double GetSkill(string name) => _skills.GetValueOrDefault(name);

        public void SetSkill(string name, double value) => _skills[name] = value;

        public AcknexObject? GetObject(string name) => Objects.GetValueOrDefault(name);

        public bool IsKeywordReference(string name) => _keywordReferences.ContainsKey(name);

        public bool TryGetKeywordReference(string name, out string? value) =>
            _keywordReferences.TryGetValue(name, out value);

        public void SetKeywordReference(string name, string? value) => _keywordReferences[name] = value;

        public double Random(double maximum) => NextRandomUnit * maximum;

        public void Randomize()
        {
            // Not exercised by expression tests.
        }

        public void CallAction(string name)
        {
            // Not exercised by expression tests.
        }
    }
}
