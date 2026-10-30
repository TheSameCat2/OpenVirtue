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

    /// <summary>Round 10: the room region with <paramref name="flags"/>, its SKILL1 and floor changed before the save.</summary>
    private static TraceProbeCase RegionSave(string label, string flags) => new(
        label,
        $"Round 10. The room declared FLAGS {flags}, its SKILL1 set to 77.25 and FLOOR_HGT to -2: where do SAVE regions go?",
        Declarations,
        "    SET sb_ts, sb_t;\n    SET sb_as, sb_a;\n    SET sb_ws, sb_w;\n    SET HERE.SKILL1, 77.25;\n    SET HERE.FLOOR_HGT, -2;\n    WAIT 2;\n" +
        "    SAVE \"ovsav\", 2;\n" + P("SAVED"),
        Predict(("regions in the empty sections", "the second or third section holds a region record")),
        Map: World with { RoomBody = "FLAGS " + flags + ";" });

    /// <summary>Round 10: the SAVE room's listed skills, each set to its own pattern, as round 2 did for objects.</summary>
    private static TraceProbeCase RegionFields(string label, string[] fields) => new(
        label,
        "Round 10. The SAVE room's skills each set to its own raw pattern 0x201 + 64·i: where is each in its record?",
        Declarations,
        "    SET sb_ts, sb_t;\n    SET sb_as, sb_a;\n    SET sb_ws, sb_w;\n" +
        string.Concat(fields.Select((f, i) => FormattableString.Invariant($"    SET HERE.{f}, {FieldValue(i)};\n"))) +
        "    WAIT 2;\n" + V("F", [.. fields.Select(f => "HERE." + f)]) + "    SAVE \"ovsav\", 2;\n" + P("SAVED"),
        Predict(("fixed offsets", "each pattern once in the region's record")),
        Map: World with { RoomBody = "FLAGS SAVE;" });

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        RegionFields("region-fields", ["FLOOR_HGT", "CEIL_HGT", "FLOOR_OFFS_X", "FLOOR_OFFS_Y", "CEIL_OFFS_X", "CEIL_OFFS_Y",
            "SKILL1", "SKILL2", "SKILL3", "SKILL4", "SKILL5", "SKILL6", "SKILL7", "SKILL8"]),
        RegionFields("region-ambient", ["AMBIENT"]),
        RegionSave("region-save", "SAVE"),
        RegionSave("region-save-all", "SAVE, SAVE_ALL"),
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
        new TraceProbeCase(
            "ip",
            "Round 8. Nine actions, each running a different mix of instructions before WAITT 100, are CALLed before a SAVE: " +
            "how do the two position words of each waiting action's record count?",
            "SKILL ip_x { VAL 0; }\nSKILL ip_k { VAL 0; }\n" +
            "ACTION ip_a0\n{\n    WAITT 100;\n}\n" +
            "ACTION ip_a1\n{\n    RULE ip_x = 1;\n    WAITT 100;\n}\n" +
            "ACTION ip_a2\n{\n    RULE ip_x = 1;\n    RULE ip_x = 2;\n    WAITT 100;\n}\n" +
            "ACTION ip_a3\n{\n    SET ip_x, 1;\n    WAITT 100;\n}\n" +
            "ACTION ip_a4\n{\n    RULE ip_x = 1 + 2 * 3 - 4;\n    WAITT 100;\n}\n" +
            "ACTION ip_a5\n{\n    IF (ip_x > 0) {\n        RULE ip_k = 1;\n    }\n    WAITT 100;\n}\n" +
            "ACTION ip_a6\n{\n    WHILE (ip_k < 5) {\n        WAITT 100;\n    }\n}\n" +
            "ACTION ip_a7\n{\n    WAITT 100;\n    RULE ip_x = 1;\n    WAITT 100;\n}\n" +
            "ACTION ip_a8\n{\n    WAIT 1;\n    WAITT 100;\n}\n",
            "    CALL ip_a0;\n    CALL ip_a1;\n    CALL ip_a2;\n    CALL ip_a3;\n    CALL ip_a4;\n    CALL ip_a5;\n    CALL ip_a6;\n    CALL ip_a7;\n    CALL ip_a8;\n" +
            "    WAIT 3;\n    SAVE \"ovsav\", 2;\n" + P("SAVED"),
            Predict(
                ("instructions counted from the action's start", "a0 0, a1 1, a2 2, a3 1 …"),
                ("compiled words", "a1–a4 differ by their expressions' length"))),
        new TraceProbeCase(
            "ip2",
            "Round 8b. More actions waiting in WAITT 100 with instructions after the wait, in WHILE bodies and after IF/ELSE: what is the second position word?",
            "SKILL ip_x { VAL 0; }\nSKILL ip_k { VAL 0; }\n" +
            "ACTION ip_b0\n{\n    WAITT 100;\n}\n" +
            "ACTION ip_b1\n{\n    WAITT 100;\n    RULE ip_x = 1;\n}\n" +
            "ACTION ip_b2\n{\n    WAITT 100;\n    RULE ip_x = 1;\n    RULE ip_x = 2;\n}\n" +
            "ACTION ip_b3\n{\n    WAITT 100;\n    RULE ip_x = 1;\n    RULE ip_x = 2;\n    RULE ip_x = 3;\n}\n" +
            "ACTION ip_b4\n{\n    RULE ip_x = 1;\n    WAITT 100;\n    RULE ip_x = 2;\n}\n" +
            "ACTION ip_c2\n{\n    WHILE (ip_k < 5) {\n        RULE ip_x = 1;\n        WAITT 100;\n    }\n}\n" +
            "ACTION ip_c3\n{\n    RULE ip_x = 1;\n    WHILE (ip_k < 5) {\n        WAITT 100;\n    }\n}\n" +
            "ACTION ip_c4\n{\n    WHILE (ip_k < 5) {\n        WAITT 100;\n        RULE ip_x = 1;\n    }\n}\n" +
            "ACTION ip_c5\n{\n    WHILE (ip_k < 5) {\n        WAITT 100;\n    }\n    RULE ip_x = 1;\n}\n" +
            "ACTION ip_d1\n{\n    IF (ip_x > 50) {\n        RULE ip_k = 1;\n    } ELSE {\n        RULE ip_k = 2;\n    }\n    WAITT 100;\n}\n" +
            "ACTION ip_d2\n{\n    IF (ip_x < 50) {\n        WAITT 100;\n    }\n    RULE ip_k = 2;\n}\n" +
            "",
            "    CALL ip_b0;\n    CALL ip_b1;\n    CALL ip_b2;\n    CALL ip_b3;\n    CALL ip_b4;\n    CALL ip_c2;\n    CALL ip_c3;\n    CALL ip_c4;\n    CALL ip_c5;\n    CALL ip_d1;\n    CALL ip_d2;\n" +
            "    WAIT 3;\n    SAVE \"ovsav\", 2;\n" + P("SAVED"),
            Predict(("a pointer past the block", "the second word grows with what follows the wait"))),
        new TraceProbeCase(
            "ip3",
            "Round 8c. One instruction of each kind the carrier scaffold uses before WAITT 100: which of them take a place?",
            "SKILL ip_x { VAL 0; }\n" +
            "ACTION ip_e1\n{\n    PRINTFILE \"ovsvx\", 0;\n    WAITT 100;\n}\n" +
            "ACTION ip_e2\n{\n    PRINT_STRING \"x\";\n    WAITT 100;\n}\n" +
            "ACTION ip_e3\n{\n    PRINT_VALUE ip_x;\n    WAITT 100;\n}\n" +
            "ACTION ip_e4\n{\n    SET PLAYER_ARC, 1;\n    WAITT 100;\n}\n" +
            "ACTION ip_e5\n{\n    SET RENDER_MODE, 2;\n    WAITT 100;\n}\n" +
            "ACTION ip_e6\n{\n    SET MOTION_BLUR, 0;\n    WAITT 100;\n}\n" +
            "ACTION ip_e7\n{\n    PRINT_STRING \"\\n\";\n    WAITT 100;\n}\n" +
            "ACTION ip_e8\n{\n    SET PLAYER_SIZE, 8;\n    WAITT 100;\n}\n" +
            "",
            "    CALL ip_e1;\n    CALL ip_e2;\n    CALL ip_e3;\n    CALL ip_e4;\n    CALL ip_e5;\n    CALL ip_e6;\n    CALL ip_e7;\n    CALL ip_e8;\n" +
            "    WAIT 3;\n    SAVE \"ovsav\", 2;\n" + P("SAVED"),
            Predict(("each takes one place", "every action waits at 1"))),
        new TraceProbeCase(
            "load-edit",
            "Round 7, run twice. The probe LOADs OVSAV2.SAV (missing on the first run), SAVEs it and prints the world. " +
            "On the second run the LOAD resumes the saved action after its SAVE: does the original load a save edited in between, and which values does it take? " +
            "sb_m is LOCAL (not in a full save): 2 means the LOAD failed and the action went on, 0 that it loaded.",
            Declarations + "SKILL sb_m { TYPE LOCAL; VAL 0; }\nSKILL sb_k { VAL 0; }\n" +
            "ACTION sb_tick\n{\n    RULE sb_k = sb_k + 1;\n    IF (sb_k < 40) {\n        PRINTFILE \"ovsvt\", 1;\n" +
            "        PRINT_STRING \"T \";\n        PRINT_VALUE TICKS;\n        PRINT_STRING \" \";\n        PRINT_VALUE sb_k;\n" +
            "        PRINT_STRING \" \";\n        PRINT_VALUE sb_m;\n        PRINT_STRING \" \";\n        PRINT_VALUE sb_n;\n" +
            "        PRINT_STRING \" \";\n        PRINT_VALUE sb_as.X;\n        PRINT_STRING \"\\n\";\n    }\n}\n" +
            "ACTION sb_note\n{\n    WAIT 2;\n    PRINTFILE \"ovsvn\", 0;\n    PRINT_STRING \"N \";\n    PRINT_VALUE sb_k;\n    PRINT_STRING \" \";\n    PRINT_VALUE sb_m;\n    PRINT_STRING \"\\n\";\n}\n",
            "    SET sb_ts, sb_t;\n    SET sb_as, sb_a;\n    SET sb_ws, sb_w;\n    SET EACH_TICK.2, sb_tick;\n    WAIT 2;\n" +
            "    LOAD \"ovsav\", 2;\n    WAIT 3;\n    RULE sb_m = 2;\n    RULE sb_n = 7;\n    CALL sb_note;\n    SAVE \"ovsav\", 2;\n    PRINTFILE \"ovsvl\", 0;\n" +
            V("L", "sb_m", "sb_n", "sb_ts.X", "sb_as.X", "sb_as.Y", "sb_ws.OFFSET_X") + P("RET"),
            Predict(
                ("the edit loads", "the second run prints the edited values"),
                ("a check rejects it", "the second run prints the first run's values, or LOAD fails")),
            Map: World),
        new TraceProbeCase(
            "load-region",
            "Round 10, run twice, with the room a SAVE region whose SKILL1 is set to 33 before the SAVE. The probe LOADs OVSAV2.SAV (missing on the first run), SAVEs it and prints the world. " +
            "On the second run the LOAD resumes the saved action after its SAVE: does the original load a save edited in between, and which values does it take? " +
            "sb_m is LOCAL (not in a full save): 2 means the LOAD failed and the action went on, 0 that it loaded.",
            Declarations + "SYNONYM sb_rg { TYPE REGION; }\nSKILL sb_m { TYPE LOCAL; VAL 0; }\nSKILL sb_k { VAL 0; }\n" +
            "ACTION sb_tick\n{\n    RULE sb_k = sb_k + 1;\n    IF (sb_k < 40) {\n        PRINTFILE \"ovsvt\", 1;\n" +
            "        PRINT_STRING \"T \";\n        PRINT_VALUE TICKS;\n        PRINT_STRING \" \";\n        PRINT_VALUE sb_k;\n" +
            "        PRINT_STRING \" \";\n        PRINT_VALUE sb_m;\n        PRINT_STRING \" \";\n        PRINT_VALUE sb_n;\n" +
            "        PRINT_STRING \" \";\n        PRINT_VALUE sb_as.X;\n        PRINT_STRING \" \";\n        PRINT_VALUE sb_rg.SKILL1;\n        PRINT_STRING \"\\n\";\n    }\n}\n" +
            "ACTION sb_note\n{\n    WAIT 2;\n    PRINTFILE \"ovsvn\", 0;\n    PRINT_STRING \"N \";\n    PRINT_VALUE sb_k;\n    PRINT_STRING \" \";\n    PRINT_VALUE sb_m;\n    PRINT_STRING \"\\n\";\n}\n",
            "    SET sb_ts, sb_t;\n    SET sb_as, sb_a;\n    SET sb_ws, sb_w;\n    SET sb_rg, HERE;\n    SET EACH_TICK.2, sb_tick;\n    WAIT 2;\n" +
            "    LOAD \"ovsav\", 2;\n    WAIT 3;\n    RULE sb_m = 2;\n    RULE sb_n = 7;\n    SET HERE.SKILL1, 33;\n    CALL sb_note;\n    SAVE \"ovsav\", 2;\n    PRINTFILE \"ovsvl\", 0;\n" +
            V("L", "sb_m", "sb_n", "sb_ts.X", "sb_as.X", "sb_as.Y", "sb_ws.OFFSET_X") + P("RET"),
            Predict(
                ("the edit loads", "the second run prints the edited values"),
                ("a check rejects it", "the second run prints the first run's values, or LOAD fails")),
            Map: World with { RoomBody = "FLAGS SAVE;" }),
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
