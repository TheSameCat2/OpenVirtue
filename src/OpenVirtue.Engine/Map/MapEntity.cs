// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Map;

/// <summary>
/// Base for placed sprite objects positioned in the world: a <see cref="Thing"/>
/// (static) or an <see cref="Actor"/> (dynamic/AI). Carries the common pose fields.
/// </summary>
public abstract class MapEntity : AcknexObject
{
    private static readonly HashSet<string> EntityActionHookNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "IF_NEAR",
        "IF_FAR",
        "IF_ARRIVED",
        "IF_HIT",
    };

    private readonly Dictionary<string, string?> _actionHooks = new(StringComparer.OrdinalIgnoreCase);

    protected MapEntity(string name) : base(name) { }

    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    /// <summary>The object's WED/WMP placement angle in degrees.</summary>
    public double PlacementAngleDegrees { get; set; }

    /// <summary>The WDL <c>ANGLE</c> parameter: a runtime radian offset from the WED placement angle.</summary>
    public double Angle { get; set; }

    /// <summary>The combined facing angle in radians.</summary>
    public double FacingAngleRadians => PlacementAngleDegrees * (Math.PI / 180.0) + Angle;

    /// <summary>The sprite's height in world units (its on-screen size; width follows the texture aspect).</summary>
    public double Height { get; set; } = 1;

    /// <summary>Index of the region the entity is in.</summary>
    public int Region { get; set; }

    /// <summary>The entity's current texture/sprite name.</summary>
    public string? Texture { get; set; }

    /// <summary>The action declared by object-level <c>EACH_TICK</c>, run after each frame cycle.</summary>
    public string? EachTickAction { get; set; }

    /// <summary>The action declared by object-level <c>EACH_CYCLE</c>, run after animation-cycle completion.</summary>
    public string? EachCycleAction { get; set; }

    /// <summary>Object-level <c>IF_*</c> event action assignments, by hook keyword.</summary>
    public IReadOnlyDictionary<string, string?> ActionHooks => _actionHooks;

    /// <summary>Assigns or clears a documented object-level event action hook.</summary>
    public void SetActionHook(string name, string? actionName)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (EntityActionHookNames.Contains(name))
        {
            _actionHooks[name] = string.IsNullOrWhiteSpace(actionName) ? null : actionName;
        }
    }

    /// <summary>Reads a documented object-level event action hook assignment.</summary>
    public bool TryGetActionHook(string name, out string? actionName) =>
        _actionHooks.TryGetValue(name, out actionName);

    public override bool TryGetString(string name, out string? value)
    {
        if (name.Equals("TEXTURE", StringComparison.OrdinalIgnoreCase))
        {
            value = Texture;
            return true;
        }

        value = null;
        return false;
    }

    public override bool TrySetString(string name, string? value)
    {
        if (name.Equals("TEXTURE", StringComparison.OrdinalIgnoreCase))
        {
            Texture = value;
            return true;
        }

        return false;
    }

    protected override bool TryGetTyped(string name, out double value)
    {
        switch (name.ToLowerInvariant())
        {
            case "x": value = X; return true;
            case "y": value = Y; return true;
            case "z": value = Z; return true;
            case "angle": value = Angle; return true;
            case "height": value = Height; return true;
            case "region": value = Region; return true;
            default: value = 0; return false;
        }
    }

    protected override bool TrySetTyped(string name, double value)
    {
        switch (name.ToLowerInvariant())
        {
            case "x": X = value; return true;
            case "y": Y = value; return true;
            case "z": Z = value; return true;
            case "angle": Angle = value; return true;
            case "height": Height = value; return true;
            case "region": Region = (int)value; return true;
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
