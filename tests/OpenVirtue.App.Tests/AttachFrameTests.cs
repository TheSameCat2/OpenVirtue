// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.App.Platform;
using OpenVirtue.App.Rendering;
using OpenVirtue.Engine;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Host;
using Xunit.Abstractions;

namespace OpenVirtue.App.Tests;

/// <summary>
/// Draws the attach probe's <c>pos</c> and <c>wall-pos</c> scenes (OV-20261104-attach) and checks
/// each layer's screen rectangle against the original's screenshot readings: a 32×48 orange thing
/// 24 steps ahead with a 16 px cyan square attached at POS_X 24, POS_Y 40, and a blue wall 32 steps
/// ahead with the square attached at POS_X 24, POS_Y 16.
/// </summary>
[Collection(GlTestGroup.Name)]
public sealed class AttachFrameTests
{
    private const int Width = 640;
    private const int Height = 480;

    private readonly ITestOutputHelper _output;

    public AttachFrameTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void AttachLayers_LandWhereTheOriginalDrewThem()
    {
        if (!GlContext.TryOpen(Width, Height, "OpenVirtue attach", out SdlPlatform? platform, out GlRenderBackend? graphics, out string? error) ||
            platform is null ||
            graphics is null)
        {
            _output.WriteLine($"SKIP: no GL context ({error})");
            return;
        }

        using (platform)
        using (graphics)
        {
            // The thing: orange x 295–343, y 379–459; the square x 332–355, y 448–473 (round 4, pos).
            (int X0, int X1, int Y0, int Y1) orange = Measure(platform, graphics, ThingScene(), "O");
            (int X0, int X1, int Y0, int Y1) square = Measure(platform, graphics, ThingScene(), "C");
            AssertRect(orange, 295, 343, 379, 459);
            AssertRect(square, 332, 355, 448, 473);

            // The wall's square: x 357–374, y 159–178 (round 4, wall-pos).
            (int X0, int X1, int Y0, int Y1) wallSquare = Measure(platform, graphics, WallScene(), "C");
            AssertRect(wallSquare, 357, 374, 159, 178);
        }
    }

    private (int X0, int X1, int Y0, int Y1) Measure(SdlPlatform platform, GlRenderBackend graphics, Level level, string color)
    {
        var textures = new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase)
        {
            ["at_base"] = Solid(240, 130, 40),
            ["at_x"] = Solid(40, 220, 220),
            ["blu_tex"] = Solid(40, 80, 230),
            ["flr_tex"] = Solid(120, 120, 120),
            ["cel_tex"] = Solid(60, 60, 60),
        };
        using RuntimeHost host = RuntimeHost.OpenLevel(level, textures, platform, platform, new NullAudioSink(), graphics);
        host.Boot();
        platform.PumpEvents();
        host.Frame(present: false);
        RgbaImage image = graphics.ReadRgba();

        int x0 = int.MaxValue, x1 = -1, y0 = int.MaxValue, y1 = -1;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int i = ((y * image.Width) + x) * 4;
                if (Classify(image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2]) != color)
                {
                    continue;
                }

                x0 = Math.Min(x0, x);
                x1 = Math.Max(x1, x);
                y0 = Math.Min(y0, y);
                y1 = Math.Max(y1, y);
            }
        }

        (int X0, int X1, int Y0, int Y1) rect = (
            Scale(x0, image.Width, Width), Scale(x1, image.Width, Width),
            Scale(y0, image.Height, Height), Scale(y1, image.Height, Height));
        _output.WriteLine($"{color}: x {rect.X0}..{rect.X1} y {rect.Y0}..{rect.Y1} ({image.Width}x{image.Height})");
        return rect;
    }

    private static void AssertRect((int X0, int X1, int Y0, int Y1) rect, int x0, int x1, int y0, int y1)
    {
        Assert.InRange(rect.X0, x0 - 2, x0 + 2);
        Assert.InRange(rect.X1, x1 - 2, x1 + 2);
        Assert.InRange(rect.Y0, y0 - 2, y0 + 2);
        Assert.InRange(rect.Y1, y1 - 2, y1 + 2);
    }

    private static int Scale(int value, int actual, int virtualSize) => (int)Math.Round(value * (virtualSize / (double)actual));

    private static string Classify(byte r, byte g, byte b)
    {
        if (r > 180 && g > 90 && g < 170 && b < 90)
        {
            return "O";
        }

        if (g > 150 && b > 150 && r < 120)
        {
            return "C";
        }

        return "?";
    }

    private static TextureImage Solid(byte r, byte g, byte b) => new(1, 1, [r, g, b, 255]);

    private const string Room = """
        VERTEX -8 -30 0;#0
        VERTEX 60 -30 0;#1
        VERTEX 60 30 0;#2
        VERTEX -8 30 0;#3
        REGION room 0 16;#0
        WALL blu_wall 0 1 0 -1 0 0;#0
        WALL blu_wall 1 2 0 -1 0 0;#1
        WALL blu_wall 2 3 0 -1 0 0;#2
        WALL blu_wall 3 0 0 -1 0 0;#3
        """;

    private const string Common = """
        VIDEO S640x480;
        MAPFILE <m.wmp>;
        BMAP at_bm,<PRBATB.PCX>,0,0,32,48;
        BMAP at_qm,<PRBATQ.PCX>,0,0,16,16;
        BMAP blu_map,<blu.pcx>,0,0,64,64;
        BMAP flr_map,<flr.pcx>,0,0,64,64;
        BMAP cel_map,<cel.pcx>,0,0,64,64;
        TEXTURE blu_tex { SCALE_X 4; SCALE_Y 4; BMAPS blu_map; }
        TEXTURE flr_tex { SCALE_X 4; SCALE_Y 4; BMAPS flr_map; }
        TEXTURE cel_tex { SCALE_X 4; SCALE_Y 4; BMAPS cel_map; }
        TEXTURE at_base { SCALE_XY 16, 16; BMAPS at_bm; }
        WALL blu_wall { TEXTURE blu_tex; }
        REGION room { FLOOR_HGT 0; CEIL_HGT 16; FLOOR_TEX flr_tex; CEIL_TEX cel_tex; }
        ACTION probe_start { SET PLAYER_ARC, 1; SET PLAYER_TILT, 0; SET PLAYER_SIZE, 8; SET PLAYER_Z, 8; }
        IF_START probe_start;
        """;

    private static Level ThingScene() => Load(
        Room + "\nTHING at_t 24 0 0 0;#4\nPLAYER_START 0 0 0 0;#5",
        Common + "\nTEXTURE at_x { BMAPS at_qm; POS_X 24; POS_Y 40; }\nTHING at_t { TEXTURE at_base; ATTACH at_x; }");

    private static Level WallScene() => Load(
        Room + "\nVERTEX 32 -4 0;#4\nVERTEX 32 4 0;#5\nWALL at_w 4 5 0 0 0 0;#4\nPLAYER_START 0 0 0 0;#6",
        Common + "\nTEXTURE at_x { BMAPS at_qm; POS_X 24; POS_Y 16; }\nWALL at_w { TEXTURE blu_tex; ATTACH at_x; }");

    private static Level Load(string map, string main)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        return LevelLoader.LoadCore("attach", main, name => resources.GetValueOrDefault(Path.GetFileName(name)));
    }
}
