// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.App.Platform;
using OpenVirtue.App.Rendering;

namespace OpenVirtue.App.Tests;

/// <summary>SDL and GL are not safe to open from two test threads at once, so GL tests share one collection.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlTestGroup
{
    public const string Name = "GL";
}

/// <summary>
/// Opens a hidden SDL window with a GL 3.3 context, falling back to SDL's offscreen driver.
/// Tests skip with the returned error when neither works.
/// </summary>
internal static class GlContext
{
    public static bool TryOpen(
        int width,
        int height,
        string title,
        out SdlPlatform? platform,
        out GlRenderBackend? graphics,
        out string? error)
    {
        if (TryOpenOnce(width, height, title, out platform, out graphics, out error))
        {
            return true;
        }

        string? first = error;
        string? previousDriver = Environment.GetEnvironmentVariable("SDL_VIDEODRIVER");
        Environment.SetEnvironmentVariable("SDL_VIDEODRIVER", "offscreen");
        try
        {
            if (TryOpenOnce(width, height, title, out platform, out graphics, out error))
            {
                return true;
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SDL_VIDEODRIVER", previousDriver);
        }

        error = string.IsNullOrEmpty(error) ? first : $"{first}; offscreen: {error}";
        return false;
    }

    private static bool TryOpenOnce(
        int width,
        int height,
        string title,
        out SdlPlatform? platform,
        out GlRenderBackend? graphics,
        out string? error)
    {
        graphics = null;
        if (!SdlPlatform.TryCreate(width, height, title, hidden: true, out platform, out error) ||
            platform is null)
        {
            return false;
        }

        try
        {
            graphics = new GlRenderBackend(SdlPlatform.GetProcAddress, platform.Swap, platform.Width, platform.Height);
            return true;
        }
        catch
        {
            platform.Dispose();
            platform = null;
            throw;
        }
    }
}
