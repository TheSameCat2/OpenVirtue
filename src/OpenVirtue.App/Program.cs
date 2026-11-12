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
        string[] positional = args.Where(static a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (positional.Length < 1)
        {
            Console.Error.WriteLine("Usage: OpenVirtue.App <archive.wrs> [main.wdl] [--mouselook]");
            Console.Error.WriteLine("Controls (as the original's): cursor keys walk and turn, [,] and [.] strafe, PgUp/PgDn look up and down,");
            Console.Error.WriteLine("Home/End jump and duck, hold Shift to run. The mouse turns and walks; hold Alt to strafe with it.");
            Console.Error.WriteLine("--mouselook makes moving the mouse forward and back look up and down instead of walking.");
            return 1;
        }

        string archivePath = positional[0];
        string mainWdl = positional.Length >= 2 ? positional[1] : Path.GetFileNameWithoutExtension(archivePath) + ".WDL";
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
}
