// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text;

/// <summary>
/// Writes the DOSBox-X launch files for an oracle carrier: a POSIX <c>run.sh</c> +
/// <c>dosbox-x.conf</c> for native Linux DOSBox-X and a <c>run.bat</c> + <c>dosbox.conf</c>
/// for Windows. Both mount the carrier's <c>work/</c> directory as C: and run the patched
/// DOS runtime with the probe world, then exit. Settings follow OV-20260701
/// (<c>forcerate=60</c>) plus <c>aspect=false</c> / <c>output=surface</c> so any manual
/// capture is raw framebuffer pixels.
/// </summary>
internal static class OracleLaunchers
{
    /// <summary>The world name every loose-file carrier is launched as (TITLE.WDL/TITLE.WMP).</summary>
    public const string WorldName = "TITLE";

    public const string RuntimeCommand = "VRUN.EXE -NJ " + WorldName + " -RUN";

    /// <summary>
    /// The autoexec body: list what DOS sees, optionally queue scripted key presses with DOSBox-X
    /// <c>AUTOTYPE</c> (it returns at once and types on its own timer, so it goes before the
    /// runtime), run the runtime with its console redirected to <c>OVCON.TXT</c> (the Genesis
    /// banner and compiler errors are otherwise only visible on screen), then leave the emulator.
    /// </summary>
    public static string AutoexecBody(string? autotype = null) =>
        "dir /b > OVDIR.TXT\n" +
        (string.IsNullOrWhiteSpace(autotype) ? "" : "AUTOTYPE " + autotype + "\n") +
        RuntimeCommand + " > OVCON.TXT\n" +
        "exit";

    /// <summary>
    /// Launcher settings. <c>CompletionMarker</c> is the five-character PRINTFILE base name
    /// whose <c>&lt;name&gt;*.TXT</c> file proves the probe action ran to completion; when set,
    /// <c>run.sh</c> exits 3 if it is missing after the run. <c>Autotype</c>, when set, is the
    /// argument list of an <c>AUTOTYPE</c> line in the autoexec (scripted key presses).
    /// <c>ClickAfterSeconds</c>, when set, makes <c>run.sh</c> start <see cref="ClickHelperName"/>
    /// next to it, which sends one left click to the DOSBox-X window that many seconds after launch.
    /// </summary>
    public sealed record Options(
        string Title,
        string VariantLabel,
        string WorkDir,
        string? DosboxX,
        string LaunchLogRelativePath = "../launches.md",
        string? CompletionMarker = null,
        string? Autotype = null,
        double? ClickAfterSeconds = null);

    /// <summary>The host-side click helper's file name in a variant directory.</summary>
    public const string ClickHelperName = "x11_click.py";

    /// <summary>The click helper as shipped next to <c>ovtool</c> (copied from <c>tools/oracle/</c> at build time).</summary>
    public static string ClickHelperSource() =>
        Path.Combine(AppContext.BaseDirectory, "oracle", ClickHelperName) is var path && File.Exists(path)
            ? path
            : throw new FileNotFoundException("The click helper was not copied next to ovtool.", path);

    /// <summary>Writes both the Linux (<c>run.sh</c>, <c>dosbox-x.conf</c>) and Windows (<c>run.bat</c>, <c>dosbox.conf</c>) launchers.</summary>
    public static void Write(string variantDir, Options options)
    {
        WriteLinux(variantDir, options);
        WriteWindows(variantDir, options);
    }

