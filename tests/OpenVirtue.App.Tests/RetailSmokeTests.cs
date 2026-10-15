// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;
using OpenVirtue.App.Platform;
using OpenVirtue.App.Rendering;
using OpenVirtue.Engine;
using OpenVirtue.Formats.Wrs;
using OpenVirtue.Host;
using Xunit.Abstractions;

namespace OpenVirtue.App.Tests;

/// <summary>
/// The retail smoke on the GL host: the title FLC, splash, menu and keyboard handoff from
/// <c>TITLE.WRS</c> into Apathy in one host, and one drawn frame from each retail archive.
/// Runs only when the user's own archives are under <c>_research/</c> and a GL context opens;
/// otherwise it writes a SKIP line and returns. Frames go to <c>ov-retail-smoke/</c> in the temp
/// folder for a visual check.
/// </summary>
[Collection(GlTestGroup.Name)]
public sealed partial class RetailSmokeTests
{
    private const int Width = 640;
    private const int Height = 480;
    private const int FrameBudget = 1800;

    private static readonly string[] RetailArchives = ["TITLE", "START", "APATHY", "HEART", "LEGALISM", "NEWAGE"];

    private static readonly EnumerationOptions AnyCaseRecursive = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = true,
    };

    private readonly ITestOutputHelper _output;

    public RetailSmokeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Title_KeyboardThroughMenu_HandsOffToApathyAtItsDeclaredStart()
    {
        if (!TryPrepare(out string? gameDir, out SdlPlatform? platform, out GlRenderBackend? graphics) ||
            platform is null ||
            graphics is null)
        {
            return;
        }

        using (platform)
        using (graphics)
        {
            var input = new ScriptedInput();
            using RuntimeHost host = RuntimeHost.OpenArchive(
                Path.Combine(gameDir, "TITLE.WRS"),
                "TITLE.WDL",
                platform,
                input,
                new NullAudioSink(),
                graphics);
            host.Boot();
            var session = new Session(host, platform, graphics, input, _output);

            session.RunUntil("the logo FLC plays", () => host.StatusText.Contains("flic logo.flc", StringComparison.OrdinalIgnoreCase));
            session.Run(30);
            session.Capture("title-1-logo-flc");

            // PLAY_FLICFILE holds showLogoScreen until the flic ends, so IF_ANYKEY is armed only then.
            session.RunUntil("the logo FLC has finished", () => Hook(host, "IF_ANYKEY") == "advanceToTitle");
            session.Press(HostKey.Space);
            session.RunUntil(
                "the title splash is up",
                () => Hook(host, "IF_ANYKEY") == "advanceToMenu" && UiNumber(host, "titleScreenPanel", "VISIBLE") == 1);
            session.Run(60);
            session.Capture("title-2-splash");

            session.Press(HostKey.Space);
            session.RunUntil("the title menu is up", () => host.Runtime.GetSkill("inTitle") == 1);
            session.Run(60);
            session.Capture("title-3-menu");
            Assert.Equal(1, host.Runtime.GetSkill("menu_pos"));
            Assert.Equal(UiNumber(host, "menu1_txt", "POS_Y"), UiNumber(host, "arrowOvly", "POS_Y"));

            // New Game opens the difficulty menu (menu_exec BRANCHes to diff_menu), and Enter there
            // starts at the preselected difficulty (keepArrowPos 4). While BRANCH returns like CALL, menu_exec runs on
            // with the new menu_pos and the first Enter already starts the game; the log says which.
            session.Press(HostKey.Enter);
            session.RunUntil(
                "the difficulty menu is up or Apathy is loaded",
                () => host.Runtime.GetSkill("menu_pos") == 4 || WorldName(host) == "APATHY");
            if (WorldName(host) != "APATHY")
            {
                session.Run(30);
                session.Capture("title-4-difficulty");
                session.Press(HostKey.Enter);
            }
            else
            {
                _output.WriteLine("New Game skipped the difficulty menu and loaded Apathy directly.");
            }

            session.RunUntil("Apathy is loaded", () => WorldName(host) == "APATHY");
            session.Run(180);
            session.Capture("apathy-start");
            _output.WriteLine($"difficulty {host.Runtime.GetSkill("gameDifficulty")}");

            WrsArchive archive = WrsArchive.ReadFile(Path.Combine(gameDir, "APATHY.WRS"));
            Level apathy = LevelLoader.Load(archive, "APATHY.WDL");
            Assert.NotNull(apathy.PlayerStart);
            int declared = apathy.PlayerStart.Value.Region;
            Match region = StatusRegion().Match(host.StatusText);
            Assert.True(region.Success, host.StatusText);
            _output.WriteLine($"declared start region {declared} {apathy.Regions[declared].Name}, player in {region.Value}");
            Assert.Equal(declared, int.Parse(region.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Contains("IF_START ran", host.StatusText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RetailArchives_OpenDirectly_DrawAFrame()
    {
        if (!TryPrepare(out string? gameDir, out SdlPlatform? platform, out GlRenderBackend? graphics) ||
            platform is null ||
            graphics is null)
        {
            return;
        }

        using (platform)
        using (graphics)
        {
            foreach (string name in RetailArchives)
            {
                var input = new ScriptedInput();
                using RuntimeHost host = RuntimeHost.OpenArchive(
                    Path.Combine(gameDir, name + ".WRS"),
                    name + ".WDL",
                    platform,
                    input,
                    new NullAudioSink(),
                    graphics);
                host.Boot();
                var session = new Session(host, platform, graphics, input, _output);
                session.Run(120);
                Assert.False(host.ExitRequested, $"{name} asked to exit: {host.StatusText}");
                Assert.Contains("IF_START ran", host.StatusText, StringComparison.Ordinal);
                session.Capture($"archive-{name.ToLowerInvariant()}");
            }
        }
    }

    private bool TryPrepare(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? gameDir,
        out SdlPlatform? platform,
        out GlRenderBackend? graphics)
    {
        platform = null;
        graphics = null;
        gameDir = LinkRetailArchives();
        if (gameDir is null)
        {
            _output.WriteLine("SKIP: the six retail archives are not under _research/.");
            return false;
        }

        if (!GlContext.TryOpen(Width, Height, "OpenVirtue retail smoke", out platform, out graphics, out string? error))
        {
            _output.WriteLine($"SKIP: no GL context ({error})");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Links (or copies) the archives into a fresh temp folder, so the saves and the INFO file
    /// the title writes land there and not beside the user's game data.
    /// </summary>
    private static string? LinkRetailArchives()
    {
        string? research = ResearchDirectory();
        if (research is null)
        {
            return null;
        }

        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(research, "*.wrs", AnyCaseRecursive))
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            if (RetailArchives.Contains(stem, StringComparer.OrdinalIgnoreCase) &&
                Path.GetExtension(path).Equals(".wrs", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("oracle-runs", StringComparison.OrdinalIgnoreCase))
            {
                found.TryAdd(stem, path);
            }
        }

        if (found.Count != RetailArchives.Length)
        {
            return null;
        }

        string dir = Path.Combine(Path.GetTempPath(), "ov-retail-smoke", "game");
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        Directory.CreateDirectory(dir);
        foreach ((string stem, string source) in found)
        {
            // The title asks for "apathy.wrs" by name; lower-case links resolve on any file system.
            string target = Path.Combine(dir, stem.ToLowerInvariant() + ".wrs");
            try
            {
                File.CreateSymbolicLink(target, source);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                File.Copy(source, target);
            }

            if (!File.Exists(Path.Combine(dir, stem.ToUpperInvariant() + ".WRS")))
            {
                File.CreateSymbolicLink(Path.Combine(dir, stem.ToUpperInvariant() + ".WRS"), target);
            }
        }

        return dir;
    }

    private static string? ResearchDirectory()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OpenVirtue.slnx")))
            {
                string research = Path.Combine(dir.FullName, "_research");
                return Directory.Exists(research) ? research : null;
            }
        }

        return null;
    }

    private static string? Hook(RuntimeHost host, string name) =>
        host.Runtime.ActionHooks.GetValueOrDefault(name);

    private static double UiNumber(RuntimeHost host, string element, string member) =>
        host.Runtime.TryGetUiNumber(element, member, out double value) ? value : double.NaN;

    private static string WorldName(RuntimeHost host)
    {
        Match match = StatusWorld().Match(host.StatusText);
        return match.Success ? Path.GetFileNameWithoutExtension(match.Groups[1].Value).ToUpperInvariant() : "";
    }

    [GeneratedRegex(@"^world (\S+)")]
    private static partial Regex StatusWorld();

    [GeneratedRegex(@"region (-?\d+) (\S*)")]
    private static partial Regex StatusRegion();

    /// <summary>Steps the host at roughly 60 Hz of wall time, which the FLC and TIME_CORR read.</summary>
    private sealed class Session(
        RuntimeHost host,
        SdlPlatform platform,
        GlRenderBackend graphics,
        ScriptedInput input,
        ITestOutputHelper output)
    {
        public int Frames { get; private set; }

        public void Press(HostKey key)
        {
            output.WriteLine($"[{Frames}] press {key}");
            input.Queue(key);
        }

        public void Run(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                Step();
            }
        }

        public void RunUntil(string what, Func<bool> done, int minimum = 0)
        {
            int start = Frames;
            while (Frames - start < minimum || !done())
            {
                Assert.True(Frames - start < FrameBudget, $"Gave up waiting until {what}: {host.StatusText}");
                Assert.False(host.ExitRequested, $"The host exited while waiting until {what}: {host.StatusText}");
                Step();
            }

            output.WriteLine($"[{Frames}] {what}");
        }

        public void Capture(string name)
        {
            RgbaImage image = graphics.ReadRgba();
            string dir = Path.Combine(Path.GetTempPath(), "ov-retail-smoke");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name + ".png");
            File.WriteAllBytes(path, PngWriter.Encode(image.Width, image.Height, image.Pixels));
            int colors = DistinctColors(image);
            output.WriteLine($"[{Frames}] {name}: {colors} colours -> {path}");
            output.WriteLine($"    {host.StatusText}");
            Assert.True(colors >= 16, $"{name} drew only {colors} colours: {host.StatusText}");
        }

        private void Step()
        {
            long started = Stopwatch.GetTimestamp();
            platform.PumpEvents();
            input.BeginFrame();
            host.Frame(present: false);
            Frames++;

            TimeSpan spent = Stopwatch.GetElapsedTime(started);
            TimeSpan wait = TimeSpan.FromMilliseconds(16) - spent;
            if (wait > TimeSpan.Zero)
            {
                Thread.Sleep(wait);
            }
        }

        private static int DistinctColors(RgbaImage image)
        {
            var colors = new HashSet<int>();
            for (int i = 0; i < image.Pixels.Length; i += 4 * 7)
            {
                colors.Add(image.Pixels[i] | (image.Pixels[i + 1] << 8) | (image.Pixels[i + 2] << 16));
            }

            return colors.Count;
        }
    }

    /// <summary>Keyboard edges fed one frame at a time; the pointer stays still.</summary>
    private sealed class ScriptedInput : IInputSource
    {
        private readonly Queue<HostKey> _queued = new();
        private readonly List<HostKey> _pressed = [];

        public IReadOnlyCollection<HostKey> Pressed => _pressed;

        public Vector2 PointerPosition => new(Width / 2f, Height / 2f);

        public void Queue(HostKey key) => _queued.Enqueue(key);

        /// <summary>Releases last frame's key and presses the next queued one, if any.</summary>
        public void BeginFrame()
        {
            _pressed.Clear();
            if (_queued.TryDequeue(out HostKey key))
            {
                _pressed.Add(key);
            }
        }

        public bool IsDown(HostKey key) => _pressed.Contains(key);

        public bool IsDown(MouseButton button) => false;

        public bool WasPressed(MouseButton button) => false;

        public bool WasReleased(MouseButton button) => false;

        public Vector2 ConsumeMotion() => Vector2.Zero;
    }
}
