// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Formats.Pcx;
using OpenVirtue.Formats.Wdl;
using OpenVirtue.Formats.Wmp;

/// <summary>
/// Builds loose-file carriers for the 2026-10-09 projection probes
/// (<c>docs/clean-room/observations/OV-20261009-*.md</c>): one synthetic room per parameter
/// value, screenshotted from inside the engine with the documented <c>SCREENSHOT</c>
/// instruction after a settling <c>WAITT</c>. The <c>shoot-mapping</c> probe adds three
/// THING targets with <c>IF_HIT</c> marker actions and fires one <c>SHOOT</c> per variant.
/// Everything in a carrier is generated here (WDL, WMP, flat-color PCX textures, palette);
/// the only retail input is the user's patched <c>VRUN.EXE</c>, copied in by path.
/// </summary>
internal static class ProjectionProbeGenerator
{
    public const string FovProbeName = "projection-fov";
    public const string AspectProbeName = "pixel-aspect";
    public const string TiltProbeName = "tilt-model";
    public const string ShootProbeName = "shoot-mapping";
    public const string BranchProbeName = "branch-semantics";
    public const string EachSecProbeName = "each-sec-cadence";
    public const string FlicProbeName = "flic-play";
    public const string FlicDetailProbeName = "flic-detail";
    public const string ProximityProbeName = "proximity-events";
    public const string WallDistanceProbeName = "wall-distance";
    public const string WallPassClipProbeName = "wall-pass-clip";
    public const string ClipContactProbeName = "clip-contact";
    public const string BorderClimbProbeName = "border-climb";
    public const string EyeClampProbeName = "eye-clamp";
    public const string ActorContactProbeName = "actor-contact";
    public const string BorderKindsProbeName = "border-kinds";
    public const string ClipDetailProbeName = "clip-detail";
    public const string ViewActivityProbeName = "view-activity";
    public const string DistanceRefreshProbeName = "distance-refresh";
    public const string SchedulerOrderProbeName = "scheduler-order";
    public const string ActorEventsProbeName = "actor-events";
    public const string NumericEdgeProbeName = "numeric-edge";
    public const string MotionTimingProbeName = "motion-timing";
    public const string SaveFormatProbeName = "save-format";
    public const string SkyProbeName = "sky";
    public const string ShadingProbeName = "shading";
    public const string SlopeProbeName = "slope";
    public const string AudioProbeName = "audio";
    public const string AutorangeProbeName = "autorange";

    private const string FovProbeId = "OV-20261009-projection-fov-player-arc";
    private const string AspectProbeId = "OV-20261009-pixel-aspect-video-modes";
    private const string TiltProbeId = "OV-20261009-tilt-model-shear-vs-pitch";
    private const string ShootProbeId = "OV-20261009-shoot-mapping";
    private const string BranchProbeId = "OV-20261015-branch-tail-jump";
    private const string EachSecProbeId = "OV-20261015-each-sec-cadence";
    private const string FlicProbeId = "OV-20261015-flic-play-blocking";
    private const string FlicDetailProbeId = "OV-20261015-flic-scheduler-detail";
    private const string ProximityProbeId = "OV-20261019-proximity-events";
    private const string WallDistanceProbeId = "OV-20261019-wall-distance";
    private const string WallPassClipProbeId = "OV-20261019-wall-pass-clip";
    private const string ClipContactProbeId = "OV-20261019-clip-contact";
    private const string BorderClimbProbeId = "OV-20261019-border-climb";
    private const string EyeClampProbeId = "OV-20261019-eye-clamp";
    private const string ActorContactProbeId = "OV-20261019-actor-contact";
    private const string BorderKindsProbeId = "OV-20261019-border-kinds";
    private const string ClipDetailProbeId = "OV-20261019-clip-detail";
    private const string ViewActivityProbeId = "OV-20261020-view-activity";
    private const string DistanceRefreshProbeId = "OV-20261021-distance-refresh";
    private const string SchedulerOrderProbeId = "OV-20261029-scheduler-order";
    private const string ActorEventsProbeId = "OV-20261029-actor-events";
    private const string NumericEdgeProbeId = "OV-20261029-numeric-edge";
    private const string MotionTimingProbeId = "OV-20261029-motion-timing";
    private const string SaveFormatProbeId = "OV-20261029-save-format";
    private const string SkyProbeId = "OV-20261029-sky";
    private const string ShadingProbeId = "OV-20261029-shading";
    private const string SlopeProbeId = "OV-20261029-slope";
    private const string AudioProbeId = "OV-20261029-audio";
    private const string AutorangeProbeId = "OV-20261029-autorange";

    /// <summary>
    /// SHOOT targets: three THINGs standing inside the <see cref="Room.Face"/> room at
    /// x = <see cref="TargetDepth"/> (the far wall is at 48), centered on map y = +12 (left on
    /// the original's screen, OV-20261009-tilt-model-shear-vs-pitch), 0 and −12. Each is
    /// <see cref="TargetWidthSteps"/> × <see cref="TargetHeightSteps"/> steps: a 64 × 256 px
    /// bitmap at SCALE 16 px/step (manual p.37 allows up to 1024 px; p.39 SCALE is px/step),
    /// so HIT_X/HIT_Y resolve 1/16 step. With the eye at z = 8 the targets' vertical center is
    /// the eye line. Predicted lateral offset for SHOOT_X ±0.5 at arc 1: 0.5·tan(0.5)·44 = 12.0
    /// steps, dead center on a side target; a linear-angle model (tan(0.25)·44 = 11.2) differs
    /// by 0.8 steps = 13 texels.
    /// </summary>
    private const int TargetDepth = 44;
    private const int TargetOffsetY = 12;
    private const int TargetWidthSteps = 4;
    private const int TargetHeightSteps = 16;
    private const int TargetScale = 16;
    private const int TargetBitmapWidth = TargetWidthSteps * TargetScale;
    private const int TargetBitmapHeight = TargetHeightSteps * TargetScale;

