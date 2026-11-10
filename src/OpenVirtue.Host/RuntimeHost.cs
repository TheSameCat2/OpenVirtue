// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using OpenVirtue.Engine;
using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Formats.Pcx;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Host;

/// <summary>
/// The platform-neutral frame loop: WDL runtime, player, input skills, and the draw list.
/// Window, audio, and GL stay behind <see cref="IPlatformWindow"/>, <see cref="IAudioSink"/>,
/// and <see cref="IRenderBackend"/>.
/// </summary>
public sealed partial class RuntimeHost : IDisposable
{
    private static readonly Vector4 DefaultClearColor = new(0.08f, 0.10f, 0.14f, 1f);
    private const double FullTurnRadians = Math.PI * 2.0;

    private readonly IPlatformWindow _window;
    private readonly IInputSource _input;
    private readonly IAudioSink _audio;
    private readonly IRenderBackend _graphics;
    private readonly string _contentRoot;
    private readonly Camera _camera = new();
    private readonly Stopwatch _frameClock = new();
    private readonly List<CachedBatch> _batches = [];
    private readonly List<RenderVertex> _clipFill = [];
    private readonly Dictionary<(string Element, int Place), (int Signature, int Frames)> _uiDirty = [];
    private readonly Dictionary<string, int> _uiPieceIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(string Element, int Place)> _uiSeen = [];
    private RenderVertex[] _occluders = [];
    private RenderVertex[] _spriteFrames = [];
    private IReadOnlySet<string>? _blankSurfaces;
    private ViewClip? _viewClip;
    private (int Width, int Height) _screenSize;

    /// <summary>The frames a panel without <c>REFRESH</c> redraws a changed piece on (OV-20261106-screen-frame).</summary>
    private const int PanelRedrawFrames = 2;
    private readonly List<SpriteBillboard> _sprites = [];
    private readonly HashSet<int> _hostedChannels = [];
    private readonly Dictionary<AcknexObject, SpatialLoopSound> _spatialLoopSounds = [];
    private readonly List<WdlScreenshotRequest> _pendingScreenshots = [];

