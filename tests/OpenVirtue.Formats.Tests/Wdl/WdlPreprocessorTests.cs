// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text;
using OpenVirtue.Formats.Tests.TestSupport;
using OpenVirtue.Formats.Wdl;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Formats.Tests.Wdl;

public class WdlPreprocessorTests
{
    [Fact]
    public void Flatten_InlinesIncludesInOrder()
    {
        const string main = "SKILL a { VAL 1; } INCLUDE <mod.wdl>; SKILL c { VAL 3; }";
        const string module = "SKILL b { VAL 2; }";

        WdlProgram program = WdlPreprocessor.Flatten(
            main,
            name => name.Equals("mod.wdl", StringComparison.OrdinalIgnoreCase) ? module : null);

        string[] skills = SkillNames(program);
        Assert.Equal(["a", "b", "c"], skills); // 'b' inlined between 'a' and 'c'
    }

    [Fact]
    public void Flatten_MainItems_ExcludeIncludedDeclarations()
    {
        const string main = "SKILL a { VAL 1; } INCLUDE <mod.wdl>; SKILL c { VAL 3; }";
        const string module = "SKILL b { VAL 2; }";

        WdlProgram program = WdlPreprocessor.Flatten(
            main,
            name => name.Equals("mod.wdl", StringComparison.OrdinalIgnoreCase) ? module : null);

        Assert.Equal(["a", "c"], SkillNames(program.MainItems));
    }

    [Fact]
    public void Flatten_RepeatedInclude_InlinesEachOccurrence()
    {
        const string main = "INCLUDE <mod.wdl>; INCLUDE <mod.wdl>;";
        const string module = "SKILL b { VAL 2; }";

        WdlProgram program = WdlPreprocessor.Flatten(main, _ => module);

        Assert.Equal(["b", "b"], SkillNames(program));
    }

    [Fact]
    public void Flatten_CyclicInclude_SkipsOnlyActiveRecursion()
    {
        const string main = "INCLUDE <a.wdl>; INCLUDE <a.wdl>;";
        var modules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["a.wdl"] = "SKILL a { VAL 1; } INCLUDE <b.wdl>;",
            ["b.wdl"] = "SKILL b { VAL 2; } INCLUDE <a.wdl>;",
        };

        WdlProgram program = WdlPreprocessor.Flatten(main, name => modules.GetValueOrDefault(name));

