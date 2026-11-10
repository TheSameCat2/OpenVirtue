// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using static TraceProbeWdl;

/// <summary>
/// The cases of the <c>clip-view</c> probe: what <c>CLIP_DIST</c> does to the picture. Manual p.30:
/// only walls and objects entirely within the distance from the player are rendered, walls outside
/// it are drawn in palette colour 1; p.105: <c>CLIPPING</c> 0, 0.5 and 1 decide when a wall counts
/// as outside; p.55: a wall's <c>FAR</c> keeps it. It does not say which distance, what a floor or a
/// ceiling does, or what a sky does. All retail levels declare a global 400 and most regions their
/// own, and sprites that the original hides show through in OpenVirtue.
/// </summary>
internal static class ClipViewProbeCases
{
    /// <summary>The PRINTFILE base name of the trace file (written as OVCVT0.TXT).</summary>
    public const string TraceFile = "ovcvt";

    private const string Sprites =
        $"BMAP cv_bm,<{AttachProbeCases.BaseFile}>;\nBMAP cv_am,<{AttachProbeCases.SquareFile}>;\n" +
        "TEXTURE cv_tex { SCALE_XY 16, 16; BMAPS cv_bm; }\nTEXTURE cv_atx { SCALE_XY 4, 4; BMAPS cv_am; }\n" +
        "THING cv_t { TEXTURE cv_tex; }\nACTOR cv_a { TEXTURE cv_atx; }\n";

    private const string High =
        Sprites + "THING cv_h { TEXTURE cv_tex; HEIGHT 20; }\n";

    private const string Low =
        Sprites + "THING cv_h { TEXTURE cv_tex; HEIGHT -8; }\n";

    private const string WallDecl =
        "TEXTURE cv_wtx { SCALE_X 4; SCALE_Y 4; BMAPS blu_map; }\nWALL cv_w { TEXTURE cv_wtx; }\nWALL cv_f { TEXTURE cv_wtx; FLAGS FAR; }\n";

    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static TraceProbeCase Case(
        string label,
        string question,
        string roomBody = "",
        string declarations = "",
        string run = "",
        IReadOnlyList<TraceProbeWall>? walls = null,
        IReadOnlyList<string>? placements = null,
        string? roomCeiling = null,
        string? padBody = null,
        string? padWall = null,
        string? textures = null,
        int padFloor = 0,
        int? padCeiling = null) => new(
        label,
        question,
        declarations,
        run + "    WAITT 8;\n" + V("V", "CLIPPING", "THING_DIST", "ACTOR_DIST"),
        Predict(
            ("walls outside are palette colour 1 (p.30)", "index 1 where the wall would be"),
            ("objects outside are not drawn", "no sprite beyond the distance")),
        Map: new TraceProbeMap(
            RoomBody: roomBody,
            Walls: walls,
            Placements: placements,
            RoomCeiling: roomCeiling,
            PadBody: padBody,
            PadWall: padWall,
            Textures: textures,
            PadFloor: padFloor,
            PadCeiling: padCeiling));

    /// <summary>The pad with invisible walls and the given floor, one thing, the view turned to it.</summary>
    private static TraceProbeCase Slab(string label, int floor, string placement, string question, string thing = "", string texture = "") =>
        Case(label, question, declarations: Sprites + texture + thing, run: "    SET PLAYER_ANGLE, 2.03;\n", padBody: "", padFloor: floor, placements: [placement]);

    private static string Clip(double value) => $"CLIP_DIST {N(value)};";

    private static TraceProbeWall Frontal(int x, int half = 6, string keyword = "cv_w") => new(keyword, x, -half, x, half);

    private static TraceProbeCase WallAt(string label, string question, double clip, TraceProbeWall wall, double? clipping = null, string keyword = WallDecl) =>
        Case(
            label,
            question,
            roomBody: Clip(clip),
            declarations: keyword,
            run: clipping is { } c ? $"    SET CLIPPING, {N(c)};\n" : "",
            walls: [wall]);

    private static List<string> Row(string kind, params double[] depths)
    {
        var lines = new List<string>();
        double y = -15;
        foreach (double depth in depths)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{kind} {depth} {y} 0 0"));
            y += 6;
        }

