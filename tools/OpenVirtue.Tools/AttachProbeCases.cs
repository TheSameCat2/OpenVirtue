// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>attach</c> probe: where an <c>ATTACH</c> texture is drawn (manual pp. 42–45:
/// at the pixel position <c>POS_X</c>, <c>POS_Y</c> from the original texture's upper left corner;
/// <c>BEHIND</c> behind the original; <c>SHADOW</c> "at the actor's floor height"). The retail title
/// attaches a wide, low shadow strip (<c>DIAPHANOUS, BEHIND, SHADOW</c>) to its heads, a cross and
/// cracks to walls with <c>POS_X</c>/<c>POS_Y</c>, and buttons to walls. A thing of a 32×48 orange
/// bitmap stands 24 steps ahead of the player (the floor there is on screen; the far wall's target
/// is yellow); its attach is a 48×12 magenta strip or a 16×16 cyan square, so the final
/// screenshot's palette indices give each layer's screen rectangle.
/// </summary>
internal static class AttachProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVATT0.TXT).</summary>
    public const string TraceFile = "ovatt";

    /// <summary>The 32×48 solid orange base bitmap.</summary>
    public const string BaseFile = "PRBATB.PCX";

    /// <summary>The 48×12 solid magenta strip (the retail shadows are 80×9).</summary>
    public const string StripFile = "PRBATS.PCX";

    /// <summary>The 16×16 solid cyan square.</summary>
    public const string SquareFile = "PRBATQ.PCX";

    private static readonly string Bitmaps =
        $"BMAP at_bm,<{BaseFile}>;\nBMAP at_sm,<{StripFile}>;\nBMAP at_qm,<{SquareFile}>;\n" +
        "TEXTURE at_base { SCALE_XY 16, 16; BMAPS at_bm; }\n";

    private static TraceProbeCase Thing(string label, string question, string attachTexture, params (string, string)[] predictions) => new(
        label,
        question,
        Bitmaps + attachTexture + "THING at_t { TEXTURE at_base; " + (attachTexture.Length > 0 ? "ATTACH at_x; " : "") + "}\n",
        "    WAITT 8;\n" + P("RET"),
        Predict(predictions),
        Map: new TraceProbeMap(Placements: ["THING at_t 24 0 0 0"]));

    private static TraceProbeCase Wall(string label, string question, string attachTexture, params (string, string)[] predictions) => new(
        label,
        question,
        "",
        "    WAITT 8;\n" + P("RET"),
        Predict(predictions),
        Map: new TraceProbeMap(
            Textures: Bitmaps + attachTexture + "WALL at_w { TEXTURE blu_tex; ATTACH at_x; }\n",
            Walls: [new TraceProbeWall("at_w", 32, -4, 32, 4)]));

    private static TraceProbeCase Size(string label, string textureBody, string bitmap = "at_bm") => new(
        label,
        $"The base thing alone, its texture {{ {textureBody} BMAPS {bitmap}; }}: the sprite's screen width and height for the scale rule (round 1's yellow base was lost against the far wall's yellow target).",
        $"BMAP at_bm,<{BaseFile}>;\nTEXTURE at_base {{ {textureBody} BMAPS {bitmap}; }}\nTHING at_t {{ TEXTURE at_base; }}\n",
        "    WAITT 8;\n" + P("RET"),
        Predict(("steps = pixels / SCALE", "32 px at 8: 4 steps, 146 px"), ("steps = pixels · SCALE / 96 across and / 72 up", "32 px at 8: 2.67 steps, 98 px")),
        Map: new TraceProbeMap(Placements: ["THING at_t 24 0 0 0"]));

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Size("size-sep", "SCALE_X 16; SCALE_Y 16;"),
        Size("size-none", ""),
        Size("size-8", "SCALE_XY 8,8;"),
        Size("size-32", "SCALE_XY 32,32;"),
        Size("size-48", "SCALE_XY 48,48;"),
        Size("size-yel", "SCALE_XY 16,16;", "yel_map"),
        Thing(
            "plain",
            "The base thing alone: its screen rectangle, for every other case to compare with.",
            "",
            ("32×48 at SCALE 16 is 2×3 steps on the floor 24 steps ahead", "an orange rectangle whose bottom is the floor line")),
        Thing(
            "shadow",
            "ATTACH of the strip with FLAGS SHADOW: drawn flat on the floor (a foreshortened band) or upright at the floor height (a 12-pixel strip, 0.75 steps tall) and where across?",
            "TEXTURE at_x { BMAPS at_sm; FLAGS SHADOW; }\n",
            ("upright at the floor, centred", "magenta 0.75 steps tall at the base's bottom, wider than the base"),
            ("flat on the floor", "a magenta band a few pixels tall around the base's bottom"),
            ("at the upper left as POS 0, 0", "magenta at the base's top left")),
        Thing(
            "shadow-behind",
            "The strip with FLAGS SHADOW, BEHIND (the retail shadows): which wins where the strip and the base overlap?",
            "TEXTURE at_x { BMAPS at_sm; FLAGS SHADOW, BEHIND; }\n",
            ("the base covers the strip", "no magenta inside the yellow rectangle"),
            ("BEHIND makes no difference at the floor", "as shadow")),
        Thing(
            "shadow-pos",
            "The strip with FLAGS SHADOW and POS_X 16, POS_Y 0: does POS_X shift a shadow?",
            "TEXTURE at_x { BMAPS at_sm; FLAGS SHADOW; POS_X 16; POS_Y 0; }\n",
            ("shifted a step to the right", "the magenta a step right of shadow's"),
            ("POS ignored for shadows", "as shadow")),
        Thing(
            "front",
            "The square with POS 0, 0 and no flags: at the base's upper left, over it?",
            "TEXTURE at_x { BMAPS at_qm; }\n",
            ("upper left, in front", "cyan 1×1 steps at the yellow's top left corner, the yellow cut there"),
            ("centred", "cyan in the middle of the yellow")),
        Thing(
            "pos",
            "The square with POS_X 24, POS_Y 40: its upper left 24, 40 pixels (1.5, 2.5 steps) from the base's, so it sticks out at the bottom right?",
            "TEXTURE at_x { BMAPS at_qm; POS_X 24; POS_Y 40; }\n",
            ("pixels from the upper left, the base's scale", "cyan from 1.5 steps right and 2.5 down of the top left: half of it below the floor line and right of the yellow"),
            ("POS_Y upward", "the cyan above the yellow")),
        Thing(
            "neg",
            "The square with POS_X −8, POS_Y −8: above and left of the base?",
            "TEXTURE at_x { BMAPS at_qm; POS_X -8; POS_Y -8; }\n",
            ("half a step up and left", "cyan overlapping the yellow's top left corner by half")),
        Thing(
            "behind",
            "The square with FLAGS BEHIND, POS_X −8, POS_Y −8: the base covers the overlap?",
            "TEXTURE at_x { BMAPS at_qm; FLAGS BEHIND; POS_X -8; POS_Y -8; }\n",
            ("behind", "cyan only outside the yellow")),
        Thing(
            "scale",
            "The square with its own SCALE_XY 8, 8 and POS_X 24, POS_Y 40: is the attach scaled by its own SCALE (2×2 steps) or the base's (1×1), and is POS in the base's pixels?",
            "TEXTURE at_x { BMAPS at_qm; SCALE_XY 8, 8; POS_X 24; POS_Y 40; }\n",
            ("its own scale, POS in the base's pixels", "cyan 2×2 steps from 1.5, 2.5 steps"),
            ("the base's scale", "as pos")),
        Thing(
            "shadow-posy",
            "The strip with FLAGS SHADOW and POS_Y 8: does POS_Y move a shadow down half a step from its floor-centred place?",
            "TEXTURE at_x { BMAPS at_sm; FLAGS SHADOW; POS_X 0; POS_Y 8; }\n",
            ("half a step down", "magenta from the floor line down"),
            ("POS_Y ignored for shadows", "as shadow")),
        new TraceProbeCase(
            "shadow-z",
            "The strip with FLAGS SHADOW on a thing raised to Z 4 at run time: does the shadow stay on the floor while the base rises?",
            Bitmaps + "TEXTURE at_x { BMAPS at_sm; FLAGS SHADOW; }\nTHING at_t { TEXTURE at_base; ATTACH at_x; }\nSYNONYM at_s { TYPE THING; }\n",
            "    SET at_s, at_t;\n    SET at_s.Z, 4;\n    WAITT 8;\n" + P("RET"),
            Predict(("the shadow stays at the floor", "orange 4 steps up, magenta where shadow had it"), ("the shadow rises with the thing", "both up")),
            Map: new TraceProbeMap(Placements: ["THING at_t 24 0 0 0"])),
        Wall(
            "wall",
            "A free wall of blu_tex (64 px at SCALE 4, 16 px a step) 8 steps long, 32 steps ahead, with the square attached at POS 0, 0: drawn at all, and at which corner (the texture's upper left: top of the wall at its first vertex?)",
            "TEXTURE at_x { BMAPS at_qm; }\n",
            ("upper left of the wall at the first vertex", "cyan 1×1 steps at the wall's top at y −4 (screen left or right as the wall reads)"),
            ("at the wall's floor", "cyan at the bottom")),
        Wall(
            "wall-pos",
            "The wall with the square at POS_X 24, POS_Y 16: 1.5 steps along and 1 step down from the corner?",
            "TEXTURE at_x { BMAPS at_qm; POS_X 24; POS_Y 16; }\n",
            ("pixels at the wall texture's scale from the upper left", "cyan from 1.5 steps along, 1 step down")),
        Wall(
            "wall-neg",
            "The wall with the square at POS_X −8, POS_Y −8: drawn past the wall's end and above its top, or clipped to the wall?",
            "TEXTURE at_x { BMAPS at_qm; POS_X -8; POS_Y -8; }\n",
            ("clipped to the wall", "a quarter of the square at the corner"),
            ("drawn whole", "cyan past the corner")),
        WallFlags(
            "wall-port",
            "The wall with FLAGS PORTCULLIS and the square at POS_X 24, POS_Y 16 (the manual: with POSITION 0 the attach's vertical zero is the wall's lower edge): below the floor, hidden, or up from the bottom?",
            "TEXTURE at_x { BMAPS at_qm; POS_X 24; POS_Y 16; }\n",
            "FLAGS PORTCULLIS;",
            ("down from the bottom edge, so hidden in the floor", "no cyan"),
            ("up from the bottom edge", "cyan 4 to 5 steps above the floor")),
        WallFlags(
            "wall-port-neg",
            "The PORTCULLIS wall with the square at POS_X 24, POS_Y −16: above the lower edge?",
            "TEXTURE at_x { BMAPS at_qm; POS_X 24; POS_Y -16; }\n",
            "FLAGS PORTCULLIS;",
            ("up from the bottom edge by 4 steps", "cyan 3 to 4 steps above the floor"),
            ("as wall-pos", "cyan 4 steps below the top")),
        WallTexture(
            "wall-s3",
            "A wall texture of SCALE_X 4, SCALE_Y 3 (16 steps across a repeat, 21.3 up, so the 16-step wall is no whole number of repeats) with the square at POS_X 24, POS_Y 16: is POS_Y in SCALE_Y pixels (5.33 steps down from the top)?",
            "TEXTURE at_x { BMAPS at_qm; POS_X 24; POS_Y 16; }\n",
            "",
            ("SCALE_Y pixels from the top", "cyan 5.33 to 6.33 steps below the ceiling"),
            ("SCALE_X pixels", "cyan 4 to 5 steps below")),
        WallTexture(
            "wall-port-s3",
            "The SCALE_Y 3 wall with FLAGS PORTCULLIS and the square at POS_X 24, POS_Y 16: the vertical zero at the lower edge (the manual) now that the repeat does not divide the height?",
            "TEXTURE at_x { BMAPS at_qm; POS_X 24; POS_Y 16; }\n",
            "FLAGS PORTCULLIS;",
            ("from the lower edge upward", "cyan 5.33 steps above the floor"),
            ("from the lower edge downward: hidden", "no cyan"),
            ("as wall-s3", "cyan 5.33 steps below the ceiling")),
        Wall(
            "wall-half",
            "The wall with the square at POS_X −2, POS_Y 0 (half a step past the wall's u origin end): clipped to the wall or drawn whole?",
            "TEXTURE at_x { BMAPS at_qm; POS_X -2; POS_Y 0; }\n",
            ("clipped", "half a square at the corner"),
            ("whole", "a whole square sticking out")),
    ];

    private static TraceProbeCase WallTexture(string label, string question, string attachTexture, string wallFlags, params (string, string)[] predictions) => new(
        label,
        question,
        "",
        "    WAITT 8;\n" + P("RET"),
        Predict(predictions),
        Map: new TraceProbeMap(
            Textures: Bitmaps + attachTexture + "TEXTURE at_wt { SCALE_X 4; SCALE_Y 3; BMAPS blu_map; }\nWALL at_w { TEXTURE at_wt; ATTACH at_x; " + wallFlags + " }\n",
            Walls: [new TraceProbeWall("at_w", 32, -4, 32, 4)]));

    private static TraceProbeCase WallFlags(string label, string question, string attachTexture, string wallFlags, params (string, string)[] predictions) => new(
        label,
        question,
        "",
        "    WAITT 8;\n" + P("RET"),
        Predict(predictions),
        Map: new TraceProbeMap(
            Textures: Bitmaps + attachTexture + "WALL at_w { TEXTURE blu_tex; ATTACH at_x; " + wallFlags + " }\n",
            Walls: [new TraceProbeWall("at_w", 32, -4, 32, 4)]));

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "attach",
        TraceFile,
        "ovats",
        "The final screenshot `OVATS0.PCX` is the reading: the orange (11), magenta (7) and cyan (10) index\n" +
        "rectangles are the base and attach layers. Compare with `case.json` → `predictions`, check\n" +
        "`OVCON.TXT` for compile errors.",
        All);
}
