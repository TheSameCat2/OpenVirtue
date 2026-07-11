// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine;

/// <summary>
/// Structural WDL UI declaration captured from <c>OVERLAY</c>, <c>PANEL</c>, <c>TEXT</c>,
/// <c>FONT</c>, <c>VIDEO</c>, and related top-level items.
/// </summary>
public sealed record WdlUiElement(
    string Keyword,
    string Name,
    IReadOnlyList<WdlUiValue> Header,
    IReadOnlyList<WdlUiProperty> Properties);

/// <summary>A named property row inside a structural WDL UI declaration.</summary>
public sealed record WdlUiProperty(
    string Keyword,
    IReadOnlyList<WdlUiValue> Values);

/// <summary>A token value preserved for later UI interpretation without exposing source text.</summary>
public sealed record WdlUiValue(string Kind, string Text);
