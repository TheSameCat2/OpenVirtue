// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Text;
using static ProximityProbeCases;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>border-climb</c> probe
/// (<c>docs/clean-room/observations/OV-20261019-border-climb.md</c>). OV-20261019-clip-contact saw
/// the player walk onto the pad island with its floor at 14 and at the ceiling, from a floor-0
/// room with the default <c>PLAYER_CLIMB</c>, through <c>PASSABLE</c> boundary walls. This probe asks
/// what stops the player from entering a neighbouring region: the pad's boundary walls are plain
/// (no flag) unless a case says otherwise, and each case walks into the pad several times with a
/// different <c>PLAYER_CLIMB</c>, <c>PLAYER_Z</c> (eye height) and <c>PLAYER_SIZE</c>. The walls carry
/// <c>IF_NEAR</c>/<c>IF_FAR</c> (<c>DIST 0</c>), so a blocked walk also shows whether pushing
/// against a region border fires <c>IF_NEAR</c>. Every trace line adds the player position,
/// <c>PLAYER_Z</c>, <c>FLOOR_HGT</c> and <c>HERE.SKILL1</c> (room 1, pad 2).
/// </summary>
internal static class BorderClimbProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVBCX0.TXT).</summary>
    public const string TraceFile = "ovbcx";

    /// <summary>A trace line with the position, eye height, floor height and region marker.</summary>
    private static string Line(string token) =>
        "    RULE pe_h = HERE.SKILL1;\n" + L(token, "PLAYER_X", "PLAYER_Y", "PLAYER_Z", "FLOOR_HGT", "pe_h");

    private static string Declarations(string flags) =>
        Skills +
        Action("bc_near", "    RULE pe_nn = pe_nn + 1;\n    RULE pe_d = MY.DISTANCE;\n    RULE pe_h = HERE.SKILL1;\n" + L("N", "pe_nn", "pe_d", "PLAYER_X", "PLAYER_Y", "PLAYER_Z", "pe_h")) +
        Action("bc_far", "    RULE pe_fn = pe_fn + 1;\n    RULE pe_d = MY.DISTANCE;\n    RULE pe_h = HERE.SKILL1;\n" + L("F", "pe_fn", "pe_d", "PLAYER_X", "PLAYER_Y", "PLAYER_Z", "pe_h")) +
        Action("bc_enter", Line("ENTER")) +
        Action("bc_leave", Line("LEAVE")) +
        "WALL bc_wall { TEXTURE red_tex; IF_NEAR bc_near; IF_FAR bc_far;" + (flags.Length > 0 ? $" FLAGS {flags};" : "") + " }\n" +
        Sampler("    RULE pe_h = HERE.SKILL1;\n", "pe_h", "PLAYER_Z");

    private static TraceProbeMap Map(int floor, int? ceiling = null) => new(
        RoomBody: "SKILL1 1;",
        PadBody: "SKILL1 2;\n    IF_ENTER bc_enter;\n    IF_LEAVE bc_leave;",
        PadWall: "bc_wall",
        PadFloor: floor,
        PadCeiling: ceiling);

    /// <summary>
    /// One walk: beam with <c>LOCATE</c> to (<paramref name="x"/>, <paramref name="y"/>), set
    /// <c>PLAYER_SIZE</c>, <c>PLAYER_Z</c> and <c>PLAYER_CLIMB</c>, stand 8 ticks, walk at
    /// (<paramref name="vx"/>, <paramref name="vy"/>) for <paramref name="ticks"/> ticks, stand 16,
    /// walk back as far, stand 8. The default walk starts at (−8, 6) and crosses the pad's south
    /// wall (y = 12) after 30 ticks, ending at y ≈ 16 inside the pad; a blocked walk pushes for
    /// about 26 ticks.
    /// </summary>
    private static string Probe(
        string tag,
        double climb,
        double z = 8,
        double size = 8,
        double x = -8,
        double y = 6,
        double vx = 0,
        double vy = 0.2,
        int ticks = 50)
    {
        var sb = new StringBuilder();
        sb.Append(Line(tag));
        sb.Append(CultureInfo.InvariantCulture, $"    SET PLAYER_X, {x};\n    SET PLAYER_Y, {y};\n    LOCATE;\n");
        sb.Append(CultureInfo.InvariantCulture, $"    SET PLAYER_SIZE, {size};\n    SET PLAYER_Z, {z};\n    SET PLAYER_CLIMB, {climb};\n");
        sb.Append(Line(tag + "D"));
        sb.Append("    WAITT 8;\n").Append(Line(tag + "S"));
        sb.Append(CultureInfo.InvariantCulture, $"    RULE pe_vx = {vx};\n    RULE pe_vy = {vy};\n    WAITT {ticks};\n");
        sb.Append("    RULE pe_vx = 0;\n    RULE pe_vy = 0;\n").Append(Line(tag + "W"));
        sb.Append("    WAITT 16;\n").Append(Line(tag + "T"));
        sb.Append(CultureInfo.InvariantCulture, $"    RULE pe_vx = {-vx};\n    RULE pe_vy = {-vy};\n    WAITT {ticks};\n");
        sb.Append("    RULE pe_vx = 0;\n    RULE pe_vy = 0;\n").Append(Line(tag + "B"));
        sb.Append("    WAITT 8;\n").Append(Line(tag + "E"));
        return sb.ToString();
    }

    private static string Run(params string[] probes) =>
        ArmSampler + Line("GO") + string.Concat(probes) + ClearSampler;

    /// <summary>A walk that starts inside the pad at (−8, 16) and walks out across its south wall to y ≈ 6.</summary>
    private static string ProbeOut(string tag, double climb, double z) =>
        Probe(tag, climb, z, y: 16, vy: -0.2);

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        new TraceProbeCase(
            "climb-1",
            "Plain (not PASSABLE) pad walls, pad floor 1: does PLAYER_CLIMB stop the player, and is the step measured from the room's floor or from the player's feet (PLAYER_Z − PLAYER_SIZE)?",
            Declarations(""),
            Run(
                Probe("A", 0.5), Probe("B", 0.9), Probe("C", 1.1), Probe("D", 1.5),
                Probe("E", 0.5, z: 9.5)),
            Predict(
                ("step from the floor ≤ PLAYER_CLIMB", "A, B blocked (stop at y ≈ 10.8, no ENTER); C, D enter; E blocked (floor step 1 > 0.5)"),
                ("step from the feet ≤ PLAYER_CLIMB", "A, B blocked; C, D enter; E enters (feet at 1.5, above the pad floor)"),
                ("plain region borders never block", "ENTER in every walk"),
                ("contact with a blocking border fires IF_NEAR every cycle", "N in each pushing cycle of A, B")),
            Map: Map(1)),
        new TraceProbeCase(
            "climb-2",
            "Pad floor 2: is the PLAYER_CLIMB limit inclusive (1.9 vs 2.1), and does raising PLAYER_Z lower the step?",
            Declarations(""),
            Run(
                Probe("A", 1.5), Probe("B", 1.9), Probe("C", 2.1),
                Probe("D", 1.5, z: 9), Probe("E", 0.5, z: 9.6)),
            Predict(
                ("step from the floor", "A, B blocked; C enters; D, E blocked"),
                ("step from the feet", "A, B blocked; C enters; D enters (step 1); E enters (step 0.4)")),
            Map: Map(2)),
        new TraceProbeCase(
            "climb-4",
            "Pad floor 4: the PLAYER_CLIMB limit at 3.9 vs 4.1, and PLAYER_Z 10 vs 11 (feet 2 or 3 steps up) with the default climb.",
            Declarations(""),
            Run(
                Probe("A", 1.5), Probe("B", 3.9), Probe("C", 4.1),
                Probe("D", 1.5, z: 11), Probe("E", 1.5, z: 10)),
            Predict(
                ("step from the floor", "A, B blocked; C enters; D, E blocked"),
                ("step from the feet", "A, B blocked; C enters; D enters (step 1); E blocked (step 2)")),
            Map: Map(4)),
        new TraceProbeCase(
            "climb-14",
            "Pad floor 14, 2 below the ceiling (headroom 2 < PLAYER_SIZE 8): blocked by the climb, by the headroom, or by both? D has PLAYER_SIZE 1.",
            Declarations(""),
            Run(
                Probe("A", 1.5), Probe("B", 13.9), Probe("C", 14.1),
                Probe("D", 14.1, z: 1, size: 1)),
            Predict(
                ("climb only", "A, B blocked; C, D enter"),
                ("climb and headroom ≥ PLAYER_SIZE", "A, B, C blocked; D enters"),
                ("climb and headroom ≥ some fixed minimum above 2", "A–D blocked")),
            Map: Map(14)),
        new TraceProbeCase(
            "ceil-4",
            "Pad floor 0, ceiling 4 (the room's is 16): does a low ceiling block the player, by PLAYER_SIZE (headroom) or by the eye height PLAYER_Z?",
            Declarations(""),
            Run(
                Probe("A", 1.5), Probe("B", 1.5, z: 3, size: 3), Probe("C", 1.5, z: 8, size: 3),
                Probe("D", 1.5, z: 5, size: 5)),
            Predict(
                ("headroom ≥ PLAYER_SIZE", "A, D blocked; B, C enter"),
                ("eye below the pad's ceiling", "A, C, D blocked; B enters"),
                ("ceilings never block", "ENTER in every walk")),
            Map: Map(0, ceiling: 4)),
        new TraceProbeCase(
            "ceil-0",
            "Pad floor 0 and ceiling 0 (closed, no step): does zero headroom block on its own, even with a large PLAYER_CLIMB or a tiny PLAYER_SIZE?",
            Declarations(""),
            Run(Probe("A", 1.5), Probe("B", 20), Probe("C", 1.5, z: 0.5, size: 0.5)),
            Predict(
                ("zero headroom blocks", "A, B, C blocked"),
                ("headroom ≥ PLAYER_SIZE", "A, B blocked; C blocked (0 < 0.5)"),
                ("only the floor step counts", "ENTER in every walk")),
            Map: Map(0, ceiling: 0)),
        new TraceProbeCase(
            "step-down",
            "Pad floor −4: is stepping down always allowed, where does PLAYER_Z go, and can the player step back up 4 to the room? C–E start inside the pad with PLAYER_Z 4 (feet on the pad floor) or 8 (feet level with the room).",
            Declarations(""),
            Run(
                Probe("A", 1.5), Probe("B", 0.5),
                ProbeOut("C", 1.5, z: 4), ProbeOut("D", 4.5, z: 4), ProbeOut("E", 0.5, z: 8)),
            Predict(
                ("down is free, up from the floor", "A, B enter, then are held in the pad on the way back; C blocked; D leaves; E blocked"),
                ("down is free, up from the feet", "A, B enter and leave (PLAYER_Z stays 8); C blocked; D leaves; E leaves")),
            Map: Map(-4)),
        new TraceProbeCase(
            "pass-4",
            "The same pad floor 4 behind PASSABLE walls (the clip-contact layout): does PASSABLE let the player past a step above PLAYER_CLIMB?",
            Declarations("PASSABLE"),
            Run(Probe("A", 0.5), Probe("B", 1.5)),
            Predict(
                ("PASSABLE skips the height test", "A, B enter"),
                ("PASSABLE changes nothing for region borders", "A, B blocked")),
            Map: Map(4)),
        new TraceProbeCase(
            "pad-solid",
            "Plain pad walls with the pad's floor and ceiling at 16, like the room's solid border region. Then O walks from the origin into the room's back wall (x = −16, the solid region behind) with PLAYER_CLIMB 20: why does the room's outer wall hold?",
            Declarations(""),
            Run(
                Probe("A", 1.5), Probe("B", 20), Probe("C", 20, z: 0.5, size: 0.5),
                Probe("O", 20, x: 0, y: 0, vx: -0.2, vy: 0, ticks: 100)),
            Predict(
                ("zero headroom blocks", "A, B, C blocked; O stops at x ≈ −14.8"),
                ("the floor step alone", "A blocked; B, C enter; O passes x = −16")),
            Map: Map(16, ceiling: 16)),
        new TraceProbeCase(
            "eye-10",
            "Round 2. climb-14 blocked the step onto floor 14 with feet 0 and PLAYER_CLIMB 14.1, even with PLAYER_SIZE 1, and pad-solid held with climb 20. Pad floor 10, ceiling 16: must the eye (PLAYER_Z) be above the new floor, or must the body (new floor + PLAYER_SIZE) fit under the ceiling? And is the eye compared with the new ceiling?",
            Declarations(""),
            Run(
                Probe("A", 7.5, z: 11), Probe("B", 1.5, z: 10.5, size: 1), Probe("C", 1.5, z: 9.9, size: 1),
                Probe("D", 7, z: 9.9, size: 4), Probe("E", 2.5, z: 15.9), Probe("F", 2.5, z: 16.1)),
            Predict(
                ("feet + climb ≥ floor, floor < eye < ceiling", "A, B, E enter; C, D, F blocked"),
                ("feet + climb ≥ floor, new floor + PLAYER_SIZE ≤ ceiling, eye < ceiling", "B, C, D enter; A, E, F blocked (new floor + 8 = 18 > 16)")),
            Map: Map(10)),
        new TraceProbeCase(
            "ceil-4b",
            "Round 2. ceil-4 blocked eye 8 and 5 under a ceiling of 4 and let eye 3 in. The margin (eye 3.9 vs 4.1 with PLAYER_SIZE 3), and PLAYER_SIZE 5 above the headroom 4 with the eye at 3.5: is the headroom compared with PLAYER_SIZE at all?",
            Declarations(""),
            Run(Probe("A", 1.5, z: 3.9, size: 3), Probe("B", 1.5, z: 4.1, size: 3), Probe("C", 1.5, z: 3.5, size: 5)),
            Predict(
                ("eye < ceiling only", "A, C enter; B blocked"),
                ("eye < ceiling and headroom ≥ PLAYER_SIZE", "A enters; B, C blocked")),
            Map: Map(0, ceiling: 4)),
        new TraceProbeCase(
            "pass-10",
            "Round 2. PASSABLE walls, pad floor 10, ceiling 16: where does PLAYER_Z go when a PASSABLE crossing puts the eye below the new floor (border-ceil read 15.650 on a floor-16 pad)?",
            Declarations("PASSABLE"),
            Run(Probe("A", 1.5), Probe("B", 1.5, z: 8, size: 3), Probe("C", 1.5, z: 12)),
            Predict(
                ("the eye is lifted to the new floor plus PLAYER_SIZE, capped below the ceiling", "A: 15.65; B: 13; C: 12 (unchanged)"),
                ("the eye is lifted just above the floor", "A, B: about 10.35; C: 12"),
                ("the eye stays", "A, B: 8; C: 12")),
            Map: Map(10)),
        new TraceProbeCase(
            "edge-1",
            "Round 3. Pad floor 1: is a step equal to PLAYER_CLIMB allowed (A: step 1, climb 1), and how far above the new floor must the eye be (B–D: PLAYER_SIZE 1, eye 0.1, 0.3, 0.4 above the floor)?",
            Declarations(""),
            Run(
                Probe("A", 1, z: 8), Probe("B", 1.5, z: 1.1, size: 1), Probe("C", 1.5, z: 1.3, size: 1),
                Probe("D", 1.5, z: 1.4, size: 1)),
            Predict(
                ("step < PLAYER_CLIMB, eye > floor", "A blocked; B, C, D enter"),
                ("step ≤ PLAYER_CLIMB", "A enters"),
                ("eye ≥ floor + 0.35 (the floor clamp)", "B, C blocked; D enters")),
            Map: Map(1)),
        new TraceProbeCase(
            "ceil-4c",
            "Round 3. ceil-4b blocked eye 3.5 with PLAYER_SIZE 5 under a ceiling of 4, where the step from the feet (−1.5) equals PLAYER_CLIMB 1.5. A: step 1.4; B: climb 1.6; C: PLAYER_SIZE 3 (feet 0.5). Is it the step, or the size against the headroom?",
            Declarations(""),
            Run(Probe("A", 1.5, z: 3.6, size: 5), Probe("B", 1.6, z: 3.5, size: 5), Probe("C", 1.5, z: 3.5, size: 3)),
            Predict(
                ("the step (strict)", "A, B, C enter"),
                ("PLAYER_SIZE against the headroom", "A, B blocked; C enters")),
            Map: Map(0, ceiling: 4)),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "border-climb",
        TraceFile,
        "ovbcp",
        "After the skill dump, `IF_START` selects the trace file, writes `BEGIN` and runs the variant's case.\n" +
        "Each trace line is a token followed by `TICKS` and `pe_n` (the global EACH_TICK sampler's run count)\n" +
        "when it was printed, then PLAYER_X, PLAYER_Y, PLAYER_Z, FLOOR_HGT and HERE.SKILL1 (room 1, pad 2).\n" +
        "A walk `A` prints `A` (before the beam), `AD` (after it), `AS`, `AW` (end of the walk in), `AT`,\n" +
        "`AB` (end of the walk back) and `AE`. `N`/`F` are the pad walls' IF_NEAR/IF_FAR (count, MY.DISTANCE,\n" +
        "position, PLAYER_Z, region). Compare the trace with `case.json` → `predictions`, and check\n" +
        "`OVCON.TXT` for compile errors.",
        All);
}
