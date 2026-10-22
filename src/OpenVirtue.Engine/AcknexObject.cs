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

    /// <summary>Replaces the object's dynamic skills with a saved runtime snapshot.</summary>
    public void ReplaceSkills(IReadOnlyDictionary<string, double> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        _skills.Clear();
        foreach ((string name, double value) in skills)
        {
            _skills[name] = value;
        }
    }

    /// <summary>Attempts to read a keyword-valued object property, such as a texture reference.</summary>
    public virtual bool TryGetString(string name, out string? value)
    {
        value = null;
        return false;
    }

    /// <summary>Attempts to write a keyword-valued object property, such as a texture reference.</summary>
    public virtual bool TrySetString(string name, string? value) => false;

    /// <summary>Maps a well-known property name to a typed field. Override in derived types.</summary>
    protected virtual bool TryGetTyped(string name, out double value)
    {
        value = 0;
        return false;
    }

    /// <summary>Writes a well-known property name to a typed field. Override in derived types.</summary>
    protected virtual bool TrySetTyped(string name, double value) => false;

    /// <summary>
    /// Lower-cases a property name into <paramref name="buffer"/> as <see cref="string.ToLowerInvariant()"/>
    /// would, for the typed-name switches the scheduler reads every cycle, without allocating a string.
    /// The lowering keeps the length, so a name longer than the buffer (and so than every key) comes
    /// back empty and matches none.
    /// </summary>
    protected static ReadOnlySpan<char> LowerInvariant(string name, Span<char> buffer)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length <= buffer.Length
            ? buffer[..name.AsSpan().ToLowerInvariant(buffer)]
            : [];
    }

    /// <inheritdoc/>
    public override string ToString() => $"{GetType().Name} '{Name}'";
}
