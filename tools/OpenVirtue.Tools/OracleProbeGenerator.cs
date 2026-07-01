// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text;
using OpenVirtue.Formats.Wdl;
using OpenVirtue.Formats.Wmp;

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
        if (runtimeDir is not null)
        {
            copied = CopyRuntimeShell(runtimeDir, workDir);
        }

        Console.WriteLine($"Prepared {SchedulerProbeId} in {fullOutputDir}");
        Console.WriteLine($"  fixture: {Path.Combine(workDir, "TITLE.WDL")}");
        Console.WriteLine($"  map:     {Path.Combine(workDir, "TITLE.WMP")}");
        Console.WriteLine($"  launch:  {Path.Combine(fullOutputDir, "run-oracle.bat")}");
        if (runtimeDir is not null)
        {
            Console.WriteLine($"  copied {copied} runtime/support file(s) into work/; WRS archives were not copied.");
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

    private static void ValidateFixture(string wdl, string wmp)
    {
        WdlParser.Parse(wdl);
        WmpMap.Read(wmp);
    }

    private static string SchedulerWdl() =>
        """
        // Synthetic OpenVirtue clean-room oracle probe.
        // Local-only generated file; do not commit generated WDL/WMP artifacts.

        MAPFILE <TITLE.WMP>;

        SKILL probe_order { VAL 0; }
        SKILL probe_ticks { VAL 0; }

        ACTION probe_start
        {
            RULE probe_order = probe_order * 10 + 1;
        }

        ACTION probe_cycle
        {
            IF (probe_ticks < 1)
            {
                RULE probe_order = probe_order * 10 + 2;
            }

            RULE probe_ticks += 1;
        }

        IF_START probe_start;

        THING probe_actor
        {
            HEIGHT 1;
            each_cycle probe_cycle;
        }
        """;

    private static string SchedulerWmp() =>
        """
        VERTEX -64 -64 0;#0
        VERTEX 64 -64 0;#1
        VERTEX 64 64 0;#2
        VERTEX -64 64 0;#3
        REGION probe_room 0 64;#0
        WALL probe_wall 0 1 0 -1 0 0;#0
        WALL probe_wall 1 2 0 -1 0 0;#1
        WALL probe_wall 2 3 0 -1 0 0;#2
        WALL probe_wall 3 0 0 -1 0 0;#3
        THING probe_actor 0 24 0 0;#0
        PLAYER_START 0 0 0 0;#1
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
        VRUN.EXE -NJ TITLE -RUN
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

        Does the original runtime execute `IF_START` before the first scheduled
        `each_cycle` dispatch?

        ## Fixture

        - `work/TITLE.WDL` defines two global skills:
          - `probe_order` starts at `0`.
          - `probe_ticks` starts at `0`.
        - `IF_START` appends digit `1` to `probe_order`.
        - The first `each_cycle` call appends digit `2` to `probe_order` and
          increments `probe_ticks`.

        Possible observations:

        | `probe_order` | Meaning |
        |---------------|---------|
        | `12` | Startup ran before the first cycle. |
        | `21` | A cycle ran before startup. |
        | `1`  | Startup ran, but the cycle did not attach/fire before observation. |
        | `2`  | Cycle fired, but startup did not run before observation. |

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
        (`VRUN.EXE`, `WWRUN.WDF`, `WWRUN.MDF`) when `--runtime-dir` is supplied.
        It deliberately does not copy retail WRS archives.

        ## Manual Observation Steps

        1. Launch `run-oracle.bat`.
        2. If the synthetic fixture boots, enable SaintsX debug mode with
           `CTRL+ALT+C`.
        3. Press `V` to show variables.
        4. Record only the values of `probe_order` and `probe_ticks`.
        5. Reduce the result into `docs/clean-room/observations/{{SchedulerProbeId}}.md`.

        If the runtime does not accept loose synthetic files, record that as a
        harness result and adjust the local-only wrapper before drawing scheduler
        conclusions.
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
