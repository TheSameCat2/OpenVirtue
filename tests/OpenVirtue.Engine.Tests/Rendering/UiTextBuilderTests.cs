// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

public sealed class UiTextBuilderTests
{
    [Fact]
    public void Build_VisibleTextWithFontAndString_ReturnsGlyphs()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            STRING prompt,"Hi";
            FONT menuFont, <menu.pcx>, 8, 10;
            TEXT promptText { FONT menuFont; STRING prompt; POS_X 12; POS_Y 20; FLAGS VISIBLE; }
            """);

        IReadOnlyList<UiTextGlyph> glyphs = UiTextBuilder.Build(level);

        Assert.Equal(2, glyphs.Count);
        Assert.Equal('H', glyphs[0].CodePoint);
        Assert.Equal("menuFont", glyphs[0].Font);
        Assert.Equal(12, glyphs[0].X);
        Assert.Equal(20, glyphs[0].Y);
        Assert.Equal(8, glyphs[0].Width);
        Assert.Equal(10, glyphs[0].Height);
        Assert.Equal('i', glyphs[1].CodePoint);
        Assert.Equal(20, glyphs[1].X);
    }

    [Fact]
    public void Build_TextWithoutVisibleFlag_IsSkipped()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            STRING prompt,"hidden";
            FONT menuFont, <menu.pcx>, 8, 8;
            TEXT promptText { FONT menuFont; STRING prompt; POS_X 1; POS_Y 2; }
            """);

        Assert.Empty(UiTextBuilder.Build(level));
    }

    [Fact]
    public void Build_UsesProvidedRuntimeStringValues()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            STRING prompt,"old";
            FONT menuFont, <menu.pcx>, 8, 8;
            TEXT promptText { FONT menuFont; STRING prompt; POS_X 0; POS_Y 0; FLAGS VISIBLE; }
            """);
        var runtimeStrings = new Dictionary<string, WdlString>(StringComparer.OrdinalIgnoreCase)
        {
            ["prompt"] = new("new"),
        };

        IReadOnlyList<UiTextGlyph> glyphs = UiTextBuilder.Build(level, runtimeStrings);

        Assert.Equal(['n', 'e', 'w'], glyphs.Select(glyph => (char)glyph.CodePoint).ToArray());
    }

    [Fact]
    public void Build_TextWithMultipleStringKeywords_SkipsCommaTokens()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            STRING first,"A";
            STRING second,"B";
            FONT menuFont, <menu.pcx>, 8, 8;
            TEXT promptText { FONT menuFont; STRING first,second; POS_X 0; POS_Y 0; FLAGS VISIBLE; }
            """);

        IReadOnlyList<UiTextGlyph> glyphs = UiTextBuilder.Build(level);

        Assert.Equal(['A', 'B'], glyphs.Select(glyph => (char)glyph.CodePoint).ToArray());
        Assert.Equal(0, glyphs[0].Y);
        Assert.Equal(8, glyphs[1].Y);
    }

    [Fact]
    public void Build_NewlinesAdvanceToNextFontLine()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            STRING prompt,"A\nB";
            FONT menuFont, <menu.pcx>, 6, 9;
            TEXT promptText { FONT menuFont; STRING prompt; POS_X 3; POS_Y 4; FLAGS VISIBLE; }
            """);

        IReadOnlyList<UiTextGlyph> glyphs = UiTextBuilder.Build(level);

        Assert.Equal(2, glyphs.Count);
        Assert.Equal(3, glyphs[1].X);
        Assert.Equal(13, glyphs[1].Y);
    }

    [Fact]
    public void Build_CenteredText_UsesPosAsCenter()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            STRING prompt,"AB\nC";
            FONT menuFont, <menu.pcx>, 8, 10;
            TEXT promptText { FONT menuFont; STRING prompt; POS_X 50; POS_Y 40; FLAGS VISIBLE,CENTER_X,CENTER_Y; }
            """);

        IReadOnlyList<UiTextGlyph> glyphs = UiTextBuilder.Build(level);

        Assert.Equal(42, glyphs[0].X);
        Assert.Equal(30, glyphs[0].Y);
        Assert.Equal(50, glyphs[1].X);
        Assert.Equal(46, glyphs[2].X);
        Assert.Equal(40, glyphs[2].Y);
    }

    [Fact]
    public void Build_CondensedAndNarrowText_CompressesHorizontalAdvance()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            STRING condensed,"AB";
            STRING narrow,"CD";
            FONT menuFont, <menu.pcx>, 8, 10;
            TEXT condensedText { FONT menuFont; STRING condensed; POS_X 0; POS_Y 0; FLAGS VISIBLE,CONDENSED; }
            TEXT narrowText { FONT menuFont; STRING narrow; POS_X 0; POS_Y 20; FLAGS VISIBLE,NARROW; }
            """);

        IReadOnlyList<UiTextGlyph> glyphs = UiTextBuilder.Build(level);

        Assert.Equal(7, glyphs[0].DrawWidth);
        Assert.Equal(7, glyphs[1].X);
        Assert.Equal(6, glyphs[2].DrawWidth);
        Assert.Equal(6, glyphs[3].X);
    }

    [Fact]
    public void Build_SizeYAndOffsetY_CropsScrolledTextVertically()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            STRING prompt,"A\nB\nC";
            FONT menuFont, <menu.pcx>, 8, 10;
            TEXT promptText { FONT menuFont; STRING prompt; POS_X 3; POS_Y 20; SIZE_Y 15; OFFSET_Y 5; FLAGS VISIBLE; }
            """);

        IReadOnlyList<UiTextGlyph> glyphs = UiTextBuilder.Build(level);

        Assert.Equal(['A', 'B'], glyphs.Select(glyph => (char)glyph.CodePoint).ToArray());
        Assert.Equal(20, glyphs[0].Y);
        Assert.Equal(5, glyphs[0].SourceY);
        Assert.Equal(5, glyphs[0].SourceHeight);
        Assert.Equal(5, glyphs[0].DrawHeight);
        Assert.Equal(25, glyphs[1].Y);
        Assert.Equal(0, glyphs[1].SourceY);
        Assert.Equal(10, glyphs[1].DrawHeight);
    }

    [Fact]
    public void Build_VisiblePanelDigits_ReturnsPanelRelativeGlyphs()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            SKILL health { VAL 7; }
            FONT digitFont, <digits.pcx>, 5, 7;
            PANEL hud { POS_X 10; POS_Y 20; DIGITS 2,3,3,digitFont,1,health; FLAGS VISIBLE; }
            """);

        IReadOnlyList<UiTextGlyph> glyphs = UiTextBuilder.Build(level);

        Assert.Equal([' ', ' ', '7'], glyphs.Select(glyph => (char)glyph.CodePoint).ToArray());
        Assert.Equal(12, glyphs[0].X);
        Assert.Equal(23, glyphs[0].Y);
        Assert.Equal(22, glyphs[2].X);
        Assert.Equal("digitFont", glyphs[0].Font);
    }

    [Fact]
    public void Build_PanelDigits_UsesProvidedRuntimeSkillValuesAndFactor()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            SKILL ammo { VAL 1; }
            FONT digitFont, <digits.pcx>, 5, 7;
            PANEL hud { DIGITS 0,0,4,digitFont,10,ammo; FLAGS VISIBLE; }
            """);
        var runtimeSkills = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["ammo"] = 12.9,
        };

        IReadOnlyList<UiTextGlyph> glyphs = UiTextBuilder.Build(level, skills: runtimeSkills);

        Assert.Equal([' ', '1', '2', '9'], glyphs.Select(glyph => (char)glyph.CodePoint).ToArray());
    }

    private static Level Load(string main)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["test.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\nPLAYER_START 0 0 0 0;#1",
        };

        return LevelLoader.LoadCore("test.wdl", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }
}
