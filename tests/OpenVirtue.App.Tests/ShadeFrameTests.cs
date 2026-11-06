// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.Text;
using OpenVirtue.App.Platform;
using OpenVirtue.App.Rendering;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Formats.Wrs;
using OpenVirtue.Host;
using Xunit.Abstractions;

namespace OpenVirtue.App.Tests;

/// <summary>
/// Draws the shade-global probe's wall and floor scenes through the real GL host and reads the shade
/// levels back off the frame (OV-20261105-shade-global): a wall steps its zones across the screen at
/// a slope rounded up to a 1024th from its first visible column, so the level boundaries fall on the
/// columns the original's screenshots show. The palette's gray ramp sits at indices 16…47, declared
/// one <c>RANGE</c>, and the wall and floor are its brightest colour, so a texel's palette index is 16
/// plus its shade level.
/// </summary>
[Collection(GlTestGroup.Name)]
public sealed class ShadeFrameTests : IDisposable
{
    private const int Width = 640;
    private const int Height = 480;
    private const int RampStart = 16;
    private const int RampLength = 32;

    /// <summary><c>PLAYER_LIGHT</c> 0.199219 is 204/1024, which the WDL literal holds exactly.</summary>
    private const double RetailLight = 204 / 1024.0;

    private readonly ITestOutputHelper _output;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ov-shade-frame-" + Guid.NewGuid().ToString("N"));

    public ShadeFrameTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Shade_ObliqueWalls_StepTheirZonesWhereTheOriginalDoes()
    {
        // OV-20261105-shade-global, round 2 block C and round 3: a free wall stands on the eye line, the
        // eye at the origin eight steps up facing +x. The columns are where the level changes, as the
        // original's screenshots read them, and the rule's own rounded slope must land within 3.
        // The bounds are 3 columns, the width the measurements themselves support, but for one wall:
        // the mirrored front wall at retail's light has the smallest step of any (1/1024 a column), so
        // the zones fit's few tenths of a percent become 4 columns there (ours 408.5, the original's
        // 404.5). Every other row holds to 3: see the report.
        (string Name, int X1, int Y1, int X2, int Y2, double Light, int LightDistance, double[] Columns, double Bound)[] cases =
        [
            ("w-a", 30, -10, 40, 10, 1, 10, [227.5, 483.5], 3),
            ("w-a-m", 40, -10, 30, 10, 1, 10, [138.5], 3),
            ("w-c", 20, -6, 44, 6, 1, 10, [272.5, 332.5, 392.5, 453.5], 3),
            ("w-c-m", 44, -6, 20, 6, 1, 10, [179.5, 243.5, 307.5, 371.5], 3),
            ("w-f", 24, -20, 48, 20, 1, 10, [248.5, 453.5], 3),
            ("w-f-m", 48, -20, 24, 20, 1, 10, [147.5, 403.5], 3),
            ("w-r", 24, -20, 44, -4, 1, 10, [454.5, 600.5], 3),
            ("w-l", 24, 20, 44, 4, 1, 10, [31.5, 202.5], 3),
            ("wr-a", 30, -10, 40, 10, RetailLight, 20, [363.5], 3),
            ("wr-a-m", 40, -10, 30, 10, RetailLight, 20, [294.5], 3),
            ("wr-f", 24, -20, 48, 20, RetailLight, 20, [405.5], 3),
            ("wr-f-m", 48, -20, 24, 20, RetailLight, 20, [404.5], 4),
        ];

        if (!GlContext.TryOpen(Width, Height, "OpenVirtue shade", out SdlPlatform? platform, out GlRenderBackend? graphics, out string? error) ||
            platform is null ||
            graphics is null)
        {
            _output.WriteLine($"SKIP: no GL context ({error})");
            return;
        }

        using (platform)
        using (graphics)
        {
            foreach ((string name, int x1, int y1, int x2, int y2, double light, int lightDistance, double[] expected, double bound) in cases)
            {
                RgbaImage image = Render(
                    platform,
                    graphics,
                    Room + FormattableString.Invariant($"\nVERTEX {x1} {y1} 0;#4\nVERTEX {x2} {y2} 0;#5\nWALL free_wall 4 5 0 0 0 0;#4\nPLAYER_START 0 0 0 0;#5"),
                    light,
                    lightDistance);

                List<double> actual = WallTransitions(image, x1, y1, x2, y2);
                _output.WriteLine($"{name}: transitions {string.Join(", ", actual.Select(static c => c.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))} expected {string.Join(", ", expected.Select(static c => c.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))}");
                Assert.Equal(expected.Length, actual.Count);
                for (int i = 0; i < expected.Length; i++)
                {
                    Assert.InRange(actual[i], expected[i] - bound, expected[i] + bound);
                }
            }
        }
    }

