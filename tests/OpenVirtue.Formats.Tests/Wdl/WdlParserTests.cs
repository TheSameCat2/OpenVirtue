// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text;
using OpenVirtue.Formats.Tests.TestSupport;
using OpenVirtue.Formats.Wdl;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Formats.Tests.Wdl;

public class WdlParserTests
{
    [Fact]
    public void Parse_DeclarationWithBlock()
    {
        WdlDocument doc = WdlParser.Parse("SKILL AMBIENT { VAL 0.3; }");

        WdlItem skill = Assert.Single(doc.Items);
        Assert.Equal("SKILL", skill.Keyword);
        Assert.Equal("AMBIENT", Assert.Single(skill.Header).Text);
        Assert.True(skill.HasBody);

        WdlItem val = Assert.Single(skill.Body!.Items);
        Assert.Equal("VAL", val.Keyword);
        Assert.Equal("0.3", Assert.Single(val.Header).Text);
    }

    [Fact]
    public void Parse_DirectiveWithFileReference()
    {
        WdlDocument doc = WdlParser.Parse("MAPFILE <start.WMP>;");

        WdlItem item = Assert.Single(doc.Items);
        Assert.Equal("MAPFILE", item.Keyword);
        Assert.False(item.HasBody);
        WdlToken arg = Assert.Single(item.Header);
        Assert.Equal(WdlTokenKind.FileRef, arg.Kind);
        Assert.Equal("start.WMP", arg.Text);
    }

    [Fact]
    public void Parse_NestedBlocks()
    {
        WdlDocument doc = WdlParser.Parse("ACTION a { IF (x < 1) { SET y, 2; } }");

        WdlItem action = Assert.Single(doc.Items);
        Assert.Equal("ACTION", action.Keyword);

        WdlItem ifItem = Assert.Single(action.Body!.Items);
        Assert.Equal("IF", ifItem.Keyword);

        WdlItem set = Assert.Single(ifItem.Body!.Items);
        Assert.Equal("SET", set.Keyword);
    }

    [Fact]
    public void Parse_Label()
    {
        WdlDocument doc = WdlParser.Parse("ACTION a { doHideStuff: SET x, 1; }");

        IReadOnlyList<WdlItem> body = Assert.Single(doc.Items).Body!.Items;
        Assert.Equal(2, body.Count);
        Assert.True(body[0].IsLabel);
        Assert.Equal("doHideStuff", body[0].Keyword);
        Assert.Equal("SET", body[1].Keyword);
    }

    [Fact]
    public void Parse_ToleratesEmptyStatements()
    {
        // A trailing ';;' (seen in real scripts) must not produce a spurious item.
        WdlDocument doc = WdlParser.Parse("LEVEL \"a.wdl\", \"a.wrs\";;");

        Assert.Equal("LEVEL", Assert.Single(doc.Items).Keyword);
    }

    [Fact]
    public void Parse_UnbalancedBrace_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WdlParser.Parse("ACTION a { SET x, 1;"));
    }

    /// <summary>
    /// Parses every real WDL script (when present locally). A clean structural parse of
    /// all of the game's scripts is strong evidence the grammar is complete. No-op without data.
    /// </summary>
    [Fact]
    public void Parse_RealWdlFromArchives_AllParse()
    {
        IReadOnlyList<string> wrsFiles = ResearchData.WrsFiles();
        if (wrsFiles.Count == 0)
        {
            return; // no local game data — skip
        }

        int scripts = 0;
        long totalItems = 0;
        foreach (string path in wrsFiles)
        {
            WrsArchive archive = WrsArchive.ReadFile(path);
            foreach (WrsEntry entry in archive.Entries)
            {
                if (!entry.Name.EndsWith(".wdl", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string text = Encoding.Latin1.GetString(entry.GetData());
                WdlDocument doc = WdlParser.Parse(text);

                Assert.NotEmpty(doc.Items);
                totalItems += CountItems(doc.Items);
                scripts++;
            }
        }

        Assert.True(scripts > 0, "expected to parse at least one real WDL script");
        Assert.True(totalItems > scripts, "expected scripts to contain many items");
    }

    private static long CountItems(IReadOnlyList<WdlItem> items)
    {
        long count = items.Count;
        foreach (WdlItem item in items)
        {
            if (item.Body is { } body)
            {
                count += CountItems(body.Items);
            }
        }

        return count;
    }
}
