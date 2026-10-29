// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>inkey</c> probe: keyboard input into a string (manual p.85). The manual says
/// <c>INKEY</c> waits for [Return] like a <c>WAIT</c>, aborts on [Esc], [Up], [Down], [PgUp] and
/// [PgDn] (<c>RESULT</c> −1, 72, 80, 73, 81) restoring the old content, edits with [BackSpace],
/// [Del] and the cursor keys, stops accepting characters at the string's declared length, cuts
/// trailing spaces and sets <c>STR_LEN</c>. It does not say whether the input starts empty or from the
/// old content, what [BackSpace] removes at the start, what <c>STR_LEN</c> reads after an abort, or
/// whether the scheduler goes on while <c>INKEY</c> waits. Each case types its keys with DOSBox-X
/// <c>AUTOTYPE</c> once the probe action waits in <c>INKEY</c>, then prints <c>RESULT</c>,
/// <c>STR_LEN</c>, the cycles the global sampler counted while it waited, and the string between
/// brackets.
/// </summary>
internal static class InkeyProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVINK0.TXT).</summary>
    public const string TraceFile = "ovink";

    private static TraceProbeCase Keys(string label, string initial, string keys, string question) => new(
        label,
        question,
        $"STRING ik_s,\"{initial}\";\nSKILL ik_n {{ VAL 0; }}\nSKILL ik_b {{ VAL 0; }}\n" +
        Action("ik_tick", "    RULE ik_n = ik_n + 1;\n"),
        "    SET EACH_TICK.1, ik_tick;\n    WAIT 2;\n    RULE ik_b = ik_n;\n" + V("B", "TICKS", "ik_n") +
        "    INKEY ik_s;\n" + V("R", "TICKS", "ik_n", "ik_b", "RESULT", "STR_LEN") +
        "    PRINT_STRING \"S[\";\n    PRINT_STRING ik_s;\n    PRINT_STRING \"]\\n\";\n    SET EACH_TICK.1, NULL;\n",
        Predict(
            ("the input replaces the old content", "S[abc]"),
            ("the input is appended to it", "S[xxxxxabc]")),
        Autotype: "-w 10 -p 0.4 " + keys);

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Keys("type", "xxxxxxxx", "a b c enter", "Old content xxxxxxxx (8), type a b c, Return: does the input start empty or after the old text?"),
        Keys("escape", "xxxxxxxx", "a b esc", "Type a b, Esc: RESULT -1, the old content back, and STR_LEN?"),
        Keys("backspace", "hello", "bspace bspace p enter", "Old content hello, Backspace twice, p, Return: hel+p (edit from the end) or p?"),
        Keys("full", "xxx", "a b c d e enter", "A 3-character string, type five letters: the first three kept?"),
        Keys("up", "xxxxxxxx", "a up", "Type a, Up: RESULT 72?"),
        Keys("pgdn", "xxxxxxxx", "a pagedown", "Type a, PgDn: RESULT 81?"),
        Keys("trailing", "xxxxxxxx", "a b space space enter", "Type a b and two spaces, Return: trailing spaces cut, STR_LEN 2?"),
        Keys("empty", "xxxxxxxx", "enter", "Return at once: the old content kept, or an empty string?"),
        Keys("right", "hello", "right right z enter", "Round 2. Old content hello, cursor right twice, z, Return: hezlo (the cursor moves over the old text)?"),
        Keys("delete", "hello", "del enter", "Round 2. Old content hello, Del, Return: ello?"),
        Keys("type-bspace", "hello", "a b bspace c enter", "Round 2. Old content hello, a b, Backspace, c, Return: acllo?"),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "inkey",
        TraceFile,
        "ovins",
        "`B` is printed before INKEY (TICKS, cycles so far), `R` after it (TICKS, cycles, the count before,\n" +
        "RESULT, STR_LEN), `S[...]` the string. The keys are typed by AUTOTYPE 10 s after launch.",
        All);
}
