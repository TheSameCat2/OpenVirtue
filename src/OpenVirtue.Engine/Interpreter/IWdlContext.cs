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

    /// <summary>
    /// Invokes a named action if one exists; otherwise does nothing. Lets a statement whose
    /// keyword is an action name (or an as-yet-unmodelled instruction) chain into another script.
    /// </summary>
    void CallAction(string name);
}

/// <summary>
/// Optional scheduler surface for WDL instructions that assign named actions to
/// engine-managed callback slots instead of numeric skills.
/// </summary>
public interface IWdlSchedulerContext
{
    /// <summary>
    /// Assigns or clears one of the 16 global <c>EACH_TICK</c> slots.
    /// </summary>
    /// <param name="slot">The one-based scheduler slot, from 1 through 16.</param>
    /// <param name="actionName">The action to run each tick, or <c>null</c> to clear the slot.</param>
    void SetEachTickAction(int slot, string? actionName);
}

/// <summary>
/// Optional object-group surface for instructions such as <c>SET_ALL</c> that apply to
/// every placed map object with the same WDL keyword instead of only the first unique one.
/// </summary>
public interface IWdlObjectGroupContext
{
    /// <summary>Returns every loaded object with the given WDL name, or an empty sequence.</summary>
    IEnumerable<AcknexObject> GetObjectsByName(string name);
}
