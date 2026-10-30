// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;
using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Formats.Save;
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
    IWdlSkillRangeContext,
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
    private readonly Dictionary<string, SkillRange> _bounds;
    private readonly Dictionary<string, WdlString> _strings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WdlUiElement> _uiElements = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _keywordReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _keywordReferenceTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, string> _sounds;
    private readonly IReadOnlyDictionary<string, string> _palettes;
    private readonly List<WdlSoundRequest> _soundRequests = [];

    /// <summary>When each sound channel's current sound started, to find the oldest one (OV-20261029-audio).</summary>
    private readonly long[] _channelStarted = new long[8];

    /// <summary>World cycles until a finished or stopped channel reads free; 0 when none is pending.</summary>
    private readonly int[] _channelRelease = new int[8];

    private long _soundSequence;
    private readonly List<WdlAudioRequest> _audioRequests = [];
    private readonly List<WdlTextureSoundRequest> _textureSoundRequests = [];
    private readonly List<WdlPaletteFadeRequest> _paletteFadeRequests = [];

    // The palette a full save names, by declaration number: the last declared at the start (apathy's
    // six palettes save 6, the probe's one 1; round 13), then the last FADE_PAL's (LEGALISM, which
    // declares five and fades to its fourth, saves 4; round 15).
    private int? _savedPalette;
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

    /// <summary>The point each actor last turned toward and the facing it took then (OV-20261029-actor-step round 5).</summary>
    private readonly Dictionary<Actor, (double X, double Y, double Facing)> _legFacings = [];

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
        _bounds = new Dictionary<string, SkillRange>(level.SkillBounds, StringComparer.OrdinalIgnoreCase);
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
        InitializeFootHeights();
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

    /// <summary>
    /// The level's WDL, map and archive file names. When set, a full <c>SAVE</c> also writes the
    /// original's file (<see cref="CaptureOriginalSave"/>) in <see cref="SaveDirectory"/>, in the
    /// instruction.
    /// </summary>
    public (string Wdl, string Wmp, string Wrs)? OriginalSaveNames { get; set; }

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
            WaitingActions = CaptureWaitingActions(),
        };

    /// <summary>Restores a state previously captured with <see cref="CaptureSnapshot"/>.</summary>
    public void RestoreSnapshot(WdlRuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RestoreSkills(snapshot.Skills);
        for (int channel = 0; channel < 8; channel++)
        {
            SetSkill($"CHANNEL_{channel}", 0);
            _channelRelease[channel] = 0;
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
        if (snapshot.WaitingActions is { } waiting)
        {
            RestoreWaitingActions(waiting);
        }
    }

    /// <summary>
    /// The waiting actions as <see cref="WdlWaitingActionSnapshot"/>s. An action whose blocks cannot be
    /// traced back to a declared action (it never happens for parsed WDL) is left out.
    /// </summary>
    private List<WdlWaitingActionSnapshot> CaptureWaitingActions()
    {
        List<AcknexObject> objects = MutableLevelObjects();
        int IndexOf(AcknexObject? obj) => obj is null ? -1 : objects.IndexOf(obj);
        var captured = new List<WdlWaitingActionSnapshot>();
        foreach (ActiveAction action in _waitingActions)
        {
            WdlExecutionFrame[] frames = [.. action.State.Frames.Reverse()];
            if (frames.Length == 0 ||
                _actions.FirstOrDefault(pair => ReferenceEquals(pair.Value.Items, frames[0].Items)).Key is not { } name)
            {
                continue;
            }

            var saved = new List<WdlFrameSnapshot> { new([], false, frames[0].Index) };
            var path = new List<int>();
            bool traced = true;
            for (int i = 1; i < frames.Length && traced; i++)
            {
                IReadOnlyList<WdlItem> parent = frames[i - 1].Items;
                int item = -1;
                for (int j = 0; j < parent.Count; j++)
                {
                    if (parent[j].Body is { } body && ReferenceEquals(body.Items, frames[i].Items))
                    {
                        item = j;
                        break;
                    }
                }

                traced = item >= 0;
                path.Add(item);
                saved.Add(new WdlFrameSnapshot([.. path], frames[i].Kind == WdlExecutionFrameKind.While, frames[i].Index));
            }

            if (traced)
            {
                captured.Add(new WdlWaitingActionSnapshot(
                    name,
                    saved,
                    action.Mode == WdlWaitMode.FrameCycles,
                    action.Remaining,
                    action.Scope.HasMy ? IndexOf(action.Scope.My) : -1,
                    action.Scope.HasThere ? IndexOf(action.Scope.There) : -1,
                    IndexOf(action.Scope.Target)));
            }
        }

        return captured;
    }

    /// <summary>
    /// Takes the global <c>EACH_TICK</c> and <c>EACH_SEC</c> slots, the key hooks and the waiting actions of a full save the original wrote,
    /// in place of the current ones (OV-20261029-save-body rounds 7 and 8). Actions are numbered from 11
    /// in declaration order; a waiting action goes on after the instruction in its saved place. The
    /// action that ran the <c>SAVE</c> is not resumed, as in the original. Their <c>MY</c> and
    /// <c>YOU</c> are not read yet.
    /// </summary>
    /// <returns>The number of waiting actions taken.</returns>
    public int ImportOriginalActions(IReadOnlyList<SaveAction> actions, IReadOnlyList<int>? slots)
    {
        ArgumentNullException.ThrowIfNull(actions);
        string? NameOf(int number) =>
            number >= FirstActionNumber && number - FirstActionNumber < _level.ActionOrder.Count
                ? _level.ActionOrder[number - FirstActionNumber]
                : null;

        if (slots is not null)
        {
            for (int slot = 1; slot <= EachTickSlotCount && EachTickSlot(slot) < slots.Count; slot++)
            {
                SetEachTickAction(slot, NameOf(slots[EachTickSlot(slot)]));
            }

            for (int slot = 1; slot <= EachSecSlotCount && AcknexFullSave.EachSecSlotWord(slot) < slots.Count; slot++)
            {
                SetEachSecAction(slot, NameOf(slots[AcknexFullSave.EachSecSlotWord(slot)]));
            }

            // Engine defaults (numbers below 11) have no WDL action, so their keys keep OpenVirtue's own handling.
            foreach ((string hook, int word) in AcknexFullSave.KeySlotWords)
            {
                if (word < slots.Count && slots[word] >= FirstActionNumber && NameOf(slots[word]) is { } action)
                {
                    SetActionHook(hook, action);
                }
            }
        }

        _waitingActions.Clear();
        List<AcknexObject> placedObjects = [.. _level.MapObjects.OfType<AcknexObject>()];
        bool savingAction = true;
        foreach (SaveAction saved in actions)
        {
            // The second list opens with the action that ran the SAVE, which is not resumed; the actions
            // objects' events started follow it, with their MY (round 17).
            bool skip = saved.Running && savingAction;
            savingAction &= !saved.Running;
            if (skip || NameOf(saved.Action) is not { } name || !_actions.TryGetValue(name, out WdlBlock? block) ||
                !WdlFlatPosition.TryResumeAfter(block, saved.Position, out WdlExecutionState state, out WdlItem item))
            {
                continue;
            }

            WdlWaitMode mode = item.Keyword.Equals("WAIT", StringComparison.OrdinalIgnoreCase) ? WdlWaitMode.FrameCycles : WdlWaitMode.Ticks;
            AcknexObject? my = saved.Running && saved.My >= 1 && saved.My <= placedObjects.Count ? placedObjects[saved.My - 1] : null;
            var scope = new ActionScope(my is not null, my, false, null, null);
            _waitingActions.Add(new ActiveAction(state, new WdlWaitRequest(saved.Remaining / 1024.0, mode), scope));
        }

        return _waitingActions.Count;
    }

    /// <summary>
    /// The state as the original's full <c>SAVE</c> holds it (OV-20261029-save-body rounds 7 to 9),
    /// for <see cref="AcknexFullSave.Write"/>:
    /// <list type="bullet">
    /// <item>the unplaced walls, things and actors in reverse declaration order, then actors, things and
    /// walls, each in reverse map order, with their skills, textures, events and geometry;</item>
    /// <item>the <c>EACH_TICK</c> slots;</item>
    /// <item>the synonyms' objects;</item>
    /// <item>the level's non-<c>LOCAL</c> skills and the engine's own;</item>
    /// <item>the waiting actions at their flat places (the action running the <c>SAVE</c> is not
    /// among them).</item>
    /// </list>
    /// </summary>
    public FullSave CaptureOriginalSave(string wdlName, string wmpName, string wrsName)
    {
        // Values are cut toward zero to 1/1024, as the original reads literals: AMBIENT 0.2 is saved as
        // 204, FLOOR_HGT 26.2 as 26828 (round 15). The rounding to six places first keeps a value that is
        // a whole number of 1/1024 steps, give or take double error, on that step.
        static int Raw(double value) => (int)Math.Clamp(Math.Truncate(Math.Round(value * 1024, 6)), int.MinValue, int.MaxValue);

        // Map coordinates are cut toward zero to 1/1024, not rounded (round 13). The map writes three
        // decimals and OpenVirtue keeps them as floats, so the decimal value is recovered first.
        static int Vertex(double value) => (int)Math.Clamp(Math.Truncate(Math.Round(value, 3) * 1024), -int.MaxValue, int.MaxValue);

        // Textures count by declaration, repeats included (apathy's walls read 18 higher than a count of
        // names); a repeated name takes the place of its last declaration (round 12).
        List<string> textures = _level.TextureDeclarations.Count > 0 ? [.. _level.TextureDeclarations] : [.. _level.Textures.Keys];
        int TextureNumber(string? name) =>
            name is null ? 0 : textures.FindLastIndex(t => t.Equals(TextureAnimation.LogicalTextureName(name), StringComparison.OrdinalIgnoreCase)) + 1;
        Dictionary<string, int> Fields(AcknexObject obj, IEnumerable<string> names) =>
            names.ToDictionary(static n => n, n => Raw(obj[n]), StringComparer.OrdinalIgnoreCase);

        // A thing's or actor's ANGLE is saved as its absolute facing (1608 for a 90° placement), and Z as
        // the absolute height of its foot: the floor plus OpenVirtue's Z less HEIGHT, unless it stands on
        // the ground (round 12).
        Dictionary<string, int> EntityFields(MapEntity entity, IEnumerable<string> names)
        {
            Dictionary<string, int> fields = Fields(entity, names);
            fields["X"] = Vertex(entity.X);
            fields["Y"] = Vertex(entity.Y);
            // The facing runs from 0 to 2π and is cut toward zero: 270° reads 4825, 180° 3216 (round 13).
            double facing = NormalizeAngle(entity.FacingAngleRadians);
            fields["ANGLE"] = (int)Math.Truncate((facing < 0 ? facing + Math.Tau : facing) * 1024);
            // Z is the script's Z (MapEntity.FootZ), which the runtime starts as the original does.
            return fields;
        }

        int ActionNumber(string? name) =>
            name is null ? -1 : _level.ActionOrder.ToList().FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)) is >= 0 and int i ? i + FirstActionNumber : -1;

        // A texture's size in world steps less one raw unit, from its first bitmap and scale, as a
        // 16-bit word: (pixels·1024 − 1) / SCALE (round 13; 16383 without a texture).
        LevelTexture? TextureOf(string? name) =>
            name is not null && _level.Textures.TryGetValue(TextureAnimation.LogicalTextureName(name), out LevelTexture found) ? found : null;
        static int Step(int pixels, double scale) =>
            pixels <= 0 || scale <= 0 ? 16383 : (int)Math.Truncate(((pixels * 1024.0) - 1) / scale);
        int AttachNumber(string? name) => name is null ? -1 : TextureNumber(name);
        Dictionary<string, int> Events(AcknexObject obj) =>
            AcknexFullSave.EventNames
                .Select(e => (Event: e, Action: obj.TryGetString(e, out string? action) ? ActionNumber(action) : -1))
                .Where(static e => e.Action >= 0)
                .ToDictionary(static e => e.Event, static e => e.Action, StringComparer.OrdinalIgnoreCase);

        var unplaced = new HashSet<AcknexObject>(_level.UnplacedObjects, ReferenceEqualityComparer.Instance);
        string[] entityFields = ["ANGLE", "TARGET_X", "TARGET_Y", "HEIGHT", "SPEED", "VSPEED", "ASPEED", "DIST", "SKILL1", "SKILL2", "SKILL3",
            "SKILL4", "SKILL5", "SKILL6", "SKILL7", "SKILL8", "CYCLE", "X", "Y", "Z"];
        string[] wallFields = ["OFFSET_X", "OFFSET_Y", "DIST", "SKILL1", "SKILL2", "SKILL3", "SKILL4"];
        string[] flagNames = ["INVISIBLE", "PASSABLE", "BERKELEY", "LIBER", "FAR", "GROUND", "CAREFULLY", "CANDELABER", "IMMATERIAL", "SENSITIVE",
            "FRAGILE", "IMPASSABLE", "TRANSPARENT", "PORTCULLIS", "FENCE", "FLAG1", "FLAG2", "FLAG3", "FLAG4", "FLAG5", "FLAG6", "FLAG7", "FLAG8"];
        string[] FlagsOf(AcknexObject obj) => [.. flagNames.Where(f => obj[f] > 0)];

        FullSaveEntity Entity(MapEntity entity)
        {
            // An unplaced object keeps its zero height until a script brings it in.
            Dictionary<string, int> fields = unplaced.Contains(entity) && entity["INVISIBLE"] > 0
                ? Fields(entity, entityFields)
                : EntityFields(entity, entityFields);
            LevelTexture? texture = TextureOf(entity.Texture);
            return new FullSaveEntity(entity is Actor, fields, TextureNumber(entity.Texture))
            {
                Flags = FlagsOf(entity),
                Size = (Step(texture?.Width ?? 0, texture?.ScaleX ?? 0), Step(texture?.Height ?? 0, texture?.ScaleY ?? 0)),
                Region = Math.Max(0, entity.Region),
                Floor = entity.Region >= 0 && entity.Region < _level.Regions.Count && !(unplaced.Contains(entity) && entity["INVISIBLE"] > 0)
                    ? Raw(LevelTopology.FloorHeightAt(_level, entity.Region, entity.X, entity.Y))
                    : 0,
                Attach = AttachNumber(entity.AttachTexture),
                Events = Events(entity),
                MapColor = (int)entity["MAP_COLOR"],
                Unplaced = unplaced.Contains(entity),
            };
        }

        FullSaveWall WallRecord(Wall wall)
        {
            LevelTexture? texture = TextureOf(wall.Texture);
            var record = new FullSaveWall(Fields(wall, wallFields), 0, 0, (0, 0), (0, 0), 0, TextureNumber(wall.Texture))
            {
                Flags = FlagsOf(wall),
                OneRegion = wall.LeftRegion == wall.RightRegion,
                TextureStep = Step(texture?.Height ?? 0, texture?.ScaleY ?? 0),
                Attach = AttachNumber(wall.AttachTexture),
                Events = Events(wall),
                MapColor = (int)wall["MAP_COLOR"],
                Unplaced = unplaced.Contains(wall),
            };
            if (record.Unplaced)
            {
                return record;
            }

            Vertex a = _level.Vertices[Math.Clamp(wall.Vertex1, 0, _level.Vertices.Count - 1)];
            Vertex b = _level.Vertices[Math.Clamp(wall.Vertex2, 0, _level.Vertices.Count - 1)];
            // The length is worked out in fixed point: dx² and dy² each lose their low 10 bits, and the
            // square root of the sum is cut down to 1/1024, so 2050 reads 2049 and 245 reads 243. The span
            // is that × SCALE_X (round 13, all compared retail walls).
            long rawDx = (long)Vertex(b.X) - Vertex(a.X);
            long rawDy = (long)Vertex(b.Y) - Vertex(a.Y);
            int rawLength = (int)IntegerSquareRoot((((rawDx * rawDx) >> 10) + ((rawDy * rawDy) >> 10)) << 10);
            double scale = texture is { ScaleX: > 0 } t ? t.ScaleX : 1;
            return record with
            {
                RawLength = rawLength,
                RawSpan = (int)Math.Truncate(rawLength * scale),
                First = (Vertex(a.X), Vertex(a.Y)),
                Second = (Vertex(b.X), Vertex(b.Y)),
                Heights = (Vertex(a.Z), Vertex(b.Z)),
                RightRegion = Math.Max(0, wall.RightRegion),
                LeftRegion = Math.Max(0, wall.LeftRegion),
            };
        }

        // Unplaced objects come first, in reverse declaration order (round 13).
        List<FullSaveObject> prefix =
            [.. _level.UnplacedObjects.Reverse().Select(o => o is Wall wall ? (FullSaveObject)WallRecord(wall) : Entity((MapEntity)o))];
        List<FullSaveEntity> entities =
        [
            .. _level.Actors.Where(a => !unplaced.Contains(a)).Reverse().Select(Entity),
            .. _level.Things.Where(t => !unplaced.Contains(t)).Reverse().Select(Entity),
        ];
        List<FullSaveWall> walls = [.. _level.Walls.AsEnumerable().Reverse().Select(WallRecord)];

        int[] slots = [.. Enumerable.Repeat(-1, AcknexFullSave.SlotWords)];
        foreach ((int word, int engineAction) in EngineSlotWords)
        {
            slots[word] = engineAction;
        }

        for (int slot = 1; slot <= EachTickSlotCount; slot++)
        {
            slots[EachTickSlot(slot)] = ActionNumber(_eachTickActions[slot - 1]);
        }

        for (int slot = 1; slot <= EachSecSlotCount; slot++)
        {
            slots[AcknexFullSave.EachSecSlotWord(slot)] = ActionNumber(_eachSecActions[slot - 1]);
        }

        // A key hook the script set, or cleared, replaces the engine's default at its scan code (round 16).
        foreach ((string hook, string? action) in _actionHooks)
        {
            if (AcknexFullSave.KeySlotWords.TryGetValue(hook, out int word))
            {
                slots[word] = ActionNumber(action);
            }
        }

        // A region is its index (the room read 0); a wall, thing or actor its map place + 1, counting
        // the map's walls, things and actors only (rounds 9, 10 and 15). The other kinds hold their
        // target's number (round 15, LEGALISM): an action's, a texture's, a string's among the STRING
        // declarations and a panel's, text's or overlay's among the three kinds' declarations together.
        List<AcknexObject> placedObjects = [.. _level.MapObjects.OfType<AcknexObject>()];
        int Numbered(string kind, string? name) =>
            name is not null && _level.NumberedDeclarations.TryGetValue(kind, out List<string>? names) &&
            names.FindIndex(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) is >= 0 and int found ? found + 1 : -1;
        List<int> synonyms = [];
        foreach ((string synonym, WdlSynonym declared) in _level.Synonyms.Reverse())
        {
            int number;
            if (_keywordReferences.ContainsKey(synonym))
            {
                string? target = KeywordTarget(synonym);
                number = declared.Type?.ToUpperInvariant() switch
                {
                    "ACTION" => ActionNumber(target),
                    "TEXTURE" => target is null ? -1 : TextureNumber(target),
                    "STRING" => Numbered("STRING", target),
                    _ => Numbered(WdlDeclarations.ScreenElements, target),
                };
            }
            else
            {
                number = GetObject(synonym) switch
                {
                    null => -1,
                    Region region => _level.Regions.ToList().IndexOf(region),
                    AcknexObject target => placedObjects.IndexOf(target) is >= 0 and int place ? place + 1 : -1,
                };
            }

            synonyms.Add(number);
        }

        synonyms.AddRange(Enumerable.Repeat(-1, AcknexFullSave.PredefinedSynonyms));

        SaveSkill SkillRecord(string name)
        {
            (int min, int max) = _bounds.TryGetValue(name, out SkillRange range)
                ? (double.IsFinite(range.Min) ? Raw(range.Min) : SaveSkill.NoMin, double.IsFinite(range.Max) ? Raw(range.Max) : SaveSkill.NoMax)
                : (SaveSkill.NoMin, SaveSkill.NoMax);
            return new SaveSkill(name, Raw(GetSkill(name)), min, max);
        }

        // The level's skills, newest first, then the engine's. A level may declare an engine skill again
        // (PLAYER_SIZE, PLAYER_TILT in move.wdl); the original lists each name once, in the engine's
        // block at its own place, with the declared range (apathy: 633 records, not 652; LEGALISM round 16).
        var predefined = new HashSet<string>(AcknexPredefinedSkills.Names, StringComparer.OrdinalIgnoreCase);
        List<SaveSkill> skills =
        [
            .. _level.SkillDeclarationOrder.Reverse()
                .Where(n => !_level.LocalSkills.Contains(n) && !predefined.Contains(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(SkillRecord),
            .. AcknexPredefinedSkills.Names.Distinct(StringComparer.OrdinalIgnoreCase).Select(SkillRecord),
        ];

        // The action running the SAVE opens the second list: the waiting action being resumed, with its MY.
        List<SaveAction> actions = [];
        if (_resumedRecord is { } resumed)
        {
            actions.Add(resumed.Action.Scope.HasMy && resumed.Action.Scope.My is { } runningMy
                ? resumed.Record with { My = placedObjects.IndexOf(runningMy) is >= 0 and int at ? at + 1 : -1, You = 0 }
                : resumed.Record);
        }
        else
        {
            // An action that has not waited yet: any action at any place loads (round 9).
            actions.Add(new SaveAction(FirstActionNumber, 0, 0, 1, -1, -1, Running: true));
        }

        foreach (ActiveAction action in _waitingActions)
        {
            if (action.State.Frames.Count == 0)
            {
                continue;
            }

            WdlExecutionFrame bottom = action.State.Frames.Last();
            string? name = _actions.FirstOrDefault(pair => ReferenceEquals(pair.Value.Items, bottom.Items)).Key;
            if (name is not null && WdlFlatPosition.TryPositionOf(_actions[name], action.State, out int position, out int next))
            {
                // The original saves a wait as it stands after this cycle's count: sb_note's WAIT 2,
                // armed in the cycle of the SAVE, was saved as 1 (round 9), and WAITT 100 armed four
                // cycles earlier as 100 less four cycles' TIME_CORR (round 8).
                double step = action.Mode == WdlWaitMode.FrameCycles ? 1 : GetSkill(Timing.TimeCorrectionSkill);
                int remaining = Raw(Math.Max(0, action.Remaining - step));
                if (action.Scope.HasMy && action.Scope.My is { } my)
                {
                    // An action an object's event started goes in the section's second list, with MY as the
                    // object's map number (a thing's IF_NEAR action read 14 for the 14th object) and YOU 0
                    // (round 17). A region's MY as its index is not measured.
                    int myNumber = my is Region region ? _level.Regions.ToList().IndexOf(region) : placedObjects.IndexOf(my) is >= 0 and int place ? place + 1 : -1;
                    actions.Add(new SaveAction(ActionNumber(name), remaining, position, next, myNumber, 0, Running: true));
                }
                else
                {
                    actions.Add(new SaveAction(ActionNumber(name), remaining, position, next, -1, -1, Running: false));
                }
            }
        }

        // A region's GENIUS is saved as the number of the first map object of that name, counting the
        // map's walls, things and actors from 1 (round 15).
        List<AcknexObject> mapObjects = [.. _level.MapObjects.OfType<AcknexObject>()];
        int GeniusNumber(string? name) =>
            string.IsNullOrWhiteSpace(name) ? -1 : mapObjects.FindIndex(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is >= 0 and int found ? found + 1 : -1;
        string[] regionFields = [.. AcknexFullSave.RegionFieldOffsets.Select(static f => f.Name)];
        string[] regionFlags = ["BASE", "STICKY", "FLOOR_ASCEND", "FLOOR_DESCEND", "CEIL_ASCEND", "CEIL_DESCEND",
            "FLAG1", "FLAG2", "FLAG3", "FLAG4", "FLAG5", "FLAG6", "FLAG7", "FLAG8"];
        int playerRegion = (int)GetSkill("PLAYER_REGION");
        List<FullSaveRegion> regions =
        [
            .. _level.Regions.Select(static (r, i) => (Region: r, Index: i)).Reverse()
                .Where(static p => p.Region["SAVE"] > 0 || p.Region["SAVE_ALL"] > 0)
                .Select(p => new FullSaveRegion(Fields(p.Region, regionFields), TextureNumber(p.Region.FloorTexture), TextureNumber(p.Region.CeilTexture), p.Region["SAVE_ALL"] > 0)
                {
                    Flags = [.. regionFlags.Where(f => p.Region[f] > 0)],
                    Here = p.Index == playerRegion,
                    Events = AcknexFullSave.RegionEventNames
                        .Select(e => (Event: e, Action: p.Region.TryGetString(e, out string? action) ? ActionNumber(action) : -1))
                        .Where(static e => e.Action >= 0)
                        .ToDictionary(static e => e.Event, static e => e.Action, StringComparer.OrdinalIgnoreCase),
                    Genius = GeniusNumber(p.Region.GeniusName),
                }),
        ];

        List<FullSaveTexture> saveTextures =
        [
            .. _level.SaveTextures.Reverse().Select(t =>
            {
                var words = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AMBIENT"] = Raw(t.Ambient), ["ALBEDO"] = Raw(t.Albedo), ["SCALE_X"] = Raw(t.ScaleX), ["SCALE_Y"] = Raw(t.ScaleY),
                    ["FONT"] = t.Font, ["TOUCH"] = t.Touch, ["SOUND"] = t.Sound, ["ATTACH"] = AttachNumber(t.AttachName),
                    ["SVOL"] = Raw(t.SoundVolume), ["SDIST"] = Raw(t.SoundDistance), ["SVDIST"] = Raw(t.SoundVerticalDistance),
                    ["POS_X"] = Raw(t.PosX), ["POS_Y"] = Raw(t.PosY),
                };
                foreach ((string e, string action) in t.Events)
                {
                    words[e] = ActionNumber(action);
                }

                return new FullSaveTexture(words);
            }),
        ];

        return new FullSave(wdlName, wmpName, wrsName, entities, walls, slots, synonyms, skills, actions)
        {
            Textures = saveTextures,
            Regions = regions,
            Unplaced = prefix,
            PlayerRegion = (int)GetSkill("PLAYER_REGION"),
            Palette = _savedPalette ?? _level.Palettes.Count,
        };
    }

    /// <summary>
    /// Points the level's synonyms at the objects a full save the original wrote names (reverse
    /// declaration order; a region by index, a wall, thing or actor by map place + 1, −1 for none).
    /// Only synonyms of a known object kind are set, so a synonym of another kind is left alone.
    /// </summary>
    /// <returns>How many synonyms were set.</returns>
    public int ImportOriginalSynonyms(IReadOnlyList<int> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        List<string> names = [.. _level.Synonyms.Keys.Reverse()];
        List<AcknexObject> placedObjects = [.. _level.MapObjects.OfType<AcknexObject>()];
        int set = 0;
        for (int i = 0; i < names.Count && i < words.Count; i++)
        {
            int number = words[i];
            WdlSynonym synonym = _level.Synonyms[names[i]];
            AcknexObject? target = synonym.Type?.ToUpperInvariant() switch
            {
                "REGION" => number >= 0 && number < _level.Regions.Count ? _level.Regions[number] : null,
                "WALL" or "THING" or "ACTOR" => number >= 1 && number <= placedObjects.Count ? placedObjects[number - 1] : null,
                _ => null,
            };
            if (target is not null || number == -1)
            {
                if (target is null)
                {
                    _objects.Remove(names[i]);
                }
                else
                {
                    SetObjectReference(names[i], target);
                }

                set++;
            }
        }

        return set;
    }

    private static long IntegerSquareRoot(long value)
    {
        long root = (long)Math.Sqrt(value);
        while (root * root > value)
        {
            root--;
        }

        while ((root + 1) * (root + 1) <= value)
        {
            root++;
        }

        return root;
    }

    /// <summary>The number the original's saves give the first declared action (OV-20261029-save-body).</summary>
    private const int FirstActionNumber = 11;

    /// <summary>
    /// The slot section's word for <c>EACH_TICK.n</c>: 135 + 2n, every other word from 137 to 167
    /// (<c>EACH_TICK.2</c> was word 139 and <c>EACH_TICK.3</c> word 141, OV-20261029-save-body round 9).
    /// </summary>
    private static int EachTickSlot(int slot) => 135 + (2 * slot);

    /// <summary>The engine's own entries in the slot section, the same in every save the original wrote.</summary>
    private static readonly (int Word, int Action)[] EngineSlotWords =
        [(11, 9), (12, 7), (13, 8), (15, 6), (27, 7), (53, 8), (60, 0), (61, 1), (63, 2), (64, 3), (68, 4), (88, 5)];

    /// <summary>Replaces the waiting actions with saved ones (the original drops the ones waiting at the load).</summary>
    private void RestoreWaitingActions(List<WdlWaitingActionSnapshot> waiting)
    {
        List<AcknexObject> objects = MutableLevelObjects();
        AcknexObject? At(int index) => index >= 0 && index < objects.Count ? objects[index] : null;
        _waitingActions.Clear();
        foreach (WdlWaitingActionSnapshot saved in waiting)
        {
            if (!_actions.TryGetValue(saved.Action, out WdlBlock? block) || saved.Frames.Count == 0)
            {
                continue;
            }

            var state = new WdlExecutionState(block);
            state.Frames.Peek().Index = saved.Frames[0].Index;
            bool restored = true;
            for (int i = 1; i < saved.Frames.Count && restored; i++)
            {
                IReadOnlyList<WdlItem> items = block.Items;
                WdlItem? owner = null;
                foreach (int index in saved.Frames[i].Path)
                {
                    if (index < 0 || index >= items.Count || items[index].Body is not { } body)
                    {
                        restored = false;
                        break;
                    }

                    owner = items[index];
                    items = body.Items;
                }

                if (restored && owner is not null)
                {
                    WdlExecutionFrame frame = saved.Frames[i].While ? WdlExecutionFrame.While(owner) : WdlExecutionFrame.Block(items);
                    frame.Index = saved.Frames[i].Index;
                    state.Frames.Push(frame);
                }
            }

            if (restored)
            {
                var scope = new ActionScope(saved.My >= 0, At(saved.My), saved.There >= 0, At(saved.There), At(saved.Target));
                var wait = new WdlWaitRequest(saved.Remaining, saved.FrameCycles ? WdlWaitMode.FrameCycles : WdlWaitMode.Ticks);
                _waitingActions.Add(new ActiveAction(state, wait, scope));
            }
        }
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

        if (!IsSoundAudible(volume, emitterName))
        {
            return;
        }

        int channel = ReserveSoundChannel(loop: false);
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
        if (!IsSoundAudible(volume, emitterName: null))
        {
            return;
        }

        int channel = ReserveSoundChannel(loop: false);
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
    /// <remarks>
    /// Stops the normal sounds and leaves <c>SLOOP</c> loops playing. The stopped channels still read
    /// busy for the rest of this cycle and the next one and free from the cycle after
    /// (OV-20261029-audio).
    /// </remarks>
    public void StopSounds()
    {
        StopSoundCount++;
        for (int channel = 0; channel < 8; channel++)
        {
            if (GetSkill($"CHANNEL_{channel}") != 1)
            {
                continue;
            }

            if (_channelRelease[channel] == 0)
            {
                _channelRelease[channel] = 2;
            }

            _audioRequests.Add(new WdlAudioRequest(WdlSoundOperation.StopChannel, default, channel));
        }
    }

    /// <summary>Requests playback for a floor or ceiling texture sound.</summary>
    public int PlayTextureSound(string soundName, double volume, bool loop, double? balance = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(soundName);
        int channel = ReserveSoundChannel(loop);
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
        _channelRelease[channel] = 0;
        _audioRequests.Add(new WdlAudioRequest(WdlSoundOperation.StopChannel, default, channel));
        return true;
    }

    /// <summary>
    /// Notes that host playback on <paramref name="channel"/> has ended. The channel reads free from
    /// the next world cycle on, not during a flic (OV-20261029-audio).
    /// </summary>
    public bool CompleteSound(int channel)
    {
        if (channel is < 0 or >= 8 || GetSkill($"CHANNEL_{channel}") == 0 || _channelRelease[channel] != 0)
        {
            return false;
        }

        _channelRelease[channel] = 1;
        return true;
    }

    /// <summary>
    /// Whether the host's sound on a channel has played out, asked at the start of every world cycle
    /// once the cycle's <c>TICKS</c> are set. <c>null</c> leaves it to <see cref="CompleteSound"/>.
    /// </summary>
    public Func<int, bool>? SoundPlayedOut { get; set; }

    /// <summary>
    /// Frees the channels whose sounds ended or were stopped, at the start of a world cycle. A sound
    /// that played out before this cycle's <c>TICKS</c> frees its channel in this cycle: a
    /// quarter-second sound started at 13.438 read free at 17.625, the first cycle after 17.438
    /// (OV-20261029-audio).
    /// </summary>
    private void ReleaseFinishedChannels()
    {
        for (int channel = 0; channel < 8; channel++)
        {
            if (_channelRelease[channel] == 0 && SoundPlayedOut is { } playedOut &&
                GetSkill($"CHANNEL_{channel}") == 1 && playedOut(channel))
            {
                _channelRelease[channel] = 1;
            }

            if (_channelRelease[channel] > 0 && --_channelRelease[channel] == 0)
            {
                SetSkill($"CHANNEL_{channel}", 0);
            }
        }
    }

    /// <summary>
    /// Whether a sound is played at all. At volume 0, at <c>SOUND_VOL</c> 0, or from an object
    /// beyond its texture's <c>SDIST</c>, the original skips it: no channel, and <c>CHANNEL</c>
    /// keeps its value (OV-20261029-audio).
    /// </summary>
    private bool IsSoundAudible(double volume, string? emitterName)
    {
        if (volume <= 0 || GetSkill("SOUND_VOL") <= 0)
        {
            return false;
        }

        if (emitterName is null || GetObject(emitterName) is not { } emitter)
        {
            return true;
        }

        (double x, double y, string? texture) = emitter switch
        {
            MapEntity entity => (entity.X, entity.Y, entity.Texture),
            Wall wall when wall.Vertex1 >= 0 && wall.Vertex1 < _level.Vertices.Count &&
                           wall.Vertex2 >= 0 && wall.Vertex2 < _level.Vertices.Count =>
                ((_level.Vertices[wall.Vertex1].X + _level.Vertices[wall.Vertex2].X) * 0.5,
                 (_level.Vertices[wall.Vertex1].Y + _level.Vertices[wall.Vertex2].Y) * 0.5,
                 wall.Texture),
            _ => (double.NaN, double.NaN, null),
        };
        if (double.IsNaN(x))
        {
            return true;
        }

        double audible = _level.FindTexture(texture) is { } entry ? entry.Texture.SoundDistance : 100;
        double dx = x - GetSkill("PLAYER_X");
        double dy = y - GetSkill("PLAYER_Y");
        return Math.Sqrt((dx * dx) + (dy * dy)) < audible;
    }

    /// <summary>
    /// Takes the lowest free channel. With none free a normal sound takes the channel of the oldest
    /// normal sound; <c>SLOOP</c> channels (2) are never taken, and when all are loops
    /// <c>CHANNEL</c> becomes −1 (manual p.109, OV-20261029-audio). A loop needs a free channel.
    /// </summary>
    private int ReserveSoundChannel(bool loop)
    {
        int channel = -1;
        for (int candidate = 0; candidate < 8 && channel < 0; candidate++)
        {
            if (GetSkill($"CHANNEL_{candidate}") == 0)
            {
                channel = candidate;
            }
        }

        if (channel < 0 && !loop)
        {
            for (int candidate = 0; candidate < 8; candidate++)
            {
                if (GetSkill($"CHANNEL_{candidate}") == 1 &&
                    (channel < 0 || _channelStarted[candidate] < _channelStarted[channel]))
                {
                    channel = candidate;
                }
            }
        }

        if (channel < 0)
        {
            SetSkill("CHANNEL", -1);
            return -1;
        }

        SetSkill($"CHANNEL_{channel}", loop ? 2 : 1);
        SetSkill("CHANNEL", channel);
        _channelStarted[channel] = ++_soundSequence;
        _channelRelease[channel] = 0;
        return channel;
    }

    /// <inheritdoc/>
    public void FadePalette(string paletteName, string? fileName, double factor)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = _palettes.GetValueOrDefault(paletteName);
        }

        _paletteFadeRequests.Add(new WdlPaletteFadeRequest(paletteName, fileName, factor));
        int number = _palettes.Keys.ToList().FindIndex(n => n.Equals(paletteName, StringComparison.OrdinalIgnoreCase));
        if (number >= 0)
        {
            _savedPalette = number + 1;
        }
    }

    /// <inheritdoc/>
    public bool Locate(AcknexObject? target)
    {
        AcknexObject? player = GetObject("player");
        target ??= player;
        if (target is null || !TryGetMapPosition(target, out double x, out double y))
        {
            return false;
        }

        if (ReferenceEquals(target, player))
        {
            // A script beams the player with SET PLAYER_X/PLAYER_Y and then LOCATEs; the host moves the
            // player object only after the cycle, so the skills hold the position to locate.
            (x, y) = (GetSkill("PLAYER_X"), GetSkill("PLAYER_Y"));
        }

        if (LevelTopology.LocateRegion(_level, x, y) is not { } region)
        {
            return false;
        }

        if (ReferenceEquals(target, player))
        {
            // In a BELOW stack, the storey that holds the eye (OV-20261030-below-beam).
            region = LevelTopology.StoreyHoldingEye(_level, region, GetSkill("PLAYER_Z"));
        }

        target["region"] = region;
        if (ReferenceEquals(target, GetObject("player")))
        {
            // The host copies PLAYER_REGION onto the player when the position changes; keep it current
            // so a beam followed by LOCATE lands in the located region (OV-20261029-slope).
            SetSkill("PLAYER_REGION", region);
            if (RegionAt(region) is { } here)
            {
                SetObjectReference("here", here);
            }
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
            (sector <= 0 || Math.Abs(delta) <= (sector / 2) + (1 / WdlFixed.Scale)) &&
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
    /// <remarks>
    /// <c>SAVE_INFO</c> writes the original's file at once when <see cref="SaveDirectory"/> is set
    /// (OV-20261029-save-format). A full <c>SAVE</c> takes the state at once, without the action running
    /// it, and leaves the writing to the host.
    /// </remarks>
    public void SaveGame(string baseName, int number, bool infoOnly)
    {
        var request = new WdlPersistenceRequest(
            WdlPersistenceOperation.Save,
            NormalizePrintBaseName(baseName),
            Math.Clamp(number, 0, 999),
            infoOnly)
        {
            Snapshot = infoOnly ? null : CaptureSnapshot(),
        };
        _persistenceRequests.Add(request);
        if (!infoOnly && OriginalSaveNames is { } names && SaveFilePath(request) is { } fullPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, AcknexSaveFile.Pack(AcknexFullSave.Write(CaptureOriginalSave(names.Wdl, names.Wmp, names.Wrs))));
        }
        if (infoOnly && SaveFilePath(request) is { } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, AcknexSaveFile.Pack(AcknexSaveFile.WriteInfo(CaptureInfoSkills())));
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>LOAD_INFO</c> restores the <c>GLOBAL</c> and <c>LOCAL</c> skills from the original's file at
    /// once, and the action goes on (OV-20261029-save-format); a full <c>LOAD</c> is left to the host.
    /// </remarks>
    public void LoadGame(string baseName, int number, bool infoOnly)
    {
        var request = new WdlPersistenceRequest(
            WdlPersistenceOperation.Load,
            NormalizePrintBaseName(baseName),
            Math.Clamp(number, 0, 999),
            infoOnly);
        _persistenceRequests.Add(request);
        if (infoOnly && SaveFilePath(request) is { } path && File.Exists(path))
        {
            RestoreSavedSkills(
                AcknexSaveFile.ReadSkills(AcknexSaveFile.Unpack(File.ReadAllBytes(path))),
                name => _level.GlobalSkills.Contains(name) || _level.LocalSkills.Contains(name));
        }
    }

    /// <summary>
    /// The directory the original's save files go to (the game directory, p.94); <c>null</c> keeps
    /// <c>SAVE_INFO</c>/<c>LOAD_INFO</c> from touching the disk.
    /// </summary>
    public string? SaveDirectory { get; set; }

    /// <summary>The original's file for a save slot: the base name in upper case, the number unpadded, <c>.SAV</c>.</summary>
    public string? SaveFilePath(WdlPersistenceRequest request) =>
        SaveDirectory is null
            ? null
            : Path.Combine(SaveDirectory, string.Create(CultureInfo.InvariantCulture, $"{request.BaseName.ToUpperInvariant()}{request.Number}.SAV"));

    /// <summary>
    /// The skills <c>SAVE_INFO</c> keeps: the <c>GLOBAL</c> and <c>LOCAL</c> ones, in reverse
    /// declaration order as the original lists them, with their raw fixed-point values and bounds.
    /// </summary>
    public IReadOnlyList<SaveSkill> CaptureInfoSkills()
    {
        var skills = new List<SaveSkill>();
        for (int i = _level.SkillDeclarationOrder.Count - 1; i >= 0; i--)
        {
            string name = _level.SkillDeclarationOrder[i];
            if (!_level.GlobalSkills.Contains(name) && !_level.LocalSkills.Contains(name))
            {
                continue;
            }

            SkillRange? range = _level.SkillBounds.TryGetValue(name, out SkillRange bounds) ? bounds : null;
            skills.Add(new SaveSkill(
                name,
                WdlFixed.ToRaw(GetSkill(name)),
                range is { } r && !double.IsInfinity(r.Min) ? WdlFixed.ToRaw(r.Min) : SaveSkill.NoMin,
                range is { } q && !double.IsInfinity(q.Max) ? WdlFixed.ToRaw(q.Max) : SaveSkill.NoMax));
        }

        return skills;
    }

    /// <summary>Sets the saved skills that <paramref name="accept"/> takes and this runtime knows.</summary>
    /// <returns>How many were set.</returns>
    public int RestoreSavedSkills(IEnumerable<SaveSkill> skills, Func<string, bool> accept)
    {
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(accept);
        int restored = 0;
        foreach (SaveSkill skill in skills)
        {
            if (accept(skill.Name) && _skills.ContainsKey(skill.Name))
            {
                SetSkill(skill.Name, skill.RawValue / WdlFixed.Scale);
                restored++;
            }
        }

        return restored;
    }

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
    /// <c>IF_START</c> counts as a cycle of its own with <c>TIME_CORR</c> 1 for the waits it arms:
    /// a <c>WAITT 16</c> has 15 left when the first cycle starts, and a <c>WAIT 3</c> resumes at the
    /// start of cycle 3 (OV-20261029-scheduler-order).
    /// </remarks>
    public bool RunStartup()
    {
        bool ran = _startupAction is not null && Run(_startupAction);
        CountDownWaitingActions(GetSkill(Timing.TimeCorrectionSkill));
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
    /// The player's region events and the proximity events come after the global slots and before
    /// the object pass (OV-20261019-actor-contact round 8: a walker's <c>IF_ENTER</c> sees the count
    /// the global slot has just raised).
    /// Walls' <c>DISTANCE</c> is refreshed before the resumed actions (see <see cref="RefreshWallDistances"/>);
    /// things' and actors' only when the view changed or an actor in front of the player moved (see
    /// <see cref="RefreshEntityDistances"/>).
    /// </summary>
    /// <returns>The frame's time-correction factor.</returns>
    public double Tick(double deltaSeconds)
    {
        double timeCorrection = UpdateTimingSkills(deltaSeconds, out double elapsedSeconds);
        ReleaseFinishedChannels();
        RefreshView();
        RefreshWallDistances();
        UpdateHere();
        ResumeWaitingActions();
        ResetGlobalSchedulerDiagnostics();
        if (AdvanceSecond(elapsedSeconds))
        {
            DispatchGlobalEachSecActions();
        }

        DispatchGlobalEachTickActions();
        DispatchPlayerRegionEvents();
        DispatchProximityEvents();
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
        _skills["TOTAL_TIME"] = _skills[Timing.TicksSkill]; // reads as TICKS (scheduler-order round 4)
        _skills["TOTAL_FRAMES"] = GetSkill("TOTAL_FRAMES") + 1;
        _skills["HERE_INDEX"] = GetSkill("PLAYER_REGION"); // the player's region, as the saves hold it (round 16)
        // "RENDER_MODE is returned to 0.5 automatically after each frame cycle" (manual p.104): a probe that
        // set 2 saved 0.5. 0, which suppresses rendering, stays (save-body round 18).
        if (GetSkill("RENDER_MODE") > 0.5)
        {
            _skills["RENDER_MODE"] = 0.5;
        }
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

        foreach (AcknexObject obj in level.Walls.Concat(level.UnplacedObjects.OfType<Wall>()))
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
        // Every save V3.963 wrote holds SHOOT_SECTOR 0 where the manual says 2π (p.91), so 0 is the
        // full circle (save-body round 18).
        EnsureSkillDefault("SHOOT_SECTOR", 0);
        EnsureSkillDefault("SHOOT_X", 0);
        EnsureSkillDefault("SHOOT_Y", 0);
        // A fresh V3.963 level starts KEY_SENSE at 1 and MOUSE_CALM at 1 (the manual says 0.7 and 3),
        // PLAYER_SIZE at 3.5, PLAYER_CLIMB at 2 (manual 1.5), WAVE_PERIOD at 20 (manual 16) and MUSIC_VOL
        // at 0.5 (OV-20261029-scheduler-order round 4, `defaults`).
        EnsureSkillDefault("KEY_SENSE", 1);
        EnsureSkillDefault("SHIFT_SENSE", 2);
        EnsureSkillDefault("MOUSE_SENSE", 1);
        EnsureSkillDefault("JOY_SENSE", 1);
        EnsureSkillDefault("MOUSE_MODE", 0);
        EnsureSkillDefault("TOUCH_MODE", 1);
        EnsureSkillDefault("MOUSE_MOVING", 0);
        EnsureSkillDefault("MOUSE_CALM", 1);
        EnsureSkillDefault("MOUSE_TIME", 4);
        EnsureSkillDefault("MICKEY_X", 0);
        EnsureSkillDefault("MICKEY_Y", 0);
        EnsureSkillDefault("MOUSE_X", 0);
        EnsureSkillDefault("MOUSE_Y", 0);
        EnsureSkillDefault("MOUSE_ANGLE", 0);
        EnsureSkillDefault("PLAYER_WIDTH", 1.2);
        EnsureSkillDefault("THING_DIST", 1);
        EnsureSkillDefault("ACTOR_DIST", 1);
        EnsureSkillDefault("PLAYER_SIZE", 3.5);
        EnsureSkillDefault("PLAYER_CLIMB", 2);
        EnsureSkillDefault("PLAYER_TILT", 0);
        EnsureValidSkillDefault("PLAYER_ARC", 1, value => value is >= 0.2 and <= 2);
        EnsureValidSkillDefault("ASPECT", 1, value => value is >= 0.1 and <= 10);
        EnsureSkillDefault("PLAYER_LIGHT", 1);
        EnsureSkillDefault("LIGHT_DIST", 10);
        // The manual (p.112) says DARK_DIST is recalculated from PLAYER_LIGHT; patched V3.963 read
        // 1000 at every PLAYER_LIGHT and LIGHT_DIST measured (OV-20261029-shading).
        EnsureSkillDefault("DARK_DIST", 1000);
        EnsureSkillDefault("SOUND_VOL", 1);
        EnsureSkillDefault("MUSIC_VOL", 0.5);
        EnsureSkillDefault("WALK_PERIOD", 4);
        EnsureSkillDefault("WALK_TIME", 4);
        EnsureSkillDefault("WAVE_PERIOD", 20);
        EnsureSkillDefault("WALK", 0);
        EnsureSkillDefault("WAVE", 0);
        EnsureSkillDefault("PSOUND_VOL", 1);
        EnsureSkillDefault("PSOUND_TONE", 1);
        EnsureSkillDefault("CHANNEL", 0);
        for (int channel = 0; channel < 8; channel++)
        {
            EnsureSkillDefault($"CHANNEL_{channel}", 0);
        }

        // Engine settings and constants OpenVirtue does not otherwise model, as a fresh V3.963 level's
        // save holds them, so a save written for the original keeps its renderer's settings: a 0 for
        // BUFFER_MODE changed the original's frame pacing after a load (OV-20261029-save-body round 13).
        EnsureSkillDefault("PI", 3216 / 1024.0);
        EnsureSkillDefault("TWO_PI", 6433 / 1024.0);
        EnsureSkillDefault("HALF_PI", 1608 / 1024.0);
        EnsureSkillDefault("BUFFER_MODE", 1);
        EnsureSkillDefault("FLOOR_MODE", 1);
        EnsureSkillDefault("MOVE_MODE", 1);
        EnsureSkillDefault("CLIPPING", 0.5);
        EnsureSkillDefault("EYE_DIST", 0.5);
        EnsureSkillDefault("GFX_DX", 1000);
        EnsureSkillDefault("GFX_DY", 1000);
        EnsureSkillDefault("CDAUDIO_VOL", 614 / 1024.0);
        EnsureSkillDefault("NODE", -1);
        EnsureSkillDefault("TOUCH_RANGE", 100);
        EnsureSkillDefault("PLAYER_RANGE", 3);
        EnsureSkillDefault("MAP_SCALE", 921 / 1024.0);
        EnsureSkillDefault("COLOR_BORDER", 17);
        EnsureSkillDefault("COLOR_WALLS", 244);
        EnsureSkillDefault("COLOR_THINGS", 13);
        EnsureSkillDefault("COLOR_ACTORS", 3);
        EnsureSkillDefault("COLOR_PLAYER", 7);
        EnsureSkillDefault("WDIST_MINX", int.MinValue / 1024.0);
        EnsureSkillDefault("WDIST_MINY", int.MinValue / 1024.0);
        EnsureSkillDefault("WDIST_MAXX", int.MaxValue / 1024.0);
        EnsureSkillDefault("WDIST_MAXY", int.MaxValue / 1024.0);

        // OV-20261019-border-kinds: the runtime starts ACTOR_CLIMB at 1.1 (the manual says 1, p.113).
        EnsureSkillDefault("ACTOR_CLIMB", 1.1);
        EnsureSkillDefault("SKIP_FRAMES", 5);
        EnsureSkillDefault("ACTOR_WIDTH", 1);
        EnsureSkillDefault("THING_WIDTH", 1);
        EnsureSkillDefault("SCREEN_X", 0);
        EnsureSkillDefault("SCREEN_Y", 0);
        EnsureSkillDefault("SCREEN_WIDTH", _level.VideoMode?.Width ?? 640);
        EnsureSkillDefault("SCREEN_HGT", _level.VideoMode?.Height ?? 480);

        // The frame's size and the map's extent, which the retail saves hold (S640x480: 640 by 480; the
        // bounds of the vertices and the placed things and actors, cut to 1/1024: apathy's MAP_MAXX is
        // an actor's X; save-body round 16).
        EnsureSkillDefault("RENDER_MODE", 0.5);
        EnsureSkillDefault("MAP_EDGE_X2", _level.VideoMode?.Width ?? 640);
        EnsureSkillDefault("MAP_EDGE_Y2", _level.VideoMode?.Height ?? 480);
        EnsureSkillDefault("_SVOL_OLD", 0.5);
        EnsureSkillDefault("_MVOL_OLD", 0.5);
        EnsureSkillDefault("FRAME_WIDTH", _level.VideoMode?.Width ?? 640);
        EnsureSkillDefault("FRAME_HGT", _level.VideoMode?.Height ?? 480);
        List<(double X, double Y)> points =
        [
            .. _level.Vertices.Select(static v => ((double)v.X, (double)v.Y)),
            .. _level.MapObjects.OfType<MapEntity>().Select(static e => (e.X, e.Y)),
        ];
        if (points.Count > 0)
        {
            static double Cut(double value) => Math.Truncate(Math.Round(value, 3) * 1024) / 1024;
            EnsureSkillDefault("MAP_MINX", Cut(points.Min(static p => p.X)));
            EnsureSkillDefault("MAP_MAXX", Cut(points.Max(static p => p.X)));
            EnsureSkillDefault("MAP_MINY", Cut(points.Min(static p => p.Y)));
            EnsureSkillDefault("MAP_MAXY", Cut(points.Max(static p => p.Y)));
        }
    }

    // An engine default is a 22.10 value like every skill, cut toward zero: PLAYER_WIDTH's 1.2 prints
    // 1.199 in the original (OV-20261019-eye-clamp GO lines).
    private void EnsureSkillDefault(string name, double value)
    {
        if (!_skills.ContainsKey(name) || !_level.ExplicitlyInitializedSkills.Contains(name))
        {
            _skills[name] = Math.Truncate(Math.Round(value * 1024, 6)) / 1024;
        }
    }

    private void EnsureValidSkillDefault(string name, double value, Func<double, bool> isValid)
    {
        if (!_skills.TryGetValue(name, out double current) ||
            !_level.ExplicitlyInitializedSkills.Contains(name) ||
            !double.IsFinite(current) ||
            !isValid(current))
        {
            _skills[name] = Math.Truncate(Math.Round(value * 1024, 6)) / 1024;
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

            (SaveAction Record, ActiveAction Action)? previousRecord = _resumedRecord;
            _resumedRecord = OriginalSaveNames is null ? null : ResumedRecord(action);
            try
            {
                WdlExecutionResult result = ContinueWithScope(action);
                StoreSuspendedAction(action.State, result, action.Scope);
            }
            catch (WdlExpressionException ex)
            {
                AbortAction("(resumed action)", ex);
            }
            finally
            {
                _resumedRecord = previousRecord;
            }
        }
    }

    // The waiting action being resumed, as a full save records the action running the SAVE: the wait it
    // came out of and the time it had left, under one step (round 17). Taken only when original saves
    // are written.
    private (SaveAction Record, ActiveAction Action)? _resumedRecord;

    private (SaveAction Record, ActiveAction Action)? ResumedRecord(ActiveAction action)
    {
        if (action.State.Frames.Count == 0)
        {
            return null;
        }

        WdlExecutionFrame bottom = action.State.Frames.Last();
        string? name = _actions.FirstOrDefault(pair => ReferenceEquals(pair.Value.Items, bottom.Items)).Key;
        if (name is null || !WdlFlatPosition.TryPositionOf(_actions[name], action.State, out int position, out int next))
        {
            return null;
        }

        int number = _level.ActionOrder.ToList().FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)) is >= 0 and int i ? i + FirstActionNumber : -1;
        int remaining = (int)Math.Truncate(Math.Round(Math.Max(0, action.Remaining) * 1024, 6));
        return (new SaveAction(number, remaining, position, next, -1, -1, Running: true), action);
    }

    /// <summary>
    /// Counts every waiting action down by the frame that is ending: one frame cycle for
    /// <c>WAIT</c>, the frame's <c>TIME_CORR</c> for <c>WAITT</c>. It runs last in the frame, so an
    /// action armed during the frame (or by a hook or a host call before it) counts that frame too
    /// (OV-20261015-flic-scheduler-detail). Actions of inactive entities do not count down.
    /// </summary>
    private void CountDownWaitingActions(double timeCorrection)
    {
        foreach (ActiveAction action in _waitingActions)
        {
            if (!IsInactiveEntity(action.Scope.Target))
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

    /// <summary>
    /// Starts every thing's and actor's script <c>Z</c> as the original's saves hold it: 0 on the
    /// <c>GROUND</c>, a <c>CANDELABER</c>'s ceiling less its sprite's height, otherwise the floor under
    /// it, and 0 for an unplaced object (OV-20261029-save-body round 13; below-view reads a thing's
    /// <c>Z</c> 10 on a floor 10 high).
    /// </summary>
    private void InitializeFootHeights()
    {
        var unplaced = new HashSet<AcknexObject>(_level.UnplacedObjects, ReferenceEqualityComparer.Instance);
        foreach (MapEntity entity in _level.Things.Cast<MapEntity>().Concat(_level.Actors))
        {
            if (!unplaced.Contains(entity))
            {
                UpdateFootHeight(entity);
            }
        }
    }

    private void UpdateFootHeight(MapEntity entity)
    {
        if (entity.Region < 0 || entity.Region >= _level.Regions.Count || entity["GROUND"] > 0)
        {
            return;
        }

        if (entity["CANDELABER"] > 0)
        {
            int sprite = entity.Texture is { } name && _level.Textures.TryGetValue(TextureAnimation.LogicalTextureName(name), out LevelTexture texture) &&
                texture.Height > 0 && texture.ScaleY > 0
                ? (int)Math.Truncate(((texture.Height * 1024.0) - 1) / texture.ScaleY)
                : 16383;
            entity.FootZ = ((long)Math.Round(LevelTopology.CeilingHeightAt(_level, entity.Region, entity.X, entity.Y) * 1024) - sprite) / 1024.0;
            return;
        }

        entity.FootZ = LevelTopology.FloorHeightAt(_level, entity.Region, entity.X, entity.Y);
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
        SetSkill("PLAYER_X", playerStart.X);
        SetSkill("PLAYER_Y", playerStart.Y);
        SetSkill("PLAYER_REGION", playerStart.Region);
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
    /// <summary>
    /// Points <c>HERE</c> at the player's region as the last movement left it, before the cycle's
    /// actions run: the global slot already reads the new region's skills in the cycle whose
    /// <c>IF_ENTER</c> runs after it (OV-20261019-clip-contact border traces, round 8 of actor-contact).
    /// </summary>
    private void UpdateHere()
    {
        if (GetObject("player") is Player player && RegionAt(player.Region) is { } currentRegion)
        {
            SetObjectReference("here", currentRegion);
        }
    }

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

    /// <summary>
    /// Fires <c>IF_NEAR</c> for what the player's movement just pushed against or passed through,
    /// at once, before any distance is refreshed: a player beamed into a solid thing is pushed out in
    /// that cycle's movement and its <c>IF_NEAR</c> runs in the same cycle, reading the thing's
    /// <c>DISTANCE</c> from before the beam (34 for a thing 24, −10 from the origin;
    /// OV-20261019-actor-contact round 8). A target already within its <c>DIST</c> does not fire again.
    /// The host calls it right after the player's movement.
    /// </summary>
    public void DispatchContactEvents()
    {
        if (GetObject("player") is not Player player)
        {
            return;
        }

        foreach (AcknexObject target in player.TakeTouchedEntities())
        {
            if (ShouldTrackProximity(target) &&
                (target is not MapEntity || IsWithinClipDistance(target)) &&
                !(_proximityStates.TryGetValue(target, out bool near) && near))
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
        // OV-20261029-actor-step round 5: a way actor standing (SPEED 0) on a point does not arrive
        // there; given a speed, it spends its first cycle arriving and steps from the next one.
        if (actor["SPEED"] <= 0)
        {
            return false;
        }

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
            SetSkill("ACTOR_FLOOR_HGT", LevelTopology.FloorHeightAt(_level, actor.Region, actor.X, actor.Y));
            SetSkill("ACTOR_CEIL_HGT", LevelTopology.CeilingHeightAt(_level, actor.Region, actor.X, actor.Y));
        }
    }

    private bool AdvanceActorForward(Actor actor, double timeCorrection)
    {
        // OV-20261029-actor-step: a step along the actor's angle is SPEED·TIME_CORR in 22.10 fixed point,
        // split with the cosine and sine of the angle (itself a 22.10 value), each cut toward zero to
        // 22.10 and each part rounded to a whole 1/1024 step. Unlike a step toward a point, it is not
        // measured in |dx| + |dy|.
        double distance = EngineProduct(Math.Max(0, actor["SPEED"]), timeCorrection);
        if (distance <= 0)
        {
            return false;
        }

        double angle = WdlFixed.Quantize(actor.FacingAngleRadians);
        double cos = Math.Truncate(Math.Cos(angle) * 1024) / 1024;
        double sin = Math.Truncate(Math.Sin(angle) * 1024) / 1024;
        double stepX = Math.Round(distance * 1024 * cos, MidpointRounding.AwayFromZero) / 1024;
        double stepY = Math.Round(distance * 1024 * sin, MidpointRounding.AwayFromZero) / 1024;
        double previousX = actor.X;
        double previousY = actor.Y;
        MoveActor(actor, actor.X + stepX, actor.Y + stepY);
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

    /// <summary>
    /// A product the engine itself takes, as an actor's step <c>SPEED·TIME_CORR</c>: rounded to 1/1024,
    /// where a WDL <c>*</c> cuts toward zero (OV-20261029-actor-step round 6).
    /// </summary>
    private static double EngineProduct(double left, double right) =>
        Math.Round(WdlFixed.ToRaw(left) * (double)WdlFixed.ToRaw(right) / 1024, MidpointRounding.AwayFromZero) / 1024;

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

        // The engine's own product rounds to 1/1024 (a WDL '*' cuts toward zero): SPEED 0.05 (51 raw)
        // over six skipped cycles of 0.25 makes 76.5 units, and the original stepped 77 (round 6).
        double distance = EngineProduct(Math.Max(0, actor["SPEED"]), timeCorrection);
        if (distance <= 0)
        {
            return false;
        }

        // The facing is taken once per point, from where the actor sets off: a 22.10 angle in 0..2π
        // cut toward zero, 1608 raw for a way along +Y however far a CAREFULLY actor then drifts off
        // it, 367 for 40:15 all the way (OV-20261029-actor-step round 5).
        if (!_legFacings.TryGetValue(actor, out (double X, double Y, double Facing) leg) || leg.X != targetX || leg.Y != targetY)
        {
            double facing = Math.Atan2(dy, dx);
            leg = (targetX, targetY, Math.Truncate((facing < 0 ? facing + (2 * Math.PI) : facing) * 1024) / 1024);
            _legFacings[actor] = leg;
        }

        TurnActorToward(actor, leg.Facing, timeCorrection);
        if (distance >= remaining)
        {
            MoveActor(actor, targetX, targetY);
            return true;
        }

        // OV-20261029-actor-step round 5: each part of the step is cut toward zero to 1/1024 (40:15
        // split a 128-unit step 93 and 34), so a CAREFULLY actor 4 units off its line gets no X
        // part, its nudge, and a Y part one short. Round 6: the part is the 22.10 product of the step
        // and the distance left along the axis, rounded to 1/1024, then divided by |dx| + |dy| and cut
        // toward zero, which explains the one-unit short steps (143 for 144 once 39760 units are
        // left): every step of the plain +Y and 40:15 ways fits.
        double rawX = Math.Round(dx * 1024);
        double rawY = Math.Round(dy * 1024);
        double rawRemaining = Math.Abs(rawX) + Math.Abs(rawY);
        double rawStep = Math.Round(distance * 1024);
        static double Part(double step, double along, double remaining) =>
            Math.Truncate(Math.Round(step * along / 1024, MidpointRounding.AwayFromZero) * 1024 / remaining) / 1024;
        double stepX = rawRemaining > 0 ? Part(rawStep, rawX, rawRemaining) : 0;
        double stepY = rawRemaining > 0 ? Part(rawStep, rawY, rawRemaining) : 0;
        MoveActor(actor, actor.X + stepX, actor.Y + stepY);
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
                    wallClearance: Player.WallClearance,
                    clearanceOnContact: true);
            actor.X = result.Position.X;
            actor.Y = result.Position.Y;
            UpdateFootHeight(actor);
            if (result.Collided)
            {
                // OV-20261029-actor-step: where a wall stops the step, the original's position is
                // the contact point cut to 1/1024 toward zero.
                actor.X = Math.Truncate(actor.X * 1024) / 1024;
                actor.Y = Math.Truncate(actor.Y * 1024) / 1024;
            }

            // OV-20261029-actor-step: a CAREFULLY step's part under 4/1024 along one axis becomes
            // 4/1024, keeping its sign and positive for none, even when a wall stopped the step:
            // axis-aligned walkers creep 4 units a step, and one drifting back toward its way's line
            // (a part of −1) steps −4 (round 5).
            const double Nudge = 4.0 / 1024;
            if (Math.Abs(requestedX) < Nudge - 1e-9 && Math.Abs(requestedY) >= Nudge - 1e-9)
            {
                actor.X += (requestedX < 0 ? -Nudge : Nudge) - requestedX;
            }
            else if (Math.Abs(requestedY) < Nudge - 1e-9 && Math.Abs(requestedX) >= Nudge - 1e-9)
            {
                actor.Y += (requestedY < 0 ? -Nudge : Nudge) - requestedY;
            }

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
                blocked.PushAway(
                    actor,
                    new Vector2((float)actor.X, (float)actor.Y),
                    LevelCollisionWorld.ContactDistance(ActorCollisionRadius(actor) + blocked.Width)))
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
            StartAnimationLoop(entry.Texture, state, timeCorrection);
            _textureAnimationStates[target] = state;
            return; // the first phase counts from the next cycle
        }

        AdvanceAnimationTarget(target, entry.Texture, state, timeCorrection);
    }

    /// <summary>
    /// Steps a texture animation by one frame. The original fixes each loop's phase lengths in frames
    /// when the loop starts: <c>floor(DELAY · (1 + RANDOM·u) / TIME_CORR)</c>, with the
    /// <c>TIME_CORR</c> of the cycle that starts the loop and one random draw u for the whole loop
    /// (OV-20261029-anim-timing). A phase then lasts that many frames whatever the frame rate does,
    /// and the first loop, started in the first cycle, counts with <c>TIME_CORR</c> 1.
    /// </summary>
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
                return;
            }

            if (!state.WasPlaying)
            {
                SetAnimationCycle(target, 1);
                QueueAnimationTextureSound(target, texture, 1);
                state.FramesLeft = AnimationFrames(texture, 1, state);
                state.WasPlaying = true;
                return;
            }
        }

        int cycle = NormalizeAnimationCycle(target["CYCLE"], cycles);
        if (target["CYCLE"] != cycle)
        {
            SetAnimationCycle(target, cycle);
        }

        if (--state.FramesLeft > 0)
        {
            return;
        }

        if (cycle < cycles)
        {
            cycle++;
            SetAnimationCycle(target, cycle);
            QueueAnimationTextureSound(target, texture, cycle);
            state.FramesLeft = AnimationFrames(texture, cycle, state);
            return;
        }

        TriggerEachCycle(target);
        StartAnimationLoop(texture, state, timeCorrection);
        if (texture.IsOneShot)
        {
            target["PLAY"] = 0;
            state.WasPlaying = false;
            return;
        }

        SetAnimationCycle(target, 1);
        QueueAnimationTextureSound(target, texture, 1);
        state.FramesLeft = AnimationFrames(texture, 1, state);
    }

    /// <summary>Fixes the frame step and the random factor of a new animation loop.</summary>
    private void StartAnimationLoop(LevelTexture texture, TextureAnimationState state, double timeCorrection)
    {
        state.LoopStep = timeCorrection > 0 ? timeCorrection : 1;
        state.LoopFactor = 1 + (texture.RandomDelayFactor * _random.NextDouble());
        state.FramesLeft = AnimationFrames(texture, 1, state);
    }

    private static int AnimationFrames(LevelTexture texture, int cycle, TextureAnimationState state) =>
        Math.Max(1, (int)Math.Floor((AnimationDelay(texture, cycle) * state.LoopFactor / state.LoopStep) + 1e-9));

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
        // OV-20261009-shoot-mapping (sweep): the shot goes through a whole screen pixel, the one
        // whose column is floor(W/2 + SHOOT_X·W/2) and whose row is floor(H/2 − SHOOT_Y·H/2).
        double halfWidth = projection.ScreenWidth * 0.5;
        double halfHeight = projection.ScreenHeight * 0.5;
        double column = Math.Floor(halfWidth + (WdlFixed.Quantize(Math.Clamp(shootX, -1, 1)) * halfWidth)) - halfWidth;
        double row = Math.Floor(halfHeight - (WdlFixed.Quantize(Math.Clamp(shootY, -1, 1)) * halfHeight)) - halfHeight;
        shootX = column / halfWidth;
        shootY = -row / halfHeight;
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
            player.Region,
            new ShotPixel(column, halfWidth * Math.Truncate(1024 / projection.HorizontalHalfTangent) / 1024));
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
        int startRegion,
        ShotPixel shot)
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

                        hitX = sprite.Mirror
                            ? pixelX
                            : SpriteHitColumn(shot, (dx * forwardY) - (dy * forwardX) + sprite.CenterOffset.X, depth, sprite.HalfWidth, image.Width);
                        // OV-20261009-shoot-mapping: the original's HIT_Y is one row below the sampled
                        // pixel row (129 on the eye line's row 128, 64 for a hit at row 63.9).
                        hitY = pixelY + 1;
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

        double openingFloor = Math.Max(
            LevelTopology.FloorHeightAt(_level, region, x, y),
            LevelTopology.FloorHeightAt(_level, nextRegion, x, y));
        double openingCeiling = Math.Min(
            LevelTopology.CeilingHeightAt(_level, region, x, y),
            LevelTopology.CeilingHeightAt(_level, nextRegion, x, y));
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
            ? test.IsSpriteVisible(viewer, entity.X, entity.Y, size.Width * 0.5, bottom, bottom + size.Height, entity.Region)
            : test.IsSpriteVisible(viewer, entity.X, entity.Y, 0, bottom, bottom + 1, entity.Region);
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

    /// <inheritdoc/>
    public bool TryGetSkillMargin(string skillName, string memberName, out double value)
    {
        bool min = memberName.Equals("MIN", StringComparison.OrdinalIgnoreCase);
        if (!(min || memberName.Equals("MAX", StringComparison.OrdinalIgnoreCase)) || !_skills.ContainsKey(skillName))
        {
            value = 0;
            return false;
        }

        // A skill without a margin reads the widest one its save record can hold.
        SkillRange range = _bounds.TryGetValue(skillName, out SkillRange found) ? found : new SkillRange(double.NegativeInfinity, double.PositiveInfinity);
        double margin = min ? range.Min : range.Max;
        value = double.IsFinite(margin) ? margin : (min ? SaveSkill.NoMin : SaveSkill.NoMax) / WdlFixed.Scale;
        return true;
    }

    /// <inheritdoc/>
    public bool TrySetSkillMargin(string skillName, string memberName, double value)
    {
        if (!TryGetSkillMargin(skillName, memberName, out _))
        {
            return false;
        }

        SkillRange range = _bounds.TryGetValue(skillName, out SkillRange found) ? found : new SkillRange(double.NegativeInfinity, double.PositiveInfinity);
        _bounds[skillName] = memberName.Equals("MIN", StringComparison.OrdinalIgnoreCase) ? range with { Min = value } : range with { Max = value };
        return true;
    }

    /// <summary>What a keyword synonym names, through any synonyms it names in turn; null for none.</summary>
    private string? KeywordTarget(string name)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? current = name;
        while (current is not null && _keywordReferences.TryGetValue(current, out string? target) && seen.Add(current))
        {
            current = target;
        }

        return string.IsNullOrWhiteSpace(current) || current.Equals("NULL", StringComparison.OrdinalIgnoreCase) || _keywordReferences.ContainsKey(current)
            ? null
            : current;
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

    /// <summary>
    /// Restores a full save's skills. <c>LOCAL</c> skills keep the values they have now: the original's
    /// full <c>SAVE</c> leaves them out, and after a <c>LOAD</c> one read what it was set to before
    /// (OV-20261029-save-body round 7).
    /// </summary>
    private void RestoreSkills(IReadOnlyDictionary<string, double> skills)
    {
        Dictionary<string, double> locals = _skills
            .Where(pair => _level.LocalSkills.Contains(pair.Key))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        _skills.Clear();
        foreach ((string name, double value) in skills)
        {
            if (!_level.LocalSkills.Contains(name))
            {
                SetSkill(name, value);
            }
        }

        foreach ((string name, double value) in locals)
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
            .Concat(_level.UnplacedObjects.OfType<Wall>())
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

    /// <summary>
    /// A shot's screen column, as an offset from the centre, and the focal length the original
    /// projects sprites with: half the width times 1/tan(<c>PLAYER_ARC</c>/2) cut to 22.10.
    /// </summary>
    private readonly record struct ShotPixel(double Column, double FocalLength);

    /// <summary>
    /// A sprite's <c>HIT_X</c> (OV-20261009-shoot-mapping, sweep): the texel column the renderer
    /// steps to at the shot's screen column. It counts from the sprite's projected left edge,
    /// 0.714 pixels further on. The pixels per step at the sprite's depth and the texels per pixel
    /// are both cut to 22.10 (1230/1024 texels for a 64-texel 4-step sprite 44 ahead). Fitted to
    /// three sprites and 19 columns in one 640-wide view, within 0.0013.
    /// </summary>
    private static double SpriteHitColumn(ShotPixel shot, double centerRight, double depth, double halfWidth, int textureWidth)
    {
        double pixelsPerStep = Math.Truncate(1024 * shot.FocalLength / depth) / 1024;
        double left = (centerRight - halfWidth) * pixelsPerStep;
        double texelsPerPixel = Math.Truncate(1024 * textureWidth / (2 * halfWidth * pixelsPerStep)) / 1024;
        double texel = texelsPerPixel * (shot.Column + 0.714 - left);
        return WdlFixed.Quantize(Math.Clamp(texel, 0, textureWidth - (1.0 / 1024)));
    }

    private sealed record HitCandidate(
        AcknexObject Target,
        double Distance,
        double TraceDistance,
        double HitX = 0,
        double HitY = 0);

    private sealed class TextureAnimationState
    {
        /// <summary>Frames left in the current phase.</summary>
        public int FramesLeft { get; set; }

        /// <summary>The <c>TIME_CORR</c> the current loop counts its phases with.</summary>
        public double LoopStep { get; set; } = 1;

        /// <summary>The current loop's <c>RANDOM</c> factor (1 without <c>RANDOM</c>).</summary>
        public double LoopFactor { get; set; } = 1;

        public bool WasPlaying { get; set; }
    }
}
