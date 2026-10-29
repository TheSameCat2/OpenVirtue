// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;
using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Formats.Wdl;

namespace OpenVirtue.Engine.Interpreter;

/// <summary>
/// The runtime environment for a loaded <see cref="Level"/>: the live skill table (seeded
/// from the level's declared skills), the named object registry, and the level's ACTION
/// scripts. Implements <see cref="IWdlContext"/> so the <see cref="WdlInterpreter"/> can
/// read and write state, and exposes <see cref="RunAction"/> to invoke a script by name.
/// </summary>
public sealed class WdlRuntime :
    IWdlContext,
    IWdlActionResolver,
    IWdlSchedulerContext,
    IWdlActionHookContext,
    IWdlObjectGroupContext,
    IWdlObjectMutationContext,
    IWdlObjectReadContext,
    IWdlObjectReferenceContext,
    IWdlStringContext,
    IWdlInputContext,
    IWdlPrintContext,
    IWdlSoundContext,
    IWdlPaletteContext,
    IWdlLocationContext,
    IWdlRandomContext,
    IWdlSkillBoundsContext,
    IWdlGeometryContext,
    IWdlHitContext,
    IWdlPresentationContext,
    IWdlPersistenceContext,
    IWdlKeywordReferenceContext,
    IWdlUiContext,
    IWdlControlContext,
    IWdlExecutionDiagnostics
{
    private const int MaxCallDepth = 64;
    private const int EachTickSlotCount = 16;
    private const int EachSecSlotCount = 16;

    /// <summary>The thing and actor skill holding the player's approximate distance (manual p.69).</summary>
    private const string DistanceSkill = "DISTANCE";

    /// <summary>A thing's or actor's <c>DISTANCE</c> before its first refresh (OV-20261019-clip-contact).</summary>
    private const double UnrefreshedDistance = 65535.999;

    /// <summary>The first refresh of a thing's or actor's <c>DISTANCE</c> needs the player within this times its border (OV-20261019-actor-contact).</summary>
    private const double DistanceStartFactor = 1.5;

    /// <summary>The skill holding the 1-based number of the flic frame on screen, 0 when none plays.</summary>
    public const string FlicFrameSkill = "FLIC_FRAME";

    private static readonly string[] RegionNumberFields =
    [
        "FLOOR_HGT",
        "CEIL_HGT",
        "AMBIENT",
        "FLOOR_OFFS_X",
        "FLOOR_OFFS_Y",
        "CEIL_OFFS_X",
        "CEIL_OFFS_Y",
    ];
    private static readonly string[] RegionStringFields =
    [
        "FLOOR_TEX",
        "CEIL_TEX",
        "GENIUS",
        "EACH_TICK",
        "EACH_CYCLE",
        "IF_ENTER",
        "IF_LEAVE",
        "IF_DIVE",
        "IF_ARISE",
    ];
    private static readonly string[] WallNumberFields =
    [
        "OFFSX",
        "OFFSY",
    ];
    private static readonly string[] WallStringFields =
    [
        "TEXTURE",
        "TARGET",
        "WAY",
        "OVERLAY",
        "ATTACH",
        "EACH_TICK",
        "EACH_CYCLE",
        "IF_NEAR",
        "IF_FAR",
        "IF_HIT",
    ];
    private static readonly string[] EntityNumberFields =
    [
        "X",
        "Y",
        "Z",
        "ANGLE",
        "HEIGHT",
        "REGION",
    ];
    private static readonly string[] EntityStringFields =
    [
        "TEXTURE",
        "EACH_TICK",
        "EACH_CYCLE",
        "IF_NEAR",
        "IF_FAR",
        "IF_ARRIVED",
        "IF_HIT",
        "IF_KLICK",
    ];

    private readonly Level _level;
    private readonly Dictionary<string, double> _skills;
    private readonly IReadOnlyDictionary<string, SkillRange> _bounds;
    private readonly Dictionary<string, WdlString> _strings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WdlUiElement> _uiElements = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _keywordReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _keywordReferenceTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, string> _sounds;
    private readonly IReadOnlyDictionary<string, string> _palettes;
    private readonly List<WdlSoundRequest> _soundRequests = [];
    private readonly List<WdlAudioRequest> _audioRequests = [];
    private readonly List<WdlTextureSoundRequest> _textureSoundRequests = [];
    private readonly List<WdlPaletteFadeRequest> _paletteFadeRequests = [];
    private readonly List<WdlShakeRequest> _shakeRequests = [];
    private readonly List<WdlFlicRequest> _flicRequests = [];
    private readonly List<WdlScreenshotRequest> _screenshotRequests = [];
    private readonly List<WdlPersistenceRequest> _persistenceRequests = [];
    private readonly List<WdlInputRequest> _inputRequests = [];
    private readonly Dictionary<int, ActiveInputAction> _inputActions = [];
    private Random _random = new();
    private readonly Dictionary<string, AcknexObject> _objects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<AcknexObject>> _objectsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ambiguousObjectNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Region> _regions = [];
    private readonly List<AcknexObject> _levelObjects = [];
    private readonly string?[] _eachTickActions = new string?[EachTickSlotCount];
    private readonly string?[] _eachSecActions = new string?[EachSecSlotCount];

    /// <summary>The elapsed part of the runtime's current second, from 0 up to (not including) 1.</summary>
    private double _secondFraction;
    private readonly Dictionary<string, string?> _actionHooks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ActiveAction> _waitingActions = [];
    private readonly IReadOnlyDictionary<string, WdlBlock> _actions;
    private readonly WdlInterpreter _interpreter;
    private readonly string? _startupAction;
    private readonly string? _printDirectory;
    private readonly Dictionary<AcknexObject, bool> _proximityStates = [];
    private readonly HashSet<MapEntity> _distanceStarted = [];
    private readonly HashSet<AcknexObject> _arrivedTargets = [];

    /// <summary>The level's walls indexed for the view test (<see cref="ViewTest.Update"/> follows moving vertices).</summary>
    private ViewTest? _viewTest;

    /// <summary>The things, actors and <c>BERKELEY</c> walls visible in the last drawn frame (see <see cref="RefreshView"/>).</summary>
    private readonly HashSet<AcknexObject> _inView = [];

    /// <summary>
    /// Unseen actors at most this far from the player (|dx| + |dy|, see <see cref="RefreshEntityDistances"/>)
    /// move every cycle whatever their <c>DIST</c>; the manual does not mention it
    /// (OV-20261020-view-activity, range-* and edge-*).
    /// </summary>
    private const double SkipFramesNearRange = 20;

    /// <summary>Each thing's and actor's distance from the player as <see cref="RefreshEntityDistances"/> last refreshed it.</summary>
    private readonly Dictionary<MapEntity, double> _entityDistances = [];

    /// <summary>The player's view at the last refresh of <see cref="_entityDistances"/>.</summary>
    private DistanceView? _distanceView;

    /// <summary>Whether an actor in front of the player moved since the last refresh of <see cref="_entityDistances"/>.</summary>
    private bool _frontActorMoved;

    /// <summary>The view of the last drawn frame (<see cref="RefreshView"/>), counted by <see cref="_viewFrame"/>.</summary>
    private Viewer? _lastViewer;

    private int _viewFrame;

    /// <summary>The frame in which each wall's <c>VISIBLE</c> was last worked out (<see cref="TestWallView"/>).</summary>
    private readonly Dictionary<Wall, int> _wallViewFrames = [];

    /// <summary>Whether an action has read a wall's <c>SEEN</c>, so every wall is tested in every frame.</summary>
    private bool _trackWallsSeen;

    /// <summary>The frame in which each region's <c>VISIBLE</c> was last worked out (<see cref="TestRegionView"/>).</summary>
    private readonly Dictionary<Region, int> _regionViewFrames = [];

    /// <summary>Whether an action has read a region's <c>SEEN</c>, so every region is tested in every frame.</summary>
    private bool _trackRegionsSeen;

    /// <summary>The cycles an unseen actor has skipped since it last moved (<c>SKIP_FRAMES</c>).</summary>
    private readonly Dictionary<Actor, int> _skippedCycles = [];

    /// <summary>Whether the actor moving now makes a skipped actor's long step (see <see cref="TryEndPointCarefulMove"/>).</summary>
    private bool _longSkippedStep;
    private readonly Dictionary<Actor, string> _actorTargetStates = [];
    private readonly Dictionary<AcknexObject, TextureAnimationState> _textureAnimationStates = [];
    private readonly LevelCollisionWorld _collisionWorld;
    private readonly IReadOnlyDictionary<string, TextureImage> _hitTextures;
    private string _printBaseName = "PRINT";

    /// <summary>The storage of the last cycle's object <c>EACH_TICK</c> snapshot, reused by the next one.</summary>
    private readonly List<AcknexObject> _objectPassBuffer = [];
    private bool _firstCycleRun;
    private int _printNumber;
    private int _callDepth;
    private int _nextInputRequestId;
    private int _nextFlicRequestId;
    private int? _observedPlayerRegion;
    private AcknexObject? _activeActionTarget;
    private bool _dispatchingGlobalEachTick;

    public WdlRuntime(
        Level level,
        string? printDirectory = null,
        IReadOnlyDictionary<string, TextureImage>? hitTextures = null)
    {
        ArgumentNullException.ThrowIfNull(level);
        _level = level;
        _collisionWorld = new LevelCollisionWorld(level);
        _hitTextures = hitTextures ?? new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase);
        _skills = new Dictionary<string, double>(level.Skills, StringComparer.OrdinalIgnoreCase);
        _bounds = level.SkillBounds;
        InitializePredefinedSkillDefaults();
        _printDirectory = printDirectory;
        _sounds = level.Sounds;
        _palettes = level.Palettes;
        foreach ((string name, WdlString value) in level.Strings)
        {
            _strings[name] = value;
        }

        foreach ((string name, WdlUiElement value) in level.UiElements)
        {
            _uiElements[name] = value;
        }

        _actions = level.Actions;
        _startupAction = level.StartupAction;
        RegisterLevelObjects(level);
        InitializePredefinedReferences(level);
        InitializeSynonyms(level);
        InitializeEachTickActions(level);
        InitializeEachSecActions(level);
        InitializeActionHooks(level);
        RefreshView();
        InitializeAnimationCycles();
        // IF_START and the first cycle read TIME_CORR 1 (OV-20261029-scheduler-order).
        _skills[Timing.TimeCorrectionSkill] = 1;
        _skills[Timing.TimeFactorSkill] = 1;
        _interpreter = new WdlInterpreter(this);
    }

    /// <summary>The live global skill values.</summary>
    public IReadOnlyDictionary<string, double> Skills => _skills;

    /// <summary>Captures the declared <c>GLOBAL</c> values that survive a documented <c>LEVEL</c> change.</summary>
    public IReadOnlyDictionary<string, double> CaptureLevelTransitionGlobals()
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in _level.GlobalSkills)
        {
            if (_skills.TryGetValue(name, out double value))
            {
                values[name] = value;
            }
        }

        return values;
    }

    /// <summary>Restores values captured before <c>LEVEL</c>, limited to globals declared by this level.</summary>
    public void RestoreLevelTransitionGlobals(IReadOnlyDictionary<string, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (string name in _level.GlobalSkills)
        {
            if (values.TryGetValue(name, out double value))
            {
                SetSkill(name, value);
            }
        }
    }

    /// <summary>The named object references available to scripts.</summary>
    public IReadOnlyDictionary<string, AcknexObject> Objects => _objects;

    /// <summary>The live WDL string values.</summary>
    public IReadOnlyDictionary<string, WdlString> Strings => _strings;

    /// <summary>The live WDL UI declaration state.</summary>
    public IReadOnlyDictionary<string, WdlUiElement> UiElements => _uiElements;

    /// <summary>The live keyword-valued synonym references.</summary>
    public IReadOnlyDictionary<string, string?> KeywordReferences => _keywordReferences;

    /// <summary>Sound playback requests emitted by WDL sound instructions.</summary>
    public IReadOnlyList<WdlSoundRequest> SoundRequests => _soundRequests;

    /// <summary>Chronological host audio requests emitted by WDL sound instructions.</summary>
    public IReadOnlyList<WdlAudioRequest> AudioRequests => _audioRequests;

    /// <summary>Automatic animation-phase texture sounds awaiting host distance filtering.</summary>
    public IReadOnlyList<WdlTextureSoundRequest> TextureSoundRequests => _textureSoundRequests;

    /// <summary>Palette fade requests emitted by WDL palette instructions.</summary>
    public IReadOnlyList<WdlPaletteFadeRequest> PaletteFadeRequests => _paletteFadeRequests;

    /// <summary>Geometry invalidations emitted by WDL <c>SHAKE</c> instructions.</summary>
    public IReadOnlyList<WdlShakeRequest> ShakeRequests => _shakeRequests;

    /// <summary>Full-screen flic playback requests emitted by WDL flic instructions.</summary>
    public IReadOnlyList<WdlFlicRequest> FlicRequests => _flicRequests;

    /// <summary>
    /// Set by the host: whether a <c>PLAY_FLICFILE</c> file exists. When it returns <c>false</c>,
    /// the request is still recorded but <c>FLIC_FRAME</c> keeps its value, because the original
    /// skips a missing file and its action goes on (OV-20261015-flic-scheduler-detail). When it is
    /// not set, every file counts as present.
    /// </summary>
    public Func<string, bool>? FlicFileExists { get; set; }

    /// <summary>Screenshot requests emitted by WDL <c>SCREENSHOT</c> instructions.</summary>
    public IReadOnlyList<WdlScreenshotRequest> ScreenshotRequests => _screenshotRequests;

    /// <summary>Save/load requests emitted by documented WDL persistence instructions.</summary>
    public IReadOnlyList<WdlPersistenceRequest> PersistenceRequests => _persistenceRequests;

    /// <summary>Pending text-entry requests emitted by WDL <c>INKEY</c> instructions.</summary>
    public IReadOnlyList<WdlInputRequest> PendingInputRequests => _inputRequests;

    /// <summary>How many times WDL requested that all running sounds be stopped.</summary>
    public int StopSoundCount { get; private set; }

    /// <summary>How many diagnostic beeps WDL requested.</summary>
    public int BeepCount { get; private set; }

    /// <summary>How many times WDL requested that the current flic be stopped.</summary>
    public int StopFlicCount { get; private set; }

    /// <summary>How many actions are currently suspended by <c>WAIT</c> or <c>WAITT</c>.</summary>
    public int WaitingActionCount => _waitingActions.Count;

    /// <summary>How many global <c>EACH_TICK</c> scheduler slots are currently occupied.</summary>
    public int ActiveEachTickActionCount => _eachTickActions.Count(actionName => actionName is not null);

    /// <summary>How many global <c>EACH_SEC</c> scheduler slots are currently occupied.</summary>
    public int ActiveEachSecActionCount => _eachSecActions.Count(actionName => actionName is not null);

    /// <summary>How many occupied global scheduler slots directly reference player input and velocity skills.</summary>
    public int ActivePlayerMovementActionCount => _eachTickActions.Count(IsPlayerMovementAction);

    /// <summary>Number of player-velocity assignments made by global scheduler actions in the latest tick.</summary>
    public int LastTickPlayerVelocityWriteCount { get; private set; }

    /// <summary>Number of statements entered by global scheduler actions in the latest tick.</summary>
    public int LastTickGlobalStatementCount { get; private set; }

    /// <summary>Number of entered global scheduler statements targeting a player velocity skill.</summary>
    public int LastTickPlayerVelocityStatementCount { get; private set; }

    /// <summary>Number of global scheduler statements skipped for unsupported expression forms.</summary>
    public int LastTickUnsupportedExpressionCount { get; private set; }

    /// <summary>Number of true conditional evaluations in global scheduler actions during the latest tick.</summary>
    public int LastTickTrueConditionCount { get; private set; }

    /// <summary>Number of false conditional evaluations in global scheduler actions during the latest tick.</summary>
    public int LastTickFalseConditionCount { get; private set; }

    /// <summary>All placed level objects registered for runtime iteration.</summary>
    public IReadOnlyList<AcknexObject> LevelObjects => _levelObjects;

    /// <summary>The live global <c>IF_*</c> action-hook assignments.</summary>
    public IReadOnlyDictionary<string, string?> ActionHooks => _actionHooks;

    /// <summary>Whether a WDL <c>EXIT</c> instruction has requested game termination.</summary>
    public bool ExitRequested { get; private set; }

    /// <summary>The optional message supplied by the WDL <c>EXIT</c> instruction.</summary>
    public string? ExitMessage { get; private set; }

    /// <summary>The latest WDL level name requested by a <c>LEVEL</c> instruction.</summary>
    public string? RequestedLevelName { get; private set; }

    /// <summary>The optional WRS resource name supplied by a <c>LEVEL</c> instruction.</summary>
    public string? RequestedLevelResource { get; private set; }

    /// <summary>The latest WMP map name requested by a <c>MAP</c> instruction.</summary>
    public string? RequestedMapName { get; private set; }

    /// <summary>Captures the live runtime and mutable level state for OpenVirtue harness save/load.</summary>
    public WdlRuntimeSnapshot CaptureSnapshot() =>
        new(
            new Dictionary<string, double>(_skills, StringComparer.OrdinalIgnoreCase),
            _strings.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string?>(_keywordReferences, StringComparer.OrdinalIgnoreCase),
            _eachTickActions.ToList(),
            new Dictionary<string, string?>(_actionHooks, StringComparer.OrdinalIgnoreCase),
            _level.Vertices.ToList(),
            MutableLevelObjects().Select(CaptureObjectSnapshot).ToList(),
            CapturePlayerSnapshot())
        {
            EachSecActions = _eachSecActions.ToList(),
            SecondFraction = _secondFraction,
        };

    /// <summary>Restores a state previously captured with <see cref="CaptureSnapshot"/>.</summary>
    public void RestoreSnapshot(WdlRuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RestoreSkills(snapshot.Skills);
        for (int channel = 0; channel < 8; channel++)
        {
            SetSkill($"CHANNEL_{channel}", 0);
        }

        RestoreStrings(snapshot.Strings);
        RestoreKeywordReferences(snapshot.KeywordReferences);
        RestoreEachTickActions(snapshot.EachTickActions);
        RestoreEachSecActions(snapshot.EachSecActions, snapshot.SecondFraction);
        RestoreActionHooks(snapshot.ActionHooks);
        RestoreVertices(snapshot.Vertices);
        RestoreLevelObjects(snapshot.LevelObjects);
        RestorePlayer(snapshot.Player);
        _skippedCycles.Clear();
        _entityDistances.Clear();
        _distanceView = null;
        RefreshView();
        _proximityStates.Clear();
        _arrivedTargets.Clear();
        _actorTargetStates.Clear();
        _textureAnimationStates.Clear();
        _observedPlayerRegion = null;
    }

    /// <inheritdoc/>
    public double GetSkill(string name) => _skills.GetValueOrDefault(name);

    /// <inheritdoc/>
    public void SetSkill(string name, double value)
    {
        _skills[name] = _bounds.TryGetValue(name, out SkillRange range) ? range.Clamp(value) : value;
        if (_dispatchingGlobalEachTick && name.StartsWith("PLAYER_V", StringComparison.OrdinalIgnoreCase))
        {
            LastTickPlayerVelocityWriteCount++;
        }
    }

    /// <inheritdoc/>
    public void RecordStatementExecution(string keyword, string? target)
    {
        if (!_dispatchingGlobalEachTick)
        {
            return;
        }

        LastTickGlobalStatementCount++;
        if (target?.StartsWith("PLAYER_V", StringComparison.OrdinalIgnoreCase) == true)
        {
            LastTickPlayerVelocityStatementCount++;
        }
    }

    /// <inheritdoc/>
    public void RecordUnsupportedStatementExpression()
    {
        if (_dispatchingGlobalEachTick)
        {
            LastTickUnsupportedExpressionCount++;
        }
    }

    /// <inheritdoc/>
    public void RecordConditionEvaluation(bool result)
    {
        if (!_dispatchingGlobalEachTick)
        {
            return;
        }

        if (result)
        {
            LastTickTrueConditionCount++;
        }
        else
        {
            LastTickFalseConditionCount++;
        }
    }

    /// <inheritdoc/>
    public bool TryGetString(string name, out string value)
    {
        string? resolvedName = ResolveStringName(name);
        if (resolvedName is not null && _strings.TryGetValue(resolvedName, out WdlString text))
        {
            value = text.Value;
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <inheritdoc/>
    public bool SetString(string name, string value)
    {
        string? resolvedName = ResolveStringName(name);
        if (resolvedName is null || !_strings.TryGetValue(resolvedName, out WdlString text))
        {
            return false;
        }

        _strings[resolvedName] = text.WithValue(value);
        return true;
    }

    /// <inheritdoc/>
    public bool BeginInput(string stringName, out WdlInputRequest request)
    {
        string? resolvedName = ResolveStringName(stringName);
        if (resolvedName is null || !_strings.TryGetValue(resolvedName, out WdlString text))
        {
            request = default;
            return false;
        }

        request = new WdlInputRequest(++_nextInputRequestId, resolvedName, text.Value, text.Capacity);
        _inputRequests.Add(request);
        return true;
    }

    /// <summary>
    /// Completes a pending <c>INKEY</c> request and resumes the suspended action, if any.
    /// Result code 0 accepts <paramref name="value"/>; other documented codes restore the original string.
    /// </summary>
    public bool CompleteInput(int requestId, string? value, int resultCode = 0)
    {
        int index = _inputRequests.FindIndex(request => request.Id == requestId);
        if (index < 0)
        {
            return false;
        }

        WdlInputRequest request = _inputRequests[index];
        _inputRequests.RemoveAt(index);

        string nextValue = resultCode == 0 ? (value ?? string.Empty).TrimEnd() : request.InitialValue;
        SetString(request.StringName, nextValue);
        TryGetString(request.StringName, out string current);
        SetSkill("RESULT", resultCode);
        SetSkill("STR_LEN", current.Length);

        if (_inputActions.Remove(requestId, out ActiveInputAction? action))
        {
            WdlExecutionResult result = ContinueWithScope(action.State, action.Scope);
            StoreSuspendedAction(action.State, result, action.Scope);
        }

        return true;
    }

    /// <inheritdoc/>
    public void SetPrintFile(string baseName, int number)
    {
        _printBaseName = NormalizePrintBaseName(baseName);
        _printNumber = Math.Clamp(number, 0, 999);
    }

    /// <inheritdoc/>
    /// <remarks>Three decimals and no separator: a script writes its own spaces and line feeds (OV-20261029-print-output).</remarks>
    public void PrintValue(double value) =>
        AppendPrintText(value.ToString("F3", CultureInfo.InvariantCulture));

    /// <inheritdoc/>
    public void PrintString(string value) => AppendPrintText(value);

    /// <inheritdoc/>
    public void PlaySound(string soundName, string? fileName, double volume, double? balance, string? emitterName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = _sounds.GetValueOrDefault(soundName);
        }

        int channel = ReserveSoundChannel();
        if (channel < 0)
        {
            return;
        }

        var request = new WdlSoundRequest(
            soundName,
            fileName,
            volume,
            balance,
            emitterName,
            Streamed: false,
            channel,
            Loop: false);
        _soundRequests.Add(request);
        _audioRequests.Add(new WdlAudioRequest(WdlSoundOperation.Play, request));
    }

    /// <inheritdoc/>
    public void PlaySoundFile(string fileName, double volume, double? balance)
    {
        int channel = ReserveSoundChannel();
        if (channel < 0)
        {
            return;
        }

        var request = new WdlSoundRequest(
            fileName,
            fileName,
            volume,
            balance,
            EmitterName: null,
            Streamed: true,
            channel,
            Loop: false);
        _soundRequests.Add(request);
        _audioRequests.Add(new WdlAudioRequest(WdlSoundOperation.Play, request));
    }

    /// <inheritdoc/>
    public void StopSounds()
    {
        StopSoundCount++;
        for (int channel = 0; channel < 8; channel++)
        {
            SetSkill($"CHANNEL_{channel}", 0);
        }

        _audioRequests.Add(new WdlAudioRequest(WdlSoundOperation.StopAll, default));
    }

    /// <summary>Requests playback for a floor or ceiling texture sound.</summary>
    public int PlayTextureSound(string soundName, double volume, bool loop, double? balance = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(soundName);
        int channel = ReserveSoundChannel();
        if (channel < 0)
        {
            return -1;
        }

        var request = new WdlSoundRequest(
            soundName,
            _sounds.GetValueOrDefault(soundName),
            volume,
            balance,
            EmitterName: null,
            Streamed: false,
            channel,
            loop);
        _soundRequests.Add(request);
        _audioRequests.Add(new WdlAudioRequest(WdlSoundOperation.Play, request));
        return channel;
    }

    /// <summary>Stops one host sound channel and releases its script-visible state.</summary>
    public bool StopSoundChannel(int channel)
    {
        if (channel is < 0 or >= 8 || GetSkill($"CHANNEL_{channel}") == 0)
        {
            return false;
        }

        SetSkill($"CHANNEL_{channel}", 0);
        _audioRequests.Add(new WdlAudioRequest(WdlSoundOperation.StopChannel, default, channel));
        return true;
    }

    /// <summary>Releases a script-visible sound channel after host playback stops.</summary>
    public bool CompleteSound(int channel)
    {
        if (channel is < 0 or >= 8 || GetSkill($"CHANNEL_{channel}") == 0)
        {
            return false;
        }

        SetSkill($"CHANNEL_{channel}", 0);
        return true;
    }

    private int ReserveSoundChannel()
    {
        for (int channel = 0; channel < 8; channel++)
        {
            if (GetSkill($"CHANNEL_{channel}") != 0)
            {
                continue;
            }

            SetSkill($"CHANNEL_{channel}", 1);
            SetSkill("CHANNEL", channel);
            return channel;
        }

        SetSkill("CHANNEL", -1);
        return -1;
    }

    /// <inheritdoc/>
    public void FadePalette(string paletteName, string? fileName, double factor)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = _palettes.GetValueOrDefault(paletteName);
        }

        _paletteFadeRequests.Add(new WdlPaletteFadeRequest(paletteName, fileName, factor));
    }

    /// <inheritdoc/>
    public bool Locate(AcknexObject? target)
    {
        target ??= GetObject("player");
        if (target is null || !TryGetMapPosition(target, out double x, out double y))
        {
            return false;
        }

        if (LevelTopology.LocateRegion(_level, x, y) is not { } region)
        {
            return false;
        }

        target["region"] = region;
        if (ReferenceEquals(target, GetObject("player")) && RegionAt(region) is { } here)
        {
            SetObjectReference("here", here);
        }

        return true;
    }

    /// <inheritdoc/>
    public double Random(double maximum) => _random.NextDouble() * maximum;

    /// <inheritdoc/>
    public void Randomize() => _random = new Random();

    /// <inheritdoc/>
    public bool IsSkillAtMinimum(string name) =>
        _bounds.TryGetValue(name, out SkillRange range) && GetSkill(name) <= range.Min;

    /// <inheritdoc/>
    public void Shake(AcknexObject target) => RecordGeometryInvalidation(target);

    /// <inheritdoc/>
    public bool DropObject(AcknexObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target is not MapEntity entity ||
            GetObject("player") is not Player player)
        {
            return false;
        }

        double distance = entity["DIST"];
        double angle = GetSkill("MOUSE_MODE") != 0 ? GetSkill("MOUSE_ANGLE") : GetSkill("PLAYER_ANGLE");
        Vector3 position = player.Position;
        entity.X = position.X + (Math.Cos(angle) * distance);
        entity.Y = position.Z + (Math.Sin(angle) * distance);
        entity.Region = player.Region;
        RecordGeometryInvalidation(entity);
        return true;
    }

    /// <inheritdoc/>
    public bool ShiftRegion(Region region, double dx, double dy)
    {
        ArgumentNullException.ThrowIfNull(region);
        int regionIndex = _regions.FindIndex(candidate => ReferenceEquals(candidate, region));
        if (regionIndex < 0 ||
            _level.Vertices is not IList<Vertex> vertices ||
            LevelTopology.TraceRegionLoop(_level, regionIndex) is not { } loop)
        {
            return false;
        }

        foreach (int vertexIndex in loop.Distinct())
        {
            Vertex vertex = vertices[vertexIndex];
            vertices[vertexIndex] = new Vertex(vertex.X + (float)dx, vertex.Y + (float)dy, vertex.Z);
        }

        foreach (MapEntity entity in _level.Things.Cast<MapEntity>().Concat(_level.Actors))
        {
            if (entity.Region == regionIndex)
            {
                entity.X += dx;
                entity.Y += dy;
            }
        }

        foreach (Player player in _objects.Values.OfType<Player>().Distinct())
        {
            if (player.Region == regionIndex)
            {
                Vector3 position = player.Position;
                player.Position = new Vector3(position.X + (float)dx, position.Y, position.Z + (float)dy);
            }
        }

        RecordGeometryInvalidation(region);
        return true;
    }

    /// <inheritdoc/>
    public bool LiftRegion(Region region, double amount)
    {
        ArgumentNullException.ThrowIfNull(region);
        int regionIndex = _regions.FindIndex(candidate => ReferenceEquals(candidate, region));
        if (regionIndex < 0 ||
            _level.Vertices is not IList<Vertex> vertices ||
            LevelTopology.TraceRegionLoop(_level, regionIndex) is not { } loop)
        {
            return false;
        }

        foreach (int vertexIndex in loop.Distinct())
        {
            Vertex vertex = vertices[vertexIndex];
            vertices[vertexIndex] = new Vertex(vertex.X, vertex.Y, vertex.Z + (float)amount);
        }

        foreach (MapEntity entity in _level.Things.Cast<MapEntity>().Concat(_level.Actors))
        {
            if (entity.Region == regionIndex)
            {
                entity.Z += amount;
            }
        }

        RecordGeometryInvalidation(region);
        return true;
    }

    /// <inheritdoc/>
    public bool RotateRegion(Region region, double radians)
    {
        ArgumentNullException.ThrowIfNull(region);
        int regionIndex = _regions.FindIndex(candidate => ReferenceEquals(candidate, region));
        if (regionIndex < 0 ||
            string.IsNullOrWhiteSpace(region.GeniusName) ||
            GetObject(region.GeniusName) is not { } center ||
            !TryGetMapPosition(center, out double centerX, out double centerY) ||
            _level.Vertices is not IList<Vertex> vertices ||
            LevelTopology.TraceRegionLoop(_level, regionIndex) is not { } loop)
        {
            return false;
        }

        double sin = Math.Sin(radians);
        double cos = Math.Cos(radians);
        foreach (int vertexIndex in loop.Distinct())
        {
            Vertex vertex = vertices[vertexIndex];
            (double x, double y) = RotateMapPoint(vertex.X, vertex.Y, centerX, centerY, sin, cos);
            vertices[vertexIndex] = new Vertex((float)x, (float)y, vertex.Z);
        }

        foreach (MapEntity entity in _level.Things.Cast<MapEntity>().Concat(_level.Actors))
        {
            if (entity.Region == regionIndex)
            {
                (entity.X, entity.Y) = RotateMapPoint(entity.X, entity.Y, centerX, centerY, sin, cos);
                entity.Angle += radians;
            }
        }

        foreach (Player player in _objects.Values.OfType<Player>().Distinct())
        {
            if (player.Region == regionIndex)
            {
                Vector3 position = player.Position;
                (double x, double y) = RotateMapPoint(position.X, position.Z, centerX, centerY, sin, cos);
                player.Position = new Vector3((float)x, position.Y, (float)y);
            }
        }

        RecordGeometryInvalidation(region);
        return true;
    }

    /// <inheritdoc/>
    public void Shoot(AcknexObject? source)
    {
        if (source is null)
        {
            ShootFromPlayer(GetSkill("SHOOT_RANGE"), useShootDeviation: true);
            return;
        }

        if (source is not MapEntity entity ||
            GetObject("player") is not Player player)
        {
            ClearHitResult();
            return;
        }

        double dx = player.Position.X - entity.X;
        double dy = player.Position.Z - entity.Y;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        double angle = Math.Atan2(dy, dx);
        SetSkill("SHOOT_ANGLE", angle);

        double range = GetSkill("SHOOT_RANGE");
        double sector = GetSkill("SHOOT_SECTOR");
        double delta = NormalizeAngle(angle - entity.FacingAngleRadians);
        if (range > 0 &&
            distance <= range &&
            // The original compares fixed-point angles: a full circle given as 6.2832 (truncated to
            // 6.2822) still takes in an object straight behind (OV-20261029-numeric-edge).
            Math.Abs(delta) <= (sector / 2) + (1 / WdlFixed.Scale) &&
            HasPortalLineOfSight(
                entity.X,
                entity.Y,
                player.Position.Y,
                Math.Cos(angle),
                Math.Sin(angle),
                distance,
                entity.Region,
                player.Region))
        {
            SetHitResult(distance, range);
            return;
        }

        ClearHitResult();
    }

    /// <inheritdoc/>
    public void Push(double distance) => ShootFromPlayer(distance, useShootDeviation: false);

    /// <inheritdoc/>
    public void Explode(AcknexObject source)
    {
        if (source is not MapEntity entity)
        {
            ClearHitResult();
            SetSkill("HIT_MINDIST", 0);
            return;
        }

        double range = GetSkill("SHOOT_RANGE");
        if (range <= 0)
        {
            ClearHitResult();
            SetSkill("HIT_MINDIST", 0);
            return;
        }

        double nearest = double.PositiveInfinity;
        AcknexObject? nearestTarget = null;
        foreach (AcknexObject target in FragileTargets(entity.Region))
        {
            if (!TryDistanceFromPoint(entity.X, entity.Y, target, out double distance) ||
                distance > range)
            {
                continue;
            }

            if (distance < nearest)
            {
                nearest = distance;
                nearestTarget = target;
            }

            SetHitResult(distance, range);
            TriggerObjectActionHook(target, "IF_HIT");
        }

        if (nearestTarget is not null)
        {
            SetObjectReference("hit", nearestTarget);
            SetSkill("HIT_MINDIST", nearest);
        }
        else
        {
            SetSkill("HIT_MINDIST", 0);
        }

        SetPlayerExplosionResult(entity.X, entity.Y, range);
    }

    /// <inheritdoc/>
    public void Beep() => BeepCount++;

    /// <summary>
    /// Records a host request to play a flic. The calling action goes on at once, and
    /// <c>FLIC_FRAME</c> reads 1 (the first frame is on screen) for the rest of it; the host
    /// keeps <c>FLIC_FRAME</c> at the frame on screen and sets it to 0 when the flic ends, is
    /// stopped, or cannot be played (OV-20261015-flic-play-blocking). A new flic replaces one that
    /// is playing. For a file that <see cref="FlicFileExists"/> says is missing, <c>FLIC_FRAME</c>
    /// keeps its value: 0, or the frame of the flic still playing (OV-20261015-flic-scheduler-detail).
    /// </summary>
    public void BeginFlicFile(string fileName)
    {
        _flicRequests.Add(new WdlFlicRequest(++_nextFlicRequestId, fileName, Streamed: true, StopsBefore: StopFlicCount));
        if (FlicFileExists?.Invoke(fileName) ?? true)
        {
            SetSkill(FlicFrameSkill, 1);
        }
    }

    /// <summary>
    /// Records a host request to stop the flic. The host ends it before the next frame; within
    /// the calling action <c>FLIC_FRAME</c> still reads the frame on screen
    /// (OV-20261015-flic-play-blocking).
    /// </summary>
    public void StopFlic() => StopFlicCount++;

    /// <inheritdoc/>
    public void Screenshot(string baseName, int number) =>
        _screenshotRequests.Add(new WdlScreenshotRequest(NormalizePrintBaseName(baseName), Math.Clamp(number, 0, 999)));

    /// <inheritdoc/>
    public void SaveGame(string baseName, int number, bool infoOnly) =>
        _persistenceRequests.Add(new WdlPersistenceRequest(
            WdlPersistenceOperation.Save,
            NormalizePrintBaseName(baseName),
            Math.Clamp(number, 0, 999),
            infoOnly));

    /// <inheritdoc/>
    public void LoadGame(string baseName, int number, bool infoOnly) =>
        _persistenceRequests.Add(new WdlPersistenceRequest(
            WdlPersistenceOperation.Load,
            NormalizePrintBaseName(baseName),
            Math.Clamp(number, 0, 999),
            infoOnly));

    /// <inheritdoc/>
    public bool IsKeywordReference(string name) => _keywordReferences.ContainsKey(name);

    /// <inheritdoc/>
    public bool TryGetKeywordReference(string name, out string? value) =>
        _keywordReferences.TryGetValue(name, out value);

    /// <inheritdoc/>
    public void SetKeywordReference(string name, string? value)
    {
        if (_keywordReferences.ContainsKey(name))
        {
            _keywordReferences[name] = value;
        }
    }

    /// <inheritdoc/>
    public bool TryGetUiNumber(string elementName, string memberName, out double value)
    {
        value = 0;
        if (ResolveUiElementName(elementName) is not { } resolved ||
            !_uiElements.TryGetValue(resolved, out WdlUiElement? element) ||
            element is null)
        {
            return false;
        }

        if (IsUiFlag(memberName))
        {
            value = HasUiFlag(element, memberName) ? 1 : 0;
            return true;
        }

        if (TryGetUiPropertyNumber(element, memberName, out value))
        {
            return true;
        }

        value = 0;
        return true;
    }

    /// <inheritdoc/>
    public bool TrySetUiNumber(string elementName, string memberName, double value)
    {
        if (ResolveUiElementName(elementName) is not { } resolved ||
            !_uiElements.TryGetValue(resolved, out WdlUiElement? element) ||
            element is null)
        {
            return false;
        }

        _uiElements[resolved] = IsUiFlag(memberName)
            ? SetUiFlag(element, memberName, value != 0)
            : SetUiPropertyNumber(element, memberName, value);
        return true;
    }

    /// <inheritdoc/>
    public bool TryGetUiKeyword(string elementName, string memberName, out string? value)
    {
        value = null;
        if (!IsUiKeywordProperty(memberName) ||
            ResolveUiElementName(elementName) is not { } resolved ||
            !_uiElements.TryGetValue(resolved, out WdlUiElement? element) ||
            element is null)
        {
            return false;
        }

        value = UiKeywordValue(element, memberName);
        return true;
    }

    /// <inheritdoc/>
    public bool TrySetUiKeyword(string elementName, string memberName, string? value)
    {
        if (!IsUiKeywordProperty(memberName) ||
            ResolveUiElementName(elementName) is not { } resolved ||
            !_uiElements.TryGetValue(resolved, out WdlUiElement? element) ||
            element is null)
        {
            return false;
        }

        _uiElements[resolved] = SetUiPropertyKeyword(element, memberName, value);
        return true;
    }

    /// <inheritdoc/>
    public void RequestExit(string? message)
    {
        ExitRequested = true;
        ExitMessage = message;
    }

    /// <inheritdoc/>
    public void RequestLevelChange(string levelName, string? resourceName)
    {
        RequestedLevelName = levelName;
        RequestedLevelResource = resourceName;
    }

    /// <inheritdoc/>
    public void RequestMapChange(string mapName) => RequestedMapName = mapName;

    /// <inheritdoc/>
    public AcknexObject? GetObject(string name) =>
        _objects.GetValueOrDefault(name) ??
        (_objectsByName.TryGetValue(name, out List<AcknexObject>? matches) ? matches.FirstOrDefault() : null);

    /// <inheritdoc/>
    public IEnumerable<AcknexObject> GetObjectsByName(string name) =>
        _objectsByName.GetValueOrDefault(name) ?? [];

    /// <inheritdoc/>
    public void SetObjectNumber(AcknexObject target, string memberName, double value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(memberName);
        if (target is MapEntity visibilityEntity &&
            memberName.Equals("INVISIBLE", StringComparison.OrdinalIgnoreCase))
        {
            visibilityEntity[memberName] = value;
            visibilityEntity["VISIBLE"] = value > 0 ? 0 : 1;
            return;
        }

        if (target is not MapEntity entity ||
            !memberName.Equals("GROUND", StringComparison.OrdinalIgnoreCase) ||
            entity.Region < 0 ||
            entity.Region >= _level.Regions.Count)
        {
            target[memberName] = value;
            return;
        }

        bool wasAbsolute = entity["GROUND"] > 0;
        bool becomesAbsolute = value > 0;
        if (wasAbsolute == becomesAbsolute)
        {
            entity[memberName] = value;
            return;
        }

        double floorHeight = _level.Regions[entity.Region].FloorHeight;
        double worldHeight = wasAbsolute ? entity.Z : floorHeight + entity.Z;
        entity[memberName] = value;
        entity.Z = becomesAbsolute ? worldHeight : worldHeight - floorHeight;
    }

    /// <summary>Registers a named object reference (e.g. <c>my</c>, <c>you</c>, or a level object).</summary>
    public void RegisterObject(string name, AcknexObject value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        SetObjectReference(name, value);
    }

    /// <inheritdoc/>
    public void SetObjectReference(string name, AcknexObject? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (value is null)
        {
            _objects.Remove(name);
            return;
        }

        _objects[name] = value;
    }

    /// <inheritdoc/>
    public bool AdvanceObjectReference(string name, WdlObjectIterationMode mode)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!_objects.TryGetValue(name, out AcknexObject? current))
        {
            return false;
        }

        List<AcknexObject> candidates = mode switch
        {
            WdlObjectIterationMode.SameName => MatchingNameCandidates(current),
            WdlObjectIterationMode.SameRegion => MatchingRegionCandidates(current),
            _ => [],
        };
        if (candidates.Count == 0)
        {
            return false;
        }

        int currentIndex = candidates.FindIndex(candidate => ReferenceEquals(candidate, current));
        int nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % candidates.Count;
        _objects[name] = candidates[nextIndex];
        return true;
    }

    /// <summary>
    /// Registers an object/action pair for object-level <c>EACH_TICK</c>, dispatched once per frame.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "This is intentionally an instance runtime API for host scheduler registration.")]
    public void RegisterEachTick(AcknexObject target, string actionName)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(actionName);
        SetObjectSchedulerAction(target, eachCycle: false, actionName);
    }

    /// <summary>
    /// Registers an object/action pair for <c>EACH_CYCLE</c>, dispatched when the host observes animation-cycle completion.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "This is intentionally an instance runtime API for host scheduler registration.")]
    public void RegisterEachCycle(AcknexObject target, string actionName)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(actionName);
        SetObjectSchedulerAction(target, eachCycle: true, actionName);
    }

    /// <summary>Triggers a target's current <c>EACH_CYCLE</c> action, if one is assigned.</summary>
    public bool TriggerEachCycle(AcknexObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return !IsInactiveEntity(target) &&
               TryGetObjectSchedulerAction(target, eachCycle: true, out string? actionName) &&
               actionName is not null &&
               RunForActionTarget(target, actionName);
    }

    /// <inheritdoc/>
    public void SetEachTickAction(int slot, string? actionName)
    {
        if (slot is < 1 or > EachTickSlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), "EACH_TICK slots are numbered 1 through 16.");
        }

        _eachTickActions[slot - 1] = string.IsNullOrWhiteSpace(actionName) ? null : actionName;
    }

    /// <inheritdoc/>
    public void SetEachSecAction(int slot, string? actionName)
    {
        if (slot is < 1 or > EachSecSlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), "EACH_SEC slots are numbered 1 through 16.");
        }

        // All slots share the runtime's second: arming a slot does not restart it, so the first run
        // comes when SECS next goes up (OV-20261015-each-sec-cadence).
        _eachSecActions[slot - 1] = string.IsNullOrWhiteSpace(actionName) ? null : actionName;
    }

    /// <summary>Reads back the action in a global <c>EACH_TICK</c> slot, or <c>null</c> when it is clear.</summary>
    /// <param name="slot">The one-based scheduler slot, from 1 through 16.</param>
    public string? GetEachTickAction(int slot) =>
        slot is < 1 or > EachTickSlotCount
            ? throw new ArgumentOutOfRangeException(nameof(slot), "EACH_TICK slots are numbered 1 through 16.")
            : _eachTickActions[slot - 1];

    /// <summary>Reads back the action in a global <c>EACH_SEC</c> slot, or <c>null</c> when it is clear.</summary>
    /// <param name="slot">The one-based scheduler slot, from 1 through 16.</param>
    public string? GetEachSecAction(int slot) =>
        slot is < 1 or > EachSecSlotCount
            ? throw new ArgumentOutOfRangeException(nameof(slot), "EACH_SEC slots are numbered 1 through 16.")
            : _eachSecActions[slot - 1];

    /// <inheritdoc/>
    public bool IsActionHook(string name) => IsActionHookName(name);

    /// <inheritdoc/>
    public void SetActionHook(string name, string? actionName)
    {
        if (IsActionHookName(name))
        {
            _actionHooks[name] = string.IsNullOrWhiteSpace(actionName) ? null : actionName;
        }
    }

    /// <summary>Whether an action with this name exists.</summary>
    public bool HasAction(string name) => ResolveActionName(name) is { } actionName && _actions.ContainsKey(actionName);

    /// <summary>Runs the level's <c>IF_START</c> startup action, if one is defined.</summary>
    /// <returns><c>true</c> if a startup action existed and ran.</returns>
    /// <remarks>
    /// <c>IF_START</c> counts as a cycle of its own with <c>TIME_CORR</c> 1 for the time waits it
    /// arms: a <c>WAITT 16</c> has 15 left when the first cycle starts (OV-20261029-scheduler-order).
    /// Whether a frame-count <c>WAIT</c> armed there counts it too is unmeasured; it does not here.
    /// </remarks>
    public bool RunStartup()
    {
        bool ran = _startupAction is not null && Run(_startupAction);
        CountDownWaitingActions(GetSkill(Timing.TimeCorrectionSkill), timeWaitsOnly: true);
        return ran;
    }

    /// <summary>Runs the named action's script, if it exists.</summary>
    /// <returns><c>true</c> if the action was found and executed.</returns>
    public bool RunAction(string name) => Run(name);

    /// <summary>Triggers a global <c>IF_*</c> action hook such as <c>IF_F1</c> or <c>IF_LEFT</c>.</summary>
    /// <returns><c>true</c> if the hook was assigned to an action that ran.</returns>
    public bool TriggerActionHook(string name) =>
        _actionHooks.TryGetValue(name, out string? actionName) &&
        actionName is not null &&
        Run(actionName);

    /// <summary>
    /// Triggers an object-level <c>IF_*</c> hook on a placed world object.
    /// Event detectors remain responsible for checking the documented distance, movement, or hit conditions.
    /// </summary>
    /// <returns><c>true</c> if the hook was assigned to an action that ran.</returns>
    public bool TriggerObjectActionHook(AcknexObject target, string name)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return !IsInactiveEntity(target) &&
               TryGetObjectActionHook(target, name, out string? actionName) &&
               actionName is not null &&
               RunForActionTarget(target, actionName);
    }

    /// <summary>Dispatches a documented vertical player-region transition without horizontal enter/leave hooks.</summary>
    public void DispatchPlayerVerticalTransition(PlayerVerticalTransition transition)
    {
        _observedPlayerRegion = transition.ToRegion;
        if (RegionAt(transition.ToRegion) is { } current)
        {
            SetObjectReference("here", current);
        }

        TriggerObjectActionHook(transition.Source, transition.HookName);
    }

    /// <summary>
    /// Advances the runtime by one frame lasting <paramref name="deltaSeconds"/>, refreshing the
    /// global <c>TIME_CORR</c> skill that movement/animation scripts scale by (see <see cref="Timing"/>).
    /// Global and object-level <c>EACH_TICK</c> hooks are dispatched after the correction is set.
    /// Waiting actions resume first and count the frame down last (see <see cref="CountDownWaitingActions"/>).
    /// Walls' <c>DISTANCE</c> is refreshed before the resumed actions (see <see cref="RefreshWallDistances"/>);
    /// things' and actors' only when the view changed or an actor in front of the player moved (see
    /// <see cref="RefreshEntityDistances"/>).
    /// </summary>
    /// <returns>The frame's time-correction factor.</returns>
    public double Tick(double deltaSeconds)
    {
        double timeCorrection = UpdateTimingSkills(deltaSeconds, out double elapsedSeconds);
        RefreshView();
        RefreshWallDistances();
        ResumeWaitingActions();
        DispatchPlayerRegionEvents();
        DispatchProximityEvents();
        ResetGlobalSchedulerDiagnostics();
        if (AdvanceSecond(elapsedSeconds))
        {
            DispatchGlobalEachSecActions();
        }

        DispatchGlobalEachTickActions();
        RunObjectPass(timeCorrection);
        CountDownWaitingActions(timeCorrection);
        return timeCorrection;
    }

    /// <summary>
    /// One scheduler cycle while a flic plays. The original runs one cycle per flic frame, with
    /// <c>TIME_CORR</c> the frame delay in 64 ms units (<see cref="Timing.FlicCycleSeconds"/>). Player
    /// and actor movement, animation cycles, region and proximity events, and the object and region
    /// <c>EACH_TICK</c>/<c>EACH_CYCLE</c> actions are paused (manual p.82,
    /// OV-20261015-flic-scheduler-detail). Waiting actions resume, then the global <c>EACH_SEC</c>
    /// slots (once per second) and <c>EACH_TICK</c> slots run, in the same order as
    /// <see cref="Tick"/> (OV-20261015-flic-play-blocking), and the waits count the cycle down.
    /// </summary>
    public double TickFlic(double deltaSeconds)
    {
        double timeCorrection = UpdateTimingSkills(deltaSeconds, out double elapsedSeconds, flic: true);
        ResumeWaitingActions();
        ResetGlobalSchedulerDiagnostics();
        if (AdvanceSecond(elapsedSeconds))
        {
            DispatchGlobalEachSecActions();
        }

        DispatchGlobalEachTickActions();
        CountDownWaitingActions(timeCorrection);
        return timeCorrection;
    }

    /// <summary>
    /// Sets <c>TIME_CORR</c>, <c>TIME_FAC</c> and <c>TICKS</c> for a cycle lasting
    /// <paramref name="deltaSeconds"/>. The first cycle keeps <c>TIME_CORR</c> at the 1 that
    /// <c>IF_START</c> saw and leaves <c>TICKS</c> (and the second) at 0, as patched V3.963 does;
    /// <c>TICKS</c> counts from the second cycle on (OV-20261029-scheduler-order).
    /// </summary>
    /// <param name="deltaSeconds">The cycle's duration.</param>
    /// <param name="elapsedSeconds">The time that counts toward <c>SECS</c>: 0 in the first cycle.</param>
    /// <param name="flic">
    /// A flic cycle, whose <c>TIME_CORR</c> comes from the frame delay; it is never the start-up
    /// cycle (whether a flic started in <c>IF_START</c> sees one is unmeasured).
    /// </param>
    private double UpdateTimingSkills(double deltaSeconds, out double elapsedSeconds, bool flic = false)
    {
        bool firstCycle = !_firstCycleRun;
        _firstCycleRun = true;
        if (firstCycle && !flic)
        {
            _skills[Timing.TimeCorrectionSkill] = 1;
            _skills[Timing.TimeFactorSkill] = 1;
            elapsedSeconds = 0;
            return 1;
        }

        elapsedSeconds = deltaSeconds;
        double timeCorrection = Timing.TimeCorrection(deltaSeconds);
        _skills[Timing.TimeCorrectionSkill] = timeCorrection;
        _skills[Timing.TimeFactorSkill] = Timing.TimeFactor(deltaSeconds);
        _skills[Timing.TicksSkill] = GetSkill(Timing.TicksSkill) + timeCorrection;
        return timeCorrection;
    }

    /// <summary>
    /// Advances the runtime's second by the frame's capped duration, the same time that feeds
    /// <c>TICKS</c>, so one second is 16 ticks. When the second completes, <c>SECS</c> goes up by
    /// one: the original keeps it a whole number of seconds and raises it after the frame's resumed
    /// <c>WAIT</c> actions, right before the <c>EACH_SEC</c> slots (OV-20261015-each-sec-cadence).
    /// A frame counts at most one second (<see cref="Timing.MaxFrameSeconds"/>), so a stall never
    /// causes a burst of catch-up seconds.
    /// </summary>
    /// <returns><c>true</c> if a second completed during this frame.</returns>
    private bool AdvanceSecond(double deltaSeconds)
    {
        _secondFraction += Timing.EffectiveFrameSeconds(deltaSeconds);
        if (_secondFraction < 1)
        {
            return false;
        }

        _secondFraction = Math.Min(_secondFraction - 1, Math.BitDecrement(1.0));
        _skills[Timing.SecondsSkill] = GetSkill(Timing.SecondsSkill) + 1;
        return true;
    }

    /// <summary>Clears the per-frame counters that describe the global scheduler actions' work.</summary>
    private void ResetGlobalSchedulerDiagnostics()
    {
        LastTickPlayerVelocityWriteCount = 0;
        LastTickGlobalStatementCount = 0;
        LastTickPlayerVelocityStatementCount = 0;
        LastTickUnsupportedExpressionCount = 0;
        LastTickTrueConditionCount = 0;
        LastTickFalseConditionCount = 0;
    }

    private void DispatchGlobalEachTickActions()
    {
        _dispatchingGlobalEachTick = true;
        try
        {
            foreach (string? actionName in _eachTickActions)
            {
                if (actionName is not null)
                {
                    Run(actionName);
                }
            }
        }
        finally
        {
            _dispatchingGlobalEachTick = false;
        }
    }

    /// <summary>
    /// Runs every occupied global <c>EACH_SEC</c> slot once, in slot order, in a frame where the
    /// runtime's second completed. The original runs them just before the global <c>EACH_TICK</c>
    /// slots of that frame, and an action may <c>WAIT</c>: each second then starts another instance
    /// while earlier ones go on waiting (OV-20261015-each-sec-cadence).
    /// </summary>
    private void DispatchGlobalEachSecActions()
    {
        bool wasDispatching = _dispatchingGlobalEachTick;
        _dispatchingGlobalEachTick = true;
        try
        {
            for (int i = 0; i < _eachSecActions.Length; i++)
            {
                if (_eachSecActions[i] is { } actionName)
                {
                    Run(actionName);
                }
            }
        }
        finally
        {
            _dispatchingGlobalEachTick = wasDispatching;
        }
    }

    /// <inheritdoc/>
    public void CallAction(string name) => Run(name);

    /// <inheritdoc/>
    public bool TryGetAction(string name, [NotNullWhen(true)] out WdlBlock? body)
    {
        body = null;
        return ResolveActionName(name) is { } actionName && _actions.TryGetValue(actionName, out body);
    }

    private void RegisterLevelObjects(Level level)
    {
        foreach (Region obj in level.Regions)
        {
            _regions.Add(obj);
            RegisterNamedObject(obj);
        }

        foreach (AcknexObject obj in level.Walls)
        {
            RegisterNamedObject(obj);
        }

        foreach (AcknexObject obj in level.Things)
        {
            RegisterLevelObject(obj);
        }

        foreach (AcknexObject obj in level.Actors)
        {
            RegisterLevelObject(obj);
        }
    }

    private void RegisterLevelObject(AcknexObject value)
    {
        _levelObjects.Add(value);
        RegisterNamedObject(value);
    }

    private List<AcknexObject> MatchingNameCandidates(AcknexObject current)
    {
        if (!_objectsByName.TryGetValue(current.Name, out List<AcknexObject>? matches))
        {
            return [];
        }

        Type currentType = current.GetType();
        return matches.Where(candidate => candidate.GetType() == currentType).ToList();
    }

    private List<AcknexObject> MatchingRegionCandidates(AcknexObject current)
    {
        if (current is not MapEntity entity)
        {
            return [];
        }

        return _levelObjects
            .OfType<MapEntity>()
            .Where(candidate => candidate.Region == entity.Region)
            .Cast<AcknexObject>()
            .ToList();
    }

    private void RegisterNamedObject(AcknexObject value)
    {
        if (!_objectsByName.TryGetValue(value.Name, out List<AcknexObject>? matches))
        {
            matches = [];
            _objectsByName[value.Name] = matches;
        }

        matches.Add(value);

        if (_ambiguousObjectNames.Contains(value.Name))
        {
            return;
        }

        if (_objects.ContainsKey(value.Name))
        {
            _objects.Remove(value.Name);
            _ambiguousObjectNames.Add(value.Name);
            return;
        }

        _objects[value.Name] = value;
    }

    private bool RunForActionTarget(AcknexObject target, string actionName)
    {
        bool hadMy = _objects.TryGetValue("my", out AcknexObject? previousMy);
        bool hadThere = _objects.TryGetValue("there", out AcknexObject? previousThere);
        AcknexObject? previousActionTarget = _activeActionTarget;
        bool bindMy = target is not Region;
        Region? targetRegion = RegionForActionTarget(target);
        if (bindMy)
        {
            _objects["my"] = target;
        }
        else
        {
            _objects.Remove("my");
        }

        if (targetRegion is not null)
        {
            _objects["there"] = targetRegion;
        }
        else
        {
            _objects.Remove("there");
        }

        _activeActionTarget = target;

        try
        {
            return Run(actionName);
        }
        finally
        {
            _activeActionTarget = previousActionTarget;
            if (hadMy)
            {
                _objects["my"] = previousMy!;
            }
            else
            {
                _objects.Remove("my");
            }

            if (hadThere)
            {
                _objects["there"] = previousThere!;
            }
            else
            {
                _objects.Remove("there");
            }
        }
    }

    private bool Run(string name)
    {
        // Guard against runaway mutual recursion between actions.
        string? actionName = ResolveActionName(name);
        if (_callDepth >= MaxCallDepth || actionName is null || !_actions.TryGetValue(actionName, out WdlBlock? block))
        {
            return false;
        }

        _callDepth++;
        try
        {
            WdlExecutionState state = WdlInterpreter.Start(block);
            WdlExecutionResult result = _interpreter.Continue(state);
            StoreSuspendedAction(state, result, CaptureActionScope());
        }
        catch (WdlExpressionException ex)
        {
            AbortAction(actionName, ex);
        }
        finally
        {
            _callDepth--;
        }

        return true;
    }

    /// <summary>The last action stopped by an arithmetic or execution-limit error, and why; <c>null</c> if none.</summary>
    public string? LastActionError { get; private set; }

    /// <summary>
    /// Stops an action that hit a division by zero (which crashes the DOS runtime,
    /// OV-20261029-numeric-edge) or a runaway-loop limit; its caller and the cycle go on.
    /// </summary>
    private void AbortAction(string actionName, WdlExpressionException ex)
    {
        LastActionError = $"{actionName}: {ex.Message}";
        Console.Error.WriteLine($"WDL action stopped: {LastActionError}");
    }

    private bool IsPlayerMovementAction(string? name) =>
        name is not null &&
        ResolveActionName(name) is { } actionName &&
        _actions.TryGetValue(actionName, out WdlBlock? body) &&
        WdlCoverageAnalyzer.ReferencesPlayerMovement(body);

    private void InitializePredefinedSkillDefaults()
    {
        // The manual says 0.5 (p.112); patched V3.963 reads 0 (OV-20261029-motion-timing).
        EnsureSkillDefault("FRICTION", 0);
        EnsureSkillDefault("INERTIA", 1);
        EnsureSkillDefault("SHOOT_RANGE", 500);
        EnsureSkillDefault("SHOOT_FAC", 1);
        EnsureSkillDefault("SHOOT_SECTOR", Math.Tau);
        EnsureSkillDefault("SHOOT_X", 0);
        EnsureSkillDefault("SHOOT_Y", 0);
        EnsureSkillDefault("KEY_SENSE", 0.7);
        EnsureSkillDefault("SHIFT_SENSE", 2);
        EnsureSkillDefault("MOUSE_SENSE", 1);
        EnsureSkillDefault("JOY_SENSE", 1);
        EnsureSkillDefault("MOUSE_MODE", 0);
        EnsureSkillDefault("TOUCH_MODE", 1);
        EnsureSkillDefault("MOUSE_MOVING", 0);
        EnsureSkillDefault("MOUSE_CALM", 3);
        EnsureSkillDefault("MOUSE_TIME", 4);
        EnsureSkillDefault("MICKEY_X", 0);
        EnsureSkillDefault("MICKEY_Y", 0);
        EnsureSkillDefault("MOUSE_X", 0);
        EnsureSkillDefault("MOUSE_Y", 0);
        EnsureSkillDefault("MOUSE_ANGLE", 0);
        EnsureSkillDefault("PLAYER_WIDTH", 1.2);
        EnsureSkillDefault("THING_DIST", 1);
        EnsureSkillDefault("ACTOR_DIST", 1);
        EnsureSkillDefault("PLAYER_SIZE", 3);
        EnsureSkillDefault("PLAYER_CLIMB", 1.5);
        EnsureSkillDefault("PLAYER_TILT", 0);
        EnsureValidSkillDefault("PLAYER_ARC", 1, value => value is >= 0.2 and <= 2);
        EnsureValidSkillDefault("ASPECT", 1, value => value is >= 0.1 and <= 10);
        EnsureSkillDefault("PLAYER_LIGHT", 1);
        EnsureSkillDefault("LIGHT_DIST", 10);
        EnsureSkillDefault("SOUND_VOL", 1);
        EnsureSkillDefault("MUSIC_VOL", 1);
        EnsureSkillDefault("WALK_PERIOD", 4);
        EnsureSkillDefault("WALK_TIME", 4);
        EnsureSkillDefault("WAVE_PERIOD", 16);
        EnsureSkillDefault("WALK", 0);
        EnsureSkillDefault("WAVE", 0);
        EnsureSkillDefault("PSOUND_VOL", 1);
        EnsureSkillDefault("PSOUND_TONE", 1);
        EnsureSkillDefault("CHANNEL", -1);
        for (int channel = 0; channel < 8; channel++)
        {
            EnsureSkillDefault($"CHANNEL_{channel}", 0);
        }

        // OV-20261019-border-kinds: the runtime starts ACTOR_CLIMB at 1.1 (the manual says 1, p.113).
        EnsureSkillDefault("ACTOR_CLIMB", 1.1);
        EnsureSkillDefault("SKIP_FRAMES", 5);
        EnsureSkillDefault("ACTOR_WIDTH", 1);
        EnsureSkillDefault("THING_WIDTH", 1);
        EnsureSkillDefault("SCREEN_X", 0);
        EnsureSkillDefault("SCREEN_Y", 0);
        EnsureSkillDefault("SCREEN_WIDTH", _level.VideoMode?.Width ?? 640);
        EnsureSkillDefault("SCREEN_HGT", _level.VideoMode?.Height ?? 480);
    }

    private void EnsureSkillDefault(string name, double value)
    {
        if (!_skills.ContainsKey(name) || !_level.ExplicitlyInitializedSkills.Contains(name))
        {
            _skills[name] = value;
        }
    }

    private void EnsureValidSkillDefault(string name, double value, Func<double, bool> isValid)
    {
        if (!_skills.TryGetValue(name, out double current) ||
            !_level.ExplicitlyInitializedSkills.Contains(name) ||
            !double.IsFinite(current) ||
            !isValid(current))
        {
            _skills[name] = value;
        }
    }

    /// <summary>
    /// Resumes, in arming order, the waiting actions whose count has dropped below 1. With
    /// <see cref="CountDownWaitingActions"/> at the end of the frame, <c>WAIT n</c> resumes n frames
    /// after the frame it was armed in, and <c>WAITT n</c> in the first frame where the time counted
    /// from the start of its arming frame exceeds n − 1 ticks (OV-20261015-flic-scheduler-detail).
    /// </summary>
    private void ResumeWaitingActions()
    {
        int initialCount = _waitingActions.Count;
        for (int processed = 0; processed < initialCount; processed++)
        {
            ActiveAction action = _waitingActions[0];
            _waitingActions.RemoveAt(0);

            if (IsInactiveEntity(action.Scope.Target) || action.Remaining >= 1)
            {
                _waitingActions.Add(action);
                continue;
            }

            try
            {
                WdlExecutionResult result = ContinueWithScope(action);
                StoreSuspendedAction(action.State, result, action.Scope);
            }
            catch (WdlExpressionException ex)
            {
                AbortAction("(resumed action)", ex);
            }
        }
    }

    /// <summary>
    /// Counts every waiting action down by the frame that is ending: one frame cycle for
    /// <c>WAIT</c>, the frame's <c>TIME_CORR</c> for <c>WAITT</c>. It runs last in the frame, so an
    /// action armed during the frame (or by a hook or a host call before it) counts that frame too
    /// (OV-20261015-flic-scheduler-detail). Actions of inactive entities do not count down.
    /// </summary>
    private void CountDownWaitingActions(double timeCorrection, bool timeWaitsOnly = false)
    {
        foreach (ActiveAction action in _waitingActions)
        {
            if (!IsInactiveEntity(action.Scope.Target) &&
                !(timeWaitsOnly && action.Mode == WdlWaitMode.FrameCycles))
            {
                action.Remaining -= action.Mode == WdlWaitMode.FrameCycles ? 1 : timeCorrection;
            }
        }
    }

    private WdlExecutionResult ContinueWithScope(ActiveAction action)
    {
        return ContinueWithScope(action.State, action.Scope);
    }

    private WdlExecutionResult ContinueWithScope(WdlExecutionState state, ActionScope scope)
    {
        ActionScope previous = CaptureActionScope();
        ApplyActionScope(scope);
        try
        {
            return _interpreter.Continue(state);
        }
        finally
        {
            ApplyActionScope(previous);
        }
    }

    private void StoreSuspendedAction(WdlExecutionState state, WdlExecutionResult result, ActionScope scope)
    {
        switch (result)
        {
            case { Status: WdlExecutionStatus.Waiting, Wait: { } wait }:
                _waitingActions.Add(new ActiveAction(state, wait, scope));
                break;
            case { Status: WdlExecutionStatus.InputWaiting, Input: { } input }:
                _inputActions[input.Id] = new ActiveInputAction(state, input, scope);
                break;
        }
    }

    private ActionScope CaptureActionScope()
    {
        bool hasMy = _objects.TryGetValue("my", out AcknexObject? my);
        bool hasThere = _objects.TryGetValue("there", out AcknexObject? there);
        return new ActionScope(hasMy, my, hasThere, there, _activeActionTarget);
    }

    private void ApplyActionScope(ActionScope scope)
    {
        _activeActionTarget = scope.Target;
        if (scope.HasMy)
        {
            _objects["my"] = scope.My!;
        }
        else
        {
            _objects.Remove("my");
        }

        if (scope.HasThere)
        {
            _objects["there"] = scope.There!;
        }
        else
        {
            _objects.Remove("there");
        }
    }

    private void InitializeSynonyms(Level level)
    {
        foreach ((string name, WdlSynonym synonym) in level.Synonyms)
        {
            if (IsObjectSynonymType(synonym.Type))
            {
                if (synonym.DefaultKeyword is not null &&
                    TryResolveObjectByType(synonym.DefaultKeyword, synonym.Type, out AcknexObject? value))
                {
                    SetObjectReference(name, value);
                }

                continue;
            }

            if (IsKeywordSynonymType(synonym.Type))
            {
                _keywordReferences[name] = synonym.DefaultKeyword;
                _keywordReferenceTypes[name] = synonym.Type;
            }
        }
    }

    private void InitializePredefinedReferences(Level level)
    {
        if (level.PlayerStart is not { } playerStart)
        {
            return;
        }

        if (RegionAt(playerStart.Region) is { } here)
        {
            SetObjectReference("here", here);
        }

        // The WMP start heading (degrees, counter-clockwise, 0 = map +x) is the initial
        // PLAYER_ANGLE; the host's camera yaw is derived from the same value (RenderSpace).
        double angle = playerStart.AngleRadians;
        SetSkill("PLAYER_ANGLE", angle);
        SetSkill("PLAYER_SIN", Math.Sin(angle));
        SetSkill("PLAYER_COS", Math.Cos(angle));
    }

    private void InitializeEachTickActions(Level level)
    {
        int count = Math.Min(_eachTickActions.Length, level.EachTickActions.Count);
        for (int i = 0; i < count; i++)
        {
            _eachTickActions[i] = string.IsNullOrWhiteSpace(level.EachTickActions[i]) ? null : level.EachTickActions[i];
        }
    }

    private void InitializeEachSecActions(Level level)
    {
        int count = Math.Min(_eachSecActions.Length, level.EachSecActions.Count);
        for (int i = 0; i < count; i++)
        {
            _eachSecActions[i] = string.IsNullOrWhiteSpace(level.EachSecActions[i]) ? null : level.EachSecActions[i];
        }
    }

    private void InitializeActionHooks(Level level)
    {
        foreach ((string name, string? actionName) in level.ActionHooks)
        {
            SetActionHook(name, actionName);
        }
    }

    private static bool IsActionHookName(string name) =>
        name.StartsWith("IF_", StringComparison.OrdinalIgnoreCase) &&
        !name.Equals("IF_START", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Fires <c>IF_LEAVE</c>/<c>IF_ENTER</c> when the player's region changed since the last cycle.
    /// Only movement across a wall and <c>LOCATE</c> change it. A write to <c>PLAYER_X/PLAYER_Y</c>
    /// leaves the region (and <c>HERE</c>) as it was, so a beam fires nothing
    /// (OV-20261019-proximity-events).
    /// </summary>
    private void DispatchPlayerRegionEvents()
    {
        if (GetObject("player") is not Player player ||
            RegionAt(player.Region) is not { } currentRegion)
        {
            return;
        }

        SetObjectReference("here", currentRegion);
        if (_observedPlayerRegion is null)
        {
            _observedPlayerRegion = player.Region;
            return;
        }

        int previousRegion = _observedPlayerRegion.Value;
        if (previousRegion == player.Region)
        {
            return;
        }

        _observedPlayerRegion = player.Region;
        if (RegionAt(previousRegion) is { } previous)
        {
            TriggerObjectActionHook(previous, "IF_LEAVE");
        }

        TriggerObjectActionHook(currentRegion, "IF_ENTER");
    }

    /// <summary>
    /// Fires <c>IF_NEAR</c> once when the player comes within an object's <c>DIST</c> and
    /// <c>IF_FAR</c> once when they leave it. Checked every cycle from the current positions, so
    /// walking, a beam and a moving actor all count; standing within <c>DIST</c> does not repeat
    /// the hook (OV-20261019-proximity-events). A wall's <c>IF_NEAR</c> also fires in every cycle
    /// after the player's movement passed through the wall or pushed against it, unless the player
    /// is within its <c>DIST</c>. This leaves the <c>DIST</c> state alone, so no <c>IF_FAR</c>
    /// follows, and with <c>DIST 0</c> it is the only way the wall fires (OV-20261019-wall-pass-clip).
    /// The same holds for a wall between two regions, and for a thing or actor the player pushed
    /// against. A thing or actor outside its <c>CLIP_DIST</c> border (see
    /// <see cref="IsWithinClipDistance"/>) is not tested at all and keeps its state, so it fires
    /// <c>IF_NEAR</c> when it comes within the border while the player is within <c>DIST</c>, and
    /// no <c>IF_FAR</c> when it leaves the border (OV-20261019-clip-contact).
    /// </summary>
    private void DispatchProximityEvents()
    {
        if (GetObject("player") is not Player player)
        {
            _proximityStates.Clear();
            return;
        }

        AcknexObject[] touched = player.TakeTouchedObjects();
        foreach (AcknexObject target in CurrentProximityTargets())
        {
            if (!ShouldTrackProximity(target))
            {
                _proximityStates.Remove(target);
                continue;
            }

            bool withinBorder = IsWithinClipDistance(target);
            if (target is MapEntity && !withinBorder)
            {
                continue;
            }

            // OV-20261019-clip-detail: a wall outside its CLIP_DIST border is not DIST-tested either
            // and keeps its state; passing or touching it still counts.
            bool wasNear = _proximityStates.TryGetValue(target, out bool previous) && previous;
            bool isNear = wasNear;
            if (withinBorder)
            {
                if (TryCurrentProximity(player, target, out isNear))
                {
                    _proximityStates[target] = isNear;
                }
                else
                {
                    _proximityStates.Remove(target);
                    wasNear = false;
                }
            }

            if (isNear && !wasNear)
            {
                TriggerObjectActionHook(target, "IF_NEAR");
            }
            else if (!isNear && wasNear)
            {
                TriggerObjectActionHook(target, "IF_FAR");
            }
            else if (!isNear && Array.IndexOf(touched, target) >= 0)
            {
                TriggerObjectActionHook(target, "IF_NEAR");
            }
        }
    }

    private IEnumerable<AcknexObject> CurrentProximityTargets()
    {
        foreach (MapEntity entity in _levelObjects.OfType<MapEntity>())
        {
            yield return entity;
        }

        foreach (Wall wall in _level.Walls)
        {
            yield return wall;
        }
    }

    private static bool ShouldTrackProximity(AcknexObject target) =>
        !IsInactiveEntity(target) &&
        (TryGetObjectActionHook(target, "IF_NEAR", out _) ||
         TryGetObjectActionHook(target, "IF_FAR", out _));

    private bool TryCurrentProximity(Player player, AcknexObject target, out bool isNear)
    {
        double threshold = target["DIST"];
        if (threshold <= 0)
        {
            isNear = false;
            return false;
        }

        if (!TargetCanSeePlayerRegion(target, player.Region) ||
            !TryDistTestDistance(player, target, out double distance))
        {
            isNear = false;
            return true;
        }

        target[DistanceSkill] = distance;
        // OV-20261019-actor-contact: the border itself is outside (strictly less than DIST), and a
        // thing or actor is near only while the eye is also less than DIST above or below its centre.
        isNear = distance < threshold &&
                 (target is not MapEntity entity || VerticalOffsetFromCentre(player, entity) < threshold);
        return true;
    }

    /// <summary>
    /// How far the player's eye lies above or below the centre of a thing's or actor's texture
    /// (its feet plus half its height); 0 when the size is unknown.
    /// </summary>
    private double VerticalOffsetFromCentre(Player player, MapEntity entity) =>
        MapEntitySizing.TryGetSize(_level, entity, out MapEntitySize size)
            ? Math.Abs(player.Position.Y - (EntityFeetHeight(entity) + (size.Height / 2)))
            : 0;

    /// <summary>
    /// Sets every wall's <c>DISTANCE</c> to the value the <c>DIST</c> test compares with (see
    /// <see cref="TryProximityDistance"/>), once a cycle before the cycle's actions
    /// (OV-20261019-wall-distance). The manual calls it approximate and only valid within the
    /// player's <c>CLIP_DIST</c> (p.52); the original computes a wall's outside it too. A beam
    /// reaches the player's position only after the cycle, when the host applies
    /// <c>PLAYER_X/PLAYER_Y</c>, so it shows in the next cycle as in the original. Things and
    /// actors keep the value of the last refresh instead (<see cref="RefreshEntityDistances"/>).
    /// </summary>
    private void RefreshWallDistances()
    {
        if (GetObject("player") is not Player player)
        {
            return;
        }

        foreach (Wall wall in _level.Walls)
        {
            if (TryProximityDistance(player.Position.X, player.Position.Z, wall, out double distance))
            {
                wall[DistanceSkill] = distance;
            }
        }
    }

    /// <summary>
    /// The distance the <c>DIST</c> test compares with: a thing's or actor's as last refreshed
    /// (<see cref="RefreshEntityDistances"/>, OV-20261021-distance-refresh), a wall's current one.
    /// </summary>
    private bool TryDistTestDistance(Player player, AcknexObject target, out double distance) =>
        target is MapEntity entity && _entityDistances.TryGetValue(entity, out distance)
            ? true
            : TryProximityDistance(player.Position.X, player.Position.Z, target, out distance);

    /// <summary>
    /// The distance a <c>DIST</c> border is compared with, the value <c>MY.DISTANCE</c> reads in
    /// the original: |dx| + |dy| between the player and a thing's or actor's center
    /// (OV-20261019-proximity-events), or the nearer of a wall's two vertices
    /// (OV-20261019-wall-distance).
    /// </summary>
    private bool TryProximityDistance(double x, double y, AcknexObject target, out double distance)
    {
        switch (target)
        {
            case MapEntity entity:
                distance = Math.Abs(entity.X - x) + Math.Abs(entity.Y - y);
                return true;
            case Wall wall when TryGetWallSegment(wall, out double ax, out double ay, out double bx, out double by):
                distance = Math.Min(
                    Math.Abs(ax - x) + Math.Abs(ay - y),
                    Math.Abs(bx - x) + Math.Abs(by - y));
                return true;
            default:
                distance = 0;
                return false;
        }
    }

    /// <summary>
    /// Whether an object's actions run this cycle. The original runs a wall's <c>EACH_TICK</c> only
    /// while its distance from the player (<see cref="TryProximityDistance"/>) is below
    /// <c>CLIP_DIST</c>, a thing's below <c>CLIP_DIST × THING_DIST</c> and an actor's below
    /// <c>CLIP_DIST × ACTOR_DIST</c> (OV-20261019-wall-pass-clip). <c>CLIP_DIST</c> is the smaller
    /// of the player's region's and the object's own region's (a wall's: its side regions'). Outside
    /// that border a thing or actor also runs no <c>EACH_CYCLE</c>, does not move and is not
    /// <c>DIST</c>-tested, unless it is <c>LIBER</c>. A <c>BERKELEY</c> thing or actor is inactive
    /// while the player is outside its <c>DIST</c>, even in view (OV-20261019-clip-contact).
    /// Regions are not gated.
    /// </summary>
    private bool IsWithinClipDistance(AcknexObject target)
    {
        if (target is not (Wall or MapEntity) ||
            GetObject("player") is not Player player ||
            RegionAt(player.Region) is not { } region ||
            !TryProximityDistance(player.Position.X, player.Position.Z, target, out double distance))
        {
            return true;
        }

        if (target is MapEntity && target["BERKELEY"] > 0 && distance > target["DIST"])
        {
            return false;
        }

        return target["LIBER"] > 0 || distance < ClipBorder(target, region);
    }

    /// <summary>
    /// The <c>CLIP_DIST</c> border of a wall, thing or actor, ignoring <c>LIBER</c>. A thing's or
    /// actor's is the smaller of the player's region's and its own region's <c>CLIP_DIST</c>
    /// (OV-20261019-clip-contact), times <c>THING_DIST</c> or <c>ACTOR_DIST</c>. A wall's is the
    /// player's region's alone, even for a wall between two regions (OV-20261019-clip-detail).
    /// </summary>
    private double ClipBorder(AcknexObject target, AcknexObject playerRegion)
    {
        double clipDistance = playerRegion["CLIP_DIST"];
        if (target is MapEntity entity && RegionAt(entity.Region) is { } ownRegion)
        {
            clipDistance = Math.Min(clipDistance, ownRegion["CLIP_DIST"]);
        }

        return clipDistance * target switch
        {
            Actor => GetSkill("ACTOR_DIST"),
            MapEntity => GetSkill("THING_DIST"),
            _ => 1,
        };
    }

    private static bool TargetCanSeePlayerRegion(AcknexObject target, int playerRegion) =>
        target switch
        {
            MapEntity entity => entity.Region == playerRegion,
            Wall wall => wall.LeftRegion == playerRegion || wall.RightRegion == playerRegion,
            _ => false,
        };

    /// <summary>
    /// Moves one actor toward its target, in its own turn of the object pass, before its
    /// <c>EACH_TICK</c>: a <c>WAY</c> actor's <c>IF_ARRIVED</c> runs after the global slot actions
    /// and before the actor's <c>EACH_TICK</c> in the same cycle (OV-20261029-actor-events).
    /// </summary>
    private void AdvanceActorTarget(Actor actor, double cycleCorrection)
    {
        MoveActorTowardTarget(actor, cycleCorrection);
        _longSkippedStep = false;
    }

    private void MoveActorTowardTarget(Actor actor, double cycleCorrection)
    {
        double timeCorrection = cycleCorrection;
        _longSkippedStep = false;
        actor["MOVED"] = 0;
        actor["IMPACT_VX"] = 0;
        actor["IMPACT_VY"] = 0;
        actor["IMPACT_VZ"] = 0;
        actor["IMPACT_VROT"] = 0;
        string? target = actor.Target;
        LevelWay? way = null;
        // A TARGET naming a way assigns it (p.66), like the WAY property.
        bool followsWay = (target ?? actor.WayName) is { } wayName &&
                          _level.Ways.TryGetValue(wayName, out way) &&
                          way.Points.Count > 0;
        if (timeCorrection <= 0 || actor["INVISIBLE"] > 0 || (target is null && !followsWay))
        {
            _arrivedTargets.Remove(actor);
            _actorTargetStates.Remove(actor);
            return;
        }

        // OV-20261019-clip-contact: an actor outside its CLIP_DIST border does not move.
        if (!IsWithinClipDistance(actor))
        {
            return;
        }

        int cycles = SkipFramesMultiplier(actor);
        if (cycles == 0)
        {
            return;
        }

        timeCorrection = cycleCorrection * cycles;
        _longSkippedStep = cycles > 1;
        double startX = actor.X;
        double startY = actor.Y;

        string targetState = followsWay ? $"WAY:{way!.Name}" : target!;
        bool targetChanged = !_actorTargetStates.TryGetValue(actor, out string? previousTarget) ||
                             !previousTarget.Equals(targetState, StringComparison.OrdinalIgnoreCase);
        _actorTargetStates[actor] = targetState;
        if (targetChanged && targetState.Equals("STICK", StringComparison.OrdinalIgnoreCase))
        {
            InitializeStickTarget(actor);
        }

        if (followsWay)
        {
            if (AdvanceActorWay(actor, way!, timeCorrection))
            {
                TriggerObjectActionHook(actor, "IF_ARRIVED");
            }

            _arrivedTargets.Remove(actor);
            NoteFrontMove(actor, startX, startY);
            return;
        }

        bool arrived = target!.ToUpperInvariant() switch
        {
            "MOVE" or "BULLET" => AdvanceActorForward(actor, timeCorrection),
            "FOLLOW" => AdvanceActorRelativeToPlayer(actor, timeCorrection, repel: false),
            "REPEL" => AdvanceActorRelativeToPlayer(actor, timeCorrection, repel: true),
            "VERTEX" => AdvanceActorToward(actor, actor["TARGET_X"], actor["TARGET_Y"], timeCorrection),
            "STICK" => AdvanceStickActor(actor, timeCorrection),
            "HOLD" or "NODE1" or "NODE2" => false,
            _ => AdvanceActorTowardNamedTarget(actor, target, timeCorrection),
        };

        // A VERTEX or object target triggers IF_ARRIVED in every cycle the actor is there
        // (OV-20261029-actor-events).
        if (arrived)
        {
            _arrivedTargets.Add(actor);
            TriggerObjectActionHook(actor, "IF_ARRIVED");
        }
        else
        {
            _arrivedTargets.Remove(actor);
        }

        NoteFrontMove(actor, startX, startY);
    }

    private bool AdvanceActorWay(Actor actor, LevelWay way, double timeCorrection)
    {
        int waypoint = (int)Math.Truncate(actor["WAYPOINT"]);
        if (waypoint <= 0)
        {
            waypoint = 1;
        }
        else if (waypoint > way.Points.Count)
        {
            waypoint = ((waypoint - 1) % way.Points.Count) + 1;
        }

        actor["WAYPOINT"] = waypoint;
        LevelWayPoint point = way.Points[waypoint - 1];
        bool arrived = AdvanceActorToward(actor, point.X, point.Y, timeCorrection);
        if (!arrived)
        {
            return false;
        }

        // WAYPOINT moves on before IF_ARRIVED runs; TARGET_X/Y still show the point reached until the
        // next step (OV-20261029-actor-events).
        actor["WAYPOINT"] = (waypoint % way.Points.Count) + 1;
        return true;
    }

    private void PrepareActorTickSkills(AcknexObject target)
    {
        if (target is not Actor actor)
        {
            return;
        }

        SetSkill("ACTOR_IMPACT_VX", actor["IMPACT_VX"]);
        SetSkill("ACTOR_IMPACT_VY", actor["IMPACT_VY"]);
        SetSkill("ACTOR_IMPACT_VZ", actor["IMPACT_VZ"]);
        SetSkill("IMPACT_VROT", actor["IMPACT_VROT"]);
        if (actor.Region >= 0 && actor.Region < _level.Regions.Count)
        {
            Region region = _level.Regions[actor.Region];
            double offset = LevelTopology.TryGetSurfaceHeightOffset(
                _level,
                actor.Region,
                actor.X,
                actor.Y,
                out double value) ? value : 0;
            SetSkill("ACTOR_FLOOR_HGT", region.FloorHeight + offset);
            SetSkill("ACTOR_CEIL_HGT", region.CeilHeight + offset);
        }
    }

    private bool AdvanceActorForward(Actor actor, double timeCorrection)
    {
        double distance = Math.Max(0, actor["SPEED"]) * timeCorrection;
        if (distance <= 0)
        {
            return false;
        }

        // The step is SPEED·TIME_CORR in |dx| + |dy|, like a step toward a point (OV-20261029-actor-events).
        double angle = actor.FacingAngleRadians;
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        double norm = Math.Abs(cos) + Math.Abs(sin);
        double previousX = actor.X;
        double previousY = actor.Y;
        MoveActor(actor, actor.X + (cos / norm * distance), actor.Y + (sin / norm * distance));
        double actualDistance = Math.Sqrt(
            ((actor.X - previousX) * (actor.X - previousX)) +
            ((actor.Y - previousY) * (actor.Y - previousY)));
        actor.Z += actualDistance * actor["VSPEED"];

        return false;
    }

    private bool AdvanceActorRelativeToPlayer(Actor actor, double timeCorrection, bool repel)
    {
        if (GetObject("player") is not Player player)
        {
            return false;
        }

        double targetX = player.Position.X;
        double targetY = player.Position.Z;
        actor["TARGET_X"] = targetX;
        actor["TARGET_Y"] = targetY;
        if (repel)
        {
            double dx = actor.X - targetX;
            double dy = actor.Y - targetY;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length <= 0.000001)
            {
                return false;
            }

            double distance = Math.Max(0, actor["SPEED"]) * timeCorrection;
            MoveActor(actor, actor.X + ((dx / length) * distance), actor.Y + ((dy / length) * distance));
            return false;
        }

        // FOLLOW triggers IF_ARRIVED only at region borders with CAREFULLY (p.66, MoveActor), not at the player.
        AdvanceActorToward(actor, targetX, targetY, timeCorrection);
        AdjustActorHeightTowardPlayer(actor, player, timeCorrection);
        return false;
    }

    private void InitializeStickTarget(Actor actor)
    {
        if (GetObject("player") is not Player player)
        {
            return;
        }

        double dx = actor.X - player.Position.X;
        double dy = actor.Y - player.Position.Z;
        if (!actor.Skills.ContainsKey("REL_DIST"))
        {
            actor["REL_DIST"] = Math.Sqrt((dx * dx) + (dy * dy));
        }

        if (!actor.Skills.ContainsKey("REL_ANGLE"))
        {
            actor["REL_ANGLE"] = NormalizeSignedAngle(Math.Atan2(dy, dx) - GetSkill("PLAYER_ANGLE"));
        }
    }

    private bool AdvanceStickActor(Actor actor, double timeCorrection)
    {
        if (GetObject("player") is not Player player)
        {
            return false;
        }

        double angle = GetSkill("PLAYER_ANGLE") + actor["REL_ANGLE"];
        double distance = actor["REL_DIST"];
        double targetX = player.Position.X + (Math.Cos(angle) * distance);
        double targetY = player.Position.Z + (Math.Sin(angle) * distance);
        AdvanceActorToward(actor, targetX, targetY, timeCorrection);
        return false;
    }

    /// <summary>
    /// How close (in |dx| + |dy|) an actor counts as arrived without standing on its point. Patched
    /// V3.963 arrived at 0.098 and not at 0.134 at steps of 0.125 to 0.14 (OV-20261029-actor-events);
    /// whether the limit is this constant or the cycle's step is unmeasured.
    /// </summary>
    private const double ArrivalDistance = 0.125;

    private bool AdvanceActorToward(Actor actor, double targetX, double targetY, double timeCorrection)
    {
        actor["TARGET_X"] = targetX;
        actor["TARGET_Y"] = targetY;
        // OV-20261029-actor-events: the step and the distance left are both |dx| + |dy|, so an actor
        // covers SPEED·TIME_CORR of it per cycle (diagonally, 1/√2 of that as the crow flies). It
        // arrives on the point when the step would reach it, or where it is once less than
        // ArrivalDistance is left (measured between 0.098, arrived, and 0.134, not).
        double dx = targetX - actor.X;
        double dy = targetY - actor.Y;
        double remaining = Math.Abs(dx) + Math.Abs(dy);
        if (remaining <= 0.000001)
        {
            return true;
        }

        double distance = Math.Max(0, actor["SPEED"]) * timeCorrection;
        if (distance <= 0)
        {
            return false;
        }

        TurnActorToward(actor, Math.Atan2(dy, dx), timeCorrection);
        if (distance >= remaining)
        {
            MoveActor(actor, targetX, targetY);
            return true;
        }

        MoveActor(actor, actor.X + ((dx / remaining) * distance), actor.Y + ((dy / remaining) * distance));
        return Math.Abs(targetX - actor.X) + Math.Abs(targetY - actor.Y) < ArrivalDistance;
    }

    private bool AdvanceActorTowardNamedTarget(Actor actor, string target, double timeCorrection)
    {
        if (!TryActorTargetPoint(target, out double x, out double y, out double? height))
        {
            return false;
        }

        bool horizontalArrived = AdvanceActorToward(actor, x, y, timeCorrection);
        bool verticalArrived = true;
        if (height is { } targetHeight)
        {
            double currentHeight = ActorWorldHeight(actor);
            double difference = targetHeight - currentHeight;
            double maximum = Math.Abs(actor["VSPEED"]) * timeCorrection;
            if (maximum > 0)
            {
                SetActorWorldHeight(actor, currentHeight + Math.Clamp(difference, -maximum, maximum));
            }

            verticalArrived = Math.Abs(targetHeight - ActorWorldHeight(actor)) <= 0.000001;
        }

        return horizontalArrived && verticalArrived;
    }

    private void AdjustActorHeightTowardPlayer(Actor actor, Player player, double timeCorrection)
    {
        double verticalSpeed = actor["VSPEED"];
        if (verticalSpeed == 0)
        {
            return;
        }

        double targetFeet = player.Position.Y - player.EyeHeight;
        double currentFeet = ActorWorldHeight(actor);
        double difference = targetFeet - currentFeet;
        double maximum = difference >= 0
            ? (verticalSpeed > 0 ? verticalSpeed : Math.Max(0, actor["SPEED"])) * timeCorrection
            : Math.Abs(verticalSpeed) * timeCorrection;
        SetActorWorldHeight(actor, currentFeet + Math.Clamp(difference, -maximum, maximum));
    }

    private double ActorWorldHeight(Actor actor) => EntityFeetHeight(actor);

    /// <summary>A thing's or actor's feet in world height: its <c>HEIGHT</c> above its region's floor, or absolute with <c>GROUND</c>.</summary>
    private double EntityFeetHeight(MapEntity entity)
    {
        if (entity["GROUND"] > 0 || entity.Region < 0 || entity.Region >= _level.Regions.Count)
        {
            return entity.Z;
        }

        return _level.Regions[entity.Region].FloorHeight + entity.Z;
    }

    private void SetActorWorldHeight(Actor actor, double height)
    {
        actor.Z = actor["GROUND"] > 0 || actor.Region < 0 || actor.Region >= _level.Regions.Count
            ? height
            : height - _level.Regions[actor.Region].FloorHeight;
    }

    private void MoveActor(Actor actor, double x, double y)
    {
        if (Math.Abs(actor.X - x) <= 0.000001 && Math.Abs(actor.Y - y) <= 0.000001)
        {
            return;
        }

        double requestedX = x - actor.X;
        double requestedY = y - actor.Y;
        int previousRegion = actor.Region;
        bool carefully = actor["CAREFULLY"] > 0;
        Player? blocked = actor["PASSABLE"] > 0 ? null : GetObject("player") as Player;
        if (carefully)
        {
            double scale = blocked is null ? 1 : CarefulStepScale(actor, blocked, requestedX, requestedY);
            if (scale < 1)
            {
                FireActorContact(actor);
            }

            var start = new Vector2((float)actor.X, (float)actor.Y);
            var step = new Vector2((float)(requestedX * scale), (float)(requestedY * scale));
            CircleMoveResult result = TryEndPointCarefulMove(actor, step.X, step.Y, out int endRegion)
                ? new CircleMoveResult(start + step, endRegion, step, Collided: false, RegionChanged: endRegion != actor.Region)
                : _collisionWorld.Move(
                    start,
                    actor.Region,
                    step,
                    ActorCollisionRadius(actor),
                    (wall, current, other) => ActorCanCross(actor, wall, current, other),
                    EntityCollisionRadius,
                    static entity => entity["INVISIBLE"] > 0,
                    actor,
                    wallClearance: Player.WallClearance);
            actor.X = result.Position.X;
            actor.Y = result.Position.Y;
            actor.Region = result.Region;
            actor["IMPACT_VX"] = result.Displacement.X - requestedX;
            actor["IMPACT_VY"] = result.Displacement.Y - requestedY;
            if (actor.Target?.Equals("STICK", StringComparison.OrdinalIgnoreCase) == true &&
                GetObject("player") is Player player)
            {
                double actualRelativeAngle = Math.Atan2(
                    actor.Y - player.Position.Z,
                    actor.X - player.Position.X) - GetSkill("PLAYER_ANGLE");
                actor["IMPACT_VROT"] = NormalizeSignedAngle(actualRelativeAngle - actor["REL_ANGLE"]);
            }
            if (result.Collided &&
                (actor["SENSITIVE"] > 0 || actor.Target?.Equals("BULLET", StringComparison.OrdinalIgnoreCase) == true))
            {
                TriggerObjectActionHook(actor, "IF_HIT");
            }
        }
        else
        {
            actor.X = x;
            actor.Y = y;
            UpdateActorRegion(actor);
            // OV-20261019-actor-contact: an actor without CAREFULLY pushes the player ahead of it.
            if (blocked is not null &&
                blocked.PushAway(actor, new Vector2((float)actor.X, (float)actor.Y), ActorCollisionRadius(actor) + blocked.Width))
            {
                FireActorContact(actor);
            }
        }

        actor["MOVED"] = Math.Abs(actor.X - (x - requestedX)) > 0.000001 ||
                         Math.Abs(actor.Y - (y - requestedY)) > 0.000001
            ? 1
            : 0;
        if (carefully && actor.Region != previousRegion)
        {
            TriggerObjectActionHook(actor, "IF_ARRIVED");
        }
    }

    /// <summary>
    /// The share of its step a <c>CAREFULLY</c> actor takes toward the player
    /// (OV-20261019-actor-contact): the position two steps ahead must lie at least the contact
    /// distance (half the actor's width plus <c>PLAYER_WIDTH</c>) from the player; otherwise the
    /// step is halved, up to four times, and with no free step the actor stays.
    /// </summary>
    private double CarefulStepScale(Actor actor, Player player, double stepX, double stepY)
    {
        double contact = ActorCollisionRadius(actor) + player.Width;
        double scale = 1;
        for (int attempt = 0; attempt < 5; attempt++, scale /= 2)
        {
            double dx = actor.X + (2 * scale * stepX) - player.Position.X;
            double dy = actor.Y + (2 * scale * stepY) - player.Position.Z;
            if ((dx * dx) + (dy * dy) >= contact * contact)
            {
                return scale;
            }
        }

        return 0;
    }

    /// <summary>
    /// An actor that pushes the player, or a <c>CAREFULLY</c> one the player blocks, fires its
    /// <c>IF_NEAR</c> in that cycle while the player is outside its <c>DIST</c>
    /// (OV-20261019-actor-contact), as the player's contact does (OV-20261019-clip-contact).
    /// </summary>
    private void FireActorContact(Actor actor)
    {
        if (_proximityStates.TryGetValue(actor, out bool near) && near)
        {
            return;
        }

        TriggerObjectActionHook(actor, "IF_NEAR");
    }

    /// <summary>
    /// Whether a <c>CAREFULLY</c> actor crosses a wall (OV-20261019-border-kinds). <c>IMPASSABLE</c>
    /// stops it even on a <c>PASSABLE</c> wall (p.53). A wall with one region on both sides is
    /// crossed only when <c>PASSABLE</c>. Into another region, the floor may rise less than
    /// <c>ACTOR_CLIMB</c> (p.113; a rise equal to it blocks) and less than the actor's own height
    /// above its feet; <c>PLAYER_CLIMB</c> does not count.
    /// </summary>
    private bool ActorCanCross(Actor actor, Wall wall, int currentRegion, int otherRegion)
    {
        if (wall["IMPASSABLE"] > 0)
        {
            return false;
        }

        if (otherRegion == currentRegion || otherRegion < 0 || otherRegion >= _level.Regions.Count)
        {
            return wall["PASSABLE"] > 0;
        }

        return ActorCanClimbInto(actor, otherRegion);
    }

    private bool ActorCanClimbInto(Actor actor, int region)
    {
        double rise = _level.Regions[region].FloorHeight - ActorWorldHeight(actor);
        double height = MapEntitySizing.TryGetSize(_level, actor, out MapEntitySize size) ? size.Height : double.PositiveInfinity;
        return rise < GetSkill("ACTOR_CLIMB") && rise < height;
    }

    /// <summary>
    /// A skipped actor's <c>CAREFULLY</c> step (<c>SKIP_FRAMES</c>) longer than its half width is
    /// tested at its end only (OV-20261020-view-activity, long-wall and long-pad): it goes through when the region at its
    /// end point may be entered (<see cref="ActorCanClimbInto"/>) and no wall the actor may not cross
    /// lies within its half width and the wall clearance ahead of that point. Walls in between are
    /// not tested, so the step can jump a wall or a whole raised region. Otherwise the step is swept
    /// as usual and stops at the first wall. A shorter step is swept: the end test of the step before
    /// covered the walls it can reach. Other things, actors and the player are not tested at the end
    /// either. Steps of actors that are not skipping were not measured and are always swept.
    /// </summary>
    private bool TryEndPointCarefulMove(Actor actor, double stepX, double stepY, out int endRegion)
    {
        endRegion = actor.Region;
        double radius = ActorCollisionRadius(actor);
        double length = Math.Sqrt((stepX * stepX) + (stepY * stepY));
        if (!_longSkippedStep || length <= radius || actor.Region < 0 || actor.Region >= _level.Regions.Count)
        {
            return false;
        }

        double directionX = stepX / length;
        double directionY = stepY / length;
        // The end point's region: follow the region borders the step crosses, nearest first.
        int region = actor.Region;
        foreach (Wall wall in CrossedWalls(actor.X, actor.Y, directionX, directionY, length))
        {
            region = OtherSide(wall, region);
        }

        if (region < 0 || region >= _level.Regions.Count || (region != actor.Region && !ActorCanClimbInto(actor, region)))
        {
            return false;
        }

        double endX = actor.X + stepX;
        double endY = actor.Y + stepY;
        int current = region;
        foreach (Wall wall in CrossedWalls(endX, endY, directionX, directionY, radius + Player.WallClearance))
        {
            int other = OtherSide(wall, current);
            if (!ActorCanCross(actor, wall, current, other))
            {
                return false;
            }

            current = other;
        }

        endRegion = region;
        return true;
    }

    /// <summary>The walls a segment from (x, y) along a unit direction crosses within <paramref name="length"/>, nearest first.</summary>
    private IEnumerable<Wall> CrossedWalls(double x, double y, double directionX, double directionY, double length)
    {
        var crossed = new List<(double Distance, Wall Wall)>();
        foreach (Wall wall in _level.Walls)
        {
            if (TryGetWallSegment(wall, out double ax, out double ay, out double bx, out double by) &&
                TryRaySegmentDistance(x, y, directionX, directionY, ax, ay, bx, by, out double distance) &&
                distance > 1e-9 &&
                distance <= length)
            {
                crossed.Add((distance, wall));
            }
        }

        return crossed.OrderBy(static c => c.Distance).Select(static c => c.Wall);
    }

    private static int OtherSide(Wall wall, int region) =>
        wall.LeftRegion == region ? wall.RightRegion :
        wall.RightRegion == region ? wall.LeftRegion :
        region;

    private float ActorCollisionRadius(Actor actor) => EntityCollisionRadius(actor);

    private float EntityCollisionRadius(MapEntity entity)
    {
        if (!MapEntitySizing.TryGetSize(_level, entity, out MapEntitySize size))
        {
            return 0.5f;
        }

        double factor = entity is Actor ? GetSkill("ACTOR_WIDTH") : GetSkill("THING_WIDTH");
        return size.Width * 0.5f * (float)Math.Max(0, factor);
    }

    private void UpdateActorRegion(Actor actor)
    {
        if (LevelTopology.LocateRegion(_level, actor.X, actor.Y) is { } region)
        {
            actor.Region = region;
        }
    }

    private static void TurnActorToward(Actor actor, double desiredFacing, double timeCorrection)
    {
        double placementAngle = actor.PlacementAngleDegrees * (Math.PI / 180.0);
        double targetAngle = NormalizeSignedAngle(desiredFacing - placementAngle);
        double maximumTurn = Math.Max(0, actor["ASPEED"]) * timeCorrection;
        if (maximumTurn <= 0)
        {
            actor.Angle = targetAngle;
            return;
        }

        double difference = NormalizeSignedAngle(targetAngle - actor.Angle);
        actor.Angle = NormalizeSignedAngle(actor.Angle + Math.Clamp(difference, -maximumTurn, maximumTurn));
    }

    private bool TryActorTargetPoint(string target, out double x, out double y, out double? height)
    {
        switch (GetObject(target))
        {
            case MapEntity entity:
                x = entity.X;
                y = entity.Y;
                height = entity["GROUND"] > 0 || entity.Region < 0 || entity.Region >= _level.Regions.Count
                    ? entity.Z
                    : _level.Regions[entity.Region].FloorHeight + entity.Z;
                return true;
            case Player player:
                x = player.Position.X;
                y = player.Position.Z;
                height = player.Position.Y - player.EyeHeight;
                return true;
            case Wall wall when wall.Vertex1 >= 0 && wall.Vertex1 < _level.Vertices.Count:
                x = _level.Vertices[wall.Vertex1].X;
                y = _level.Vertices[wall.Vertex1].Y;
                height = null;
                return true;
            default:
                x = 0;
                y = 0;
                height = null;
                return false;
        }
    }

    private static double NormalizeSignedAngle(double angle)
    {
        angle %= Math.Tau;
        if (angle > Math.PI)
        {
            angle -= Math.Tau;
        }
        else if (angle < -Math.PI)
        {
            angle += Math.Tau;
        }

        return angle;
    }

    private void RecordGeometryInvalidation(AcknexObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _shakeRequests.Add(new WdlShakeRequest(target.Name));
    }

    /// <summary>
    /// The objects' part of a cycle, after the global slots, in the original's order
    /// (OV-20261029-scheduler-order): the regions, then the actors, the things and the walls, each
    /// kind from the last the map places to the first (the region order is assumed, one region was
    /// measured). Each object runs its <c>EACH_TICK</c>, read when its turn comes so a <c>SET</c>
    /// earlier in the cycle counts, and then steps its texture animation, which runs its
    /// <c>EACH_CYCLE</c> when a cycle ends.
    /// </summary>
    private void RunObjectPass(double timeCorrection)
    {
        List<AcknexObject> order = _objectPassBuffer;
        order.Clear();
        for (int i = _regions.Count - 1; i >= 0; i--)
        {
            order.Add(_regions[i]);
        }

        for (int i = _levelObjects.Count - 1; i >= 0; i--)
        {
            if (_levelObjects[i] is Actor)
            {
                order.Add(_levelObjects[i]);
            }
        }

        for (int i = _levelObjects.Count - 1; i >= 0; i--)
        {
            if (_levelObjects[i] is MapEntity and not Actor)
            {
                order.Add(_levelObjects[i]);
            }
        }

        for (int i = _level.Walls.Count - 1; i >= 0; i--)
        {
            order.Add(_level.Walls[i]);
        }

        bool actorsDone = false;
        foreach (AcknexObject target in order)
        {
            if (target is Actor actor)
            {
                AdvanceActorTarget(actor, timeCorrection);
            }
            else if (!actorsDone && target is not Region)
            {
                actorsDone = true;
                FinishActorMoves();
            }

            RunObjectEachTick(target);
            if (timeCorrection > 0)
            {
                AdvanceAnimationCycle(target, timeCorrection);
            }
        }

        if (!actorsDone)
        {
            FinishActorMoves();
        }

        order.Clear();
    }

    private void FinishActorMoves()
    {
        if (_frontActorMoved)
        {
            // OV-20261020-view-activity round 9: the moves and the refreshed distances show together.
            RefreshEntityDistances(force: true);
        }
    }

    private void RunObjectEachTick(AcknexObject target)
    {
        // OV-20261019-clip-detail: an INVISIBLE wall runs no EACH_TICK.
        if (IsInactiveEntity(target) ||
            !TryGetObjectSchedulerAction(target, eachCycle: false, out string? actionName) ||
            string.IsNullOrWhiteSpace(actionName) ||
            (target is Wall && target["INVISIBLE"] > 0) ||
            !IsWithinClipDistance(target) ||
            IsDormantBerkeleyWall(target))
        {
            return;
        }

        PrepareActorTickSkills(target);
        RunForActionTarget(target, actionName);
    }

    private void AdvanceAnimationCycle(AcknexObject target, double timeCorrection)
    {
        // OV-20261019-clip-contact: a thing or actor outside its CLIP_DIST border runs no EACH_CYCLE.
        // None of these checks changes anything, so the cheap texture test goes first.
        if (AnimationTexture(target) is not { } entry ||
            (entry.Texture.Cycles <= 1 && !entry.Texture.IsOneShot) ||
            IsInactiveEntity(target) ||
            (target is MapEntity && !IsWithinClipDistance(target)))
        {
            return;
        }

        if (!_textureAnimationStates.TryGetValue(target, out TextureAnimationState? state))
        {
            state = new TextureAnimationState();
            _textureAnimationStates[target] = state;
        }

        AdvanceAnimationTarget(target, entry.Texture, state, timeCorrection);
    }

    private void AdvanceAnimationTarget(
        AcknexObject target,
        LevelTexture texture,
        TextureAnimationState state,
        double timeCorrection)
    {
        int cycles = Math.Max(1, texture.Cycles);
        bool play = target["PLAY"] > 0;
        if (texture.IsOneShot)
        {
            if (!play)
            {
                state.WasPlaying = false;
                state.ElapsedTicks = 0;
                return;
            }

            if (!state.WasPlaying)
            {
                SetAnimationCycle(target, 1);
                QueueAnimationTextureSound(target, texture, 1);
                state.ElapsedTicks = 0;
                state.WasPlaying = true;
            }
        }

        int cycle = NormalizeAnimationCycle(target["CYCLE"], cycles);
        if (target["CYCLE"] != cycle)
        {
            SetAnimationCycle(target, cycle);
        }

        state.ElapsedTicks += timeCorrection;
        int phasesAdvanced = 0;
        while (state.ElapsedTicks >= AnimationDelay(texture, cycle) && phasesAdvanced++ < 4096)
        {
            state.ElapsedTicks -= AnimationDelay(texture, cycle);
            if (cycle < cycles)
            {
                cycle++;
                SetAnimationCycle(target, cycle);
                QueueAnimationTextureSound(target, texture, cycle);
                continue;
            }

            TriggerEachCycle(target);
            if (texture.IsOneShot)
            {
                target["PLAY"] = 0;
                state.WasPlaying = false;
                state.ElapsedTicks = 0;
                break;
            }

            cycle = 1;
            SetAnimationCycle(target, cycle);
            QueueAnimationTextureSound(target, texture, cycle);
        }
    }

    private void QueueAnimationTextureSound(AcknexObject target, LevelTexture texture, int cycle)
    {
        if (target is Region || texture.IsSoundLoop || string.IsNullOrWhiteSpace(texture.SoundName))
        {
            return;
        }

        int index = cycle - 1;
        bool trigger = texture.SoundCycles.Count == 0
            ? cycle == 1
            : index >= 0 && index < texture.SoundCycles.Count && texture.SoundCycles[index];
        if (trigger)
        {
            _textureSoundRequests.Add(new WdlTextureSoundRequest(target, texture));
        }
    }

    private void InitializeAnimationCycles()
    {
        foreach (AcknexObject target in CurrentCycleTargets())
        {
            if (AnimationTexture(target) is not null && target["CYCLE"] <= 0)
            {
                target["CYCLE"] = 1;
            }
        }
    }

    private LevelTextureEntry? AnimationTexture(AcknexObject target) =>
        target switch
        {
            MapEntity entity => _level.FindTexture(entity.Texture),
            Wall wall => _level.FindTexture(wall.Texture),
            Region region => PreferredRegionAnimationTexture(region),
            _ => null,
        };

    private LevelTextureEntry? PreferredRegionAnimationTexture(Region region)
    {
        if (_level.FindTexture(region.FloorTexture) is { } floor &&
            (floor.Texture.Cycles > 1 || floor.Texture.IsOneShot))
        {
            return floor;
        }

        return _level.FindTexture(region.CeilTexture);
    }

    private static void SetAnimationCycle(AcknexObject target, int cycle)
    {
        if (target["CYCLE"] == cycle)
        {
            return;
        }

        target["CYCLE"] = cycle;
    }

    private static int NormalizeAnimationCycle(double value, int cycles) =>
        value > 0 ? Math.Clamp((int)Math.Floor(value), 1, cycles) : 1;

    private static double AnimationDelay(LevelTexture texture, int cycle)
    {
        int index = cycle - 1;
        return index >= 0 && index < texture.Delays.Count && texture.Delays[index] > 0
            ? texture.Delays[index]
            : 1;
    }

    private IEnumerable<AcknexObject> CurrentCycleTargets()
    {
        foreach (Region region in _regions)
        {
            yield return region;
        }

        foreach (Wall wall in _level.Walls)
        {
            yield return wall;
        }

        foreach (MapEntity entity in _levelObjects.OfType<MapEntity>())
        {
            yield return entity;
        }
    }

    private static bool TryGetObjectSchedulerAction(AcknexObject target, bool eachCycle, out string? actionName)
    {
        actionName = target switch
        {
            Region region => eachCycle ? region.EachCycleAction : region.EachTickAction,
            Wall wall => eachCycle ? wall.EachCycleAction : wall.EachTickAction,
            MapEntity entity => eachCycle ? entity.EachCycleAction : entity.EachTickAction,
            _ => null,
        };

        return target is Region or Wall or MapEntity;
    }

    private static void SetObjectSchedulerAction(AcknexObject target, bool eachCycle, string? actionName)
    {
        actionName = string.IsNullOrWhiteSpace(actionName) ? null : actionName;
        switch (target)
        {
            case Region region when eachCycle:
                region.EachCycleAction = actionName;
                break;
            case Region region:
                region.EachTickAction = actionName;
                break;
            case Wall wall when eachCycle:
                wall.EachCycleAction = actionName;
                break;
            case Wall wall:
                wall.EachTickAction = actionName;
                break;
            case MapEntity entity when eachCycle:
                entity.EachCycleAction = actionName;
                break;
            case MapEntity entity:
                entity.EachTickAction = actionName;
                break;
        }
    }

    private static bool TryGetObjectActionHook(AcknexObject target, string name, out string? actionName)
    {
        switch (target)
        {
            case Region region:
                return region.TryGetActionHook(name, out actionName);
            case Wall wall:
                return wall.TryGetActionHook(name, out actionName);
            case MapEntity entity:
                return entity.TryGetActionHook(name, out actionName);
            default:
                actionName = null;
                return false;
        }
    }

    private Region? RegionForActionTarget(AcknexObject target) =>
        target switch
        {
            Region region => region,
            MapEntity entity => RegionAt(entity.Region),
            _ => null,
        };

    private Region? RegionAt(int index) =>
        index >= 0 && index < _regions.Count ? _regions[index] : null;

    private static bool TryGetMapPosition(AcknexObject target, out double x, out double y)
    {
        switch (target)
        {
            case MapEntity entity:
                x = entity.X;
                y = entity.Y;
                return true;
            case Player player:
                x = player.Position.X;
                y = player.Position.Z;
                return true;
            default:
                x = y = 0;
                return false;
        }
    }

    private static (double X, double Y) RotateMapPoint(
        double x,
        double y,
        double centerX,
        double centerY,
        double sin,
        double cos)
    {
        double dx = x - centerX;
        double dy = y - centerY;
        return (centerX + (dx * cos) - (dy * sin), centerY + (dx * sin) + (dy * cos));
    }

    private void ShootFromPlayer(double range, bool useShootDeviation)
    {
        if (GetObject("player") is not Player player || range <= 0)
        {
            ClearHitResult();
            return;
        }

        double originX = player.Position.X;
        double originY = player.Position.Z;
        // The SHOOT ray goes through the same projection the renderer uses (OV-20261009-*,
        // confirmed for SHOOT_X/SHOOT_Y in OV-20261009-shoot-mapping): horizontal FOV =
        // PLAYER_ARC radians, vertical half-tangent = 2/3 · ASPECT of the horizontal one,
        // positive SHOOT_X right, positive SHOOT_Y up, and PLAYER_TILT a y-shear that lowers
        // the center ray by tilt · H rows.
        ProjectionModel projection = ProjectionModel.Create(
            GetSkill("PLAYER_ARC"),
            GetSkill("ASPECT"),
            GetSkill("PLAYER_TILT"),
            (int)Math.Round(GetSkill("SCREEN_WIDTH")),
            (int)Math.Round(GetSkill("SCREEN_HGT")));
        double shootX = useShootDeviation ? GetSkill("SHOOT_X") : 0;
        double shootY = useShootDeviation ? GetSkill("SHOOT_Y") : 0;
        (double horizontalDeviation, double rayVertical) = projection.ShootRay(shootX, shootY);
        double horizontalMagnitude = Math.Sqrt(1 + (horizontalDeviation * horizontalDeviation));
        double verticalSlope = rayVertical / horizontalMagnitude;
        double playerAngle = GetSkill("PLAYER_ANGLE");
        double angle = playerAngle - Math.Atan2(horizontalDeviation, 1);
        double dirX = Math.Cos(angle);
        double dirY = Math.Sin(angle);
        // HIT_DIST is the depth of the hit along the line of sight (PLAYER_ANGLE), not the
        // length of the deviated ray: OV-20261009-shoot-mapping read 44.000 for every shot at
        // the billboard targets 44 steps ahead, with SHOOT_X ±0.5 (ray length 45.6), SHOOT_Y
        // ±0.25 and PLAYER_TILT ±0.1 alike. Billboards report the entity center's depth; walls
        // the trace distance × cos(deviation), the natural extrapolation (no wall hit was
        // probed). SHOOT_RANGE limits the same measure (unprobed): walls through the
        // horizontal range below, billboards through their depth.
        double distanceScale = 1 / horizontalMagnitude;
        HitCandidate? hit = FindClosestHit(
            originX,
            originY,
            player.Position.Y,
            dirX,
            dirY,
            Math.Cos(playerAngle),
            Math.Sin(playerAngle),
            verticalSlope,
            range / distanceScale,
            distanceScale,
            player.Region);
        if (hit is null)
        {
            ClearHitResult();
            return;
        }

        SetObjectReference("hit", hit.Target);
        SetHitResult(hit.Distance, range, hit.HitX, hit.HitY);
        TriggerObjectActionHook(hit.Target, "IF_HIT");
    }

    private HitCandidate? FindClosestHit(
        double originX,
        double originY,
        double eyeHeight,
        double dirX,
        double dirY,
        double forwardX,
        double forwardY,
        double verticalSlope,
        double horizontalRange,
        double distanceScale,
        int startRegion)
    {
        int region = startRegion;
        double minimumDistance = 0;
        Dictionary<MapEntity, SpriteBillboard> hitSprites = SpriteBuilder
            .Build(_level, new Vector2((float)originX, (float)originY))
            .Where(sprite => sprite.Source is not null &&
                !sprite.IsAttachment &&
                sprite.Orientation == SpriteOrientation.Upright)
            .ToDictionary(sprite => sprite.Source!);
        var traversedPortals = new HashSet<(int Region, int Wall)>();
        for (int traversal = 0; traversal <= _level.Regions.Count; traversal++)
        {
            HitCandidate? bestEntity = null;
            foreach (MapEntity entity in _level.Things.Cast<MapEntity>().Concat(_level.Actors))
            {
                if (entity.Region != region || entity["IMMATERIAL"] != 0 || IsInactiveEntity(entity))
                {
                    continue;
                }

                double dx = entity.X - originX;
                double dy = entity.Y - originY;
                double along = (dx * dirX) + (dy * dirY);
                double depth = (dx * forwardX) + (dy * forwardY); // HIT_DIST: the billboard's depth along the line of sight
                if (along <= minimumDistance || depth > horizontalRange * distanceScale)
                {
                    continue;
                }

                double signedCenter = (dx * dirY) - (dy * dirX);
                if (hitSprites.TryGetValue(entity, out SpriteBillboard sprite))
                {
                    double width = sprite.HalfWidth * 2;
                    double height = sprite.HalfHeight * 2;
                    double u = 0.5 - ((signedCenter + sprite.CenterOffset.X) / width);
                    double shotHeight = eyeHeight + (along * verticalSlope);
                    double bottom = sprite.BasePosition.Y + sprite.CenterOffset.Y;
                    double v = 1 - ((shotHeight - bottom) / height);
                    if (u < 0 || u > 1 || v < 0 || v > 1)
                    {
                        continue;
                    }

                    double hitX = 0;
                    double hitY = 0;
                    if (_hitTextures.TryGetValue(sprite.Texture, out TextureImage image) &&
                        SpriteBuilder.TrySamplePixel(
                            image,
                            (float)u,
                            (float)v,
                            sprite.Mirror,
                            out int pixelX,
                            out int pixelY,
                            out bool opaque))
                    {
                        if (!opaque)
                        {
                            continue;
                        }

                        hitX = pixelX;
                        hitY = pixelY;
                    }

                    bestEntity = Nearer(
                        bestEntity,
                        new HitCandidate(entity, depth, along, hitX, hitY));
                }
                else
                {
                    double radius = Math.Max(0.01, EntityCollisionRadius(entity));
                    if (Math.Abs(signedCenter) <= radius)
                    {
                        bestEntity = Nearer(
                            bestEntity,
                            new HitCandidate(entity, depth, along));
                    }
                }
            }

            HitCandidate? bestWall = null;
            int bestWallIndex = -1;
            for (int i = 0; i < _level.Walls.Count; i++)
            {
                Wall wall = _level.Walls[i];
                if ((wall.LeftRegion != region && wall.RightRegion != region) ||
                    wall["IMMATERIAL"] != 0 ||
                    !TryGetWallSegment(wall, out double ax, out double ay, out double bx, out double by) ||
                    !TryRaySegmentDistance(originX, originY, dirX, dirY, ax, ay, bx, by, out double distance) ||
                    distance <= minimumDistance ||
                    distance > horizontalRange)
                {
                    continue;
                }

                if (bestWall is null || distance < bestWall.TraceDistance)
                {
                    double wallHeight = eyeHeight + (distance * verticalSlope);
                    bestWall = CreateWallHitCandidate(
                        wall,
                        distance * distanceScale,
                        distance,
                        originX + (dirX * distance),
                        originY + (dirY * distance),
                        wallHeight,
                        ax,
                        ay,
                        bx,
                        by);
                    bestWallIndex = i;
                }
            }

            if (bestEntity is not null && (bestWall is null || bestEntity.Distance < bestWall.Distance))
            {
                return bestEntity;
            }

            if (bestWall is null || bestWallIndex < 0)
            {
                return null;
            }

            Wall portal = _level.Walls[bestWallIndex];
            if (!TryTraverseShotPortal(
                    portal,
                    bestWallIndex,
                    region,
                    originX + (dirX * bestWall.TraceDistance),
                    originY + (dirY * bestWall.TraceDistance),
                    eyeHeight + (bestWall.TraceDistance * verticalSlope),
                    traversedPortals,
                    out int nextRegion))
            {
                return bestWall;
            }

            region = nextRegion;
            minimumDistance = bestWall.TraceDistance + 1e-6;
        }

        return null;
    }

    private HitCandidate CreateWallHitCandidate(
        Wall wall,
        double distance,
        double traceDistance,
        double hitMapX,
        double hitMapY,
        double eyeHeight,
        double ax,
        double ay,
        double bx,
        double by)
    {
        if (wall.Texture is not { } textureName ||
            !_level.Textures.TryGetValue(textureName, out LevelTexture texture))
        {
            return new HitCandidate(wall, distance, traceDistance);
        }

        double segmentX = bx - ax;
        double segmentY = by - ay;
        double length = Math.Sqrt((segmentX * segmentX) + (segmentY * segmentY));
        if (length <= 0)
        {
            return new HitCandidate(wall, distance, traceDistance);
        }

        double along = Math.Clamp(
            (((hitMapX - ax) * segmentX) + ((hitMapY - ay) * segmentY)) / (length * length),
            0,
            1);
        double pixelsPerStepX = texture.ScaleX > 0 ? texture.ScaleX : 16;
        double pixelsPerStepY = texture.ScaleY > 0 ? texture.ScaleY : 16;
        // The bitmap's left edge sits at the wall's SECOND vertex for a viewer in its left region
        // (OV-20261009-shoot-mapping, far-wall face), the same direction MeshBuilder maps u.
        double hitX = Math.Floor(wall.OffsetX + ((1 - along) * length * pixelsPerStepX));
        double wallTop = WallSegmentTopAtHit(wall, along, eyeHeight);
        double hitY = Math.Floor(wall.OffsetY + ((wallTop - eyeHeight) * pixelsPerStepY));
        return new HitCandidate(wall, distance, traceDistance, hitX, hitY);
    }

    private double WallSegmentTopAtHit(Wall wall, double along, double eyeHeight)
    {
        double vertexOffset = 0;
        if (wall.Vertex1 >= 0 && wall.Vertex1 < _level.Vertices.Count &&
            wall.Vertex2 >= 0 && wall.Vertex2 < _level.Vertices.Count)
        {
            vertexOffset = _level.Vertices[wall.Vertex1].Z +
                ((_level.Vertices[wall.Vertex2].Z - _level.Vertices[wall.Vertex1].Z) * along);
        }

        Region? left = wall.LeftRegion >= 0 && wall.LeftRegion < _level.Regions.Count
            ? _level.Regions[wall.LeftRegion]
            : null;
        Region? right = wall.RightRegion >= 0 && wall.RightRegion < _level.Regions.Count
            ? _level.Regions[wall.RightRegion]
            : null;
        if (left is not null && right is not null && !ReferenceEquals(left, right))
        {
            double openingFloor = Math.Max(left.FloorHeight, right.FloorHeight) + vertexOffset;
            return eyeHeight <= openingFloor
                ? openingFloor
                : Math.Max(left.CeilHeight, right.CeilHeight) + vertexOffset;
        }

        Region? region = left ?? right;
        return (region?.CeilHeight ?? eyeHeight) + vertexOffset;
    }

    private bool TryTraverseShotPortal(
        Wall wall,
        int wallIndex,
        int region,
        double x,
        double y,
        double eyeHeight,
        HashSet<(int Region, int Wall)> traversed,
        out int nextRegion)
    {
        nextRegion = wall.LeftRegion == region ? wall.RightRegion : wall.LeftRegion;
        if (nextRegion < 0 ||
            nextRegion >= _level.Regions.Count ||
            !traversed.Add((region, wallIndex)))
        {
            return false;
        }

        Region current = _level.Regions[region];
        Region next = _level.Regions[nextRegion];
        double currentOffset = LevelTopology.TryGetSurfaceHeightOffset(_level, region, x, y, out double currentValue)
            ? currentValue
            : 0;
        double nextOffset = LevelTopology.TryGetSurfaceHeightOffset(_level, nextRegion, x, y, out double nextValue)
            ? nextValue
            : 0;
        double openingFloor = Math.Max(current.FloorHeight + currentOffset, next.FloorHeight + nextOffset);
        double openingCeiling = Math.Min(current.CeilHeight + currentOffset, next.CeilHeight + nextOffset);
        return eyeHeight > openingFloor && eyeHeight < openingCeiling;
    }

    private bool HasPortalLineOfSight(
        double originX,
        double originY,
        double eyeHeight,
        double dirX,
        double dirY,
        double range,
        int startRegion,
        int targetRegion)
    {
        int region = startRegion;
        double minimumDistance = 0;
        var traversedPortals = new HashSet<(int Region, int Wall)>();
        for (int traversal = 0; traversal <= _level.Regions.Count; traversal++)
        {
            double nearestDistance = double.PositiveInfinity;
            int nearestWall = -1;
            for (int i = 0; i < _level.Walls.Count; i++)
            {
                Wall wall = _level.Walls[i];
                if ((wall.LeftRegion != region && wall.RightRegion != region) ||
                    wall["IMMATERIAL"] != 0 ||
                    !TryGetWallSegment(wall, out double ax, out double ay, out double bx, out double by) ||
                    !TryRaySegmentDistance(originX, originY, dirX, dirY, ax, ay, bx, by, out double distance) ||
                    distance <= minimumDistance ||
                    distance >= range ||
                    distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = distance;
                nearestWall = i;
            }

            if (nearestWall < 0)
            {
                return region == targetRegion;
            }

            Wall portal = _level.Walls[nearestWall];
            if (!TryTraverseShotPortal(
                    portal,
                    nearestWall,
                    region,
                    originX + (dirX * nearestDistance),
                    originY + (dirY * nearestDistance),
                    eyeHeight,
                    traversedPortals,
                    out int nextRegion))
            {
                return false;
            }

            region = nextRegion;
            minimumDistance = nearestDistance + 1e-6;
        }

        return false;
    }

    private static HitCandidate Nearer(HitCandidate? current, HitCandidate candidate) =>
        current is null || candidate.Distance < current.Distance ? candidate : current;

    private bool TryGetWallSegment(Wall wall, out double ax, out double ay, out double bx, out double by)
    {
        if (wall.Vertex1 < 0 || wall.Vertex1 >= _level.Vertices.Count ||
            wall.Vertex2 < 0 || wall.Vertex2 >= _level.Vertices.Count)
        {
            ax = ay = bx = by = 0;
            return false;
        }

        Vertex a = _level.Vertices[wall.Vertex1];
        Vertex b = _level.Vertices[wall.Vertex2];
        ax = a.X;
        ay = a.Y;
        bx = b.X;
        by = b.Y;
        return true;
    }

    private static bool TryRaySegmentDistance(
        double originX,
        double originY,
        double dirX,
        double dirY,
        double ax,
        double ay,
        double bx,
        double by,
        out double distance)
    {
        double sx = bx - ax;
        double sy = by - ay;
        double denominator = Cross(dirX, dirY, sx, sy);
        if (Math.Abs(denominator) < 1e-9)
        {
            distance = 0;
            return false;
        }

        double qx = ax - originX;
        double qy = ay - originY;
        double rayDistance = Cross(qx, qy, sx, sy) / denominator;
        double segmentPosition = Cross(qx, qy, dirX, dirY) / denominator;
        if (rayDistance < 0 || segmentPosition is < 0 or > 1)
        {
            distance = 0;
            return false;
        }

        distance = rayDistance;
        return true;
    }

    private static double Cross(double ax, double ay, double bx, double by) => (ax * by) - (ay * bx);

    private IEnumerable<AcknexObject> FragileTargets(int region)
    {
        foreach (MapEntity entity in _level.Things.Cast<MapEntity>().Concat(_level.Actors))
        {
            if (entity.Region == region && entity["FRAGILE"] != 0 && !IsInactiveEntity(entity))
            {
                yield return entity;
            }
        }

        foreach (Wall wall in _level.Walls)
        {
            if ((wall.LeftRegion == region || wall.RightRegion == region) && wall["FRAGILE"] != 0)
            {
                yield return wall;
            }
        }
    }

    private bool TryDistanceFromPoint(double x, double y, AcknexObject target, out double distance)
    {
        switch (target)
        {
            case MapEntity entity:
                distance = Distance(x, y, entity.X, entity.Y);
                return true;
            case Wall wall when TryGetWallSegment(wall, out double ax, out double ay, out double bx, out double by):
                distance = DistanceToSegment(x, y, ax, ay, bx, by);
                return true;
            default:
                distance = 0;
                return false;
        }
    }

    private void SetPlayerExplosionResult(double x, double y, double range)
    {
        if (GetObject("player") is not Player player)
        {
            ClearHitResult();
            return;
        }

        double distance = Distance(x, y, player.Position.X, player.Position.Z);
        if (distance <= range)
        {
            SetHitResult(distance, range);
        }
        else
        {
            ClearHitResult();
        }
    }

    private static double Distance(double ax, double ay, double bx, double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>
    /// Works out what the last drawn frame showed (ADR-0010): the player's view as it stands at the
    /// start of the cycle, before the cycle's actions turn or move it, is the frame the original drew
    /// at the end of the previous cycle. A thing's or actor's <c>VISIBLE</c> is set while some of it
    /// is on the screen (p.70, <see cref="ViewTest"/>); an <c>INVISIBLE</c> one is never visible.
    /// Things, actors and walls set <c>SEEN</c> once visible and keep it (p.53, p.71).
    /// <c>SKIP_FRAMES</c> and <c>BERKELEY</c> walls follow it (OV-20261020-view-activity), so things,
    /// actors and <c>BERKELEY</c> walls are tested here in every cycle. Other walls get their
    /// <c>VISIBLE</c> the same way (round 6, wall-seen) but are tested only when an action reads it
    /// (<see cref="GetObjectNumber"/>): testing thousands of walls costs milliseconds a cycle, and the
    /// retail scripts never read them. Once an action reads a wall's <c>SEEN</c>, every wall is tested
    /// here from then on. Regions are tested in the same way (<see cref="TestRegionView"/>). Without a player there is no view: every object that is not
    /// <c>INVISIBLE</c> counts as visible, and none as seen. Also refreshes the distances of
    /// <see cref="RefreshEntityDistances"/>.
    /// </summary>
    private void RefreshView()
    {
        _inView.Clear();
        _viewFrame++;
        Viewer? viewer = CurrentViewer();
        _lastViewer = viewer;
        if (viewer is not null && _viewTest?.Update(_level) != true)
        {
            _viewTest = new ViewTest(_level);
        }

        ViewTest? test = viewer is null ? null : _viewTest;
        foreach (MapEntity entity in _levelObjects.OfType<MapEntity>())
        {
            bool visible = entity["INVISIBLE"] <= 0 && (viewer is not { } view || IsVisible(test!, view, entity));
            entity["VISIBLE"] = visible ? 1 : 0;
            if (visible)
            {
                if (viewer is not null)
                {
                    entity["SEEN"] = 1;
                }

                _inView.Add(entity);
            }
        }

        RefreshEntityDistances();
        if (viewer is null)
        {
            return;
        }

        foreach (Wall wall in _level.Walls)
        {
            if (_trackWallsSeen || wall["BERKELEY"] > 0)
            {
                TestWallView(wall);
            }
        }

        if (_trackRegionsSeen)
        {
            foreach (Region region in _level.Regions)
            {
                TestRegionView(region);
            }
        }
    }

    /// <summary>Sets a wall's <c>VISIBLE</c> (and <c>SEEN</c>) for the last drawn frame, once a frame.</summary>
    private void TestWallView(Wall wall)
    {
        if (_lastViewer is not { } viewer || _viewTest is null || _wallViewFrames.GetValueOrDefault(wall) == _viewFrame)
        {
            return;
        }

        _wallViewFrames[wall] = _viewFrame;
        bool visible = wall["INVISIBLE"] <= 0 && _viewTest.IsWallVisible(viewer, wall);
        wall["VISIBLE"] = visible ? 1 : 0;
        if (visible)
        {
            wall["SEEN"] = 1;
            _inView.Add(wall);
        }
    }

    /// <summary>
    /// Sets a region's <c>VISIBLE</c> (and <c>SEEN</c>) for the last drawn frame, once a frame: set
    /// while some of its floor, ceiling or walls is on the screen (p.53, <see cref="ViewTest.IsRegionVisible"/>)
    /// or the player stands in it, as for walls one frame after a turn or beam, with <c>SEEN</c>
    /// kept once set (OV-20261021-distance-refresh, region-seen).
    /// </summary>
    private void TestRegionView(Region region)
    {
        if (_lastViewer is not { } viewer || _viewTest is null || _regionViewFrames.GetValueOrDefault(region) == _viewFrame ||
            GetObject("player") is not Player player)
        {
            return;
        }

        _regionViewFrames[region] = _viewFrame;
        bool visible = false;
        for (int index = 0; index < _level.Regions.Count && !visible; index++)
        {
            // A REGION keyword may name several map regions, each its own object.
            visible = ReferenceEquals(_level.Regions[index], region) &&
                      (index == player.Region || _viewTest.IsRegionVisible(viewer, index));
        }

        region["VISIBLE"] = visible ? 1 : 0;
        if (visible)
        {
            region["SEEN"] = 1;
        }
    }

    /// <inheritdoc/>
    public double GetObjectNumber(AcknexObject target, string memberName)
    {
        ArgumentNullException.ThrowIfNull(target);
        bool seen = memberName.Equals("SEEN", StringComparison.OrdinalIgnoreCase);
        if (seen || memberName.Equals("VISIBLE", StringComparison.OrdinalIgnoreCase))
        {
            switch (target)
            {
                case Wall wall:
                    _trackWallsSeen |= seen;
                    TestWallView(wall);
                    break;
                case Region region:
                    _trackRegionsSeen |= seen;
                    TestRegionView(region);
                    break;
            }
        }
        else if (_frontActorMoved && target is MapEntity && memberName.Equals("DISTANCE", StringComparison.OrdinalIgnoreCase))
        {
            // Actors now move in their turn of the object pass (OV-20261029-actor-events); a script
            // that reads DISTANCE after an actor in front moved sees it refreshed, as the moves and
            // the refreshed distances show together (OV-20261020-view-activity round 9).
            RefreshEntityDistances(force: true);
        }

        return target[memberName];
    }

    /// <summary>
    /// Refreshes every thing's and actor's <c>DISTANCE</c> (|dx| + |dy| from the player), which the
    /// <c>DIST</c> test (<c>IF_NEAR</c>/<c>IF_FAR</c>, <see cref="IsWithinDist"/>) and the
    /// <c>SKIP_FRAMES</c> near range read. The original refreshes them all at once, and only when
    /// the view changed since the last refresh (the player walked or was beamed, turned or tilted)
    /// or an actor in front of the player walked; between refreshes they keep their value, however
    /// far the objects move (OV-20261020-view-activity rule 5, OV-20261021-distance-refresh). A
    /// script's <c>SET</c> of a thing's or actor's position refreshes nothing, and reading
    /// <c>DISTANCE</c> refreshes nothing. A thing's or actor's <c>DISTANCE</c> reads 65535.999
    /// until the player first comes within 1.5 times its <c>CLIP_DIST</c> border, <c>LIBER</c> or
    /// not (OV-20261019-actor-contact). <paramref name="force"/> refreshes after a move in front.
    /// </summary>
    private void RefreshEntityDistances(bool force = false)
    {
        if (GetObject("player") is not Player player)
        {
            return;
        }

        var view = new DistanceView(
            player.Position.X,
            player.Position.Z,
            player.Position.Y,
            GetSkill("PLAYER_ANGLE"),
            GetSkill("PLAYER_TILT"),
            GetSkill("PLAYER_ARC"));
        if (!force && !_frontActorMoved && _distanceView == view && _entityDistances.Count == _levelObjects.OfType<MapEntity>().Count())
        {
            return;
        }

        _frontActorMoved = false;
        _distanceView = view;
        _entityDistances.Clear();
        AcknexObject? playerRegion = RegionAt(player.Region);
        foreach (MapEntity entity in _levelObjects.OfType<MapEntity>())
        {
            double distance = Math.Abs(entity.X - view.X) + Math.Abs(entity.Y - view.Y);
            _entityDistances[entity] = distance;
            if (!_distanceStarted.Contains(entity))
            {
                if (playerRegion is not null && distance >= DistanceStartFactor * ClipBorder(entity, playerRegion))
                {
                    entity[DistanceSkill] = UnrefreshedDistance;
                    continue;
                }

                _distanceStarted.Add(entity);
            }

            entity[DistanceSkill] = distance;
        }
    }

    /// <summary>What a change of refreshes the things' and actors' distances (<see cref="RefreshEntityDistances"/>).</summary>
    private readonly record struct DistanceView(double X, double Y, double EyeHeight, double Angle, double Tilt, double Arc);

    /// <summary>Notes an actor's move this cycle when it ends in front of the player (see <see cref="RefreshEntityDistances"/>).</summary>
    private void NoteFrontMove(Actor actor, double startX, double startY)
    {
        if ((actor.X == startX && actor.Y == startY) || GetObject("player") is not Player player)
        {
            return;
        }

        double angle = GetSkill("PLAYER_ANGLE");
        double depth = ((actor.X - player.Position.X) * Math.Cos(angle)) + ((actor.Y - player.Position.Z) * Math.Sin(angle));
        _frontActorMoved |= depth > 0;
    }

    private Viewer? CurrentViewer()
    {
        if (GetObject("player") is not Player player)
        {
            return null;
        }

        ProjectionModel projection = ProjectionModel.Create(
            GetSkill("PLAYER_ARC"),
            GetSkill("ASPECT"),
            GetSkill("PLAYER_TILT"),
            (int)Math.Round(GetSkill("SCREEN_WIDTH")),
            (int)Math.Round(GetSkill("SCREEN_HGT")));
        return new Viewer(player.Position.X, player.Position.Z, player.Position.Y, GetSkill("PLAYER_ANGLE"), projection);
    }

    private bool IsVisible(ViewTest test, Viewer viewer, MapEntity entity)
    {
        double bottom = EntityFeetHeight(entity);
        return MapEntitySizing.TryGetSize(_level, entity, out MapEntitySize size)
            ? test.IsSpriteVisible(viewer, entity.X, entity.Y, size.Width * 0.5, bottom, bottom + size.Height)
            : test.IsSpriteVisible(viewer, entity.X, entity.Y, 0, bottom, bottom + 1);
    }

    /// <summary>
    /// How many cycles' distance an actor covers this cycle under <c>SKIP_FRAMES</c> (p.113), 0 to
    /// stay put. An actor that was not visible in the last drawn frame, is outside its <c>DIST</c>
    /// and more than <see cref="SkipFramesNearRange"/> from the player (as last refreshed, see
    /// <see cref="RefreshEntityDistances"/>) moves only every
    /// (<c>SKIP_FRAMES</c> + 1)th cycle, counted from its own last move, by that many times the
    /// cycle's <c>TIME_CORR</c>; <c>FAR</c> (p.71) or <c>SKIP_FRAMES</c> 0 lifts it
    /// (OV-20261019-border-kinds, OV-20261020-view-activity).
    /// </summary>
    private int SkipFramesMultiplier(Actor actor)
    {
        int skip = (int)Math.Truncate(GetSkill("SKIP_FRAMES"));
        if (skip <= 0 ||
            actor["FAR"] > 0 ||
            GetObject("player") is not Player player ||
            _inView.Contains(actor) ||
            _entityDistances.GetValueOrDefault(actor, Math.Abs(actor.X - player.Position.X) + Math.Abs(actor.Y - player.Position.Z)) <= SkipFramesNearRange ||
            IsWithinDist(player, actor))
        {
            _skippedCycles.Remove(actor);
            return 1;
        }

        int skipped = _skippedCycles.GetValueOrDefault(actor) + 1;
        if (skipped <= skip)
        {
            _skippedCycles[actor] = skipped;
            return 0;
        }

        _skippedCycles.Remove(actor);
        return skip + 1;
    }

    /// <summary>
    /// Whether a <c>BERKELEY</c> wall sleeps this cycle: it runs its <c>EACH_TICK</c> only while it
    /// was in view in the last drawn frame or the player is within its <c>DIST</c> (p.53,
    /// OV-20261019-clip-detail, OV-20261020-view-activity). Without a player it never sleeps.
    /// </summary>
    private bool IsDormantBerkeleyWall(AcknexObject target) =>
        target is Wall wall &&
        wall["BERKELEY"] > 0 &&
        GetObject("player") is Player player &&
        !_inView.Contains(wall) &&
        !IsWithinDist(player, wall);

    /// <summary>The player is within an object's <c>DIST</c>, as the <c>IF_NEAR</c> test counts it (<see cref="TryCurrentProximity"/>).</summary>
    private bool IsWithinDist(Player player, AcknexObject target)
    {
        double threshold = target["DIST"];
        return threshold > 0 &&
               TryDistTestDistance(player, target, out double distance) &&
               distance < threshold &&
               (target is not MapEntity entity || VerticalOffsetFromCentre(player, entity) < threshold);
    }

    private static bool IsInactiveEntity(AcknexObject? target) =>
        target is MapEntity entity && entity["INVISIBLE"] > 0;

    private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double sx = bx - ax;
        double sy = by - ay;
        double lengthSquared = (sx * sx) + (sy * sy);
        if (lengthSquared == 0)
        {
            return Distance(px, py, ax, ay);
        }

        double t = (((px - ax) * sx) + ((py - ay) * sy)) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        return Distance(px, py, ax + (t * sx), ay + (t * sy));
    }

    private void SetHitResult(double distance, double range, double hitX = 0, double hitY = 0)
    {
        SetSkill("HIT_DIST", distance);
        SetSkill("RESULT", range > 0 ? Math.Max(0, GetSkill("SHOOT_FAC") * (1 - (distance / range))) : 0);
        SetSkill("HIT_X", hitX);
        SetSkill("HIT_Y", hitY);
    }

    private void ClearHitResult()
    {
        SetSkill("HIT_DIST", 0);
        SetSkill("RESULT", 0);
        SetSkill("HIT_X", 0);
        SetSkill("HIT_Y", 0);
    }

    private static double NormalizeAngle(double radians)
    {
        while (radians <= -Math.PI)
        {
            radians += Math.Tau;
        }

        while (radians > Math.PI)
        {
            radians -= Math.Tau;
        }

        return radians;
    }

    private bool TryResolveObjectByType(string name, string type, out AcknexObject? value)
    {
        if (name.Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }

        if (!_objectsByName.TryGetValue(name, out List<AcknexObject>? matches))
        {
            value = null;
            return false;
        }

        value = matches.FirstOrDefault(candidate => ObjectMatchesSynonymType(candidate, type));
        return value is not null;
    }

    private string? ResolveActionName(string name)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string current = name;
        while (IsKeywordReferenceType(current, "ACTION"))
        {
            if (!seen.Add(current) ||
                !_keywordReferences.TryGetValue(current, out string? target) ||
                string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            current = target;
        }

        return current;
    }

    private string? ResolveStringName(string name)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string current = name;
        while (IsKeywordReferenceType(current, "STRING"))
        {
            if (!seen.Add(current) ||
                !_keywordReferences.TryGetValue(current, out string? target) ||
                string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            current = target;
        }

        return current;
    }

    private string? ResolveUiElementName(string name)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string current = name;
        while (IsUiKeywordReference(current))
        {
            if (!seen.Add(current) ||
                !_keywordReferences.TryGetValue(current, out string? target) ||
                string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            current = target;
        }

        return _uiElements.ContainsKey(current) ? current : null;
    }

    private bool IsUiKeywordReference(string name) =>
        IsKeywordReferenceType(name, "OVERLAY") ||
        IsKeywordReferenceType(name, "PANEL") ||
        IsKeywordReferenceType(name, "TEXT");

    private static bool IsUiFlag(string property) =>
        property.Equals("VISIBLE", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("ABSPOS", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("RELPOS", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("REFRESH", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("TRANSPARENT", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("CENTER_X", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("CENTER_Y", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("CONDENSED", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("NARROW", StringComparison.OrdinalIgnoreCase);

    private static bool IsUiKeywordProperty(string property) =>
        property.Equals("BMAP", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("FONT", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("IF_KLICK", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("OVLY", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("OVLYS", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("PAN_MAP", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("STRING", StringComparison.OrdinalIgnoreCase);

    private static bool HasUiFlag(WdlUiElement element, string flag) =>
        element.Properties.Any(property =>
            property.Keyword.Equals("FLAGS", StringComparison.OrdinalIgnoreCase) &&
            property.Values.Any(value => value.Text.Equals(flag, StringComparison.OrdinalIgnoreCase)));

    private static WdlUiElement SetUiFlag(WdlUiElement element, string flag, bool enabled)
    {
        var properties = new List<WdlUiProperty>();
        bool handled = false;
        foreach (WdlUiProperty property in element.Properties)
        {
            if (!property.Keyword.Equals("FLAGS", StringComparison.OrdinalIgnoreCase))
            {
                properties.Add(property);
                continue;
            }

            handled = true;
            var values = property.Values
                .Where(value => !value.Text.Equals(flag, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (enabled)
            {
                values.Add(new WdlUiValue("Identifier", flag.ToUpperInvariant()));
            }

            if (values.Count > 0)
            {
                properties.Add(property with { Values = values.ToArray() });
            }
        }

        if (!handled && enabled)
        {
            properties.Add(new WdlUiProperty("FLAGS", [new WdlUiValue("Identifier", flag.ToUpperInvariant())]));
        }

        return element with { Properties = properties.ToArray() };
    }

    private static bool TryGetUiPropertyNumber(WdlUiElement element, string propertyName, out double value)
    {
        foreach (WdlUiProperty property in element.Properties)
        {
            if (property.Keyword.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                FirstUiNumber(property.Values) is { } parsed)
            {
                value = parsed;
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static WdlUiElement SetUiPropertyNumber(WdlUiElement element, string propertyName, double value)
    {
        var properties = new List<WdlUiProperty>();
        bool handled = false;
        foreach (WdlUiProperty property in element.Properties)
        {
            if (!property.Keyword.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                properties.Add(property);
                continue;
            }

            handled = true;
            properties.Add(property with
            {
                Values = [new WdlUiValue("Number", value.ToString("G15", CultureInfo.InvariantCulture))],
            });
        }

        if (!handled)
        {
            properties.Add(new WdlUiProperty(
                propertyName.ToUpperInvariant(),
                [new WdlUiValue("Number", value.ToString("G15", CultureInfo.InvariantCulture))]));
        }

        return element with { Properties = properties.ToArray() };
    }

    private static string? UiKeywordValue(WdlUiElement element, string propertyName)
    {
        if (IsTextStringProperty(element, propertyName))
        {
            return IndexedUiTextString(element);
        }

        return FirstUiKeyword(element, propertyName);
    }

    private static string? FirstUiKeyword(WdlUiElement element, string propertyName)
    {
        foreach (WdlUiProperty property in element.Properties)
        {
            if (!property.Keyword.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (WdlUiValue value in property.Values)
            {
                if (!value.Kind.Equals("Comma", StringComparison.OrdinalIgnoreCase))
                {
                    return value.Text;
                }
            }

            return null;
        }

        return null;
    }

    private static WdlUiElement SetUiPropertyKeyword(WdlUiElement element, string propertyName, string? value)
    {
        if (IsTextStringProperty(element, propertyName))
        {
            return SetUiTextString(element, value);
        }

        return SetSingleUiPropertyKeyword(element, propertyName, value);
    }

    private static WdlUiElement SetSingleUiPropertyKeyword(WdlUiElement element, string propertyName, string? value)
    {
        var properties = new List<WdlUiProperty>();
        bool handled = false;
        foreach (WdlUiProperty property in element.Properties)
        {
            if (!property.Keyword.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                properties.Add(property);
                continue;
            }

            handled = true;
            if (!string.IsNullOrWhiteSpace(value))
            {
                properties.Add(property with { Values = [new WdlUiValue("Identifier", value)] });
            }
        }

        if (!handled && !string.IsNullOrWhiteSpace(value))
        {
            properties.Add(new WdlUiProperty(
                propertyName.ToUpperInvariant(),
                [new WdlUiValue("Identifier", value)]));
        }

        return element with { Properties = properties.ToArray() };
    }

    private static WdlUiElement SetUiTextString(WdlUiElement element, string? value)
    {
        int index = UiTextStringIndex(element);
        var properties = new List<WdlUiProperty>();
        bool handled = false;
        foreach (WdlUiProperty property in element.Properties)
        {
            if (!property.Keyword.Equals("STRING", StringComparison.OrdinalIgnoreCase))
            {
                properties.Add(property);
                continue;
            }

            if (handled)
            {
                continue;
            }

            handled = true;
            List<string> names = UiKeywordValues(property.Values).ToList();
            while (names.Count < index)
            {
                names.Add("NULL");
            }

            names[index - 1] = string.IsNullOrWhiteSpace(value) ? "NULL" : value;
            properties.Add(property with { Values = UiKeywordListValues(names) });
        }

        if (!handled)
        {
            var names = new List<string>();
            while (names.Count < index)
            {
                names.Add("NULL");
            }

            names[index - 1] = string.IsNullOrWhiteSpace(value) ? "NULL" : value;
            properties.Add(new WdlUiProperty("STRING", UiKeywordListValues(names)));
        }

        return element with { Properties = properties.ToArray() };
    }

    private static string? IndexedUiTextString(WdlUiElement element)
    {
        int index = UiTextStringIndex(element);
        foreach (WdlUiProperty property in element.Properties)
        {
            if (!property.Keyword.Equals("STRING", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            List<string> names = UiKeywordValues(property.Values).ToList();
            return index <= names.Count ? names[index - 1] : null;
        }

        return null;
    }

    private static int UiTextStringIndex(WdlUiElement element)
    {
        int index = TryGetUiPropertyNumber(element, "INDEX", out double value)
            ? Math.Max(1, (int)Math.Truncate(value))
            : 1;
        if (TryGetUiPropertyNumber(element, "STRINGS", out double maximum) && maximum > 0)
        {
            index = Math.Min(index, Math.Max(1, (int)Math.Truncate(maximum)));
        }

        return index;
    }

    private static bool IsTextStringProperty(WdlUiElement element, string propertyName) =>
        element.Keyword.Equals("TEXT", StringComparison.OrdinalIgnoreCase) &&
        propertyName.Equals("STRING", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> UiKeywordValues(IReadOnlyList<WdlUiValue> values)
    {
        foreach (WdlUiValue value in values)
        {
            if (!value.Kind.Equals("Comma", StringComparison.OrdinalIgnoreCase))
            {
                yield return value.Text;
            }
        }
    }

    private static WdlUiValue[] UiKeywordListValues(IReadOnlyList<string> names)
    {
        var values = new List<WdlUiValue>();
        foreach (string name in names)
        {
            if (values.Count > 0)
            {
                values.Add(new WdlUiValue("Comma", ","));
            }

            values.Add(new WdlUiValue("Identifier", name));
        }

        return values.ToArray();
    }

    private static double? FirstUiNumber(IReadOnlyList<WdlUiValue> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        int sign = 1;
        int index = 0;
        if (values.Count > 1 &&
            values[0].Kind.Equals("Operator", StringComparison.OrdinalIgnoreCase) &&
            (values[0].Text == "-" || values[0].Text == "+"))
        {
            sign = values[0].Text == "-" ? -1 : 1;
            index = 1;
        }

        return values[index].Kind.Equals("Number", StringComparison.OrdinalIgnoreCase) &&
               double.TryParse(values[index].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? sign * value
            : null;
    }

    private bool IsKeywordReferenceType(string name, string type) =>
        _keywordReferenceTypes.TryGetValue(name, out string? declaredType) &&
        declaredType.Equals(type, StringComparison.OrdinalIgnoreCase);

    private static bool IsObjectSynonymType(string type) =>
        type.Equals("REGION", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("WALL", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("THING", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("ACTOR", StringComparison.OrdinalIgnoreCase);

    private static bool IsKeywordSynonymType(string type) =>
        type.Equals("TEXTURE", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("STRING", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("ACTION", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("OVERLAY", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("PANEL", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("TEXT", StringComparison.OrdinalIgnoreCase);

    private static bool ObjectMatchesSynonymType(AcknexObject value, string type) =>
        type.ToUpperInvariant() switch
        {
            "REGION" => value is Region,
            "WALL" => value is Wall,
            "THING" => value is Thing,
            "ACTOR" => value is Actor,
            _ => false,
        };

    private void AppendPrintText(string text)
    {
        if (_printDirectory is null)
        {
            return;
        }

        Directory.CreateDirectory(_printDirectory);
        File.AppendAllText(PrintFilePath(), text, Encoding.Latin1);
    }

    // The DOS runtime names the file in upper case with the number unpadded (PRINT0.TXT, OVSTA0.TXT;
    // OV-20261029-print-output).
    private string PrintFilePath() =>
        Path.Combine(
            _printDirectory!,
            string.Create(CultureInfo.InvariantCulture, $"{_printBaseName.ToUpperInvariant()}{_printNumber}.TXT"));

    private static string NormalizePrintBaseName(string baseName)
    {
        if (string.IsNullOrEmpty(baseName))
        {
            return "PRINT";
        }

        var builder = new StringBuilder(capacity: 5);
        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in baseName)
        {
            if (builder.Length == 5)
            {
                break;
            }

            if (invalidChars.Contains(c) || c is '/' or '\\')
            {
                continue;
            }

            builder.Append(c);
        }

        return builder.Length == 0 ? "PRINT" : builder.ToString();
    }

    private WdlPlayerSnapshot? CapturePlayerSnapshot()
    {
        if (GetObject("player") is not Player player)
        {
            return null;
        }

        Vector3 position = player.Position;
        return new WdlPlayerSnapshot(
            new Dictionary<string, double>(player.Skills, StringComparer.OrdinalIgnoreCase),
            position.X,
            position.Z,
            position.Y,
            player.Region);
    }

    private static WdlObjectSnapshot CaptureObjectSnapshot(AcknexObject obj)
    {
        string[] numberFields = obj switch
        {
            Region => RegionNumberFields,
            Wall => WallNumberFields,
            MapEntity => EntityNumberFields,
            _ => [],
        };
        string[] stringFields = obj switch
        {
            Region => RegionStringFields,
            Wall => WallStringFields,
            MapEntity => EntityStringFields,
            _ => [],
        };

        var numbers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (string field in numberFields)
        {
            numbers[field] = obj[field];
        }

        var strings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (string field in stringFields)
        {
            if (obj.TryGetString(field, out string? value))
            {
                strings[field] = value;
            }
        }

        return new WdlObjectSnapshot(
            new Dictionary<string, double>(obj.Skills, StringComparer.OrdinalIgnoreCase),
            numbers,
            strings);
    }

    private void RestoreSkills(IReadOnlyDictionary<string, double> skills)
    {
        _skills.Clear();
        foreach ((string name, double value) in skills)
        {
            SetSkill(name, value);
        }
    }

    private void RestoreStrings(IReadOnlyDictionary<string, string> strings)
    {
        foreach ((string name, string value) in strings)
        {
            if (_strings.TryGetValue(name, out WdlString current))
            {
                _strings[name] = current.WithValue(value);
            }
        }
    }

    private void RestoreKeywordReferences(IReadOnlyDictionary<string, string?> keywordReferences)
    {
        _keywordReferences.Clear();
        foreach ((string name, string? value) in keywordReferences)
        {
            if (_keywordReferenceTypes.ContainsKey(name))
            {
                _keywordReferences[name] = value;
            }
        }
    }

    private void RestoreEachTickActions(List<string?> eachTickActions)
    {
        Array.Clear(_eachTickActions);
        for (int i = 0; i < Math.Min(_eachTickActions.Length, eachTickActions.Count); i++)
        {
            _eachTickActions[i] = string.IsNullOrWhiteSpace(eachTickActions[i]) ? null : eachTickActions[i];
        }
    }

    private void RestoreEachSecActions(List<string?>? eachSecActions, double? secondFraction)
    {
        Array.Clear(_eachSecActions);
        for (int i = 0; i < Math.Min(_eachSecActions.Length, eachSecActions?.Count ?? 0); i++)
        {
            _eachSecActions[i] = string.IsNullOrWhiteSpace(eachSecActions![i]) ? null : eachSecActions[i];
        }

        _secondFraction = Math.Clamp(secondFraction ?? 0, 0, Math.BitDecrement(1.0));
    }

    private void RestoreActionHooks(IReadOnlyDictionary<string, string?> actionHooks)
    {
        _actionHooks.Clear();
        foreach ((string name, string? actionName) in actionHooks)
        {
            _actionHooks[name] = string.IsNullOrWhiteSpace(actionName) ? null : actionName;
        }
    }

    private void RestoreVertices(List<Vertex> vertices)
    {
        if (_level.Vertices is not IList<Vertex> current)
        {
            return;
        }

        for (int i = 0; i < Math.Min(current.Count, vertices.Count); i++)
        {
            current[i] = vertices[i];
        }
    }

    private void RestoreLevelObjects(List<WdlObjectSnapshot> snapshots)
    {
        List<AcknexObject> objects = MutableLevelObjects();
        int count = Math.Min(objects.Count, snapshots.Count);
        for (int i = 0; i < count; i++)
        {
            RestoreObjectSnapshot(objects[i], snapshots[i]);
        }
    }

    private List<AcknexObject> MutableLevelObjects() =>
        _level.Regions
            .Cast<AcknexObject>()
            .Concat(_level.Walls)
            .Concat(_level.Things)
            .Concat(_level.Actors)
            .ToList();

    private static void RestoreObjectSnapshot(AcknexObject obj, WdlObjectSnapshot snapshot)
    {
        obj.ReplaceSkills(snapshot.Skills);
        foreach ((string name, double value) in snapshot.Numbers)
        {
            obj[name] = value;
        }

        foreach ((string name, string? value) in snapshot.Strings)
        {
            obj.TrySetString(name, value);
        }
    }

    private void RestorePlayer(WdlPlayerSnapshot? snapshot)
    {
        if (snapshot is null || GetObject("player") is not Player player)
        {
            return;
        }

        player.ReplaceSkills(snapshot.Skills);
        player["region"] = snapshot.Region;
        player.Position = new Vector3((float)snapshot.X, (float)snapshot.Z, (float)snapshot.Y);
    }

    private readonly record struct ActionScope(
        bool HasMy,
        AcknexObject? My,
        bool HasThere,
        AcknexObject? There,
        AcknexObject? Target);

    private sealed class ActiveAction(WdlExecutionState state, WdlWaitRequest wait, ActionScope scope)
    {
        public WdlExecutionState State { get; } = state;

        public WdlWaitMode Mode { get; } = wait.Mode;

        public ActionScope Scope { get; } = scope;

        public double Remaining { get; set; } = wait.Amount;

        public ActiveAction WithWait(WdlWaitRequest wait) => new(State, wait, Scope);
    }

    private sealed class ActiveInputAction(WdlExecutionState state, WdlInputRequest request, ActionScope scope)
    {
        public WdlExecutionState State { get; } = state;

        public WdlInputRequest Request { get; } = request;

        public ActionScope Scope { get; } = scope;
    }

    private sealed record HitCandidate(
        AcknexObject Target,
        double Distance,
        double TraceDistance,
        double HitX = 0,
        double HitY = 0);

    private sealed class TextureAnimationState
    {
        public double ElapsedTicks { get; set; }

        public bool WasPlaying { get; set; }
    }
}
