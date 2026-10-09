// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenVirtue.Formats.Pcx;
using OpenVirtue.Formats.Wdl;
using OpenVirtue.Formats.Wmp;

/// <summary>
/// Builds loose-file carriers for the three 2026-10-09 projection probes
/// (<c>docs/clean-room/observations/OV-20261009-*.md</c>): one synthetic room per parameter
/// value, screenshotted from inside the engine with the documented <c>SCREENSHOT</c>
/// instruction after a settling <c>WAITT</c>. Everything in a carrier is generated here
/// (WDL, WMP, flat-color PCX textures, palette); the only retail input is the user's patched
/// <c>VRUN.EXE</c>, copied in by path.
/// </summary>
internal static class ProjectionProbeGenerator
{
    public const string FovProbeName = "projection-fov";
    public const string AspectProbeName = "pixel-aspect";
    public const string TiltProbeName = "tilt-model";

    private const string FovProbeId = "OV-20261009-projection-fov-player-arc";
    private const string AspectProbeId = "OV-20261009-pixel-aspect-video-modes";
    private const string TiltProbeId = "OV-20261009-tilt-model-shear-vs-pitch";

    private static readonly string[] DefaultFovVideos = ["S640x480"];

    /// <summary>
    /// Skills dumped to OVPRB0.TXT and the short labels used for them: the patched V3.963
    /// compiler rejects inline string literals longer than 12 characters
    /// ("Error(90): Filename length", observed 2026-10-09), so labels stay at 6 or fewer.
    /// </summary>
    private static readonly (string Skill, string Label)[] StateLabels =
    [
        ("PLAYER_ARC", "ARC"), ("PLAYER_TILT", "TILT"), ("PLAYER_X", "PX"), ("PLAYER_Y", "PY"),
        ("PLAYER_Z", "PZ"), ("PLAYER_ANGLE", "ANG"), ("PLAYER_SIZE", "PSIZE"), ("FLOOR_HGT", "FLOOR"),
        ("CEIL_HGT", "CEIL"), ("SCREEN_X", "SX"), ("SCREEN_Y", "SY"), ("SCREEN_WIDTH", "SW"),
        ("SCREEN_HGT", "SH"), ("MOTION_BLUR", "BLUR"), ("ASPECT", "ASPECT"),
    ];
    private static readonly string[] DefaultAspectVideos = ["S640x480", "320x200", "X320x240", "X320x400"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Palette indices used by the fixtures. 0..2 keep their documented special meaning.</summary>
    private enum Ink : byte
    {
        Transparent = 0,
        Infinity = 1,
        Brightness = 2,
        Red = 3,
        Green = 4,
        Blue = 5,
        Yellow = 6,
        Magenta = 7,
        Floor = 8,
        Ceiling = 9,
        White = 255,
    }

    /// <summary>
    /// The synthetic room, in steps, y-up, player at the origin looking along +X.
    /// The far wall at x = <see cref="Depth"/> is split into <see cref="StripeWidth"/>-wide
    /// segments of alternating red/green plus an optional centered yellow target of
    /// <see cref="TargetWidth"/>; side walls are blue, the wall behind the player magenta.
    /// </summary>
    private sealed record Room(
        int HalfWidth,
        int Depth,
        int BackDepth,
        int CeilingHeight,
        int StripeWidth,
        int TargetWidth,
        int StepHeight = 0,
        int AlcoveDepth = 0,
        int EyeHeight = 8)
    {
        /// <summary>
        /// FOV / pixel-aspect room: 48 wide, 48 deep, 16 high, 4-step stripes, 16x16 target face.
        /// The eye is raised to 8 (SET PLAYER_SIZE/PLAYER_Z in IF_START) so the face is centered
        /// vertically and stays in frame up to f_v ≈ 1440 px (ASPECT 2, PLAYER_ARC 0.5).
        /// </summary>
        public static readonly Room Face = new(HalfWidth: 24, Depth: 48, BackDepth: 16, CeilingHeight: 16, StripeWidth: 4, TargetWidth: 16);

        /// <summary>
        /// Tilt room: narrow and tall so stripes stay in frame while the horizon moves. A raised
        /// alcove (floor at <see cref="StepHeight"/>) behind the far wall turns the stripe face into
        /// a 40-step step whose top edge is a geometric horizontal line 32 steps above the eye:
        /// off-frame at tilt 0 by design (there the floor junction and the side walls give the eye
        /// line) and, under a y-shear of f·tilt with f ≈ 554, at rows ≈ 148 (tilt 0.5) and ≈ 425
        /// (tilt 1.0) of a 480-line frame. Negative tilts keep the floor junction in frame instead.
        /// </summary>
        public static readonly Room Tall = new(HalfWidth: 12, Depth: 48, BackDepth: 16, CeilingHeight: 64, StripeWidth: 4, TargetWidth: 0, StepHeight: 40, AlcoveDepth: 16);

        public bool HasAlcove => StepHeight > 0 && AlcoveDepth > 0;

        /// <summary>Height of the striped face: the step height with an alcove, else the room height.</summary>
        public int FarWallTop => HasAlcove ? StepHeight : CeilingHeight;

        /// <summary>Far-wall segment boundaries from y = -HalfWidth to +HalfWidth.</summary>
        public List<int> FarWallBoundaries()
        {
            var boundaries = new List<int>();
            int halfTarget = TargetWidth / 2;
            for (int y = -HalfWidth; y <= HalfWidth; y += StripeWidth)
            {
                if (TargetWidth > 0 && y > -halfTarget && y < halfTarget)
                {
                    continue;
                }

                boundaries.Add(y);
            }

            return boundaries;
        }
    }

    private sealed record Variant(
        string Label,
        string Video,
        double Arc,
        double? Aspect,
        double Tilt,
        Room Room,
        string ScreenshotName);

    private sealed record Plan(string ProbeId, string ProbeName, IReadOnlyList<Variant> Variants);

    public static int Prepare(string probeName, string[] args)
    {
        string outputDir = Path.Combine("_research", "oracle-runs", ProbeId(probeName));
        string? runtime = null;
        string? dosboxX = null;
        string[]? videos = null;
        double[]? arcs = null;
        double[]? aspects = null;
        double[]? tilts = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--runtime":
                    runtime = RequireValue(args, ref i, arg);
                    break;
                case "--dosbox-x":
                    dosboxX = RequireValue(args, ref i, arg);
                    break;
                case "--videos":
                    videos = RequireValue(args, ref i, arg).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
                case "--arcs":
                    arcs = ParseNumbers(RequireValue(args, ref i, arg));
                    break;
                case "--aspects":
                    aspects = ParseNumbers(RequireValue(args, ref i, arg));
                    break;
                case "--tilts":
                    tilts = ParseNumbers(RequireValue(args, ref i, arg));
                    break;
                case "-h" or "--help":
                    return Usage();
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Unknown option '{arg}'.");
                    }

                    outputDir = arg;
                    break;
            }
        }

        foreach (string video in videos ?? [])
        {
            VideoSize(video);
        }

        Plan plan = BuildPlan(probeName, videos, arcs, aspects, tilts);
        string fullOutputDir = Path.GetFullPath(outputDir);
        Directory.CreateDirectory(fullOutputDir);

        string? runtimePath = runtime is null ? null : Path.GetFullPath(runtime);
        if (runtimePath is not null && !File.Exists(runtimePath))
        {
            throw new FileNotFoundException("Patched runtime not found.", runtimePath);
        }

        var runAll = new StringBuilder();
        runAll.Append("#!/usr/bin/env sh\n# Generated by ovtool oracle prepare; runs every variant in order. Local-only.\nset -u\nDIR=\"$(cd \"$(dirname \"$0\")\" && pwd)\"\nRC=0\n");
        foreach (Variant variant in plan.Variants)
        {
            string variantDir = Path.Combine(fullOutputDir, variant.Label);
            OracleLaunchers.ParkIfExists(variantDir);
            WriteVariant(plan, variant, variantDir, runtimePath, dosboxX);
            runAll.Append(CultureInfo.InvariantCulture, $"sh \"$DIR/{variant.Label}/run.sh\" || RC=1\n");
        }

        runAll.Append("exit $RC\n");
        string runAllPath = Path.Combine(fullOutputDir, "run-all.sh");
        File.WriteAllText(runAllPath, runAll.ToString(), Utf8NoBom);
        File.WriteAllText(Path.Combine(fullOutputDir, "README.md"), Readme(plan, fullOutputDir, runtimePath), Utf8NoBom);

        Console.WriteLine($"Prepared {plan.ProbeId} in {fullOutputDir}");
        foreach (Variant variant in plan.Variants)
        {
            Console.WriteLine($"  {variant.Label}: {Path.Combine(fullOutputDir, variant.Label, "run.sh")}");
        }

        Console.WriteLine($"  all:     {runAllPath}");
        Console.WriteLine(runtimePath is null
            ? "  no runtime copied; copy the patched VRUN.EXE into each work/ or rerun with --runtime <exe>."
            : $"  runtime: {runtimePath} copied into each work/ as VRUN.EXE");
        return 0;
    }

    private static string ProbeId(string probeName) => probeName switch
    {
        FovProbeName => FovProbeId,
        AspectProbeName => AspectProbeId,
        TiltProbeName => TiltProbeId,
        _ => throw new ArgumentException($"Unknown projection probe '{probeName}'."),
    };

    private static Plan BuildPlan(string probeName, string[]? videos, double[]? arcs, double[]? aspects, double[]? tilts)
    {
        var variants = new List<Variant>();
        switch (probeName)
        {
            case FovProbeName:
                foreach (string video in videos ?? DefaultFovVideos)
                {
                    foreach (double arc in arcs ?? [0.5, 1.0, 1.5, 2.0])
                    {
                        variants.Add(new Variant($"{video}-arc-{Num(arc)}", video, arc, null, 0, Room.Face, "ovfov"));
                    }
                }

                // Width-scaling cross-check at the default arc in the low-res modes.
                if (videos is null)
                {
                    variants.Add(new Variant("320x200-arc-1", "320x200", 1.0, null, 0, Room.Face, "ovfov"));
                    variants.Add(new Variant("X320x400-arc-1", "X320x400", 1.0, null, 0, Room.Face, "ovfov"));
                }

                break;
            case AspectProbeName:
                foreach (string video in videos ?? DefaultAspectVideos)
                {
                    foreach (double aspect in aspects ?? (video == "S640x480" ? [1.0, 0.5, 2.0] : [1.0]))
                    {
                        variants.Add(new Variant($"{video}-aspect-{Num(aspect)}", video, 1.0, aspect, 0, Room.Face, "ovasp"));
                    }
                }

                break;
            case TiltProbeName:
                foreach (string video in videos ?? DefaultFovVideos)
                {
                    // Sign-symmetric by default: the manual gives no sign for PLAYER_TILT, and the
                    // first batch must both resolve the sign and leave an in-frame measurement.
                    // --arcs / --aspects cross the tilts with other focal lengths, to separate a
                    // shift proportional to SCREEN_HGT from one proportional to the vertical scale.
                    foreach (double tilt in tilts ?? [0.0, 0.5, 1.0, -0.5, -1.0])
                    {
                        foreach (double arc in arcs ?? [1.0])
                        {
                            foreach (double? aspect in aspects?.Select(static a => (double?)a) ?? [null])
                            {
                                string label = $"{video}-tilt-{Label(tilt)}" +
                                    (arc != 1.0 ? $"-arc-{Num(arc)}" : "") +
                                    (aspect is { } a && a != 1.0 ? $"-aspect-{Num(a)}" : "");
                                variants.Add(new Variant(label, video, arc, aspect, tilt, Room.Tall, "ovtlt"));
                            }
                        }
                    }
                }

                break;
            default:
                throw new ArgumentException($"Unknown projection probe '{probeName}'.");
        }

        return new Plan(ProbeId(probeName), probeName, variants);
    }

    private static void WriteVariant(Plan plan, Variant variant, string variantDir, string? runtimePath, string? dosboxX)
    {
        string workDir = Path.Combine(variantDir, "work");
        Directory.CreateDirectory(workDir);

        SyntheticPcx.Palette palette = BuildPalette();
        string wdl = Wdl(plan, variant);
        string wmp = Wmp(variant.Room);
        WdlParser.Parse(wdl);
        WmpMap.Read(wmp);

        File.WriteAllText(Path.Combine(workDir, OracleLaunchers.WorldName + ".WDL"), wdl, Encoding.Latin1);
        File.WriteAllText(Path.Combine(workDir, OracleLaunchers.WorldName + ".WMP"), wmp, Encoding.Latin1);

        WriteTexture(workDir, "PRBRED.PCX", Ink.Red, palette);
        WriteTexture(workDir, "PRBGRN.PCX", Ink.Green, palette);
        WriteTexture(workDir, "PRBYEL.PCX", Ink.Yellow, palette);
        WriteTexture(workDir, "PRBBLU.PCX", Ink.Blue, palette);
        WriteTexture(workDir, "PRBMAG.PCX", Ink.Magenta, palette);
        WriteTexture(workDir, "PRBFLR.PCX", Ink.Floor, palette);
        WriteTexture(workDir, "PRBCEL.PCX", Ink.Ceiling, palette);
        WriteValidated(Path.Combine(workDir, "PRBPAL.PCX"), SyntheticPcx.Swatch(palette));

        if (runtimePath is not null)
        {
            File.Copy(runtimePath, Path.Combine(workDir, "VRUN.EXE"), overwrite: true);
        }

        File.WriteAllText(Path.Combine(variantDir, "geometry.json"), GeometryJson(variant, palette), Utf8NoBom);
        OracleLaunchers.Write(variantDir, new OracleLaunchers.Options(
            Title: $"{plan.ProbeId} {variant.Label}",
            VariantLabel: $"{plan.ProbeName}/{variant.Label}",
            WorkDir: workDir,
            DosboxX: dosboxX,
            CompletionMarker: "OVEND"));
    }

    private static void WriteTexture(string workDir, string name, Ink ink, SyntheticPcx.Palette palette) =>
        WriteValidated(Path.Combine(workDir, name), SyntheticPcx.Solid(64, 64, (byte)ink, palette));

    private static void WriteValidated(string path, byte[] pcx)
    {
        PcxImage image = PcxImage.Read(pcx);
        if (image.Width <= 0 || image.Height <= 0)
        {
            throw new InvalidDataException($"Generated PCX {path} did not round-trip through the reader.");
        }

        File.WriteAllBytes(path, pcx);
    }

    private static SyntheticPcx.Palette BuildPalette()
    {
        var palette = new SyntheticPcx.Palette();
        palette.Set((int)Ink.Transparent, 0, 0, 0);
        palette.Set((int)Ink.Infinity, 0, 0, 0);
        palette.Set((int)Ink.Brightness, 255, 255, 255);
        palette.Set((int)Ink.Red, 220, 32, 32);
        palette.Set((int)Ink.Green, 32, 200, 32);
        palette.Set((int)Ink.Blue, 40, 80, 230);
        palette.Set((int)Ink.Yellow, 240, 220, 40);
        palette.Set((int)Ink.Magenta, 220, 40, 220);
        palette.Set((int)Ink.Floor, 120, 120, 120);
        palette.Set((int)Ink.Ceiling, 60, 60, 60);
        palette.Set((int)Ink.White, 255, 255, 255);
        return palette;
    }

    private static string Wdl(Plan plan, Variant variant)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $$"""
            // Synthetic OpenVirtue clean-room oracle probe fixture ({{plan.ProbeId}}, variant {{variant.Label}}).
            // Generated by `ovtool oracle prepare {{plan.ProbeName}}`; local-only, do not commit.
            // Room: player at the origin looking along +X at a striped far wall {{variant.Room.Depth}} steps away.

            DITHER 0;
            VIDEO {{variant.Video}};
            NEXUS 14;
            MAPFILE <TITLE.WMP>;
            BIND <TITLE.WDL>;

            PALETTE probe_pal { PALFILE <PRBPAL.PCX>; }

            BMAP red_map,<PRBRED.PCX>;
            BMAP grn_map,<PRBGRN.PCX>;
            BMAP yel_map,<PRBYEL.PCX>;
            BMAP blu_map,<PRBBLU.PCX>;
            BMAP mag_map,<PRBMAG.PCX>;
            BMAP flr_map,<PRBFLR.PCX>;
            BMAP cel_map,<PRBCEL.PCX>;

            TEXTURE red_tex { SCALE_X 4; SCALE_Y 4; BMAPS red_map; }
            TEXTURE grn_tex { SCALE_X 4; SCALE_Y 4; BMAPS grn_map; }
            TEXTURE yel_tex { SCALE_X 4; SCALE_Y 4; BMAPS yel_map; }
            TEXTURE blu_tex { SCALE_X 4; SCALE_Y 4; BMAPS blu_map; }
            TEXTURE mag_tex { SCALE_X 4; SCALE_Y 4; BMAPS mag_map; }
            TEXTURE flr_tex { SCALE_X 4; SCALE_Y 4; BMAPS flr_map; }
            TEXTURE cel_tex { SCALE_X 4; SCALE_Y 4; BMAPS cel_map; }

            WALL red_wall { TEXTURE red_tex; }
            WALL grn_wall { TEXTURE grn_tex; }
            WALL yel_wall { TEXTURE yel_tex; }
            WALL blu_wall { TEXTURE blu_tex; }
            WALL mag_wall { TEXTURE mag_tex; }

            REGION room
            {
                FLOOR_HGT 0;
                CEIL_HGT {{variant.Room.CeilingHeight}};
                FLOOR_TEX flr_tex;
                CEIL_TEX cel_tex;
            }
            REGION solid
            {
                FLOOR_HGT {{variant.Room.CeilingHeight}};
                CEIL_HGT {{variant.Room.CeilingHeight}};
                FLOOR_TEX flr_tex;
                CEIL_TEX cel_tex;
                CLIP_DIST 0;
            }
            {{(variant.Room.HasAlcove ? AlcoveRegionWdl(variant.Room) : "")}}
            ACTION probe_start
            {
                PRINTFILE "ovsta", 0;
                PRINT_STRING "START\n";
                SET PLAYER_ARC, {{Num(variant.Arc)}};
                SET PLAYER_TILT, {{Num(variant.Tilt)}};
                SET PLAYER_SIZE, {{variant.Room.EyeHeight}};
                SET PLAYER_Z, {{variant.Room.EyeHeight}};

            """);

        if (variant.Aspect is { } aspect && aspect != 1.0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"    SET ASPECT, {Num(aspect)};\n");
        }

        sb.Append("""
                SET MOTION_BLUR, 0;
                SET RENDER_MODE, 2;
                WAITT 16;
                PRINTFILE "ovprb", 0;

            """);

        foreach ((string skill, string label) in StateLabels)
        {
            if (skill == "ASPECT" && (variant.Aspect is not { } printedAspect || printedAspect == 1.0))
            {
                continue;
            }

            sb.Append(CultureInfo.InvariantCulture, $"    PRINT_STRING \"{label}=\";\n    PRINT_VALUE {skill};\n    PRINT_STRING \"\\n\";\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $$"""
                SCREENSHOT "{{variant.ScreenshotName}}", 0;
                WAITT 4;
                PRINTFILE "ovend", 0;
                PRINT_STRING "END\n";
                EXIT;
            }

            IF_START probe_start;

            """);
        return sb.ToString().Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    private static string AlcoveRegionWdl(Room room) =>
        $$"""
        REGION alcove
        {
            FLOOR_HGT {{room.StepHeight}};
            CEIL_HGT {{room.CeilingHeight}};
            FLOOR_TEX flr_tex;
            CEIL_TEX cel_tex;
        }

        """;

    /// <summary>
    /// Counter-clockwise (y-up) room outline with the interior region listed first on every
    /// wall. The manual (p.17) names the WMP wall fields <c>rr rl</c> but describes them as
    /// "left and right"; the fourth field is written here as the region on the left of
    /// v1→v2 in a y-up frame, which matches the OpenVirtue reader, the OV-20260701 fixture and
    /// the convention observed in the retail maps around their player starts. With an alcove,
    /// the far-wall segments separate <c>room</c> (left) from <c>alcove</c> (right) and the
    /// alcove is closed by its own three walls.
    /// </summary>
    private static string Wmp(Room room)
    {
        var sb = new StringBuilder();
        var vertices = new List<(int X, int Y)>
        {
            (-room.BackDepth, -room.HalfWidth),
        };
        List<int> boundaries = room.FarWallBoundaries();
        foreach (int y in boundaries)
        {
            vertices.Add((room.Depth, y));
        }

        vertices.Add((-room.BackDepth, room.HalfWidth));
        int farStart = 1;
        int farEnd = vertices.Count - 2;
        int alcoveSouth = -1;
        int alcoveNorth = -1;
        if (room.HasAlcove)
        {
            alcoveSouth = vertices.Count;
            vertices.Add((room.Depth + room.AlcoveDepth, -room.HalfWidth));
            alcoveNorth = vertices.Count;
            vertices.Add((room.Depth + room.AlcoveDepth, room.HalfWidth));
        }

        for (int i = 0; i < vertices.Count; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"VERTEX {vertices[i].X} {vertices[i].Y} 0;#{i}\r\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"REGION room 0 {room.CeilingHeight};#0\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"REGION solid {room.CeilingHeight} {room.CeilingHeight};#1\r\n");
        if (room.HasAlcove)
        {
            sb.Append(CultureInfo.InvariantCulture, $"REGION alcove {room.StepHeight} {room.CeilingHeight};#2\r\n");
        }

        int wall = 0;
        int farRight = room.HasAlcove ? 2 : 1;
        sb.Append(CultureInfo.InvariantCulture, $"WALL blu_wall 0 {farStart} 0 1 0 0;#{wall++}\r\n");
        int halfTarget = room.TargetWidth / 2;
        for (int v = farStart; v < farEnd; v++)
        {
            int y0 = vertices[v].Y;
            int y1 = vertices[v + 1].Y;
            bool isTarget = room.TargetWidth > 0 && y0 == -halfTarget && y1 == halfTarget;
            int segmentIndex = v - farStart;
            string wallName = isTarget ? "yel_wall" : (segmentIndex % 2 == 0 ? "red_wall" : "grn_wall");
            sb.Append(CultureInfo.InvariantCulture, $"WALL {wallName} {v} {v + 1} 0 {farRight} 0 0;#{wall++}\r\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"WALL blu_wall {farEnd} {farEnd + 1} 0 1 0 0;#{wall++}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"WALL mag_wall {farEnd + 1} 0 0 1 0 0;#{wall++}\r\n");
        if (room.HasAlcove)
        {
            sb.Append(CultureInfo.InvariantCulture, $"WALL blu_wall {farStart} {alcoveSouth} 2 1 0 0;#{wall++}\r\n");
            sb.Append(CultureInfo.InvariantCulture, $"WALL mag_wall {alcoveSouth} {alcoveNorth} 2 1 0 0;#{wall++}\r\n");
            sb.Append(CultureInfo.InvariantCulture, $"WALL blu_wall {alcoveNorth} {farEnd} 2 1 0 0;#{wall++}\r\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"PLAYER_START 0 0 0 0;#{wall}\r\n");
        return sb.ToString();
    }

    private static string GeometryJson(Variant variant, SyntheticPcx.Palette palette)
    {
        (int width, int height) = VideoSize(variant.Video);
        var segments = new List<object>();
        List<int> boundaries = variant.Room.FarWallBoundaries();
        int halfTarget = variant.Room.TargetWidth / 2;
        for (int i = 0; i + 1 < boundaries.Count; i++)
        {
            bool isTarget = variant.Room.TargetWidth > 0 && boundaries[i] == -halfTarget && boundaries[i + 1] == halfTarget;
            segments.Add(new
            {
                y0 = boundaries[i],
                y1 = boundaries[i + 1],
                color = isTarget ? "yellow" : (i % 2 == 0 ? "red" : "green"),
            });
        }

        object geometry = new
        {
            variant = variant.Label,
            video = variant.Video,
            screen = new { width, height },
            player_arc = variant.Arc,
            player_tilt = variant.Tilt,
            aspect = variant.Aspect ?? 1.0,
            screenshot = variant.ScreenshotName.ToUpperInvariant() + "0.PCX",
            state_file = "OVPRB0.TXT",
            console_file = "OVCON.TXT",
            state_labels = StateLabels.ToDictionary(static p => p.Label, static p => p.Skill),
            room = new
            {
                half_width = variant.Room.HalfWidth,
                depth = variant.Room.Depth,
                back_depth = variant.Room.BackDepth,
                floor = 0,
                ceiling = variant.Room.CeilingHeight,
                stripe_width = variant.Room.StripeWidth,
                target_width = variant.Room.TargetWidth,
                far_wall_top = variant.Room.FarWallTop,
                alcove_depth = variant.Room.AlcoveDepth,
                eye_height = variant.Room.EyeHeight,
            },
            far_wall_segments = segments,
            colors = new Dictionary<string, int[]>
            {
                ["red"] = Rgb(palette, Ink.Red),
                ["green"] = Rgb(palette, Ink.Green),
                ["blue"] = Rgb(palette, Ink.Blue),
                ["yellow"] = Rgb(palette, Ink.Yellow),
                ["magenta"] = Rgb(palette, Ink.Magenta),
                ["floor"] = Rgb(palette, Ink.Floor),
                ["ceiling"] = Rgb(palette, Ink.Ceiling),
                ["black"] = Rgb(palette, Ink.Infinity),
            },
        };
        return JsonSerializer.Serialize(geometry, JsonOptions);
    }

    private static int[] Rgb(SyntheticPcx.Palette palette, Ink ink)
    {
        int i = (int)ink * 3;
        ReadOnlySpan<byte> rgb = palette.Rgb;
        return [rgb[i], rgb[i + 1], rgb[i + 2]];
    }

    private static (int Width, int Height) VideoSize(string video) => video.ToUpperInvariant() switch
    {
        "320X200" => (320, 200),
        "X320X240" => (320, 240),
        "X320X400" => (320, 400),
        "S640X480" => (640, 480),
        "S800X600" => (800, 600),
        _ => throw new ArgumentException($"Unknown VIDEO mode '{video}'. Use one of 320x200, X320x240, X320x400, S640x480, S800x600."),
    };

    private static string Readme(Plan plan, string outputDir, string? runtimePath) =>
        $$"""
        # {{plan.ProbeId}} — local-only oracle run folder

        Generated by `ovtool oracle prepare {{plan.ProbeName}}`. Nothing here may be committed; reduce
        results into `docs/clean-room/observations/{{plan.ProbeId}}.md`.

        ## Layout

        One carrier per parameter value, each with `work/` (mounted as C:), `dosbox-x.conf` +
        `run.sh` (native Linux DOSBox-X) and `dosbox.conf` + `run.bat` (Windows), plus a
        `geometry.json` the measurement script reads. `run-all.sh` runs every variant in order.
        Each `run.sh` parks previous `OV*.PCX`/`OV*.TXT` outputs under `work/_stale/<stamp>/`
        (never deletes) and appends a row to `launches.md` here.

        Variants:
        {{string.Join("\n", plan.Variants.Select(v => $"- `{v.Label}`: VIDEO {v.Video}, PLAYER_ARC {Num(v.Arc)}, PLAYER_TILT {Num(v.Tilt)}, ASPECT {Num(v.Aspect ?? 1.0)}, room {(v.Room == Room.Tall ? "tall/narrow" : "face")}"))}}

        ## Fixture

        A single rectangular region (`room`, floor 0) closed by a solid border region. The player
        starts at the origin facing +X. The far wall at x = depth is split into stripe-wide
        segments of alternating red/green (plus a centered yellow target face on the `face`
        room, exactly as wide as the room is high so it is a square face). Side walls are blue,
        the wall behind the player magenta, floor mid-gray, ceiling dark gray. `IF_START` raises
        the eye to z = 8 (`SET PLAYER_SIZE`/`PLAYER_Z`) so the face is vertically centered. The
        `tall` room adds a raised `alcove` region (floor 40, magenta back wall) behind the far
        wall, so the striped face is a 40-step step whose top edge is a geometric line 32 steps
        above the eye: off-frame at tilt 0 (use the floor junction and side walls there), in frame
        for positive tilts under the shear model; negative tilts keep the floor junction. The
        palette has no
        shading RANGEs (manual p.34: no ranges = shading off) and DITHER is 0, so every rendered
        pixel is a flat palette index. All textures are generated 64x64 solids at SCALE 4 px/step.

        `IF_START` sets the probe skills, waits 16 ticks, writes the live skill values to
        `OVPRB0.TXT` (PRINTFILE/PRINT_VALUE), grabs `SCREENSHOT` into `<name>0.PCX`, waits 4 ticks
        and EXITs; the DOSBox-X autoexec then exits.

        Expected outputs per variant: `OVDIR.TXT` (what DOS saw in C:), `OVCON.TXT` (the
        runtime's console output: banner, Genesis compiler errors), `OVSTA0.TXT` (START),
        `OVPRB0.TXT` (skill dump, short labels mapped back by geometry.json `state_labels`),
        `<screenshot>0.PCX`, `OVEND0.TXT` (END). A missing screenshot with a present END marker
        means SCREENSHOT is unusable on this runtime: fall back to DOSBox-X's capture key and
        say so in the note.

        ## Runtime

        {{runtimePath ?? "(not copied; copy the patched VRUN.EXE into each work/)"}}

        The runtime is launched as `{{OracleLaunchers.RuntimeCommand}}` (the carrier is always named
        TITLE.WDL/TITLE.WMP, the form that booted in OV-20260701). DOSBox-X: forcerate=60,
        aspect=false, output=surface, autolock=false; no SaintsX setup scripts are involved.

        ## Measure

        ```sh
        _research/oracle-runs/.venv/bin/python tools/oracle/measure_projection.py {{outputDir}}/[!_]*/
        ```
        """;

    private static double[] ParseNumbers(string list) =>
        list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : throw new ArgumentException($"'{s}' is not a number (expected a comma-separated list like 0.5,1,2)."))
            .ToArray();

    private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Directory-name-safe number: negatives get a <c>neg</c> prefix instead of a dash.</summary>
    private static string Label(double value) => value < 0 ? "neg" + Num(-value) : Num(value);

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }

    public const string UsageLines =
        """
          ovtool oracle prepare projection-fov [output-dir] [--runtime <VRUN-patched.EXE>] [--dosbox-x <exe>] [--arcs 0.5,1,1.5,2] [--videos S640x480]
          ovtool oracle prepare pixel-aspect   [output-dir] [--runtime <VRUN-patched.EXE>] [--dosbox-x <exe>] [--aspects 1,0.5,2] [--videos S640x480,320x200,X320x240,X320x400]
          ovtool oracle prepare tilt-model     [output-dir] [--runtime <VRUN-patched.EXE>] [--dosbox-x <exe>] [--tilts 0,0.5,1,-0.5,-1] [--arcs 1] [--aspects 1] [--videos S640x480]
        """;

    public static int Usage(string? error = null)
    {
        if (error is not null)
        {
            Console.Error.WriteLine($"error: {error}");
        }

        Console.WriteLine("Usage:");
        Console.WriteLine(UsageLines);
        return error is null ? 0 : 2;
    }
}