    public static void WriteLinux(string variantDir, Options options)
    {
        Directory.CreateDirectory(variantDir);
        string workDir = Path.GetFullPath(options.WorkDir);
        File.WriteAllText(Path.Combine(variantDir, "dosbox-x.conf"), DosboxConfig(options.Title, workDir, options.Autotype), Encoding.ASCII);
        string runSh = Path.Combine(variantDir, "run.sh");
        File.WriteAllText(runSh, RunShell(options), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        TryMarkExecutable(runSh);
    }

    public static void WriteWindows(string variantDir, Options options)
    {
        Directory.CreateDirectory(variantDir);
        string workDir = Path.GetFullPath(options.WorkDir);
        File.WriteAllText(Path.Combine(variantDir, "dosbox.conf"), DosboxConfig(options.Title, workDir, options.Autotype), Encoding.ASCII);
        File.WriteAllText(Path.Combine(variantDir, "run.bat"), RunBatch(options.DosboxX, "dosbox.conf"), Encoding.ASCII);
    }

    public static string DosboxConfig(string title, string workDir, string? autotype = null) =>
        $$"""
        [sdl]
        autolock=false
        showmenu=false
        output=surface

        [dosbox]
        memsize=32
        quit warning=false
        fastbioslogo=true
        startbanner=false
        title={{title}}

        [render]
        aspect=false

        [video]
        forcerate=60

        [autoexec]
        mount c "{{workDir}}"
        C:
        {{AutoexecBody(autotype)}}

        """;

    public static string RunBatch(string? dosboxX, string configName)
    {
        string exe = string.IsNullOrWhiteSpace(dosboxX) ? "dosbox-x.exe" : Path.GetFullPath(dosboxX);
        return $$"""
        @echo off
        "{{exe}}" -conf "%~dp0{{configName}}" -noconsole
        """;
    }

    private static string RunShell(Options options)
    {
        string exe = string.IsNullOrWhiteSpace(options.DosboxX) ? "dosbox-x" : Path.GetFullPath(options.DosboxX);
        string command = string.IsNullOrWhiteSpace(options.Autotype)
            ? RuntimeCommand
            : $"AUTOTYPE {options.Autotype}; {RuntimeCommand}";
        string clickAfter = options.ClickAfterSeconds is { } seconds
            ? seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
            : "";
        if (clickAfter.Length > 0)
        {
            command += $"; host left click at {clickAfter} s";
        }

        string clickStart = clickAfter.Length > 0
            ? $$"""
              python3 -I "$DIR/{{ClickHelperName}}" --title "{{options.Title}}" --after {{clickAfter}} >"$DIR/click-$STAMP.log" 2>&1 &
              CLICK_PID=$!
              """
            : "CLICK_PID=\"\"";
        string completionCheck = options.CompletionMarker is { } marker
            ? $$"""
              if [ "$RC" -eq 0 ] && [ -z "$(ls "$WORK"/{{marker.ToUpperInvariant()}}*.TXT 2>/dev/null)" ]; then
                RC=3   # emulator exited cleanly but the probe action never wrote its completion marker
              fi
              """
            : string.Empty;
        return $$"""
        #!/usr/bin/env sh
        # Generated by ovtool oracle prepare. Local-only; do not commit.
        # Launches native DOSBox-X on this carrier, parks previous probe outputs under
        # work/_stale/<stamp>/ (nothing is deleted), and appends one row to the launch log.
        # Exit codes: dosbox-x's own, 124 on timeout, 3 when the completion marker is missing.
        set -u
        DIR="$(cd "$(dirname "$0")" && pwd)"
        WORK="$DIR/work"
        LOG="$DIR/{{options.LaunchLogRelativePath}}"
        STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
        for f in "$WORK"/OV*.PCX "$WORK"/OV*.TXT; do
          [ -e "$f" ] || continue
          mkdir -p "$WORK/_stale/$STAMP"
          mv "$f" "$WORK/_stale/$STAMP/"
        done
        START="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
        {{clickStart}}
        timeout -k 10 "${OV_TIMEOUT:-120}" "${DOSBOX_X:-{{exe}}}" -conf "$DIR/dosbox-x.conf" -nogui -nomenu -nopromptfolder >"$DIR/dosbox-x-$STAMP.log" 2>&1
        RC=$?
        if [ -n "$CLICK_PID" ]; then
          kill "$CLICK_PID" 2>/dev/null
          wait "$CLICK_PID" 2>/dev/null
        fi
        {{completionCheck}}
        OUT="$(cd "$WORK" && ls OV*.PCX OV*.TXT 2>/dev/null | tr '\n' ' ')"
        if [ ! -f "$LOG" ]; then
          printf '| when (UTC) | variant | command | rc | outputs |\n|---|---|---|---|---|\n' >"$LOG"
        fi
        printf '| %s | %s | `%s` via `dosbox-x -conf dosbox-x.conf` (forcerate=60, aspect=false, output=surface) | %s | %s |\n' \
          "$START" "{{options.VariantLabel}}" "{{command}}" "$RC" "${OUT:-none}" >>"$LOG"
        echo "{{options.VariantLabel}}: rc=$RC outputs: ${OUT:-none}"
        exit $RC

        """;
    }

    private static void TryMarkExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (IOException)
        {
            // Best effort; the user can chmod +x.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort; the user can chmod +x.
        }
    }

    /// <summary>
    /// Moves an existing directory to <c>&lt;parent&gt;/_stale/&lt;name&gt;-&lt;stamp&gt;</c> instead of
    /// deleting it, but only when it holds something observational (probe outputs
    /// <c>OV*.PCX</c>/<c>OV*.TXT</c> or a <c>dosbox-x-*.log</c>); never-launched carriers are
    /// simply overwritten in place, so re-preparing does not pile up copies of the runtime.
    /// </summary>
    public static void ParkIfExists(string directory)
    {
        if (!Directory.Exists(directory) || !HasObservations(directory))
        {
            return;
        }

        string parent = Path.GetDirectoryName(Path.GetFullPath(directory)) ?? ".";
        string staleRoot = Path.Combine(parent, "_stale");
        Directory.CreateDirectory(staleRoot);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        string target = Path.Combine(staleRoot, $"{Path.GetFileName(directory)}-{stamp}");
        int suffix = 1;
        while (Directory.Exists(target))
        {
            target = Path.Combine(staleRoot, $"{Path.GetFileName(directory)}-{stamp}-{suffix++}");
        }

        Directory.Move(directory, target);
    }

    private static bool HasObservations(string directory) =>
        Directory.EnumerateFiles(directory, "OV*.PCX", SearchOption.AllDirectories).Any() ||
        Directory.EnumerateFiles(directory, "OV*.TXT", SearchOption.AllDirectories).Any() ||
        Directory.EnumerateFiles(directory, "dosbox-x-*.log", SearchOption.AllDirectories).Any();
}
