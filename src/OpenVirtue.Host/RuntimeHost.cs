// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using OpenVirtue.Engine;
using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Rendering;
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
    private IReadOnlyDictionary<string, TextureImage> _textureImages =
        new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, TextureImage> _uiImages =
        new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, TextureImage> _fontImages =
        new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase);

    private Vector4 _clearColor = DefaultClearColor;
    private Vector3 _paletteTint = Vector3.One;
    private string _baseTitle = "OpenVirtue";
    private bool _booted;
    private bool _geometryReady;
    private bool _startupRan;
    private bool _handlingInputRequest;
    private bool _exitRequested;
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

    /// <summary>The eye. Tests read it after <see cref="Boot"/>.</summary>
    public Camera Camera => _camera;

    /// <summary>The live WDL runtime.</summary>
    public WdlRuntime Runtime => _runtime;

    /// <summary>The status line written into the window title.</summary>
    public string StatusText { get; private set; } = "";

    /// <summary>True after the runtime or the window has asked to stop.</summary>
    public bool ExitRequested => _exitRequested || !_window.IsOpen;

    /// <summary>Opens a WRS archive the way the interactive app does.</summary>
    public static RuntimeHost OpenArchive(
        string archivePath,
        string mainWdlName,
        IPlatformWindow window,
        IInputSource input,
        IAudioSink audio,
        IRenderBackend graphics)
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

        FlushPendingScreenshots();
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
        _textureImages = textures;
        _uiImages = uiImages;
        _fontImages = fontImages;
        _runtime = new WdlRuntime(level, RuntimePrintDirectory(), textures)
        {
            FlicFileExists = HasArchiveEntry,
        };
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
        _paletteTint = Vector3.One;
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
        _lastFrameSeconds = _frameClock.Elapsed.TotalSeconds;
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
            WdlPlayerMotion.Apply(_runtime, _player, _lastTimeCorrection);
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
        _player = new Player(_level);
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
        BuildGeometry();
        BuildSprites();
        _geometryReady = true;
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
        LevelMesh mesh = MeshBuilder.Build(_level);
        foreach (MeshBatch batch in mesh.Batches)
        {
            _batches.Add(new CachedBatch(
                batch.Texture,
                batch.Vertices.ToArray(),
                batch.AnimationSource,
                batch.AlphaTest,
                TextureAmbient(batch.Texture)));
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
            $" | pal {request.PaletteName}:{request.Factor:F2} x{_runtime.PaletteFadeRequests.Count} tint {_paletteTint.X:F2},{_paletteTint.Y:F2},{_paletteTint.Z:F2}");
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
        float ambient)
    {
        public string? Texture { get; } = texture;

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
