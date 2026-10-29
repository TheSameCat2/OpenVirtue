// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using OpenVirtue.Engine.Map;
using OpenVirtue.Formats.Wdl;

namespace OpenVirtue.Engine.Interpreter;

/// <summary>
/// Executes WDL statement blocks (ACTION bodies and nested IF blocks) against an
/// <see cref="IWdlContext"/>. This is the first slice of the runtime: <c>SET</c> and
/// <c>RULE</c> assignments (including <c>+=</c>/<c>-=</c>/<c>*=</c>/<c>/=</c>) and
/// <c>IF (expr) { … }</c> conditionals, synchronous <c>WHILE</c> loops, explicit
/// <c>CALL</c>s, <c>GOTO</c>, <c>END</c>, and basic object-reference iteration via
/// <c>NEXT_MY</c>/<c>NEXT_THERE</c>, plus documented string utility instructions.
/// Optional runtime surfaces also handle documented print-file instructions and global
/// <c>IF_*</c> hook reassignment. More statements (WAIT) and deeper scheduler semantics
/// are layered on later.
/// </summary>
public sealed class WdlInterpreter(IWdlContext context)
{
    private const int MaxStatementSteps = 100_000;

    /// <summary>Executes all statements in a block, in order.</summary>
    public void Execute(WdlBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        ExecuteAction(block.Items);
    }

    /// <summary>Creates resumable execution state for a WDL action block.</summary>
    public static WdlExecutionState Start(WdlBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return new WdlExecutionState(block);
    }

    /// <summary>Advances a WDL action until it completes or reaches <c>WAIT</c>/<c>WAITT</c>.</summary>
    public WdlExecutionResult Continue(WdlExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        int steps = 0;
        while (state.Frames.Count > 0)
        {
            if (++steps > MaxStatementSteps)
            {
                throw new WdlExpressionException("WDL action exceeded the statement execution limit.");
            }

            WdlExecutionFrame frame = state.Frames.Peek();
            if (frame.Index >= frame.Items.Count)
            {
                if (frame.Kind == WdlExecutionFrameKind.While &&
                    frame.Condition is not null &&
                    WdlExpressionParser.Parse(frame.Condition).Evaluate(context) != 0)
                {
                    frame.Index = 0;
                    continue;
                }

                state.Frames.Pop();
                continue;
            }

            WdlItem item = frame.Items[frame.Index];
            if (item.IsLabel)
            {
                frame.Index++;
                continue;
            }

            try
            {
                if (context is IWdlExecutionDiagnostics diagnostics)
                {
                    diagnostics.RecordStatementExecution(
                        item.Keyword,
                        item.Header.Count > 0 && item.Header[0].Kind == WdlTokenKind.Identifier
                            ? item.Header[0].Text
                            : null);
                }

                WdlExecutionResult? result = ExecuteResumableItem(state, frame, item);
                if (result is { } suspension)
                {
                    return suspension;
                }
            }
            catch (InvalidDataException)
            {
                if (context is IWdlExecutionDiagnostics diagnostics)
                {
                    diagnostics.RecordUnsupportedStatementExpression();
                }

                // Match the synchronous interpreter's tolerance for expression forms
                // that are still outside the modeled subset.
                frame.Index++;
            }
            catch (GotoException jump)
            {
                if (!TryJumpToLabel(state, jump.Label))
                {
                    state.Frames.Clear();
                }
            }
            catch (BreakLoopException)
            {
                if (!TryBreakNearestWhile(state))
                {
                    state.Frames.Clear();
                }
            }
            catch (ContinueLoopException)
            {
                if (!TryContinueNearestWhile(state))
                {
                    state.Frames.Clear();
                }
            }
        }

        return new WdlExecutionResult(WdlExecutionStatus.Completed, wait: null);
    }

    /// <summary>Executes the top-level items of a document as statements.</summary>
    public void Execute(WdlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ExecuteAction(document.Items);
    }

    private void ExecuteAction(IReadOnlyList<WdlItem> items)
    {
        IReadOnlyList<WdlItem>? current = items;
        int jumps = 0;
        while (current is not null)
        {
            IReadOnlyList<WdlItem> running = current;
            current = null;
            try
            {
                ExecuteItems(running);
            }
            catch (BranchException jump)
            {
                // BRANCH continues the same action with the target's body (OV-20261015-branch-tail-jump).
                if (++jumps > MaxStatementSteps)
                {
                    throw new WdlExpressionException("WDL action exceeded the statement execution limit.");
                }

                current = jump.Target.Items;
            }
            catch (EndActionException)
            {
                // END terminates the current action.
            }
            catch (GotoException)
            {
                // An unresolved GOTO leaves the current action; valid targets are handled by ExecuteItems.
            }
            catch (LoopControlException)
            {
                // BREAK/CONTINUE outside a WHILE terminate the current action.
            }
        }
    }

