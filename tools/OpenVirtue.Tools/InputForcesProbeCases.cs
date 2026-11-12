// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>input-forces</c> probe: what the original's movement skills read while keys are held
/// and the mouse moves. The manual names the sources (p.119-120: cursor keys, [,] and [.], [Pg up]/[Pg dn],
/// [Home]/[End], the mouse, [Alt] with the mouse for strafing; <c>KEY_SENSE</c>, <c>SHIFT_SENSE</c>,
/// <c>MOUSE_SENSE</c>) but not the mouse's scale, whether a held key ramps, nor how the keyboard and the
/// mouse combine. A global <c>EACH_TICK</c> sampler prints every cycle; the input comes from outside, held
/// keys and relative mouse motion sent to the emulator through XTest on its own X display
/// (<c>tools/oracle/xtest_input.py</c>, on Xvfb), since <c>AUTOTYPE</c> cannot hold a key.
/// </summary>
internal static class InputForcesProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVINP0.TXT).</summary>
    public const string TraceFile = "ovinp";

    /// <summary>How many cycles the sampler prints: about 80 s at 60 cycles a second.</summary>
    public const int Cycles = 4800;

    private const string Skills =
        "SKILL in_n { VAL 0; }\nSKILL in_a { VAL 0; }\nSKILL in_r { VAL 0; }\nSKILL in_s { VAL 0; }\n" +
        "SKILL in_t { VAL 0; }\nSKILL in_u { VAL 0; }\n";

    private static readonly string Sampler = Action(
        "in_tick",
        "    RULE in_n = in_n + 1;\n" +
        "    IF (in_n < " + Cycles.ToString(CultureInfo.InvariantCulture) + ") {\n" +
        Indent(
            "    RULE in_a = FORCE_AHEAD * 1024;\n    RULE in_r = FORCE_ROT * 1024;\n    RULE in_s = FORCE_STRAFE * 1024;\n" +
            "    RULE in_t = FORCE_TILT * 1024;\n    RULE in_u = FORCE_UP * 1024;\n" +
            V("I", "TICKS", "in_n", "TIME_CORR", "in_a", "in_r", "in_s", "in_t", "in_u", "MICKEY_X", "MICKEY_Y",
                "KEY_SHIFT", "KEY_ALT", "KEY_CUU", "KEY_CUR", "MOUSE_MOVING", "MOUSE_X", "MOUSE_Y")) +
        "    }\n");

    private static readonly string Prelude =
        "    PRINTFILE \"" + TraceFile + "\", 0;\n" + V("S0", "TICKS", "in_n", "TIME_CORR", "KEY_SENSE", "SHIFT_SENSE", "MOUSE_SENSE", "MOUSE_CALM", "MOUSE_TIME") +
        "    SET EACH_TICK.1, in_tick;\n";

    private static TraceProbeCase Forces(string label, string setup, string question) => new(
        label,
        question,
        Skills + Sampler,
        setup + V("GO", "TICKS", "in_n", "KEY_SENSE", "SHIFT_SENSE", "MOUSE_SENSE") +
        "    WAIT " + (Cycles + 20).ToString(CultureInfo.InvariantCulture) + ";\n" + P("RET"),
        Predict(
            ("a held key gives KEY_SENSE at once", "in_a reads 1024 from the first cycle the key is down"),
            ("the mouse adds mickeys times a fixed factor, bounded at 1", "in_r is proportional to MICKEY_X and stops at ±1024")),
        Prelude: Prelude);

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Forces("defaults", "",
            "The forces with the runtime's own KEY_SENSE, SHIFT_SENSE and MOUSE_SENSE, keys held and the mouse moved from outside."),
        Forces("sense", "    SET KEY_SENSE, 0.5;\n    SET SHIFT_SENSE, 3;\n    SET MOUSE_SENSE, 2;\n",
            "The same input with KEY_SENSE 0.5, SHIFT_SENSE 3 and MOUSE_SENSE 2: which of them scale which force?"),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "input-forces",
        TraceFile,
        "ovinq",
        "After the skill dump, `IF_START` selects the trace file and arms the sampler. Each `I` line is `TICKS`,\n" +
        "the cycle, `TIME_CORR`, `FORCE_AHEAD`, `FORCE_ROT`, `FORCE_STRAFE`, `FORCE_TILT` and `FORCE_UP` × 1024,\n" +
        "`MICKEY_X`, `MICKEY_Y`, `KEY_SHIFT`, `KEY_ALT`, `KEY_CUU`, `KEY_CUR`, `MOUSE_MOVING`, `MOUSE_X` and `MOUSE_Y`.\n" +
        "The input comes from an XTest driver on the emulator's own X display, not from AUTOTYPE.",
        All);
}