    /// <summary>The three SHOOT targets (WDL keyword, marker letter, map y, IF_HIT action).</summary>
    private static readonly (string Keyword, string Letter, int Y, string Action)[] ShootTargets =
    [
        ("tgt_left", "L", TargetOffsetY, "hit_left"),
        ("tgt_center", "C", 0, "hit_center"),
        ("tgt_right", "R", -TargetOffsetY, "hit_right"),
    ];

    /// <summary>Skills printed right after the SHOOT (to OVSHO0.TXT) and inside IF_HIT (to OVHIT0.TXT), with ≤ 6-character labels.</summary>
    private static readonly (string Skill, string Label)[] HitLabels =
    [
        ("HIT_DIST", "D"), ("HIT_X", "X"), ("HIT_Y", "Y"), ("RESULT", "R"), ("TICKS", "T"),
    ];

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
        TargetLeft = 10,
        TargetRight = 11,
        Mark = 12,
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
        string ScreenshotName,
        double ShootX = 0,
        double ShootY = 0,
        bool Targets = false,
        TraceProbeCase? Trace = null);

    private sealed record Plan(string ProbeId, string ProbeName, IReadOnlyList<Variant> Variants, TraceProbeSet? Trace = null);

    public static int Prepare(string probeName, string[] args)
    {
        string outputDir = Path.Combine("_research", "oracle-runs", ProbeId(probeName));
        string? runtime = null;
        string? dosboxX = null;
        string[]? videos = null;
        double[]? arcs = null;
        double[]? aspects = null;
        double[]? tilts = null;
        double[]? shootX = null;
        double[]? shootY = null;

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
                case "--shoot-x":
                    shootX = ParseNumbers(RequireValue(args, ref i, arg));
                    break;
                case "--shoot-y":
                    shootY = ParseNumbers(RequireValue(args, ref i, arg));
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

        Plan plan = BuildPlan(probeName, videos, arcs, aspects, tilts, shootX, shootY);
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
        ShootProbeName => ShootProbeId,
        BranchProbeName => BranchProbeId,
        EachSecProbeName => EachSecProbeId,
        FlicProbeName => FlicProbeId,
        FlicDetailProbeName => FlicDetailProbeId,
        ProximityProbeName => ProximityProbeId,
        WallDistanceProbeName => WallDistanceProbeId,
        WallPassClipProbeName => WallPassClipProbeId,
        ClipContactProbeName => ClipContactProbeId,
        BorderClimbProbeName => BorderClimbProbeId,
        EyeClampProbeName => EyeClampProbeId,
        ActorContactProbeName => ActorContactProbeId,
        BorderKindsProbeName => BorderKindsProbeId,
        ClipDetailProbeName => ClipDetailProbeId,
        ViewActivityProbeName => ViewActivityProbeId,
        DistanceRefreshProbeName => DistanceRefreshProbeId,
        SchedulerOrderProbeName => SchedulerOrderProbeId,
        ActorEventsProbeName => ActorEventsProbeId,
        NumericEdgeProbeName => NumericEdgeProbeId,
        MotionTimingProbeName => MotionTimingProbeId,
        SaveFormatProbeName => SaveFormatProbeId,
        SkyProbeName => SkyProbeId,
        ShadingProbeName => ShadingProbeId,
        SlopeProbeName => SlopeProbeId,
        AudioProbeName => AudioProbeId,
        AutorangeProbeName => AutorangeProbeId,
        _ => throw new ArgumentException($"Unknown projection probe '{probeName}'."),
    };

    private static Plan BuildPlan(
        string probeName,
        string[]? videos,
        double[]? arcs,
        double[]? aspects,
        double[]? tilts,
        double[]? shootX,
        double[]? shootY)
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
            case ShootProbeName:
                foreach (string video in videos ?? DefaultFovVideos)
                {
                    // Baseline (SHOOT 0,0, no tilt) resolves HIT_DIST/HIT_X/HIT_Y semantics; SHOOT_X ±0.5
                    // resolves the lateral sign and tangent-vs-linear mapping (dead center on a side
                    // target); 0.55 lands 1.2 steps off-center so HIT_X also separates a mirrored bitmap
                    // (texel 51.5 tangent / 12.5 mirrored / 38.6 linear); SHOOT_Y ±0.25 the vertical sign
                    // and scale (±0.5 would leave the 16-step room); PLAYER_TILT ±0.1 whether the center
                    // ray follows the shear (±0.5 would miss the targets by 16 steps at D = 44).
                    // --arcs / --aspects cross every variant with other focal lengths.
                    foreach (double arc in arcs ?? [1.0])
                    {
                        foreach (double? aspect in aspects?.Select(static a => (double?)a) ?? [null])
                        {
                            string suffix = (arc != 1.0 ? $"-arc-{Num(arc)}" : "") + (aspect is { } a && a != 1.0 ? $"-aspect-{Num(a)}" : "");
                            variants.Add(new Variant($"{video}-center{suffix}", video, arc, aspect, 0, Room.Face, "ovsht", Targets: true));
                            foreach (double x in shootX ?? [0.5, -0.5, 0.55])
                            {
                                variants.Add(new Variant($"{video}-shootx-{Label(x)}{suffix}", video, arc, aspect, 0, Room.Face, "ovsht", ShootX: x, Targets: true));
                            }

                            foreach (double y in shootY ?? [0.25, -0.25])
                            {
                                variants.Add(new Variant($"{video}-shooty-{Label(y)}{suffix}", video, arc, aspect, 0, Room.Face, "ovsht", ShootY: y, Targets: true));
                            }

                            foreach (double tilt in tilts ?? [0.1, -0.1])
                            {
                                variants.Add(new Variant($"{video}-tilt-{Label(tilt)}{suffix}", video, arc, aspect, tilt, Room.Face, "ovsht", Targets: true));
                            }
                        }
                    }
                }

                break;
            case BranchProbeName:
            case EachSecProbeName:
            case FlicProbeName:
            case FlicDetailProbeName:
            case ProximityProbeName:
            case WallDistanceProbeName:
            case WallPassClipProbeName:
            case ClipContactProbeName:
            case BorderClimbProbeName:
            case EyeClampProbeName:
            case ActorContactProbeName:
            case BorderKindsProbeName:
            case ClipDetailProbeName:
            case ViewActivityProbeName:
            case DistanceRefreshProbeName:
            case SchedulerOrderProbeName:
            case ActorEventsProbeName:
            case NumericEdgeProbeName:
            case MotionTimingProbeName:
            case SaveFormatProbeName:
            case SkyProbeName:
            case ShadingProbeName:
            case SlopeProbeName:
            case AudioProbeName:
            case AutorangeProbeName:
                // Trace probes: the room only gives the runtime a valid world to boot.
                foreach (TraceProbeCase traceCase in TraceSet(probeName)!.Cases)
                {
                    variants.Add(new Variant(traceCase.Label, "S640x480", 1.0, null, 0, Room.Face, TraceSet(probeName)!.ScreenshotName, Trace: traceCase));
                }

                break;
            default:
                throw new ArgumentException($"Unknown projection probe '{probeName}'.");
        }

        return new Plan(ProbeId(probeName), probeName, variants, TraceSet(probeName));
    }

    private static TraceProbeSet? TraceSet(string probeName) => probeName switch
    {
        BranchProbeName => BranchProbeCases.Set,
        EachSecProbeName => EachSecProbeCases.Set,
        FlicProbeName => FlicProbeCases.Set,
        FlicDetailProbeName => FlicDetailProbeCases.Set,
        ProximityProbeName => ProximityProbeCases.Set,
        WallDistanceProbeName => WallDistanceProbeCases.Set,
        WallPassClipProbeName => WallDistanceProbeCases.PassClipSet,
        ClipContactProbeName => ClipContactProbeCases.Set,
        BorderClimbProbeName => BorderClimbProbeCases.Set,
        EyeClampProbeName => EyeClampProbeCases.Set,
        ActorContactProbeName => ActorContactProbeCases.Set,
        BorderKindsProbeName => BorderKindsProbeCases.Set,
        ClipDetailProbeName => ClipDetailProbeCases.Set,
        ViewActivityProbeName => ViewActivityProbeCases.Set,
        DistanceRefreshProbeName => DistanceRefreshProbeCases.Set,
        SchedulerOrderProbeName => SchedulerOrderProbeCases.Set,
        ActorEventsProbeName => ActorEventsProbeCases.Set,
        NumericEdgeProbeName => NumericEdgeProbeCases.Set,
        MotionTimingProbeName => MotionTimingProbeCases.Set,
        SaveFormatProbeName => SaveFormatProbeCases.Set,
        SkyProbeName => SkyProbeCases.Set,
        ShadingProbeName => ShadingProbeCases.Set,
        SlopeProbeName => SlopeProbeCases.Set,
        AudioProbeName => AudioProbeCases.Set,
        AutorangeProbeName => AutorangeProbeCases.Set,
        _ => null,
    };

    private static void WriteVariant(Plan plan, Variant variant, string variantDir, string? runtimePath, string? dosboxX)
    {
        string workDir = Path.Combine(variantDir, "work");
        Directory.CreateDirectory(workDir);

        SyntheticPcx.Palette palette = plan.ProbeName != AutorangeProbeName ? BuildPalette()
            : variant.Label.StartsWith("auto", StringComparison.Ordinal) ? AutorangeProbeCases.AutoPalette()
            : AutorangeProbeCases.SortedPalette();
        string wdl = Wdl(plan, variant);
        string wmp = Wmp(variant.Room, variant.Targets, variant.Trace?.Map);
        WdlParser.Parse(wdl);
        WmpMap.Read(wmp);

        File.WriteAllText(Path.Combine(workDir, OracleLaunchers.WorldName + ".WDL"), wdl, Encoding.Latin1);
        File.WriteAllText(Path.Combine(workDir, OracleLaunchers.WorldName + ".WMP"), wmp, Encoding.Latin1);

        WriteTexture(workDir, "PRBRED.PCX", Ink.Red, palette);
        WriteTexture(workDir, "PRBGRN.PCX", Ink.Green, palette);
        if (variant.Targets)
        {
            // Asymmetric far-wall face (left half yellow, right half light blue) so the
            // screenshot shows which texture half the engine draws toward the first wall vertex,
            // and the half/half target bitmap (left half cyan, right half orange) for sprites.
            WriteValidated(Path.Combine(workDir, "PRBYEL.PCX"), SyntheticPcx.HalfHalf(64, 64, (byte)Ink.Yellow, (byte)Ink.Mark, palette));
            WriteValidated(Path.Combine(workDir, "PRBTGT.PCX"), SyntheticPcx.HalfHalf(TargetBitmapWidth, TargetBitmapHeight, (byte)Ink.TargetLeft, (byte)Ink.TargetRight, palette));
        }
        else if (plan.ProbeName == AutorangeProbeName)
        {
            WriteValidated(Path.Combine(workDir, "PRBYEL.PCX"), SyntheticPcx.Write(64, 64, AutorangeProbeCases.GridBitmap, palette));
        }
        else
        {
            WriteTexture(workDir, "PRBYEL.PCX", Ink.Yellow, palette);
        }

        if (plan.ProbeName == SkyProbeName)
        {
            // Four sky sides (SkyProbeCases): the left half in the side's colour, the right half
            // yellow, and a band in the marker colour over rows 16-47, whose edges show where the
            // bitmap sits and how tall it is drawn (rows 0 and 63 are the side colour, which the
            // runtime repeats above and below the bitmap, p.38).
            Ink[] sides = [Ink.Red, Ink.Green, Ink.Blue, Ink.Magenta];
            for (int side = 0; side < sides.Length; side++)
            {
                byte left = (byte)sides[side];
                WriteValidated(
                    Path.Combine(workDir, SkyProbeCases.SideFile(side)),
                    SyntheticPcx.Write(
                        SkyProbeCases.SideWidth,
                        SkyProbeCases.SideHeight,
                        (x, y) => y is >= 16 and < 48 ? (byte)Ink.Mark : x < SkyProbeCases.SideWidth / 2 ? left : (byte)Ink.Yellow,
                        palette));
            }
        }

        if (plan.ProbeName == ViewActivityProbeName)
        {
            // view-activity round 6: a texture entirely in colour 0, see-through on a TRANSPARENT wall (p.53).
            WriteTexture(workDir, "PRBCLR.PCX", Ink.Transparent, palette);
        }

        if (plan.ProbeName == ShadingProbeName)
        {
            // The side walls and the floor in the ramp's brightest colour, which shading darkens.
            WriteValidated(Path.Combine(workDir, "PRBBLU.PCX"), SyntheticPcx.Solid(64, 64, ShadeRampStart, palette));
        }
        else
        {
            WriteTexture(workDir, "PRBBLU.PCX", Ink.Blue, palette);
        }

        WriteTexture(workDir, "PRBMAG.PCX", Ink.Magenta, palette);
        if (plan.ProbeName == ShadingProbeName)
        {
            WriteValidated(Path.Combine(workDir, "PRBFLR.PCX"), SyntheticPcx.Solid(64, 64, ShadeRampStart, palette));
        }
        else
        {
            WriteTexture(workDir, "PRBFLR.PCX", Ink.Floor, palette);
        }

        WriteTexture(workDir, "PRBCEL.PCX", Ink.Ceiling, palette);
        WriteValidated(Path.Combine(workDir, "PRBPAL.PCX"), SyntheticPcx.Swatch(palette));

        if (plan.ProbeName is FlicProbeName or FlicDetailProbeName)
        {
            File.WriteAllBytes(
                Path.Combine(workDir, FlicProbeCases.FlicFile),
                SyntheticFlic.WriteValidated(FlicProbeCases.FlicFrames, FlicProbeCases.FlicSpeedMs));
        }

        if (plan.ProbeName == AudioProbeName)
        {
            foreach ((string file, int rate, int samples, int tone) in AudioProbeCases.Sounds)
            {
                File.WriteAllBytes(Path.Combine(workDir, file), SyntheticWav.Tone(rate, samples, tone));
            }
        }

        if (plan.ProbeName is FlicDetailProbeName or AudioProbeName)
        {
            File.WriteAllBytes(
                Path.Combine(workDir, FlicDetailProbeCases.SecondFlicFile),
                SyntheticFlic.WriteValidated(FlicDetailProbeCases.SecondFlicFrames, FlicProbeCases.FlicSpeedMs, alternate: true));
            File.WriteAllBytes(
                Path.Combine(workDir, FlicDetailProbeCases.FastFlicFile),
                SyntheticFlic.WriteValidated(FlicDetailProbeCases.FastFlicFrames, FlicDetailProbeCases.FastFlicSpeedMs));
        }

        if (variant.Trace?.ClickAfterSeconds is not null)
        {
            File.Copy(OracleLaunchers.ClickHelperSource(), Path.Combine(variantDir, OracleLaunchers.ClickHelperName), overwrite: true);
        }

        if (runtimePath is not null)
        {
            File.Copy(runtimePath, Path.Combine(workDir, "VRUN.EXE"), overwrite: true);
        }

        File.WriteAllText(
            Path.Combine(variantDir, variant.Trace is null ? "geometry.json" : "case.json"),
            variant.Trace is { } traceCase ? TraceCaseJson(plan.Trace!, traceCase) : GeometryJson(variant, palette),
            Utf8NoBom);
        OracleLaunchers.Write(variantDir, new OracleLaunchers.Options(
            Title: $"{plan.ProbeId} {variant.Label}",
            VariantLabel: $"{plan.ProbeName}/{variant.Label}",
            WorkDir: workDir,
            DosboxX: dosboxX,
            CompletionMarker: "OVEND",
            Autotype: variant.Trace?.Autotype,
            ClickAfterSeconds: variant.Trace?.ClickAfterSeconds));
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
        palette.Set((int)Ink.TargetLeft, 40, 220, 220);
        palette.Set((int)Ink.TargetRight, 240, 130, 40);
        palette.Set((int)Ink.Mark, 170, 190, 255);
        palette.Set((int)Ink.White, 255, 255, 255);

        // A gray ramp from bright to black at ShadeRampStart for the shading probe's RANGE (p.34);
        // the other probes do not use these indices.
        for (int i = 0; i < ShadeRampLength; i++)
        {
            int level = 248 - (i * 8);
            palette.Set(ShadeRampStart + i, (byte)level, (byte)level, (byte)level);
        }

        return palette;
    }

    /// <summary>The first palette index of the shading probe's gray ramp.</summary>
    public const int ShadeRampStart = 16;

    /// <summary>The number of colours in the gray ramp (brightest first).</summary>
    public const int ShadeRampLength = 32;

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

            PALETTE probe_pal { PALFILE <PRBPAL.PCX>; {{variant.Trace?.Map?.PaletteBody ?? ""}}}

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

            {{variant.Trace?.Map?.Textures ?? ""}}
            WALL red_wall { TEXTURE red_tex; }
            WALL grn_wall { TEXTURE grn_tex; }
            WALL yel_wall { TEXTURE yel_tex; }
            WALL blu_wall { TEXTURE blu_tex; }
            WALL mag_wall { TEXTURE mag_tex; }
            {{(variant.Targets ? TargetDeclarationsWdl() : "")}}
            REGION room
            {
                FLOOR_HGT 0;
                CEIL_HGT {{variant.Room.CeilingHeight}};
                FLOOR_TEX flr_tex;
                CEIL_TEX {{variant.Trace?.Map?.RoomCeiling ?? "cel_tex"}};
                {{variant.Trace?.Map?.RoomBody ?? ""}}
            }
            {{(variant.Trace?.Map is { PadBody: not null } padMap ? PadRegionWdl(variant.Room, padMap) : "")}}
            REGION solid
            {
                FLOOR_HGT {{variant.Room.CeilingHeight}};
                CEIL_HGT {{variant.Room.CeilingHeight}};
                FLOOR_TEX flr_tex;
                CEIL_TEX cel_tex;
                CLIP_DIST 0;
            }
            {{(variant.Room.HasAlcove ? AlcoveRegionWdl(variant.Room) : "")}}
            {{variant.Trace?.Declarations ?? ""}}
            ACTION probe_start
            {
                PRINTFILE "ovsta", 0;
                PRINT_STRING "START\n";
            {{variant.Trace?.Prelude ?? ""}}    SET PLAYER_ARC, {{Num(variant.Arc)}};
                SET PLAYER_TILT, {{Num(variant.Tilt)}};
                SET PLAYER_SIZE, {{variant.Room.EyeHeight}};
                SET PLAYER_Z, {{variant.Room.EyeHeight}};

            """);

        if (variant.Aspect is { } aspect && aspect != 1.0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"    SET ASPECT, {Num(aspect)};\n");
        }

        if (variant.Targets)
        {
            sb.Append(CultureInfo.InvariantCulture, $"    SET SHOOT_X, {Num(variant.ShootX)};\n    SET SHOOT_Y, {Num(variant.ShootY)};\n");
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

            AppendPrint(sb, label, skill);
        }

        if (variant.Targets)
        {
            foreach ((string skill, string label) in ShootStateLabels)
            {
                AppendPrint(sb, label, skill);
            }

            // The pre-shot marker proves the file was selected before SHOOT; the hit actions
            // select OVHIT0.TXT, so the file is re-selected afterwards. The manual (p.88) only
            // says PRINT_VALUE appends to an existing file; whether PRINTFILE truncates is a
            // harness fact this ordering exposes (a lost PRE line).
            sb.Append("""
                    PRINTFILE "ovsho", 0;
                    PRINT_STRING "PRE\n";
                    SHOOT;
                    PRINTFILE "ovsho", 0;

                """);
            foreach ((string skill, string label) in HitLabels)
            {
                AppendPrint(sb, "HOUT_" + label, skill);
            }
        }

        if (variant.Trace is { } traceCase)
        {
            sb.Append(CultureInfo.InvariantCulture, $"    PRINTFILE \"{plan.Trace!.TraceFile}\", 0;\n    PRINT_STRING \"BEGIN\\n\";\n");
            sb.Append(traceCase.Run);
        }

        sb.Append(CultureInfo.InvariantCulture, $$"""
                WAITT 4;
                SCREENSHOT "{{variant.ScreenshotName}}", 0;
                PRINTFILE "ovend", 0;
                PRINT_STRING "END\n";
                EXIT;
            }

            IF_START probe_start;

            """);
        return sb.ToString().Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    private static void AppendPrint(StringBuilder sb, string label, string skill) =>
        sb.Append(CultureInfo.InvariantCulture, $"    PRINT_STRING \"{label}=\";\n    PRINT_VALUE {skill};\n    PRINT_STRING \"\\n\";\n");

    /// <summary>Extra skills dumped before the shot by the shoot-mapping probe.</summary>
    private static readonly (string Skill, string Label)[] ShootStateLabels =
    [
        ("SHOOT_X", "SHX"), ("SHOOT_Y", "SHY"), ("SHOOT_RANGE", "SHRNG"), ("SHOOT_FAC", "SHFAC"), ("TICKS", "TCK"),
    ];

    /// <summary>
    /// The SHOOT targets' bitmap, texture, THING definitions and IF_HIT actions. Each hit action
    /// selects OVHIT0.TXT, writes the target's letter and the HIT_* skills as seen inside the hook
    /// (whether IF_HIT sees them is undocumented; p.90 only says they are valid "after the SHOOT
    /// instruction").
    /// </summary>
    private static string TargetDeclarationsWdl()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $$"""

            BMAP tgt_map,<PRBTGT.PCX>;
            TEXTURE tgt_tex { SCALE_X {{TargetScale}}; SCALE_Y {{TargetScale}}; BMAPS tgt_map; }

            """);
        foreach ((string keyword, _, _, string action) in ShootTargets)
        {
            sb.Append(CultureInfo.InvariantCulture, $"THING {keyword} {{ TEXTURE tgt_tex; IF_HIT {action}; }}\n");
        }

        foreach ((_, string letter, _, string action) in ShootTargets)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\nACTION {action}\n{{\n    PRINTFILE \"ovhit\", 0;\n    PRINT_STRING \"{letter}\\n\";\n");
            foreach ((string skill, string label) in HitLabels)
            {
                AppendPrint(sb, "HIN_" + label, skill);
            }

            sb.Append("}\n");
        }

        return sb.ToString();
    }

    /// <summary>The pad island's region and, unless the case brings its own, its invisible, passable boundary wall (see <see cref="TraceProbeMap"/>).</summary>
    private static string PadRegionWdl(Room room, TraceProbeMap map) =>
        $$"""
        {{(map.PadWall is null ? "WALL pad_wall { TEXTURE red_tex; FLAGS INVISIBLE, PASSABLE; }" : "")}}
        REGION pad
        {
            FLOOR_HGT {{map.PadFloor}};
            CEIL_HGT {{map.PadCeiling ?? room.CeilingHeight}};
            FLOOR_TEX flr_tex;
            CEIL_TEX cel_tex;
            {{map.PadBody}}
        }

        """;

    /// <summary>The pad island's corners, counter-clockwise in a y-up frame so the pad is on the left of every wall.</summary>
    private static readonly (int X, int Y)[] PadCorners = [(-12, 12), (-4, 12), (-4, 20), (-12, 20)];

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
    private static string Wmp(Room room, bool targets = false, TraceProbeMap? map = null)
    {
        if (map?.PadBody is not null && room.HasAlcove)
        {
            throw new InvalidOperationException("The pad island assumes region 2 is free; the alcove room uses it.");
        }

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

        int padFirst = vertices.Count;
        if (map?.PadBody is not null)
        {
            vertices.AddRange(PadCorners);
        }

        int extraFirst = vertices.Count;
        foreach (TraceProbeWall extra in map?.Walls ?? [])
        {
            vertices.Add((extra.X1, extra.Y1));
            vertices.Add((extra.X2, extra.Y2));
        }

        for (int i = 0; i < vertices.Count; i++)
        {
            double z = map?.PadZ is { } padZ && map.PadBody is not null && i >= padFirst && i < padFirst + PadCorners.Length
                ? padZ[i - padFirst]
                : 0;
            sb.Append(CultureInfo.InvariantCulture, $"VERTEX {vertices[i].X} {vertices[i].Y} {Num(z)};#{i}\r\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"REGION room 0 {room.CeilingHeight};#0\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"REGION solid {room.CeilingHeight} {room.CeilingHeight};#1\r\n");
        if (room.HasAlcove)
        {
            sb.Append(CultureInfo.InvariantCulture, $"REGION alcove {room.StepHeight} {room.CeilingHeight};#2\r\n");
        }
        else if (map?.PadBody is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $"REGION pad {map.PadFloor} {map.PadCeiling ?? room.CeilingHeight};#2\r\n");
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

        if (map?.PadBody is not null)
        {
            for (int k = 0; k < PadCorners.Length; k++)
            {
                int from = padFirst + k;
                int to = padFirst + ((k + 1) % PadCorners.Length);
                sb.Append(CultureInfo.InvariantCulture, $"WALL {map.PadWall ?? "pad_wall"} {from} {to} 2 0 0 0;#{wall++}\r\n");
            }
        }

        foreach (TraceProbeWall extra in map?.Walls ?? [])
        {
            sb.Append(CultureInfo.InvariantCulture, $"WALL {extra.Keyword} {extraFirst} {extraFirst + 1} 0 0 0 0;#{wall++}\r\n");
            extraFirst += 2;
        }

        int placement = wall;
        foreach (string line in map?.Placements ?? [])
        {
            sb.Append(CultureInfo.InvariantCulture, $"{line};#{placement++}\r\n");
        }

        if (targets)
        {
            foreach ((string keyword, _, int y, _) in ShootTargets)
            {
                sb.Append(CultureInfo.InvariantCulture, $"THING {keyword} {TargetDepth} {y} 0 0;#{placement++}\r\n");
            }
        }

        sb.Append(CultureInfo.InvariantCulture, $"PLAYER_START 0 0 0 0;#{placement}\r\n");
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
            state_labels = (variant.Targets ? StateLabels.Concat(ShootStateLabels) : StateLabels)
                .ToDictionary(static p => p.Label, static p => p.Skill),
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
                ["target_left"] = Rgb(palette, Ink.TargetLeft),
                ["target_right"] = Rgb(palette, Ink.TargetRight),
                ["mark"] = Rgb(palette, Ink.Mark),
            },
            shoot = variant.Targets ? ShootGeometry(variant, width, height) : null,
        };
        return JsonSerializer.Serialize(geometry, JsonOptions);
    }

    /// <summary>
    /// The SHOOT fixture and its predictions under the model OpenVirtue implements (the ray
    /// shares the renderer's projection: lateral tangent SHOOT_X·tan(arc/2) toward screen
    /// right = map −y, vertical slope +SHOOT_Y·(2/3)·ASPECT·tan(arc/2) plus the tilt shear
    /// −tilt·H/f_v, positive SHOOT_Y up per OV-20261009-shoot-mapping) next to the competing
    /// readings the measurement script distinguishes: linear-angle lateral and vertical
    /// mappings, no shear on the center ray, the opposite signs, a mirrored texture, and
    /// HIT_DIST along the forward axis versus along the ray.
    /// </summary>
    private static object ShootGeometry(Variant variant, int width, int height)
    {
        ProjectionModel model = ProjectionModel.Create(variant.Arc, variant.Aspect ?? 1.0, variant.Tilt, width, height);
        (double lateralTangent, double verticalSlope) = model.ShootRay(variant.ShootX, variant.ShootY);
        double eye = variant.Room.EyeHeight;
        double depth = TargetDepth;
        double hitY = -lateralTangent * depth;                 // screen right = map −y
        double hitZ = eye + (verticalSlope * depth);
        double linearLateral = Math.Tan(variant.ShootX * variant.Arc * 0.5) * depth;
        double noShearSlope = verticalSlope - model.CenterSlope; // the shared ray without the tilt term
        double noShearZ = eye + (noShearSlope * depth);
        double linearVerticalSlope = Math.Tan(variant.ShootY * Math.Atan(model.VerticalHalfTangent)) + model.CenterSlope;
        double linearVerticalZ = eye + (linearVerticalSlope * depth);

        var targets = ShootTargets.Select(t => new
        {
            keyword = t.Keyword,
            letter = t.Letter,
            action = t.Action,
            x = TargetDepth,
            y = t.Y,
            y_min = t.Y - (TargetWidthSteps / 2.0),
            y_max = t.Y + (TargetWidthSteps / 2.0),
            z_min = 0,
            z_max = TargetHeightSteps,
            bitmap = new { file = "PRBTGT.PCX", width = TargetBitmapWidth, height = TargetBitmapHeight, scale = TargetScale, left_half = "target_left", right_half = "target_right" },
        }).ToArray();

        return new
        {
            shoot_x = variant.ShootX,
            shoot_y = variant.ShootY,
            hit_file = "OVHIT0.TXT",
            after_shot_file = "OVSHO0.TXT",
            hit_labels = HitLabels.ToDictionary(static p => "HIN_" + p.Label, static p => p.Skill),
            after_shot_labels = HitLabels.ToDictionary(static p => "HOUT_" + p.Label, static p => p.Skill),
            face_bitmap = new { file = "PRBYEL.PCX", left_half = "yellow", right_half = "mark", first_vertex_y = -variant.Room.TargetWidth / 2 },
            targets,
            prediction = new
            {
                model = "shared-projection (ProjectionModel.ShootRay; OV-20261009-* FOV, aspect, tilt notes)",
                lateral_tangent = lateralTangent,
                vertical_slope = verticalSlope,
                hit = new { x = depth, y = hitY, z = hitZ },
                target = TargetAt(hitY, hitZ),
                texel = Texel(hitY, hitZ, mirrored: false),
                hit_dist_forward = depth,
                hit_dist_ray = Math.Sqrt((depth * depth) + (hitY * hitY) + ((hitZ - eye) * (hitZ - eye))),
                alternatives = new
                {
                    linear_angle = new { lateral = linearLateral, target = TargetAt(-linearLateral, hitZ), texel = Texel(-linearLateral, hitZ, mirrored: false) },
                    linear_angle_vertical = new { z = linearVerticalZ, texel = Texel(hitY, linearVerticalZ, mirrored: false) },
                    shoot_x_sign_inverted = new { target = TargetAt(-hitY, hitZ), texel = Texel(-hitY, hitZ, mirrored: false) },
                    shoot_y_sign_inverted = new { z = eye - (verticalSlope * depth), texel = Texel(hitY, eye - (verticalSlope * depth), mirrored: false) },
                    no_shear = new { z = noShearZ, texel = Texel(hitY, noShearZ, mirrored: false) },
                    texture_mirrored = new { texel = Texel(hitY, hitZ, mirrored: true) },
                },
            },
        };
    }

    private static string TraceCaseJson(TraceProbeSet set, TraceProbeCase traceCase) =>
        JsonSerializer.Serialize(
            new
            {
                variant = traceCase.Label,
                question = traceCase.Question,
                trace_file = set.TraceFileName,
                console_file = "OVCON.TXT",
                autotype = traceCase.Autotype,
                predictions = traceCase.Predictions,
            },
            JsonOptions);

    private static string? TargetAt(double y, double z)
    {
        foreach ((string keyword, _, int targetY, _) in ShootTargets)
        {
            if (Math.Abs(y - targetY) <= TargetWidthSteps / 2.0 && z >= 0 && z <= TargetHeightSteps)
            {
                return keyword;
            }
        }

        return null;
    }

    /// <summary>
    /// Texture pixel (from the bitmap's upper-left corner, manual p.90) for a hit at map
    /// (y, z) on the target containing it. Unmirrored: the bitmap's left edge is on the
    /// original's screen left, i.e. the target's +y edge.
    /// </summary>
    private static object? Texel(double y, double z, bool mirrored)
    {
        string? keyword = TargetAt(y, z);
        if (keyword is null)
        {
            return null;
        }

        int targetY = ShootTargets.Single(t => t.Keyword == keyword).Y;
        double fromLeft = (targetY + (TargetWidthSteps / 2.0)) - y;
        double texelX = (mirrored ? TargetWidthSteps - fromLeft : fromLeft) * TargetScale;
        double texelY = (TargetHeightSteps - z) * TargetScale;
        return new { x = texelX, y = texelY };
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
        {{string.Join("\n", plan.Variants.Select(v => $"- `{v.Label}`: VIDEO {v.Video}, PLAYER_ARC {Num(v.Arc)}, PLAYER_TILT {Num(v.Tilt)}, ASPECT {Num(v.Aspect ?? 1.0)}, room {(v.Room == Room.Tall ? "tall/narrow" : "face")}{(v.Targets ? $", SHOOT_X {Num(v.ShootX)}, SHOOT_Y {Num(v.ShootY)}" : "")}"))}}

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
        pixel is a flat palette index. All textures are generated 64x64 solids at SCALE 4 px/step
        (except the shoot-mapping probe's half/half face and 64x256 target bitmaps, see below).

        `IF_START` sets the probe skills, waits 16 ticks, writes the live skill values to
        `OVPRB0.TXT` (PRINTFILE/PRINT_VALUE), grabs `SCREENSHOT` into `<name>0.PCX`, waits 4 ticks
        and EXITs; the DOSBox-X autoexec then exits.
        {{(plan.ProbeName == ShootProbeName ? ShootReadme() : "")}}{{(plan.Trace is { } readmeSet ? TraceReadme(readmeSet) : "")}}
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
        {{(plan.Trace is { } measureSet
            ? $"for d in {outputDir}/[!_]*/; do echo \"== $d\"; cat \"$d/work/{measureSet.TraceFileName}\"; done"
            : $"_research/oracle-runs/.venv/bin/python tools/oracle/{(plan.ProbeName == ShootProbeName ? "measure_shoot.py" : "measure_projection.py")} {outputDir}/[!_]*/")}}
        ```
        """;

    private static string TraceReadme(TraceProbeSet set)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"""

            ### {set.ProbeName}

            Trace file: `{set.TraceFileName}`.
            {set.Intro}

            """);
        foreach (TraceProbeCase traceCase in set.Cases)
        {
            sb.Append(CultureInfo.InvariantCulture, $"- `{traceCase.Label}`: {traceCase.Question}\n");
            foreach ((string reading, string trace) in traceCase.Predictions)
            {
                sb.Append(CultureInfo.InvariantCulture, $"  - {reading}: `{trace}`\n");
            }
        }

        return sb.ToString();
    }

    private static string ShootReadme() =>
        $"""

        ### shoot-mapping

        Three THINGs (`tgt_left` at map y = +{TargetOffsetY}, `tgt_center` at 0, `tgt_right` at
        −{TargetOffsetY}) stand inside the room at x = {TargetDepth}, each {TargetWidthSteps} steps wide
        × {TargetHeightSteps} tall (a {TargetBitmapWidth}×{TargetBitmapHeight} px bitmap at SCALE {TargetScale}
        px/step, left half cyan, right half orange). The far-wall yellow face is left-half yellow /
        right-half light blue so the screenshot shows wall texture orientation too. `IF_START`
        additionally SETs `SHOOT_X`/`SHOOT_Y`, dumps them with `SHOOT_RANGE`, `SHOOT_FAC` and `TICKS`,
        selects `OVSHO0.TXT`, writes `PRE`, fires `SHOOT`, re-selects `OVSHO0.TXT` and writes the
        `HOUT_*` values (HIT_DIST, HIT_X, HIT_Y, RESULT, TICKS). Each target's `IF_HIT` action
        selects `OVHIT0.TXT`, writes its letter (L/C/R) and the same five skills as `HIN_*`.
        `geometry.json` → `shoot.prediction` holds the hit, target and texel under the shared-
        projection model next to the competing readings (linear angle, inverted signs, no shear,
        mirrored texture, HIT_DIST along the ray). Measure with
        `_research/oracle-runs/.venv/bin/python tools/oracle/measure_shoot.py <probe-dir>/[!_]*/`.
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
          ovtool oracle prepare shoot-mapping  [output-dir] [--runtime <VRUN-patched.EXE>] [--dosbox-x <exe>] [--shoot-x 0.5,-0.5,0.55] [--shoot-y 0.25,-0.25] [--tilts 0.1,-0.1] [--videos S640x480]
          ovtool oracle prepare branch-semantics [output-dir] [--runtime <VRUN-patched.EXE>] [--dosbox-x <exe>]
          ovtool oracle prepare each-sec-cadence [output-dir] [--runtime <VRUN-patched.EXE>] [--dosbox-x <exe>]
          ovtool oracle prepare flic-play|flic-detail [output-dir] [--runtime <VRUN-patched.EXE>] [--dosbox-x <exe>]
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
