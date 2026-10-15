// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Diagnostics.CodeAnalysis;
using OpenVirtue.Formats.Wdl;

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
/// Optional surface that resolves an action name to its body, so <c>BRANCH</c> can replace the
/// running action in place (OV-20261015-branch-tail-jump) instead of nesting a call.
/// </summary>
public interface IWdlActionResolver
{
    /// <summary>
    /// Resolves <paramref name="name"/>, following <c>ACTION</c> synonyms, to an action body.
    /// </summary>
    /// <returns><c>false</c> when the name, or the synonym it goes through, names no action.</returns>
    bool TryGetAction(string name, [NotNullWhen(true)] out WdlBlock? body);
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

    /// <summary>
    /// Assigns or clears one of the 16 global <c>EACH_SEC</c> slots, which run once per engine
    /// second (16 ticks). The 1998 manual does not document <c>EACH_SEC</c>; see
    /// <c>docs/clean-room/observations/OV-20261015-each-sec-cadence.md</c>.
    /// </summary>
    /// <param name="slot">The one-based scheduler slot, from 1 through 16.</param>
    /// <param name="actionName">The action to run each second, or <c>null</c> to clear the slot.</param>
    void SetEachSecAction(int slot, string? actionName);
}

/// <summary>
/// Optional action-hook surface for global input/load callbacks such as <c>IF_F1</c> or <c>IF_LEFT</c>.
/// </summary>
public interface IWdlActionHookContext
{
    /// <summary>Whether the keyword names a documented global action hook.</summary>
    bool IsActionHook(string name);

    /// <summary>Assigns or clears a global action hook.</summary>
    void SetActionHook(string name, string? actionName);
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

/// <summary>Optional aggregate execution diagnostics used by clean-room runtime probes.</summary>
public interface IWdlExecutionDiagnostics
{
    /// <summary>Records an ACTION statement immediately before execution.</summary>
    void RecordStatementExecution(string keyword, string? target);

    /// <summary>Records a statement skipped because its expression form could not be evaluated.</summary>
    void RecordUnsupportedStatementExpression();

    /// <summary>Records the boolean result of a conditional statement.</summary>
    void RecordConditionEvaluation(bool result);
}

/// <summary>Optional runtime surface for numeric object writes that require engine-side invariants.</summary>
public interface IWdlObjectMutationContext
{
    /// <summary>Writes a numeric object member while preserving any engine-managed relationships.</summary>
    void SetObjectNumber(AcknexObject target, string memberName, double value);
}

/// <summary>How a synonym-iteration instruction selects the next object reference.</summary>
public enum WdlObjectIterationMode
{
    /// <summary>Advance among loaded objects with the current reference's WDL name.</summary>
    SameName,

    /// <summary>Advance among loaded objects in the current reference's region.</summary>
    SameRegion,
}

/// <summary>
/// Optional object-reference surface for WDL synonyms such as <c>MY</c> and <c>THERE</c>.
/// </summary>
public interface IWdlObjectReferenceContext
{
    /// <summary>Assigns or clears a named object reference.</summary>
    void SetObjectReference(string name, AcknexObject? value);

    /// <summary>Moves a named object reference to the next matching loaded object.</summary>
    bool AdvanceObjectReference(string name, WdlObjectIterationMode mode);
}

/// <summary>Optional runtime storage for WDL <c>STRING</c> values.</summary>
public interface IWdlStringContext
{
    /// <summary>Attempts to resolve the current value of a named WDL string.</summary>
    bool TryGetString(string name, out string value);

    /// <summary>Assigns a named WDL string, preserving its declared capacity.</summary>
    bool SetString(string name, string value);
}

/// <summary>A host-facing input request emitted by WDL <c>INKEY</c>.</summary>
public readonly record struct WdlInputRequest(
    int Id,
    string StringName,
    string InitialValue,
    int Capacity);

/// <summary>Optional runtime surface for WDL text-entry instructions.</summary>
public interface IWdlInputContext
{
    /// <summary>Begins an <c>INKEY</c> request for a declared WDL string.</summary>
    bool BeginInput(string stringName, out WdlInputRequest request);
}

/// <summary>Optional output surface for WDL <c>PRINTFILE</c>/<c>PRINT_VALUE</c>/<c>PRINT_STRING</c>.</summary>
public interface IWdlPrintContext
{
    /// <summary>Selects the text file that subsequent print instructions append to.</summary>
    void SetPrintFile(string baseName, int number);

