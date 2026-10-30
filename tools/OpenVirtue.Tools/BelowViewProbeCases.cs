// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>below-view</c> probe: whether the original's <c>#VISIBLE</c> flag (p.53, p.70)
/// sees into and past a <c>BELOW</c> stack (p.59–61) the way the storeys' geometry says. The pad
/// is a bridge: deck floor 10, ceiling 16, with an underpass <c>bv_low</c> (floor 0, ceiling 8)
/// stacked under it, bounded by visible passable walls. The thing <c>bv_lo</c> has <c>GROUND</c>
/// and height 0, so it starts in the underpass (p.61); <c>bv_hi</c> stands on the deck. Each
/// case puts the eye somewhere, waits, and prints both things' <c>VISIBLE</c> as 1 or 0.
/// </summary>
internal static class BelowViewProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVBLV0.TXT).</summary>
    public const string TraceFile = "ovblv";

    private const string Declarations =
        "SYNONYM bv_sl { TYPE THING; }\nSYNONYM bv_sh { TYPE THING; }\n" +
        "SKILL bv_vl { VAL 0; }\nSKILL bv_vh { VAL 0; }\n" +
        "WALL bv_wall { TEXTURE grn_tex; FLAGS PASSABLE; }\n" +
        "THING bv_lo { TEXTURE mag_tex; FLAGS GROUND; HEIGHT 0; }\n" +
        "THING bv_hi { TEXTURE yel_tex; }\n";

    private static readonly TraceProbeMap Bridge = new(
        PadBody: "BELOW bv_low;",
        PadWall: "bv_wall",
        PadFloor: 10,
        Textures: "REGION bv_low { FLOOR_HGT 0; CEIL_HGT 8; FLOOR_TEX flr_tex; CEIL_TEX cel_tex; }\n",
        Placements: ["THING bv_lo -8 17 0 2", "THING bv_hi -6 18 0 2"]);

    private const string Read =
        "    IF (bv_sl.VISIBLE == 1) { RULE bv_vl = 1; } ELSE { RULE bv_vl = 0; }\n" +
        "    IF (bv_sh.VISIBLE == 1) { RULE bv_vh = 1; } ELSE { RULE bv_vh = 0; }\n";

    private static TraceProbeCase View(string label, string question, string place) => new(
        label,
        question,
        Declarations,
        "    SET bv_sl, bv_lo;\n    SET bv_sh, bv_hi;\n" + place + "    WAIT 3;\n" + Read +
        V("V", "PLAYER_X", "PLAYER_Y", "PLAYER_Z", "bv_vl", "bv_vh", "bv_sl.Z", "bv_sh.Z"),
        Predict(
            ("storeys see into the underpass", "from the room at eye 4: bv_vl 1"),
            ("the slab hides the underpass from the deck", "from the deck: bv_vl 0, bv_vh 1")),
        Map: Bridge);

    private static string Place(double x, double y, double eye, double angle) => FormattableString.Invariant(
        $"    SET PLAYER_X, {x};\n    SET PLAYER_Y, {y};\n    SET PLAYER_SIZE, 4;\n    SET PLAYER_Z, {eye};\n    SET PLAYER_ANGLE, {angle};\n    LOCATE;\n");

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        View("room-low", "From the room at (-8, 2), eye 4, facing the bridge (+y): is the underpass thing VISIBLE, and the deck thing?", Place(-8, 2, 4, 1.5708)),
        View("room-high", "From the room at (-8, 2), eye 15, facing the bridge: the deck thing over the edge, the underpass thing under the slab?", Place(-8, 2, 15, 1.5708)),
        View("deck", "On the deck at (-8, 13), eye 14, facing +y: the slab between the eye and the underpass thing.", Place(-8, 13, 14, 1.5708)),
        View("away", "In the room at (-8, 2), eye 4, facing away (-y): both 0 (control).", Place(-8, 2, 4, -1.5708)),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "below-view",
        TraceFile,
        "ovbls",
        "`V` lines hold the player's position and eye, the underpass thing's and the deck thing's VISIBLE\n" +
        "(1 or 0) and both things' Z.",
        All);
}
