// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Engine.Interpreter;

/// <summary>
/// The evaluation environment a WDL expression resolves against: named skills/constants
/// and object references (e.g. <c>my</c>, <c>you</c>, or a named object) for member access.
/// The full interpreter provides the real implementation; tests can supply a stub.
/// </summary>
public interface IWdlContext
{
    /// <summary>Resolves a bare name (skill or constant) to its value; unknown names are 0.</summary>
    double GetSkill(string name);

    /// <summary>Assigns a skill/constant value.</summary>
    void SetSkill(string name, double value);

    /// <summary>Resolves an object reference (<c>my</c>, <c>you</c>, or a named object), or null.</summary>
    AcknexObject? GetObject(string name);
}
