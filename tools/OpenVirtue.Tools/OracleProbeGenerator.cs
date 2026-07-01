// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

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
            "-h" or "--help" or "help" => Usage(),
            _ => Usage($"Unknown oracle probe '{args[0]}'."),
        };
    }

    private static int PrepareSchedulerProbe(string[] args)
    {
        string outputDir = Path.Combine("_research", "oracle-runs", SchedulerProbeId);
        string? runtimeDir = null;
        string? dosboxX = null;

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
        File.WriteAllText(Path.Combine(fullOutputDir, "dosbox.conf"), DosboxConfig(workDir), Encoding.ASCII);
        File.WriteAllText(Path.Combine(fullOutputDir, "run-oracle.bat"), RunBatch(dosboxX), Encoding.ASCII);
        File.WriteAllText(Path.Combine(fullOutputDir, "README.md"), Readme(workDir, runtimeDir, dosboxX), Encoding.UTF8);

        int copied = 0;
        int copiedAssets = 0;
        if (runtimeDir is not null)
        {
            copied = CopyRuntimeShell(runtimeDir, workDir);
            copiedAssets = CopyProbeAssets(runtimeDir, workDir);
        }

        int packed = WriteProbeArchive(workDir);

        Console.WriteLine($"Prepared {SchedulerProbeId} in {fullOutputDir}");
        Console.WriteLine($"  fixture: {Path.Combine(workDir, "TITLE.WDL")}");
        Console.WriteLine($"  map:     {Path.Combine(workDir, "TITLE.WMP")}");
        Console.WriteLine($"  archive: {Path.Combine(workDir, "TITLE.WRS")} ({packed} entries)");
        Console.WriteLine($"  launch:  {Path.Combine(fullOutputDir, "run-oracle.bat")}");
        if (runtimeDir is not null)
        {
            Console.WriteLine($"  copied {copied} runtime/support file(s) into work/; retail WRS archives were not copied.");
            Console.WriteLine($"  copied {copiedAssets} local-only display asset(s) into work/.");
        }
        else
        {
            Console.WriteLine("  no runtime copied; copy VRUN.EXE/support files into work/ or rerun with --runtime-dir.");
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

    private static int WriteProbeArchive(string workDir)
    {
        string[] names =
        [
            "TITLE.WDL",
            "TITLE.WMP",
            "font_pnl.pcx",
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
            "font_pnl.pcx",
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

        FONT probe_font,<font_pnl.pcx>,8,16;
        PANEL probe_panel
        {
            POS_X 8;
            POS_Y 8;
            DIGITS 0,0,4,probe_font,1,probe_order;
            DIGITS 80,0,4,probe_font,1,probe_ticks;
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
            SET PANELS.1, probe_panel;
            SET probe_order, 1;
            SET EACH_TICK.1, probe_tick;
        }

        ACTION probe_tick
        {
            IF (probe_ticks < 1)
            {
                RULE probe_order = probe_order * 10 + 2;
            }

            ADD probe_ticks, 1;
            SET EACH_TICK.1, NULL;
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

    private static string RunBatch(string? dosboxX)
    {
        string exe = string.IsNullOrWhiteSpace(dosboxX) ? "dosbox-x.exe" : Path.GetFullPath(dosboxX);
        return $$"""
        @echo off
        "{{exe}}" -conf "%~dp0dosbox.conf" -noconsole
        """;
    }

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
        - `IF_START` sets `probe_order` to `1`, shows a `DIGITS` panel, and
          assigns `probe_tick` into `EACH_TICK.1`.
        - The first `EACH_TICK.1` call appends digit `2` to `probe_order`,
          increments `probe_ticks`, and unregisters itself.

        Possible observations:

        | `probe_order` | Meaning |
        |---------------|---------|
        | `12` | Startup ran, then the first tick callback ran. |
        | `1`  | Startup ran, but the tick callback did not fire before observation. |
        | `0`  | Startup did not run before observation. |

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
        (`VRUN.EXE`, `WWRUN.WDF`, `WWRUN.MDF`) and the two local-only display
        assets required by the probe (`font_pnl.pcx`, `black.pcx`) when
        `--runtime-dir` is supplied. It then packs the generated fixture and any
        copied display assets into `work/TITLE.WRS`. It deliberately does not copy
        retail WRS archives.

        ## Safety

        This generated probe does not run SaintsX setup scripts, `patch.exe`, or
        any other external patching tool. Do not run `setup*.bat` from this
        oracle folder. If this synthetic archive harness is insufficient, add a
        noninteractive prep step to `ovtool` first so the clean-room evidence is
        repeatable and auditable.

        ## Manual Observation Steps

        1. Launch `run-oracle.bat`.
        2. If the synthetic fixture boots, read the two visible numeric fields:
           `probe_order` is on the left and `probe_ticks` is on the right.
        3. Reduce the result into `docs/clean-room/observations/{{SchedulerProbeId}}.md`.

        If the runtime does not accept the synthetic archive, record that as a
        harness result before drawing scheduler conclusions.
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
              ovtool oracle prepare scheduler-if-start-cycle [output-dir] [--runtime-dir <dir>] [--dosbox-x <exe>]
            """);

        return error is null ? 0 : 2;
    }

    private static int SchedulerUsage() => Usage();
}
