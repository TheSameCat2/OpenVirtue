// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Diagnostics;
using System.Drawing.Imaging;
using System.Media;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.Wave;
using OpenVirtue.Engine;
using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Formats.Flic;
using OpenVirtue.Formats.Pcx;
using OpenVirtue.Formats.Wav;
using OpenVirtue.Formats.Wrs;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace OpenVirtue.App;

/// <summary>
/// A WinForms window hosting a Direct3D 11 view of a loaded level: the wall geometry,
/// textured from the level's PCX bitmaps, with a depth buffer and WDL-driven player status.
/// </summary>
internal sealed class LevelWindow : Form
{
    private static readonly Color4 DefaultClearColor = new(0.08f, 0.10f, 0.14f, 1f);
    private static readonly JsonSerializerOptions SaveJsonOptions = new() { WriteIndented = true };
    private const double FullTurnRadians = Math.PI * 2.0;

    private const string ShaderSource =
        """
        cbuffer Frame : register(b0) { float4x4 ViewProjection; float4 TintAndLight; float4 Material; };
        Texture2D Diffuse : register(t0);
        SamplerState Sampler : register(s0);

        struct VSInput  { float3 pos : POSITION; float2 uv : TEXCOORD0; float ambient : TEXCOORD1; };
        struct VSOutput { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float ambient : TEXCOORD1; };

        VSOutput VSMain(VSInput input)
        {
            VSOutput o;
            o.pos = mul(ViewProjection, float4(input.pos, 1.0));
            o.uv = input.uv;
            o.ambient = input.ambient;
            return o;
        }

        float4 PSMain(VSOutput input) : SV_Target
        {
            float4 color = Diffuse.Sample(Sampler, input.uv);
            if (Material.x > 0.5) clip(color.a - 0.5);
            float light = saturate(TintAndLight.a + input.ambient);
            return float4(saturate(color.rgb * TintAndLight.rgb * light), 1.0);
        }
        """;

    private WrsArchive _archive;
    private string _archivePath;
    private string _mainWdlName;
    private string? _mapOverrideName;
    private Level _level = null!;
    private IReadOnlyDictionary<string, TextureImage> _textureImages = null!;
    private Color4 _clearColor;
    private readonly Panel _renderSurface = new()
    {
        BackColor = System.Drawing.Color.Black,
        Dock = DockStyle.Fill,
        TabStop = true,
    };
    private readonly StatusStrip _statusStrip = new() { SizingGrip = false };
    private readonly ToolStripStatusLabel _statusLabel = new()
    {
        Spring = true,
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Camera _camera = new();
    private Player _player = null!;
    private readonly HashSet<Keys> _keysDown = [];
    private readonly HashSet<MouseButtons> _mouseButtonsDown = [];
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly List<(
        int Start,
        int Count,
        string? Texture,
        float Ambient,
        AcknexObject? AnimationSource,
        bool AlphaTest)> _batches = [];
    private readonly Dictionary<string, ID3D11ShaderResourceView> _textureViews = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ID3D11ShaderResourceView> _uiTextureViews = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ID3D11ShaderResourceView> _fontTextureViews = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SpriteBillboard> _sprites = [];
    private readonly List<ActiveSound> _activeSounds = [];
    private readonly Dictionary<AcknexObject, SpatialLoopSound> _spatialLoopSounds = [];
    private readonly List<WdlScreenshotRequest> _pendingScreenshots = [];
    private WdlRuntime _runtime = null!;
    private IReadOnlyDictionary<string, TextureImage> _uiImages = new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, TextureImage> _fontImages = new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();

    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private IDXGISwapChain1 _swapChain = null!;
    private ID3D11RenderTargetView _renderTarget = null!;
    private ID3D11Texture2D _depthTexture = null!;
    private ID3D11DepthStencilView _depthView = null!;
    private ID3D11VertexShader _vertexShader = null!;
    private ID3D11PixelShader _pixelShader = null!;
    private ID3D11InputLayout _inputLayout = null!;
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11Buffer _constantBuffer = null!;
    private ID3D11RasterizerState _rasterizer = null!;
    private ID3D11DepthStencilState _depthState = null!;
    private ID3D11SamplerState _sampler = null!;
    private ID3D11ShaderResourceView? _defaultView;
    private ID3D11ShaderResourceView? _flicView;
    private int _vertexCount;

    private Point _lastMouseSample;
    private bool _hasMouseSample;
    private Vector2 _pendingMickey;
    private Vector2 _frameMickey;
    private Vector2 _mouseMotionWindow;
    private double _mouseMotionTicks;
    private bool _mouseWasMoving;
    private bool _systemCursorHidden;
    private UiButtonIdentity? _pressedUiButton;
    private string? _pressedUiButtonReleaseAction;
    private UiSliderTarget? _draggedUiSlider;
    private float _sliderGrabOffset;
    private string _baseTitle = "";
    private bool _startupRan;
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
    private Vector3 _paletteTint = Vector3.One;
    private bool _handlingInputRequest;
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

    public LevelWindow(string archivePath, string mainWdlName)
    {
        _archivePath = Path.GetFullPath(archivePath);
        _archive = WrsArchive.ReadFile(_archivePath);
        _mainWdlName = Path.GetFileName(mainWdlName);
        LoadWorld(_archive, _mainWdlName, mapOverrideName: null);
        Text = $"OpenVirtue - {_level.Name}";
        ClientSize = new System.Drawing.Size(1280, 720);
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;
        _renderSurface.MouseDown += RenderSurface_MouseDown;
        _renderSurface.MouseUp += RenderSurface_MouseUp;
        _renderSurface.MouseMove += RenderSurface_MouseMove;
        _renderSurface.MouseEnter += (_, _) => HideSystemCursor();
        _renderSurface.MouseLeave += (_, _) => ResetMouseSampleAndShowCursor();
        _renderSurface.Resize += (_, _) => ResizeBuffers();
        Deactivate += (_, _) => ShowSystemCursor();
        Activated += (_, _) => HideCursorWhenPointerIsOverSurface();
        _statusStrip.Items.Add(_statusLabel);
        Controls.Add(_renderSurface);
        Controls.Add(_statusStrip);
        PlaceCameraAtStart();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public Matrix4x4 ViewProjection;
        public Vector4 TintAndLight;
        public Vector4 Material;
    }

    private void LoadWorld(WrsArchive archive, string mainWdlName, string? mapOverrideName)
    {
        Level level = LevelLoader.Load(archive, Path.GetFileName(mainWdlName), mapOverrideName: mapOverrideName);
        IReadOnlyDictionary<string, TextureImage> textures = TextureLoader.Load(archive, level);
        IReadOnlyDictionary<string, TextureImage> uiImages = TextureLoader.LoadBitmaps(archive, level);
        IReadOnlyDictionary<string, TextureImage> fontImages = TextureLoader.LoadFonts(archive, level);
        CommitWorld(archive, _archivePath, Path.GetFileName(mainWdlName), mapOverrideName, level, textures, uiImages, fontImages);
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
        if (_device is not null)
        {
            RebuildWorldResources();
        }

        BootRuntime();
    }

    private (WrsArchive Archive, string ArchivePath) ResolveArchive(string? resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            return (_archive, _archivePath);
        }

        string candidate = resourceName;
        if (!Path.IsPathRooted(candidate))
        {
            candidate = Path.Combine(Path.GetDirectoryName(_archivePath) ?? Environment.CurrentDirectory, candidate);
        }

        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException($"Requested WRS archive '{resourceName}' could not be resolved.", candidate);
        }

