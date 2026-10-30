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

    private static readonly TraceProbeMap Bridge = new(
        RoomBody: "SKILL1 1;",
        PadBody: "BELOW bb_low; SKILL1 2;",
        PadWall: "bb_wall",
        PadFloor: 10,
        Textures: "REGION bb_low { FLOOR_HGT 0; CEIL_HGT 8; FLOOR_TEX flr_tex; CEIL_TEX cel_tex; SKILL1 3; }\n");

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
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "below-beam",
        TraceFile,
        "ovbbs",
        "`B0`/`B1` lines before and after the beam, then `C` each cycle: `TICKS`, the cycle, the player's\n" +
        "X, Y, Z, `PLAYER_HGT` and the region's `SKILL1` (room 1, deck 2, underpass 3).",
        All);
}