    [Fact]
    public void Shade_FrontalWalls_HoldTheMeasuredLevel()
    {
        // Frontal walls: the zones are Qw/d everywhere on the face (the step is 0), so the level holds
        // across the whole wall: p 1, l 10 at 25, 25.5, 30, 30.5, 37.5 and 38 steps, and retail's light
        // at 32, 33, 60 and 61.
        (double Depth, double Light, int LightDistance, int Level)[] cases =
        [
            (25, 1, 10, 16),
            (25.5, 1, 10, 18),
            (30, 1, 10, 18),
            (30.5, 1, 10, 20),
            (37.5, 1, 10, 20),
            (38, 1, 10, 22),
            (32, RetailLight, 20, 24),
            (33, RetailLight, 20, 26),
            (60, RetailLight, 20, 26),
            (61, RetailLight, 20, 26),
        ];

        if (!GlContext.TryOpen(Width, Height, "OpenVirtue shade frontal", out SdlPlatform? platform, out GlRenderBackend? graphics, out string? error) ||
            platform is null ||
            graphics is null)
        {
            _output.WriteLine($"SKIP: no GL context ({error})");
            return;
        }

        using (platform)
        using (graphics)
        {
            foreach ((double depth, double light, int lightDistance, int level) in cases)
            {
                // An integer-x wall with the eye moved back by the fraction: the depth is exact. The
                // wall stays inside the room (the far wall stands at 48), so a depth of 60 or 61 is
                // reached by moving the eye back past the room's near wall instead, never by standing
                // the wall beyond the far one. At retail's light both that wall and the room's own
                // (62 and 63 steps off) read 26, so these two carriers pin the level at the depth the
                // measured ones name rather than telling the surfaces apart.
                int wall = Math.Min((int)Math.Floor(depth), 46);
                double eye = wall - depth;
                RgbaImage image = Render(
                    platform,
                    graphics,
                    Room + FormattableString.Invariant($"\nVERTEX {wall} -6 0;#4\nVERTEX {wall} 6 0;#5\nWALL free_wall 4 5 0 0 0 0;#4\nPLAYER_START 0 0 0 0;#5"),
                    light,
                    lightDistance,
                    eye);

                int row = ScaleRow(image, 240);
                var levels = new HashSet<int>();
                for (int x = ScaleColumn(image, 250); x <= ScaleColumn(image, 390); x++)
                {
                    levels.Add(LevelAt(image, x, row));
                }

                _output.WriteLine(FormattableString.Invariant($"frontal d {depth}: levels {string.Join(",", levels)} expected {level}"));
                Assert.Equal([level], levels);
            }
        }
    }

    [Fact]
    public void Shade_Floor_Steps26To24AtTheMeasuredDepth()
    {
        // The room's floor down column 320 with retail's light and no free wall: level 26 on d 32.03…64.07
        // and 24 nearer than 32.03, so 26 at 36 steps and 24 at 28 (rows from the measured projection,
        // d = 8·659/(row + 0.5 − 240)).
        if (!GlContext.TryOpen(Width, Height, "OpenVirtue shade floor", out SdlPlatform? platform, out GlRenderBackend? graphics, out string? error) ||
            platform is null ||
            graphics is null)
        {
            _output.WriteLine($"SKIP: no GL context ({error})");
            return;
        }

        using (platform)
        using (graphics)
        {
            RgbaImage image = Render(platform, graphics, Room + "\nPLAYER_START 0 0 0 0;#4", RetailLight, 20);

            foreach ((double depth, int expected) in new (double, int)[] { (36, 26), (28, 24) })
            {
                int row = FloorRow(image, depth);
                int level = LevelAt(image, ScaleColumn(image, 320), row);
                _output.WriteLine(FormattableString.Invariant($"floor d {depth}: row {row} level {level}"));
                Assert.Equal(expected, level);
            }
        }
    }

