// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Map;
using OpenVirtue.Formats.Wdl;

namespace OpenVirtue.Engine.Tests.Interpreter;

public class WdlInterpreterTests
{
    [Fact]
    public void Set_AssignsSkillFromExpression()
    {
        TestContext context = Run("SET health, 20 + 30;");

        Assert.Equal(50, context.GetSkill("health"));
    }

    [Fact]
    public void Rule_AssignsAndCompounds()
    {
        TestContext context = Run("RULE x = 10; RULE x += 5; RULE x *= 2;");

        Assert.Equal(30, context.GetSkill("x"));
    }

    [Fact]
    public void Rule_EvaluatesExpressionOverSkills()
    {
        TestContext context = Run("SET g, 2; SET fs, 3; SET fj, 4; RULE force = g + fs * fj;");

        Assert.Equal(14, context.GetSkill("force"));
    }

    [Fact]
    public void If_ExecutesBodyWhenTrue()
    {
        TestContext context = Run("SET x, 5; IF (x > 3) { SET y, 1; }");

        Assert.Equal(1, context.GetSkill("y"));
    }

    [Fact]
    public void If_SkipsBodyWhenFalse()
    {
        TestContext context = Run("SET x, 1; IF (x > 3) { SET y, 1; }");

        Assert.Equal(0, context.GetSkill("y")); // body never ran
    }

    [Fact]
    public void Set_WritesObjectMemberTarget()
    {
        var context = new TestContext();
        var thing = new Thing("plant");
        context.Objects["my"] = thing;

        new WdlInterpreter(context).Execute(WdlParser.Parse("SET my.x, 9;"));

        Assert.Equal(9, thing.X);
    }

    private static TestContext Run(string statements)
    {
        var context = new TestContext();
        new WdlInterpreter(context).Execute(WdlParser.Parse(statements));
        return context;
    }

    private sealed class TestContext : IWdlContext
    {
        private readonly Dictionary<string, double> _skills = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, AcknexObject> Objects { get; } = new(StringComparer.OrdinalIgnoreCase);

        public double GetSkill(string name) => _skills.GetValueOrDefault(name);

        public void SetSkill(string name, double value) => _skills[name] = value;

        public AcknexObject? GetObject(string name) => Objects.GetValueOrDefault(name);
    }
}