    /// <summary>Appends a numeric value to the currently selected print file.</summary>
    void PrintValue(double value);

    /// <summary>Appends text to the currently selected print file.</summary>
    void PrintString(string value);
}

/// <summary>A host-facing sound request emitted by documented WDL sound instructions.</summary>
public readonly record struct WdlSoundRequest(
    string SoundName,
    string? FileName,
    double Volume,
    double? Balance,
    string? EmitterName,
    bool Streamed,
    int Channel,
    bool Loop);

/// <summary>The host-side audio operation requested by a WDL sound instruction.</summary>
public enum WdlSoundOperation
{
    /// <summary>Play one WAVE sound.</summary>
    Play,

    /// <summary>Stop one sound channel.</summary>
    StopChannel,

    /// <summary>Stop all currently playing sounds.</summary>
    StopAll,
}

/// <summary>A chronological host-facing audio request emitted by WDL sound instructions.</summary>
public readonly record struct WdlAudioRequest(
    WdlSoundOperation Operation,
    WdlSoundRequest Sound,
    int Channel = -1);

/// <summary>An automatic non-looping texture sound emitted on an authored animation phase.</summary>
public readonly record struct WdlTextureSoundRequest(AcknexObject Target, LevelTexture Texture);

/// <summary>Optional runtime surface for documented WDL sound instructions.</summary>
public interface IWdlSoundContext
{
    /// <summary>Requests playback of a named <c>SOUND</c> declaration.</summary>
    void PlaySound(string soundName, string? fileName, double volume, double? balance, string? emitterName);

    /// <summary>Requests playback of an explicit sound file.</summary>
    void PlaySoundFile(string fileName, double volume, double? balance);

    /// <summary>Requests stopping all currently playing sounds.</summary>
    void StopSounds();
}

/// <summary>A host-facing palette fade request emitted by documented WDL palette instructions.</summary>
public readonly record struct WdlPaletteFadeRequest(string PaletteName, string? FileName, double Factor);

/// <summary>Optional runtime surface for documented WDL palette instructions.</summary>
public interface IWdlPaletteContext
{
    /// <summary>Requests fading toward a named <c>PALETTE</c> declaration.</summary>
    void FadePalette(string paletteName, string? fileName, double factor);
}

/// <summary>Optional runtime surface for restoring an object's region from its current position.</summary>
public interface IWdlLocationContext
{
    /// <summary>
    /// Locates <paramref name="target"/> in the loaded map. A <c>null</c> target locates the registered player.
    /// </summary>
    bool Locate(AcknexObject? target);
}

/// <summary>Optional random-number surface for documented WDL expression functions.</summary>
public interface IWdlRandomContext
{
    /// <summary>Returns a random value in the range from 0 through <paramref name="maximum"/>.</summary>
    double Random(double maximum);

    /// <summary>Reseeds the runtime random-number generator.</summary>
    void Randomize();
}

/// <summary>Optional runtime surface for querying declared WDL skill bounds.</summary>
public interface IWdlSkillBoundsContext
{
    /// <summary>Returns whether the named skill is currently at its declared minimum value.</summary>
    bool IsSkillAtMinimum(string name);
}

/// <summary>A host-facing geometry invalidation emitted by documented WDL movement instructions.</summary>
public readonly record struct WdlShakeRequest(string ObjectName);

/// <summary>Optional runtime surface for geometry/cache invalidation instructions.</summary>
public interface IWdlGeometryContext
{
    /// <summary>Signals that an object's position or shape has changed.</summary>
    void Shake(AcknexObject target);

    /// <summary>Drops a thing or actor into the player's line of sight.</summary>
    bool DropObject(AcknexObject target);

    /// <summary>Shifts a region footprint and its contained objects in map-space.</summary>
    bool ShiftRegion(OpenVirtue.Engine.Map.Region region, double dx, double dy);

    /// <summary>Lifts a region's vertex heights and contained objects vertically.</summary>
    bool LiftRegion(OpenVirtue.Engine.Map.Region region, double amount);

