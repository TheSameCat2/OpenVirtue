// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>wall-slide</c> probe: how the player slides along a solid wall it walks into at
/// an angle. Apathy's walk slid along a diagonal wall with about the whole step projected onto it, held
/// 1484 to 1486/1024 from the wall with <c>PLAYER_WIDTH</c> 1.4, where OpenVirtue's slide came out a
/// unit short and its hold a unit nearer (OV-20261110-retail-play). Each case puts one free-standing
/// wall with whole-step vertices across the player's path from the origin and walks the player into
/// it with a fixed step a cycle: a global <c>EACH_TICK</c> sampler sets <c>PLAYER_VX</c> and
/// <c>PLAYER_VY</c> to the step over <c>TIME_CORR</c> every cycle once <c>GO</c> is printed and prints
/// <c>TICKS</c>, a cycle counter, <c>TIME_CORR</c> and the position × 1024.
/// </summary>
internal static class WallSlideProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVWSL0.TXT).</summary>
    public const string TraceFile = "ovwsl";

    private const string Skills =
        "SKILL ws_n { VAL 0; }\nSKILL ws_go { VAL 0; }\nSKILL ws_sx { VAL 0; }\nSKILL ws_sy { VAL 0; }\n" +
        "SKILL ws_vx { VAL 0; }\nSKILL ws_vy { VAL 0; }\nSKILL ws_x { VAL 0; }\nSKILL ws_y { VAL 0; }\n" +
        "WALL ws_wall { TEXTURE red_tex; }\n";

    private static readonly string Sampler = Action(
        "ws_tick",
        "    RULE ws_n = ws_n + 1;\n" +
        "    IF (ws_go > 0) {\n        RULE ws_vx = ws_sx / TIME_CORR;\n        RULE ws_vy = ws_sy / TIME_CORR;\n" +
        "        SET PLAYER_VX, ws_vx;\n        SET PLAYER_VY, ws_vy;\n    }\n" +
        "    IF (ws_n < 400) {\n" +
        Indent("    RULE ws_x = PLAYER_X * 1024;\n    RULE ws_y = PLAYER_Y * 1024;\n" + V("Y", "TICKS", "ws_n", "TIME_CORR", "ws_x", "ws_y")) +
        "    }\n");

    private static readonly string Prelude =
        "    PRINTFILE \"" + TraceFile + "\", 0;\n" + V("S0", "TICKS", "ws_n", "TIME_CORR") + "    SET EACH_TICK.1, ws_tick;\n";

    private static TraceProbeCase Slide(string label, (double X1, double Y1, double X2, double Y2) wall, double sx, double sy, string question, double? width = null, double offsetX = 0, double offsetY = 0, string setup = "") => new(
        label,
        question,
        Skills + Sampler,
        (width is { } w ? string.Create(CultureInfo.InvariantCulture, $"    SET PLAYER_WIDTH, {w};\n") : "") + setup +
        string.Create(CultureInfo.InvariantCulture, $"    RULE ws_sx = {sx};\n    RULE ws_sy = {sy};\n    RULE ws_go = 1;\n") +
        V("GO", "TICKS", "ws_n") +
        "    WAIT 150;\n    RULE ws_go = 0;\n    SET PLAYER_VX, 0;\n    SET PLAYER_VY, 0;\n" + P("RET"),
        Predict(
            ("the whole step projected onto the wall, held at PLAYER_WIDTH + 51/1024", "each cycle moves by the step's part along the wall, the distance stays at the hold"),
            ("the step cut at contact, then the rest slid", "the first cycle at the wall moves less along it than the later ones")),
        Map: new TraceProbeMap(Walls: [new TraceProbeWall("ws_wall", wall.X1, wall.Y1, wall.X2, wall.Y2)], OffsetX: offsetX, OffsetY: offsetY),
        Prelude: Prelude);

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Slide("w45-s02", (4, -8, 20, 8), 0.2, 0,
            "A wall at 45° (y = x − 12) across the player's +x walk at 0.2 a cycle: where is the player each cycle once it meets the wall?"),
        Slide("w45-s05", (4, -8, 20, 8), 0.5, 0, "The 45° wall at 0.5 a cycle."),
        Slide("w27-s02", (-4, -12, 28, 4), 0.2, 0, "A wall of slope 1/2 (y = x/2 − 10) at 0.2 a cycle."),
        Slide("w63-s02", (8, -8, 16, 8), 0.2, 0, "A wall of slope 2 (y = 2x − 24) at 0.2 a cycle."),
        Slide("w37-s03", (0, -9, 24, 9), 0.3, 0, "A wall of slope 3/4 (y = 3x/4 − 9) at 0.3 a cycle."),
        Slide("w45-s02-w14", (4, -8, 20, 8), 0.2, 0, "The 45° wall at 0.2 a cycle with PLAYER_WIDTH 1.4, as Apathy sets it.", 1.4),
        // Round 2: corners off the 1/1024 grid (4.154 is 4253.696 units, −7.9 is −8089.6), so a corner
        // rounded, cut toward zero or floored lands on a different unit (Apathy's and Heart's walls).
        Slide("round2/f45-s02", (4.154, -7.9, 20.154, 8.1), 0.2, 0,
            "Round 2. The 45° wall with corners off the 1/1024 grid: rounded, cut toward zero or floored to 1/1024?"),
        Slide("round2/f37-s03", (0.154, -9.1, 24.154, 8.9), 0.3, 0,
            "Round 2. A 3:4 wall with corners off the 1/1024 grid, at 0.3 a cycle."),
        Slide("round2/f45-s02-w14", (4.154, -7.9, 20.154, 8.1), 0.2, 0,
            "Round 2. The off-grid 45° wall with PLAYER_WIDTH 1.4.", 1.4),
        // Round 3: a long wall. Legalism's player stopped 2/1024 nearer a 70-step maze wall than the exact normal
        // holds it, as if the wall's length came out 0.04 to 0.1 % short; 70825² overflows 32 bits. The same line,
        // y = x/5 − 2, met at x ≈ 8 and slid along for the rest of the walk; 61 steps long from either end, and 20
        // steps long as the control.
        Slide("round3/long-a", (-15, -5, 45, 7), 0.2, 0,
            "Round 3. A 61-step wall of slope 1/5 met 23 steps from its first vertex: held as the exact normal holds it?", 1.4),
        Slide("round3/long-b", (45, 7, -15, -5), 0.2, 0,
            "Round 3. The long wall with its vertices swapped, so the first vertex is the near one.", 1.4),
        Slide("round3/short", (5, -1, 25, 3), 0.2, 0,
            "Round 3. The control: 20 steps of the same line.", 1.4),
        // Round 4: Legalism's maze lies 1226 steps out, where a product of absolute coordinates passes 32 bits, and its
        // first contact came out a unit from the hold that fits every probe near the origin. The same walls, moved out.
        Slide("round4/w45-far", (4, -8, 20, 8), 0.2, 0, "Round 4. The 45° wall with the whole map moved by (−1200, 950).", 1.4, -1200, 950),
        Slide("round4/w27-far", (-4, -12, 28, 4), 0.2, 0, "Round 4. The slope-1/2 wall moved by (−1200, 950).", 1.4, -1200, 950),
        Slide("round4/w37-far", (0, -9, 24, 9), 0.3, 0, "Round 4. The 3:4 wall moved by (−1200, 950).", 1.4, -1200, 950),
        Slide("round4/w45-mid", (4, -8, 20, 8), 0.2, 0, "Round 4. The 45° wall moved by (−300, 200).", 1.4, -300, 200),
        // Round 5: Legalism's first stop itself. Its maze wall's corners as the map gives them, (−1195.154, 944.359) and
        // (−1264.319, 957.931) (the second outside the room), the player beamed to where that walk stood one cycle before
        // (−1256010 and 971873 raw), stepping 243 raw a cycle along x as it did.
        Slide("round5/legalism-stop", (30.846, -4.641, -38.319, 8.931), 243.0 / 1024, 0,
            "Round 5. Legalism's first wall stop rebuilt: does the step from (−1256010, 971873) end at (−1255787, 971780) as there, or a unit off?",
            1.4, -1226, 949, "    SET PLAYER_X, -1226.572265625;\n    SET PLAYER_Y, 949.0947265625;\n    WAIT 2;\n"),
        .. new[] { ("up", 20), ("down", -20) }.Select(static v => Slide($"round5/legalism-stop-{v.Item1}", (30.846, -4.641, -38.319, 8.931), 243.0 / 1024, v.Item2 / 1024.0,
            $"Round 5. Legalism's stop with a y step of {v.Item2} raw, so the 4/1024 minimum plays no part: is y still a unit above OpenVirtue's hold?",
            1.4, -1226, 949, "    SET PLAYER_X, -1226.572265625;\n    SET PLAYER_Y, 949.0947265625;\n    WAIT 2;\n")),
        // Round 9: the hold's bias across wall directions. Walls through (8, 6) of slope k from −0.9 to 0.9, the player
        // beamed 6 below the wall at (8, 0) and stepping (0.1, 0.25) a cycle into it, so the push's larger part is
        // negative (Legalism's wall took 0.15/1024 there, Apathy's curtain none).
        .. new[] { -0.9, -0.7, -0.5, -0.3, -0.1, 0.1, 0.3, 0.5, 0.7, 0.9 }.Select(static k => Slide(
            FormattableString.Invariant($"round9/k{(k < 0 ? "m" : "p")}{Math.Abs(k) * 10:0}"),
            (-10, 6 - (18 * k), 26, 6 + (18 * k)), 0.1, 0.25,
            FormattableString.Invariant($"Round 9. A wall of slope {k} through (8, 6), met from below: how is the push's negative larger part rounded?"),
            1.4, setup: "    SET PLAYER_X, 8;\n    SET PLAYER_Y, 0;\n    WAIT 2;\n")),
        // Round 10: Apathy's curtain took no bias where Legalism's wall did; its steps ran −x. Round 9's walls stepped
        // (−0.1, 0.25): does the bias follow the step's x sign or the push's?
        .. new[] { -0.9, -0.5, 0.5, 0.9 }.Select(static k => Slide(
            FormattableString.Invariant($"round10/k{(k < 0 ? "m" : "p")}{Math.Abs(k) * 10:0}n"),
            (-10, 6 - (18 * k), 26, 6 + (18 * k)), -0.1, 0.25,
            FormattableString.Invariant($"Round 10. The slope-{k} wall stepped (−0.1, 0.25): is the push's negative larger part still biased?"),
            1.4, setup: "    SET PLAYER_X, 8;\n    SET PLAYER_Y, 0;\n    WAIT 2;\n")),
        // Round 11: Apathy's curtain, from (−1.447, 42.397) to (−12, 32.5), rebuilt with the map moved by (0, 32) so it
        // lies in the probe room, the player beamed to where Apathy's walk stood a cycle before its slide (−5220, 37656 raw)
        // and stepping along the curtain at four lengths in Apathy's direction (−180, 156 raw a cycle) and two others.
        .. new (string Label, int X, int Y)[] { ("c180", -180, 156), ("c90", -90, 78), ("c240", -240, 208), ("c45", -45, 39), ("c-steep", -120, 200), ("c-flat", -200, 100) }
            .Select(static v => Slide($"round11/{v.Label}", (-1.447, 10.397, -12, 0.5), v.X / 1024.0, v.Y / 1024.0,
                $"Round 11. Apathy's curtain stepped ({v.X}, {v.Y}) raw a cycle: does the push's negative larger part take the bias?",
                1.4, 0, 32, "    SET PLAYER_X, -5.09765625;\n    SET PLAYER_Y, 36.7734375;\n    WAIT 2;\n")),
        // Round 8: Legalism's walk took the hold's y part floored at x steps of 273, where its stop rebuilt in a probe at
        // 243 wanted it cut toward zero (round 5). The same stop, the x step varied, y left to the 4/1024 minimum.
        .. new[] { 213, 258, 273, 303 }.Select(static sx => Slide($"round8/legalism-x{sx}", (30.846, -4.641, -38.319, 8.931), sx / 1024.0, 0,
            $"Round 8. Legalism's stop with an x step of {sx} raw: is the hold's y part floored or cut toward zero?",
            1.4, -1226, 949, "    SET PLAYER_X, -1226.572265625;\n    SET PLAYER_Y, 949.0947265625;\n    WAIT 2;\n")),
        // Round 6: a step that starts inside the hold. Apathy's player, jumping over a border it then could not climb,
        // started 398/1024 from the wall's line (PLAYER_WIDTH 1.4) and was put back at the hold with half its x step
        // (−72 of −144; OV-20261110-retail-play apathy-keys3 line 1018). A free wall along y = 4, the player beamed
        // 0.4 below it while it already walks with the case's step, so the step after the beam is not the zero first one.
        .. new[]
        {
            ("in-a", -144, 428), ("in-b", -144, 0), ("in-c", 200, 100), ("in-d", -300, 428), ("in-e", 0, 428), ("in-f", -144, -20),
        }.Select(static v => Slide($"round6/{v.Item1}", (-20, 4, 20, 4), v.Item2 / 1024.0, v.Item3 / 1024.0,
            $"Round 6. Beamed 0.4 inside a wall's hold, stepping ({v.Item2}, {v.Item3}) raw a cycle: where does the first cycle put the player, and how much of the x step does it keep?",
            1.4, setup: FormattableString.Invariant(
                $"    SET PLAYER_X, 2;\n    SET PLAYER_Y, 2;\n    WAIT 2;\n    RULE ws_sx = {v.Item2 / 1024.0};\n    RULE ws_sy = {v.Item3 / 1024.0};\n    RULE ws_go = 1;\n    WAIT 3;\n    SET PLAYER_X, 2;\n    SET PLAYER_Y, 3.6;\n"))),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "wall-slide",
        TraceFile,
        "ovwsp",
        "After the skill dump, `IF_START` selects the trace file, arms the sampler and runs the case. Each `Y`\n" +
        "line is `TICKS`, the cycle, `TIME_CORR` and the player's x and y × 1024; `GO` marks the start of the\n" +
        "walk and `RET` its end. The wall's vertices are in `work/TITLE.WMP`.",
        All);
}
