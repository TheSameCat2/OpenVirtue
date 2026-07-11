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
        "IF_KLICK",
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

    /// <summary>The documented <c>HEIGHT</c> alias for the object's feet offset (<see cref="Z"/>).</summary>
    public double Height
    {
        get => Z;
        set => Z = value;
    }

    /// <summary>Index of the region the entity is in.</summary>
    public int Region { get; set; }

    /// <summary>The entity's current texture/sprite name.</summary>
    public string? Texture { get; set; }

    /// <summary>The actor's current movement target or target mode keyword.</summary>
    public string? Target { get; set; }

    /// <summary>The actor's assigned waypoint path keyword.</summary>
    public string? WayName { get; set; }

    /// <summary>The inventory/interaction overlay assigned to this object.</summary>
    public string? OverlayName { get; set; }

    /// <summary>The texture attached to this object.</summary>
    public string? AttachTexture { get; set; }

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
        if (IsEntityActionHook(name))
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

        if (name.Equals("TARGET", StringComparison.OrdinalIgnoreCase))
        {
            value = Target;
            return true;
        }

        if (name.Equals("WAY", StringComparison.OrdinalIgnoreCase))
        {
            value = WayName;
            return true;
        }

        if (name.Equals("OVERLAY", StringComparison.OrdinalIgnoreCase))
        {
            value = OverlayName;
            return true;
        }

        if (name.Equals("ATTACH", StringComparison.OrdinalIgnoreCase))
        {
            value = AttachTexture;
            return true;
        }

        if (name.Equals("EACH_TICK", StringComparison.OrdinalIgnoreCase))
        {
            value = EachTickAction;
            return true;
        }

        if (name.Equals("EACH_CYCLE", StringComparison.OrdinalIgnoreCase))
        {
            value = EachCycleAction;
            return true;
        }

        if (IsEntityActionHook(name))
        {
            _actionHooks.TryGetValue(name, out value);
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

        if (name.Equals("TARGET", StringComparison.OrdinalIgnoreCase))
        {
            Target = NormalizeKeyword(value);
            return true;
        }

        if (name.Equals("WAY", StringComparison.OrdinalIgnoreCase))
        {
            WayName = NormalizeKeyword(value);
            return true;
        }

        if (name.Equals("OVERLAY", StringComparison.OrdinalIgnoreCase))
        {
            OverlayName = NormalizeKeyword(value);
            return true;
        }

        if (name.Equals("ATTACH", StringComparison.OrdinalIgnoreCase))
        {
            AttachTexture = NormalizeKeyword(value);
            return true;
        }

        if (name.Equals("EACH_TICK", StringComparison.OrdinalIgnoreCase))
        {
            EachTickAction = string.IsNullOrWhiteSpace(value) ? null : value;
            return true;
        }

        if (name.Equals("EACH_CYCLE", StringComparison.OrdinalIgnoreCase))
        {
            EachCycleAction = string.IsNullOrWhiteSpace(value) ? null : value;
            return true;
        }

        if (IsEntityActionHook(name))
        {
            SetActionHook(name, value);
            return true;
        }

        return false;
    }

    private static bool IsEntityActionHook(string name) => EntityActionHookNames.Contains(name);

    private static string? NormalizeKeyword(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : value;

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