    /// <summary>Rotates a region footprint and its contained objects around the region's <c>GENIUS</c> object.</summary>
    bool RotateRegion(OpenVirtue.Engine.Map.Region region, double radians);
}

/// <summary>Optional runtime surface for documented hit-test instructions such as <c>SHOOT</c> and <c>PUSH</c>.</summary>
public interface IWdlHitContext
{
    /// <summary>
    /// Performs a documented <c>SHOOT</c>. A null source shoots from the player; an object source checks player visibility.
    /// </summary>
    void Shoot(AcknexObject? source);

    /// <summary>Triggers the closest visible object within the given distance, using <c>SHOOT</c>-style result skills.</summary>
    void Push(double distance);

    /// <summary>Triggers fragile objects near the given explosion source using <c>SHOOT</c>-style result skills.</summary>
    void Explode(AcknexObject source);
}

/// <summary>A host-facing request to play a full-screen flic animation.</summary>
public readonly record struct WdlFlicRequest(int Id, string FileName, bool Streamed);

/// <summary>A host-facing request to capture a screenshot.</summary>
public readonly record struct WdlScreenshotRequest(string BaseName, int Number);

/// <summary>Optional runtime surface for documented presentation-side instructions.</summary>
public interface IWdlPresentationContext
{
    /// <summary>Requests a short diagnostic beep.</summary>
    void Beep();

    /// <summary>Begins playback of an explicit flic file and returns its host request.</summary>
    bool BeginFlicFile(string fileName, out WdlFlicRequest request);

    /// <summary>Requests stopping the currently playing flic.</summary>
    void StopFlic();

    /// <summary>Requests writing a screenshot with the documented base name and number.</summary>
    void Screenshot(string baseName, int number);
}

/// <summary>The kind of host persistence request emitted by documented WDL save/load instructions.</summary>
public enum WdlPersistenceOperation
{
    /// <summary>Save the current game state.</summary>
    Save,

    /// <summary>Load a previously saved game state.</summary>
    Load,
}

/// <summary>A host-facing request to save or load a numbered game slot.</summary>
public readonly record struct WdlPersistenceRequest(
    WdlPersistenceOperation Operation,
    string BaseName,
    int Number,
    bool InfoOnly);

/// <summary>Optional runtime surface for documented save/load instructions.</summary>
public interface IWdlPersistenceContext
{
    /// <summary>Requests saving game state under a documented five-character base plus numeric slot.</summary>
    void SaveGame(string baseName, int number, bool infoOnly);

    /// <summary>Requests loading game state from a documented five-character base plus numeric slot.</summary>
    void LoadGame(string baseName, int number, bool infoOnly);
}

/// <summary>Optional runtime storage for keyword-valued WDL synonyms, such as <c>TYPE TEXTURE</c>.</summary>
public interface IWdlKeywordReferenceContext
{
    /// <summary>Whether the keyword is a declared keyword-valued synonym.</summary>
    bool IsKeywordReference(string name);

    /// <summary>Attempts to resolve the keyword currently assigned to a synonym.</summary>
    bool TryGetKeywordReference(string name, out string? value);

    /// <summary>Assigns a keyword-valued synonym.</summary>
    void SetKeywordReference(string name, string? value);
}

/// <summary>Optional runtime storage for mutable WDL UI declarations.</summary>
public interface IWdlUiContext
{
    /// <summary>Reads a numeric UI property or flag from an overlay, panel, or text declaration.</summary>
    bool TryGetUiNumber(string elementName, string memberName, out double value);

    /// <summary>Assigns a numeric UI property or flag on an overlay, panel, or text declaration.</summary>
    bool TrySetUiNumber(string elementName, string memberName, double value);

    /// <summary>Reads a keyword-valued UI property from an overlay, panel, or text declaration.</summary>
    bool TryGetUiKeyword(string elementName, string memberName, out string? value);

    /// <summary>Assigns a keyword-valued UI property on an overlay, panel, or text declaration.</summary>
    bool TrySetUiKeyword(string elementName, string memberName, string? value);
}

/// <summary>
/// Optional runtime surface for instructions that ask the host to leave or replace
/// the current world instead of mutating a skill immediately.
/// </summary>
public interface IWdlControlContext
{
    /// <summary>Requests game termination with an optional message.</summary>
    void RequestExit(string? message);

    /// <summary>Requests loading a new WDL level, optionally from another WRS resource.</summary>
    void RequestLevelChange(string levelName, string? resourceName);

    /// <summary>Requests replacing the current map topology with a WMP file.</summary>
    void RequestMapChange(string mapName);
}
