// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine;

/// <summary>
/// A WDL <c>STRING</c> value together with its fixed declared capacity.
/// </summary>
public readonly record struct WdlString(string Value, int Capacity)
{
    /// <summary>Creates a string whose capacity is its initial length.</summary>
    public WdlString(string value) : this(value, value.Length)
    {
    }

    /// <summary>Returns this string with a new value truncated to its declared capacity.</summary>
    public WdlString WithValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int capacity = Math.Max(0, Capacity);
        return this with { Value = value.Length <= capacity ? value : value[..capacity] };
    }
}
