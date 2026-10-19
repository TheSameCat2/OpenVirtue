// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.Text;
using OpenVirtue.Formats.Wdl;
using OpenVirtue.Formats.Wmp;
using OpenVirtue.Formats.Wrs;

internal static class OracleProbeGenerator
{
    private const string SchedulerProbeId = "OV-20260701-scheduler-if-start-cycle";
    private const string SchedulerProbeName = "scheduler-if-start-cycle";

    public static int Prepare(string[] args)
    {
        if (args.Length == 0)
        {
            return Usage("oracle prepare requires a probe name.");
        }

        string probe = args[0].ToLowerInvariant();
        return probe switch
        {
            SchedulerProbeName => PrepareSchedulerProbe(args[1..]),
            ProjectionProbeGenerator.FovProbeName or
            ProjectionProbeGenerator.AspectProbeName or
            ProjectionProbeGenerator.TiltProbeName or
            ProjectionProbeGenerator.ShootProbeName or
            ProjectionProbeGenerator.BranchProbeName or
            ProjectionProbeGenerator.EachSecProbeName => ProjectionProbeGenerator.Prepare(probe, args[1..]),
            ProjectionProbeGenerator.FlicProbeName or
            ProjectionProbeGenerator.FlicDetailProbeName or
            ProjectionProbeGenerator.ProximityProbeName or
            ProjectionProbeGenerator.WallDistanceProbeName or
            ProjectionProbeGenerator.WallPassClipProbeName => ProjectionProbeGenerator.Prepare(probe, args[1..]),
            "-h" or "--help" or "help" => Usage(),
            _ => Usage($"Unknown oracle probe '{args[0]}'."),
        };
    }

    private static int PrepareSchedulerProbe(string[] args)
    {
        string outputDir = Path.Combine("_research", "oracle-runs", SchedulerProbeId);
        string? runtimeDir = null;
        string? dosboxX = null;
        string? carrierDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--runtime-dir":
                    runtimeDir = RequireValue(args, ref i, arg);
                    break;
                case "--dosbox-x":
                    dosboxX = RequireValue(args, ref i, arg);
                    break;
                case "--carrier-dir":
                    carrierDir = RequireValue(args, ref i, arg);
                    break;
                case "-h" or "--help":
                    return SchedulerUsage();
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Unknown option '{arg}'.");
                    }