        string archivePath = Path.GetFullPath(candidate);
        return (WrsArchive.ReadFile(archivePath), archivePath);
    }

    private void CommitWorld(
        WrsArchive archive,
        string archivePath,
        string mainWdlName,
        string? mapOverrideName,
        Level level,
        IReadOnlyDictionary<string, TextureImage> textures,
        IReadOnlyDictionary<string, TextureImage> uiImages,
        IReadOnlyDictionary<string, TextureImage> fontImages)
    {
        ClearFlicPlayback();
        _archive = archive;
        _archivePath = archivePath;
        _mainWdlName = mainWdlName;
        _mapOverrideName = mapOverrideName;
        _level = level;
        _textureImages = textures;
        _uiImages = uiImages;
        _fontImages = fontImages;
        _runtime = new WdlRuntime(_level, RuntimePrintDirectory(), _textureImages);
        _clearColor = ChooseBackgroundColor();
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
        _keysDown.Clear();
        _mouseButtonsDown.Clear();
        _pendingMickey = Vector2.Zero;
        _frameMickey = Vector2.Zero;
        _mouseMotionWindow = Vector2.Zero;
        _mouseMotionTicks = 0;
        _mouseWasMoving = false;
        _hasMouseSample = false;
        StopActiveSounds();
        _textureSoundRegion = -1;
        _ceilingSoundChannel = -1;
        _walkSoundDistance = 0;
        _lastWaveSoundCycle = 0;
        _spatialLoopSounds.Clear();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        InitializeDirect3D();
        RebuildWorldResources();
        BootRuntime();
        _timer.Tick += (_, _) => RenderFrame();
        _timer.Start();
    }

    /// <summary>Boots the level's WDL runtime: runs its IF_START script and shows status in the strip.</summary>
    private void BootRuntime()
    {
        SyncPlayerRuntimeState();
        try
        {
            _startupRan = _runtime.RunStartup(); // run the level's IF_START script, if any
        }
        catch
        {
            // A startup-script fault must not take down the viewer.
        }

        _baseTitle = $"OpenVirtue - {_level.Name}";
        Text = _baseTitle;
        UpdateStatus();
        HandleRuntimeHostRequests();
        _frameClock.Restart(); // discard level-load time before the first real frame delta
    }

    /// <summary>Advances the runtime one frame and surfaces the live tick in the status strip.</summary>
    private void TickRuntime()
    {
        _lastFrameSeconds = _frameClock.Elapsed.TotalSeconds;
        _frameClock.Restart();
        WdlRuntime tickRuntime = _runtime;
        Player tickPlayer = _player;
        FlicPlayback? tickFlic = _activeFlic;
        if (tickFlic is not null)
        {
            _lastTimeCorrection = tickRuntime.TickFlic(_lastFrameSeconds);
            HandleRuntimeHostRequests();
            if (ReferenceEquals(_runtime, tickRuntime) && ReferenceEquals(_activeFlic, tickFlic))
            {
                AdvanceFlic(tickFlic, _lastFrameSeconds);
            }

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
        if (_statusAccum >= 0.25)
        {
            _statusAccum = 0;
            UpdateStatus();
        }
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

    private void HandleRuntimeControlRequests()
    {
        if (_runtime.ExitRequested)
        {
            _timer.Stop();
            BeginInvoke(Close);
            return;
        }

        try
        {
            if (_runtime.RequestedLevelName is { } levelName)
            {
                IReadOnlyDictionary<string, double> globals = _runtime.CaptureLevelTransitionGlobals();
                ReloadWorld(levelName, mapOverrideName: null, _runtime.RequestedLevelResource, globals);
            }
            else if (_runtime.RequestedMapName is { } mapName)
            {
                ReloadWorld(_mainWdlName, mapName, resourceName: null);
            }
        }
        catch (Exception ex)
        {
            _timer.Stop();
            MessageBox.Show(ex.ToString(), "OpenVirtue - script load request failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            BeginInvoke(Close);
        }
    }

    private void HandleRuntimeGeometryInvalidations()
    {
        int pending = _runtime.ShakeRequests.Count;
        if (pending == _handledGeometryInvalidations)
        {
            return;
        }

        _handledGeometryInvalidations = pending;
        if (_device is not null)
        {
            RebuildGeometryResources();
        }
    }

    private void HandleRuntimeAudioRequests()
    {
        PruneFinishedSounds();

        while (_handledTextureSoundRequests < _runtime.TextureSoundRequests.Count)
        {
            PlayTextureSoundCue(_runtime.TextureSoundRequests[_handledTextureSoundRequests++]);
        }

        while (_handledAudioRequests < _runtime.AudioRequests.Count)
        {
            WdlAudioRequest request = _runtime.AudioRequests[_handledAudioRequests++];
            switch (request.Operation)
            {
                case WdlSoundOperation.Play:
                    TryPlaySound(request.Sound);
                    break;
                case WdlSoundOperation.StopChannel:
                    StopActiveSound(request.Channel);
                    break;
                case WdlSoundOperation.StopAll:
                    StopActiveSounds();
                    break;
            }
        }
    }

    private void PlayTextureSoundCue(WdlTextureSoundRequest cue)
    {
        if (string.IsNullOrWhiteSpace(cue.Texture.SoundName) ||
            !TrySoundEmitter(cue.Target, out Vector3 position, out _))
        {
            return;
        }

        (float volume, float pan) = SpatialSoundMix(
            cue.Texture.SoundVolume,
            position,
            cue.Texture,
            applyVerticalDistance: cue.Target is MapEntity);
        if (volume > 0)
        {
            _runtime.PlayTextureSound(cue.Texture.SoundName, volume, loop: false, balance: pan);
        }
    }

    private void HandleRuntimePaletteRequests()
    {
        while (_handledPaletteFadeRequests < _runtime.PaletteFadeRequests.Count)
        {
            WdlPaletteFadeRequest request = _runtime.PaletteFadeRequests[_handledPaletteFadeRequests++];
            ApplyPaletteFade(request);
        }
    }

    private void ApplyPaletteFade(WdlPaletteFadeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName) ||
            TryGetArchiveBytes(request.FileName, out byte[] bytes) == false)
        {
            return;
        }

        try
        {
            PcxImage paletteImage = PcxImage.Read(bytes);
            Vector3 target = PaletteColorCast(paletteImage.Palette);
            float factor = (float)Math.Clamp(request.Factor, 0, 1);
            _paletteTint = Vector3.Lerp(Vector3.One, target, factor);
        }
        catch
        {
            // Palette fades are approximated for the harness; bad palette assets should not stop rendering.
        }
    }

    private void HandleRuntimePresentationRequests()
    {
        while (_handledBeepRequests < _runtime.BeepCount)
        {
            _handledBeepRequests++;
            SystemSounds.Beep.Play();
        }

        while (_handledFlicRequests < _runtime.FlicRequests.Count)
        {
            HandleFlicRequest(_runtime.FlicRequests[_handledFlicRequests++]);
        }

        while (_handledStopFlicRequests < _runtime.StopFlicCount)
        {
            _handledStopFlicRequests++;
            ClearFlicPlayback();
            _lastPresentationStatus = "flic stopped";
        }

        while (_handledScreenshotRequests < _runtime.ScreenshotRequests.Count)
        {
            _pendingScreenshots.Add(_runtime.ScreenshotRequests[_handledScreenshotRequests++]);
        }
    }

    private void HandleFlicRequest(WdlFlicRequest request)
    {
        string fileName = Path.GetFileName(request.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            _lastPresentationStatus = "flic request ignored";
            _runtime.CompleteFlic(request.Id);
            return;
        }

        if (!TryGetArchiveBytes(fileName, out byte[] bytes))
        {
            _lastPresentationStatus = $"flic {fileName} missing";
            _runtime.CompleteFlic(request.Id);
            return;
        }

        try
        {
            FlicAnimation animation = FlicAnimation.Read(bytes);
            if (animation.Frames.Count == 0)
            {
                throw new InvalidDataException("FLIC contains no display frames.");
            }

            if (_activeFlic is { } replaced)
            {
                _runtime.CompleteFlic(replaced.Request.Id);
            }

            ClearFlicPlayback();
            _activeFlic = new FlicPlayback(request, fileName, animation);
            _runtime.SetSkill("FLIC_FRAME", 0);
            _lastPresentationStatus = $"flic {fileName} 1/{animation.Frames.Count}";
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or OverflowException)
        {
            _lastPresentationStatus = $"flic {fileName} invalid";
            _runtime.CompleteFlic(request.Id);
        }
    }

    private void AdvanceFlic(FlicPlayback playback, double deltaSeconds)
    {
        playback.ElapsedSeconds += Math.Max(0, deltaSeconds);
        double frameSeconds = Math.Max(0.001, playback.Animation.FrameDelay.TotalSeconds);
        while (playback.ElapsedSeconds >= frameSeconds)
        {
            playback.ElapsedSeconds -= frameSeconds;
            if (playback.FrameIndex + 1 >= playback.Animation.Frames.Count)
            {
                int requestId = playback.Request.Id;
                ClearFlicPlayback();
                _lastPresentationStatus = $"flic {playback.FileName} complete";
                _runtime.CompleteFlic(requestId);
                return;
            }

            playback.FrameIndex++;
            _runtime.SetSkill("FLIC_FRAME", playback.FrameIndex);
            _lastPresentationStatus =
                $"flic {playback.FileName} {playback.FrameIndex + 1}/{playback.Animation.Frames.Count}";
        }
    }

    private void ClearFlicPlayback()
    {
        _flicView?.Dispose();
        _flicView = null;
        _activeFlic = null;
    }

    private void HandleRuntimePersistenceRequests()
    {
        while (_handledPersistenceRequests < _runtime.PersistenceRequests.Count)
        {
            WdlPersistenceRequest request = _runtime.PersistenceRequests[_handledPersistenceRequests++];
            if (request.InfoOnly)
            {
                _lastPersistenceStatus = $"{PersistenceVerb(request)} info {PersistenceSlotName(request)} noted";
                continue;
            }

            try
            {
                if (request.Operation == WdlPersistenceOperation.Save)
                {
                    SaveRuntimeSnapshot(request);
                }
                else
                {
                    LoadRuntimeSnapshot(request);
                }
            }
            catch (Exception ex)
            {
                _lastPersistenceStatus = $"{PersistenceVerb(request)} {PersistenceSlotName(request)} failed: {ex.Message}";
            }
        }
    }

    private void TryPlaySound(WdlSoundRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName) ||
            TryGetArchiveBytes(request.FileName, out byte[] bytes) == false)
        {
            _runtime.CompleteSound(request.Channel);
            return;
        }

        try
        {
            WavSound.Read(bytes);
            (float volume, float pan) = SoundMix(request);
            if (volume <= 0)
            {
                _runtime.CompleteSound(request.Channel);
                return;
            }

            var sound = new ActiveSound(bytes, request.Channel, volume, pan, request.Loop);
            _activeSounds.Add(sound);
            sound.Play();
        }
        catch
        {
            _runtime.CompleteSound(request.Channel);
            // Audio is helpful for the harness, but malformed/missing assets should not stop rendering.
        }
    }

    private (float Volume, float Pan) SoundMix(WdlSoundRequest request)
    {
        double volume = Math.Clamp(request.Volume, 0, 1) *
                        Math.Clamp(_runtime.GetSkill("SOUND_VOL"), 0, 1);
        double pan = Math.Clamp(request.Balance ?? 0, -1, 1);
        if (request.EmitterName is null ||
            _runtime.GetObject(request.EmitterName) is not { } emitter ||
            !TrySoundEmitter(emitter, out Vector3 position, out LevelTexture? texture))
        {
            return ((float)volume, (float)pan);
        }

        return SpatialSoundMix(volume, position, texture, pan, emitter is MapEntity);
    }

    private (float Volume, float Pan) SpatialSoundMix(
        double volume,
        Vector3 position,
        LevelTexture? texture,
        double pan = 0,
        bool applyVerticalDistance = true)
    {
        // Emitter and player are map positions; the pan is taken against the camera's
        // screen-right axis, which lives in render space, so convert the offset first.
        Vector3 offset = RenderSpace.FromMap(position) - RenderSpace.FromMap(_player.Position);
        float horizontalDistance = MathF.Sqrt((offset.X * offset.X) + (offset.Z * offset.Z));
        double audibleDistance = texture?.SoundDistance ?? 100;
        if (audibleDistance <= 0 || horizontalDistance >= audibleDistance)
        {
            return (0, 0);
        }

        volume *= 1 - (horizontalDistance / audibleDistance);
        if (applyVerticalDistance &&
            texture is { SoundVerticalDistance: > 0 } spatialTexture &&
            Math.Abs(offset.Y) >= spatialTexture.SoundVerticalDistance)
        {
            return (0, 0);
        }

        if (horizontalDistance > 0.0001f)
        {
            var horizontalDirection = Vector3.Normalize(new Vector3(offset.X, 0, offset.Z));
            pan = Math.Clamp(Vector3.Dot(horizontalDirection, _camera.Right), -1, 1);
        }

        return ((float)Math.Clamp(volume, 0, 1), (float)pan);
    }

    private bool TrySoundEmitter(AcknexObject emitter, out Vector3 position, out LevelTexture? texture)
    {
        string? textureName = null;
        switch (emitter)
        {
            case MapEntity entity:
                double floor = entity.Region >= 0 && entity.Region < _level.Regions.Count
                    ? _level.Regions[entity.Region].FloorHeight
                    : 0;
                double height = entity["GROUND"] > 0 ? entity.Z : floor + entity.Z;
                position = new Vector3((float)entity.X, (float)height, (float)entity.Y);
                textureName = entity.Texture;
                break;
            case Wall wall when wall.Vertex1 >= 0 && wall.Vertex1 < _level.Vertices.Count &&
                                wall.Vertex2 >= 0 && wall.Vertex2 < _level.Vertices.Count:
                Vertex first = _level.Vertices[wall.Vertex1];
                Vertex second = _level.Vertices[wall.Vertex2];
                position = new Vector3(
                    (float)((first.X + second.X) * 0.5),
                    0,
                    (float)((first.Y + second.Y) * 0.5));
                textureName = wall.Texture;
                break;
            default:
                position = default;
                texture = null;
                return false;
        }

        texture = textureName is not null && _level.Textures.TryGetValue(textureName, out LevelTexture found)
            ? found
            : null;
        return true;
    }

    private bool TryGetArchiveBytes(string fileName, out byte[] bytes)
    {
        string normalized = Path.GetFileName(fileName);
        foreach (WrsEntry entry in _archive.Entries)
        {
            if (entry.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                bytes = entry.GetData();
                return true;
            }
        }

        bytes = [];
        return false;
    }

    private void PruneFinishedSounds()
    {
        for (int i = _activeSounds.Count - 1; i >= 0; i--)
        {
            if (_activeSounds[i].IsExpired)
            {
                _runtime.CompleteSound(_activeSounds[i].Channel);
                _activeSounds[i].Dispose();
                _activeSounds.RemoveAt(i);
            }
        }
    }

    private void StopActiveSound(int channel)
    {
        for (int i = _activeSounds.Count - 1; i >= 0; i--)
        {
            if (_activeSounds[i].Channel != channel)
            {
                continue;
            }

            _activeSounds[i].Dispose();
            _activeSounds.RemoveAt(i);
        }
    }

    private void StopActiveSounds()
    {
        foreach (ActiveSound sound in _activeSounds)
        {
            _runtime?.CompleteSound(sound.Channel);
            sound.Dispose();
        }

        _activeSounds.Clear();
    }

    private void UpdateTextureSounds()
    {
        if (_player.Region < 0 || _player.Region >= _level.Regions.Count)
        {
            return;
        }

        OpenVirtue.Engine.Map.Region region = _level.Regions[_player.Region];
        var position = new Vector2(_player.Position.X, _player.Position.Z);
        if (_textureSoundRegion != _player.Region)
        {
            if (_ceilingSoundChannel >= 0)
            {
                _runtime.StopSoundChannel(_ceilingSoundChannel);
            }

            _textureSoundRegion = _player.Region;
            _ceilingSoundChannel = -1;
            _lastTextureSoundPosition = position;
            _walkSoundDistance = 0;
            _lastWaveSoundCycle = WaveSoundCycle();
            StartCeilingTextureSound(region);
        }

        UpdateCeilingSoundVolume(region);
        UpdateSpatialTextureSounds();

        float moved = Vector2.Distance(position, _lastTextureSoundPosition);
        _lastTextureSoundPosition = position;
        double wavePeriod = Math.Max(0.001, _runtime.GetSkill("WAVE_PERIOD"));
        _runtime.SetSkill("WAVE", Math.Sin(Math.Tau * _runtime.GetSkill("TICKS") / wavePeriod));
        if (!_player.Grounded || moved <= 0.0001f ||
            !TryTexture(region.FloorTexture, out LevelTexture floorTexture) ||
            string.IsNullOrWhiteSpace(floorTexture.SoundName))
        {
            _runtime.SetSkill("WALK", 0);
            _lastWaveSoundCycle = WaveSoundCycle();
            return;
        }

        _walkSoundDistance += moved;
        double walkPeriod = Math.Max(0.001, _runtime.GetSkill("WALK_PERIOD"));
        _runtime.SetSkill("WALK", Math.Sin(Math.Tau * _walkSoundDistance / walkPeriod));

        bool trigger;
        if (floorTexture.IsSoundLoop)
        {
            long waveCycle = WaveSoundCycle();
            trigger = waveCycle != _lastWaveSoundCycle;
            _lastWaveSoundCycle = waveCycle;
        }
        else
        {
            trigger = _walkSoundDistance >= walkPeriod;
            if (trigger)
            {
                _walkSoundDistance %= walkPeriod;
            }
        }

        if (trigger)
        {
            double volume = floorTexture.SoundVolume * Math.Clamp(_runtime.GetSkill("PSOUND_VOL"), 0, 2);
            _runtime.PlayTextureSound(floorTexture.SoundName, volume, loop: false);
        }
    }

    private long WaveSoundCycle()
    {
        double period = Math.Max(0.001, _runtime.GetSkill("WAVE_PERIOD"));
        return (long)Math.Floor(_runtime.GetSkill("TICKS") / period);
    }

    private void StartCeilingTextureSound(OpenVirtue.Engine.Map.Region region)
    {
        if (!TryTexture(region.CeilTexture, out LevelTexture texture) ||
            string.IsNullOrWhiteSpace(texture.SoundName))
        {
            return;
        }

        int channel = _runtime.PlayTextureSound(texture.SoundName, texture.SoundVolume, texture.IsSoundLoop);
        if (texture.IsSoundLoop)
        {
            _ceilingSoundChannel = channel;
        }
    }

    private void UpdateCeilingSoundVolume(OpenVirtue.Engine.Map.Region region)
    {
        if (_ceilingSoundChannel < 0 ||
            !TryTexture(region.CeilTexture, out LevelTexture texture))
        {
            return;
        }

        if (_runtime.GetSkill($"CHANNEL_{_ceilingSoundChannel}") == 0)
        {
            _ceilingSoundChannel = -1;
            return;
        }

        ActiveSound? sound = _activeSounds.FirstOrDefault(candidate => candidate.Channel == _ceilingSoundChannel);
        sound?.UpdateMix(
            (float)(texture.SoundVolume * Math.Clamp(_runtime.GetSkill("SOUND_VOL"), 0, 1)),
            pan: 0);
    }

    private bool TryTexture(string? name, out LevelTexture texture)
    {
        if (!string.IsNullOrWhiteSpace(name) && _level.Textures.TryGetValue(name, out texture))
        {
            return true;
        }

        texture = default;
        return false;
    }

    private void UpdateSpatialTextureSounds()
    {
        var candidates = new List<SpatialLoopCandidate>();
        foreach (Wall wall in _level.Walls)
        {
            AddSpatialLoopCandidate(wall, candidates);
        }

        foreach (Thing thing in _level.Things)
        {
            AddSpatialLoopCandidate(thing, candidates);
        }

        foreach (Actor actor in _level.Actors)
        {
            AddSpatialLoopCandidate(actor, candidates);
        }

        Dictionary<AcknexObject, SpatialLoopCandidate> audible = candidates.ToDictionary(
            static candidate => candidate.Emitter);
        foreach ((AcknexObject emitter, SpatialLoopSound active) in _spatialLoopSounds.ToArray())
        {
            if (!audible.TryGetValue(emitter, out SpatialLoopCandidate candidate) ||
                !candidate.SoundName.Equals(active.SoundName, StringComparison.OrdinalIgnoreCase) ||
                _runtime.GetSkill($"CHANNEL_{active.Channel}") == 0)
            {
                _runtime.StopSoundChannel(active.Channel);
                _spatialLoopSounds.Remove(emitter);
                continue;
            }

            ActiveSound? sound = _activeSounds.FirstOrDefault(item => item.Channel == active.Channel);
            sound?.UpdateMix(candidate.Volume, candidate.Pan);
        }

        foreach (SpatialLoopCandidate candidate in candidates.OrderByDescending(static item => item.Volume))
        {
            if (_spatialLoopSounds.ContainsKey(candidate.Emitter) || !HasFreeSoundChannel())
            {
                continue;
            }

            int channel = _runtime.PlayTextureSound(
                candidate.SoundName,
                candidate.BaseVolume,
                loop: true,
                candidate.Pan);
            if (channel >= 0)
            {
                _spatialLoopSounds[candidate.Emitter] = new SpatialLoopSound(channel, candidate.SoundName);
            }
        }
    }

    private void AddSpatialLoopCandidate(AcknexObject emitter, List<SpatialLoopCandidate> candidates)
    {
        if (!TrySoundEmitter(emitter, out Vector3 position, out LevelTexture? texture) ||
            texture is not { IsSoundLoop: true } spatialTexture ||
            string.IsNullOrWhiteSpace(spatialTexture.SoundName))
        {
            return;
        }

        (float requestVolume, float pan) = SpatialSoundMix(
            spatialTexture.SoundVolume,
            position,
            spatialTexture,
            applyVerticalDistance: emitter is MapEntity);
        if (requestVolume > 0)
        {
            float volume = requestVolume * (float)Math.Clamp(_runtime.GetSkill("SOUND_VOL"), 0, 1);
            candidates.Add(new SpatialLoopCandidate(
                emitter,
                spatialTexture.SoundName,
                requestVolume,
                volume,
                pan));
        }
    }

    private bool HasFreeSoundChannel() =>
        Enumerable.Range(0, 8).Any(channel => _runtime.GetSkill($"CHANNEL_{channel}") == 0);

    private static Vector3 PaletteColorCast(Rgb24[] palette)
    {
        if (palette.Length == 0)
        {
            return Vector3.One;
        }

        long r = 0;
        long g = 0;
        long b = 0;
        foreach (Rgb24 color in palette)
        {
            r += color.R;
            g += color.G;
            b += color.B;
        }

        var average = new Vector3(r, g, b) / (255.0f * palette.Length);
        float strongestChannel = Math.Max(average.X, Math.Max(average.Y, average.Z));
        return strongestChannel > 0.01f ? average / strongestChannel : Vector3.Zero;
    }

    private void HandleRuntimeInputRequests()
    {
        if (_handlingInputRequest || _runtime.PendingInputRequests.Count == 0)
        {
            return;
        }

        _handlingInputRequest = true;
        bool wasRunning = _timer.Enabled;
        if (wasRunning)
        {
            _timer.Stop();
        }

        try
        {
            while (_runtime.PendingInputRequests.Count > 0)
            {
                WdlInputRequest request = _runtime.PendingInputRequests[0];
                using var dialog = new WdlInputDialog(request);
                DialogResult result = dialog.ShowDialog(this);
                int resultCode = result == DialogResult.OK ? 0 : -1;
                _runtime.CompleteInput(request.Id, dialog.Value, resultCode);
                HandleRuntimeHostRequests();
                if (_runtime.ExitRequested || IsDisposed)
                {
                    break;
                }
            }
        }
        finally
        {
            _handlingInputRequest = false;
            if (wasRunning && !_runtime.ExitRequested && !IsDisposed)
            {
                _frameClock.Restart();
                _timer.Start();
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool wasAlreadyDown = !_keysDown.Add(e.KeyCode);
        UpdateRuntimeInputSkills();
        if (!wasAlreadyDown)
        {
            TriggerRuntimeKeyHooks(e.KeyCode);
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _keysDown.Remove(e.KeyCode);
        UpdateRuntimeInputSkills();
    }

    private void TriggerRuntimeKeyHooks(Keys key)
    {
        if (_handlingInputRequest || _runtime is null)
        {
            return;
        }

        SyncPlayerRuntimeState();
        _runtime.TriggerActionHook("IF_ANYKEY");
        string? hookName = KeyHookName(key);
        if (hookName is not null)
        {
            _runtime.TriggerActionHook(hookName);
        }

        HandleRuntimeHostRequests();
    }

    private static string? KeyHookName(Keys key) =>
        key switch
        {
            >= Keys.F1 and <= Keys.F12 => "IF_F" + (key - Keys.F1 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            >= Keys.A and <= Keys.Z => "IF_" + (char)('A' + key - Keys.A),
            >= Keys.D0 and <= Keys.D9 => "IF_" + (key - Keys.D0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            >= Keys.NumPad0 and <= Keys.NumPad9 => "IF_" + (key - Keys.NumPad0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Keys.Escape => "IF_ESC",
            Keys.Enter => "IF_ENTER",
            Keys.Space => "IF_SPACE",
            Keys.Tab => "IF_TAB",
            Keys.Back => "IF_BKSP",
            Keys.ControlKey or Keys.LControlKey or Keys.RControlKey => "IF_CTRL",
            Keys.OemQuotes => "IF_APO",
            Keys.OemMinus or Keys.Subtract => "IF_MINUS",
            Keys.Oemplus or Keys.Add => "IF_PLUS",
            Keys.OemOpenBrackets => "IF_BRACKL",
            Keys.OemCloseBrackets => "IF_BRACKR",
            Keys.Up => "IF_CUU",
            Keys.Down => "IF_CUD",
            Keys.Left => "IF_CUL",
            Keys.Right => "IF_CUR",
            Keys.Insert => "IF_INS",
            Keys.PageUp => "IF_PGUP",
            Keys.PageDown => "IF_PGDN",
            _ => null,
        };

    private void RenderSurface_MouseDown(object? sender, MouseEventArgs e)
    {
        UpdateMouseRuntimeState(e.Location);
        _mouseButtonsDown.Add(e.Button);
        UpdateRuntimeInputSkills();
        if (e.Button == MouseButtons.Left)
        {
            _renderSurface.Focus();
            TriggerRuntimeMouseHook("IF_LEFT");
            if (!TryTriggerUiKlick(e.Location) && !TryTriggerSpriteKlick(e.Location))
            {
                TriggerRuntimeMouseHook("IF_KLICK");
            }
        }
        else if (e.Button == MouseButtons.Right)
        {
            TriggerRuntimeMouseHook("IF_RIGHT");
        }
        else if (e.Button == MouseButtons.Middle)
        {
            TriggerRuntimeMouseHook("IF_MIDDLE");
        }
    }

    private void UpdateMouseRuntimeState(Point point)
    {
        if (_runtime is null)
        {
            return;
        }

        if (_runtime.GetSkill("MOUSE_MODE") <= 0)
        {
            Vector2 virtualPoint = VirtualPoint(point);
            _runtime.SetSkill("MOUSE_X", virtualPoint.X);
            _runtime.SetSkill("MOUSE_Y", virtualPoint.Y);
        }
    }

    private void TriggerRuntimeMouseHook(string hookName)
    {
        if (_handlingInputRequest || _runtime is null)
        {
            return;
        }

        SyncPlayerRuntimeState();
        _runtime.TriggerActionHook(hookName);
        HandleRuntimeHostRequests();
    }

    private bool TryTriggerUiKlick(Point point)
    {
        if (_handlingInputRequest || _runtime is null)
        {
            return false;
        }

        Dictionary<string, UiBitmapSize> bitmapSizes = _uiImages.ToDictionary(
            pair => pair.Key,
            pair => new UiBitmapSize(pair.Value.Width, pair.Value.Height),
            StringComparer.OrdinalIgnoreCase);
        Vector2 virtualPoint = PointerVirtualPoint(point);
        UiHitTarget? target = UiHitTester.HitTest(
            _level,
            virtualPoint.X,
            virtualPoint.Y,
            bitmapSizes,
            _runtime.UiElements,
            _runtime.Skills,
            _runtime.Strings);
        if (target is not { } hit)
        {
            return false;
        }

        if (hit.Button is { } button)
        {
            _pressedUiButton = button;
            _pressedUiButtonReleaseAction = hit.ReleaseAction;
        }

        if (hit.Slider is { } slider)
        {
            _draggedUiSlider = slider;
            _sliderGrabOffset = slider.Vertical ? virtualPoint.Y - hit.Y : virtualPoint.X - hit.X;
        }

        if (hit.Action is { } action)
        {
            SyncPlayerRuntimeState();
            _runtime.RunAction(action);
            HandleRuntimeHostRequests();
        }

        return true;
    }

    private bool TryTriggerSpriteKlick(Point point)
    {
        if (_handlingInputRequest || _runtime is null || _sprites.Count == 0)
        {
            return false;
        }

        point = PointerPhysicalPoint(point);
        VirtualDisplayLayout display = DisplayLayout(RenderSize());
        DisplayRectangle viewport = WorldViewport(display);
        ProjectionModel projection = CurrentProjection();
        float pixelScaleX = viewport.Width / projection.ScreenWidth;
        float pixelScaleY = viewport.Height / projection.ScreenHeight;
        Vector3 cameraPosition = _camera.Position;
        Vector3 forward = _camera.LevelForward;
        Vector3 right = _camera.Right;
        Vector3 up = Vector3.UnitY;

        IEnumerable<SpriteBillboard> clickableSprites = SpriteBuilder.SelectClickableLayers(_sprites)
            .OrderBy(sprite => Vector3.DistanceSquared(sprite.BasePosition, cameraPosition));
        foreach (SpriteBillboard sprite in clickableSprites)
        {
            if (sprite.Source is null ||
                !sprite.Source.TryGetString("IF_KLICK", out string? actionName) ||
                string.IsNullOrWhiteSpace(actionName))
            {
                continue;
            }

            Vector3 center = sprite.BasePosition +
                (right * sprite.CenterOffset.X) +
                (up * (sprite.HalfHeight + sprite.CenterOffset.Y));
            Vector3 toCenter = center - cameraPosition;
            float distance = Vector3.Dot(toCenter, forward);
            if (distance <= 1f)
            {
                continue;
            }

            float projectedX = Vector3.Dot(toCenter, right);
            float projectedY = Vector3.Dot(toCenter, up);
            (double windowX, double windowY) = projection.Project(distance, projectedX, projectedY);
            float screenX = viewport.X + ((float)windowX * pixelScaleX);
            float screenY = viewport.Y + ((float)windowY * pixelScaleY);
            float halfWidth = (float)(projection.FocalLengthX * sprite.HalfWidth / distance) * pixelScaleX;
            float halfHeight = (float)(projection.FocalLengthY * sprite.HalfHeight / distance) * pixelScaleY;

            if (point.X < screenX - halfWidth ||
                point.X > screenX + halfWidth ||
                point.Y < screenY - halfHeight ||
                point.Y > screenY + halfHeight ||
                !IsOpaqueSpritePixel(sprite, point, screenX, screenY, halfWidth, halfHeight))
            {
                continue;
            }

            SyncPlayerRuntimeState();
            if (_runtime.TriggerObjectActionHook(sprite.Source, "IF_KLICK"))
            {
                HandleRuntimeHostRequests();
                return true;
            }
        }

        return false;
    }

    private bool IsOpaqueSpritePixel(
        SpriteBillboard sprite,
        Point point,
        float screenX,
        float screenY,
        float halfWidth,
        float halfHeight)
    {
        if (!_textureImages.TryGetValue(sprite.Texture, out TextureImage image) || halfWidth <= 0 || halfHeight <= 0)
        {
            return true;
        }

        float u = (point.X - (screenX - halfWidth)) / (halfWidth * 2);
        float v = (point.Y - (screenY - halfHeight)) / (halfHeight * 2);
        return SpriteBuilder.IsOpaque(image, u, v, sprite.Mirror);
    }

    private void RenderSurface_MouseUp(object? sender, MouseEventArgs e)
    {
        UpdateMouseRuntimeState(e.Location);
        _mouseButtonsDown.Remove(e.Button);
        UpdateRuntimeInputSkills();
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_draggedUiSlider is not null)
        {
            UpdateDraggedUiSlider(e.Location);
            _draggedUiSlider = null;
            _sliderGrabOffset = 0;
        }

        if (_pressedUiButton is null)
        {
            return;
        }

        string? releaseAction = _pressedUiButtonReleaseAction;
        _pressedUiButton = null;
        _pressedUiButtonReleaseAction = null;
        if (releaseAction is null || _handlingInputRequest || _runtime is null)
        {
            return;
        }

        SyncPlayerRuntimeState();
        _runtime.RunAction(releaseAction);
        HandleRuntimeHostRequests();
    }

    private void RenderSurface_MouseMove(object? sender, MouseEventArgs e)
    {
        RecordMouseMotion(e.Location);
        UpdateMouseRuntimeState(e.Location);
        if (_draggedUiSlider is not null)
        {
            UpdateDraggedUiSlider(e.Location);
            return;
        }

    }

    private void UpdateDraggedUiSlider(Point point)
    {
        if (_draggedUiSlider is not { } slider || _runtime is null)
        {
            return;
        }

        Vector2 virtualPoint = VirtualPoint(point);
        float pointer = slider.Vertical ? virtualPoint.Y : virtualPoint.X;
        float start = slider.Vertical ? slider.StartY : slider.StartX;
        _runtime.SetSkill(slider.Skill, slider.ValueAtOffset(pointer - _sliderGrabOffset - start));
    }

    private void PlaceCameraAtStart()
    {
        // The player owns position (floor-following physics); the camera is its eye.
        _player = new Player(_level);
        SpawnInLargestRoom(); // fallback only for synthetic/debug maps without a PLAYER_START
        _runtime.RegisterObject("player", _player);
        _camera.Position = RenderSpace.FromMap(_player.Position);
        if (_level.PlayerStart is { } start)
        {
            // The WMP start angle is the initial PLAYER_ANGLE (0 = map +x); the runtime seeded
            // the skill from the same value, so the start sync below reports it unchanged.
            _camera.Yaw = (float)RenderSpace.YawFromPlayerAngle(start.AngleRadians);
        }

        SyncPlayerRuntimeState();
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

            bounds[region] = bounds.TryGetValue(region, out var b)
                ? (MathF.Min(b.MinX, vx), MathF.Min(b.MinZ, vz), MathF.Max(b.MaxX, vx), MathF.Max(b.MaxZ, vz))
                : (vx, vz, vx, vz);
        }

        foreach (var wall in _level.Walls)
        {
            foreach (int vi in new[] { wall.Vertex1, wall.Vertex2 })
            {
                if (vi >= 0 && vi < _level.Vertices.Count)
                {
                    var v = _level.Vertices[vi];
                    Accumulate(wall.LeftRegion, v.X, v.Y);
                    Accumulate(wall.RightRegion, v.X, v.Y);
                }
            }
        }

        int best = -1;
        float bestArea = 0, cx = 0, cz = 0;
        foreach ((int region, var b) in bounds)
        {
            var r = _level.Regions[region];
            if (r.CeilHeight - r.FloorHeight < 5)
            {
                continue; // skip solid/thin regions
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

        UpdateStatus();
    }

    private void InitializeDirect3D()
    {
        D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out _device,
            out _context).CheckError();

        using IDXGIFactory2 factory = CreateDXGIFactory1<IDXGIFactory2>();
        var description = new SwapChainDescription1
        {
            Width = (uint)Math.Max(1, _renderSurface.ClientSize.Width),
            Height = (uint)Math.Max(1, _renderSurface.ClientSize.Height),
            Format = Format.B8G8R8A8_UNorm,
            BufferCount = 2,
            BufferUsage = Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.FlipDiscard,
            SampleDescription = new SampleDescription(1, 0),
            Scaling = Scaling.Stretch,
            AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
        };
        _swapChain = factory.CreateSwapChainForHwnd(_device, _renderSurface.Handle, description);

        CreateSizedResources();

        _rasterizer = _device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid)
        {
            DepthClipEnable = true,
        });
        _depthState = _device.CreateDepthStencilState(DepthStencilDescription.Default);
        _sampler = _device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Wrap,
            AddressV = TextureAddressMode.Wrap,
            AddressW = TextureAddressMode.Wrap,
            MaxLOD = float.MaxValue,
        });

        ReadOnlyMemory<byte> vsBytecode = Compiler.Compile(ShaderSource, "VSMain", "level.hlsl", "vs_5_0");
        ReadOnlyMemory<byte> psBytecode = Compiler.Compile(ShaderSource, "PSMain", "level.hlsl", "ps_5_0");
        _vertexShader = _device.CreateVertexShader(vsBytecode.Span);
        _pixelShader = _device.CreatePixelShader(psBytecode.Span);
        _inputLayout = _device.CreateInputLayout(
            [
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 12, 0),
                new InputElementDescription("TEXCOORD", 1, Format.R32_Float, 20, 0),
            ],
            vsBytecode.Span);

        _constantBuffer = _device.CreateBuffer(
            new BufferDescription((uint)Marshal.SizeOf<FrameConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Default));
    }

    private void CreateSizedResources()
    {
        System.Drawing.Size renderSize = RenderSize();
        using ID3D11Texture2D backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTarget = _device.CreateRenderTargetView(backBuffer);

        _depthTexture = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)renderSize.Width,
            Height = (uint)renderSize.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.D32_Float,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.DepthStencil,
        });
        _depthView = _device.CreateDepthStencilView(_depthTexture);
    }

    private void ResizeBuffers()
    {
        if (_swapChain is null || _device is null)
        {
            return;
        }

        System.Drawing.Size renderSize = RenderSize();
        _renderTarget.Dispose();
        _depthView.Dispose();
        _depthTexture.Dispose();
        _swapChain.ResizeBuffers(2, (uint)renderSize.Width, (uint)renderSize.Height, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
        CreateSizedResources();
    }

    private void RebuildWorldResources()
    {
        DisposeWorldResources();
        LoadTextures();
        BuildGeometry();
        BuildSprites();
    }

    private void RebuildGeometryResources()
    {
        _vertexBuffer?.Dispose();
        _vertexBuffer = null;
        _batches.Clear();
        _vertexCount = 0;
        BuildGeometry();
        BuildSprites();
    }

    private void DisposeWorldResources()
    {
        _flicView?.Dispose();
        _flicView = null;
        foreach (ID3D11ShaderResourceView view in _textureViews.Values)
        {
            view.Dispose();
        }

        _textureViews.Clear();
        foreach (ID3D11ShaderResourceView view in _uiTextureViews.Values)
        {
            view.Dispose();
        }

        _uiTextureViews.Clear();
        foreach (ID3D11ShaderResourceView view in _fontTextureViews.Values)
        {
            view.Dispose();
        }

        _fontTextureViews.Clear();
        _defaultView?.Dispose();
        _defaultView = null;
        _vertexBuffer?.Dispose();
        _vertexBuffer = null;
        _batches.Clear();
        _sprites.Clear();
        _vertexCount = 0;
    }

    private void LoadTextures()
    {
        _defaultView = UploadTexture(new TextureImage(1, 1, [255, 255, 255, 255]));
        foreach ((string name, TextureImage image) in _textureImages)
        {
            try
            {
                _textureViews[name] = UploadTexture(image);
            }
            catch
            {
                // Skip a texture that fails to upload rather than failing the whole view.
            }
        }

        foreach ((string name, TextureImage image) in _uiImages)
        {
            try
            {
                _uiTextureViews[name] = UploadTexture(image);
            }
            catch
            {
                // UI work should not prevent the 3D level from opening.
            }
        }

        foreach ((string name, TextureImage image) in _fontImages)
        {
            try
            {
                _fontTextureViews[name] = UploadTexture(image);
            }
            catch
            {
                // Missing text should not prevent the 3D level from opening.
            }
        }
    }

    private ID3D11ShaderResourceView UploadTexture(TextureImage image)
    {
        var description = new Texture2DDescription
        {
            Width = (uint)image.Width,
            Height = (uint)image.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.ShaderResource,
        };

        unsafe
        {
            fixed (byte* pixels = image.Rgba)
            {
                var data = new SubresourceData((nint)pixels, (uint)(image.Width * 4));
                using ID3D11Texture2D texture = _device.CreateTexture2D(description, [data]);
                return _device.CreateShaderResourceView(texture);
            }
        }
    }

    private void BuildGeometry()
    {
        LevelMesh mesh = MeshBuilder.Build(_level);
        var vertices = new List<RenderVertex>();
        foreach (MeshBatch batch in mesh.Batches)
        {
            _batches.Add((
                vertices.Count,
                batch.Vertices.Count,
                batch.Texture,
                TextureAmbient(batch.Texture),
                batch.AnimationSource,
                batch.AlphaTest));
            vertices.AddRange(batch.Vertices);
        }

        _vertexCount = vertices.Count;
        if (_vertexCount == 0)
        {
            return;
        }

        _vertexBuffer = _device.CreateBuffer(
            vertices.ToArray().AsSpan(),
            new BufferDescription((uint)(_vertexCount * Marshal.SizeOf<RenderVertex>()), BindFlags.VertexBuffer, ResourceUsage.Immutable));
    }

    private void BuildSprites()
    {
        _sprites.Clear();
        // Directional sides are picked in map space, so hand over the player's map (x, y).
        _sprites.AddRange(SpriteBuilder.Build(
            _level,
            new Vector2(_player.Position.X, _player.Position.Z)));
    }

    private void RenderSprites(Matrix4x4 viewProjection)
    {
        if (_sprites.Count == 0 || _defaultView is null)
        {
            return;
        }

        // Cylindrical billboards: face the camera horizontally, stay upright.
        Vector3 right = _camera.Right;
        Vector3 up = Vector3.UnitY;
        Vector3 horizontalForward = _camera.LevelForward;

        IReadOnlyList<SpriteBillboard> sorted = SpriteBuilder.OrderBackToFront(_sprites, _camera.Position);
        for (int start = 0; start < sorted.Count;)
        {
            string texture = sorted[start].Texture;
            int end = start + 1;
            while (end < sorted.Count && sorted[end].Texture.Equals(texture, StringComparison.OrdinalIgnoreCase))
            {
                end++;
            }

            ApplyFrameConstants(viewProjection, TextureAmbient(texture), alphaTest: true);
            var vertices = new List<RenderVertex>((end - start) * 6);
            for (int i = start; i < end; i++)
            {
                SpriteBillboard billboard = sorted[i];
                Vector3 r = right * billboard.HalfWidth;
                Vector3 u;
                Vector3 center;
                if (billboard.Orientation == SpriteOrientation.Ground)
                {
                    u = horizontalForward * billboard.HalfHeight;
                    center = billboard.BasePosition +
                        (right * billboard.CenterOffset.X) +
                        (horizontalForward * billboard.CenterOffset.Y);
                }
                else
                {
                    u = up * billboard.HalfHeight;
                    center = billboard.BasePosition +
                        (horizontalForward * billboard.CameraDepthOffset) +
                        (right * billboard.CenterOffset.X) +
                        (up * (billboard.HalfHeight + billboard.CenterOffset.Y));
                }

                Vector3 bottomLeft = center - r - u;
                Vector3 bottomRight = center + r - u;
                Vector3 topRight = center + r + u;
                Vector3 topLeft = center - r + u;

                float leftU = billboard.Mirror ? 1 : 0;
                float rightU = billboard.Mirror ? 0 : 1;
                float localAmbient = billboard.Source is { } source &&
                    source.Region >= 0 && source.Region < _level.Regions.Count
                    ? (float)_level.Regions[source.Region].Ambient
                    : 0;
                vertices.Add(new RenderVertex(bottomLeft.X, bottomLeft.Y, bottomLeft.Z, leftU, 1, localAmbient));
                vertices.Add(new RenderVertex(bottomRight.X, bottomRight.Y, bottomRight.Z, rightU, 1, localAmbient));
                vertices.Add(new RenderVertex(topRight.X, topRight.Y, topRight.Z, rightU, 0, localAmbient));
                vertices.Add(new RenderVertex(bottomLeft.X, bottomLeft.Y, bottomLeft.Z, leftU, 1, localAmbient));
                vertices.Add(new RenderVertex(topRight.X, topRight.Y, topRight.Z, rightU, 0, localAmbient));
                vertices.Add(new RenderVertex(topLeft.X, topLeft.Y, topLeft.Z, leftU, 0, localAmbient));
            }

            using ID3D11Buffer buffer = _device.CreateBuffer(
                vertices.ToArray().AsSpan(),
                new BufferDescription((uint)(vertices.Count * Marshal.SizeOf<RenderVertex>()), BindFlags.VertexBuffer, ResourceUsage.Immutable));

            ID3D11ShaderResourceView view = _textureViews.TryGetValue(texture, out ID3D11ShaderResourceView? found)
                ? found
                : _defaultView;

            _context.IASetVertexBuffer(0, buffer, (uint)Marshal.SizeOf<RenderVertex>());
            _context.PSSetShaderResource(0, view);
            _context.Draw((uint)vertices.Count, 0);
            start = end;
        }
    }

    private void RenderUiOverlays(VirtualDisplayLayout display)
    {
        if (_defaultView is null)
        {
            return;
        }

        Dictionary<string, UiBitmapSize> bitmapSizes = _uiImages.ToDictionary(
            pair => pair.Key,
            pair => new UiBitmapSize(pair.Value.Width, pair.Value.Height),
            StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, WdlUiElement>? uiElements = _runtime?.UiElements;
        IReadOnlyList<UiOverlaySprite> overlays = UiOverlayBuilder.Build(_level, uiElements, _runtime?.Skills);
        IReadOnlyList<UiOverlaySprite> dynamicWidgets = _runtime is not null
            ? UiOverlayBuilder.BuildPanelWidgets(_level, _runtime.Skills, bitmapSizes, uiElements, _pressedUiButton)
            : [];
        if (overlays.Count == 0 && dynamicWidgets.Count == 0)
        {
            return;
        }

        Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(
            0,
            display.VirtualWidth,
            display.VirtualHeight,
            0,
            0,
            1);
        ApplyFrameConstants(projection, 1, alphaTest: true);
        _context.OMSetRenderTargets(_renderTarget, null);
        DisplayRectangle viewport = display.PhysicalDisplay;
        _context.RSSetViewport(new Viewport(viewport.X, viewport.Y, viewport.Width, viewport.Height, 0, 1));

        foreach (UiOverlaySprite overlay in overlays.Concat(dynamicWidgets))
        {
            TextureImage? image = _uiImages.TryGetValue(overlay.Bitmap, out TextureImage foundImage)
                ? foundImage
                : null;
            float width = overlay.Width > 0 ? overlay.Width : image?.Width ?? 0;
            float height = overlay.Height > 0 ? overlay.Height : image?.Height ?? 0;
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            float x = overlay.X;
            float y = overlay.Y;
            float right = x + width;
            float bottom = y + height;
            float u0 = 0;
            float v0 = 0;
            float u1 = 1;
            float v1 = 1;
            if (image is not null && overlay.SourceWidth > 0 && overlay.SourceHeight > 0)
            {
                TextureImage source = image.Value;
                u0 = (float)Math.Clamp(overlay.SourceX, 0, source.Width) / source.Width;
                v0 = (float)Math.Clamp(overlay.SourceY, 0, source.Height) / source.Height;
                u1 = (float)Math.Clamp(overlay.SourceX + overlay.SourceWidth, 0, source.Width) / source.Width;
                v1 = (float)Math.Clamp(overlay.SourceY + overlay.SourceHeight, 0, source.Height) / source.Height;
            }

            RenderVertex[] vertices =
            [
                new(x, bottom, 0, u0, v1),
                new(right, bottom, 0, u1, v1),
                new(right, y, 0, u1, v0),
                new(x, bottom, 0, u0, v1),
                new(right, y, 0, u1, v0),
                new(x, y, 0, u0, v0),
            ];

            using ID3D11Buffer buffer = _device.CreateBuffer(
                vertices.AsSpan(),
                new BufferDescription((uint)(vertices.Length * Marshal.SizeOf<RenderVertex>()), BindFlags.VertexBuffer, ResourceUsage.Immutable));

            ID3D11ShaderResourceView view = _uiTextureViews.TryGetValue(overlay.Bitmap, out ID3D11ShaderResourceView? found)
                ? found
                : _defaultView;

            _context.IASetVertexBuffer(0, buffer, (uint)Marshal.SizeOf<RenderVertex>());
            _context.PSSetShaderResource(0, view);
            _context.Draw((uint)vertices.Length, 0);
        }
    }

    private void RenderUiText(VirtualDisplayLayout display)
    {
        if (_fontTextureViews.Count == 0 || _defaultView is null || _runtime is null)
        {
            return;
        }

        IReadOnlyList<UiTextGlyph> glyphs = UiTextBuilder.Build(_level, _runtime.Strings, _runtime.Skills, _runtime.UiElements);
        if (glyphs.Count == 0)
        {
            return;
        }

        Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(
            0,
            display.VirtualWidth,
            display.VirtualHeight,
            0,
            0,
            1);
        ApplyFrameConstants(projection, 1, alphaTest: true);
        _context.OMSetRenderTargets(_renderTarget, null);
        DisplayRectangle viewport = display.PhysicalDisplay;
        _context.RSSetViewport(new Viewport(viewport.X, viewport.Y, viewport.Width, viewport.Height, 0, 1));

        foreach (IGrouping<string, UiTextGlyph> group in glyphs.GroupBy(glyph => glyph.Font, StringComparer.OrdinalIgnoreCase))
        {
            if (!_fontImages.TryGetValue(group.Key, out TextureImage image) ||
                !_fontTextureViews.TryGetValue(group.Key, out ID3D11ShaderResourceView? view))
            {
                continue;
            }

            var vertices = new List<RenderVertex>();
            foreach (UiTextGlyph glyph in group)
            {
                if (glyph.Width <= 0 || glyph.Height <= 0)
                {
                    continue;
                }

                int columns = Math.Max(1, image.Width / glyph.Width);
                int rows = Math.Max(1, image.Height / glyph.Height);
                int glyphCount = columns * rows;
                int code = FontAtlasIndex(glyph.CodePoint, glyphCount);
                if (code < 0 || code >= glyphCount)
                {
                    continue;
                }

                int sourceOffsetX = Math.Clamp(glyph.SourceX, 0, glyph.Width);
                int sourceOffsetY = Math.Clamp(glyph.SourceY, 0, glyph.Height);
                int sourceWidth = glyph.SourceWidth > 0 ? Math.Min(glyph.SourceWidth, glyph.Width - sourceOffsetX) : glyph.Width - sourceOffsetX;
                int sourceHeight = glyph.SourceHeight > 0 ? Math.Min(glyph.SourceHeight, glyph.Height - sourceOffsetY) : glyph.Height - sourceOffsetY;
                int drawWidth = glyph.DrawWidth > 0 ? glyph.DrawWidth : sourceWidth;
                int drawHeight = glyph.DrawHeight > 0 ? glyph.DrawHeight : sourceHeight;
                if (sourceWidth <= 0 || sourceHeight <= 0 || drawWidth <= 0 || drawHeight <= 0)
                {
                    continue;
                }

                int sourceX = (code % columns * glyph.Width) + sourceOffsetX;
                int sourceY = (code / columns * glyph.Height) + sourceOffsetY;
                float u0 = (float)sourceX / image.Width;
                float v0 = (float)sourceY / image.Height;
                float u1 = (float)(sourceX + sourceWidth) / image.Width;
                float v1 = (float)(sourceY + sourceHeight) / image.Height;

                float x = glyph.X;
                float y = glyph.Y;
                float right = x + drawWidth;
                float bottom = y + drawHeight;
                vertices.Add(new RenderVertex(x, bottom, 0, u0, v1));
                vertices.Add(new RenderVertex(right, bottom, 0, u1, v1));
                vertices.Add(new RenderVertex(right, y, 0, u1, v0));
                vertices.Add(new RenderVertex(x, bottom, 0, u0, v1));
                vertices.Add(new RenderVertex(right, y, 0, u1, v0));
                vertices.Add(new RenderVertex(x, y, 0, u0, v0));
            }

            if (vertices.Count == 0)
            {
                continue;
            }

            using ID3D11Buffer buffer = _device.CreateBuffer(
                vertices.ToArray().AsSpan(),
                new BufferDescription((uint)(vertices.Count * Marshal.SizeOf<RenderVertex>()), BindFlags.VertexBuffer, ResourceUsage.Immutable));

            _context.IASetVertexBuffer(0, buffer, (uint)Marshal.SizeOf<RenderVertex>());
            _context.PSSetShaderResource(0, view);
            _context.Draw((uint)vertices.Count, 0);
        }
    }

    private static int FontAtlasIndex(int codePoint, int glyphCount)
    {
        if (glyphCount == 11)
        {
            if (codePoint >= '0' && codePoint <= '9')
            {
                return codePoint - '0';
            }

            return codePoint == ' ' ? 10 : -1;
        }

        return codePoint;
    }

    private void UpdateCamera()
    {
        // Input feeds the documented FORCE_* skills; the level's movement action owns velocity.
        UpdateRuntimeMouseMotion();
        UpdateRuntimeInputSkills(includeMouse: true);
        SyncPlayerRuntimeState();
        _camera.Position = RenderSpace.FromMap(_player.Position);
    }

    private void SyncPlayerRuntimeState()
    {
        if (_runtime is null || _player is null)
        {
            return;
        }

        Vector3 position = _player.Position;
        WdlPlayerMotion.Synchronize(_runtime, _player);

        double runtimeAngle = RenderSpace.PlayerAngleFromYaw(_camera.Yaw);
        _runtime.SetSkill("PLAYER_ANGLE", runtimeAngle);
        _runtime.SetSkill("PLAYER_SIN", Math.Sin(runtimeAngle));
        _runtime.SetSkill("PLAYER_COS", Math.Cos(runtimeAngle));
        double runtimeTilt = Math.Tan(_camera.Pitch);
        _runtime.SetSkill("PLAYER_TILT", runtimeTilt);
        _runtime.SetSkill("MOUSE_ANGLE", runtimeAngle);
        RememberPlayerRuntimeSnapshot(position.X, position.Z, position.Y, _player.Region, runtimeAngle, runtimeTilt);
    }

    private void UpdateRuntimeInputSkills(bool includeMouse = false)
    {
        if (_runtime is null)
        {
            return;
        }

        for (Keys key = Keys.F1; key <= Keys.F12; key++)
        {
            SetRuntimeKeySkill("KEY_F" + (key - Keys.F1 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), key);
        }

        for (Keys key = Keys.A; key <= Keys.Z; key++)
        {
            SetRuntimeKeySkill("KEY_" + (char)('A' + key - Keys.A), key);
        }

        for (Keys key = Keys.D0; key <= Keys.D9; key++)
        {
            string name = "KEY_" + (key - Keys.D0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _runtime.SetSkill(name, IsKeyDown(key) || IsKeyDown(Keys.NumPad0 + (key - Keys.D0)) ? 1 : 0);
        }

        SetRuntimeKeySkill("KEY_ESC", Keys.Escape);
        SetRuntimeKeySkill("KEY_TAB", Keys.Tab);
        SetRuntimeKeySkill("KEY_SHIFT", Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey);
        SetRuntimeKeySkill("KEY_CTRL", Keys.ControlKey, Keys.LControlKey, Keys.RControlKey);
        SetRuntimeKeySkill("KEY_ALT", Keys.Menu, Keys.LMenu, Keys.RMenu);
        SetRuntimeKeySkill("KEY_SPACE", Keys.Space);
        SetRuntimeKeySkill("KEY_BKSP", Keys.Back);
        SetRuntimeKeySkill("KEY_CUU", Keys.Up);
        SetRuntimeKeySkill("KEY_CUD", Keys.Down);
        SetRuntimeKeySkill("KEY_CUR", Keys.Right);
        SetRuntimeKeySkill("KEY_CUL", Keys.Left);
        SetRuntimeKeySkill("KEY_PGUP", Keys.PageUp);
        SetRuntimeKeySkill("KEY_PGDN", Keys.PageDown);
        SetRuntimeKeySkill("KEY_HOME", Keys.Home);
        SetRuntimeKeySkill("KEY_END", Keys.End);
        SetRuntimeKeySkill("KEY_INS", Keys.Insert);
        SetRuntimeKeySkill("KEY_DEL", Keys.Delete);
        SetRuntimeKeySkill("KEY_PAUSE", Keys.Pause);
        SetRuntimeKeySkill("KEY_PLUS", Keys.Oemplus, Keys.Add);
        SetRuntimeKeySkill("KEY_MINUS", Keys.OemMinus, Keys.Subtract);
        SetRuntimeKeySkill("KEY_ENTER", Keys.Enter);
        _runtime.SetSkill("KEY_ANY", _keysDown.Count > 0 ? 1 : 0);

        _runtime.SetSkill("MOUSE_LEFT", _mouseButtonsDown.Contains(MouseButtons.Left) ? 1 : 0);
        _runtime.SetSkill("MOUSE_MIDDLE", _mouseButtonsDown.Contains(MouseButtons.Middle) ? 1 : 0);
        _runtime.SetSkill("MOUSE_RIGHT", _mouseButtonsDown.Contains(MouseButtons.Right) ? 1 : 0);

        double sensitivity = _runtime.GetSkill("KEY_SENSE");
        if (IsKeyDown(Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey))
        {
            sensitivity *= _runtime.GetSkill("SHIFT_SENSE");
        }

        int ahead = (IsKeyDown(Keys.Up, Keys.W) ? 1 : 0) - (IsKeyDown(Keys.Down, Keys.S) ? 1 : 0);
        int strafe = (IsKeyDown(Keys.OemPeriod, Keys.D) ? 1 : 0) - (IsKeyDown(Keys.Oemcomma, Keys.A) ? 1 : 0);
        int vertical = (IsKeyDown(Keys.Home, Keys.Space) ? 1 : 0) - (IsKeyDown(Keys.End) ? 1 : 0);
        double mouseRotation = 0;
        double mouseTilt = 0;
        if (includeMouse)
        {
            double mouseSensitivity = _runtime.GetSkill("MOUSE_SENSE");
            double mouseMode = _runtime.GetSkill("MOUSE_MODE");
            mouseRotation = WdlInputForces.MouseAxis(_frameMickey.X, mouseSensitivity, mouseMode);
            mouseTilt = WdlInputForces.MouseAxis(-_frameMickey.Y, mouseSensitivity, mouseMode);
        }

        _runtime.SetSkill("FORCE_AHEAD", sensitivity * ahead);
        _runtime.SetSkill("FORCE_STRAFE", sensitivity * strafe);
        _runtime.SetSkill(
            "FORCE_ROT",
            WdlInputForces.Combine(sensitivity * Axis(Keys.Left, Keys.Right), mouseRotation));
        _runtime.SetSkill(
            "FORCE_TILT",
            WdlInputForces.Combine(sensitivity * Axis(Keys.PageUp, Keys.PageDown), mouseTilt));
        _runtime.SetSkill("FORCE_UP", sensitivity * vertical);
    }

    private void RecordMouseMotion(Point point)
    {
        if (_hasMouseSample)
        {
            VirtualDisplayLayout layout = DisplayLayout(RenderSize());
            _pendingMickey += new Vector2(
                (point.X - _lastMouseSample.X) / Math.Max(0.001f, layout.ScaleX),
                (point.Y - _lastMouseSample.Y) / Math.Max(0.001f, layout.ScaleY));
        }

        _lastMouseSample = point;
        _hasMouseSample = true;
    }

    private void UpdateRuntimeMouseMotion()
    {
        _frameMickey = _pendingMickey;
        _pendingMickey = Vector2.Zero;
        _runtime.SetSkill("MICKEY_X", _frameMickey.X);
        _runtime.SetSkill("MICKEY_Y", _frameMickey.Y);

        _mouseMotionWindow += _frameMickey;
        _mouseMotionTicks += Math.Max(0, _lastTimeCorrection);
        double calm = Math.Max(0, _runtime.GetSkill("MOUSE_CALM"));
        double sampleTicks = Math.Max(0.001, _runtime.GetSkill("MOUSE_TIME"));
        bool moving = _mouseMotionWindow.Length() > calm;
        if (moving)
        {
            _mouseMotionWindow = Vector2.Zero;
            _mouseMotionTicks = 0;
        }
        else if (_mouseMotionTicks < sampleTicks)
        {
            moving = _mouseWasMoving;
        }
        else
        {
            _mouseMotionWindow = Vector2.Zero;
            _mouseMotionTicks = 0;
        }

        _runtime.SetSkill("MOUSE_MOVING", moving ? 1 : 0);
        if (_mouseWasMoving && !moving && _runtime.GetSkill("MOUSE_MODE") > 0)
        {
            _runtime.TriggerActionHook("IF_MSTOP");
            HandleRuntimeHostRequests();
        }

        _mouseWasMoving = moving;
    }

    private void HideSystemCursor()
    {
        if (_systemCursorHidden)
        {
            return;
        }

        Cursor.Hide();
        _systemCursorHidden = true;
    }

    private void ResetMouseSampleAndShowCursor()
    {
        _hasMouseSample = false;
        ShowSystemCursor();
    }

    private void ShowSystemCursor()
    {
        if (!_systemCursorHidden)
        {
            return;
        }

        Cursor.Show();
        _systemCursorHidden = false;
    }

    private void HideCursorWhenPointerIsOverSurface()
    {
        Point point = _renderSurface.PointToClient(Cursor.Position);
        if (_renderSurface.ClientRectangle.Contains(point))
        {
            HideSystemCursor();
        }
    }

    private Vector2 PointerVirtualPoint(Point physicalPoint) =>
        _runtime is not null && _runtime.GetSkill("MOUSE_MODE") > 0
            ? new Vector2((float)_runtime.GetSkill("MOUSE_X"), (float)_runtime.GetSkill("MOUSE_Y"))
            : VirtualPoint(physicalPoint);

    private Point PointerPhysicalPoint(Point physicalPoint)
    {
        if (_runtime is null || _runtime.GetSkill("MOUSE_MODE") <= 0)
        {
            return physicalPoint;
        }

        VirtualDisplayLayout display = DisplayLayout(RenderSize());
        DisplayRectangle point = display.ToPhysical(
            (float)_runtime.GetSkill("MOUSE_X"),
            (float)_runtime.GetSkill("MOUSE_Y"),
            0,
            0);
        return new Point((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    private void SetRuntimeKeySkill(string name, params Keys[] keys) =>
        _runtime.SetSkill(name, IsKeyDown(keys) ? 1 : 0);

    private bool IsKeyDown(params Keys[] keys) => keys.Any(_keysDown.Contains);

    private int Axis(Keys positive, Keys negative) =>
        (IsKeyDown(positive) ? 1 : 0) - (IsKeyDown(negative) ? 1 : 0);

    private void ApplyRuntimePlayerState()
    {
        if (_runtime is null || _player is null)
        {
            return;
        }

        if (!_hasPlayerRuntimeSnapshot)
        {
            SyncPlayerRuntimeState();
            return;
        }

        double playerX = _runtime.GetSkill("PLAYER_X");
        double playerY = _runtime.GetSkill("PLAYER_Y");
        double playerZ = _runtime.GetSkill("PLAYER_Z");
        double playerRegion = _runtime.GetSkill("PLAYER_REGION");
        double playerAngle = NormalizeAngle(_runtime.GetSkill("PLAYER_ANGLE"));
        double playerTilt = _runtime.GetSkill("PLAYER_TILT");

        bool positionChanged =
            !NearlyEqual(playerX, _lastPlayerX) ||
            !NearlyEqual(playerY, _lastPlayerY) ||
            !NearlyEqual(playerZ, _lastPlayerZ) ||
            !NearlyEqual(playerRegion, _lastPlayerRegion);
        bool angleChanged = !NearlyEqual(playerAngle, _lastPlayerAngle);
        bool tiltChanged = !NearlyEqual(playerTilt, _lastPlayerTilt);

        if (positionChanged)
        {
            _player["region"] = Math.Round(playerRegion);
            _player.Position = new Vector3((float)playerX, (float)playerZ, (float)playerY);
        }

        if (angleChanged)
        {
            _camera.Yaw = (float)RenderSpace.YawFromPlayerAngle(playerAngle);
        }

        if (tiltChanged)
        {
            _camera.Pitch = (float)Math.Atan(playerTilt);
        }

        _camera.Position = RenderSpace.FromMap(_player.Position);
        SyncPlayerRuntimeState();
    }

    private void RememberPlayerRuntimeSnapshot(
        double x,
        double y,
        double z,
        double region,
        double angle,
        double tilt)
    {
        _hasPlayerRuntimeSnapshot = true;
        _lastPlayerX = x;
        _lastPlayerY = y;
        _lastPlayerZ = z;
        _lastPlayerRegion = region;
        _lastPlayerAngle = NormalizeAngle(angle);
        _lastPlayerTilt = tilt;
    }

    private static double NormalizeAngle(double angle)
    {
        angle %= FullTurnRadians;
        return angle < 0 ? angle + FullTurnRadians : angle;
    }

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= 0.000001;

    private void FlushPendingScreenshots()
    {
        if (_pendingScreenshots.Count == 0)
        {
            return;
        }

        foreach (WdlScreenshotRequest request in _pendingScreenshots)
        {
            try
            {
                SaveScreenshot(request);
            }
            catch
            {
                // Screenshots are a presentation-side convenience for now; keep the game running if capture fails.
            }
        }

        _pendingScreenshots.Clear();
    }

    private unsafe void SaveScreenshot(WdlScreenshotRequest request)
    {
        using ID3D11Texture2D backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        Texture2DDescription source = backBuffer.Description;
        var stagingDescription = new Texture2DDescription
        {
            Width = source.Width,
            Height = source.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = source.Format,
            SampleDescription = source.SampleDescription,
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        };

        using ID3D11Texture2D staging = _device.CreateTexture2D(stagingDescription);
        _context.CopyResource(staging, backBuffer);
        _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out MappedSubresource mapped);
        try
        {
            using var bitmap = new Bitmap((int)source.Width, (int)source.Height, PixelFormat.Format32bppArgb);
            var rectangle = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData bitmapData = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int widthBytes = bitmap.Width * 4;
                int destinationStride = Math.Abs(bitmapData.Stride);
                byte* destinationStart = (byte*)bitmapData.Scan0;
                if (bitmapData.Stride < 0)
                {
                    destinationStart -= (bitmap.Height - 1) * destinationStride;
                }

                byte* sourceStart = (byte*)mapped.DataPointer;
                for (int y = 0; y < bitmap.Height; y++)
                {
                    Buffer.MemoryCopy(
                        sourceStart + (y * mapped.RowPitch),
                        destinationStart + (y * destinationStride),
                        destinationStride,
                        widthBytes);
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            string path = ScreenshotPath(request);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            bitmap.Save(path, ImageFormat.Png);
        }
        finally
        {
            _context.Unmap(staging, 0);
        }
    }

    private string ScreenshotPath(WdlScreenshotRequest request)
    {
        string directory = Path.Combine(Path.GetDirectoryName(_archivePath) ?? Environment.CurrentDirectory, "screenshots");
        string baseName = SanitizeFileName(request.BaseName);
        return Path.Combine(directory, $"{baseName}{request.Number:000}.png");
    }

    private void SaveRuntimeSnapshot(WdlPersistenceRequest request)
    {
        string path = RuntimeSavePath(request);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, _runtime.CaptureSnapshot(), SaveJsonOptions);
        _lastPersistenceStatus = $"saved {PersistenceSlotName(request)}";
    }

    private void LoadRuntimeSnapshot(WdlPersistenceRequest request)
    {
        string path = RuntimeSavePath(request);
        if (!File.Exists(path))
        {
            _lastPersistenceStatus = $"missing {PersistenceSlotName(request)}";
            return;
        }

        using FileStream stream = File.OpenRead(path);
        WdlRuntimeSnapshot? snapshot = JsonSerializer.Deserialize<WdlRuntimeSnapshot>(stream, SaveJsonOptions);
        if (snapshot is null)
        {
            _lastPersistenceStatus = $"load {PersistenceSlotName(request)} empty";
            return;
        }

        _runtime.RestoreSnapshot(snapshot);
        _handledGeometryInvalidations = _runtime.ShakeRequests.Count;
        _hasPlayerRuntimeSnapshot = false;
        StopActiveSounds();
        if (_device is not null)
        {
            RebuildWorldResources();
        }

        ApplyRuntimePlayerState();
        _lastPersistenceStatus = $"loaded {PersistenceSlotName(request)}";
    }

    private string RuntimeSavePath(WdlPersistenceRequest request)
    {
        string root = Path.Combine(
            Path.GetDirectoryName(_archivePath) ?? Environment.CurrentDirectory,
            "openvirtue-saves",
            SanitizeFileName(Path.GetFileNameWithoutExtension(_archivePath)));
        string level = SanitizeFileName(Path.GetFileNameWithoutExtension(_mainWdlName));
        string baseName = SanitizeFileName(request.BaseName);
        return Path.Combine(root, $"{level}-{baseName}{request.Number:000}.ovsave.json");
    }

    private string RuntimePrintDirectory() =>
        Path.Combine(
            Path.GetDirectoryName(_archivePath) ?? Environment.CurrentDirectory,
            "openvirtue-prints",
            SanitizeFileName(Path.GetFileNameWithoutExtension(_archivePath)));

    private static string PersistenceVerb(WdlPersistenceRequest request) =>
        request.Operation == WdlPersistenceOperation.Save ? "save" : "load";

    private static string PersistenceSlotName(WdlPersistenceRequest request) =>
        $"{request.BaseName}{request.Number:000}";

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private void RenderFrame()
    {
        if (_device is null || _defaultView is null)
        {
            return;
        }

        if (_activeFlic is null)
        {
            UpdateCamera();
        }

        TickRuntime();
        if (_activeFlic is { } flic)
        {
            RenderFlic(flic);
            return;
        }

        System.Drawing.Size renderSize = RenderSize();
        VirtualDisplayLayout display = DisplayLayout(renderSize);
        DisplayRectangle worldViewport = WorldViewport(display);
        Matrix4x4 viewProjection = _camera.View * CurrentProjection().CreateProjectionMatrix(1f, 200000f);

        _context.RSSetViewport(new Viewport(
            worldViewport.X,
            worldViewport.Y,
            worldViewport.Width,
            worldViewport.Height,
            0,
            1));
        _context.RSSetState(_rasterizer);
        _context.OMSetRenderTargets(_renderTarget, _depthView);
        _context.OMSetDepthStencilState(_depthState);
        _context.ClearRenderTargetView(_renderTarget, TintedClearColor());
        _context.ClearDepthStencilView(_depthView, DepthStencilClearFlags.Depth, 1f, 0);

        _context.IASetInputLayout(_inputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.VSSetConstantBuffer(0, _constantBuffer);
        _context.PSSetShader(_pixelShader);
        _context.PSSetConstantBuffer(0, _constantBuffer);
        _context.PSSetSampler(0, _sampler);

        RenderSky(worldViewport);
        _context.OMSetRenderTargets(_renderTarget, _depthView);
        _context.OMSetDepthStencilState(_depthState);

        if (_vertexCount > 0 && _vertexBuffer is not null)
        {
            _context.IASetVertexBuffer(0, _vertexBuffer, (uint)Marshal.SizeOf<RenderVertex>());
            foreach ((
                int start,
                int count,
                string? texture,
                float ambient,
                AcknexObject? source,
                bool alphaTest) in _batches)
            {
                string? renderTexture = texture;
                if (source is not null &&
                    texture is not null &&
                    _level.Textures.TryGetValue(texture, out LevelTexture animatedTexture))
                {
                    renderTexture = TextureAnimation.ResolveRenderTexture(animatedTexture, source["CYCLE"]);
                    if (renderTexture is null)
                    {
                        continue;
                    }
                }

                ApplyFrameConstants(viewProjection, ambient, alphaTest);
                ID3D11ShaderResourceView view = renderTexture is not null &&
                    _textureViews.TryGetValue(renderTexture, out ID3D11ShaderResourceView? found)
                    ? found
                    : _defaultView;
                _context.PSSetShaderResource(0, view);
                _context.Draw((uint)count, (uint)start);
            }
        }

        RenderSprites(viewProjection);
        RenderUiOverlays(display);
        RenderUiText(display);

        FlushPendingScreenshots();
        _swapChain.Present(1, PresentFlags.None);
    }

    private void RenderFlic(FlicPlayback playback)
    {
        if (playback.UploadedFrameIndex != playback.FrameIndex)
        {
            _flicView?.Dispose();
            FlicFrame frame = playback.Animation.Frames[playback.FrameIndex];
            _flicView = UploadTexture(new TextureImage(
                playback.Animation.Width,
                playback.Animation.Height,
                frame.Rgba));
            playback.UploadedFrameIndex = playback.FrameIndex;
        }

        if (_flicView is null)
        {
            return;
        }

        RenderVertex[] vertices =
        [
            new(0, 1, 0, 0, 1),
            new(1, 1, 0, 1, 1),
            new(1, 0, 0, 1, 0),
            new(0, 1, 0, 0, 1),
            new(1, 0, 0, 1, 0),
            new(0, 0, 0, 0, 0),
        ];
        using ID3D11Buffer buffer = _device.CreateBuffer(
            vertices.AsSpan(),
            new BufferDescription(
                (uint)(vertices.Length * Marshal.SizeOf<RenderVertex>()),
                BindFlags.VertexBuffer,
                ResourceUsage.Immutable));

        System.Drawing.Size renderSize = RenderSize();
        Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(0, 1, 1, 0, 0, 1);
        _context.RSSetViewport(new Viewport(0, 0, renderSize.Width, renderSize.Height, 0, 1));
        _context.RSSetState(_rasterizer);
        _context.OMSetRenderTargets(_renderTarget, null);
        _context.ClearRenderTargetView(_renderTarget, new Color4(0, 0, 0, 1));
        _context.IASetInputLayout(_inputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.IASetVertexBuffer(0, buffer, (uint)Marshal.SizeOf<RenderVertex>());
        _context.VSSetShader(_vertexShader);
        _context.VSSetConstantBuffer(0, _constantBuffer);
        _context.PSSetShader(_pixelShader);
        _context.PSSetConstantBuffer(0, _constantBuffer);
        _context.PSSetSampler(0, _sampler);
        _context.PSSetShaderResource(0, _flicView);
        ApplyFrameConstants(projection, 1, alphaTest: false, tint: Vector3.One);
        _context.Draw((uint)vertices.Length, 0);

        FlushPendingScreenshots();
        _swapChain.Present(1, PresentFlags.None);
    }

    private void RenderSky(DisplayRectangle viewport)
    {
        LevelTexture? sky = null;
        foreach (LevelTexture candidate in _level.Textures.Values)
        {
            if (candidate.IsSky)
            {
                sky = candidate;
                break;
            }
        }

        if (sky is not { } texture || texture.Frames.Count == 0)
        {
            return;
        }

        // The panorama side is a compass selection: take it from the map heading, numbered
        // clockwise like directional sprite sides. Which side the original anchors at angle 0
        // is unprobed (roadmap: exact panorama stitching remains).
        double normalizedHeading = ((-_runtime.GetSkill("PLAYER_ANGLE") / Math.Tau) % 1 + 1) % 1;
        int side = (int)Math.Floor(normalizedHeading * Math.Max(1, texture.Sides));
        string? renderTexture = TextureAnimation.ResolveRenderTexture(texture, cycle: 1, side);
        if (renderTexture is null || !_textureViews.TryGetValue(renderTexture, out ID3D11ShaderResourceView? view))
        {
            return;
        }

        float offsetU = texture.Width > 0 ? (float)(_runtime.GetSkill("SKY_OFFS_X") / texture.Width) : 0;
        float offsetV = texture.Height > 0 ? (float)(_runtime.GetSkill("SKY_OFFS_Y") / texture.Height) : 0;
        RenderVertex[] vertices =
        [
            new(0, 1, 0, offsetU, 1 + offsetV),
            new(1, 1, 0, 1 + offsetU, 1 + offsetV),
            new(1, 0, 0, 1 + offsetU, offsetV),
            new(0, 1, 0, offsetU, 1 + offsetV),
            new(1, 0, 0, 1 + offsetU, offsetV),
            new(0, 0, 0, offsetU, offsetV),
        ];

        using ID3D11Buffer buffer = _device.CreateBuffer(
            vertices.AsSpan(),
            new BufferDescription((uint)(vertices.Length * Marshal.SizeOf<RenderVertex>()), BindFlags.VertexBuffer, ResourceUsage.Immutable));
        Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(0, 1, 1, 0, 0, 1);
        ApplyFrameConstants(projection, 1, alphaTest: false);
        _context.RSSetViewport(new Viewport(viewport.X, viewport.Y, viewport.Width, viewport.Height, 0, 1));
        _context.OMSetRenderTargets(_renderTarget, null);
        _context.IASetVertexBuffer(0, buffer, (uint)Marshal.SizeOf<RenderVertex>());
        _context.PSSetShaderResource(0, view);
        _context.Draw((uint)vertices.Length, 0);
    }

    private System.Drawing.Size RenderSize() =>
        new(Math.Max(1, _renderSurface.ClientSize.Width), Math.Max(1, _renderSurface.ClientSize.Height));

    private VirtualDisplayLayout DisplayLayout(System.Drawing.Size physicalSize)
    {
        if (_level.VideoMode is { } mode)
        {
            // Every documented VGA mode was shown on a 4:3 display; 320x200 / X320x400 frames
            // are stretched to it (OV-20261009-pixel-aspect-video-modes).
            return VirtualDisplayLayout.Create(
                physicalSize.Width,
                physicalSize.Height,
                mode.Width,
                mode.Height,
                LevelVideoMode.DisplayAspect);
        }

        return VirtualDisplayLayout.Create(
            physicalSize.Width,
            physicalSize.Height,
            physicalSize.Width,
            physicalSize.Height);
    }

    /// <summary>The live projection in 3-D-window pixels (OV-20261009-* measured model).</summary>
    private ProjectionModel CurrentProjection() =>
        ProjectionModel.Create(
            _runtime.GetSkill("PLAYER_ARC"),
            _runtime.GetSkill("ASPECT"),
            _runtime.GetSkill("PLAYER_TILT"),
            (int)Math.Round(_runtime.GetSkill("SCREEN_WIDTH")),
            (int)Math.Round(_runtime.GetSkill("SCREEN_HGT")));

    private DisplayRectangle WorldViewport(VirtualDisplayLayout display)
    {
        float x = (float)Math.Clamp(_runtime.GetSkill("SCREEN_X"), 0, display.VirtualWidth - 1);
        float y = (float)Math.Clamp(_runtime.GetSkill("SCREEN_Y"), 0, display.VirtualHeight - 1);
        float width = (float)Math.Clamp(_runtime.GetSkill("SCREEN_WIDTH"), 1, display.VirtualWidth - x);
        float height = (float)Math.Clamp(_runtime.GetSkill("SCREEN_HGT"), 1, display.VirtualHeight - y);
        return display.ToPhysical(x, y, width, height);
    }

    private Vector2 VirtualPoint(Point point) =>
        DisplayLayout(RenderSize()).ToVirtual(point.X, point.Y);

    private float TextureAmbient(string? texture)
    {
        double light = _runtime.GetSkill("AMBIENT") + _runtime.GetSkill("PLAYER_LIGHT");
        string? logicalTexture = texture is null ? null : TextureAnimation.LogicalTextureName(texture);
        if (logicalTexture is not null && _level.Textures.TryGetValue(logicalTexture, out LevelTexture levelTexture))
        {
            light += levelTexture.Ambient;
        }

        return (float)light;
    }

    private void ApplyFrameConstants(
        Matrix4x4 viewProjection,
        float ambient,
        bool alphaTest,
        Vector3? tint = null)
    {
        var constants = new FrameConstants
        {
            ViewProjection = viewProjection,
            TintAndLight = new Vector4(tint ?? _paletteTint, ambient),
            Material = new Vector4(alphaTest ? 1 : 0, 0, 0, 0),
        };
        unsafe
        {
            _context.UpdateSubresource(
                _constantBuffer,
                0,
                null,
                (nint)(&constants),
                0,
                0);
        }
    }

    private Color4 ChooseBackgroundColor()
    {
        foreach (LevelTexture texture in _level.Textures.Values)
        {
            if (texture.IsSky && _textureImages.TryGetValue(texture.Name, out TextureImage image))
            {
                return AverageColor(image);
            }
        }

        return DefaultClearColor;
    }

    private Color4 TintedClearColor() => new(
        Math.Clamp(_clearColor.R * _paletteTint.X, 0, 1),
        Math.Clamp(_clearColor.G * _paletteTint.Y, 0, 1),
        Math.Clamp(_clearColor.B * _paletteTint.Z, 0, 1),
        _clearColor.A);

    private static Color4 AverageColor(TextureImage image)
    {
        long r = 0;
        long g = 0;
        long b = 0;
        long count = 0;
        ReadOnlySpan<byte> rgba = image.Rgba;
        for (int i = 0; i + 3 < rgba.Length; i += 4)
        {
            r += rgba[i];
            g += rgba[i + 1];
            b += rgba[i + 2];
            count++;
        }

        if (count == 0)
        {
            return DefaultClearColor;
        }

        float scale = 1.0f / (255.0f * count);
        return new Color4(r * scale, g * scale, b * scale, 1f);
    }

    private void UpdateStatus()
    {
        if (_player is null || _statusLabel is null)
        {
            return;
        }

        Vector3 p = _player.Position;
        string regionName = RegionName(_player.Region);
        double fps = _lastFrameSeconds > 0 ? 1.0 / _lastFrameSeconds : 0;
        (double floor, double ceiling) = RegionHeights(_player.Region);
        _statusLabel.Text =
            $"world {_level.Name} | video {_level.VideoMode?.Name ?? "native"} | " +
            $"pos {p.X:F1}, {p.Y:F1}, {p.Z:F1} | region {_player.Region} {regionName} | " +
            $"floor {floor:F1} ceil {ceiling:F1} | FPS {fps:F0} | TIME_CORR {_lastTimeCorrection:F3} | " +
            $"audio {_activeSounds.Count}/8 ch {_runtime.GetSkill("CHANNEL"):F0} | " +
            $"IF_START {(_level.StartupAction is null ? "none" : _startupRan ? "ran" : "failed")} | " +
            $"control {RuntimeControlStatus()}{PresentationStatusText()}{PersistenceStatusText()} | WDL movement";
    }

    private string PresentationStatusText() =>
        $"{PaletteStatusText()}" +
        (_lastPresentationStatus is null ? string.Empty : $" | {_lastPresentationStatus}");

    private string PaletteStatusText()
    {
        if (_runtime.PaletteFadeRequests.Count == 0)
        {
            return string.Empty;
        }

        WdlPaletteFadeRequest request = _runtime.PaletteFadeRequests[^1];
        return $" | pal {request.PaletteName}:{request.Factor:F2} x{_runtime.PaletteFadeRequests.Count} " +
               $"tint {_paletteTint.X:F2},{_paletteTint.Y:F2},{_paletteTint.Z:F2}";
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
            var r = _level.Regions[region];
            return (r.FloorHeight, r.CeilHeight);
        }

        return (double.NaN, double.NaN);
    }

    private sealed class WdlInputDialog : Form
    {
        private readonly TextBox _textBox = new()
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Width = 320,
        };

        public WdlInputDialog(WdlInputRequest request)
        {
            Text = "OpenVirtue - Input";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new System.Drawing.Size(380, 118);

            var label = new Label
            {
                AutoSize = true,
                Text = request.StringName,
            };

            _textBox.Text = request.InitialValue;
            _textBox.MaxLength = Math.Max(0, request.Capacity);
            _textBox.SelectionStart = 0;
            _textBox.SelectionLength = _textBox.Text.Length;

            var ok = new Button
            {
                DialogResult = DialogResult.OK,
                Text = "OK",
                Width = 80,
            };
            var cancel = new Button
            {
                DialogResult = DialogResult.Cancel,
                Text = "Cancel",
                Width = 80,
            };

            var buttons = new FlowLayoutPanel
            {
                Anchor = AnchorStyles.Right,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                RowCount = 3,
                ColumnCount = 1,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(label, 0, 0);
            layout.Controls.Add(_textBox, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        public string Value => _textBox.Text;
    }

    private sealed class ActiveSound : IDisposable
    {
        private readonly MemoryStream _stream;
        private readonly WaveFileReader _reader;
        private readonly WaveStream _source;
        private readonly WaveChannel32 _channel;
        private readonly WaveOutEvent _player;
        private bool _started;

        public ActiveSound(byte[] bytes, int channel, float volume, float pan, bool loop)
        {
            Channel = channel;
            _stream = new MemoryStream(bytes, writable: false);
            _reader = new WaveFileReader(_stream);
            _source = loop ? new LoopingWaveStream(_reader) : _reader;
            _channel = new WaveChannel32(_source)
            {
                PadWithZeroes = false,
                Volume = volume,
                Pan = pan,
            };
            _player = new WaveOutEvent();
            _player.Init(_channel);
        }

        public int Channel { get; }

        public bool IsExpired => _started && _player.PlaybackState == PlaybackState.Stopped;

        public void Play()
        {
            _started = true;
            _player.Play();
        }

        public void UpdateMix(float volume, float pan)
        {
            _channel.Volume = Math.Clamp(volume, 0, 1);
            _channel.Pan = Math.Clamp(pan, -1, 1);
        }

        public void Dispose()
        {
            _player.Stop();
            _player.Dispose();
            _channel.Dispose();
            if (!ReferenceEquals(_source, _reader))
            {
                _source.Dispose();
            }

            _reader.Dispose();
            _stream.Dispose();
        }
    }

    private sealed class LoopingWaveStream(WaveStream source) : WaveStream
    {
        public override WaveFormat WaveFormat => source.WaveFormat;

        public override long Length => long.MaxValue;

        public override long Position
        {
            get => source.Position;
            set => source.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = source.Read(buffer, offset + total, count - total);
                if (read == 0)
                {
                    if (source.Length == 0)
                    {
                        break;
                    }

                    source.Position = 0;
                    continue;
                }

                total += read;
            }

            return total;
        }
    }

    private readonly record struct SpatialLoopSound(int Channel, string SoundName);

    private readonly record struct SpatialLoopCandidate(
        AcknexObject Emitter,
        string SoundName,
        double BaseVolume,
        float Volume,
        float Pan);

    private sealed class FlicPlayback(
        WdlFlicRequest request,
        string fileName,
        FlicAnimation animation)
    {
        public WdlFlicRequest Request { get; } = request;

        public string FileName { get; } = fileName;

        public FlicAnimation Animation { get; } = animation;

        public int FrameIndex { get; set; }

        public int UploadedFrameIndex { get; set; } = -1;

        public double ElapsedSeconds { get; set; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ShowSystemCursor();
            _timer.Dispose();
            StopActiveSounds();
            ClearFlicPlayback();
            foreach (ID3D11ShaderResourceView view in _textureViews.Values)
            {
                view.Dispose();
            }

            foreach (ID3D11ShaderResourceView view in _uiTextureViews.Values)
            {
                view.Dispose();
            }

            foreach (ID3D11ShaderResourceView view in _fontTextureViews.Values)
            {
                view.Dispose();
            }

            _defaultView?.Dispose();
            _sampler?.Dispose();
            _vertexBuffer?.Dispose();
            _constantBuffer?.Dispose();
            _inputLayout?.Dispose();
            _vertexShader?.Dispose();
            _pixelShader?.Dispose();
            _rasterizer?.Dispose();
            _depthState?.Dispose();
            _renderTarget?.Dispose();
            _depthView?.Dispose();
            _depthTexture?.Dispose();
            _swapChain?.Dispose();
            _context?.Dispose();
            _device?.Dispose();
        }

        base.Dispose(disposing);
    }
}
