// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>save-body</c> probe: where a full <c>SAVE</c> (manual p.94, p.111) keeps the
/// world's state, as a step toward writing saves the original can load. OV-20261029-save-format
/// found the container, the header with the world's file names and the skill section; the other
/// sections were unread. Every case builds the same world (the room, a thing <c>sb_t</c>, an actor
/// <c>sb_a</c> and a free-standing wall <c>sb_w</c>), changes one thing and saves
/// <c>OVSAV2.SAV</c>, so a byte comparison of the unpacked bodies against <c>base</c> shows where
/// each value lives. <c>base2</c> repeats <c>base</c> to show which bytes change on their own.
/// </summary>
internal static class SaveBodyProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVSVB0.TXT).</summary>
    public const string TraceFile = "ovsvb";

    private const string Declarations =
        "SYNONYM sb_ts { TYPE THING; }\nSYNONYM sb_as { TYPE ACTOR; }\nSYNONYM sb_ws { TYPE WALL; }\n" +
        "SKILL sb_n { VAL 0; }\n" +
        "WALL sb_w { TEXTURE grn_tex; }\n" +
        "THING sb_t { TEXTURE mag_tex; }\n" +
        "ACTOR sb_a { TEXTURE yel_tex; }\n" +
        "ACTION sb_wait\n{\n    WAITT 200;\n}\n" +
        "ACTION sb_slot\n{\n    RULE sb_n = sb_n + 1;\n}\n";

    private static readonly TraceProbeMap World = new(
        Placements: ["THING sb_t 20 -8 0 0", "ACTOR sb_a 20 8 0 0"],
        Walls: [new TraceProbeWall("sb_w", 30, -4, 30, 4)]);

    private static TraceProbeCase Save(string label, string question, string change) => new(
        label,
        question,
        Declarations,
        "    SET sb_ts, sb_t;\n    SET sb_as, sb_a;\n    SET sb_ws, sb_w;\n" + change + "    WAIT 2;\n" +
        V("V", "PLAYER_X", "PLAYER_Y", "sb_ts.X", "sb_as.X", "sb_ws.OFFSET_X") + "    SAVE \"ovsav\", 2;\n" + P("SAVED"),
        Predict(("one section changes", "the unpacked body differs from base's in one place")),
        Map: World);

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Save("base", "The baseline world.", ""),
        Save("base2", "The baseline again: which bytes differ between two saves of the same world?", ""),
        Save("region-floor", "The room's FLOOR_HGT set to -2 (raw FFFFF800): where are region heights?", "    SET HERE.FLOOR_HGT, -2;\n"),
        Save("region-ceil", "The room's CEIL_HGT set to 20.5 (raw 00005200).", "    SET HERE.CEIL_HGT, 20.5;\n"),
        Save("region-skill", "The room's SKILL1 set to 77.25 (raw 00013500).", "    SET HERE.SKILL1, 77.25;\n"),
        Save("wall-offset", "The wall sb_w's OFFSET_X set to 3.5 (raw 00000E00).", "    SET sb_ws.OFFSET_X, 3.5;\n"),
        Save("thing-x", "The thing's X set to 21.5 (raw 00005600): where are things?", "    SET sb_ts.X, 21.5;\n"),
        Save("thing-skill", "The thing's SKILL1 set to 77.25.", "    SET sb_ts.SKILL1, 77.25;\n"),
        Save("actor-x", "The actor's X set to 22.25 (raw 00005900): where are actors?", "    SET sb_as.X, 22.25;\n"),
        Save("slot", "Global EACH_TICK.3 set to sb_slot: where are the global slots?", "    SET EACH_TICK.3, sb_slot;\n"),
        Save("wait", "sb_wait CALLed (WAITT 200 pending at the save): where are waiting actions?", "    CALL sb_wait;\n"),
        Fields("thing-fields", "sb_ts", [.. EntityFields.Where(static f => f is not ("CYCLE" or "SKILL5" or "SKILL6" or "SKILL7" or "SKILL8"))]),
        Fields("actor-fields", "sb_as", EntityFields),
        Fields("wall-fields", "sb_ws", WallFields),
        Fields("thing-a", "sb_ts", ["Y", "Z", "ANGLE", "HEIGHT", "SPEED", "VSPEED"]),
        Fields("thing-b", "sb_ts", ["TARGET_X", "TARGET_Y", "SKILL2", "SKILL3", "SKILL4"]),
        Fields("thing-c", "sb_ts", ["DIST"]),
        Fields("thing-d", "sb_ts", ["AMBIENT"]),
        Fields("wall-a", "sb_ws", ["OFFSET_Y"]),
        Fields("wall-b", "sb_ws", ["SKILL1", "SKILL2", "SKILL3", "SKILL4"]),
        Fields("wall-c", "sb_ws", ["DIST"]),
        Fields("wall-d", "sb_ws", ["AMBIENT"]),
    ];

    /// <summary>The object skills round 2 sets, each to <see cref="FieldValue"/> of its place in the list.</summary>
    public static string[] EntityFields =>
        ["X", "Y", "Z", "ANGLE", "HEIGHT", "SPEED", "VSPEED", "DIST", "CYCLE", "SKILL1", "SKILL2", "SKILL3", "SKILL4",
         "SKILL5", "SKILL6", "SKILL7", "SKILL8", "TARGET_X", "TARGET_Y", "AMBIENT"];

    public static string[] WallFields =>
        ["OFFSET_X", "OFFSET_Y", "DIST", "SKILL1", "SKILL2", "SKILL3", "SKILL4",
         "AMBIENT"];

    /// <summary>
    /// A value whose raw 22.10 pattern is unique to field <paramref name="index"/>: 0.5 + index/16 + 1/1024
    /// (raw 0x201 + 64·index), small enough for angles, cycles and ambients.
    /// </summary>
    public static double FieldValue(int index) => (0x201 + (64 * index)) / 1024.0;

    private static TraceProbeCase Fields(string label, string target, string[] fields) => new(
        label,
        $"Round 2. Every listed skill of {target} set to its own raw pattern 0x201 + 64·i: where is each in the object's record?",
        Declarations,
        "    SET sb_ts, sb_t;\n    SET sb_as, sb_a;\n    SET sb_ws, sb_w;\n" +
        string.Concat(fields.Select((f, i) => FormattableString.Invariant($"    SET {target}.{f}, {FieldValue(i)};\n"))) +
        "    WAIT 2;\n" + V("F", [.. fields.Select(f => target + "." + f)]) + "    SAVE \"ovsav\", 2;\n" + P("SAVED"),
        Predict(("fixed offsets", "each pattern once in the object's record")),
        Map: World);

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "save-body",
        TraceFile,
        "ovsbs",
        "Each case changes one value of the same world and leaves `OVSAV2.SAV` in `work/`; unpack the bodies\n" +
        "and compare each with `base`'s. `V` holds the player position and the changed objects' values.",
        All);
}
