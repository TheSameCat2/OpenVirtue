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

    private static readonly string[] ThingFlags =
        ["INVISIBLE", "PASSABLE", "BERKELEY", "LIBER", "FAR", "GROUND", "CANDELABER", "SEEN", "MOVED", "PLAY", "IMMATERIAL",
         "SENSITIVE", "FRAGILE", "CAREFULLY", "MASTER", "SAVE", "FLAG1", "FLAG2", "FLAG3", "FLAG4", "FLAG5", "FLAG6", "FLAG7", "FLAG8"];

    private static readonly string[] WallFlags =
        ["INVISIBLE", "PASSABLE", "IMMATERIAL", "IMPASSABLE", "SEEN", "BERKELEY", "TRANSPARENT", "PLAY", "CURTAIN", "PORTCULLIS",
         "FENCE", "SENSITIVE", "FRAGILE", "FAR", "SAVE", "FLAG1", "FLAG2", "FLAG3", "FLAG4", "FLAG5", "FLAG6", "FLAG7", "FLAG8"];

    /// <summary>Round 12: <paramref name="flag"/> declared on the thing, the actor and the free-standing wall where it applies.</summary>
    private static TraceProbeCase FlagSave(string flag)
    {
        string thing = ThingFlags.Contains(flag) ? $" FLAGS {flag};" : "";
        string wall = WallFlags.Contains(flag) ? $" FLAGS {flag};" : "";
        string declarations = Declarations
            .Replace("WALL sb_w { TEXTURE grn_tex; }", "WALL sb_w { TEXTURE grn_tex;" + wall + " }", StringComparison.Ordinal)
            .Replace("THING sb_t { TEXTURE mag_tex; }", "THING sb_t { TEXTURE mag_tex;" + thing + " }", StringComparison.Ordinal)
            .Replace("ACTOR sb_a { TEXTURE yel_tex; }", "ACTOR sb_a { TEXTURE yel_tex;" + thing + " }", StringComparison.Ordinal);
        return new TraceProbeCase(
            "flag-" + flag.ToLowerInvariant(),
            $"Round 12. FLAGS {flag} on the thing, the actor and the wall where it applies: which bits of their records change?",
            declarations,
            "    SET sb_ts, sb_t;\n    SET sb_as, sb_a;\n    SET sb_ws, sb_w;\n    WAIT 2;\n    SAVE \"ovsav\", 2;\n" + P("SAVED"),
            Predict(("a bit of the +20 word", "one bit differs from base in each flagged record")),
            Map: World);
    }

    private const string EventActions =
        "SKILL ev_x { VAL 0; }\n" +
        "ACTION ev_near\n{\n    RULE ev_x = ev_x + 1;\n}\n" +
        "ACTION ev_far\n{\n    RULE ev_x = ev_x + 2;\n}\n" +
        "ACTION ev_arr\n{\n    RULE ev_x = ev_x + 3;\n}\n" +
        "ACTION ev_hit\n{\n    RULE ev_x = ev_x + 4;\n}\n" +
        "ACTION ev_tick\n{\n    RULE ev_x = ev_x + 5;\n}\n" +
        "ACTION ev_cycle\n{\n    RULE ev_x = ev_x + 6;\n}\n" +
        "ACTION ev_klick\n{\n    RULE ev_x = ev_x + 7;\n}\n";

    /// <summary>Round 13: the objects declared with <paramref name="wall"/> and <paramref name="entity"/> added to their bodies.</summary>
    private static TraceProbeCase Declared(string label, string question, string wall, string entity, string extra = "") => new(
        label,
        question,
        EventActions + Declarations
            .Replace("WALL sb_w { TEXTURE grn_tex; }", "WALL sb_w { TEXTURE grn_tex; " + wall + " }", StringComparison.Ordinal)
            .Replace("THING sb_t { TEXTURE mag_tex; }", "THING sb_t { TEXTURE mag_tex; " + entity + " }", StringComparison.Ordinal)
            .Replace("ACTOR sb_a { TEXTURE yel_tex; }", "ACTOR sb_a { TEXTURE yel_tex; " + entity + " }", StringComparison.Ordinal) + extra,
        "    SET sb_ts, sb_t;\n    SET sb_as, sb_a;\n    SET sb_ws, sb_w;\n    WAIT 2;\n    SAVE \"ovsav\", 2;\n" + P("SAVED"),
        Predict(("fixed slots", "each event's action number once, in the same order in every record")),
        Map: World);

    private static readonly string[] RegionFlags =
        ["FLOOR_ASCEND", "CEIL_ASCEND", "FLOOR_DESCEND", "CEIL_DESCEND", "SEEN", "BASE", "STICKY", "PLAY",
         "FLAG1", "FLAG2", "FLAG3", "FLAG4", "FLAG5", "FLAG6", "FLAG7", "FLAG8"];

    /// <summary>Round 15: the room declared with <paramref name="body"/>.</summary>
    private static TraceProbeCase RegionDeclared(string label, string question, string body) => new(
        label,
        question,
        EventActions + Declarations,
        "    SET sb_ts, sb_t;\n    SET sb_as, sb_a;\n    SET sb_ws, sb_w;\n    WAIT 2;\n    SAVE \"ovsav\", 2;\n" + P("SAVED"),
        Predict(("fixed slots", "each event's action number once in the region's record")),
        Map: World with { RoomBody = body });

    /// <summary>Round 15b: SAVE textures declared after <see cref="Declarations"/>, none of them shown.</summary>
    private static TraceProbeCase TextureSave(string label, string question, string textures) => new(
        label,
        question,
        EventActions + "SOUND sb_snd,<" + AudioProbeCases.Quarter + ">;\n" + $"FONT sb_font,<{UiLayoutProbeCases.FontFile}>,8,8;\n" +
        "STRING sb_str, \"t\";\n" + Declarations + textures,
        "    SET sb_ts, sb_t;\n    SET sb_as, sb_a;\n    SET sb_ws, sb_w;\n    WAIT 2;\n    SAVE \"ovsav\", 2;\n" + P("SAVED"),
        Predict(("one record per SAVE texture", "the second section holds a 68-byte record for each")),
        Map: World);

    /// <summary>Round 15b: the texture properties a value can be given, each set to its own pattern (<see cref="FieldValue"/>).</summary>
    private static readonly string[] TextureValues =
        ["AMBIENT", "ALBEDO", "RADIANCE", "SCALE_X", "SCALE_Y", "SVOL", "SDIST", "SVDIST", "POS_X", "POS_Y", "RANDOM"];

    /// <summary>Round 16: the key hooks probed, each set to its own action (the manual's list under IF_F1, and the ones Saints of Virtue uses).</summary>
    public static readonly string[] KeyHooks =
        ["IF_ESC", "IF_1", "IF_0", "IF_SZ", "IF_APO", "IF_BKSP", "IF_TAB", "IF_Q", "IF_Y", "IF_Z", "IF_BRACKL", "IF_BRACKR",
         "IF_PLUS", "IF_MINUS", "IF_ENTER", "IF_CTRL", "IF_ALT", "IF_SPACE", "IF_CAR", "IF_CAL", "IF_F1", "IF_F10", "IF_F11",
         "IF_F12", "IF_CUU", "IF_CUD", "IF_CUL", "IF_CUR", "IF_PGUP", "IF_PGDN", "IF_HOME", "IF_END", "IF_INS", "IF_DEL",
         "IF_PAUSE", "IF_ANYKEY", "IF_LEFT", "IF_RIGHT", "IF_MIDDLE"];

    /// <summary>Round 16: actions <c>sk_0</c>… for <paramref name="count"/> hooks, then <paramref name="body"/> setting them.</summary>
    private static TraceProbeCase Slots(string label, string question, int count, string body) => new(
        label,
        question,
        Declarations + string.Concat(Enumerable.Range(0, count).Select(static i => FormattableString.Invariant($"ACTION sk_{i}\n{{\n    RULE sb_n = {i};\n}}\n"))),
        "    SET sb_ts, sb_t;\n    SET sb_as, sb_a;\n    SET sb_ws, sb_w;\n" + body + "    WAIT 2;\n    SAVE \"ovsav\", 2;\n" + P("SAVED"),
        Predict(("scan codes", "each key's action number at its PC scan code in the slot section")),
        Map: World);

    private const string ScreenResources =
        "BMAP se_pan,<" + UiLayoutProbeCases.PanelFile + ">;\n" + "FONT se_font,<" + UiLayoutProbeCases.FontFile + ">,8,8;\n" +
        "STRING se_s,\"Ab\";\nOVLY se_om,<PRBRED.PCX>;\n";

    private const string ScreenPanel = "PANEL se_p { PAN_MAP se_pan; POS_X 11; POS_Y 22; LAYER 3; FLAGS VISIBLE; }\n";
    private const string ScreenText = "TEXT se_t { POS_X 33; POS_Y 44; LAYER 5; FONT se_font; STRING se_s; FLAGS VISIBLE; }\n";
    private const string ScreenOverlay = "OVERLAY se_o { OVLYS se_om; POS_X 55; POS_Y 66; LAYER 7; FLAGS VISIBLE; }\n";

    /// <summary>Round 18: the screen elements <paramref name="elements"/> declared, <paramref name="change"/> run, then saved.</summary>
    private static TraceProbeCase Screen(string label, string question, string elements, string change = "") => new(
        label,
        question,
        ScreenResources + Declarations + elements,
        "    SET sb_ts, sb_t;\n    SET sb_as, sb_a;\n    SET sb_ws, sb_w;\n" + change + "    WAIT 2;\n    SAVE \"ovsav\", 2;\n" + P("SAVED"),
        Predict(("one record per element", "the screen elements' section counts them and holds their places")),
        Map: World);

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Screen("se-panel", "Round 18. One PANEL at 11, 22, LAYER 3, VISIBLE: its record in the screen elements' section?", ScreenPanel),
        Screen("se-text", "Round 18. One TEXT at 33, 44, LAYER 5, VISIBLE.", ScreenText),
        Screen("se-ovly", "Round 18. One OVERLAY at 55, 66, LAYER 7, VISIBLE.", ScreenOverlay),
        Screen("se-ovly-abs", "Round 18. The OVERLAY with FLAGS VISIBLE, ABSPOS.", ScreenOverlay.Replace("FLAGS VISIBLE;", "FLAGS VISIBLE, ABSPOS;", StringComparison.Ordinal)),
        Screen("se-ovly-hidden", "Round 18. The OVERLAY without VISIBLE.", ScreenOverlay.Replace(" FLAGS VISIBLE;", "", StringComparison.Ordinal)),
        Screen("se-layers", "Round 18. Three OVERLAYs declared with LAYER 7, 3, 5 at x 10, 20, 30: in which order are they saved?",
            "OVERLAY se_a { OVLYS se_om; POS_X 10; POS_Y 1; LAYER 7; FLAGS VISIBLE; }\n" +
            "OVERLAY se_b { OVLYS se_om; POS_X 20; POS_Y 2; LAYER 3; FLAGS VISIBLE; }\n" +
            "OVERLAY se_c { OVLYS se_om; POS_X 30; POS_Y 3; LAYER 5; FLAGS VISIBLE; }\n"),
        Screen("se-same", "Round 18. Three OVERLAYs all LAYER 5 at x 10, 20, 30, the middle one not VISIBLE.",
            "OVERLAY se_a { OVLYS se_om; POS_X 10; POS_Y 1; LAYER 5; FLAGS VISIBLE; }\n" +
            "OVERLAY se_b { OVLYS se_om; POS_X 20; POS_Y 2; LAYER 5; }\n" +
            "OVERLAY se_c { OVLYS se_om; POS_X 30; POS_Y 3; LAYER 5; FLAGS VISIBLE; }\n"),
        Screen("se-digits", "Round 18. A PANEL with DIGITS 4,4,5,font,1,se_v1 and DIGITS 40,14,3,font,2,se_v2 (skills 7 and 9): its items?",
            "SKILL se_v1 { VAL 7; }\nSKILL se_v2 { VAL 9; }\n" +
            "PANEL se_p { PAN_MAP se_pan; POS_X 11; POS_Y 22; LAYER 3; DIGITS 4,4,5,se_font,1,se_v1; DIGITS 40,14,3,se_font,2,se_v2; FLAGS VISIBLE; }\n"),
        Screen("se-button", "Round 18. A PANEL with BUTTON 60,4,se_pan,se_pan,NULL,NULL and HSLIDER 4,30,32,se_pan,se_v1.",
            "SKILL se_v1 { VAL 7; }\n" +
            "PANEL se_p { PAN_MAP se_pan; POS_X 11; POS_Y 22; LAYER 3; BUTTON 60,4,se_pan,se_pan,NULL,NULL; HSLIDER 4,30,32,se_pan,se_v1; FLAGS VISIBLE; }\n"),
        Screen("se-nolayer", "Round 18. OVERLAYs with LAYER 5 (x 10), no LAYER (x 20) and LAYER 1 (x 30), and a TEXT without LAYER (x 40): their order?",
            "OVERLAY se_a { OVLYS se_om; POS_X 10; POS_Y 1; LAYER 5; FLAGS VISIBLE; }\n" +
            "OVERLAY se_b { OVLYS se_om; POS_X 20; POS_Y 2; FLAGS VISIBLE; }\n" +
            "OVERLAY se_c { OVLYS se_om; POS_X 30; POS_Y 3; LAYER 1; FLAGS VISIBLE; }\n" +
            "TEXT se_t { POS_X 40; POS_Y 4; FONT se_font; STRING se_s; FLAGS VISIBLE; }\n"),
        Screen("se-flags", "Round 18. Texts with CENTER_X (x 1), CENTER_Y (x 2), CONDENSED (x 3), NARROW (x 4); panels with TRANSPARENT (x 6), RELPOS (x 7), REFRESH (x 8); all LAYER 2: their flag words?",
            "TEXT se_t1 { POS_X 1; LAYER 2; FONT se_font; STRING se_s; FLAGS CENTER_X; }\n" +
            "TEXT se_t2 { POS_X 2; LAYER 2; FONT se_font; STRING se_s; FLAGS CENTER_Y; }\n" +
            "TEXT se_t3 { POS_X 3; LAYER 2; FONT se_font; STRING se_s; FLAGS CONDENSED; }\n" +
            "TEXT se_t4 { POS_X 4; LAYER 2; FONT se_font; STRING se_s; FLAGS NARROW; }\n" +
            "PANEL se_p6 { PAN_MAP se_pan; POS_X 6; LAYER 2; FLAGS TRANSPARENT; }\n" +
            "PANEL se_p7 { PAN_MAP se_pan; POS_X 7; LAYER 2; FLAGS RELPOS; }\n" +
            "PANEL se_p8 { PAN_MAP se_pan; POS_X 8; LAYER 2; FLAGS REFRESH; }\n"),
        Screen("se-show", "Round 19. Hidden OVERLAYs with LAYER 1, 2, 3 (x 10, 20, 30); the probe shows the third, waits, then the first: their order?",
            "OVERLAY se_a { OVLYS se_om; POS_X 10; POS_Y 1; LAYER 1; }\n" +
            "OVERLAY se_b { OVLYS se_om; POS_X 20; POS_Y 2; LAYER 2; }\n" +
            "OVERLAY se_c { OVLYS se_om; POS_X 30; POS_Y 3; LAYER 3; }\n",
            "    SET se_c.VISIBLE, 1;\n    WAIT 1;\n    SET se_a.VISIBLE, 1;\n"),
        Screen("se-showhide", "Round 19. The same three hidden; the probe shows the third, waits and hides it again: their order?",
            "OVERLAY se_a { OVLYS se_om; POS_X 10; POS_Y 1; LAYER 1; }\n" +
            "OVERLAY se_b { OVLYS se_om; POS_X 20; POS_Y 2; LAYER 2; }\n" +
            "OVERLAY se_c { OVLYS se_om; POS_X 30; POS_Y 3; LAYER 3; }\n",
            "    SET se_c.VISIBLE, 1;\n    WAIT 1;\n    SET se_c.VISIBLE, 0;\n"),
        Screen("se-all", "Round 18. A TEXT, an OVERLAY and a PANEL declared in that order: in which order are they saved?", ScreenText + ScreenOverlay + ScreenPanel),
        new TraceProbeCase(
            "my-load",
            "Round 17, run twice. The thing's IF_NEAR (DIST 100) starts sb_go, which waits WAITT 20 and then sets MY.X to 25. The probe LOADs " +
            "OVSAV2.SAV (missing on the first run), SAVEs while sb_go waits and prints the thing's X 40 ticks on. Does the loaded " +
            "sb_go still know its MY? sb_m is LOCAL: 2 means the LOAD failed and the action went on, 0 that it loaded.",
            Declarations.Replace("THING sb_t { TEXTURE mag_tex; }", "THING sb_t { TEXTURE mag_tex; DIST 100; IF_NEAR sb_go; }", StringComparison.Ordinal) +
            "SKILL sb_m { TYPE LOCAL; VAL 0; }\n" +
            "ACTION sb_go\n{\n    WAITT 20;\n    SET MY.X, 25;\n}\n",
            "    SET sb_ts, sb_t;\n    SET sb_as, sb_a;\n    SET sb_ws, sb_w;\n    WAIT 2;\n    LOAD \"ovsav\", 2;\n    WAIT 3;\n" +
            "    RULE sb_m = 2;\n    SAVE \"ovsav\", 2;\n    WAITT 40;\n    PRINTFILE \"ovsvl\", 0;\n" +
            V("L", "sb_m", "sb_ts.X", "sb_as.X", "sb_ws.OFFSET_X") + P("RET"),
            Predict(
                ("MY kept", "the second run prints X 25"),
                ("MY lost", "the second run prints X 20")),
            Map: World),
        Declared(
            "my-you2",
            "Round 17. As my-you, but the thing's action waits after two instructions and the wall's CALLs a waiting action: " +
            "is the short record's last word a place, and does a CALLed action keep MY?",
            "EACH_TICK sb_gw;",
            "EACH_TICK sb_go;",
            "ACTION sb_sub\n{\n    WAITT 150;\n}\n" +
            "ACTION sb_go\n{\n    SET MY.EACH_TICK, NULL;\n    RULE sb_n = 3;\n    WAITT 200;\n}\n" +
            "ACTION sb_gw\n{\n    SET MY.EACH_TICK, NULL;\n    CALL sb_sub;\n    WAITT 200;\n}\n"),
        Declared(
            "my-you",
            "Round 17. The thing, the actor and the wall each start a waiting action from EACH_TICK (WAITT 200): how do the " +
            "waiting actions' MY words name them? (V3.963 rejects SET YOU.)",
            "EACH_TICK sb_gw;",
            "EACH_TICK sb_go;",
            "ACTION sb_go\n{\n    SET MY.EACH_TICK, NULL;\n    WAITT 200;\n}\n" +
            "ACTION sb_gw\n{\n    SET MY.EACH_TICK, NULL;\n    WAITT 200;\n}\n"),
        Slots(
            "keys",
            "Round 16. Every key hook the manual and Saints of Virtue name, each SET to its own action: at which word of the slot section is each?",
            KeyHooks.Length,
            string.Concat(KeyHooks.Select(static (k, i) => FormattableString.Invariant($"    SET {k}, sk_{i};\n")))),
        Slots(
            "each-sec",
            "Round 16. EACH_SEC.1 to EACH_SEC.16 and EACH_TICK.1 set, each to its own action: where are the EACH_SEC slots?",
            17,
            string.Concat(Enumerable.Range(1, 16).Select(static i => FormattableString.Invariant($"    SET EACH_SEC.{i}, sk_{i - 1};\n"))) +
            "    SET EACH_TICK.1, sk_16;\n"),
        TextureSave(
            "texture-values",
            "Round 15b. A SAVE texture with each valued property set to its own raw pattern 0x201 + 64·i: where is each in its record?",
            "TEXTURE sb_x1 { BMAPS grn_map; " +
            string.Concat(TextureValues.Select((f, i) => FormattableString.Invariant($"{f} {FieldValue(i)}; "))) + "FLAGS SAVE; }\n"),
        TextureSave(
            "texture-refs",
            "Round 15b. A SAVE texture with SOUND, ATTACH, TOUCH, FONT, IF_TOUCH, IF_RELEASE and IF_KLICK: which of the -1 slots holds which?",
            "TEXTURE sb_x1 { BMAPS grn_map; SOUND sb_snd; ATTACH mag_tex; TOUCH sb_str; FONT sb_font; " +
            "IF_TOUCH ev_near; IF_RELEASE ev_far; IF_KLICK ev_klick; FLAGS SAVE; }\n"),
        TextureSave(
            "texture-refs2",
            "Round 15b. The second SOUND, third FONT and fourth STRING declared, on a SAVE texture: which slot holds which number?",
            "SOUND sb_snd2,<" + AudioProbeCases.Quarter + ">;\n" + $"FONT sb_font2,<{UiLayoutProbeCases.FontFile}>,8,8;\n" +
            $"FONT sb_font3,<{UiLayoutProbeCases.FontFile}>,8,8;\n" + "STRING sb_str2, \"u\";\nSTRING sb_str3, \"v\";\nSTRING sb_str4, \"w\";\n" +
            "TEXTURE sb_x1 { BMAPS grn_map; SOUND sb_snd2; TOUCH sb_str4; FONT sb_font3; FLAGS SAVE; }\n"),
        TextureSave(
            "texture-two",
            "Round 15b. Two SAVE textures, AMBIENT 0.25 then 0.5, and a third without SAVE between them: in which order are they saved?",
            "TEXTURE sb_x1 { BMAPS grn_map; AMBIENT 0.25; FLAGS SAVE; }\nTEXTURE sb_x2 { BMAPS grn_map; AMBIENT 0.75; }\n" +
            "TEXTURE sb_x3 { BMAPS grn_map; AMBIENT 0.5; FLAGS SAVE; }\n"),
        TextureSave(
            "texture-anim",
            "Round 15b. A SAVE texture of three cycles on the wall sb_w, DELAY 1 each, saved a few ticks in: does a slot count its frame?",
            "TEXTURE sb_x1 { CYCLES 3; BMAPS grn_map, mag_map, yel_map; DELAY 1, 1, 1; FLAGS SAVE; }\n" +
            "WALL sb_xw { TEXTURE sb_x1; }\n"),
        RegionDeclared(
            "region-events",
            "Round 15. The SAVE room with every region event, each naming its own action, and GENIUS sb_t: which slot of the " +
            "region record holds which, and how is the GENIUS object named?",
            "FLAGS SAVE; IF_ENTER ev_near; IF_LEAVE ev_far; IF_DIVE ev_arr; IF_ARISE ev_hit; EACH_TICK ev_tick; EACH_CYCLE ev_cycle; GENIUS sb_t;"),
        .. RegionFlags.Select(static f => RegionDeclared(
            "region-flag-" + f.ToLowerInvariant(),
            $"Round 15. The SAVE room declared FLAGS SAVE, {f}: which bits of its record's flag words change?",
            "FLAGS SAVE, " + f + ";")),
        Declared(
            "events",
            "Round 13. Every event a wall takes, and every event a thing or actor takes but IF_KLICK, each naming its own action: " +
            "which slot of the records' event block holds which?",
            "IF_NEAR ev_near; IF_FAR ev_far; IF_HIT ev_hit; EACH_TICK ev_tick; EACH_CYCLE ev_cycle;",
            "IF_NEAR ev_near; IF_FAR ev_far; IF_ARRIVED ev_arr; IF_HIT ev_hit; EACH_TICK ev_tick; EACH_CYCLE ev_cycle;"),
        Declared(
            "events-klick",
            "Round 13. IF_KLICK on the thing and the actor: where does it go?",
            "",
            "IF_KLICK ev_klick;"),
        Declared(
            "unplaced",
            "Round 13. A thing, an actor and a wall declared but not placed, then ATTACH on the placed thing, actor and wall and " +
            "MAP_COLOR 0 on the wall: where do the unplaced objects go, and where are ATTACH and MAP_COLOR?",
            "ATTACH mag_tex; MAP_COLOR 0;",
            "ATTACH grn_tex;",
            "THING sb_u1 { TEXTURE mag_tex; }\nACTOR sb_u2 { TEXTURE yel_tex; }\nWALL sb_u3 { TEXTURE grn_tex; }\n"),
        .. ThingFlags.Union(WallFlags).Select(FlagSave),
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
