// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Formats.Wdl;

namespace OpenVirtue.Engine.Interpreter;

/// <summary>
/// The runtime environment for a loaded <see cref="Level"/>: the live skill table (seeded
/// from the level's declared skills), the named object registry, and the level's ACTION
/// scripts. Implements <see cref="IWdlContext"/> so the <see cref="WdlInterpreter"/> can
/// read and write state, and exposes <see cref="RunAction"/> to invoke a script by name.
/// </summary>
public sealed class WdlRuntime : IWdlContext
{
    private const int MaxCallDepth = 64;

    private readonly Dictionary<string, double> _skills;
    private readonly IReadOnlyDictionary<string, SkillRange> _bounds;
    private readonly Dictionary<string, AcknexObject> _objects = new(StringComparer.OrdinalIgnoreCase);
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
        _interpreter = new WdlInterpreter(this);
    }

    /// <summary>The live global skill values.</summary>
    public IReadOnlyDictionary<string, double> Skills => _skills;

    /// <inheritdoc/>
    public double GetSkill(string name) => _skills.GetValueOrDefault(name);

    /// <inheritdoc/>
    public void SetSkill(string name, double value) =>
        _skills[name] = _bounds.TryGetValue(name, out SkillRange range) ? range.Clamp(value) : value;

    /// <inheritdoc/>
    public AcknexObject? GetObject(string name) => _objects.GetValueOrDefault(name);

    /// <summary>Registers a named object reference (e.g. <c>my</c>, <c>you</c>, or a level object).</summary>
    public void RegisterObject(string name, AcknexObject value) => _objects[name] = value;

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
        return timeCorrection;
    }

    /// <inheritdoc/>
    public void CallAction(string name) => Run(name);

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
}
