// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Text;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>shade-global</c> probe: the coefficient the global <c>AMBIENT</c> skill
/// takes in the shade level. OV-20261029-shading fitted <c>30 − 32·AMBIENT − k·PLAYER_LIGHT·LIGHT_DIST/d</c>
/// with region and texture <c>AMBIENT</c> only, so the <c>32</c> covering the global as well is an
/// assumption; retail declares <c>SKILL AMBIENT { VAL 0.3; }</c> next to <c>PLAYER_LIGHT</c> 0.2 and
/// <c>LIGHT_DIST</c> 20, and against retail frames the model puts the even-level boundary of level
/// 22 at d 76.2 where the picture draws it at 62.6.
/// <para>
/// Each carrier stands one free wall from (24, −20) to (48, 20) in the room, so the eye at the origin
/// looking along +x meets it at x = 60/(5/3 + u), and the room's own floor is read down a column the
/// wall leaves clear: both show the shade level at every depth from 22 to 48 steps in one picture.
/// The palette's gray ramp at <see cref="ProjectionProbeGenerator.ShadeRampStart"/> is declared one
/// <c>RANGE</c> and the wall and floor are its brightest colour, so a texel's palette index is 16
/// plus its shade level. The trace records the light skills as the runtime holds them.
/// </para>
/// </summary>
internal static class ShadeGlobalProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVSGL0.TXT).</summary>
    public const string TraceFile = "ovsgl";

    /// <summary>The gray ramp as one RANGE, so a shade level lands on a palette index.</summary>
    private static readonly string Range = string.Create(
        CultureInfo.InvariantCulture,
        $"RANGE {ProjectionProbeGenerator.ShadeRampStart},{ProjectionProbeGenerator.ShadeRampLength};");

    /// <summary>The wall read on the eye line: (24, −20) to (48, 20), its far end on the room's far wall.</summary>
    private static readonly TraceProbeWall Front = new("blu_wall", 24, -20, 48, 20);

    /// <summary>The same wall mirrored in y — (48, −20) to (24, 20) — so its face turns the other way.</summary>
    private static readonly TraceProbeWall Mirrored = new("blu_wall", 48, -20, 24, 20);

    /// <summary>The front wall's two vertices swapped: (48, 20) to (24, −20).</summary>
    private static readonly TraceProbeWall Reversed = new("blu_wall", 48, 20, 24, -20);

    /// <summary>Retail's light: <c>PLAYER_LIGHT</c> 0.2 with <c>LIGHT_DIST</c> 20.</summary>
    private const double RetailLight = 0.2;

    private const double RetailLightDistance = 20;

    /// <summary>A strong light — <c>PLAYER_LIGHT</c> 1, <c>LIGHT_DIST</c> 10 — so five boundaries fall in the picture.</summary>
    private const double StrongLight = 1;

    private const double StrongLightDistance = 10;

    /// <summary>The darkest level of the palette's range (OV-20261029-shading): the level a texel is clamped to.</summary>
    private const int Darkest = 30;

    /// <summary>
    /// The model's values for one carrier under <c>30 − 32·(g + a) − 30.4·p·l/d</c> rounded down to an
    /// even level: the base and the level it gives, and every level boundary the picture can hold (the
    /// wall spans x 24…48 and the floor is read from 22 to 48 steps, so d 22…48 is the window). This
    /// is the hypothesis the probe was to test: its round-3 readings replaced it with
    /// OV-20261105-shade-global's whole zone counts and a wall's stepped across the screen, so these
    /// are the record of the old fit, not the rule.
    /// </summary>
    private static (string, string)[] Model(double g, double a, double light, double lightDistance)
    {
        double baseLevel = 30 - (32 * (g + a));
        double slope = 30.4 * light * lightDistance;
        int flat = Math.Clamp(2 * (int)Math.Floor(baseLevel / 2), 0, Darkest);
        var boundaries = new List<string>();
        for (int level = 0; level <= Darkest; level += 2)
        {
            if (level >= baseLevel)
            {
                continue;
            }

            double depth = slope / (baseLevel - level);
            if (depth is >= 22 and <= 48)
            {
                boundaries.Add(string.Create(CultureInfo.InvariantCulture, $"level {level} beyond d {depth:0.0}"));
            }
        }

        return
        [
            ("the model's base 30 − 32·(g + a), and the flat level it gives", $"{N(baseLevel)} → {flat}"),
            ("the model's level boundaries in the picture (a level held beyond its depth)", boundaries.Count == 0 ? "none: flat" : string.Join("; ", boundaries)),
        ];
    }

    /// <summary>
    /// One carrier: the given global and region <c>AMBIENT</c>, light and wall, then a settling
    /// <c>WAITT</c> and the trace's <c>RET</c> and <c>VAL</c> lines.
    /// </summary>
    private static TraceProbeCase Carrier(
        string label,
        double g,
        double a,
        double light,
        double lightDistance,
        string? note = null,
        TraceProbeWall? wall = null,
        string? declarations = null,
        bool setGlobal = true)
    {
        string setup = (setGlobal ? $"    SET AMBIENT, {Num(g)};\n" : "") +
            $"    SET PLAYER_LIGHT, {Num(light)};\n    SET LIGHT_DIST, {Num(lightDistance)};\n";
        return new TraceProbeCase(
            label,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Global AMBIENT {Num(g)}, region AMBIENT {Num(a)}, PLAYER_LIGHT {Num(light)}, LIGHT_DIST {Num(lightDistance)}{(note is null ? "" : ", " + note)}: the shade level along the free wall's eye line and down the floor, against depth."),
            declarations ?? "",
            setup + "    WAITT 8;\n" + P("RET") + V("VAL", "AMBIENT", "PLAYER_LIGHT", "LIGHT_DIST", "DARK_DIST"),
            Predict(Model(g, a, light, lightDistance)),
            Map: new TraceProbeMap(
                RoomBody: a == 0 ? "" : $"AMBIENT {Num(a)};",
                Walls: [wall ?? Front],
                PaletteBody: Range));
    }

    /// <summary>One (g, a) crossed with retail's light (<c>r</c>) and the strong one (<c>s</c>).</summary>
    private static IEnumerable<TraceProbeCase> Pair(double g, double a)
    {
        yield return Carrier($"{Label(g, a)}-r", g, a, RetailLight, RetailLightDistance, note: "retail's light");
        yield return Carrier($"{Label(g, a)}-s", g, a, StrongLight, StrongLightDistance, note: "a strong light");
    }

    // ------------------------------------------------------------------------------------ round 2
    // Round 2 asks for raw readings only: no case here predicts a value, and every carrier prints the
    // light skills so the trace fixes what the runtime held. `round2sweep_read.py` reads them.

    /// <summary>One round-2 carrier: the run, the room's body and the free wall (none for blocks A and B).</summary>
    private static TraceProbeCase Table(
        string label,
        string question,
        string run,
        string roomBody = "",
        IReadOnlyList<TraceProbeWall>? walls = null) => new(
        label,
        question,
        "",
        run,
        Predict(("the shade level against depth", "even steps only; no value is predicted in round 2")),
        Map: new TraceProbeMap(RoomBody: roomBody, Walls: walls, PaletteBody: Range));

    /// <summary>
    /// Round 2's run block: the global <c>AMBIENT</c> 0, the eye lowered to 2 when asked
    /// (<see cref="ShadingProbeCases.LowEye"/>), an optional <c>PLAYER_X</c>, the light skills, then the
    /// trace's <c>RET</c> and <c>VAL</c> lines.
    /// </summary>
    private static string Run(double light, int lightDistance, int eye = 8, double playerX = 0)
    {
        var sb = new StringBuilder();
        sb.Append("    SET AMBIENT, 0;\n");
        if (eye != 8)
        {
            sb.Append(ShadingProbeCases.LowEye);
        }

        if (playerX != 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"    SET PLAYER_X, {Num(playerX)};\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"    SET PLAYER_LIGHT, {Num(light)};\n    SET LIGHT_DIST, {lightDistance};\n");
        sb.Append("    WAITT 8;\n");
        sb.Append(P("RET"));
        sb.Append(V("VAL", "AMBIENT", "PLAYER_LIGHT", "LIGHT_DIST", "DARK_DIST"));
        return sb.ToString();
    }

    /// <summary>
    /// Block A: the floor's zone edges against <c>PLAYER_LIGHT</c> and <c>LIGHT_DIST</c>. No free wall,
    /// so column 320 is clear floor; the eye is at 8 (where the floor shows from 22 to 48 steps) or
    /// lowered to 2 (5.5 to 48).
    /// </summary>
    private static IEnumerable<TraceProbeCase> LightSweep()
    {
        double[] lights = [0.05, 0.1, 0.15, 0.2, 0.25, 0.3, 0.35, 0.4, 0.5, 0.6, 0.75, 0.9, 1];
        int[] distances = [10, 20];
        int[] eyes = [8, 2];
        foreach (double light in lights)
        {
            foreach (int distance in distances)
            {
                foreach (int eye in eyes)
                {
                    yield return Table(
                        string.Create(CultureInfo.InvariantCulture, $"a{Hundredths(light)}l{distance}e{eye}"),
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Round 2, block A: PLAYER_LIGHT {Num(light)}, LIGHT_DIST {distance}, eye at {eye}, global and region AMBIENT 0, no free wall: the floor's zone edges down column 320."),
                        Run(light, distance, eye));
                }
            }
        }
    }

    /// <summary>
    /// Block B: the region <c>AMBIENT</c> in 1/128 steps (k/128 = 8k/1024, so the literal is exact in
    /// 22.10), eye at 2, no free wall, for four (PLAYER_LIGHT, LIGHT_DIST) pairs.
    /// </summary>
    private static IEnumerable<TraceProbeCase> AmbientSweep()
    {
        (double Light, int Distance)[] lights = [(0.2, 20), (0.2, 10), (0.5, 10), (1, 10)];
        foreach ((double light, int distance) in lights)
        {
            for (int k = 0; k <= 15; k++)
            {
                string ambient = AmbientLiteral(k);
                yield return Table(
                    string.Create(CultureInfo.InvariantCulture, $"b{Hundredths(light)}l{distance}k{k:00}"),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Round 2, block B: region AMBIENT {k}/128 = {ambient}, global AMBIENT 0, PLAYER_LIGHT {Num(light)}, LIGHT_DIST {distance}, eye at 2, no free wall: the floor's levels down column 320."),
                    Run(light, distance, eye: 2),
                    roomBody: k == 0 ? "" : $"AMBIENT {ambient};");
            }
        }
    }

    /// <summary>
    /// Block C: one free wall at a time on the eye line. The <c>w-</c> carriers run at PLAYER_LIGHT 1,
    /// LIGHT_DIST 10, and the <c>wr-</c> ones at retail's 0.2 and 20, all at global and region AMBIENT
    /// 0. A frontal wall at a half-step depth is an integer-x wall with the eye moved back by the
    /// fraction (<c>SET PLAYER_X</c>), so the depth from the eye is exactly that D without fractional
    /// map vertices; <c>wr-frb*</c> move the eye back 14 steps as asked.
    /// </summary>
    private static IEnumerable<TraceProbeCase> WallGeometry()
    {
        (string Label, int X1, int Y1, int X2, int Y2)[] oblique =
        [
            ("w-a", 30, -10, 40, 10),
            ("w-a-m", 40, -10, 30, 10),
            ("w-c", 20, -6, 44, 6),
            ("w-c-m", 44, -6, 20, 6),
            ("w-f", 24, -20, 48, 20),
            ("w-f-m", 48, -20, 24, 20),
            ("w-g", 26, -24, 34, 24),
            ("w-g-m", 34, -24, 26, 24),
            ("w-s", 20, -2, 46, 2),
            ("w-s-m", 46, -2, 20, 2),
            ("w-r", 24, -20, 44, -4),
            ("w-l", 24, 20, 44, 4),
        ];
        foreach ((string label, int x1, int y1, int x2, int y2) in oblique)
        {
            yield return Wall(label, 1, 10, x1, y1, x2, y2, 0);
        }

        foreach (double depth in Frontals())
        {
            int x = (int)Math.Floor(depth);
            yield return Wall(FrontalLabel(depth), 1, 10, x, -6, x, 6, x - depth);
        }

        (string Label, int X1, int Y1, int X2, int Y2)[] retailOblique =
        [
            ("wr-a", 30, -10, 40, 10),
            ("wr-a-m", 40, -10, 30, 10),
            ("wr-f", 24, -20, 48, 20),
            ("wr-f-m", 48, -20, 24, 20),
        ];
        foreach ((string label, int x1, int y1, int x2, int y2) in retailOblique)
        {
            yield return Wall(label, RetailLight, (int)RetailLightDistance, x1, y1, x2, y2, 0);
        }

        foreach (int depth in RetailFrontals)
        {
            yield return Wall($"wr-fr{depth}", RetailLight, (int)RetailLightDistance, depth, -6, depth, 6, 0);
        }

        foreach (int depth in RetailMovedBack)
        {
            yield return Wall($"wr-frb{depth}", RetailLight, (int)RetailLightDistance, depth - 14, -12, depth - 14, 12, -14, note: $"the eye moved back 14 steps, so the wall at x = {depth - 14} is {depth} steps ahead");
        }
    }

    /// <summary>
    /// Round 3: side walls whose near end lies behind the eye, as a corridor's walls do (Heart's start
    /// reads its side walls 1.15 to 1.4 times brighter in OpenVirtue). Each runs from x −10 to 46 at
    /// y ±6, both vertex orders, at the strong and at retail's light; the <c>-front</c> ones start at
    /// x 2, ahead of the eye but off the screen.
    /// </summary>
    private static IEnumerable<TraceProbeCase> BehindWalls()
    {
        (string Label, int X1, int Y1, int X2, int Y2)[] walls =
        [
            ("bh-l", -10, 6, 46, 6),
            ("bh-l-m", 46, 6, -10, 6),
            ("bh-r", -10, -6, 46, -6),
            ("bh-r-m", 46, -6, -10, -6),
            ("bh-l-front", 2, 6, 46, 6),
            ("bh-r-front", 2, -6, 46, -6),
        ];
        foreach ((string label, int x1, int y1, int x2, int y2) in walls)
        {
            yield return Wall(label, 1, 10, x1, y1, x2, y2, 0);
            yield return Wall("r" + label, RetailLight, (int)RetailLightDistance, x1, y1, x2, y2, 0);
        }
    }

    /// <summary>The frontal walls' depths: around the level edges at 25, 30 and 37.5 steps (p 1, l 10).</summary>
    private static double[] Frontals() => [24.5, 25, 25.5, 29, 29.5, 30, 30.5, 31, 37, 37.5, 38, 38.5, 39];

    /// <summary>Retail's frontal walls, and the three seen from 14 steps back.</summary>
    private static int[] RetailFrontals => [30, 31, 32, 33, 34];

    private static int[] RetailMovedBack => [60, 61, 62];

    /// <summary>The frontal wall's label: <c>w-fr245</c> for 24.5, <c>w-fr25</c> for 25.</summary>
    private static string FrontalLabel(double depth) => "w-fr" + Tenths(depth);

    /// <summary>One wall carrier. <paramref name="playerX"/> is the eye's x (a fractional frontal depth).</summary>
    private static TraceProbeCase Wall(
        string label,
        double light,
        int lightDistance,
        int x1,
        int y1,
        int x2,
        int y2,
        double playerX,
        string? note = null)
    {
        string caveat = note is null ? "" : ", " + note;
        return Table(
            label,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Round 2, block C: the free wall ({x1}, {y1})-({x2}, {y2}), PLAYER_LIGHT {Num(light)}, LIGHT_DIST {lightDistance}, eye at {(playerX == 0 ? "0" : Num(playerX))} x, height 8: the shade level along row 240, run by run{caveat}."),
            Run(light, lightDistance, playerX: playerX),
            walls: [new TraceProbeWall("blu_wall", x1, y1, x2, y2)]);
    }

    /// <summary>The region AMBIENT literal for k/128 — exact in 22.10, since k/128 = 8k/1024.</summary>
    private static string AmbientLiteral(int k) => (k / 128.0).ToString("0.#########", CultureInfo.InvariantCulture);

    /// <summary>A PLAYER_LIGHT as the label's three digits: 0.05 → 005, 0.2 → 020, 1 → 100.</summary>
    private static string Hundredths(double value) =>
        ((int)Math.Round(value * 100, MidpointRounding.AwayFromZero)).ToString("000", CultureInfo.InvariantCulture);

    /// <summary>A depth in tenths without a separator: 24.5 → 245, 25 → 25.</summary>
    private static string Tenths(double value) =>
        (value * 10 % 10 == 0
            ? ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
            : ((int)Math.Round(value * 10)).ToString(CultureInfo.InvariantCulture));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        ..Pair(0, 0),
        ..Pair(0.3, 0),
        ..Pair(0, 0.2),
        ..Pair(0.3, 0.2),
        ..Pair(0.3, -0.1),
        ..Pair(0, -0.1),
        ..Pair(0.5, 0),
        ..Pair(0, 0.5),
        Carrier("g3a2-p0", 0.3, 0.2, 0, RetailLightDistance, note: "no player light, so the flat base shows"),
        Carrier("g3an1-p0", 0.3, -0.1, 0, RetailLightDistance, note: "no player light, so the flat base shows"),
        Carrier("g3a0-p0", 0.3, 0, 0, RetailLightDistance, note: "no player light, so the flat base shows"),
        Carrier("g3a2-flip", 0.3, 0.2, RetailLight, RetailLightDistance, note: "the wall mirrored in y", wall: Mirrored),
        Carrier("g3an1-flip", 0.3, -0.1, RetailLight, RetailLightDistance, note: "the wall mirrored in y", wall: Mirrored),
        Carrier("g3a2-rev", 0.3, 0.2, RetailLight, RetailLightDistance, note: "the wall's vertices swapped", wall: Reversed),
        Carrier("g3an1-rev", 0.3, -0.1, RetailLight, RetailLightDistance, note: "the wall's vertices swapped", wall: Reversed),
        Carrier(
            "g3a2-dec",
            0.3,
            0.2,
            RetailLight,
            RetailLightDistance,
            note: "the global declared, retail's way, as SKILL AMBIENT { VAL 0.3; } instead of SET",
            declarations: "SKILL AMBIENT { VAL 0.3; }\n",
            setGlobal: false),
        Carrier("g3a2-ld10", 0.3, 0.2, RetailLight, 10, note: "LIGHT_DIST 10"),
        ..LightSweep(),
        ..AmbientSweep(),
        ..WallGeometry(),
        ..BehindWalls(),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "shade-global",
        TraceFile,
        "ovsgs",
        "The final screenshot `OVSGS0.PCX` holds the shade level as palette index − 16 (index 16 is level 0,\n" +
        "index 46 level 30, index 1 past the end of the range) along the free wall's eye line and down the\n" +
        "floor; `OVSGL0.TXT` records AMBIENT, PLAYER_LIGHT, LIGHT_DIST and DARK_DIST as the runtime holds\n" +
        "them. Fitting a boundary's level against its depth, level = base − k·PLAYER_LIGHT·LIGHT_DIST/d,\n" +
        "gives the base (the intercept: 30 less the ambients' share) and k (the slope). Check `OVCON.TXT`\n" +
        "for compile errors.",
        All);

    /// <summary>A carrier label's number for one ambient: 0.3 → 3, −0.1 → n1, 0 → 0.</summary>
    private static string Tag(double value) => value == 0 ? "0"
        : value < 0 ? "n" + Tenth(-value)
        : Tenth(value);

    private static string Tenth(double value) =>
        ((int)Math.Round(value * 10, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    /// <summary>The carrier label prefix for one (global, region) ambient pair, e.g. <c>g3an1</c>.</summary>
    private static string Label(double g, double a) => $"g{Tag(g)}a{Tag(a)}";

    private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
