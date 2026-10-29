// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Interpreter;

namespace OpenVirtue.Engine.Tests.Interpreter;

/// <summary>The key sequences of OV-20261029-inkey and what the original left in the string.</summary>
public class InkeyEditorTests
{
    [Theory]
    [InlineData("xxxxxxxx", "a b c enter", "abcxxxxx", 0)]
    [InlineData("xxxxxxxx", "a b esc", "xxxxxxxx", -1)]
    [InlineData("hello", "bspace bspace p enter", "phell", 0)]
    [InlineData("xxx", "a b c d e enter", "abe", 0)]
    [InlineData("xxxxxxxx", "a up", "xxxxxxxx", 72)]
    [InlineData("xxxxxxxx", "a pagedown", "xxxxxxxx", 81)]
    [InlineData("xxxxxxxx", "a b space space enter", "ab  xxxx", 0)]
    [InlineData("xxxxxxxx", "enter", "xxxxxxxx", 0)]
    [InlineData("hello", "right right z enter", "hezll", 0)]
    [InlineData("hello", "a b bspace c enter", "achel", 0)]
    public void Press_MeasuredKeySequences_LeaveWhatTheOriginalLeft(string initial, string keys, string text, int result)
    {
        var editor = new InkeyEditor(initial, initial.Length);
        foreach (string key in keys.Split(' '))
        {
            _ = key switch
            {
                "enter" => editor.Press(InkeyKey.Return),
                "esc" => editor.Press(InkeyKey.Escape),
                "bspace" => editor.Press(InkeyKey.Backspace),
                "up" => editor.Press(InkeyKey.Up),
                "pagedown" => editor.Press(InkeyKey.PageDown),
                "right" => editor.Press(InkeyKey.Right),
                "space" => editor.Press(InkeyKey.Character, ' '),
                _ => editor.Press(InkeyKey.Character, key[0]),
            };
        }

        Assert.Equal(text, editor.Text);
        Assert.Equal(result, editor.Result);
    }

    [Fact]
    public void Press_Return_CutsTrailingSpaces()
    {
        var editor = new InkeyEditor("ab", 4);
        editor.Press(InkeyKey.Right);
        editor.Press(InkeyKey.Right);
        editor.Press(InkeyKey.Character, ' ');
        editor.Press(InkeyKey.Return);

        Assert.Equal("ab", editor.Text);
    }
}
