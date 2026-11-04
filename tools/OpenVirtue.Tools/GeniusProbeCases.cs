// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>genius</c> probe: how a region's <c>GENIUS</c> lamp lights a wall with
/// <c>ALBEDO</c> along its length (manual p.33, p.37, p.58: a thing or actor whose texture has
/// <c>AMBIENT</c> serves as the region's light source; <c>ALBEDO</c> is the wall's reflection of it).
/// OV-20261029-shading rounds 4–5 saw a facing wall gain <c>ALBEDO</c> × 16 levels and grazing walls
/// gain less, on the far ends of the side walls only. Here a free wall 16 steps long stands 32 steps
/// ahead in the gray ramp's first colour with region <c>AMBIENT</c> 0.25 and <c>PLAYER_LIGHT</c> 0
/// (level 22 without the lamp), and the lamp moves around it: the shade index along the eye-line row
/// gives the gain at every wall point against the point's angle and distance to the lamp.
/// </summary>
internal static class GeniusProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVGEN0.TXT).</summary>
    public const string TraceFile = "ovgen";

    private static string Textures(string albedo, string lampAmbient) =>
        $"TEXTURE gn_lamp_tex {{ SCALE_X 4; SCALE_Y 4; BMAPS cel_map; AMBIENT {lampAmbient}; }}\nTHING gn_lamp {{ TEXTURE gn_lamp_tex; FLAGS INVISIBLE; }}\n" +
        $"TEXTURE gn_tex {{ SCALE_X 4; SCALE_Y 4; BMAPS blu_map; ALBEDO {albedo}; }}\nWALL gn_wall {{ TEXTURE gn_tex; }}\n";

    private static TraceProbeCase Lamp(string label, double x, double y, string question, string albedo = "1", string lampAmbient = "1", string ambient = "0.25", params (string, string)[] predictions) => new(
        label,
        question,
        "",
        "    SET PLAYER_LIGHT, 0;\n    WAITT 8;\n" + P("RET"),
        Predict(predictions.Length > 0 ? predictions : [("Lambert", "gain = 16 · ALBEDO · cos of the angle between the wall's normal and the lamp at each point"), ("one value a wall", "the same gain along the whole wall")]),
        Map: new TraceProbeMap(
            RoomBody: $"AMBIENT {ambient}; GENIUS gn_lamp;",
            PaletteBody: $"RANGE {ProjectionProbeGenerator.ShadeRampStart},{ProjectionProbeGenerator.ShadeRampLength};",
            Textures: Textures(albedo, lampAmbient),
            Walls: [new TraceProbeWall("gn_wall", 32, -8, 32, 8)],
            Placements: [string.Create(CultureInfo.InvariantCulture, $"THING gn_lamp {x} {y} 0 0")]));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Lamp("none", 0, 0, "No lamp in front of the wall (it stands at the player's feet, 32 steps off, facing the wall at 0°): the wall's base level and the gain at that range.", predictions: [("the gain falls with distance", "less than 16 levels"), ("no distance term", "16 levels along the whole wall")]),
        Lamp("centre", 24, 0, "The lamp 8 steps before the wall's centre: the gain from the centre (0°) to the ends (45°)."),
        Lamp("close", 30, 0, "The lamp 2 steps before the centre: 0° at the centre, 76° at the ends."),
        Lamp("far", 8, 0, "The lamp 24 steps before the centre: within 18° everywhere; does the gain fall with distance?"),
        Lamp("offset", 24, 8, "The lamp 8 steps before one end: 0° there, 63° at the other end."),
        Lamp("side", 32, 12, "The lamp on the wall's own line beyond its end: a grazing 90° everywhere, 4 to 20 steps away."),
        Lamp("behind", 40, 0, "The lamp 8 steps behind the wall: does the face toward the player gain at all?", predictions: [("no gain", "level 22 everywhere"), ("gain as from the front", "as centre")]),
        Lamp("half-albedo", 24, 0, "As centre with ALBEDO 0.5: half the gain?", albedo: "0.5"),
        Lamp("half-lamp", 24, 0, "As centre with the lamp texture's AMBIENT 0.5: does the lamp's AMBIENT scale the gain?", lampAmbient: "0.5"),
        Lamp("a28-8", 28, 8, "Round 2. The lamp at (28, 8): 63° from the wall's midpoint (cos 0.45): gain 7 (level 14) if the gain is 16 · cos of the midpoint angle, rounded down to an even level."),
        Lamp("a24-16", 24, 16, "Round 2. The lamp at (24, 16): also 63° from the midpoint but twice as far: the same level 14 if distance plays no part."),
        Lamp("a26-2", 26, 2, "Round 2. The lamp at (26, 2): 18° from the midpoint (cos 0.95): level 6."),
        Lamp("a24-24", 24, 24, "Round 2. The lamp at (24, 24): 72° (cos 0.32): level 16."),
        Lamp("a24-32", 24, 32, "Round 2. The lamp at (24, 32): 76° (cos 0.24): level 18."),
        Lamp("behind-dim", 40, 0, "Round 2. The lamp 8 steps behind the wall with region AMBIENT 0.5 (level 14 alone): a loss of 16 levels gives 30, within the ramp.", ambient: "0.5"),
        Lamp("behind-dim-45", 40, 8, "Round 2. The lamp at (40, 8), 135° from the midpoint, with AMBIENT 0.5: a loss of 11.3 gives level 24 or 25.", ambient: "0.5"),
        Lamp("behind-45", 40, 8, "Round 2. The lamp at (40, 8) with AMBIENT 0.25: level 32 or 33, past the ramp's 30: which index shows?", predictions: [("the INFINITY colour", "index 1"), ("clamped at the ramp's end", "index 47"), ("the next entry", "index 48")]),
        new TraceProbeCase(
            "sprite",
            "Round 3. The lamp at (24, 0) before the wall and a thing of the ramp colour whose texture has ALBEDO 1 at (20, 6), the floor also in the ramp colour: does the lamp light the sprite (a sprite at depth 20 reads 16 without it, OV-20261104-sprite-shade) or the floor (level 22 everywhere with PLAYER_LIGHT 0)?",
            $"BMAP gn_sm,<{SpriteShadeProbeCases.RampFile}>;\nTEXTURE gn_st {{ SCALE_XY 16, 16; BMAPS gn_sm; ALBEDO 1; }}\nTHING gn_s {{ TEXTURE gn_st; }}\n",
            "    SET PLAYER_LIGHT, 0;\n    WAITT 8;\n" + P("RET"),
            Predict(("walls only", "the sprite 24 (22 + 2) and the floor 22"), ("sprites too", "the sprite brighter by 16 · cos"), ("floors too", "the floor brighter near the lamp")),
            Map: new TraceProbeMap(
                RoomBody: "AMBIENT 0.25; GENIUS gn_lamp;",
                PaletteBody: $"RANGE {ProjectionProbeGenerator.ShadeRampStart},{ProjectionProbeGenerator.ShadeRampLength};",
                Textures: Textures("1", "1"),
                Walls: [new TraceProbeWall("gn_wall", 32, -8, 32, 8)],
                Placements: ["THING gn_lamp 24 0 0 0", "THING gn_s 20 6 0 0"])),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "genius",
        TraceFile,
        "ovges",
        "The final screenshot `OVGES0.PCX` is the reading: the index along row 240 across the wall (x 174 to\n" +
        "466) is 16 plus the shade level, 22 without the lamp. Compare with `case.json` → `predictions`,\n" +
        "check `OVCON.TXT` for compile errors.",
        All);
}