                    outputDir = arg;
                    break;
            }
        }

        string fullOutputDir = Path.GetFullPath(outputDir);
        string workDir = Path.Combine(fullOutputDir, "work");
        Directory.CreateDirectory(workDir);

        string wdl = SchedulerWdl();
        string wmp = SchedulerWmp();
        ValidateFixture(wdl, wmp);

        File.WriteAllText(Path.Combine(workDir, "TITLE.WDL"), wdl, Encoding.Latin1);
        File.WriteAllText(Path.Combine(workDir, "TITLE.WMP"), wmp, Encoding.Latin1);
        File.WriteAllBytes(Path.Combine(workDir, "prbstart.pcx"), SolidPcx(width: 280, height: 160, paletteIndex: 61));
        File.WriteAllBytes(Path.Combine(workDir, "prbtick.pcx"), SolidPcx(width: 280, height: 160, paletteIndex: 46));
        File.WriteAllText(Path.Combine(fullOutputDir, "dosbox.conf"), DosboxConfig(workDir), Encoding.ASCII);
        File.WriteAllText(Path.Combine(fullOutputDir, "run-oracle.bat"), RunBatch(dosboxX), Encoding.ASCII);
        OracleLaunchers.WriteLinux(fullOutputDir, new OracleLaunchers.Options(
            Title: SchedulerProbeId,
            VariantLabel: SchedulerProbeName,
            WorkDir: workDir,
            DosboxX: dosboxX,
            LaunchLogRelativePath: "launches.md"));
        File.WriteAllText(Path.Combine(fullOutputDir, "README.md"), Readme(workDir, runtimeDir, dosboxX), Encoding.UTF8);

        int copied = 0;
        int copiedAssets = 0;
        if (runtimeDir is not null)
        {
            copied = CopyRuntimeShell(runtimeDir, workDir);
            copiedAssets = CopyProbeAssets(runtimeDir, workDir);
        }

        int packed = WriteProbeArchive(workDir);
        string? carrierLaunchDir = null;
        if (carrierDir is not null)
        {
            carrierLaunchDir = PrepareTitleCarrier(fullOutputDir, carrierDir, dosboxX);
        }

        Console.WriteLine($"Prepared {SchedulerProbeId} in {fullOutputDir}");
        Console.WriteLine($"  fixture: {Path.Combine(workDir, "TITLE.WDL")}");
        Console.WriteLine($"  map:     {Path.Combine(workDir, "TITLE.WMP")}");
        Console.WriteLine($"  archive: {Path.Combine(workDir, "TITLE.WRS")} ({packed} entries)");
        Console.WriteLine($"  launch:  {Path.Combine(fullOutputDir, "run-oracle.bat")} (Windows) / {Path.Combine(fullOutputDir, "run.sh")} (Linux)");
        if (runtimeDir is not null)
        {
            Console.WriteLine($"  copied {copied} runtime/support file(s) into work/; retail WRS archives were not copied.");
            Console.WriteLine($"  copied {copiedAssets} local-only display asset(s) into work/.");
        }
        else
        {
            Console.WriteLine("  no runtime copied; copy VRUN.EXE/support files into work/ or rerun with --runtime-dir.");
        }
        if (carrierLaunchDir is not null)
        {
            Console.WriteLine($"  title carrier: {carrierLaunchDir}");
            Console.WriteLine($"  carrier launch: {Path.Combine(fullOutputDir, "run-title-carrier.bat")}");
        }

        return 0;
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }

    private static int CopyRuntimeShell(string runtimeDir, string workDir)
    {
        string fullRuntimeDir = Path.GetFullPath(runtimeDir);
        if (!Directory.Exists(fullRuntimeDir))
        {
            throw new DirectoryNotFoundException($"Runtime directory not found: {fullRuntimeDir}");
        }

        string[] names =
        [
            "VRUN.EXE",
            "WWRUN.WDF",
            "WWRUN.MDF",
        ];

        int copied = 0;
        foreach (string name in names)
        {
            string source = Path.Combine(fullRuntimeDir, name);
            if (!File.Exists(source))
            {
                continue;
            }

            File.Copy(source, Path.Combine(workDir, name), overwrite: true);
            copied++;
        }

        if (!File.Exists(Path.Combine(workDir, "VRUN.EXE")))
        {
            throw new FileNotFoundException("VRUN.EXE was not found in the runtime directory.", Path.Combine(fullRuntimeDir, "VRUN.EXE"));
        }

        return copied;
    }

    private static string PrepareTitleCarrier(string outputDir, string carrierDir, string? dosboxX)
    {
        string fullCarrierDir = Path.GetFullPath(carrierDir);
        if (!Directory.Exists(fullCarrierDir))
        {
            throw new DirectoryNotFoundException($"Carrier directory not found: {fullCarrierDir}");
        }

        string launchDir = Path.Combine(outputDir, "patched-title-carrier-launch");
        OracleLaunchers.ParkIfExists(launchDir);
        if (Directory.Exists(launchDir))
        {
            // Nothing observational inside: overwrite the generated files in place.
            Directory.Delete(launchDir, recursive: true);
        }

        CopyDirectory(fullCarrierDir, launchDir);
        SanitizeCarrierWdlFiles(launchDir);

        string titleWdlPath = Path.Combine(launchDir, "TITLE.WDL");
        if (!File.Exists(titleWdlPath))
        {
            throw new FileNotFoundException("Carrier directory does not contain TITLE.WDL.", titleWdlPath);
        }

        string titleWdl = Encoding.Latin1.GetString(File.ReadAllBytes(titleWdlPath));
        string instrumentedWdl = InstrumentTitleCarrierWdl(titleWdl);
        WdlParser.Parse(instrumentedWdl);
        File.WriteAllText(titleWdlPath, instrumentedWdl, Encoding.Latin1);

        File.WriteAllText(Path.Combine(outputDir, "dosbox-patched-title-carrier.conf"), CarrierDosboxConfig(launchDir), Encoding.ASCII);
        File.WriteAllText(Path.Combine(outputDir, "run-title-carrier.bat"), RunBatch(dosboxX, "dosbox-patched-title-carrier.conf"), Encoding.ASCII);
        File.WriteAllText(Path.Combine(launchDir, "OPENVIRTUE-CARRIER-NOTES.txt"), CarrierNotes(), Encoding.ASCII);
        return launchDir;
    }

    private static string InstrumentTitleCarrierWdl(string titleWdl)
    {
        string normalized = titleWdl.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.Contains("SKILL ov_probe_tick", StringComparison.Ordinal))
        {
            return titleWdl;
        }

        normalized = ReplaceRequired(
            normalized,
            "ACTION start_game {\n",
            """
            SKILL ov_probe_tick { VAL 0; }

            ACTION start_game {
                PRINTFILE "ovsta",0;
                PRINT_STRING "START\n";

            """);

        normalized = ReplaceRequired(
            normalized,
            "\tSET\tEACH_TICK.1,\ttestSpeed;\n",
            """
                SET EACH_TICK.1, testSpeed;
                PRINTFILE "ovsch",0;
                PRINT_STRING "SCHED\n";

            """);

        normalized = ReplaceRequired(
            normalized,
            "ACTION testSpeed {\n",
            """
            ACTION testSpeed {
                IF (ov_probe_tick == 0) {
                    PRINTFILE "ovtik",0;
                    PRINT_STRING "TICK\n";
                    SET ov_probe_tick, 1;
                    EXIT;
                }

            """);

        return normalized.Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    private static string ReplaceRequired(string input, string oldValue, string newValue)
    {
        if (!input.Contains(oldValue, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Carrier TITLE.WDL did not contain expected probe anchor: {oldValue.Trim()}");
        }

        return input.Replace(oldValue, newValue, StringComparison.Ordinal);
    }

    private static void SanitizeCarrierWdlFiles(string launchDir)
    {
        foreach (string file in Directory.EnumerateFiles(launchDir, "*", SearchOption.AllDirectories))
        {
            if (!string.Equals(Path.GetExtension(file), ".WDL", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string original = Encoding.Latin1.GetString(File.ReadAllBytes(file));
            string sanitized = RemoveActiveAppendModeLines(original);
            if (!string.Equals(original, sanitized, StringComparison.Ordinal))
            {
                File.WriteAllText(file, sanitized, Encoding.Latin1);
            }
        }
    }

    private static string RemoveActiveAppendModeLines(string wdl)
    {
        string normalized = wdl.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        StringBuilder builder = new(normalized.Length);
        bool changed = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (IsActiveAppendModeLine(line))
            {
                changed = true;
                continue;
            }

            builder.Append(line);
            if (i < lines.Length - 1)
            {
                builder.Append('\n');
            }
        }

        if (!changed)
        {
            return wdl;
        }

        return builder.ToString().Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    private static bool IsActiveAppendModeLine(string line)
    {
        string trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return false;
        }

        if (trimmed.StartsWith("APPEND_MODE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!trimmed.StartsWith("SET", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> rest = trimmed.AsSpan(3).TrimStart();
        return rest.StartsWith("APPEND_MODE".AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (string directory in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, directory);
            Directory.CreateDirectory(Path.Combine(destinationDir, relative));
        }

        foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, file);
            File.Copy(file, Path.Combine(destinationDir, relative), overwrite: true);
        }
    }

    private static int WriteProbeArchive(string workDir)
    {
        string[] names =
        [
            "TITLE.WDL",
            "TITLE.WMP",
            "prbstart.pcx",
            "prbtick.pcx",
            "fontdig1.pcx",
            "black.pcx",
        ];

        WrsFile[] files = names
            .Select(name => (Name: name, Path: Path.Combine(workDir, name)))
            .Where(file => File.Exists(file.Path))
            .Select(file => new WrsFile(file.Name, File.ReadAllBytes(file.Path)))
            .ToArray();

        WrsArchive.WriteFile(Path.Combine(workDir, "TITLE.WRS"), files);
        return files.Length;
    }

    private static int CopyProbeAssets(string runtimeDir, string workDir)
    {
        string archivePath = Path.Combine(Path.GetFullPath(runtimeDir), "TITLE.WRS");
        if (!File.Exists(archivePath))
        {
            return 0;
        }

        WrsArchive archive = WrsArchive.ReadFile(archivePath);
        string[] names =
        [
            "fontdig1.pcx",
            "black.pcx",
        ];

        int copied = 0;
        foreach (string name in names)
        {
            WrsEntry? entry = archive.Entries.FirstOrDefault(
                e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                continue;
            }

            File.WriteAllBytes(Path.Combine(workDir, name), entry.GetData());
            copied++;
        }

        return copied;
    }

    private static byte[] SolidPcx(int width, int height, byte paletteIndex)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "PCX dimensions must be positive.");
        }

        int bytesPerLine = width % 2 == 0 ? width : width + 1;
        using var raster = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            int remaining = bytesPerLine;
            while (remaining > 0)
            {
                int run = Math.Min(63, remaining);
                raster.WriteByte((byte)(0xC0 | run));
                raster.WriteByte(paletteIndex);
                remaining -= run;
            }
        }

        byte[] rasterBytes = raster.ToArray();
        byte[] data = new byte[128 + rasterBytes.Length + 1 + (256 * 3)];
        Span<byte> span = data;
        span[0] = 0x0A;
        span[1] = 5;
        span[2] = 1;
        span[3] = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], checked((ushort)(width - 1)));
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], checked((ushort)(height - 1)));
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], checked((ushort)width));
        BinaryPrimitives.WriteUInt16LittleEndian(span[14..], checked((ushort)height));
        span[65] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(span[66..], checked((ushort)bytesPerLine));

        rasterBytes.CopyTo(span[128..]);
        int paletteStart = 128 + rasterBytes.Length;
        span[paletteStart] = 0x0C;

        SetPalette(0, 0, 0, 0);
        SetPalette(46, 238, 226, 144);
        SetPalette(61, 202, 44, 32);
        SetPalette(90, 44, 80, 220);
        SetPalette(255, 255, 255, 255);
        return data;

        void SetPalette(int index, byte r, byte g, byte b)
        {
            int offset = paletteStart + 1 + (index * 3);
            data[offset] = r;
            data[offset + 1] = g;
            data[offset + 2] = b;
        }
    }

    private static void ValidateFixture(string wdl, string wmp)
    {
        WdlParser.Parse(wdl);
        WmpMap.Read(wmp);
    }

    private static string SchedulerWdl() =>
        """
        // Synthetic OpenVirtue clean-room oracle probe.
        // Local-only generated file; do not commit generated WDL/WMP artifacts.

        DITHER 0;
        VIDEO S640x480;
        NEXUS 8;
        MAPFILE <TITLE.WMP>;
        BIND <TITLE.WDL>;

        SKILL probe_order { VAL 0; }
        SKILL probe_ticks { VAL 0; }

        FONT probe_digit_font,<fontdig1.pcx>,9,11;

        BMAP probe_start_map,<prbstart.pcx>,0,0,280,160;
        BMAP probe_tick_map,<prbtick.pcx>,0,0,280,160;
        PANEL probe_start_panel
        {
            PAN_MAP probe_start_map;
            POS_X 40;
            POS_Y 40;
            LAYER 900;
            FLAGS REFRESH;
        }
        PANEL probe_tick_panel
        {
            PAN_MAP probe_tick_map;
            POS_X 320;
            POS_Y 40;
            LAYER 901;
            FLAGS REFRESH;
        }

        PANEL probe_panel
        {
            POS_X 48;
            POS_Y 220;
            DIGITS 0,0,4,probe_digit_font,1,probe_order;
            DIGITS 96,0,4,probe_digit_font,1,probe_ticks;
            FLAGS REFRESH;
        }

        BMAP wall1_map,<black.pcx>,0,0,10,10;
        TEXTURE wall1_tex { BMAPS wall1_map; }
        WALL wall1 { TEXTURE wall1_tex; }
        REGION border
        {
            FLOOR_HGT 40;
            CEIL_HGT 40;
            FLOOR_TEX wall1_tex;
            CEIL_TEX wall1_tex;
            CLIP_DIST 0;
        }
        REGION nothing
        {
            FLOOR_HGT 0;
            CEIL_HGT 100;
            FLOOR_TEX wall1_tex;
            CEIL_TEX wall1_tex;
        }

        ACTION probe_start
        {
            PRINTFILE "ovsta", 0;
            PRINT_STRING "START\n";

            SET RENDER_MODE, 0;
            SET SCREEN_X, 0;
            SET SCREEN_Y, 0;
            SET SCREEN_WIDTH, 640;
            SET SCREEN_HGT, 480;
            SET FRAME_COLOR, 61;
            SET PANELS.1, probe_panel;
            SET PANELS.2, probe_start_panel;
            SET probe_start_panel.VISIBLE, 1;
            SET probe_order, 1;
            SET EACH_TICK.1, probe_tick;
            SET EACH_SEC.1, probe_timeout;
        }

        ACTION probe_tick
        {
            PRINTFILE "ovtik", 0;
            PRINT_STRING "TICK\n";
            SET EACH_SEC.1, NULL;

            IF (probe_ticks < 1)
            {
                RULE probe_order = probe_order * 10 + 2;
            }

            ADD probe_ticks, 1;
            SET FRAME_COLOR, 46;
            SET PANELS.3, probe_tick_panel;
            SET probe_tick_panel.VISIBLE, 1;
            SET EACH_TICK.1, NULL;
            EXIT;
        }

        ACTION probe_timeout
        {
            PRINTFILE "ovtim", 0;
            PRINT_STRING "TIMEOUT\n";
            SET EACH_SEC.1, NULL;
            EXIT;
        }

        IF_START probe_start;
        """;

    private static string SchedulerWmp() =>
        """
        VERTEX -64 -64 0;#0
        VERTEX 64 -64 0;#1
        VERTEX 64 64 0;#2
        VERTEX -64 64 0;#3
        REGION nothing 0 10;#0
        REGION border 40 40;#1
        WALL wall1 0 1 0 1 0 0;#0
        WALL wall1 1 2 0 1 0 0;#1
        WALL wall1 2 3 0 1 0 0;#2
        WALL wall1 3 0 0 1 0 0;#3
        PLAYER_START 0 0 90 0;#4
        """;

    private static string DosboxConfig(string workDir) =>
        $$"""
        [dosbox]
        memsize=32
        quit warning=false
        fastbioslogo=true
        startbanner=false
        title={{SchedulerProbeId}}

        [sdl]
        autolock=true
        showmenu=false

        [render]
        aspect=true

        [video]
        forcerate=60

        [autoexec]
        mount c "{{workDir}}"
        C:
        VRUN.EXE -NJ TITLE
        """;

    private static string RunBatch(string? dosboxX) => RunBatch(dosboxX, "dosbox.conf");

    private static string RunBatch(string? dosboxX, string configName)
    {
        string exe = string.IsNullOrWhiteSpace(dosboxX) ? "dosbox-x.exe" : Path.GetFullPath(dosboxX);
        return $$"""
        @echo off
        "{{exe}}" -conf "%~dp0{{configName}}" -noconsole
        """;
    }

    private static string CarrierDosboxConfig(string launchDir) =>
        $$"""
        [dosbox]
        memsize=32
        quit warning=false
        fastbioslogo=true
        startbanner=false
        title=OpenVirtue PATCHED TITLE CARRIER

        [sdl]
        autolock=true
        showmenu=false

        [render]
        aspect=true

        [video]
        forcerate=60

        [autoexec]
        mount c "{{launchDir}}"
        C:
        echo OPENVIRTUE_PATCHED_TITLE_CARRIER
        VRUN.EXE -NJ TITLE -RUN
        """;

    private static string CarrierNotes() =>
        """
        OpenVirtue patched title carrier probe.

        This folder is local-only research output. It was copied from a caller-supplied
        prepared title carrier, removed active APPEND_MODE lines from copied WDL
        files, and then instrumented TITLE.WDL with three marker writes:

        Observed patched V3.963 marker names are OVSCH0.TXT/OVSTA0.TXT/OVTIK0.TXT
        even though the 1998 manual describes a three-digit suffix.

        ovsta/OVSTA0.TXT  - start_game ran from IF_START.
        ovsch/OVSCH0.TXT  - init_game assigned testSpeed to EACH_TICK.1.
        ovtik/OVTIK0.TXT  - testSpeed ran for the first time.

        Do not commit this folder or generated game data.
        """;

    private static string Readme(string workDir, string? runtimeDir, string? dosboxX) =>
        $$"""
        # {{SchedulerProbeId}}

        Local-only oracle run folder for the first scheduler probe. These generated
        files are under `_research/` by default and must not be committed.

        ## Question

        Does an action assigned to `EACH_TICK` during `IF_START` run after the
        startup action, and how soon does the first dispatch happen?

        ## Fixture

        - `work/TITLE.WDL` defines two global skills:
          - `probe_order` starts at `0`.
          - `probe_ticks` starts at `0`.
        - `IF_START` sets `probe_order` to `1`, shows a small `DIGITS` panel,
          writes `START` to `ovsta`/`OVSTA0.TXT`, sets the frame color red, and shows a
          large red block before assigning `probe_tick` into `EACH_TICK.1`.
        - A one-second timeout appends `TIMEOUT` and exits if no tick callback
          runs first.
        - The first `EACH_TICK.1` call appends digit `2` to `probe_order`,
          increments `probe_ticks`, writes `TICK` to `ovtik`/`OVTIK0.TXT`, sets the frame
          color yellow, shows a large yellow block, unregisters itself, and
          exits.

        Possible observations:

        | Screen | Meaning |
        |--------|---------|
        | red frame/block only | Startup ran, but the tick callback did not fire before observation. |
        | yellow frame or red + yellow blocks | Startup ran, then the first tick callback ran. |
        | no red block | Startup did not run before observation. |

        If rendering is unreadable, inspect marker files after the run:

        | Files | Meaning |
        |-------|---------|
        | `OVSTA0.TXT` only | Startup ran, but no tick/timeout wrote before observation. |
        | `OVSTA0.TXT` and `OVTIK0.TXT` | Startup ran, then the first tick callback ran. |
        | `OVSTA0.TXT` and `OVTIM0.TXT` | Startup ran, but the tick callback did not run within one second. |
        | no marker files | Startup did not run or file output failed. |

        ## Local Runtime Files

        Work directory:

        ```text
        {{workDir}}
        ```

        Runtime source:

        ```text
        {{runtimeDir ?? "(not copied; rerun with --runtime-dir <retail install dir> or copy VRUN.EXE locally)"}}
        ```

        DOSBox-X executable:

        ```text
        {{dosboxX ?? "dosbox-x.exe from PATH"}}
        ```

        The generator copies only the local runtime shell files it needs
        (`VRUN.EXE`, `WWRUN.WDF`, `WWRUN.MDF`) and the local-only display
        assets required by the probe (`fontdig1.pcx`, `black.pcx`) when
        `--runtime-dir` is supplied. The red and green panel PCX files are
        generated by `ovtool`. It then packs the generated fixture, generated
        panels, and any copied display assets into `work/TITLE.WRS`. It
        deliberately does not copy retail WRS archives.

        ## Safety

        This generated probe does not run SaintsX setup scripts, `patch.exe`, or
        any other external patching tool. Do not run `setup*.bat` from this
        oracle folder. If this synthetic archive harness is insufficient, add a
        noninteractive prep step to `ovtool` first so the clean-room evidence is
        repeatable and auditable.

        ## Manual Observation Steps

        1. Launch `run-oracle.bat`.
        2. If the synthetic fixture boots, record whether the red startup block
           and yellow tick block appear, or whether the full frame turns red or
           yellow.
        3. Reduce the result into `docs/clean-room/observations/{{SchedulerProbeId}}.md`.

        If the runtime does not accept the synthetic archive, record that as a
        harness result before drawing scheduler conclusions.

        ## Prepared Title Carrier

        Passing `--carrier-dir <prepared-title-dir>` also writes
        `patched-title-carrier-launch/` plus `run-title-carrier.bat`. This
        local-only harness copies the caller-supplied prepared title carrier,
        removes active `APPEND_MODE` lines from copied WDL files, instruments
        its `TITLE.WDL`, and runs the patched runtime without
        invoking SaintsX setup scripts or `patch.exe`.

        Expected marker files after `run-title-carrier.bat` exits:

        | Files | Meaning |
        |-------|---------|
        | `OVSTA0.TXT`, `OVSCH0.TXT`, `OVTIK0.TXT` | The original title carrier ran startup, assigned `EACH_TICK.1`, then dispatched the first tick callback. |
        | `OVSTA0.TXT`, `OVSCH0.TXT` only | Startup assigned `EACH_TICK.1`, but the first tick callback did not dispatch before observation. |
        | `OVSTA0.TXT` only | Startup began, but did not reach the scheduler assignment. |
        | no marker files | The carrier did not reach the instrumented start action, or file output failed. |
        """;

    private static int Usage(string? error = null)
    {
        if (error is not null)
        {
            Console.Error.WriteLine($"error: {error}");
        }

        Console.WriteLine(
            """
            Usage:
              ovtool oracle prepare scheduler-if-start-cycle [output-dir] [--runtime-dir <dir>] [--carrier-dir <dir>] [--dosbox-x <exe>]
            """);
        Console.WriteLine(ProjectionProbeGenerator.UsageLines);

        return error is null ? 0 : 2;
    }

    private static int SchedulerUsage() => Usage();
}