        return lines;
    }

    private static readonly double[] ObjectDepths = [20, 28, 29.5, 30.5, 32, 40];

    public static readonly IReadOnlyList<TraceProbeCase> All =
    [
        Case("control", "No CLIP_DIST given: the room whole."),
        Case("r100", "CLIP_DIST 100 on the room: the same picture?", Clip(100)),
        Case("r60", "CLIP_DIST 60: every wall in reach but the far corners'.", Clip(60)),
        Case("r50", "CLIP_DIST 50: the far stripes end from 48 to 53.7 away: which walls go?", Clip(50)),
        Case("r40", "CLIP_DIST 40: the far wall is out; what shows, and the floor and ceiling up to it?", Clip(40)),
        Case("r30", "CLIP_DIST 30: floor and ceiling beyond 30?", Clip(30)),
        Case("r10", "CLIP_DIST 10: the side walls run from behind the player.", Clip(10)),
        Case("r0", "CLIP_DIST 0 on the player's own region.", Clip(0)),
        Case("set-live", "A SKILL CLIP_DIST of 100, then SET CLIP_DIST, 30 in the action, no region value: does the picture change?", declarations: "SKILL CLIP_DIST { VAL 100; }\n", run: "    SET CLIP_DIST, 30;\n"),
        Case("keyword", "The manual's top-level CLIP_DIST 30; with no region value.", declarations: "CLIP_DIST 30;\n"),
        Case("skill-decl", "SKILL CLIP_DIST { VAL 30; } with no region value (retail's global).", declarations: "SKILL CLIP_DIST { VAL 30; }\n"),
        Case("set-region", "CLIP_DIST 100 on the room, then SET CLIP_DIST, 30 (Heart's start): which one counts?", Clip(100), declarations: "SKILL CLIP_DIST { VAL 100; }\n", run: "    SET CLIP_DIST, 30;\n"),
        WallAt("in", "A frontal wall at 20 with CLIP_DIST 25.", 25, Frontal(20)),
        WallAt("out", "A frontal wall at 30 with CLIP_DIST 25.", 25, Frontal(30)),
        WallAt("cross", "A wall from 20 to 30 straddling CLIP_DIST 25.", 25, new("cv_w", 20, -10, 30, 10)),
        WallAt("cross-05", "The same, CLIPPING 0.5.", 25, new("cv_w", 20, -10, 30, 10), 0.5),
        WallAt("cross-1", "The same, CLIPPING 1.", 25, new("cv_w", 20, -10, 30, 10), 1),
        WallAt("out-05", "The wall at 30, CLIPPING 0.5.", 25, Frontal(30), 0.5),
        WallAt("far-0", "The wall at 30 with flag FAR, CLIPPING 0.", 25, Frontal(30, keyword: "cv_f")),
        WallAt("far-05", "The wall at 30 with flag FAR, CLIPPING 0.5.", 25, Frontal(30, keyword: "cv_f"), 0.5),
        WallAt("far-1", "The wall at 30 with flag FAR, CLIPPING 1.", 25, Frontal(30, keyword: "cv_f"), 1),
        WallAt("metric-20", "A frontal wall at depth 18 with ends 18 sideways (Euclid 25.5, sum 36): CLIP_DIST 20.", 20, Frontal(18, 18)),
        WallAt("metric-24", "CLIP_DIST 24.", 24, Frontal(18, 18)),
        WallAt("metric-27", "CLIP_DIST 27.", 27, Frontal(18, 18)),
        WallAt("metric-34", "CLIP_DIST 34.", 34, Frontal(18, 18)),
        WallAt("metric-38", "CLIP_DIST 38.", 38, Frontal(18, 18)),
        Case("things", "Things at 20, 28, 29.5, 30.5, 32, 40 with CLIP_DIST 30.", Clip(30), Sprites, placements: Row("THING cv_t", ObjectDepths)),
        Case("things-half", "The same with THING_DIST 0.5.", Clip(30), Sprites, "    SET THING_DIST, 0.5;\n", placements: Row("THING cv_t", ObjectDepths)),
        Case("actors", "Actors at 20, 28, 29.5, 30.5, 32, 40 with CLIP_DIST 30.", Clip(30), Sprites, placements: Row("ACTOR cv_a", ObjectDepths)),
        Case("actors-half", "The same with ACTOR_DIST 0.5, THING_DIST 1.", Clip(30), Sprites, "    SET ACTOR_DIST, 0.5;\n", placements: Row("ACTOR cv_a", ObjectDepths)),
        Case("thing-side", "Things at Euclid 31 and sums 44: (22, 22), and (22, -22) with CLIP_DIST 35.", Clip(35), Sprites, placements: ["THING cv_t 22 22 0 0", "THING cv_t 22 -22 0 0"]),
        Case("sky", "A sky ceiling with CLIP_DIST 30: does the sky clip?", Clip(30), roomCeiling: "cv_sky", textures: SkyDecl),
        Case("pad-zero", "A second region with CLIP_DIST 0 and visible walls, seen from the room.", padBody: Clip(0), padWall: "cv_w", declarations: WallDecl, run: "    SET PLAYER_ANGLE, 2.03;\n"),
        Case("pad-small", "A second region with CLIP_DIST 5 and visible walls, seen from the room.", padBody: Clip(5), padWall: "cv_w", declarations: WallDecl, run: "    SET PLAYER_ANGLE, 2.03;\n"),

        // Round 2: objects in reach, one vertex in and one out, and a region stood in.
        WallAt("n-in", "Round 2. A frontal wall at 20, ends 2 sideways (22 by axes), CLIP_DIST 25: shown; what of the columns beside it?", 25, Frontal(20, 2)),
        WallAt("n-in-05", "The same, CLIPPING 0.5.", 25, Frontal(20, 2), 0.5),
        WallAt("n-in-1", "The same, CLIPPING 1, where every other wall of the room is out.", 25, Frontal(20, 2), 1),
        WallAt("n-one", "A wall (16, -4) to (32, 4), 20 and 36 by axes, CLIP_DIST 25: one vertex in, CLIPPING 0.", 25, new("cv_w", 16, -4, 32, 4)),
        WallAt("n-one-05", "The same, CLIPPING 0.5.", 25, new("cv_w", 16, -4, 32, 4), 0.5),
        WallAt("n-one-1", "The same, CLIPPING 1.", 25, new("cv_w", 16, -4, 32, 4), 1),
        WallAt("n-eq", "A frontal wall at 20, ends 5 sideways (exactly 25), CLIP_DIST 25: the border.", 25, Frontal(20, 5)),
        Case("t-20", "One thing at (20, 0), CLIP_DIST 30: every wall out, the thing over the clip colour?", Clip(30), Sprites, placements: ["THING cv_t 20 0 0 0"]),
        Case("t-near", "One thing at (20, 0), CLIP_DIST 60 and THING_DIST 0.5: within the scaled 30.", Clip(60), Sprites, "    SET THING_DIST, 0.5;\n", placements: ["THING cv_t 20 0 0 0"]),
        Case("t-29", "At 29.5.", Clip(60), Sprites, "    SET THING_DIST, 0.5;\n", placements: ["THING cv_t 29.5 0 0 0"]),
        Case("t-30", "At exactly 30.", Clip(60), Sprites, "    SET THING_DIST, 0.5;\n", placements: ["THING cv_t 30 0 0 0"]),
        Case("t-31", "At 30.5.", Clip(60), Sprites, "    SET THING_DIST, 0.5;\n", placements: ["THING cv_t 30.5 0 0 0"]),
        Case("t-side", "At (22, 22), 31 straight and 44 by axes, CLIP_DIST 60, THING_DIST 0.7 (42).", Clip(60), Sprites, "    SET THING_DIST, 0.7;\n", placements: ["THING cv_t 22 22 0 0"]),
        Case("t-side2", "The same at THING_DIST 0.8 (48).", Clip(60), Sprites, "    SET THING_DIST, 0.8;\n", placements: ["THING cv_t 22 22 0 0"]),
        Case("t-default", "A thing at 40 with CLIP_DIST 60 and THING_DIST unset: reach 60.", Clip(60), Sprites, placements: ["THING cv_t 40 0 0 0"]),
        Case("a-29", "An actor at 29.5, CLIP_DIST 60, ACTOR_DIST 0.5.", Clip(60), Sprites, "    SET ACTOR_DIST, 0.5;\n", placements: ["ACTOR cv_a 29.5 0 0 0"]),
        Case("a-31", "An actor at 30.5, CLIP_DIST 60, ACTOR_DIST 0.5.", Clip(60), Sprites, "    SET ACTOR_DIST, 0.5;\n", placements: ["ACTOR cv_a 30.5 0 0 0"]),
        Case("a-thing-dist", "An actor at 30.5, CLIP_DIST 60, THING_DIST 0.5: does the thing ratio cap an actor?", Clip(60), Sprites, "    SET THING_DIST, 0.5;\n", placements: ["ACTOR cv_a 30.5 0 0 0"]),
        Case("t-near-clip", "A thing at (20, 0) in front of a wall out of reach at 30, CLIP_DIST 25: over the clip colour.", Clip(25), Sprites + WallDecl, walls: [Frontal(30)], placements: ["THING cv_t 20 0 0 0"]),
        Case("t-far-clip", "A thing at (40, 0) behind that wall, with FAR on the wall, CLIP_DIST 25.", Clip(25), Sprites + WallDecl, walls: [Frontal(30, keyword: "cv_f")], placements: ["THING cv_t 40 0 0 0"]),
        Case(
            "island",
            "The raised pad seen from the room with CLIP_DIST 30, the room's walls behind it out: the room's floor in those columns, and above the pad?",
            Clip(30),
            WallDecl,
            "    SET PLAYER_ANGLE, 2.03;\n",
            padBody: Clip(30),
            padWall: "cv_w",
            padFloor: 4,
            padCeiling: 12),
        Case(
            "island-big",
            "The same with the pad's own CLIP_DIST 1000: does the far region's value matter?",
            Clip(30),
            WallDecl,
            "    SET PLAYER_ANGLE, 2.03;\n",
            padBody: Clip(1000),
            padWall: "cv_w",
            padFloor: 4,
            padCeiling: 12),
        Case(
            "island-tiny",
            "The room's CLIP_DIST 30 with the pad's own 5: are the pad's walls gone?",
            Clip(30),
            WallDecl,
            "    SET PLAYER_ANGLE, 2.03;\n",
            padBody: Clip(5),
            padWall: "cv_w",
            padFloor: 4,
            padCeiling: 12),
        Case(
            "stand-pad",
            "The player stands in a pad of CLIP_DIST 30 looking +x through its open sides, the room without one: the room's far wall?",
            padBody: Clip(30),
            run: "    SET PLAYER_X, -8;\n    SET PLAYER_Y, 16;\n"),
        Case(
            "stand-room",
            "The reverse: the room 1000 and the pad (seen through open sides) unset; the player in the room with CLIP_DIST 60.",
            Clip(60),
            padBody: Clip(1000)),

        // Round 3: when is a sprite itself hidden?
        Case("s-40-c30", "A thing at 40 with CLIP_DIST 30, every wall out.", Clip(30), Sprites, placements: ["THING cv_t 40 0 0 0"]),
        Case("s-31-c30", "A thing at 31 with CLIP_DIST 30.", Clip(30), Sprites, placements: ["THING cv_t 31 0 0 0"]),
        Case("s-29-c30", "A thing at 29 with CLIP_DIST 30.", Clip(30), Sprites, placements: ["THING cv_t 29 0 0 0"]),
        Case("s-40-t50", "A thing at 40, CLIP_DIST 60, THING_DIST 0.5 (reach 30).", Clip(60), Sprites, "    SET THING_DIST, 0.5;\n", placements: ["THING cv_t 40 0 0 0"]),
        Case("s-40-t25", "A thing at 40, CLIP_DIST 60, THING_DIST 0.25 (reach 15).", Clip(60), Sprites, "    SET THING_DIST, 0.25;\n", placements: ["THING cv_t 40 0 0 0"]),
        Case("s-20-t25", "A thing at 20, CLIP_DIST 60, THING_DIST 0.25.", Clip(60), Sprites, "    SET THING_DIST, 0.25;\n", placements: ["THING cv_t 20 0 0 0"]),
        Case("s-40-t00", "A thing at 40, CLIP_DIST 60, THING_DIST 0.", Clip(60), Sprites, "    SET THING_DIST, 0;\n", placements: ["THING cv_t 40 0 0 0"]),
        Case("s-40-a25", "An actor at 40, CLIP_DIST 60, ACTOR_DIST 0.25.", Clip(60), Sprites, "    SET ACTOR_DIST, 0.25;\n", placements: ["ACTOR cv_a 40 0 0 0"]),
        Case("h-room", "A thing of HEIGHT 20 at (40, 0) in a room whose ceiling is 16: is it drawn over the ceiling?", declarations: High, placements: ["THING cv_h 40 0 0 0"]),
        Case("h-low", "A thing of HEIGHT -8 at (40, 0): is it drawn over the floor?", declarations: Low, placements: ["THING cv_h 40 0 0 0"]),
        Case("h-wall", "The high thing behind a free wall at 30: its top above the wall's top.", declarations: High + WallDecl, walls: [Frontal(30, 12)], placements: ["THING cv_h 40 0 0 0"]),
        Case("h-sky", "The high thing under a sky ceiling.", declarations: High, roomCeiling: "cv_sky", textures: SkyDecl, placements: ["THING cv_h 40 0 0 0"]),
        Case("h-pad", "A high thing in the pad seen from the room, the pad's ceiling 16.", declarations: High, run: "    SET PLAYER_ANGLE, 2.03;\n", padBody: "", placements: ["THING cv_h -8 16 0 2"]),

        // Round 4: whose CLIP_DIST counts for an object in another region, and a sprite beyond a step.
        Case("o-pad-tiny", "A thing in the pad (24 by axes), the pad's CLIP_DIST 5, the room's unset, seen from the room.", declarations: Sprites, run: "    SET PLAYER_ANGLE, 2.03;\n", padBody: Clip(5), placements: ["THING cv_t -8 16 0 2"]),
        Case("o-pad-big", "The same thing, the pad's CLIP_DIST unset, the room's 20.", Clip(20), Sprites, "    SET PLAYER_ANGLE, 2.03;\n", placements: ["THING cv_t -8 16 0 2"]),
        Case("o-step-low", "A high thing on the far side of a raised pad (a lower piece only): over the piece's top?", declarations: High + WallDecl, run: "    SET PLAYER_ANGLE, 2.03;\n", padBody: "", padWall: "cv_w", padFloor: 4, placements: ["THING cv_h -14 22 0 0"]),
        Case("o-step-open", "A plain thing on the far side of the pad at its base height.", declarations: Sprites + WallDecl, run: "    SET PLAYER_ANGLE, 2.03;\n", padBody: "", padWall: "cv_w", padFloor: 4, placements: ["THING cv_t -14 22 0 0"]),

        // Round 5: a sprite seen across a second region.
        Slab("p-none", 0, "THING cv_t -8 16 0 2", "A thing in the pad, no clip distance, level floor."),
        Slab("p-behind", 4, "THING cv_t -14 22 0 0", "A thing in the room behind a raised slab, with the ray across the slab."),
        Slab("p-behind-0", 0, "THING cv_t -14 22 0 0", "A thing in the room behind a level pad."),
        Slab("p-front", 4, "THING cv_t -3 7 0 0", "A thing in the room in front of the slab."),
        Slab("p-top", 4, "THING cv_t -8 16 0 2", "A thing on the slab."),
        Slab("p-beside", 4, "THING cv_t 0 22 0 0", "A thing in the room beside the slab, at the same distance, no ray across it."),
        Slab("p-wide", 4, "THING cv_w2 -14 22 0 0", "A wide thing behind the slab, part of it past the slab's edge.", "THING cv_w2 { TEXTURE cv_wt2; }\n", "TEXTURE cv_wt2 { SCALE_XY 2, 2; BMAPS cv_bm; }\n"),

        // Round 6: a level border in front of a wall out of reach, seen from a pad and in a chain.
        Case("q-vis", "A level pad with visible walls, the room's CLIP_DIST 20 (the room's walls out, the pad's in): are the pad's floor and ceiling kept?", Clip(20), WallDecl, "    SET PLAYER_ANGLE, 2.03;\n", padBody: "", padWall: "cv_w"),
        Case("q-invis", "The same with invisible pad walls (the round 4 reading o-pad-big, without a thing).", Clip(20), run: "    SET PLAYER_ANGLE, 2.03;\n", padBody: ""),
        Case("q-color", "A level pad of its own floor colour, invisible walls: which rows does the fill take?", Clip(20), run: "    SET PLAYER_ANGLE, 2.03;\n", padBody: "FLOOR_TEX blu_tex; CEIL_TEX red_tex;"),
        Case("q-color-vis", "A level pad of its own floor colour, visible walls.", Clip(20), WallDecl, "    SET PLAYER_ANGLE, 2.03;\n", padBody: "FLOOR_TEX blu_tex; CEIL_TEX red_tex;", padWall: "cv_w"),
        Case("q-near-color", "The room in reach and the pad's CLIP_DIST 5 with its own colour, invisible walls: from the room the far pad walls are out.", declarations: "", run: "    SET PLAYER_ANGLE, 2.03;\n", padBody: "FLOOR_TEX blu_tex; CEIL_TEX red_tex; CLIP_DIST 5;"),
    ];

    private const string SkyDecl =
        "TEXTURE cv_sky { SCALE_X 4; SCALE_Y 4; BMAPS grn_map; FLAGS SKY; }\n";

    /// <summary>The probe as the generator consumes it.</summary>
    public static readonly TraceProbeSet Set = new(
        "clip-view",
        TraceFile,
        "ovcvs",
        "The final screenshot `OVCVS0.PCX` is the reading: index 1 is the clip colour, index 11 a thing, 10 an\n" +
        "actor. The trace records the skills as the runtime holds them.",
        All);
}
