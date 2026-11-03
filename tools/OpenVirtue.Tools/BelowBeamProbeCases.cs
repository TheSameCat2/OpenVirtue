// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>below-beam</c> probe: where the original puts a player beamed into a
/// <c>BELOW</c> stack (p.59–61). The bridge is below-view's: the pad (x −12…−4, y 12…20) is a deck
/// with floor 10 and ceiling 16 over an underpass <c>bb_low</c> (floor 0, ceiling 8). The room, the
/// deck and the underpass carry <c>SKILL1</c> 1, 2 and 3. Each case beams the player to (−8, 16) with
/// <c>PLAYER_SIZE</c> 4 and an eye height, with or without <c>LOCATE</c>, then prints the position,
/// <c>PLAYER_Z</c>, <c>PLAYER_HGT</c> and the region's <c>SKILL1</c> for twelve cycles.
/// </summary>
internal static class BelowBeamProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVBBM0.TXT).</summary>
    public const string TraceFile = "ovbbm";

    private const string Declarations =
        "SKILL bb_i { VAL 0; }\nSKILL bb_h { VAL 0; }\n" +
        "WALL bb_wall { TEXTURE grn_tex; FLAGS PASSABLE; }\n";

    // Round 2: a thing and an actor in the room, each GROUND (absolute HEIGHT) or not, read back
    // through a REGION synonym as the retail scripts do (SET curRegion, MY.REGION).
    private const string ObjectDeclarations = Declarations +
        "SYNONYM bb_r { TYPE REGION; }\n" +
        "THING bb_t { TEXTURE mag_tex; }\nTHING bb_tg { TEXTURE mag_tex; FLAGS GROUND; }\n" +
        "ACTOR bb_a { TEXTURE yel_tex; }\nACTOR bb_ag { TEXTURE yel_tex; FLAGS GROUND; }\n";

    private static readonly TraceProbeMap Bridge = new(
        RoomBody: "SKILL1 1;",
        PadBody: "BELOW bb_low; SKILL1 2;",
        PadWall: "bb_wall",
        PadFloor: 10,
        Textures: "REGION bb_low { FLOOR_HGT 0; CEIL_HGT 8; FLOOR_TEX flr_tex; CEIL_TEX cel_tex; SKILL1 3; }\n");

    private static readonly TraceProbeMap ObjectBridge = Bridge with
    {
        Placements = ["THING bb_t 8 6 0 0", "THING bb_tg 8 2 0 0", "ACTOR bb_a 8 -2 0 0", "ACTOR bb_ag 8 -6 0 0"],
    };

    private static string ObjectSample(string token, string target) =>
        FormattableString.Invariant($"    SET bb_r, {target}.REGION;\n    RULE bb_h = bb_r.SKILL1;\n") +
        V(token, "TICKS", "bb_i", $"{target}.X", $"{target}.Y", $"{target}.HEIGHT", "bb_h");

    private static TraceProbeCase Object(string label, string question, string target, double height, bool locate) => new(
        label,
        question,
        ObjectDeclarations,
        ObjectSample("B0", target) +
        FormattableString.Invariant(
            $"    SET {target}.X, -8;\n    SET {target}.Y, 16;\n    SET {target}.HEIGHT, {height};\n") +
        (locate ? $"    LOCATE {target};\n" : "") +
        ObjectSample("B1", target) +
        "    WHILE (bb_i < 12) {\n        RULE bb_i = bb_i + 1;\n        WAIT 1;\n" + Indent(ObjectSample("C", target)) + "    }\n",
        Predict(
            ("the storey holding its feet", "GROUND 4: underpass (3); GROUND 12: deck (2); relative 0: ?"),
            ("always the top", "every LOCATE ends on the deck (2)"),
            ("no LOCATE keeps the room", "without LOCATE the region stays the room (1)")),
        Map: ObjectBridge);

    private static string Sample(string token) =>
        "    RULE bb_h = HERE.SKILL1;\n" + V(token, "TICKS", "bb_i", "PLAYER_X", "PLAYER_Y", "PLAYER_Z", "PLAYER_HGT", "bb_h");

    private static TraceProbeCase Beam(string label, string question, double eye, bool locate) => new(
        label,
        question,
        Declarations,
        Sample("B0") +
        FormattableString.Invariant(
            $"    SET PLAYER_X, -8;\n    SET PLAYER_Y, 16;\n    SET PLAYER_SIZE, 4;\n    SET PLAYER_Z, {eye};\n") +
        (locate ? "    LOCATE;\n" : "") +
        Sample("B1") +
        "    WHILE (bb_i < 12) {\n        RULE bb_i = bb_i + 1;\n        WAIT 1;\n" + Indent(Sample("C")) + "    }\n",
        Predict(
            ("the storey holding the eye", "eye 4: underpass (3), Z stays; eye 14: deck (2)"),
            ("always the top", "every case ends on the deck (2), Z raised to 14")),
        Map: Bridge);

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Beam("low-locate", "Beamed to (-8, 16) with eye 4 (in the underpass) and LOCATE: which storey, and does Z change?", 4, true),
        Beam("high-locate", "Beamed with eye 14 (on the deck) and LOCATE.", 14, true),
        Beam("mid-locate", "Beamed with eye 9 (above the underpass's ceiling, below the deck) and LOCATE.", 9, true),
        Beam("low-nolocate", "Beamed with eye 4 and no LOCATE: does the region follow the next movement?", 4, false),
        Beam("high-nolocate", "Beamed with eye 14 and no LOCATE.", 14, false),
        Object("thing-ground-4", "A GROUND thing beamed to (-8, 16) at HEIGHT 4 (inside the underpass) and LOCATEd: which storey?", "bb_tg", 4, true),
        Object("thing-ground-12", "A GROUND thing beamed at HEIGHT 12 (on the deck) and LOCATEd.", "bb_tg", 12, true),
        Object("thing-rel-0", "A thing with relative HEIGHT 0 beamed and LOCATEd: which storey's floor does it stand on?", "bb_t", 0, true),
        Object("actor-rel-0", "An actor with relative HEIGHT 0 beamed and LOCATEd.", "bb_a", 0, true),
        Object("actor-ground-4", "A GROUND actor beamed at HEIGHT 4 and LOCATEd.", "bb_ag", 4, true),
        Object("thing-ground-12-nolocate", "A GROUND thing beamed at HEIGHT 12 without LOCATE: does its region stay the room?", "bb_tg", 12, false),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "below-beam",
        TraceFile,
        "ovbbs",
        "`B0`/`B1` lines before and after the beam, then `C` each cycle: `TICKS`, the cycle, the player's\n" +
        "X, Y, Z, `PLAYER_HGT` and the region's `SKILL1` (room 1, deck 2, underpass 3). The object cases\n" +
        "(round 2) print the object's X, Y and `HEIGHT` in place of the player's three, and its region's `SKILL1`.",
        All);
}
