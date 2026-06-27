// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine;

/// <summary>
/// Base class for runtime Acknex objects (regions, walls, things, actors). Implements
/// the hybrid object model of <see href="../../docs/adr/0006-engine-object-model-hybrid.md">ADR-0006</see>:
/// well-known fields live as typed properties on the derived class, while everything
/// else — custom skills, synonyms, reflective <c>my.skill</c> access — is stored in a
/// per-object dynamic table. The <see cref="this[string]"/> accessor bridges the two so
/// the interpreter can read and write any property uniformly by name.
/// </summary>
public abstract class AcknexObject
{
    private readonly Dictionary<string, double> _skills = new(StringComparer.OrdinalIgnoreCase);

    protected AcknexObject(string name) => Name = name;

    /// <summary>The object's name (its WDL-defined type name, or an instance name).</summary>
    public string Name { get; }

    /// <summary>The dynamic skills/properties not represented by a typed field.</summary>
    public IReadOnlyDictionary<string, double> Skills => _skills;

    /// <summary>
    /// Reflective access to any property by name (case-insensitive). Reads resolve to a
    /// typed field when one matches, otherwise to the dynamic table (defaulting to 0, as
    /// Acknex skills do). Writes update the typed field when one matches, otherwise the
    /// dynamic table.
    /// </summary>
    public double this[string name]
    {
        get => TryGetTyped(name, out double value)
            ? value
            : _skills.TryGetValue(name, out double skill) ? skill : 0;
        set
        {
            if (!TrySetTyped(name, value))
            {
                _skills[name] = value;
            }
        }
    }

    /// <summary>Whether a dynamic skill with this name has been set.</summary>
    public bool HasSkill(string name) => _skills.ContainsKey(name);

    /// <summary>Maps a well-known property name to a typed field. Override in derived types.</summary>
    protected virtual bool TryGetTyped(string name, out double value)
    {
        value = 0;
        return false;
    }

    /// <summary>Writes a well-known property name to a typed field. Override in derived types.</summary>
    protected virtual bool TrySetTyped(string name, double value) => false;

    /// <inheritdoc/>
    public override string ToString() => $"{GetType().Name} '{Name}'";
}