    private void ExecuteItems(IReadOnlyList<WdlItem> items)
    {
        Dictionary<string, int> labels = BuildLabelIndex(items);
        int steps = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (++steps > MaxStatementSteps)
            {
                throw new WdlExpressionException("WDL action exceeded the statement execution limit.");
            }

            WdlItem item = items[i];
            if (item.IsLabel)
            {
                continue;
            }

            try
            {
                switch (item.Keyword.ToUpperInvariant())
                {
                    case "SET":
                        ExecuteSet(item);
                        break;
                    case "SET_ALL":
                        ExecuteSetAll(item);
                        break;
                    case "SET_STRING":
                        ExecuteSetString(item, append: false);
                        break;
                    case "ADD_STRING":
                        ExecuteSetString(item, append: true);
                        break;
                    case "TO_STRING":
                        ExecuteToString(item);
                        break;
                    case "SET_SKILL":
                        ExecuteSetSkill(item);
                        break;
                    case "PRINTFILE":
                        ExecutePrintFile(item);
                        break;
                    case "PRINT_VALUE":
                        ExecutePrintValue(item);
                        break;
                    case "PRINT_STRING":
                        ExecutePrintString(item);
                        break;
                    case "PLAY_SOUND":
                        ExecutePlaySound(item);
                        break;
                    case "PLAY_SOUNDFILE":
                        ExecutePlaySoundFile(item);
                        break;
                    case "STOP_SOUND":
                        ExecuteStopSound();
                        break;
                    case "FADE_PAL":
                        ExecuteFadePalette(item);
                        break;
                    case "LOCATE":
                        ExecuteLocate(item);
                        break;
                    case "SHAKE":
                        ExecuteShake(item);
                        break;
                    case "DROP":
                        ExecuteDrop(item);
                        break;
                    case "SHOOT":
                        ExecuteShoot(item);
                        break;
                    case "PUSH":
                        ExecutePush(item);
                        break;
                    case "EXPLODE":
                        ExecuteExplode(item);
                        break;
                    case "SHIFT":
                        ExecuteShift(item);
                        break;
                    case "LIFT":
                        ExecuteLift(item);
                        break;
                    case "ROTATE":
                        ExecuteRotate(item);
                        break;
                    case "BEEP":
                        ExecuteBeep();
                        break;
                    case "PLAY_FLICFILE":
                        ExecutePlayFlicFile(item);
                        break;
                    case "STOP_FLIC":
                        ExecuteStopFlic();
                        break;
                    case "SCREENSHOT":
                        ExecuteScreenshot(item);
                        break;
                    case "INKEY":
                        ExecuteInkey(item);
                        break;
                    case "ACCEL":
                        ExecuteAccel(item);
                        break;
                    case "ADD":
                        ExecuteAdd(item, timeCorrected: false);
                        break;
                    case "ADDT":
                        ExecuteAdd(item, timeCorrected: true);
                        break;
                    case "RANDOMIZE":
                        ExecuteRandomize();
                        break;
                    case "SKIP":
                        i += ExecuteSkip(item);
                        break;
                    case "SAVE":
                        ExecutePersistence(item, WdlPersistenceOperation.Save, infoOnly: false);
                        break;
                    case "LOAD":
                        ExecutePersistence(item, WdlPersistenceOperation.Load, infoOnly: false);
                        break;
                    case "SAVE_INFO":
                        ExecutePersistence(item, WdlPersistenceOperation.Save, infoOnly: true);
                        break;
                    case "LOAD_INFO":
                        ExecutePersistence(item, WdlPersistenceOperation.Load, infoOnly: true);
                        break;
                    case "WAIT":
                    case "WAITT":
                        break;
                    case "EXIT":
                        ExecuteExit(item);
                        throw new EndActionException();
                    case "LEVEL":
                        ExecuteLevel(item);
                        throw new EndActionException();
                    case "MAP":
                        ExecuteMap(item);
                        throw new EndActionException();
                    case "RULE":
                        ExecuteRule(item);
                        break;
                    case "IF_EQUAL":
                    case "IF_NEQUAL":
                    case "IF_BELOW":
                    case "IF_ABOVE":
                        if (!ExecuteLegacyIf(item))
                        {
                            int target = FindLegacyElseOrEndif(items, i + 1);
                            i = target < items.Count ? target : i + 1;
                        }

                        break;
                    case "IF_MIN":
                        if (!ExecuteIfMin(item))
                        {
                            int target = FindLegacyElseOrEndif(items, i + 1);
                            i = target < items.Count ? target : i + 1;
                        }

                        break;
                    case "IF":
                        bool condition = ExecuteIf(item);
                        if (i + 1 < items.Count && IsElse(items[i + 1]))
                        {
                            if (!condition && items[i + 1].Body is { } elseBody)
                            {
                                ExecuteItems(elseBody.Items);
                            }

                            i++;
                        }
                        break;
                    case "ELSE":
                        if (item.Body is null)
                        {
                            i = FindLegacyEndif(items, i + 1);
                        }

                        break;
                    case "ENDIF":
                        break;
                    case "WHILE":
                        ExecuteWhile(item);
                        break;
                    case "BREAK":
                        throw new BreakLoopException();
                    case "CONTINUE":
                        throw new ContinueLoopException();
                    case "CALL":
                        ExecuteCall(item);
                        break;
                    case "BRANCH":
                        switch (ResolveBranch(item, out WdlBlock? branchTarget))
                        {
                            case BranchOutcome.Jump when branchTarget is not null:
                                throw new BranchException(branchTarget);
                            case BranchOutcome.Ended:
                                throw new EndActionException();
                        }

                        break;
                    case "NEXT_MY":
                        ExecuteNextObject("my", WdlObjectIterationMode.SameName);
                        break;
                    case "NEXT_THERE":
                        ExecuteNextObject("there", WdlObjectIterationMode.SameName);
                        break;
                    case "NEXT_MY_THERE":
                        ExecuteNextObject("my", WdlObjectIterationMode.SameRegion);
                        break;
                    case "GOTO":
                        ExecuteGoto(item);
                        break;
                    case "END":
                        throw new EndActionException();

                    default:
                        // A bare keyword may name another action to run; the context invokes it
                        // if it exists and ignores it otherwise (unmodelled instructions are no-ops).
                        context.CallAction(item.Keyword);
                        break;
                }
            }
            catch (InvalidDataException)
            {
                // The statement uses an expression form the evaluator doesn't model yet (e.g. a
                // string literal or an unsupported token). Skip it and continue the block rather
                // than aborting the whole action — fidelity grows as the evaluator does.
            }
            catch (GotoException jump) when (labels.TryGetValue(jump.Label, out int target))
            {
                i = target;
            }
        }
    }

    private WdlExecutionResult? ExecuteResumableItem(WdlExecutionState state, WdlExecutionFrame frame, WdlItem item)
    {
        switch (item.Keyword.ToUpperInvariant())
        {
            case "SET":
                ExecuteSet(item);
                frame.Index++;
                break;
            case "SET_ALL":
                ExecuteSetAll(item);
                frame.Index++;
                break;
            case "SET_STRING":
                ExecuteSetString(item, append: false);
                frame.Index++;
                break;
            case "ADD_STRING":
                ExecuteSetString(item, append: true);
                frame.Index++;
                break;
            case "TO_STRING":
                ExecuteToString(item);
                frame.Index++;
                break;
            case "SET_SKILL":
                ExecuteSetSkill(item);
                frame.Index++;
                break;
            case "PRINTFILE":
                ExecutePrintFile(item);
                frame.Index++;
                break;
            case "PRINT_VALUE":
                ExecutePrintValue(item);
                frame.Index++;
                break;
            case "PRINT_STRING":
                ExecutePrintString(item);
                frame.Index++;
                break;
            case "PLAY_SOUND":
                ExecutePlaySound(item);
                frame.Index++;
                break;
            case "PLAY_SOUNDFILE":
                ExecutePlaySoundFile(item);
                frame.Index++;
                break;
            case "STOP_SOUND":
                ExecuteStopSound();
                frame.Index++;
                break;
            case "FADE_PAL":
                ExecuteFadePalette(item);
                frame.Index++;
                break;
            case "LOCATE":
                ExecuteLocate(item);
                frame.Index++;
                break;
            case "SHAKE":
                ExecuteShake(item);
                frame.Index++;
                break;
            case "DROP":
                ExecuteDrop(item);
                frame.Index++;
                break;
            case "SHOOT":
                ExecuteShoot(item);
                frame.Index++;
                break;
            case "PUSH":
                ExecutePush(item);
                frame.Index++;
                break;
            case "EXPLODE":
                ExecuteExplode(item);
                frame.Index++;
                break;
            case "SHIFT":
                ExecuteShift(item);
                frame.Index++;
                break;
            case "LIFT":
                ExecuteLift(item);
                frame.Index++;
                break;
            case "ROTATE":
                ExecuteRotate(item);
                frame.Index++;
                break;
            case "BEEP":
                ExecuteBeep();
                frame.Index++;
                break;
            case "PLAY_FLICFILE":
                // The action does not wait for the flic (OV-20261015-flic-play-blocking).
                ExecutePlayFlicFile(item);
                frame.Index++;
                break;
            case "STOP_FLIC":
                ExecuteStopFlic();
                frame.Index++;
                break;
            case "SCREENSHOT":
                ExecuteScreenshot(item);
                frame.Index++;
                break;
            case "INKEY":
                return ExecuteInkey(item, frame);
            case "ACCEL":
                ExecuteAccel(item);
                frame.Index++;
                break;
            case "ADD":
                ExecuteAdd(item, timeCorrected: false);
                frame.Index++;
                break;
            case "ADDT":
                ExecuteAdd(item, timeCorrected: true);
                frame.Index++;
                break;
            case "RANDOMIZE":
                ExecuteRandomize();
                frame.Index++;
                break;
            case "SKIP":
                frame.Index += ExecuteSkip(item) + 1;
                break;
            case "SAVE":
                ExecutePersistence(item, WdlPersistenceOperation.Save, infoOnly: false);
                frame.Index++;
                break;
            case "LOAD":
                ExecutePersistence(item, WdlPersistenceOperation.Load, infoOnly: false);
                frame.Index++;
                break;
            case "SAVE_INFO":
                ExecutePersistence(item, WdlPersistenceOperation.Save, infoOnly: true);
                frame.Index++;
                break;
            case "LOAD_INFO":
                ExecutePersistence(item, WdlPersistenceOperation.Load, infoOnly: true);
                frame.Index++;
                break;
            case "EXIT":
                ExecuteExit(item);
                state.Frames.Clear();
                break;
            case "LEVEL":
                ExecuteLevel(item);
                state.Frames.Clear();
                break;
            case "MAP":
                ExecuteMap(item);
                state.Frames.Clear();
                break;
            case "RULE":
                ExecuteRule(item);
                frame.Index++;
                break;
            case "IF_EQUAL":
            case "IF_NEQUAL":
            case "IF_BELOW":
            case "IF_ABOVE":
                ExecuteResumableLegacyIf(frame, item);
                break;
            case "IF_MIN":
                ExecuteResumableIfMin(frame, item);
                break;
            case "IF":
                ExecuteResumableIf(state, frame, item);
                break;
            case "ELSE":
                frame.Index = item.Body is null
                    ? FindLegacyEndif(frame.Items, frame.Index + 1) + 1
                    : frame.Index + 1;
                break;
            case "ENDIF":
                frame.Index++;
                break;
            case "WHILE":
                ExecuteResumableWhile(state, frame, item);
                break;
            case "BREAK":
                throw new BreakLoopException();
            case "CONTINUE":
                throw new ContinueLoopException();
            case "CALL":
                ExecuteCall(item);
                frame.Index++;
                break;
            case "BRANCH":
                switch (ResolveBranch(item, out WdlBlock? branchTarget))
                {
                    case BranchOutcome.Jump when branchTarget is not null:
                        // The target replaces this action in the same state, so a WAIT in it
                        // suspends the action a CALLer is already waiting on, and chains never nest.
                        state.Frames.Clear();
                        state.Frames.Push(WdlExecutionFrame.Block(branchTarget.Items));
                        break;
                    case BranchOutcome.Ended:
                        state.Frames.Clear();
                        break;
                    default:
                        frame.Index++;
                        break;
                }

                break;
            case "NEXT_MY":
                ExecuteNextObject("my", WdlObjectIterationMode.SameName);
                frame.Index++;
                break;
            case "NEXT_THERE":
                ExecuteNextObject("there", WdlObjectIterationMode.SameName);
                frame.Index++;
                break;
            case "NEXT_MY_THERE":
                ExecuteNextObject("my", WdlObjectIterationMode.SameRegion);
                frame.Index++;
                break;
            case "GOTO":
                ExecuteGoto(item);
                frame.Index++;
                break;
            case "WAIT":
                return ExecuteWait(item, WdlWaitMode.FrameCycles, frame) is { } frameWait
                    ? new WdlExecutionResult(WdlExecutionStatus.Waiting, frameWait)
                    : null;
            case "WAITT":
                return ExecuteWait(item, WdlWaitMode.Ticks, frame) is { } tickWait
                    ? new WdlExecutionResult(WdlExecutionStatus.Waiting, tickWait)
                    : null;
            case "END":
                state.Frames.Clear();
                break;
            default:
                context.CallAction(item.Keyword);
                frame.Index++;
                break;
        }

        return null;
    }

    private void ExecuteResumableLegacyIf(WdlExecutionFrame frame, WdlItem item)
    {
        bool condition = ExecuteLegacyIf(item);
        RecordConditionEvaluation(condition);
        if (condition)
        {
            frame.Index++;
            return;
        }

        int target = FindLegacyElseOrEndif(frame.Items, frame.Index + 1);
        frame.Index = target < frame.Items.Count ? target + 1 : Math.Min(frame.Index + 2, frame.Items.Count);
    }

    private void ExecuteResumableIfMin(WdlExecutionFrame frame, WdlItem item)
    {
        bool condition = ExecuteIfMin(item);
        RecordConditionEvaluation(condition);
        if (condition)
        {
            frame.Index++;
            return;
        }

        int target = FindLegacyElseOrEndif(frame.Items, frame.Index + 1);
        frame.Index = target < frame.Items.Count ? target + 1 : Math.Min(frame.Index + 2, frame.Items.Count);
    }

    private void ExecuteResumableIf(WdlExecutionState state, WdlExecutionFrame frame, WdlItem item)
    {
        bool condition = WdlExpressionParser.Parse(item.Header).Evaluate(context) != 0;
        RecordConditionEvaluation(condition);
        int next = frame.Index + 1;
        WdlItem? elseItem = next < frame.Items.Count && IsElse(frame.Items[next])
            ? frame.Items[next]
            : null;

        if (condition)
        {
            frame.Index = elseItem is null ? next : next + 1;
            if (item.Body is { } body)
            {
                state.Frames.Push(WdlExecutionFrame.Block(body.Items));
            }

            return;
        }

        frame.Index = elseItem is null ? next : next + 1;
        if (elseItem?.Body is { } elseBody)
        {
            state.Frames.Push(WdlExecutionFrame.Block(elseBody.Items));
        }
    }

    private void RecordConditionEvaluation(bool result)
    {
        if (context is IWdlExecutionDiagnostics diagnostics)
        {
            diagnostics.RecordConditionEvaluation(result);
        }
    }

    private void ExecuteResumableWhile(WdlExecutionState state, WdlExecutionFrame frame, WdlItem item)
    {
        if (item.Body is null ||
            WdlExpressionParser.Parse(item.Header).Evaluate(context) == 0)
        {
            frame.Index++;
            return;
        }

        frame.Index++;
        state.Frames.Push(WdlExecutionFrame.While(item));
    }

    private WdlWaitRequest? ExecuteWait(WdlItem item, WdlWaitMode mode, WdlExecutionFrame frame)
    {
        double amount = item.Header.Count > 0 ? Evaluate(item.Header, 0, item.Header.Count) : 0;
        frame.Index++;
        return amount > 0 ? new WdlWaitRequest(amount, mode) : null;
    }

    private WdlExecutionResult? ExecuteInkey(WdlItem item, WdlExecutionFrame frame)
    {
        if (item.Header.Count == 0 ||
            item.Header[0].Kind != WdlTokenKind.Identifier ||
            context is not IWdlInputContext input ||
            !input.BeginInput(item.Header[0].Text, out WdlInputRequest request))
        {
            frame.Index++;
            return null;
        }

        frame.Index++;
        return new WdlExecutionResult(WdlExecutionStatus.InputWaiting, Wait: null, Input: request);
    }

    private static bool TryJumpToLabel(WdlExecutionState state, string label)
    {
        List<WdlExecutionFrame> topToBottom = state.Frames.ToList();
        int targetFrameIndex = topToBottom.FindIndex(frame => frame.Labels.TryGetValue(label, out _));
        if (targetFrameIndex < 0)
        {
            return false;
        }

        WdlExecutionFrame targetFrame = topToBottom[targetFrameIndex];
        targetFrame.Index = targetFrame.Labels[label];
        state.Frames.Clear();
        for (int i = topToBottom.Count - 1; i >= targetFrameIndex; i--)
        {
            state.Frames.Push(topToBottom[i]);
        }

        return true;
    }

    private static bool TryBreakNearestWhile(WdlExecutionState state)
    {
        List<WdlExecutionFrame> topToBottom = state.Frames.ToList();
        int whileFrameIndex = topToBottom.FindIndex(frame => frame.Kind == WdlExecutionFrameKind.While);
        if (whileFrameIndex < 0)
        {
            return false;
        }

        state.Frames.Clear();
        for (int i = topToBottom.Count - 1; i > whileFrameIndex; i--)
        {
            state.Frames.Push(topToBottom[i]);
        }

        return true;
    }

    private static bool TryContinueNearestWhile(WdlExecutionState state)
    {
        List<WdlExecutionFrame> topToBottom = state.Frames.ToList();
        int whileFrameIndex = topToBottom.FindIndex(frame => frame.Kind == WdlExecutionFrameKind.While);
        if (whileFrameIndex < 0)
        {
            return false;
        }

        WdlExecutionFrame whileFrame = topToBottom[whileFrameIndex];
        whileFrame.Index = whileFrame.Items.Count;
        state.Frames.Clear();
        for (int i = topToBottom.Count - 1; i >= whileFrameIndex; i--)
        {
            state.Frames.Push(topToBottom[i]);
        }

        return true;
    }

    private static Dictionary<string, int> BuildLabelIndex(IReadOnlyList<WdlItem> items)
    {
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].IsLabel)
            {
                labels[items[i].Keyword] = i;
            }
        }

        return labels;
    }

    private static int FindLegacyElseOrEndif(IReadOnlyList<WdlItem> items, int start)
    {
        int depth = 0;
        for (int i = start; i < items.Count; i++)
        {
            WdlItem item = items[i];
            if (item.IsLabel)
            {
                continue;
            }

            if (IsLegacyIf(item.Keyword))
            {
                depth++;
                continue;
            }

            if (item.Keyword.Equals("ENDIF", StringComparison.OrdinalIgnoreCase))
            {
                if (depth == 0)
                {
                    return i;
                }

                depth--;
                continue;
            }

            if (depth == 0 &&
                item.Body is null &&
                item.Keyword.Equals("ELSE", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return items.Count;
    }

    private static int FindLegacyEndif(IReadOnlyList<WdlItem> items, int start)
    {
        int depth = 0;
        for (int i = start; i < items.Count; i++)
        {
            WdlItem item = items[i];
            if (item.IsLabel)
            {
                continue;
            }

            if (IsLegacyIf(item.Keyword))
            {
                depth++;
                continue;
            }

            if (!item.Keyword.Equals("ENDIF", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (depth == 0)
            {
                return i;
            }

            depth--;
        }

        return items.Count;
    }

    private static bool IsLegacyIf(string keyword) =>
        keyword.Equals("IF_EQUAL", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("IF_NEQUAL", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("IF_BELOW", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("IF_ABOVE", StringComparison.OrdinalIgnoreCase);

    private void ExecuteSet(WdlItem item)
    {
        // SET target, expression
        int comma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (comma < 0)
        {
            return;
        }

        if (TryExecuteSchedulerSlotSet(item.Header, comma))
        {
            return;
        }

        if (TryExecuteActionHookSet(item.Header, comma))
        {
            return;
        }

        if (TryExecuteReferenceSet(item.Header, comma))
        {
            return;
        }

        if (TryExecuteObjectStringSet(item.Header, 0, comma, comma + 1, item.Header.Count))
        {
            return;
        }

        if (TryExecuteUiKeywordSet(item.Header, 0, comma, comma + 1, item.Header.Count))
        {
            return;
        }

        if (TryExecuteKeywordReferenceSet(item.Header, comma))
        {
            return;
        }

        double value = Evaluate(item.Header, comma + 1, item.Header.Count);
        Assign(item.Header, 0, comma, "=", value);
    }

    private void ExecuteRule(WdlItem item)
    {
        // RULE target <assign-op> expression
        int op = IndexOfAssignmentOperator(item.Header);
        if (op < 0)
        {
            return;
        }

        double value = Evaluate(item.Header, op + 1, item.Header.Count);
        Assign(item.Header, 0, op, item.Header[op].Text, value);
    }

    private void ExecuteAccel(WdlItem item)
    {
        int comma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (comma < 0 ||
            !TryReadNumericTarget(item.Header, 0, comma, out double velocity))
        {
            return;
        }

        double acceleration = Evaluate(item.Header, comma + 1, item.Header.Count);
        double timeCorrection = context.GetSkill(Timing.TimeCorrectionSkill);
        double friction = context.GetSkill("FRICTION");
        double inertia = context.GetSkill("INERTIA");
        if (inertia == 0)
        {
            inertia = 1;
        }

        double value = ((1 - (timeCorrection * friction)) * velocity) +
                       ((timeCorrection * acceleration) / inertia);
        Assign(item.Header, 0, comma, "=", value);
    }

    private void ExecuteAdd(WdlItem item, bool timeCorrected)
    {
        int comma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (comma < 0)
        {
            return;
        }

        double value = Evaluate(item.Header, comma + 1, item.Header.Count);
        if (timeCorrected)
        {
            value *= context.GetSkill(Timing.TimeCorrectionSkill);
        }

        Assign(item.Header, 0, comma, "+=", value);
    }

    private void ExecuteRandomize()
    {
        if (context is IWdlRandomContext random)
        {
            random.Randomize();
        }
    }

    private int ExecuteSkip(WdlItem item)
    {
        if (item.Header.Count == 0)
        {
            return 0;
        }

        double value = Evaluate(item.Header, 0, item.Header.Count);
        return Math.Max(0, (int)Math.Truncate(value));
    }

    private bool ExecuteIf(WdlItem item)
    {
        // IF (expr) { body }  — the parenthesised header parses directly as a grouped expression.
        bool condition = WdlExpressionParser.Parse(item.Header).Evaluate(context) != 0;
        if (condition && item.Body is { } body)
        {
            ExecuteItems(body.Items);
        }

        return condition;
    }

    private bool ExecuteLegacyIf(WdlItem item)
    {
        string op = item.Keyword.ToUpperInvariant() switch
        {
            "IF_EQUAL" => "==",
            "IF_NEQUAL" => "!=",
            "IF_BELOW" => "<",
            "IF_ABOVE" => ">",
            _ => "!=",
        };

        int comma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (comma < 0)
        {
            return WdlExpressionParser.Parse(item.Header).Evaluate(context) != 0;
        }

        List<WdlToken> comparison = Slice(item.Header, 0, comma);
        WdlToken commaToken = item.Header[comma];
        comparison.Add(new WdlToken(WdlTokenKind.Operator, op, commaToken.Line, commaToken.Column));
        comparison.AddRange(Slice(item.Header, comma + 1, item.Header.Count));
        return WdlExpressionParser.Parse(comparison).Evaluate(context) != 0;
    }

    private bool ExecuteIfMin(WdlItem item) =>
        item.Header.Count > 0 &&
        item.Header[0].Kind == WdlTokenKind.Identifier &&
        context is IWdlSkillBoundsContext bounds &&
        bounds.IsSkillAtMinimum(item.Header[0].Text);

    private static bool IsElse(WdlItem item) =>
        !item.IsLabel && item.Keyword.Equals("ELSE", StringComparison.OrdinalIgnoreCase);

    private void ExecuteWhile(WdlItem item)
    {
        if (item.Body is not { } body)
        {
            return;
        }

        int iterations = 0;
        while (WdlExpressionParser.Parse(item.Header).Evaluate(context) != 0)
        {
            if (++iterations > MaxStatementSteps)
            {
                throw new WdlExpressionException("WDL WHILE loop exceeded the iteration limit.");
            }

            try
            {
                ExecuteItems(body.Items);
            }
            catch (ContinueLoopException)
            {
                continue;
            }
            catch (BreakLoopException)
            {
                break;
            }
        }
    }

    private void ExecuteCall(WdlItem item)
    {
        if (item.Header.Count > 0 && item.Header[0].Kind == WdlTokenKind.Identifier)
        {
            context.CallAction(item.Header[0].Text);
        }
    }

    /// <summary>
    /// Resolves a <c>BRANCH</c> (OV-20261015-branch-tail-jump): a tail jump that runs the target
    /// in place of the current action. A target that resolves to no action (a <c>NULL</c>
    /// synonym) is skipped and the action goes on. A context without an
    /// <see cref="IWdlActionResolver"/> can only nest the target, so the action ends after it.
    /// </summary>
    private BranchOutcome ResolveBranch(WdlItem item, out WdlBlock? target)
    {
        target = null;
        if (item.Header.Count == 0 || item.Header[0].Kind != WdlTokenKind.Identifier)
        {
            return BranchOutcome.Skip;
        }

        string name = item.Header[0].Text;
        if (context is IWdlActionResolver resolver)
        {
            return resolver.TryGetAction(name, out target) ? BranchOutcome.Jump : BranchOutcome.Skip;
        }

        context.CallAction(name);
        return BranchOutcome.Ended;
    }

    private static void ExecuteGoto(WdlItem item)
    {
        if (item.Header.Count > 0 && item.Header[0].Kind == WdlTokenKind.Identifier)
        {
            throw new GotoException(item.Header[0].Text);
        }
    }

    private void ExecuteSetString(WdlItem item, bool append)
    {
        if (context is not IWdlStringContext strings ||
            !TryStringInstruction(item.Header, out string target, out int comma) ||
            !TryEvaluateString(item.Header, comma + 1, item.Header.Count, out string value))
        {
            return;
        }

        if (append && strings.TryGetString(target, out string current))
        {
            strings.SetString(target, current + value);
            return;
        }

        strings.SetString(target, value);
    }

    private void ExecuteToString(WdlItem item)
    {
        if (context is not IWdlStringContext strings ||
            !TryStringInstruction(item.Header, out string target, out int comma))
        {
            return;
        }

        double value = Evaluate(item.Header, comma + 1, item.Header.Count);
        strings.SetString(target, value.ToString("G15", CultureInfo.InvariantCulture));
    }

    private void ExecuteSetSkill(WdlItem item)
    {
        if (context is not IWdlStringContext ||
            !TryStringInstruction(item.Header, out string target, out int comma) ||
            !TryEvaluateString(item.Header, comma + 1, item.Header.Count, out string value) ||
            !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return;
        }

        context.SetSkill(target, parsed);
    }

    private void ExecutePrintFile(WdlItem item)
    {
        int comma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (context is not IWdlPrintContext print ||
            comma < 0 ||
            !TryEvaluateString(item.Header, 0, comma, out string baseName))
        {
            return;
        }

        int number = (int)Math.Truncate(Evaluate(item.Header, comma + 1, item.Header.Count));
        print.SetPrintFile(baseName, number);
    }

    private void ExecutePrintValue(WdlItem item)
    {
        if (context is IWdlPrintContext print && item.Header.Count > 0)
        {
            print.PrintValue(Evaluate(item.Header, 0, item.Header.Count));
        }
    }

    private void ExecutePrintString(WdlItem item)
    {
        if (context is not IWdlPrintContext print ||
            !TryEvaluateString(item.Header, 0, item.Header.Count, out string value))
        {
            return;
        }

        // "\n" is a bare line feed on every host, as the DOS runtime writes it (OV-20261029-print-output).
        print.PrintString(value.Replace("\\n", "\n", StringComparison.Ordinal));
    }

    private void ExecutePlaySound(WdlItem item)
    {
        int firstComma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (context is not IWdlSoundContext sound ||
            firstComma < 0 ||
            !TryEvaluateResourceName(item.Header, 0, firstComma, out string soundName, out _))
        {
            return;
        }

        int secondComma = IndexOfKind(item.Header, WdlTokenKind.Comma, firstComma + 1);
        int volumeEnd = secondComma < 0 ? item.Header.Count : secondComma;
        double volume = Evaluate(item.Header, firstComma + 1, volumeEnd);
        double? balance = null;
        string? emitterName = null;

        if (secondComma >= 0 && secondComma + 1 < item.Header.Count)
        {
            if (TryEvaluateSoundEmitter(item.Header, secondComma + 1, item.Header.Count, out emitterName))
            {
                sound.PlaySound(soundName, fileName: null, volume, balance, emitterName);
                return;
            }

            balance = Evaluate(item.Header, secondComma + 1, item.Header.Count);
        }

        sound.PlaySound(soundName, fileName: null, volume, balance, emitterName);
    }

    private void ExecutePlaySoundFile(WdlItem item)
    {
        int firstComma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (context is not IWdlSoundContext sound ||
            firstComma < 0 ||
            !TryEvaluateResourceName(item.Header, 0, firstComma, out string fileName, out _))
        {
            return;
        }

        int secondComma = IndexOfKind(item.Header, WdlTokenKind.Comma, firstComma + 1);
        int volumeEnd = secondComma < 0 ? item.Header.Count : secondComma;
        double volume = Evaluate(item.Header, firstComma + 1, volumeEnd);
        double? balance = secondComma >= 0 && secondComma + 1 < item.Header.Count
            ? Evaluate(item.Header, secondComma + 1, item.Header.Count)
            : null;

        sound.PlaySoundFile(fileName, volume, balance);
    }

    private void ExecuteStopSound()
    {
        if (context is IWdlSoundContext sound)
        {
            sound.StopSounds();
        }
    }

    private void ExecuteFadePalette(WdlItem item)
    {
        int comma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (context is not IWdlPaletteContext palette ||
            comma < 0 ||
            !TryEvaluateResourceName(item.Header, 0, comma, out string paletteName, out _))
        {
            return;
        }

        double factor = Evaluate(item.Header, comma + 1, item.Header.Count);
        palette.FadePalette(paletteName, fileName: null, factor);
    }

    private void ExecuteLocate(WdlItem item)
    {
        if (context is not IWdlLocationContext location)
        {
            return;
        }

        AcknexObject? target = null;
        if (item.Header.Count > 0)
        {
            if (item.Header[0].Kind != WdlTokenKind.Identifier)
            {
                return;
            }

            target = context.GetObject(item.Header[0].Text);
            if (target is null)
            {
                return;
            }
        }

        location.Locate(target);
    }

    private void ExecuteShake(WdlItem item)
    {
        if (context is not IWdlGeometryContext geometry ||
            item.Header.Count == 0 ||
            item.Header[0].Kind != WdlTokenKind.Identifier ||
            context.GetObject(item.Header[0].Text) is not { } target)
        {
            return;
        }

        geometry.Shake(target);
    }

    private void ExecuteDrop(WdlItem item)
    {
        if (context is not IWdlGeometryContext geometry ||
            item.Header.Count == 0 ||
            item.Header[0].Kind != WdlTokenKind.Identifier ||
            context.GetObject(item.Header[0].Text) is not { } target)
        {
            return;
        }

        geometry.DropObject(target);
    }

    private void ExecuteShoot(WdlItem item)
    {
        if (context is not IWdlHitContext hit)
        {
            return;
        }

        AcknexObject? source = null;
        if (item.Header.Count > 0)
        {
            if (item.Header[0].Kind != WdlTokenKind.Identifier)
            {
                return;
            }

            source = context.GetObject(item.Header[0].Text);
            if (source is null)
            {
                return;
            }
        }

        hit.Shoot(source);
    }

    private void ExecutePush(WdlItem item)
    {
        if (context is IWdlHitContext hit)
        {
            double distance = item.Header.Count > 0 ? Evaluate(item.Header, 0, item.Header.Count) : 0;
            hit.Push(distance);
        }
    }

    private void ExecuteExplode(WdlItem item)
    {
        if (context is not IWdlHitContext hit ||
            item.Header.Count == 0 ||
            item.Header[0].Kind != WdlTokenKind.Identifier ||
            context.GetObject(item.Header[0].Text) is not { } source)
        {
            return;
        }

        hit.Explode(source);
    }

    private void ExecuteShift(WdlItem item)
    {
        int firstComma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        int secondComma = firstComma < 0 ? -1 : IndexOfKind(item.Header, WdlTokenKind.Comma, firstComma + 1);
        if (context is not IWdlGeometryContext geometry ||
            firstComma < 0 ||
            secondComma < 0 ||
            item.Header.Count == 0 ||
            item.Header[0].Kind != WdlTokenKind.Identifier ||
            context.GetObject(item.Header[0].Text) is not Region region)
        {
            return;
        }

        double dx = Evaluate(item.Header, firstComma + 1, secondComma);
        double dy = Evaluate(item.Header, secondComma + 1, item.Header.Count);
        geometry.ShiftRegion(region, dx, dy);
    }

    private void ExecuteLift(WdlItem item)
    {
        int comma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (context is not IWdlGeometryContext geometry ||
            comma < 0 ||
            item.Header.Count == 0 ||
            item.Header[0].Kind != WdlTokenKind.Identifier ||
            context.GetObject(item.Header[0].Text) is not Region region)
        {
            return;
        }

        double amount = Evaluate(item.Header, comma + 1, item.Header.Count);
        geometry.LiftRegion(region, amount);
    }

    private void ExecuteRotate(WdlItem item)
    {
        int comma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (context is not IWdlGeometryContext geometry ||
            comma < 0 ||
            item.Header.Count == 0 ||
            item.Header[0].Kind != WdlTokenKind.Identifier ||
            context.GetObject(item.Header[0].Text) is not Region region)
        {
            return;
        }

        double radians = Evaluate(item.Header, comma + 1, item.Header.Count);
        geometry.RotateRegion(region, radians);
    }

    private void ExecuteBeep()
    {
        if (context is IWdlPresentationContext presentation)
        {
            presentation.Beep();
        }
    }

    private void ExecutePlayFlicFile(WdlItem item)
    {
        if (context is IWdlPresentationContext presentation &&
            TryEvaluateResourceName(item.Header, 0, item.Header.Count, out string fileName, out _))
        {
            presentation.BeginFlicFile(fileName);
        }
    }

    private void ExecuteStopFlic()
    {
        if (context is IWdlPresentationContext presentation)
        {
            presentation.StopFlic();
        }
    }

    private void ExecuteScreenshot(WdlItem item)
    {
        int comma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (context is not IWdlPresentationContext presentation ||
            comma < 0 ||
            !TryEvaluateString(item.Header, 0, comma, out string baseName))
        {
            return;
        }

        int number = (int)Math.Truncate(Evaluate(item.Header, comma + 1, item.Header.Count));
        presentation.Screenshot(baseName, number);
    }

    private void ExecuteInkey(WdlItem item)
    {
        if (item.Header.Count > 0 &&
            item.Header[0].Kind == WdlTokenKind.Identifier &&
            context is IWdlInputContext input)
        {
            input.BeginInput(item.Header[0].Text, out _);
        }
    }

    private void ExecutePersistence(WdlItem item, WdlPersistenceOperation operation, bool infoOnly)
    {
        int comma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (context is not IWdlPersistenceContext persistence ||
            comma < 0 ||
            !TryEvaluateString(item.Header, 0, comma, out string baseName))
        {
            return;
        }

        int number = (int)Math.Truncate(Evaluate(item.Header, comma + 1, item.Header.Count));
        if (operation == WdlPersistenceOperation.Save)
        {
            persistence.SaveGame(baseName, number, infoOnly);
        }
        else
        {
            persistence.LoadGame(baseName, number, infoOnly);
        }
    }

    private void ExecuteExit(WdlItem item)
    {
        if (context is not IWdlControlContext control)
        {
            return;
        }

        string? message = null;
        if (item.Header.Count > 0 && TryEvaluateString(item.Header, 0, item.Header.Count, out string value))
        {
            message = value;
        }

        control.RequestExit(message);
    }

    private void ExecuteLevel(WdlItem item)
    {
        if (context is not IWdlControlContext control ||
            !TryEvaluateResourceName(item.Header, 0, item.Header.Count, out string levelName, out int next))
        {
            return;
        }

        string? resourceName = null;
        if (next < item.Header.Count && item.Header[next].Kind == WdlTokenKind.Comma)
        {
            TryEvaluateResourceName(item.Header, next + 1, item.Header.Count, out resourceName, out _);
        }

        control.RequestLevelChange(levelName, resourceName);
    }

    private void ExecuteMap(WdlItem item)
    {
        if (context is IWdlControlContext control &&
            TryEvaluateResourceName(item.Header, 0, item.Header.Count, out string mapName, out _))
        {
            control.RequestMapChange(mapName);
        }
    }

    private void ExecuteNextObject(string referenceName, WdlObjectIterationMode mode)
    {
        if (context is IWdlObjectReferenceContext references)
        {
            references.AdvanceObjectReference(referenceName, mode);
        }
    }

    private bool TryExecuteSchedulerSlotSet(IReadOnlyList<WdlToken> header, int comma)
    {
        if (!IsSchedulerSlotTarget(header, comma, out bool eachSec, out int slot))
        {
            return false;
        }

        if (slot is < 1 or > 16 || context is not IWdlSchedulerContext scheduler)
        {
            return true;
        }

        string? actionName = null;
        if (comma + 1 < header.Count && header[comma + 1].Kind == WdlTokenKind.Identifier)
        {
            actionName = header[comma + 1].Text.Equals("NULL", StringComparison.OrdinalIgnoreCase)
                ? null
                : header[comma + 1].Text;
        }

        if (eachSec)
        {
            scheduler.SetEachSecAction(slot, actionName);
        }
        else
        {
            scheduler.SetEachTickAction(slot, actionName);
        }

        return true;
    }

    private bool TryExecuteActionHookSet(IReadOnlyList<WdlToken> header, int comma)
    {
        if (context is not IWdlActionHookContext hooks ||
            comma != 1 ||
            header[0].Kind != WdlTokenKind.Identifier ||
            !hooks.IsActionHook(header[0].Text))
        {
            return false;
        }

        string? actionName = null;
        if (comma + 2 == header.Count && header[comma + 1].Kind == WdlTokenKind.Identifier)
        {
            actionName = header[comma + 1].Text.Equals("NULL", StringComparison.OrdinalIgnoreCase)
                ? null
                : header[comma + 1].Text;
        }

        hooks.SetActionHook(header[0].Text, actionName);
        return true;
    }

    private bool TryExecuteReferenceSet(IReadOnlyList<WdlToken> header, int comma)
    {
        if (context is not IWdlObjectReferenceContext references ||
            comma != 1 ||
            header[0].Kind != WdlTokenKind.Identifier ||
            comma + 2 != header.Count ||
            header[comma + 1].Kind != WdlTokenKind.Identifier ||
            // A keyword-valued synonym (ACTION, STRING …) takes NULL as a keyword, not an object.
            (context is IWdlKeywordReferenceContext keywords && keywords.IsKeywordReference(header[0].Text)) ||
            !TryResolveObjectReference(header[comma + 1].Text, out AcknexObject? value))
        {
            return false;
        }

        references.SetObjectReference(header[0].Text, value);
        return true;
    }

    private bool TryResolveObjectReference(string name, out AcknexObject? value)
    {
        if (name.Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }

        if (context.GetObject(name) is { } reference)
        {
            value = reference;
            return true;
        }

        if (context is IWdlObjectGroupContext groups)
        {
            value = groups.GetObjectsByName(name).FirstOrDefault();
            return value is not null;
        }

        value = null;
        return false;
    }

    private bool TryEvaluateString(IReadOnlyList<WdlToken> header, int start, int end, out string value)
    {
        value = string.Empty;
        if (end - start != 1)
        {
            return false;
        }

        WdlToken token = header[start];
        if (token.Kind == WdlTokenKind.StringLiteral)
        {
            value = token.Text;
            return true;
        }

        return token.Kind == WdlTokenKind.Identifier &&
               context is IWdlStringContext strings &&
               strings.TryGetString(token.Text, out value);
    }

    private bool TryEvaluateSoundEmitter(
        IReadOnlyList<WdlToken> header,
        int start,
        int end,
        out string? emitterName)
    {
        emitterName = null;
        if (end - start != 1 || header[start].Kind != WdlTokenKind.Identifier)
        {
            return false;
        }

        string name = header[start].Text;
        if (context.GetObject(name) is null)
        {
            return false;
        }

        emitterName = name;
        return true;
    }

    private bool TryEvaluateResourceName(
        IReadOnlyList<WdlToken> header,
        int start,
        int end,
        out string value,
        out int next)
    {
        value = string.Empty;
        next = start;
        if (start >= end)
        {
            return false;
        }

        WdlToken token = header[start];
        if (token.Kind is WdlTokenKind.FileRef or WdlTokenKind.StringLiteral or WdlTokenKind.Identifier)
        {
            value = token.Text;
            next = start + 1;
            return true;
        }

        if (TryEvaluateString(header, start, start + 1, out string stringValue))
        {
            value = stringValue;
            next = start + 1;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Recognises a global scheduler slot target, <c>EACH_TICK.n</c> or <c>EACH_SEC.n</c>, in either
    /// token shape the lexer produces (one <c>.n</c> number token, or <c>.</c> then <c>n</c>).
    /// </summary>
    private static bool IsSchedulerSlotTarget(IReadOnlyList<WdlToken> header, int comma, out bool eachSec, out int slot)
    {
        slot = 0;
        eachSec = false;
        if (comma <= 0 || header.Count == 0)
        {
            return false;
        }

        if (header[0] is not { Kind: WdlTokenKind.Identifier, Text: var schedulerName })
        {
            return false;
        }

        eachSec = schedulerName.Equals("EACH_SEC", StringComparison.OrdinalIgnoreCase);
        if (!eachSec && !schedulerName.Equals("EACH_TICK", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (comma == 2 && header[1].Kind == WdlTokenKind.Number && header[1].Text.StartsWith('.'))
        {
            return int.TryParse(header[1].Text[1..], out slot);
        }

        if (comma == 3 &&
            header[1] is { Kind: WdlTokenKind.Operator, Text: "." } &&
            header[2].Kind == WdlTokenKind.Number)
        {
            return int.TryParse(header[2].Text, out slot);
        }

        return false;
    }

    private static bool TryStringInstruction(
        IReadOnlyList<WdlToken> header,
        out string target,
        out int comma)
    {
        target = string.Empty;
        comma = IndexOfKind(header, WdlTokenKind.Comma);
        if (comma != 1 || header[0].Kind != WdlTokenKind.Identifier || comma + 1 >= header.Count)
        {
            return false;
        }

        target = header[0].Text;
        return true;
    }

    private double Evaluate(IReadOnlyList<WdlToken> tokens, int start, int end) =>
        WdlExpressionParser.Parse(Slice(tokens, start, end)).Evaluate(context);

    private void Assign(IReadOnlyList<WdlToken> header, int start, int end, string op, double rvalue)
    {
        if (end <= start)
        {
            return;
        }

        string name = header[start].Text;

        // Member target: name '.' property
        if (end - start >= 3 && header[start + 1] is { Kind: WdlTokenKind.Operator, Text: "." })
        {
            string property = header[start + 2].Text;
            if (context.GetObject(name) is { } obj)
            {
                SetObjectNumber(obj, property, Apply(op, GetObjectNumber(obj, property), rvalue));
                return;
            }

            if (context is IWdlUiContext ui &&
                ui.TryGetUiNumber(name, property, out double current))
            {
                ui.TrySetUiNumber(name, property, Apply(op, current, rvalue));
            }

            return;
        }

        context.SetSkill(name, Apply(op, context.GetSkill(name), rvalue));
    }

    private bool TryReadNumericTarget(IReadOnlyList<WdlToken> header, int start, int end, out double value)
    {
        value = 0;
        if (end <= start || header[start].Kind != WdlTokenKind.Identifier)
        {
            return false;
        }

        string name = header[start].Text;
        if (end - start >= 3 && header[start + 1] is { Kind: WdlTokenKind.Operator, Text: "." })
        {
            string property = header[start + 2].Text;
            if (context.GetObject(name) is { } obj)
            {
                value = GetObjectNumber(obj, property);
                return true;
            }

            if (context is IWdlUiContext ui &&
                ui.TryGetUiNumber(name, property, out value))
            {
                return true;
            }

            return false;
        }

        value = context.GetSkill(name);
        return true;
    }

    private void ExecuteSetAll(WdlItem item)
    {
        int comma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (comma < 0 ||
            context is not IWdlObjectGroupContext groups ||
            !TryMemberTarget(item.Header, 0, comma, out string objectName, out string property) ||
            IsSetAllPositionProperty(property))
        {
            return;
        }

        List<AcknexObject> matches = groups.GetObjectsByName(objectName).ToList();
        if (TryEvaluateKeywordValue(item.Header, comma + 1, item.Header.Count, out string? textValue))
        {
            bool assignedString = false;
            foreach (AcknexObject obj in matches)
            {
                assignedString |= obj.TrySetString(property, textValue);
            }

            if (assignedString)
            {
                return;
            }
        }

        double value = Evaluate(item.Header, comma + 1, item.Header.Count);
        foreach (AcknexObject obj in matches)
        {
            SetObjectNumber(obj, property, value);
        }
    }

    private double GetObjectNumber(AcknexObject target, string memberName) =>
        context is IWdlObjectReadContext reads ? reads.GetObjectNumber(target, memberName) : target[memberName];

    private void SetObjectNumber(AcknexObject target, string memberName, double value)
    {
        if (context is IWdlObjectMutationContext mutations)
        {
            mutations.SetObjectNumber(target, memberName, value);
        }
        else
        {
            target[memberName] = value;
        }
    }

    private bool TryExecuteKeywordReferenceSet(IReadOnlyList<WdlToken> header, int comma)
    {
        if (context is not IWdlKeywordReferenceContext keywords ||
            comma != 1 ||
            header[0].Kind != WdlTokenKind.Identifier ||
            !keywords.IsKeywordReference(header[0].Text) ||
            !TryEvaluateKeywordValue(header, comma + 1, header.Count, out string? value))
        {
            return false;
        }

        keywords.SetKeywordReference(header[0].Text, value);
        return true;
    }

    private bool TryExecuteObjectStringSet(
        IReadOnlyList<WdlToken> header,
        int targetStart,
        int targetEnd,
        int valueStart,
        int valueEnd)
    {
        if (!TryMemberTarget(header, targetStart, targetEnd, out string objectName, out string property) ||
            context.GetObject(objectName) is not { } obj ||
            !TryEvaluateKeywordValue(header, valueStart, valueEnd, out string? value))
        {
            return false;
        }

        return obj.TrySetString(property, value);
    }

    private bool TryExecuteUiKeywordSet(
        IReadOnlyList<WdlToken> header,
        int targetStart,
        int targetEnd,
        int valueStart,
        int valueEnd)
    {
        if (!TryMemberTarget(header, targetStart, targetEnd, out string elementName, out string property) ||
            context is not IWdlUiContext ui ||
            !TryEvaluateKeywordValue(header, valueStart, valueEnd, out string? value))
        {
            return false;
        }

        return ui.TrySetUiKeyword(elementName, property, value);
    }

    private bool TryEvaluateKeywordValue(
        IReadOnlyList<WdlToken> header,
        int start,
        int end,
        out string? value)
    {
        value = null;
        if (end - start == 3 &&
            header[start].Kind == WdlTokenKind.Identifier &&
            header[start + 1] is { Kind: WdlTokenKind.Operator, Text: "." } &&
            header[start + 2].Kind == WdlTokenKind.Identifier &&
            context.GetObject(header[start].Text) is { } obj &&
            obj.TryGetString(header[start + 2].Text, out string? memberValue))
        {
            value = memberValue;
            return true;
        }

        if (end - start == 3 &&
            header[start].Kind == WdlTokenKind.Identifier &&
            header[start + 1] is { Kind: WdlTokenKind.Operator, Text: "." } &&
            header[start + 2].Kind == WdlTokenKind.Identifier &&
            context is IWdlUiContext ui &&
            ui.TryGetUiKeyword(header[start].Text, header[start + 2].Text, out string? uiMemberValue))
        {
            value = uiMemberValue;
            return true;
        }

        if (end - start != 1)
        {
            return false;
        }

        WdlToken token = header[start];
        if (token.Kind == WdlTokenKind.Identifier)
        {
            if (token.Text.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                value = null;
                return true;
            }

            if (context is IWdlKeywordReferenceContext keywords &&
                keywords.TryGetKeywordReference(token.Text, out string? referencedKeyword))
            {
                value = referencedKeyword;
                return true;
            }

            value = token.Text;
            return true;
        }

        if (token.Kind == WdlTokenKind.StringLiteral)
        {
            value = token.Text;
            return true;
        }

        return false;
    }

    private static bool TryMemberTarget(
        IReadOnlyList<WdlToken> header, int start, int end, out string objectName, out string property)
    {
        objectName = string.Empty;
        property = string.Empty;
        if (end - start < 3 ||
            header[start].Kind != WdlTokenKind.Identifier ||
            header[start + 1] is not { Kind: WdlTokenKind.Operator, Text: "." } ||
            header[start + 2].Kind != WdlTokenKind.Identifier)
        {
            return false;
        }

        objectName = header[start].Text;
        property = header[start + 2].Text;
        return true;
    }

    private static bool IsSetAllPositionProperty(string property) =>
        property.Equals("X", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("Z", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("X1", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("Y1", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("Z1", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("X2", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("Y2", StringComparison.OrdinalIgnoreCase) ||
        property.Equals("Z2", StringComparison.OrdinalIgnoreCase);

    private static double Apply(string op, double current, double rvalue) => op switch
    {
        "=" => rvalue,
        "+=" => current + rvalue,
        "-=" => current - rvalue,
        "*=" => current * rvalue,
        "/=" => rvalue != 0 ? current / rvalue : throw new WdlExpressionException("Division by zero."),
        _ => rvalue,
    };

    private static int IndexOfKind(IReadOnlyList<WdlToken> tokens, WdlTokenKind kind)
    {
        return IndexOfKind(tokens, kind, start: 0);
    }

    private static int IndexOfKind(IReadOnlyList<WdlToken> tokens, WdlTokenKind kind, int start)
    {
        for (int i = start; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == kind)
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfAssignmentOperator(IReadOnlyList<WdlToken> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == WdlTokenKind.Operator &&
                tokens[i].Text is "=" or "+=" or "-=" or "*=" or "/=")
            {
                return i;
            }
        }

        return -1;
    }

    private static List<WdlToken> Slice(IReadOnlyList<WdlToken> tokens, int start, int end)
    {
        var slice = new List<WdlToken>(end - start);
        for (int i = start; i < end; i++)
        {
            slice.Add(tokens[i]);
        }

        return slice;
    }

    private enum BranchOutcome
    {
        Skip,
        Jump,
        Ended,
    }

    private sealed class EndActionException : Exception
    {
    }

    private sealed class BranchException(WdlBlock target) : Exception
    {
        public WdlBlock Target => target;
    }

    private sealed class GotoException(string label) : Exception
    {
        public string Label => label;
    }

    private abstract class LoopControlException : Exception
    {
    }

    private sealed class BreakLoopException : LoopControlException
    {
    }

    private sealed class ContinueLoopException : LoopControlException
    {
    }
}
