// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Map;
using OpenVirtue.Formats.Wdl;

namespace OpenVirtue.Engine.Interpreter;

/// <summary>
/// The runtime environment for a loaded <see cref="Level"/>: the live skill table (seeded
/// from the level's declared skills), the named object registry, and the level's ACTION
/// scripts. Implements <see cref="IWdlContext"/> so the <see cref="WdlInterpreter"/> can
/// read and write state, and exposes <see cref="RunAction"/> to invoke a script by name.
/// </summary>
public sealed class WdlRuntime : IWdlContext, IWdlSchedulerContext, IWdlObjectGroupContext, IWdlObjectReferenceContext
{
    private const int MaxCallDepth = 64;
    private const int EachTickSlotCount = 16;

    private readonly Dictionary<string, double> _skills;
    private readonly IReadOnlyDictionary<string, SkillRange> _bounds;
    private readonly Dictionary<string, AcknexObject> _objects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<AcknexObject>> _objectsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ambiguousObjectNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AcknexObject> _levelObjects = [];
    private readonly string?[] _eachTickActions = new string?[EachTickSlotCount];
    private readonly List<CycleAction> _cycleActions = [];
    private readonly IReadOnlyDictionary<string, WdlBlock> _actions;
    private readonly WdlInterpreter _interpreter;
    private readonly string? _startupAction;
    private int _callDepth;

    public WdlRuntime(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _skills = new Dictionary<string, double>(level.Skills, StringComparer.OrdinalIgnoreCase);
        _bounds = level.SkillBounds;
        _actions = level.Actions;
        _startupAction = level.StartupAction;
        RegisterLevelObjects(level);
        _interpreter = new WdlInterpreter(this);
    }

    /// <summary>The live global skill values.</summary>
    public IReadOnlyDictionary<string, double> Skills => _skills;

    /// <summary>The named object references available to scripts.</summary>
    public IReadOnlyDictionary<string, AcknexObject> Objects => _objects;

    /// <summary>All placed level objects registered for runtime iteration.</summary>
    public IReadOnlyList<AcknexObject> LevelObjects => _levelObjects;

    /// <inheritdoc/>
    public double GetSkill(string name) => _skills.GetValueOrDefault(name);

    /// <inheritdoc/>
    public void SetSkill(string name, double value) =>
        _skills[name] = _bounds.TryGetValue(name, out SkillRange range) ? range.Clamp(value) : value;

    /// <inheritdoc/>
    public AcknexObject? GetObject(string name) => _objects.GetValueOrDefault(name);

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
    /// Registers an object/action pair for the provisional per-frame scheduler.
    /// Exact Acknex discovery and dispatch ordering are still clean-room parity work.
    /// </summary>
    public void RegisterEachCycle(AcknexObject target, string actionName)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(actionName);
        _cycleActions.Add(new CycleAction(target, actionName));
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

    /// <summary>Whether an action with this name exists.</summary>
    public bool HasAction(string name) => _actions.ContainsKey(name);

    /// <summary>Runs the level's <c>IF_START</c> startup action, if one is defined.</summary>
    /// <returns><c>true</c> if a startup action existed and ran.</returns>
    public bool RunStartup() => _startupAction is not null && Run(_startupAction);

    /// <summary>Runs the named action's script, if it exists.</summary>
    /// <returns><c>true</c> if the action was found and executed.</returns>
    public bool RunAction(string name) => Run(name);

    /// <summary>
    /// Advances the runtime by one frame lasting <paramref name="deltaSeconds"/>, refreshing the
    /// global <c>TIME_CORR</c> skill that movement/animation scripts scale by (see <see cref="Timing"/>).
    /// Per-object cycle hooks will be dispatched here as the scheduler grows.
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

        foreach (CycleAction cycle in _cycleActions)
        {
            RunForObject(cycle.Target, cycle.ActionName);
        }

        return timeCorrection;
    }

    /// <inheritdoc/>
    public void CallAction(string name) => Run(name);

    private void RegisterLevelObjects(Level level)
    {
        foreach (AcknexObject obj in level.Regions)
        {
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

    private bool RunForObject(AcknexObject target, string actionName)
    {
        bool hadMy = _objects.TryGetValue("my", out AcknexObject? previousMy);
        _objects["my"] = target;
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
        }
    }

    private bool Run(string name)
    {
        // Guard against runaway mutual recursion between actions.
        if (_callDepth >= MaxCallDepth || !_actions.TryGetValue(name, out WdlBlock? block))
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

    private readonly record struct CycleAction(AcknexObject Target, string ActionName);
}
