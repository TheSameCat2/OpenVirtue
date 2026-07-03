// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Text;
using OpenVirtue.Engine.Map;
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
    IWdlSchedulerContext,
    IWdlActionHookContext,
    IWdlObjectGroupContext,
    IWdlObjectReferenceContext,
    IWdlStringContext,
    IWdlPrintContext,
    IWdlKeywordReferenceContext
{
    private const int MaxCallDepth = 64;
    private const int EachTickSlotCount = 16;

    private readonly Dictionary<string, double> _skills;
    private readonly IReadOnlyDictionary<string, SkillRange> _bounds;
    private readonly Dictionary<string, WdlString> _strings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _keywordReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _keywordReferenceTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AcknexObject> _objects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<AcknexObject>> _objectsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ambiguousObjectNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Region> _regions = [];
    private readonly List<AcknexObject> _levelObjects = [];
    private readonly string?[] _eachTickActions = new string?[EachTickSlotCount];
    private readonly Dictionary<string, string?> _actionHooks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ObjectTickAction> _objectTickActions = [];
    private readonly IReadOnlyDictionary<string, WdlBlock> _actions;
    private readonly WdlInterpreter _interpreter;
    private readonly string? _startupAction;
    private readonly string? _printDirectory;
    private string _printBaseName = "PRINT";
    private int _printNumber;
    private int _callDepth;

    public WdlRuntime(Level level, string? printDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(level);
        _skills = new Dictionary<string, double>(level.Skills, StringComparer.OrdinalIgnoreCase);
        _bounds = level.SkillBounds;
        _printDirectory = printDirectory;
        foreach ((string name, WdlString value) in level.Strings)
        {
            _strings[name] = value;
        }

        _actions = level.Actions;
        _startupAction = level.StartupAction;
        RegisterLevelObjects(level);
        InitializePredefinedReferences(level);
        InitializeSynonyms(level);
        InitializeEachTickActions(level);
        InitializeObjectEachTickActions(level);
        InitializeActionHooks(level);
        _interpreter = new WdlInterpreter(this);
    }

    /// <summary>The live global skill values.</summary>
    public IReadOnlyDictionary<string, double> Skills => _skills;

    /// <summary>The named object references available to scripts.</summary>
    public IReadOnlyDictionary<string, AcknexObject> Objects => _objects;

    /// <summary>The live WDL string values.</summary>
    public IReadOnlyDictionary<string, WdlString> Strings => _strings;

    /// <summary>The live keyword-valued synonym references.</summary>
    public IReadOnlyDictionary<string, string?> KeywordReferences => _keywordReferences;

    /// <summary>All placed level objects registered for runtime iteration.</summary>
    public IReadOnlyList<AcknexObject> LevelObjects => _levelObjects;

    /// <summary>The live global <c>IF_*</c> action-hook assignments.</summary>
    public IReadOnlyDictionary<string, string?> ActionHooks => _actionHooks;

    /// <inheritdoc/>
    public double GetSkill(string name) => _skills.GetValueOrDefault(name);

    /// <inheritdoc/>
    public void SetSkill(string name, double value) =>
        _skills[name] = _bounds.TryGetValue(name, out SkillRange range) ? range.Clamp(value) : value;

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
    public void SetPrintFile(string baseName, int number)
    {
        _printBaseName = NormalizePrintBaseName(baseName);
        _printNumber = Math.Clamp(number, 0, 999);
    }

    /// <inheritdoc/>
    public void PrintValue(double value) =>
        AppendPrintText(value.ToString("F3", CultureInfo.InvariantCulture) + Environment.NewLine);

    /// <inheritdoc/>
    public void PrintString(string value) => AppendPrintText(value);

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
    public AcknexObject? GetObject(string name) =>
        _objects.GetValueOrDefault(name) ??
        (_objectsByName.TryGetValue(name, out List<AcknexObject>? matches) ? matches.FirstOrDefault() : null);

    /// <inheritdoc/>
    public IEnumerable<AcknexObject> GetObjectsByName(string name) =>
        _objectsByName.GetValueOrDefault(name) ?? [];

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
    public void RegisterEachTick(AcknexObject target, string actionName)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(actionName);
        _objectTickActions.Add(new ObjectTickAction(target, actionName));
    }

    /// <summary>
    /// Registers an object/action pair with the earlier provisional per-frame scheduler name.
    /// </summary>
    public void RegisterEachCycle(AcknexObject target, string actionName) => RegisterEachTick(target, actionName);

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
    public bool RunStartup() => _startupAction is not null && Run(_startupAction);

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
        return TryGetObjectActionHook(target, name, out string? actionName) &&
               actionName is not null &&
               RunForActionTarget(target, actionName);
    }

    /// <summary>
    /// Advances the runtime by one frame lasting <paramref name="deltaSeconds"/>, refreshing the
    /// global <c>TIME_CORR</c> skill that movement/animation scripts scale by (see <see cref="Timing"/>).
    /// Global and object-level <c>EACH_TICK</c> hooks are dispatched after the correction is set.
    /// </summary>
    /// <returns>The frame's time-correction factor.</returns>
    public double Tick(double deltaSeconds)
    {
        double timeCorrection = Timing.TimeCorrection(deltaSeconds);
        _skills[Timing.TimeCorrectionSkill] = timeCorrection;
        foreach (string? actionName in _eachTickActions)
        {
            if (actionName is not null)
            {
                Run(actionName);
            }
        }

        foreach (ObjectTickAction tickAction in _objectTickActions)
        {
            RunForActionTarget(tickAction.Target, tickAction.ActionName);
        }

        return timeCorrection;
    }

    /// <inheritdoc/>
    public void CallAction(string name) => Run(name);

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

        try
        {
            return Run(actionName);
        }
        finally
        {
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
            _interpreter.Execute(block);
        }
        finally
        {
            _callDepth--;
        }

        return true;
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
        if (level.PlayerStart is { } playerStart &&
            RegionAt(playerStart.Region) is { } here)
        {
            SetObjectReference("here", here);
        }
    }

    private void InitializeEachTickActions(Level level)
    {
        int count = Math.Min(_eachTickActions.Length, level.EachTickActions.Count);
        for (int i = 0; i < count; i++)
        {
            _eachTickActions[i] = string.IsNullOrWhiteSpace(level.EachTickActions[i]) ? null : level.EachTickActions[i];
        }
    }

    private void InitializeObjectEachTickActions(Level level)
    {
        foreach (Region region in level.Regions)
        {
            RegisterEachTickIfPresent(region, region.EachTickAction);
        }

        foreach (Wall wall in level.Walls)
        {
            RegisterEachTickIfPresent(wall, wall.EachTickAction);
        }

        foreach (MapEntity entity in level.Things.Cast<MapEntity>().Concat(level.Actors))
        {
            RegisterEachTickIfPresent(entity, entity.EachTickAction);
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

    private void RegisterEachTickIfPresent(AcknexObject target, string? actionName)
    {
        if (!string.IsNullOrWhiteSpace(actionName))
        {
            RegisterEachTick(target, actionName);
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

    private string PrintFilePath() =>
        Path.Combine(_printDirectory!, $"{_printBaseName}{_printNumber:000}.TXT");

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

    private readonly record struct ObjectTickAction(AcknexObject Target, string ActionName);
}