        Assert.Equal(["a", "b", "a", "b"], SkillNames(program));
    }

    [Fact]
    public void Flatten_NestedIfdef_FallsThroughToDefaultAndDefinesHires()
    {
        // Mirrors APATHY.WDL's resolution block: nothing predefined, so it defaults.
        const string source =
            """
            IFDEF HIRES;
              VIDEO hires;
            IFELSE;
            IFDEF LORES;
              VIDEO lores;
            IFELSE;
              DEFINE HIRES;
              VIDEO def;
            ENDIF;
            ENDIF;
            """;

        WdlProgram program = WdlPreprocessor.Flatten(source, _ => null);

        Assert.Equal(["def"], VideoArgs(program));
        Assert.Contains("HIRES", program.DefinedSymbols);
    }

    [Fact]
    public void Flatten_PredefinedSymbol_TakesIfBranch()
    {
        const string source = "IFDEF HIRES; VIDEO hires; IFELSE; VIDEO other; ENDIF;";

        WdlProgram program = WdlPreprocessor.Flatten(source, _ => null, ["HIRES"]);

        Assert.Equal(["hires"], VideoArgs(program));
    }

    [Fact]
    public void Flatten_Ifndef_TakesBranchWhenSymbolIsMissing()
    {
        const string source = "IFNDEF HIRES; VIDEO fallback; IFELSE; VIDEO hires; ENDIF;";

        WdlProgram program = WdlPreprocessor.Flatten(source, _ => null);

        Assert.Equal(["fallback"], VideoArgs(program));
    }

    [Fact]
    public void Flatten_Ifndef_TakesElseWhenSymbolIsDefined()
    {
        const string source = "IFNDEF HIRES; VIDEO fallback; IFELSE; VIDEO hires; ENDIF;";

        WdlProgram program = WdlPreprocessor.Flatten(source, _ => null, ["HIRES"]);

        Assert.Equal(["hires"], VideoArgs(program));
    }

    [Fact]
    public void Flatten_Undef_RemovesSymbolForFollowingConditionals()
    {
        const string source = "DEFINE HIRES; UNDEF HIRES; IFNDEF HIRES; VIDEO fallback; ENDIF;";

        WdlProgram program = WdlPreprocessor.Flatten(source, _ => null);

        Assert.Equal(["fallback"], VideoArgs(program));
        Assert.DoesNotContain("HIRES", program.DefinedSymbols);
    }

    [Fact]
    public void Flatten_DefineAlias_RewritesFollowingKeywordsAndParameters()
    {
        const string source = "DEFINE SpriteHeight,HEIGHT; THING crate { SpriteHeight 42; }";

        WdlProgram program = WdlPreprocessor.Flatten(source, _ => null);

        WdlItem thing = Assert.Single(
            program.Items,
            i => i.Keyword.Equals("THING", StringComparison.OrdinalIgnoreCase));
        WdlItem property = Assert.Single(thing.Body!.Items);
        Assert.Equal("HEIGHT", property.Keyword);
        Assert.Equal("42", Assert.Single(property.Header).Text);
    }

    [Fact]
    public void Flatten_DefineAlias_CanReplaceNumericConstants()
    {
        const string source = "DEFINE MyNumber,-3456; SKILL target { VAL MyNumber; }";

        WdlProgram program = WdlPreprocessor.Flatten(source, _ => null);

        WdlItem skill = Assert.Single(
            program.Items,
            i => i.Keyword.Equals("SKILL", StringComparison.OrdinalIgnoreCase));
        WdlToken[] valueTokens = Assert.Single(skill.Body!.Items).Header.ToArray();
        Assert.Collection(
            valueTokens,
            token =>
            {
                Assert.Equal(WdlTokenKind.Operator, token.Kind);
                Assert.Equal("-", token.Text);
            },
            token =>
            {
                Assert.Equal(WdlTokenKind.Number, token.Kind);
                Assert.Equal("3456", token.Text);
            });
    }

    [Fact]
    public void Flatten_DefineAlias_RewritesMemberNamesInsideActions()
    {
        const string source = "DEFINE HitEffect,SKILL3; ACTION kill { SET RESULT,MY.HitEffect; }";

        WdlProgram program = WdlPreprocessor.Flatten(source, _ => null);

        WdlItem action = Assert.Single(
            program.Items,
            i => i.Keyword.Equals("ACTION", StringComparison.OrdinalIgnoreCase));
        WdlItem set = Assert.Single(action.Body!.Items);
        Assert.Contains(set.Header, token => token.Text.Equals("SKILL3", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(set.Header, token => token.Text.Equals("HitEffect", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Flatten_Undef_RemovesFollowingAliasReplacement()
    {
        const string source = "DEFINE SpriteHeight,HEIGHT; UNDEF SpriteHeight; THING crate { SpriteHeight 42; }";

        WdlProgram program = WdlPreprocessor.Flatten(source, _ => null);

        WdlItem thing = Assert.Single(
            program.Items,
            i => i.Keyword.Equals("THING", StringComparison.OrdinalIgnoreCase));
        WdlItem property = Assert.Single(thing.Body!.Items);
        Assert.Equal("SpriteHeight", property.Keyword);
    }

    /// <summary>
    /// Flattens the real apathy level (when present): resolving its 21 INCLUDEs should pull in
    /// globals.wdl's ~233 skills, and the default resolution path should define HIRES. No-op otherwise.
    /// </summary>
    [Fact]
    public void Flatten_RealLevel_ResolvesIncludesAndConditionals()
    {
        string? apathy = ResearchData.WrsFiles()
            .FirstOrDefault(p => Path.GetFileName(p).Equals("apathy.wrs", StringComparison.OrdinalIgnoreCase));
        if (apathy is null)
        {
            return; // no local game data — skip
        }

        WrsArchive archive = WrsArchive.ReadFile(apathy);
        Dictionary<string, string> modules = archive.Entries
            .Where(e => e.Name.EndsWith(".wdl", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(e => e.Name, e => Encoding.Latin1.GetString(e.GetData()), StringComparer.OrdinalIgnoreCase);

        WdlProgram program = WdlPreprocessor.Flatten(
            modules["APATHY.WDL"],
            name => modules.TryGetValue(Path.GetFileName(name), out string? src) ? src : null);

        int skills = program.Items.Count(i => i.Keyword.Equals("SKILL", StringComparison.OrdinalIgnoreCase));
        Assert.True(skills > 200, $"expected >200 skills after INCLUDE resolution, got {skills}");
        Assert.Contains("HIRES", program.DefinedSymbols);
    }

    private static string[] SkillNames(WdlProgram program) => SkillNames(program.Items);

    private static string[] SkillNames(IReadOnlyList<WdlItem> items) => items
        .Where(i => i.Keyword.Equals("SKILL", StringComparison.OrdinalIgnoreCase))
        .Select(i => i.Header[0].Text)
        .ToArray();

    private static string[] VideoArgs(WdlProgram program) => program.Items
        .Where(i => i.Keyword.Equals("VIDEO", StringComparison.OrdinalIgnoreCase))
        .Select(i => i.Header[0].Text)
        .ToArray();
}
