// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Map;

/// <summary>
/// A wall segment between two vertices, with a region on each side (a portal when both
/// sides are real regions). Indices reference the level's vertex and region tables.
/// </summary>
public sealed class Wall : AcknexObject
{
    private static readonly HashSet<string> WallActionHookNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "IF_NEAR",
        "IF_FAR",
        "IF_HIT",
    };

    private readonly Dictionary<string, string?> _actionHooks = new(StringComparer.OrdinalIgnoreCase);

    public Wall(string name) : base(name) { }

    public int Vertex1 { get; set; }
    public int Vertex2 { get; set; }
    public int LeftRegion { get; set; }
    public int RightRegion { get; set; }
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public string? Texture { get; set; }
    public string? AttachTexture { get; set; }

    /// <summary>The action declared by wall-level <c>EACH_TICK</c>, run after each frame cycle.</summary>
    public string? EachTickAction { get; set; }

    /// <summary>The action declared by wall-level <c>EACH_CYCLE</c>, run after animation-cycle completion.</summary>
    public string? EachCycleAction { get; set; }

    /// <summary>Wall-level <c>IF_*</c> event action assignments, by hook keyword.</summary>
    public IReadOnlyDictionary<string, string?> ActionHooks => _actionHooks;

    /// <summary>Assigns or clears a documented wall-level event action hook.</summary>
    public void SetActionHook(string name, string? actionName)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (IsWallActionHook(name))
        {
            _actionHooks[name] = string.IsNullOrWhiteSpace(actionName) ? null : actionName;
        }
    }

    /// <summary>Reads a documented wall-level event action hook assignment.</summary>
    public bool TryGetActionHook(string name, out string? actionName) =>
        _actionHooks.TryGetValue(name, out actionName);

    public override bool TryGetString(string name, out string? value)
    {
        if (name.Equals("TEXTURE", StringComparison.OrdinalIgnoreCase))
        {
            value = Texture;
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

        if (IsWallActionHook(name))
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

        if (name.Equals("ATTACH", StringComparison.OrdinalIgnoreCase))
        {
            AttachTexture = string.IsNullOrWhiteSpace(value) || value.Equals("NULL", StringComparison.OrdinalIgnoreCase)
                ? null
                : value;
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

        if (IsWallActionHook(name))
        {
            SetActionHook(name, value);
            return true;
        }

        return false;
    }

    private static bool IsWallActionHook(string name) => WallActionHookNames.Contains(name);

    protected override bool TryGetTyped(string name, out double value)
    {
        switch (name.ToLowerInvariant())
        {
            case "left": value = LeftRegion; return true;
            case "right": value = RightRegion; return true;
            case "offsx": value = OffsetX; return true;
            case "offset_x": value = OffsetX; return true;
            case "offsy": value = OffsetY; return true;
            case "offset_y": value = OffsetY; return true;
            default: value = 0; return false;
        }
    }

    protected override bool TrySetTyped(string name, double value)
    {
        switch (name.ToLowerInvariant())
        {
            case "offsx": OffsetX = value; return true;
            case "offset_x": OffsetX = value; return true;
            case "offsy": OffsetY = value; return true;
            case "offset_y": OffsetY = value; return true;
            default: return false;
        }
    }
}
