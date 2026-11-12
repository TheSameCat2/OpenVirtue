// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.App.Platform;
using OpenVirtue.App.Rendering;
using OpenVirtue.Host;

namespace OpenVirtue.App;

internal static class Program
{
    private static int Main(string[] args)
    {
        bool mouseLook = args.Contains("--mouselook", StringComparer.OrdinalIgnoreCase);
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.Ordinal))
        {
            PrintUsage();
            return 0;
        }

        string[] positional = args.Where(static a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        string archivePath;
        string mainWdl;
        if (positional.Length >= 1)
        {
            archivePath = positional[0];
            mainWdl = positional.Length >= 2 ? positional[1] : Path.GetFileNameWithoutExtension(archivePath) + ".WDL";
        }
        else if (GameLocator.FindTitleArchive(GameLocator.DefaultCandidates()) is { } title)
        {
            // No arguments: the game as the original starts it, from the title archive.
            archivePath = title;
            mainWdl = GameLocator.TitleScript;
        }
        else
        {
            ReportMissingGame();
            return 1;
        }
        SdlPlatform? platform = null;
        SdlAudioSink? audioDevice = null;
        IAudioSink? audio = null;
        GlRenderBackend? graphics = null;
        RuntimeHost? host = null;
        try
        {
            if (!SdlPlatform.TryCreate(1280, 720, "OpenVirtue", hidden: false, out platform, out string? windowError) ||
                platform is null)
            {
                Console.Error.WriteLine(windowError ?? "SDL window failed.");
                return 1;
            }

            if (SdlAudioSink.TryCreate(out audioDevice, out string? audioError) && audioDevice is not null)
            {
                audio = audioDevice;
            }
            else
            {
                if (!string.IsNullOrEmpty(audioError))
                {
                    Console.Error.WriteLine(audioError);
                }

                audio = new NullAudioSink();
            }

            graphics = new GlRenderBackend(SdlPlatform.GetProcAddress, platform.Swap, platform.Width, platform.Height);
            host = RuntimeHost.OpenArchive(archivePath, mainWdl, platform, platform, audio, graphics);
            host.MouseLook = mouseLook;
            host.Boot();
            while (platform.IsOpen && !host.ExitRequested)
            {
                platform.PumpEvents();
                host.Frame();
            }

            return 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            Console.Error.WriteLine(ex);
            ShowError($"OpenVirtue could not open the game files:\n\n{ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            host?.Dispose();
            graphics?.Dispose();
            audioDevice?.Dispose();
            platform?.Dispose();
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: OpenVirtue.App [archive.wrs [main.wdl]] [--mouselook]");
        Console.WriteLine("With no archive, OpenVirtue starts the game from TITLE.WRS in the first of these folders that has it:");
        foreach (string folder in GameLocator.DefaultCandidates())
        {
            Console.WriteLine("  " + folder);
        }

        Console.WriteLine($"({GameLocator.GameDirectoryVariable} names a folder of your choice.)");
        Console.WriteLine("Controls (as the original's): cursor keys walk and turn, [,] and [.] strafe, PgUp/PgDn look up and down,");
        Console.WriteLine("Home/End jump and duck, hold Shift to run. The mouse turns and walks; hold Alt to strafe with it.");
        Console.WriteLine("--mouselook makes moving the mouse forward and back look up and down instead of walking.");
    }

    /// <summary>
    /// No game files were found: makes the per-user game folder, so there is somewhere to copy them, and says so in a
    /// message box as well as on the console (a double-clicked program has no console).
    /// </summary>
    private static void ReportMissingGame()
    {
        string userFolder = GameLocator.UserGameDirectory(GameLocator.DefaultUserDataDirectory());
        try
        {
            Directory.CreateDirectory(userFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The message still names the folder.
        }

        string message =
            "OpenVirtue needs your own copy of Saints of Virtue.\n\n" +
            "Copy the game's files (TITLE.WRS, START.WRS, APATHY.WRS, HEART.WRS, LEGALISM.WRS, NEWAGE.WRS) into:\n\n" +
            userFolder + "\n\n" +
            "or into a folder named \"game\" next to the OpenVirtue program, then start OpenVirtue again.";
        Console.Error.WriteLine(message);
        ShowError(message);
    }

    private static unsafe void ShowError(string message)
    {
        try
        {
            SDL.SDL3.SDL_ShowSimpleMessageBox(SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR, "OpenVirtue", message, null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No SDL: the console message stands.
        }
    }
}
