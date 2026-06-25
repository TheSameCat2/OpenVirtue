// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Map;

/// <summary>
/// Base for placed sprite objects positioned in the world: a <see cref="Thing"/>
/// (static) or an <see cref="Actor"/> (dynamic/AI). Carries the common pose fields.
/// </summary>
public abstract class MapEntity : AcknexObject
{
    protected MapEntity(string name) : base(name) { }

    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Angle { get; set; }

    /// <summary>Index of the region the entity is in.</summary>
    public int Region { get; set; }

    /// <summary>The entity's current texture/sprite name.</summary>
    public string? Texture { get; set; }

    protected override bool TryGetTyped(string property, out double value)
    {
        switch (property.ToLowerInvariant())
        {
            case "x": value = X; return true;
            case "y": value = Y; return true;
            case "z": value = Z; return true;
            case "angle": value = Angle; return true;
            default: value = 0; return false;
        }
    }

    protected override bool TrySetTyped(string property, double value)
    {
        switch (property.ToLowerInvariant())
        {
            case "x": X = value; return true;
            case "y": Y = value; return true;
            case "z": Z = value; return true;
            case "angle": Angle = value; return true;
            default: return false;
        }
    }
}

/// <summary>A static placed sprite (e.g. a plant, an item).</summary>
public sealed class Thing : MapEntity
{
    public Thing(string name) : base(name) { }
}

/// <summary>A dynamic/AI sprite (e.g. an enemy). Movement and behaviour fields are added as the runtime grows.</summary>
public sealed class Actor : MapEntity
{
    public Actor(string name) : base(name) { }
}