    [Fact]
    public void Shade_WallEndOnTheEyePlane_StaysOnTheRamp()
    {
        // OV-20261105-shade-global: a wall's end can sit exactly on the eye's plane, where clip w is 0
        // and both its column and 1/w blow up. The slope of 1/w per column must still be finite, or
        // the zones become NaN and the shader's int(clamp(NaN, …)) leaves an undefined index. Here a
        // wall runs from (0, 20) — its v1 on the plane — to (30, 4), so its visible columns are
        // 0…241 at depths 18.7…29.5, and palette entry 0 is magenta (the clear colour).
        if (!GlContext.TryOpen(Width, Height, "OpenVirtue shade eye plane", out SdlPlatform? platform, out GlRenderBackend? graphics, out string? error) ||
            platform is null ||
            graphics is null)
        {
            _output.WriteLine($"SKIP: no GL context ({error})");
            return;
        }

        using (platform)
        using (graphics)
        {
            RgbaImage image = Render(
                platform,
                graphics,
                Room + "\nVERTEX 0 20 0;#4\nVERTEX 30 4 0;#5\nWALL free_wall 4 5 0 0 0 0;#4\nPLAYER_START 0 0 0 0;#5",
                1,
                10);

            int row = ScaleRow(image, 240);
            var levels = new List<int>();
            for (int x = ScaleColumn(image, 2); x <= ScaleColumn(image, 238); x++)
            {
                levels.Add(LevelAt(image, x, row));
            }

            _output.WriteLine($"eye-plane wall: columns 2..238 read levels {string.Join(",", levels.Distinct().Order())}");
            Assert.InRange(LevelAt(image, ScaleColumn(image, 5), row), 10, 14);
            Assert.InRange(LevelAt(image, ScaleColumn(image, 235), row), 16, 20);
            for (int i = 1; i < levels.Count; i++)
            {
                Assert.InRange(Math.Abs(levels[i] - levels[i - 1]), 0, 2);
            }

            // The rest of the eye line is the room's far wall: still ramp colours throughout.
            for (int x = ScaleColumn(image, 250); x <= ScaleColumn(image, 630); x++)
            {
                Assert.True(LevelAt(image, x, row) <= PaletteShading.DarkestLevel);
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>The room the wall cases stand in: the eye at the origin facing +x, the far wall 48 ahead.</summary>
    private const string Room = """
        VERTEX -20 -30 0;#0
        VERTEX 48 -30 0;#1
        VERTEX 48 30 0;#2
        VERTEX -20 30 0;#3
        REGION room 0 16;#0
        WALL room_wall 0 1 0 -1 0 0;#0
        WALL room_wall 1 2 0 -1 0 0;#1
        WALL room_wall 2 3 0 -1 0 0;#2
        WALL room_wall 3 0 0 -1 0 0;#3
        """;

    private RgbaImage Render(
        SdlPlatform platform,
        GlRenderBackend graphics,
        string map,
        double playerLight,
        int lightDistance,
        double eyeX = 0)
    {
        Directory.CreateDirectory(_directory);
        string archive = Path.Combine(_directory, "SHADE.WRS");
        byte[] palette = RampPalette();
        WrsArchive.WriteFile(
            archive,
            [
                new WrsFile("TITLE.WDL", Encoding.ASCII.GetBytes(Declarations(playerLight, lightDistance, eyeX))),
                new WrsFile("m.wmp", Encoding.ASCII.GetBytes(map)),
                new WrsFile("LEVEL.PCX", Pcx(palette, 16)),
                new WrsFile("WALL.PCX", Pcx(palette, 16)),
            ]);
        using RuntimeHost host = RuntimeHost.OpenArchive(archive, "TITLE.WDL", platform, platform, new NullAudioSink(), graphics);
        host.Boot();
        platform.PumpEvents();
        host.Frame(present: false);
        return graphics.ReadRgba();
    }

    private static string Declarations(double playerLight, int lightDistance, double eyeX) => FormattableString.Invariant($$"""
        VIDEO S640x480;
        MAPFILE <m.wmp>;
        PALETTE shade_pal { PALFILE <LEVEL.PCX>; RANGE {{RampStart}}, {{RampLength}}; }
        BMAP wall_map,<WALL.PCX>,0,0,64,64;
        TEXTURE wall_tex { SCALE_X 4; SCALE_Y 4; BMAPS wall_map; }
        WALL room_wall { TEXTURE wall_tex; }
        WALL free_wall { TEXTURE wall_tex; }
        REGION room { FLOOR_HGT 0; CEIL_HGT 16; FLOOR_TEX wall_tex; CEIL_TEX wall_tex; AMBIENT 0; }
        ACTION shade_start
        {
            SET PLAYER_ARC, 1;
            SET PLAYER_TILT, 0;
            SET PLAYER_SIZE, 8;
            SET PLAYER_Z, 8;
            SET AMBIENT, 0;
            SET PLAYER_LIGHT, {{playerLight}};
            SET LIGHT_DIST, {{lightDistance}};
            SET PLAYER_X, {{eyeX}};
        }
        IF_START shade_start;
        """);

    /// <summary>
    /// The probe's palette: a gray ramp 248, 240, … 8 at indices 16…47, everything else black except
    /// entry 0, which is magenta — the clear colour — so a level the shader left undefined, or a
    /// pixel no geometry reached, cannot pass for a shade level.
    /// </summary>
    private static byte[] RampPalette()
    {
        byte[] rgb = new byte[256 * 3];
        rgb[0] = 255;
        rgb[2] = 255;
        for (int i = 0; i < RampLength; i++)
        {
            byte level = (byte)(248 - (i * 8));
            rgb[(RampStart + i) * 3] = level;
            rgb[((RampStart + i) * 3) + 1] = level;
            rgb[((RampStart + i) * 3) + 2] = level;
        }

        return rgb;
    }

    /// <summary>A 1×1 PCX of one palette index carrying the given palette, as a PALFILE and a texture.</summary>
    private static byte[] Pcx(byte[] palette, byte index)
    {
        byte[] buffer = new byte[128 + 1 + 1 + (256 * 3)];
        buffer[0] = 0x0A;
        buffer[1] = 5;
        buffer[2] = 1;
        buffer[3] = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10), 0);
        buffer[65] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(66), 1);
        buffer[128] = index;
        buffer[129] = 0x0C;
        palette.CopyTo(buffer, 130);
        return buffer;
    }

