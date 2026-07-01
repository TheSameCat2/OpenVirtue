// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Formats.Wrs;

/// <summary>
/// A file to store in a generated WRS archive.
/// </summary>
/// <param name="Name">Fixed-width archive member name, for example <c>TITLE.WDL</c>.</param>
/// <param name="Data">Uncompressed file bytes.</param>
public readonly record struct WrsFile(string Name, ReadOnlyMemory<byte> Data);
