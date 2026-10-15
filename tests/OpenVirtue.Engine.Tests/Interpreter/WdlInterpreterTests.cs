// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Diagnostics.CodeAnalysis;
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
    public void Accel_AcceleratesSkillUsingTimeCorrectionFrictionAndInertia()
    {
        TestContext context = Run(
            "SET TIME_CORR, 0.5; SET FRICTION, 0.25; SET INERTIA, 2; " +
            "SET PLAYER_VX, 10; ACCEL PLAYER_VX, 4;");

        Assert.Equal(9.75, context.GetSkill("PLAYER_VX"), 9);
    }

    [Fact]
    public void Add_IncrementsSkillAndObjectMember()
    {
        var context = new TestContext();
        var thing = new Thing("crate") { X = 4 };
        context.Objects["my"] = thing;

        new WdlInterpreter(context).Execute(WdlParser.Parse("SET score, 10; ADD score, 3 + 2; ADD my.x, 6;"));

        Assert.Equal(15, context.GetSkill("score"));
        Assert.Equal(10, thing.X);
    }

    [Fact]
    public void Addt_IncrementsByTimeCorrectedAmount()
    {
        TestContext context = Run("SET TIME_CORR, 0.25; SET speed, 2; ADDT speed, 8;");

        Assert.Equal(4, context.GetSkill("speed"));
    }

    [Fact]
    public void Randomize_ReseedsRandomContext()
    {
        var context = new TestContext();

        new WdlInterpreter(context).Execute(WdlParser.Parse("RANDOMIZE;"));

        Assert.Equal(1, context.RandomizeCount);
    }

    [Fact]
    public void Skip_AdvancesPastFollowingStatements()
    {
        TestContext context = Run("SET x, 0; SKIP 2; SET x, 1; SET x, 2; SET x, 3;");

        Assert.Equal(3, context.GetSkill("x"));
    }

    [Fact]
    public void Rule_DivisionByZero_AbortsExecution()
    {
        var context = new TestContext();
        var interpreter = new WdlInterpreter(context);

        Assert.Throws<WdlExpressionException>(() =>
            interpreter.Execute(WdlParser.Parse("SET x, 1; RULE x = 10 / 0; SET y, 1;")));

        Assert.Equal(1, context.GetSkill("x"));
        Assert.Equal(0, context.GetSkill("y"));
    }

    [Fact]
    public void Rule_CompoundDivisionByZero_AbortsExecution()
    {
        var context = new TestContext();
        var interpreter = new WdlInterpreter(context);

        Assert.Throws<WdlExpressionException>(() =>
            interpreter.Execute(WdlParser.Parse("SET x, 10; RULE x /= 0; SET y, 1;")));

        Assert.Equal(10, context.GetSkill("x"));
        Assert.Equal(0, context.GetSkill("y"));
    }

    [Fact]
    public void Call_InvokesNamedAction()
    {
        var context = new TestContext();

        new WdlInterpreter(context).Execute(WdlParser.Parse("CALL openDoor;"));

        Assert.Equal("openDoor", Assert.Single(context.Calls));
    }

    [Fact]
    public void Branch_WithoutActionResolver_InvokesTargetAndEndsAction()
    {
        var context = new TestContext();

        new WdlInterpreter(context).Execute(WdlParser.Parse("BRANCH openDoor; SET x, 1;"));

        Assert.Equal("openDoor", Assert.Single(context.Calls));
        Assert.Equal(0, context.GetSkill("x"));
    }

    // OV-20261015-branch-tail-jump: BRANCH runs the target in place of the current action.
    [Fact]
    public void Branch_WithActionResolver_ContinuesWithTargetBodyOnly()
    {
        var context = new ResolvingContext();
        context.AddAction("inner", "SET y, 7;");

        new WdlInterpreter(context).Execute(WdlParser.Parse("SET x, 1; BRANCH inner; SET x, 2;"));

        Assert.Equal(1, context.GetSkill("x"));
        Assert.Equal(7, context.GetSkill("y"));
    }

    [Fact]
    public void Branch_UnresolvedTarget_IsSkipped()
    {
        var context = new ResolvingContext();

        new WdlInterpreter(context).Execute(WdlParser.Parse("BRANCH missing; SET x, 2;"));

        Assert.Equal(2, context.GetSkill("x"));
    }

    [Fact]
    public void Branch_InsideWhile_LeavesLoopAndAction()
    {
        var context = new ResolvingContext();
        context.AddAction("inner", "RULE hits += 1;");

        new WdlInterpreter(context).Execute(WdlParser.Parse(
            "WHILE (n < 3) { RULE n += 1; BRANCH inner; RULE after += 1; } SET done, 1;"));

        Assert.Equal(1, context.GetSkill("n"));
        Assert.Equal(1, context.GetSkill("hits"));
        Assert.Equal(0, context.GetSkill("after"));
        Assert.Equal(0, context.GetSkill("done"));
    }

    [Fact]
    public void Branch_ChainedThroughTargets_RunsEachBodyInTurn()
    {
        var context = new ResolvingContext();
        context.AddAction("second", "RULE trace = trace * 10 + 2; BRANCH third; RULE trace = trace * 10 + 9;");
        context.AddAction("third", "RULE trace = trace * 10 + 3;");

        new WdlInterpreter(context).Execute(WdlParser.Parse(
            "RULE trace = 1; BRANCH second; RULE trace = trace * 10 + 9;"));

        Assert.Equal(123, context.GetSkill("trace"));
    }

    [Fact]
    public void End_TerminatesRemainingStatements()
    {
        TestContext context = Run("SET x, 1; END; SET x, 2;");

        Assert.Equal(1, context.GetSkill("x"));
    }

    [Fact]
    public void End_InsideNestedBlock_TerminatesOuterAction()
    {
        TestContext context = Run("SET x, 1; IF (x == 1) { END; } SET x, 2;");

        Assert.Equal(1, context.GetSkill("x"));
    }

    [Fact]
    public void Goto_JumpsForwardToLabel()
    {
        TestContext context = Run("SET x, 1; GOTO alarm; SET x, 2; alarm: SET y, 3;");

        Assert.Equal(1, context.GetSkill("x"));
        Assert.Equal(3, context.GetSkill("y"));
    }

    [Fact]
    public void Goto_FromNestedBlock_JumpsToOuterLabel()
    {
        TestContext context = Run("SET x, 1; IF (x == 1) { GOTO alarm; } SET x, 2; alarm: SET y, 3;");

        Assert.Equal(1, context.GetSkill("x"));
        Assert.Equal(3, context.GetSkill("y"));
    }

    [Fact]
    public void Goto_BackwardLabel_CanRepeatUntilConditionChanges()
    {
        TestContext context = Run("SET x, 0; loop: RULE x += 1; IF (x < 3) { GOTO loop; } SET y, x;");

        Assert.Equal(3, context.GetSkill("x"));
        Assert.Equal(3, context.GetSkill("y"));
    }

    [Fact]
    public void While_RepeatsBodyUntilConditionIsFalse()
    {
        TestContext context = Run("SET x, 0; WHILE (x < 3) { RULE x += 1; } SET y, x;");

        Assert.Equal(3, context.GetSkill("x"));
        Assert.Equal(3, context.GetSkill("y"));
    }

    [Fact]
    public void While_Break_ExitsLoop()
    {
        TestContext context = Run("SET x, 0; WHILE (x < 10) { RULE x += 1; IF (x == 3) { BREAK; } } SET y, x;");

        Assert.Equal(3, context.GetSkill("x"));
        Assert.Equal(3, context.GetSkill("y"));
    }

    [Fact]
    public void While_Continue_StartsNextIteration()
    {
        TestContext context = Run(
            "SET x, 0; SET y, 0; " +
            "WHILE (x < 4) { RULE x += 1; IF (x == 2) { CONTINUE; } RULE y += x; }");

        Assert.Equal(4, context.GetSkill("x"));
        Assert.Equal(8, context.GetSkill("y"));
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
    public void If_ExecutesElseWhenFalse()
    {
        TestContext context = Run("SET x, 1; IF (x > 3) { SET y, 1; } ELSE { SET y, 2; }");

        Assert.Equal(2, context.GetSkill("y"));
    }

    [Fact]
    public void If_SkipsElseWhenTrue()
    {
        TestContext context = Run("SET x, 5; IF (x > 3) { SET y, 1; } ELSE { SET y, 2; }");

        Assert.Equal(1, context.GetSkill("y"));
    }

    [Fact]
    public void LegacyIfEqual_ExecutesThenBranchWhenEqual()
    {
        TestContext context = Run("SET x, 5; IF_EQUAL x, 5; SET y, 1; ELSE; SET y, 2; ENDIF;");

        Assert.Equal(1, context.GetSkill("y"));
    }

    [Fact]
    public void LegacyIfEqual_ExecutesElseBranchWhenNotEqual()
    {
        TestContext context = Run("SET x, 5; IF_EQUAL x, 4; SET y, 1; ELSE; SET y, 2; ENDIF;");

        Assert.Equal(2, context.GetSkill("y"));
    }

    [Fact]
    public void LegacyIf_SkipsThenBranchWhenNoElseExists()
    {
        TestContext context = Run("SET x, 5; IF_ABOVE x, 10; SET y, 1; ENDIF; SET z, 2;");

        Assert.Equal(0, context.GetSkill("y"));
        Assert.Equal(2, context.GetSkill("z"));
    }

    [Fact]
    public void LegacyIf_WithoutEndifConditionsOnlyNextInstruction()
    {
        TestContext context = Run(
            "SET x, 0; IF_EQUAL x, 1; SET skipped, 1; SET reached, 2; " +
            "IF_EQUAL x, 0; SET matched, 3;");

        Assert.Equal(0, context.GetSkill("skipped"));
        Assert.Equal(2, context.GetSkill("reached"));
        Assert.Equal(3, context.GetSkill("matched"));
    }

    [Fact]
    public void Continue_LegacyIfWithoutEndifConditionsOnlyNextInstruction()
    {
        var context = new TestContext();
        OpenVirtue.Formats.Wdl.WdlDocument document = WdlParser.Parse(
            "SET x, 0; IF_EQUAL x, 1; SET skipped, 1; SET reached, 2;");
        var block = new OpenVirtue.Formats.Wdl.WdlBlock(document.Items);
        var interpreter = new WdlInterpreter(context);

        WdlExecutionResult result = interpreter.Continue(WdlInterpreter.Start(block));

        Assert.Equal(WdlExecutionStatus.Completed, result.Status);
        Assert.Equal(0, context.GetSkill("skipped"));
        Assert.Equal(2, context.GetSkill("reached"));
    }

    [Fact]
    public void LegacyIf_NestsWithoutMatchingInnerElseOrEndif()
    {
        TestContext context = Run(
            "SET x, 1; SET y, 2; " +
            "IF_EQUAL x, 0; " +
            "IF_EQUAL y, 2; SET result, 1; ELSE; SET result, 2; ENDIF; " +
            "ELSE; SET result, 3; ENDIF;");

        Assert.Equal(3, context.GetSkill("result"));
    }

    [Fact]
    public void LegacyIfComparisonVariants_EvaluateNumericComparisons()
    {
        TestContext context = Run(
            "SET x, 5; " +
            "IF_NEQUAL x, 4; SET nequal, 1; ENDIF; " +
            "IF_BELOW x, 6; SET below, 1; ENDIF; " +
            "IF_ABOVE x, 4; SET above, 1; ENDIF;");

        Assert.Equal(1, context.GetSkill("nequal"));
        Assert.Equal(1, context.GetSkill("below"));
        Assert.Equal(1, context.GetSkill("above"));
    }

    [Fact]
    public void LegacyIfMin_UsesDeclaredMinimumPredicate()
    {
        var context = new TestContext();
        context.MinimumSkills.Add("health");

        new WdlInterpreter(context).Execute(WdlParser.Parse("IF_MIN health; SET result, 1; ELSE; SET result, 2; ENDIF;"));

        Assert.Equal(1, context.GetSkill("result"));
    }

    [Fact]
    public void LegacyIfMin_ExecutesElseWhenNotAtMinimum()
    {
        TestContext context = Run("IF_MIN health; SET result, 1; ELSE; SET result, 2; ENDIF;");

        Assert.Equal(2, context.GetSkill("result"));
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

    [Fact]
    public void Execute_SkipsUnparseableStatement_AndContinues()
    {
        // A string-valued assignment isn't modelled by the numeric evaluator; it must be skipped
        // without aborting the rest of the block.
        TestContext context = Run("SET a, 1; SET msg, \"text\"; SET b, 2;");

        Assert.Equal(1, context.GetSkill("a"));
        Assert.Equal(2, context.GetSkill("b")); // reached despite the unparseable middle statement
    }

    private static TestContext Run(string statements)
    {
        var context = new TestContext();
        new WdlInterpreter(context).Execute(WdlParser.Parse(statements));
        return context;
    }

    private sealed class ResolvingContext : IWdlContext, IWdlActionResolver
    {
        private readonly Dictionary<string, double> _skills = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, WdlBlock> _actions = new(StringComparer.OrdinalIgnoreCase);

        public void AddAction(string name, string body) => _actions[name] = new WdlBlock(WdlParser.Parse(body).Items);

        public double GetSkill(string name) => _skills.GetValueOrDefault(name);

        public void SetSkill(string name, double value) => _skills[name] = value;

        public AcknexObject? GetObject(string name) => null;

        public void CallAction(string name)
        {
            if (_actions.TryGetValue(name, out WdlBlock? body))
            {
                new WdlInterpreter(this).Execute(body);
            }
        }

        public bool TryGetAction(string name, [NotNullWhen(true)] out WdlBlock? body) => _actions.TryGetValue(name, out body);
    }

    private sealed class TestContext : IWdlContext, IWdlRandomContext, IWdlSkillBoundsContext
    {
        private readonly Dictionary<string, double> _skills = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, AcknexObject> Objects { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Calls { get; } = [];

        public HashSet<string> MinimumSkills { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int RandomizeCount { get; private set; }

        public double GetSkill(string name) => _skills.GetValueOrDefault(name);

        public void SetSkill(string name, double value) => _skills[name] = value;

        public AcknexObject? GetObject(string name) => Objects.GetValueOrDefault(name);

        public void CallAction(string name) => Calls.Add(name);

        public double Random(double maximum) => 0.5 * maximum;

        public void Randomize() => RandomizeCount++;

        public bool IsSkillAtMinimum(string name) => MinimumSkills.Contains(name);
    }
}