    /// <summary>
    /// The columns where row 240's level changes inside the wall's own span (three columns clear of its
    /// projected ends), each the boundary between the last column of one level and the first of the next.
    /// </summary>
    private static List<double> WallTransitions(RgbaImage image, int x1, int y1, int x2, int y2)
    {
        double focal = 320 / Math.Tan(0.5);
        double a = 320 - (focal * y1 / x1);
        double b = 320 - (focal * y2 / x2);
        int from = Math.Max(0, (int)Math.Round(Math.Min(a, b)) + 3);
        int to = Math.Min(Width - 1, (int)Math.Round(Math.Max(a, b)) - 3);
        int row = ScaleRow(image, 240);
        var transitions = new List<double>();
        int previous = LevelAt(image, ScaleColumn(image, from), row);
        for (int x = from + 1; x <= to; x++)
        {
            int level = LevelAt(image, ScaleColumn(image, x), row);
            if (level != previous)
            {
                transitions.Add(x - 0.5);
                previous = level;
            }
        }

        return transitions;
    }

    /// <summary>
    /// The shade level of a pixel: the ramp's colour for level 0…30, as the screen shows it through the
    /// 6-bit DAC (OV-20261104-retail-frames), or <see cref="PaletteShading.InfinityLevel"/> for the
    /// INFINITY colour, palette entry 1, black.
    /// </summary>
    private static int LevelAt(RgbaImage image, int x, int y)
    {
        int i = ((Math.Clamp(y, 0, image.Height - 1) * image.Width) + Math.Clamp(x, 0, image.Width - 1)) * 4;
        byte r = image.Pixels[i];
        byte g = image.Pixels[i + 1];
        byte b = image.Pixels[i + 2];
        Assert.Equal(r, g);
        Assert.Equal(r, b);
        if (r == 0)
        {
            return PaletteShading.InfinityLevel;
        }

        for (int level = 0; level < RampLength; level++)
        {
            if (Widen((byte)(248 - (level * 8))) == r)
            {
                return level;
            }
        }

        Assert.Fail($"pixel ({x},{y}) is {r}, not on the shade ramp");
        return -1;
    }

    /// <summary>The 6-bit DAC's widening of a palette channel, as the palette upload shows it.</summary>
    private static byte Widen(byte value)
    {
        double faded = Math.Clamp(value / 255.0, 0, 1);
        int code = (int)Math.Floor((faded * 63.75) + 0.0001);
        return (byte)((code * 4) + (code / 16));
    }

    /// <summary>The row a floor depth projects to: the eye 8 steps up, the vertical focal 659 px.</summary>
    private static int FloorRow(RgbaImage image, double depth) =>
        ScaleRow(image, (int)Math.Round((8 * 659 / depth) + 240 - 0.5));

    private static int ScaleRow(RgbaImage image, int virtualRow) =>
        Math.Clamp((int)Math.Round(virtualRow * (image.Height / (double)Height)), 0, image.Height - 1);

    private static int ScaleColumn(RgbaImage image, int virtualColumn) =>
        Math.Clamp((int)Math.Round(virtualColumn * (image.Width / (double)Width)), 0, image.Width - 1);
}
