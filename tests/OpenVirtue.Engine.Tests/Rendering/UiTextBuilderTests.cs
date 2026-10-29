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

        // CENTER_Y centres the first line on POS_Y; CENTER_X centres each line (OV-20261029-ui-text).
        Assert.Equal(42, glyphs[0].X);
        Assert.Equal(35, glyphs[0].Y);
        Assert.Equal(50, glyphs[1].X);
        Assert.Equal(46, glyphs[2].X);
        Assert.Equal(45, glyphs[2].Y);
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

        // The advance shrinks; each character is still drawn whole, over the one before
        // (OV-20261029-ui-layout).
        Assert.Equal(8, glyphs[0].DrawWidth);
        Assert.Equal(7, glyphs[1].X);
        Assert.Equal(8, glyphs[2].DrawWidth);
        Assert.Equal(6, glyphs[3].X);
    }

    [Theory]
    [InlineData(7, "    7")]
    [InlineData(1234, " 1234")]
    [InlineData(-56, "  -56")]
    [InlineData(3.7, "    4")]
    [InlineData(123456, "23456")]
    [InlineData(0, "    0")]
    public void Build_PanelDigits_ShowTheMeasuredLayout(double value, string shown)
    {
        // OV-20261029-ui-layout: rounded, right-aligned in len places, the last len digits kept.
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            SKILL v { VAL 0; }
            FONT f, <f.pcx>, 8, 8;
            PANEL p { POS_X 100; POS_Y 60; DIGITS 4,14,5,f,1,v; FLAGS VISIBLE; }
            """);
        var skills = new Dictionary<string, double>(level.Skills, StringComparer.OrdinalIgnoreCase) { ["v"] = value };

        IReadOnlyList<UiTextGlyph> glyphs = UiTextBuilder.Build(level, skills: skills);

        Assert.Equal(shown, new string([.. glyphs.Select(g => (char)g.CodePoint)]));
        Assert.Equal([104f, 112f, 120f, 128f, 136f], glyphs.Select(g => g.X));
    }

    [Fact]
    public void Build_SizeYAndOffsetY_ShowOnlyWholeLines()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            STRING prompt,"A\nB\nC";
            FONT menuFont, <menu.pcx>, 8, 10;
            TEXT promptText { FONT menuFont; STRING prompt; POS_X 3; POS_Y 20; SIZE_Y 15; OFFSET_Y 5; FLAGS VISIBLE; }
            """);

        IReadOnlyList<UiTextGlyph> glyphs = UiTextBuilder.Build(level);

        // Only whole lines between POS_Y and POS_Y + SIZE_Y are drawn: A (15…25) sticks out above,
        // C (35…45) below (OV-20261029-ui-text).
        UiTextGlyph b = Assert.Single(glyphs);
        Assert.Equal('B', (char)b.CodePoint);
        Assert.Equal(25, b.Y);
        Assert.Equal(0, b.SourceY);
        Assert.Equal(10, b.DrawHeight);
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
