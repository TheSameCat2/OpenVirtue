// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.App.Platform;
using OpenVirtue.App.Rendering;
using OpenVirtue.Engine;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Formats.Pcx;
using OpenVirtue.Host;
using Xunit.Abstractions;

namespace OpenVirtue.App.Tests;

/// <summary>
/// Draws the tilt-0 synthetic room and checks the far-wall stripe order against the measured
/// DOSBox-X frame. Facing map +x, the stripes read green, red, green, red, green, red from
/// left to right, and the floor is at the bottom of the frame.
/// </summary>
public sealed class Tilt0FrameTests
{
    private const int Width = 640;
    private const int Height = 480;

    private static readonly int[] ExpectedEdges = [0, 173, 222, 271, 320, 368, 417, 466];
    private static readonly string[] ExpectedColors = ["B", "G", "R", "G", "R", "G", "R", "B"];

    private readonly ITestOutputHelper _output;

    public Tilt0FrameTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Tilt0_FarWall_ReadsGreenRedFromTheLeftAndFloorAtTheBottom()
    {
        if (!TryOpenGl(out SdlPlatform? platform, out GlRenderBackend? graphics, out string? error) ||
            platform is null ||
            graphics is null)
        {
            _output.WriteLine($"SKIP: no GL context ({error})");
            return;
        }

        using (platform)
        using (graphics)
        {
            var textures = new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase)
            {
                ["red_tex"] = Solid(220, 32, 32),
                ["grn_tex"] = Solid(32, 200, 32),
                ["blu_tex"] = Solid(40, 80, 230),
                ["mag_tex"] = Solid(220, 40, 220),
                ["flr_tex"] = Solid(120, 120, 120),
                ["cel_tex"] = Solid(60, 60, 60),
            };
            using RuntimeHost host = RuntimeHost.OpenLevel(
                TiltRoom(),
                textures,
                platform,
                platform,
                new NullAudioSink(),
                graphics);
            host.Boot();
            Assert.Equal(8f, host.Camera.Position.Y, 3);
            Assert.Equal(8, host.Runtime.GetSkill("PLAYER_Z"), 6);

            platform.PumpEvents();
            host.Frame(present: false);
            RgbaImage image = graphics.ReadRgba();
            string pngPath = Path.Combine(Path.GetTempPath(), "ov-tilt0-gl.png");
            File.WriteAllBytes(pngPath, PngWriter.Encode(image.Width, image.Height, image.Pixels));
            _output.WriteLine($"GL {graphics.Version} framebuffer {image.Width}x{image.Height} written to {pngPath}");

            List<(int X, string Color)> runs = ColorRuns(image.Pixels, image.Width, image.Height, Row(image.Height, 200));
            _output.WriteLine("GL row 200: " + string.Join(", ", runs.Select(run => $"{run.X}:{run.Color}")));
            AssertStripeRuns(runs, image.Width);

            Assert.Equal("G", Sample(image, 310, 200));
            Assert.Equal("R", Sample(image, 330, 200));
            Assert.Equal("R", Sample(image, 330, 100));
            Assert.Equal("F", Sample(image, 320, 400));

            string? pcxPath = FindOraclePcx();
            if (pcxPath is null)
            {
                _output.WriteLine("OVTLT0.PCX was not next to the solution; geometric check only.");
                return;
            }

            PcxImage pcx = PcxImage.Read(File.ReadAllBytes(pcxPath));
            byte[] rgba = pcx.ToRgba32();
            List<(int X, string Color)> oracle = ColorRuns(rgba, pcx.Width, pcx.Height, Row(pcx.Height, 200));
            _output.WriteLine("PCX row 200: " + string.Join(", ", oracle.Select(run => $"{run.X}:{run.Color}")));
            Assert.Equal(oracle.Count, runs.Count);
            for (int i = 0; i < oracle.Count; i++)
            {
                Assert.Equal(oracle[i].Color, runs[i].Color);
                int glColumn = ScaleColumn(runs[i].X, image.Width);
                int pcxColumn = ScaleColumn(oracle[i].X, pcx.Width);
                Assert.InRange(Math.Abs(glColumn - pcxColumn), 0, 2);
            }
        }
    }

    private static void AssertStripeRuns(List<(int X, string Color)> runs, int width)
    {
        Assert.Equal(ExpectedColors, runs.Select(run => run.Color).ToArray());
        Assert.Equal(ExpectedEdges.Length, runs.Count);
        for (int i = 0; i < ExpectedEdges.Length; i++)
        {
            int column = ScaleColumn(runs[i].X, width);
            Assert.InRange(column, ExpectedEdges[i] - 2, ExpectedEdges[i] + 2);
        }
    }

    private static string Sample(RgbaImage image, int column, int row)
    {
        int x = Math.Clamp((int)Math.Round(column * (image.Width / (double)Width)), 0, image.Width - 1);
        int y = Row(image.Height, row);
        int i = ((y * image.Width) + x) * 4;
        return Classify(image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2]);
    }

    private static List<(int X, string Color)> ColorRuns(byte[] rgba, int width, int height, int row)
    {
        var runs = new List<(int X, string Color)>();
        string? previous = null;
        for (int x = 0; x < width; x++)
        {
            int i = ((row * width) + x) * 4;
            string color = Classify(rgba[i], rgba[i + 1], rgba[i + 2]);
            if (color == "?" || color == previous)
            {
                continue;
            }

            runs.Add((x, color));
            previous = color;
        }

        return runs;
    }

    private static string Classify(byte r, byte g, byte b)
    {
        if (g > 150 && r < 80 && b < 80)
        {
            return "G";
        }

        if (r > 150 && g < 80 && b < 80)
        {
            return "R";
        }

        if (b > 150 && r < 120)
        {
            return "B";
        }

        if (Math.Abs(r - g) < 30 && Math.Abs(g - b) < 30 && r > 80 && r < 180)
        {
            return "F";
        }

        return "?";
    }

    private static int Row(int height, int virtualRow) =>
        Math.Clamp((int)Math.Round(virtualRow * (height / (double)Height)), 0, height - 1);

    private static int ScaleColumn(int column, int width) =>
        (int)Math.Round(column * (Width / (double)width));

    private static bool TryOpenGl(out SdlPlatform? platform, out GlRenderBackend? graphics, out string? error)
    {
        if (TryOpen(out platform, out graphics, out error))
        {
            return true;
        }

        string? first = error;
        string? previousDriver = Environment.GetEnvironmentVariable("SDL_VIDEODRIVER");
        Environment.SetEnvironmentVariable("SDL_VIDEODRIVER", "offscreen");
        try
        {
            if (TryOpen(out platform, out graphics, out error))
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

    private static bool TryOpen(out SdlPlatform? platform, out GlRenderBackend? graphics, out string? error)
    {
        graphics = null;
        if (!SdlPlatform.TryCreate(Width, Height, "OpenVirtue tilt-0", hidden: true, out platform, out error) ||
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

    private static TextureImage Solid(byte r, byte g, byte b) => new(1, 1, [r, g, b, 255]);

    private static string? FindOraclePcx()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OpenVirtue.slnx")))
            {
                string path = Path.Combine(
                    dir.FullName,
                    "_research",
                    "oracle-runs",
                    "OV-20261009-tilt-model-shear-vs-pitch",
                    "S640x480-tilt-0",
                    "work",
                    "OVTLT0.PCX");
                return File.Exists(path) ? path : null;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static Level TiltRoom()
    {
        const string map = """
            VERTEX -16 -12 0;#0
            VERTEX 48 -12 0;#1
            VERTEX 48 -8 0;#2
            VERTEX 48 -4 0;#3
            VERTEX 48 0 0;#4
            VERTEX 48 4 0;#5
            VERTEX 48 8 0;#6
            VERTEX 48 12 0;#7
            VERTEX -16 12 0;#8
            VERTEX 64 -12 0;#9
            VERTEX 64 12 0;#10
            REGION room 0 64;#0
            REGION solid 64 64;#1
            REGION alcove 40 64;#2
            WALL blu_wall 0 1 0 1 0 0;#0
            WALL red_wall 1 2 0 2 0 0;#1
            WALL grn_wall 2 3 0 2 0 0;#2
            WALL red_wall 3 4 0 2 0 0;#3
            WALL grn_wall 4 5 0 2 0 0;#4
            WALL red_wall 5 6 0 2 0 0;#5
            WALL grn_wall 6 7 0 2 0 0;#6
            WALL blu_wall 7 8 0 1 0 0;#7
            WALL mag_wall 8 0 0 1 0 0;#8
            WALL blu_wall 1 9 2 1 0 0;#9
            WALL mag_wall 9 10 2 1 0 0;#10
            WALL blu_wall 10 7 2 1 0 0;#11
            PLAYER_START 0 0 0 0;#12
            """;
        const string main = """
            VIDEO S640x480;
            MAPFILE <m.wmp>;
            BMAP red_map,<red.pcx>,0,0,1,1;
            BMAP grn_map,<grn.pcx>,0,0,1,1;
            BMAP blu_map,<blu.pcx>,0,0,1,1;
            BMAP mag_map,<mag.pcx>,0,0,1,1;
            BMAP flr_map,<flr.pcx>,0,0,1,1;
            BMAP cel_map,<cel.pcx>,0,0,1,1;
            TEXTURE red_tex { SCALE_X 4; SCALE_Y 4; BMAPS red_map; }
            TEXTURE grn_tex { SCALE_X 4; SCALE_Y 4; BMAPS grn_map; }
            TEXTURE blu_tex { SCALE_X 4; SCALE_Y 4; BMAPS blu_map; }
            TEXTURE mag_tex { SCALE_X 4; SCALE_Y 4; BMAPS mag_map; }
            TEXTURE flr_tex { SCALE_X 4; SCALE_Y 4; BMAPS flr_map; }
            TEXTURE cel_tex { SCALE_X 4; SCALE_Y 4; BMAPS cel_map; }
            WALL red_wall { TEXTURE red_tex; }
            WALL grn_wall { TEXTURE grn_tex; }
            WALL blu_wall { TEXTURE blu_tex; }
            WALL mag_wall { TEXTURE mag_tex; }
            REGION room { FLOOR_HGT 0; CEIL_HGT 64; FLOOR_TEX flr_tex; CEIL_TEX cel_tex; }
            REGION solid { FLOOR_HGT 64; CEIL_HGT 64; FLOOR_TEX flr_tex; CEIL_TEX cel_tex; }
            REGION alcove { FLOOR_HGT 40; CEIL_HGT 64; FLOOR_TEX flr_tex; CEIL_TEX cel_tex; }
            ACTION probe_start { SET PLAYER_ARC, 1; SET PLAYER_TILT, 0; SET PLAYER_SIZE, 8; SET PLAYER_Z, 8; }
            IF_START probe_start;
            """;
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        return LevelLoader.LoadCore("tilt0", main, name => resources.GetValueOrDefault(Path.GetFileName(name)));
    }
}
