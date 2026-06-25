// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Formats.Pcx;

/// <summary>A 24-bit RGB color (one palette entry).</summary>
public readonly record struct Rgb24(byte R, byte G, byte B);
