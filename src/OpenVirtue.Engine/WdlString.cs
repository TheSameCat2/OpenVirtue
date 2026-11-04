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

    /// <summary>Returns this string with a new value truncated to its declared capacity (what <c>INKEY</c> takes).</summary>
    public WdlString WithValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int capacity = Math.Max(0, Capacity);
        return this with { Value = value.Length <= capacity ? value : value[..capacity] };
    }

    /// <summary>
    /// Returns this string with a value written by <c>SET_STRING</c>, <c>ADD_STRING</c> or
    /// <c>TO_STRING</c>: one character more than the declared length fits (a 2-character string
    /// took "CDE", an empty one "Z", a 10-character one eleven; OV-20261104-strings concat).
    /// </summary>
    public WdlString WithCopiedValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int limit = Math.Max(0, Capacity) + 1;
        return this with { Value = value.Length <= limit ? value : value[..limit] };
    }
}
