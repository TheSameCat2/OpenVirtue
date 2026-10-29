// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Interpreter;

/// <summary>A key that edits or ends an <c>INKEY</c> input.</summary>
public enum InkeyKey
{
    Character,
    Backspace,
    Delete,
    Left,
    Right,
    Return,
    Escape,
    Up,
    Down,
    PageUp,
    PageDown,
}

/// <summary>
/// The text editing of an <c>INKEY</c> input (manual p.85), as measured in OV-20261029-inkey:
/// <list type="bullet">
/// <item>the input edits the string's old content with the cursor at its first character;</item>
/// <item>a typed character is inserted at the cursor, the text cut to the string's declared length,
/// and the cursor never passes the last character, so typing at the end replaces it;</item>
/// <item>[BackSpace] removes the character before the cursor (nothing at the first), [Del] the one
/// at it, and the cursor keys move it over the text;</item>
/// <item>[Return] ends the input with <c>RESULT</c> 0 and trailing spaces cut; [Esc], [Up], [PgUp],
/// [Down] and [PgDn] restore the old content with <c>RESULT</c> −1, 72, 73, 80 and 81.</item>
/// </list>
/// </summary>
public sealed class InkeyEditor
{
    private readonly string _initial;
    private readonly int _capacity;
    private string _text;

    public InkeyEditor(string initial, int capacity)
    {
        _initial = initial ?? string.Empty;
        _capacity = Math.Max(1, capacity);
        _text = _initial.Length > _capacity ? _initial[.._capacity] : _initial;
    }

    /// <summary>The text as it stands.</summary>
    public string Text => _text;

    /// <summary>The cursor, an index into <see cref="Text"/>.</summary>
    public int Cursor { get; private set; }

    /// <summary>The <c>RESULT</c> once the input has ended, else <c>null</c>.</summary>
    public int? Result { get; private set; }

    /// <summary>Applies one key; returns <c>true</c> once the input has ended.</summary>
    public bool Press(InkeyKey key, char character = '\0')
    {
        if (Result is not null)
        {
            return true;
        }

        switch (key)
        {
            case InkeyKey.Character when !char.IsControl(character):
                {
                    string inserted = _text[..Cursor] + character + _text[Cursor..];
                    _text = inserted.Length > _capacity ? inserted[.._capacity] : inserted;
                    Cursor = Math.Min(Cursor + 1, _capacity - 1);
                    break;
                }

            case InkeyKey.Backspace when Cursor > 0:
                _text = _text.Remove(Cursor - 1, 1);
                Cursor--;
                break;
            case InkeyKey.Delete when Cursor < _text.Length:
                _text = _text.Remove(Cursor, 1);
                break;
            case InkeyKey.Left:
                Cursor = Math.Max(0, Cursor - 1);
                break;
            case InkeyKey.Right:
                Cursor = Math.Min(Cursor + 1, Math.Min(_text.Length, _capacity - 1));
                break;
            case InkeyKey.Return:
                _text = _text.TrimEnd(' ');
                Result = 0;
                break;
            case InkeyKey.Escape:
                Abort(-1);
                break;
            case InkeyKey.Up:
                Abort(72);
                break;
            case InkeyKey.PageUp:
                Abort(73);
                break;
            case InkeyKey.Down:
                Abort(80);
                break;
            case InkeyKey.PageDown:
                Abort(81);
                break;
        }

        return Result is not null;
    }

    private void Abort(int result)
    {
        _text = _initial;
        Result = result;
    }
}
