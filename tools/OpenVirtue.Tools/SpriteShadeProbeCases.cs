// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>sprite-shade</c> probe: how a thing's sprite is shaded by distance
/// (OV-20261029-shading measured walls and floors: level 30 − 32·AMBIENT − 30.4·PLAYER_LIGHT·LIGHT_DIST/d
/// with d the depth along the line of sight, rounded down to an even level). A thing of a 32×48
/// bitmap in the gray ramp's first colour stands at several depths, once off to the side (depth 28,
/// distance 30.5) and once with a texture <c>AMBIENT</c>; the final screenshot's index over the
/// sprite is 16 plus its shade level, read at its centre and its edges to see whether a sprite is
/// one level or shaded across.
/// </summary>
internal static class SpriteShadeProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVSPS0.TXT).</summary>
    public const string TraceFile = "ovsps";

    /// <summary>The 32×48 bitmap of the ramp's first index.</summary>
    public const string RampFile = "PRBSHT.PCX";

    private static TraceProbeCase At(string label, double x, double y, string question, string textureBody = "", string run = "", string roomBody = "", params (string, string)[] predictions) => new(
        label,
        question,
        $"BMAP ss_bm,<{RampFile}>;\nTEXTURE ss_tex {{ SCALE_XY 16, 16; BMAPS ss_bm; {textureBody}}}\nTHING ss_t {{ TEXTURE ss_tex; }}\n",
        run + "    WAITT 8;\n" + P("RET"),
        Predict(predictions.Length > 0 ? predictions : [("the walls' curve on the depth", "level 30 − 304/depth rounded down to even, one level over the sprite"), ("the distance to the sprite", "a level from 30 − 304/distance")]),
        Map: new TraceProbeMap(
            RoomBody: roomBody,
            PaletteBody: $"RANGE {ProjectionProbeGenerator.ShadeRampStart},{ProjectionProbeGenerator.ShadeRampLength};",
            Placements: [string.Create(CultureInfo.InvariantCulture, $"THING ss_t {x} {y} 0 0")]));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        At("d12", 12, 0, "The thing 12 steps ahead (walls there read level 4)."),
        At("d20", 20, 0, "The thing 20 steps ahead (level 14)."),
        At("d28", 28, 0, "The thing 28 steps ahead (level 18)."),
        At("d36", 36, 0, "The thing 36 steps ahead (level 20)."),
        At("d44", 44, 0, "The thing 44 steps ahead (level 22), just before the far wall."),
        At("side", 28, 12, "The thing at depth 28 and 12 steps to the left (distance 30.5): the depth's level 18 or the distance's 20?"),
        At("ambient", 28, 0, "The thing 28 steps ahead with its texture's AMBIENT 0.25: 8 levels brighter (10)?", "AMBIENT 0.25; "),
        At("d15.2", 15.2, 0, "Round 2. Depth 15.2, where the walls' formula gives exactly 10: does the sprite read 10 (rounded up to an even level) or 12 (two levels darker)?"),
        At("d19", 19, 0, "Round 2. Depth 19: exactly 14; the sprite 14 or 16?"),
        At("d30.4", 30.4, 0, "Round 2. Depth 30.4: exactly 20; the sprite 20 or 22?"),
        At("d38", 38, 0, "Round 2. Depth 38: exactly 22; the sprite 22 or 24?"),
        At("dark", 28, 0, "Round 3. PLAYER_LIGHT 0 (every wall at 30): the sprite two levels past, 32 (the INFINITY colour), or held at 30?", run: "    SET PLAYER_LIGHT, 0;\n"),
        At("bright", 20, 0, "Round 3. Region AMBIENT 0.5 at depth 20 (the walls' formula −1.2, held at 0): the sprite 0 or 2?", roomBody: "AMBIENT 0.5;"),
    ];

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "sprite-shade",
        TraceFile,
        "ovsss",
        "The final screenshot `OVSSS0.PCX` is the reading: the index over the sprite (its centre and its left\n" +
        "and right edges on the eye-line row) is 16 plus the shade level. Compare with `case.json` →\n" +
        "`predictions`, check `OVCON.TXT` for compile errors.",
        All);
}
