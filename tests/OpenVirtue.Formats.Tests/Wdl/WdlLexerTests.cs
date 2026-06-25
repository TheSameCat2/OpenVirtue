// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text;
using OpenVirtue.Formats.Tests.TestSupport;
using OpenVirtue.Formats.Wdl;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Formats.Tests.Wdl;

public class WdlLexerTests
{
    [Fact]
    public void Tokenize_RegionBlock_ProducesExpectedKinds()
    {
        WdlTokenKind[] kinds = Kinds("REGION border { FLOOR_HGT 40; }");

        Assert.Equal(
            new[]
            {
                WdlTokenKind.Identifier, // REGION
                WdlTokenKind.Identifier, // border
                WdlTokenKind.LBrace,
                WdlTokenKind.Identifier, // FLOOR_HGT
                WdlTokenKind.Number,     // 40
                WdlTokenKind.Semicolon,
                WdlTokenKind.RBrace,
                WdlTokenKind.EndOfFile,
            },
            kinds);
    }

    [Fact]
    public void Tokenize_FileReference_IsOneToken()
    {
        IReadOnlyList<WdlToken> tokens = WdlLexer.Tokenize("MAPFILE <start.WMP>;");

        Assert.Equal(WdlTokenKind.Identifier, tokens[0].Kind);
        Assert.Equal(WdlTokenKind.FileRef, tokens[1].Kind);
        Assert.Equal("start.WMP", tokens[1].Text);
        Assert.Equal(WdlTokenKind.Semicolon, tokens[2].Kind);
    }

    [Fact]
    public void Tokenize_String_StripsQuotes()
    {
        IReadOnlyList<WdlToken> tokens = WdlLexer.Tokenize("PATH \"texture\";");

        Assert.Equal(WdlTokenKind.String, tokens[1].Kind);
        Assert.Equal("texture", tokens[1].Text);
    }

    [Fact]
    public void Tokenize_SkipsLineComments()
    {
        WdlTokenKind[] kinds = Kinds("// a comment\n#  another\nSET x;");

        Assert.Equal(
            new[] { WdlTokenKind.Identifier, WdlTokenKind.Identifier, WdlTokenKind.Semicolon, WdlTokenKind.EndOfFile },
            kinds);
    }

    [Fact]
    public void Tokenize_LessThan_IsOperatorNotFileRef()
    {
        IReadOnlyList<WdlToken> tokens = WdlLexer.Tokenize("x < 5");

        Assert.Equal(WdlTokenKind.Identifier, tokens[0].Kind);
        Assert.Equal(WdlTokenKind.Operator, tokens[1].Kind);
        Assert.Equal("<", tokens[1].Text);
        Assert.Equal(WdlTokenKind.Number, tokens[2].Kind);
    }

    [Theory]
    [InlineData("force += 1", "+=")]
    [InlineData("a <= b", "<=")]
    [InlineData("a == b", "==")]
    public void Tokenize_CompoundOperators(string source, string expectedOperator)
    {
        IReadOnlyList<WdlToken> tokens = WdlLexer.Tokenize(source);

        Assert.Equal(WdlTokenKind.Operator, tokens[1].Kind);
        Assert.Equal(expectedOperator, tokens[1].Text);
    }

    [Fact]
    public void Tokenize_MultiLineString_IsOneToken()
    {
        // WDL sign/scroll text spans physical lines (see APATHY.WDL).
        IReadOnlyList<WdlToken> tokens = WdlLexer.Tokenize("STRING s, \"line one\nline two\";");

        WdlToken str = tokens.Single(t => t.Kind == WdlTokenKind.String);
        Assert.Equal("line one\nline two", str.Text);
    }

    [Fact]
    public void Tokenize_UnterminatedString_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WdlLexer.Tokenize("PATH \"oops"));
    }

    /// <summary>
    /// Tokenizes every WDL script in the real archives (when present locally). A clean
    /// pass over all of the game's scripts is strong evidence the lexer covers the
    /// dialect. No-op when game data is absent.
    /// </summary>
    [Fact]
    public void Tokenize_RealWdlFromArchives_AllReachEndOfFile()
    {
        IReadOnlyList<string> wrsFiles = ResearchData.WrsFiles();
        if (wrsFiles.Count == 0)
        {
            return; // no local game data — skip
        }

        int scripts = 0;
        foreach (string path in wrsFiles)
        {
            WrsArchive archive = WrsArchive.ReadFile(path);
            foreach (WrsEntry entry in archive.Entries)
            {
                if (!entry.Name.EndsWith(".wdl", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // WDL is DOS text; Latin1 maps every byte 1:1.
                string text = Encoding.Latin1.GetString(entry.GetData());
                IReadOnlyList<WdlToken> tokens = WdlLexer.Tokenize(text);

                Assert.NotEmpty(tokens);
                Assert.Equal(WdlTokenKind.EndOfFile, tokens[^1].Kind);
                scripts++;
            }
        }

        Assert.True(scripts > 0, "expected to tokenize at least one real WDL script");
    }

    private static WdlTokenKind[] Kinds(string source) =>
        WdlLexer.Tokenize(source).Select(t => t.Kind).ToArray();
}