    private WrsArchive? _archive;
    private string? _archivePath;
    private string _mainWdlName = "";
    private string? _mapOverrideName;
    private Level _level = null!;
    private Player _player = null!;
    private WdlRuntime _runtime = null!;
    private int? _randomSeed;
    private IReadOnlyDictionary<string, TextureImage> _textureImages =
        new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, TextureImage> _uiImages =
        new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, TextureImage> _fontImages =
        new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase);

    private Vector4 _clearColor = DefaultClearColor;

    // OV-20261104-fade-pal: the declared palettes in declaration order, the current one (the last
    // declared at the start, then the last FADE_PAL's with a factor of 1 or above), the blend's target
    // and factor, the shade table of the first ranged palette, and the fade as RGB batches draw it.
    private readonly List<(string Name, byte[] Rgb)> _palettes = [];
    private byte[]? _currentPalette;
    private byte[]? _fadeTarget;
    private double _fadeFactor;
    private byte[]? _shadeTable;
    private PaletteFadeTint _fade = PaletteFadeTint.None;
    private string _baseTitle = "OpenVirtue";
    private bool _booted;
    private bool _geometryReady;
    private bool _startupRan;
    private bool _handlingInputRequest;
    private InkeyEditor? _inkey;
    private WdlInputRequest? _inkeyRequest;
    private bool _exitRequested;
    private bool _shading;
    private double _lastFrameSeconds;
    private double _lastTimeCorrection;
    private double _statusAccum;
    private int _handledGeometryInvalidations;
    private int _handledAudioRequests;
    private int _handledTextureSoundRequests;
    private int _handledPaletteFadeRequests;
    private int _handledBeepRequests;
    private int _handledFlicRequests;
    private int _handledStopFlicRequests;
    private int _handledScreenshotRequests;
    private int _handledPersistenceRequests;
    private string? _lastPresentationStatus;
    private string? _lastPersistenceStatus;
    private FlicPlayback? _activeFlic;
    private bool _hasPlayerRuntimeSnapshot;
    private double _lastPlayerX;
    private double _lastPlayerY;
    private double _lastPlayerZ;
    private double _lastPlayerRegion;
    private double _lastPlayerAngle;
    private double _lastPlayerTilt;
    private int _textureSoundRegion = -1;
    private int _ceilingSoundChannel = -1;
    private Vector2 _lastTextureSoundPosition;
    private double _walkSoundDistance;
    private long _lastWaveSoundCycle;
    private Vector2 _pendingMickey;
    private Vector2 _frameMickey;
    private Vector2 _mouseMotionWindow;
    private double _mouseMotionTicks;
    private bool _mouseWasMoving;
    private UiButtonIdentity? _pressedUiButton;
    private string? _pressedUiButtonReleaseAction;
    private UiSliderTarget? _draggedUiSlider;
    private float _sliderGrabOffset;

    private RuntimeHost(
        IPlatformWindow window,
        IInputSource input,
        IAudioSink audio,
        IRenderBackend graphics,
        string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(graphics);
        _window = window;
        _input = input;
        _audio = audio;
        _graphics = graphics;
        _contentRoot = contentRoot;
    }

    /// <summary>
    /// When set, supplies each frame's duration in seconds in place of the wall clock, so a run is
    /// reproducible: the oracle replay feeds it the original's per-cycle tick deltas.
    /// </summary>
    public Func<double>? FrameClock { get; set; }

    /// <summary>
    /// The seed <c>RANDOM</c>'s generator started from (<see cref="OpenArchive"/>'s <c>randomSeed</c>),
    /// kept across level changes so a run is reproducible (the oracle replay); <c>null</c> when every
    /// run draws differently.
    /// </summary>
    public int? RandomSeed => _randomSeed;

    /// <summary>
    /// Whether a fatal script error (a division by zero) halts the runtime and drops the unflushed
    /// print text, as the original's shutdown does (<see cref="WdlRuntime.FatalErrors"/>), on every
    /// level. Off by default; the oracle replay turns it on.
    /// </summary>
    public bool FatalErrors
    {
        get => _fatalErrors;
        set
        {
            _fatalErrors = value;
            if (_runtime is not null)
            {
                _runtime.FatalErrors = value;
            }
        }
    }

    /// <summary>
    /// Whether a full <c>SAVE</c> also writes the original's own save file, which the DOS runtime loads
    /// (OV-20261029-save-body round 9). Off by default until it has been checked against more levels;
    /// OpenVirtue's own saves are written either way.
    /// </summary>
    public bool WriteOriginalSaves
    {
        get => _writeOriginalSaves;
        set
        {
            _writeOriginalSaves = value;
            if (_runtime is not null)
            {
                _runtime.OriginalSaveNames = OriginalSaveNames();
            }
        }
    }

    private bool _writeOriginalSaves;
    private bool _fatalErrors;

    private (string, string, string)? OriginalSaveNames() =>
        _writeOriginalSaves ? (_mainWdlName, _level.MapFileName, Path.GetFileName(_archivePath ?? "TITLE.WRS")) : null;

    /// <summary>The eye. Tests read it after <see cref="Boot"/>.</summary>
    public Camera Camera => _camera;

    /// <summary>The live WDL runtime.</summary>
    public WdlRuntime Runtime => _runtime;

    /// <summary>The status line written into the window title.</summary>
    public string StatusText { get; private set; } = "";

    /// <summary>True after the runtime or the window has asked to stop.</summary>
    public bool ExitRequested => _exitRequested || !_window.IsOpen;

    /// <summary>
    /// Opens a WRS archive the way the interactive app does. A <paramref name="randomSeed"/> makes
    /// <c>RANDOM</c> draw the same values on every run.
    /// </summary>
    public static RuntimeHost OpenArchive(
        string archivePath,
        string mainWdlName,
        IPlatformWindow window,
        IInputSource input,
        IAudioSink audio,
        IRenderBackend graphics,
        int? randomSeed = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(archivePath);
        ArgumentException.ThrowIfNullOrEmpty(mainWdlName);
        string fullPath = Path.GetFullPath(archivePath);
        string contentRoot = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        var host = new RuntimeHost(window, input, audio, graphics, contentRoot)
        {
            _archivePath = fullPath,
            _archive = WrsArchive.ReadFile(fullPath),
            _mainWdlName = Path.GetFileName(mainWdlName),
            _randomSeed = randomSeed,
        };
        host.LoadWorld(host._archive, host._mainWdlName, mapOverrideName: null);
        return host;
    }

    /// <summary>
    /// Opens an already loaded level. Tests pass the textures directly and skip the archive.
    /// </summary>
    public static RuntimeHost OpenLevel(
        Level level,
        IReadOnlyDictionary<string, TextureImage> worldTextures,
        IPlatformWindow window,
        IInputSource input,
        IAudioSink audio,
        IRenderBackend graphics,
        string? contentRoot = null,
        IReadOnlyDictionary<string, TextureImage>? uiTextures = null,
        IReadOnlyDictionary<string, TextureImage>? fontTextures = null)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(worldTextures);
        var host = new RuntimeHost(
            window,
            input,
            audio,
            graphics,
            contentRoot ?? Environment.CurrentDirectory);
        host._mainWdlName = level.Name;
        host.CommitWorld(
            archive: null,
            archivePath: null,
            mainWdlName: level.Name,
            mapOverrideName: null,
            level,
            worldTextures,
            uiTextures ?? new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase),
            fontTextures ?? new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase));
        host.PlaceCameraAtStart();
        return host;
    }

    /// <summary>Runs IF_START and applies the player pose it writes, including eye height.</summary>
    public void Boot()
    {
        if (!_geometryReady)
        {
            RebuildWorldResources();
        }

        SyncPlayerRuntimeState();

        // OV-20261106-screen-frame: the first picture is drawn before IF_START runs, whole-window;
        // a border the script opens around a smaller window then shows it.
        DrawWorld();
        try
        {
            _startupRan = _runtime.RunStartup();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"IF_START failed: {ex.Message}");
        }

        _baseTitle = $"OpenVirtue - {_level.Name}";
        _window.Title = _baseTitle;
        UpdateStatus();
        HandleRuntimeHostRequests();
        _frameClock.Restart();
        _booted = true;
    }

    /// <summary>One frame: input, runtime tick, draw, then an optional present.</summary>
    public void Frame(bool present = true)
    {
        if (!_booted)
        {
            Boot();
        }

        if (!_window.IsOpen || _exitRequested)
        {
            return;
        }

        DispatchInputEdges();
        if (_activeFlic is null)
        {
            UpdateCamera();
        }

        TickRuntime();

        // OV-20261106-screen-frame: SCREENSHOT saves the screen as it stands when the instruction
        // runs, that is, the last frame drawn, and an EXIT in the same cycle does not lose it.
        FlushPendingScreenshots();
        if (!_window.IsOpen || _exitRequested)
        {
            return;
        }

        if (_activeFlic is { } flic)
        {
            DrawFlic(flic);
        }
        else
        {
            DrawWorld();
        }

        if (present)
        {
            _graphics.Present();
        }

        _window.SetCursorVisible(!(_window.Focused && _window.PointerInside && !_handlingInputRequest));
    }

    public void Dispose()
    {
        StopActiveSounds();
        _activeFlic = null;
        _pendingScreenshots.Clear();
    }

    private void LoadWorld(WrsArchive archive, string mainWdlName, string? mapOverrideName)
    {
        Level level = LevelLoader.Load(archive, Path.GetFileName(mainWdlName), mapOverrideName: mapOverrideName);
        IReadOnlyDictionary<string, TextureImage> textures = TextureLoader.Load(archive, level);
        IReadOnlyDictionary<string, TextureImage> uiImages = TextureLoader.LoadBitmaps(archive, level);
        IReadOnlyDictionary<string, TextureImage> fontImages = TextureLoader.LoadFonts(archive, level);
        CommitWorld(archive, _archivePath, Path.GetFileName(mainWdlName), mapOverrideName, level, textures, uiImages, fontImages);
        PlaceCameraAtStart();
    }

    private void ReloadWorld(
        string mainWdlName,
        string? mapOverrideName,
        string? resourceName,
        IReadOnlyDictionary<string, double>? levelTransitionGlobals = null)
    {
        (WrsArchive archive, string archivePath) = ResolveArchive(resourceName);
        string normalizedMain = Path.GetFileName(mainWdlName);
        string? normalizedMap = string.IsNullOrWhiteSpace(mapOverrideName) ? null : Path.GetFileName(mapOverrideName);
        Level level = LevelLoader.Load(archive, normalizedMain, mapOverrideName: normalizedMap);
        IReadOnlyDictionary<string, TextureImage> textures = TextureLoader.Load(archive, level);
        IReadOnlyDictionary<string, TextureImage> uiImages = TextureLoader.LoadBitmaps(archive, level);
        IReadOnlyDictionary<string, TextureImage> fontImages = TextureLoader.LoadFonts(archive, level);
        CommitWorld(archive, archivePath, normalizedMain, normalizedMap, level, textures, uiImages, fontImages);
        if (levelTransitionGlobals is not null)
        {
            _runtime.RestoreLevelTransitionGlobals(levelTransitionGlobals);
        }

        PlaceCameraAtStart();
        Boot();
    }

    private (WrsArchive Archive, string ArchivePath) ResolveArchive(string? resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            if (_archive is null || _archivePath is null)
            {
                throw new FileNotFoundException("This host has no WRS archive to reload.");
            }

            return (_archive, _archivePath);
        }

        string candidate = resourceName;
        if (!Path.IsPathRooted(candidate))
        {
            candidate = Path.Combine(_contentRoot, candidate);
        }

        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException($"Requested WRS archive '{resourceName}' could not be resolved.", candidate);
        }

        string archivePath = Path.GetFullPath(candidate);
        return (WrsArchive.ReadFile(archivePath), archivePath);
    }

    private void CommitWorld(
        WrsArchive? archive,
        string? archivePath,
        string mainWdlName,
        string? mapOverrideName,
        Level level,
        IReadOnlyDictionary<string, TextureImage> textures,
        IReadOnlyDictionary<string, TextureImage> uiImages,
        IReadOnlyDictionary<string, TextureImage> fontImages)
    {
        _activeFlic = null;
        _archive = archive;
        _archivePath = archivePath;
        _mainWdlName = mainWdlName;
        _mapOverrideName = mapOverrideName;
        _level = level;
        // Only a level read from an archive has its real bitmaps; tests hand in stand-ins of any size.
        if (archive is not null)
        {
            _blankSurfaces = TextureLoader.BlankSurfaceTextures(level, textures);
            var withBlank = new Dictionary<string, TextureImage>(textures, StringComparer.OrdinalIgnoreCase)
            {
                [MeshBuilder.BlankSurface] = TextureLoader.BlankSurfaceImage,
            };
            textures = withBlank;
        }
        else
        {
            _blankSurfaces = null;
        }

        _textureImages = textures;
        _uiImages = uiImages;
        _fontImages = fontImages;
        // A seeded generator carries on across level changes, as one generator would.
        Random? seededRandom = _randomSeed is { } seed ? (_runtime is { RandomSeeded: true } previous ? previous.RandomGenerator : new Random(seed)) : null;
        _runtime = new WdlRuntime(level, RuntimePrintDirectory(), textures, seededRandom)
        {
            FlicFileExists = HasArchiveEntry,
            SaveDirectory = _contentRoot,
            SoundPlayedOut = channel => _hostedChannels.Contains(channel) && !_audio.IsChannelPlaying(channel),
        };
        _runtime.OriginalSaveNames = OriginalSaveNames();
        _runtime.FatalErrors = _fatalErrors;
        _clearColor = ChooseBackgroundColor();
        _booted = false;
        _geometryReady = false;
        _startupRan = false;
        _lastFrameSeconds = 0;
        _lastTimeCorrection = 0;
        _statusAccum = 0;
        _handledGeometryInvalidations = 0;
        _handledAudioRequests = 0;
        _handledTextureSoundRequests = 0;
        _handledPaletteFadeRequests = 0;
        _handledBeepRequests = 0;
        _handledFlicRequests = 0;
        _handledStopFlicRequests = 0;
        _handledScreenshotRequests = 0;
        _handledPersistenceRequests = 0;
        _lastPresentationStatus = null;
        _lastPersistenceStatus = null;
        LoadPalettes();
        _hasPlayerRuntimeSnapshot = false;
        _pressedUiButton = null;
        _pressedUiButtonReleaseAction = null;
        _draggedUiSlider = null;
        _sliderGrabOffset = 0;
        _pendingScreenshots.Clear();
        _pendingMickey = Vector2.Zero;
        _frameMickey = Vector2.Zero;
        _mouseMotionWindow = Vector2.Zero;
        _mouseMotionTicks = 0;
        _mouseWasMoving = false;
        StopActiveSounds();
        _textureSoundRegion = -1;
        _ceilingSoundChannel = -1;
        _walkSoundDistance = 0;
        _lastWaveSoundCycle = 0;
        _spatialLoopSounds.Clear();
        _batches.Clear();
        _sprites.Clear();
    }

    private void TickRuntime()
    {
        _lastFrameSeconds = FrameClock?.Invoke() ?? _frameClock.Elapsed.TotalSeconds;
        _frameClock.Restart();
        WdlRuntime tickRuntime = _runtime;
        Player tickPlayer = _player;
        if (_activeFlic is { } tickFlic && !RunFlicCycles(tickRuntime, tickFlic))
        {
            UpdateStatusAtInterval();
            return;
        }

        _lastTimeCorrection = tickRuntime.Tick(_lastFrameSeconds);
        HandleRuntimeHostRequests();
        if (ReferenceEquals(_runtime, tickRuntime) &&
            ReferenceEquals(_player, tickPlayer) &&
            _activeFlic is null)
        {
            // MOVE_MODE 0 and below stops the player's movement and the object events (OV-20261104-move-mode).
            if (_runtime.GetSkill("MOVE_MODE") > 0)
            {
                WdlPlayerMotion.Apply(_runtime, _player, _lastTimeCorrection);
                _runtime.DispatchContactEvents();
            }

            UpdateTextureSounds();
            HandleRuntimeHostRequests();
            if (ReferenceEquals(_runtime, tickRuntime))
            {
                BuildSprites();
            }
        }

        UpdateStatusAtInterval();
    }

    private void UpdateStatusAtInterval()
    {
        _statusAccum += _lastFrameSeconds;
        if (_statusAccum < 0.25)
        {
            return;
        }

        _statusAccum = 0;
        UpdateStatus();
    }

    private void HandleRuntimeHostRequests()
    {
        HandleRuntimeControlRequests();
        HandleRuntimeAudioRequests();
        HandleRuntimePaletteRequests();
        HandleRuntimePresentationRequests();
        HandleRuntimePersistenceRequests();
        HandleRuntimeGeometryInvalidations();
        ApplyRuntimePlayerState();
        HandleRuntimeInputRequests();
    }

    private void PlaceCameraAtStart()
    {
        // A level's player stands on its floor at the first frame, and reads no heights before it
        // (OV-20261110-retail-play).
        _player = new Player(_level) { FeetPlaced = false };
        SpawnInLargestRoom();
        _runtime.RegisterObject("player", _player);
        _camera.Position = RenderSpace.FromMap(_player.Position);
        AdoptRuntimeHeading();
        SyncPlayerRuntimeState();
    }

    private void AdoptRuntimeHeading()
    {
        _camera.Yaw = (float)RenderSpace.YawFromPlayerAngle(_runtime.GetSkill("PLAYER_ANGLE"));
        _camera.Pitch = (float)Math.Atan(_runtime.GetSkill("PLAYER_TILT"));
    }

    private void SpawnInLargestRoom()
    {
        if (_level.PlayerStart is not null)
        {
            return;
        }

        var bounds = new Dictionary<int, (float MinX, float MinZ, float MaxX, float MaxZ)>();
        void Accumulate(int region, float vx, float vz)
        {
            if (region < 0 || region >= _level.Regions.Count)
            {
                return;
            }

            bounds[region] = bounds.TryGetValue(region, out (float MinX, float MinZ, float MaxX, float MaxZ) b)
                ? (MathF.Min(b.MinX, vx), MathF.Min(b.MinZ, vz), MathF.Max(b.MaxX, vx), MathF.Max(b.MaxZ, vz))
                : (vx, vz, vx, vz);
        }

        foreach (Wall wall in _level.Walls)
        {
            AccumulateVertex(wall.Vertex1);
            AccumulateVertex(wall.Vertex2);

            void AccumulateVertex(int vi)
            {
                if (vi >= 0 && vi < _level.Vertices.Count)
                {
                    Vertex v = _level.Vertices[vi];
                    Accumulate(wall.LeftRegion, v.X, v.Y);
                    Accumulate(wall.RightRegion, v.X, v.Y);
                }
            }
        }

        int best = -1;
        float bestArea = 0;
        float cx = 0;
        float cz = 0;
        foreach ((int region, (float MinX, float MinZ, float MaxX, float MaxZ) b) in bounds)
        {
            Region r = _level.Regions[region];
            if (r.CeilHeight - r.FloorHeight < 5)
            {
                continue;
            }

            float area = (b.MaxX - b.MinX) * (b.MaxZ - b.MinZ);
            if (area > bestArea)
            {
                bestArea = area;
                best = region;
                cx = (b.MinX + b.MaxX) * 0.5f;
                cz = (b.MinZ + b.MaxZ) * 0.5f;
            }
        }

        if (best >= 0)
        {
            _player.MoveTo(best, cx, cz);
        }
    }

    private void RebuildWorldResources()
    {
        _graphics.UploadWorld(_textureImages);
        _graphics.UploadUi(_uiImages);
        _graphics.UploadFonts(_fontImages);
        UploadShading();
        BuildGeometry();
        BuildSprites();
        _geometryReady = true;
    }

    /// <summary>
    /// Builds the shade table of the level's first palette that declares <c>RANGE</c>s (the retail
    /// levels' <c>levelPal</c>) and hands it to the renderer with the palette the screen shows;
    /// without one, textures are drawn in RGB (OV-20261029-shading).
    /// </summary>
    private void UploadShading()
    {
        _shading = false;
        _shadeTable = null;
        foreach ((string name, LevelPaletteShading shading) in _level.PaletteShading)
        {
            if (shading.Ranges.Count == 0)
            {
                continue;
            }

            byte[]? palette = _palettes.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Rgb;
            if (palette is null)
            {
                continue;
            }

            _shadeTable = PaletteShading.BuildTable(palette, shading.Ranges, shading.AutoRange);
            _shading = true;
            break;
        }

        RefreshPaletteFade();
    }

    /// <summary>
    /// Reads every declared <c>PALETTE</c>'s colours from its <c>PALFILE</c>, in declaration order;
    /// the last declared is the one the screen shows at the start (OV-20261104-fade-pal, start).
    /// </summary>
    private void LoadPalettes()
    {
        _palettes.Clear();
        foreach ((string name, string file) in _level.Palettes)
        {
            if (TryReadPalette(file, out byte[]? rgb))
            {
                _palettes.Add((name, rgb));
            }
        }

        _currentPalette = _palettes.Count > 0 ? _palettes[^1].Rgb : null;
        _fadeTarget = null;
        _fadeFactor = 0;
        _fade = PaletteFadeTint.None;
    }

    private bool TryReadPalette(string file, [NotNullWhen(true)] out byte[]? rgb)
    {
        rgb = null;
        if (!TryGetArchiveBytes(file, out byte[] bytes))
        {
            return false;
        }

        try
        {
            PcxImage image = PcxImage.Read(bytes);
            rgb = new byte[PaletteFade.Length];
            for (int i = 0; i < Math.Min(256, image.Palette.Length); i++)
            {
                rgb[i * 3] = image.Palette[i].R;
                rgb[(i * 3) + 1] = image.Palette[i].G;
                rgb[(i * 3) + 2] = image.Palette[i].B;
            }

            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>
    /// The palette the screen shows (OV-20261104-fade-pal): the current palette blended toward the
    /// fade's target by its factor, or null when the level declares none.
    /// </summary>
    public byte[]? ShownPalette =>
        _currentPalette is null ? null : PaletteFade.Blend(_currentPalette, _fadeTarget ?? [], _fadeFactor);

    /// <summary>
    /// Hands the renderer the palette the screen shows (with the shade table, for batches drawn through
    /// the palette) and works out the fade RGB batches draw: the current palette as a uniform colour
    /// when it is one, then the blend toward a uniform target.
    /// </summary>
    private void RefreshPaletteFade()
    {
        _graphics.UploadShading(_shading ? ShownPalette : null, _shading ? _shadeTable : null);

        // RGB batches cannot be re-indexed through another palette, so a non-uniform current palette
        // draws as the textures' own colours; a uniform one (the retail start palettes are black) is
        // drawn as that colour.
        var fade = PaletteFadeTint.None;
        if (_currentPalette is not null && PaletteFade.IsUniform(_currentPalette, out (byte R, byte G, byte B) shown))
        {
            fade = fade with { Current = new Vector3(shown.R, shown.G, shown.B) / 255f, CurrentWeight = 1 };
        }

        if (_fadeTarget is not null && _fadeFactor > 0 && PaletteFade.IsUniform(_fadeTarget, out (byte R, byte G, byte B) target))
        {
            fade = fade with { Target = new Vector3(target.R, target.G, target.B) / 255f, Factor = (float)Math.Min(_fadeFactor, 1) };
        }

        _fade = fade;
    }

    private void RebuildGeometryResources()
    {
        _batches.Clear();
        BuildGeometry();
        BuildSprites();
    }

    private void BuildGeometry()
    {
        _batches.Clear();
        _occluders = MeshBuilder.BuildOccluders(_level);
        _spriteFrames = MeshBuilder.BuildSpriteFrames(_level);
        _viewClip = new ViewClip(_level);
        LevelMesh mesh = MeshBuilder.Build(_level, _blankSurfaces);
        foreach (MeshBatch batch in mesh.Batches)
        {
            _batches.Add(new CachedBatch(
                batch.Texture,
                batch.Vertices.ToArray(),
                batch.AnimationSource,
                batch.AlphaTest,
                TextureAmbient(batch.Texture),
                batch.Surface));
        }
    }

    private void BuildSprites()
    {
        _sprites.Clear();
        _sprites.AddRange(SpriteBuilder.Build(_level, new Vector2(_player.Position.X, _player.Position.Z)));
    }

    private void UpdateStatus()
    {
        if (_player is null || _runtime is null)
        {
            return;
        }

        Vector3 p = _player.Position;
        string regionName = RegionName(_player.Region);
        double fps = _lastFrameSeconds > 0 ? 1.0 / _lastFrameSeconds : 0;
        (double floor, double ceiling) = RegionHeights(_player.Region);
        StatusText =
            $"world {_level.Name} | video {_level.VideoMode?.Name ?? "native"} | " +
            FormattableString.Invariant($"pos {p.X:F1}, {p.Y:F1}, {p.Z:F1} | ") +
            $"region {_player.Region} {regionName} | " +
            FormattableString.Invariant($"floor {floor:F1} ceil {ceiling:F1} | FPS {fps:F0} | TIME_CORR {_lastTimeCorrection:F3} | ") +
            $"audio {_hostedChannels.Count}/8 ch {_runtime.GetSkill("CHANNEL"):F0} | " +
            $"IF_START {(_level.StartupAction is null ? "none" : _startupRan ? "ran" : "failed")} | " +
            $"control {RuntimeControlStatus()}{PresentationStatusText()}{PersistenceStatusText()} | WDL movement";
        _window.Title = $"{_baseTitle} | {StatusText}";
    }

    private string PresentationStatusText() =>
        PaletteStatusText() + (_lastPresentationStatus is null ? string.Empty : $" | {_lastPresentationStatus}");

    private string PaletteStatusText()
    {
        if (_runtime.PaletteFadeRequests.Count == 0)
        {
            return string.Empty;
        }

        WdlPaletteFadeRequest request = _runtime.PaletteFadeRequests[^1];
        return FormattableString.Invariant(
            $" | pal {request.PaletteName}:{request.Factor:F2} x{_runtime.PaletteFadeRequests.Count} fade {_fade.CurrentWeight:F2}/{_fade.Factor:F2}");
    }

    private string PersistenceStatusText() =>
        _lastPersistenceStatus is null ? string.Empty : $" | save {_lastPersistenceStatus}";

    private string RuntimeControlStatus()
    {
        if (_runtime.ExitRequested)
        {
            return "exit";
        }

        if (_runtime.RequestedLevelName is { } levelName)
        {
            return _runtime.RequestedLevelResource is { } resource
                ? $"level {levelName} ({resource})"
                : $"level {levelName}";
        }

        if (_runtime.RequestedMapName is { } mapName)
        {
            return $"map {mapName}";
        }

        return "none";
    }

    private string RegionName(int region) =>
        region >= 0 && region < _level.Regions.Count ? _level.Regions[region].Name : "(none)";

    private (double Floor, double Ceiling) RegionHeights(int region)
    {
        if (region >= 0 && region < _level.Regions.Count)
        {
            Region r = _level.Regions[region];
            return (r.FloorHeight, r.CeilHeight);
        }

        return (double.NaN, double.NaN);
    }

    private string ArchiveStem() =>
        string.IsNullOrEmpty(_archivePath) ? "level" : Path.GetFileNameWithoutExtension(_archivePath);

    private sealed class CachedBatch(
        string? texture,
        RenderVertex[] vertices,
        AcknexObject? animationSource,
        bool alphaTest,
        float ambient,
        bool surface = false)
    {
        public string? Texture { get; } = texture;

        /// <summary>Floors and ceilings: drawn without writing depth (OV-20261104-attach).</summary>
        public bool Surface { get; } = surface;

        public RenderVertex[] Vertices { get; } = vertices;

        public AcknexObject? AnimationSource { get; } = animationSource;

        public bool AlphaTest { get; } = alphaTest;

        public float Ambient { get; } = ambient;
    }

    private readonly record struct SpatialLoopSound(int Channel, string SoundName);

    private readonly record struct SpatialLoopCandidate(
        AcknexObject Emitter,
        string SoundName,
        double BaseVolume,
        float Volume,
        float Pan);
}
