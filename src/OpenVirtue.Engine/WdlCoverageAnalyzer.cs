// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Formats.Wdl;

namespace OpenVirtue.Engine;

/// <summary>
/// Produces sanitized aggregate coverage data from a flattened WDL program.
/// Counts are structural only: no original source lines, literal strings, or
/// action bodies are retained.
/// </summary>
public static class WdlCoverageAnalyzer
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    private static readonly HashSet<string> ImplementedStatementKeywordSet = new(Ci)
    {
        "SET",
        "SET_ALL",
        "SET_STRING",
        "ADD_STRING",
        "TO_STRING",
        "SET_SKILL",
        "PRINTFILE",
        "PRINT_VALUE",
        "PRINT_STRING",
        "PLAY_SOUND",
        "PLAY_SOUNDFILE",
        "STOP_SOUND",
        "FADE_PAL",
        "LOCATE",
        "SHAKE",
        "DROP",
        "SHOOT",
        "PUSH",
        "EXPLODE",
        "SHIFT",
        "LIFT",
        "ROTATE",
        "BEEP",
        "PLAY_FLICFILE",
        "STOP_FLIC",
        "SCREENSHOT",
        "INKEY",
        "ACCEL",
        "ADD",
        "ADDT",
        "RANDOMIZE",
        "SKIP",
        "SAVE",
        "LOAD",
        "SAVE_INFO",
        "LOAD_INFO",
        "WAIT",
        "WAITT",
        "EXIT",
        "LEVEL",
        "MAP",
        "RULE",
        "IF_EQUAL",
        "IF_NEQUAL",
        "IF_BELOW",
        "IF_ABOVE",
        "IF_MIN",
        "IF",
        "ELSE",
        "ENDIF",
        "WHILE",
        "BREAK",
        "CONTINUE",
        "CALL",
        "BRANCH",
        "NEXT_MY",
        "NEXT_THERE",
        "NEXT_MY_THERE",
        "GOTO",
        "END",
    };

    private static readonly HashSet<string> PredefinedRuntimeSkillSet = new(Ci)
    {
        Timing.TimeCorrectionSkill,
        Timing.TimeFactorSkill,
        Timing.TicksSkill,
        Timing.SecondsSkill,
        "MOUSE_MODE",
        "MOUSE_MOVING",
        "MOUSE_CALM",
        "MOUSE_TIME",
        "MICKEY_X",
        "MICKEY_Y",
        "MOUSE_X",
        "MOUSE_Y",
        "MOUSE_ANGLE",
    };

    private static readonly HashSet<string> PlayerMovementSkillSet = new(Ci)
    {
        "FORCE_AHEAD",
        "FORCE_STRAFE",
        "FORCE_ROT",
        "FORCE_TILT",
        "FORCE_UP",
        "PLAYER_VX",
        "PLAYER_VY",
        "PLAYER_VZ",
        "PLAYER_VROT",
        "PLAYER_ANGLE",
        "PLAYER_SIN",
        "PLAYER_COS",
        "SLOPE_AHEAD",
        "SLOPE_SIDE",
        "FRICTION",
        "INERTIA",
    };

    /// <summary>Statement keywords currently handled as built-in instructions by <see cref="Interpreter.WdlInterpreter"/>.</summary>
    public static IReadOnlySet<string> ImplementedStatementKeywords => ImplementedStatementKeywordSet;

    /// <summary>Predefined runtime skill names currently seeded or maintained by the runtime.</summary>
    public static IReadOnlySet<string> PredefinedRuntimeSkills => PredefinedRuntimeSkillSet;

    /// <summary>Analyzes one flattened WDL program.</summary>
    public static WdlCoverageReport Analyze(WdlProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var report = new WdlCoverageReport();
        foreach (WdlItem item in program.Items)
        {
            if (!item.IsLabel)
            {
                Increment(report.DeclarationKeywords, item.Keyword);
            }

            if (item.Keyword.Equals("ACTION", StringComparison.OrdinalIgnoreCase))
            {
                if (item.Body is { } body)
                {
                    report.ActionCount++;
                    CountActionBody(body, report);
                }

                continue;
            }

            CountDeclarationBody(item, report);
        }

        foreach ((string keyword, int count) in report.ActionStatementKeywords)
        {
            Dictionary<string, int> target = IsImplementedStatementKeyword(keyword)
                ? report.ImplementedStatementKeywords
                : report.NonBuiltinStatementKeywords;
            target[keyword] = count;
        }

        return report;
    }

    /// <summary>Whether a statement keyword is implemented as a built-in instruction.</summary>
    public static bool IsImplementedStatementKeyword(string keyword) =>
        ImplementedStatementKeywordSet.Contains(keyword);

    /// <summary>Whether an action body directly references both player input and velocity skills.</summary>
    public static bool ReferencesPlayerMovement(WdlBlock body)
    {
        ArgumentNullException.ThrowIfNull(body);

        bool input = false;
        bool velocity = false;
        InspectMovementReferences(body, ref input, ref velocity);
        return input && velocity;
    }

    private static void InspectMovementReferences(WdlBlock body, ref bool input, ref bool velocity)
    {
        foreach (WdlItem item in body.Items)
        {
            foreach (WdlToken token in item.Header)
            {
                if (token.Kind != WdlTokenKind.Identifier)
                {
                    continue;
                }

                input |= token.Text.StartsWith("FORCE_", StringComparison.OrdinalIgnoreCase);
                velocity |= token.Text.StartsWith("PLAYER_V", StringComparison.OrdinalIgnoreCase);
            }

            if (item.Body is { } child)
            {
                InspectMovementReferences(child, ref input, ref velocity);
            }
        }
    }

    private static void CountDeclarationBody(WdlItem item, WdlCoverageReport report)
    {
        CountDeclarationSurface(item, report);
        if (item.Body is not { } body)
        {
            return;
        }

        foreach (WdlItem child in body.Items)
        {
            CountDeclarationBody(child, report);
        }
    }

    private static void CountDeclarationSurface(WdlItem item, WdlCoverageReport report)
    {
        if (item.IsLabel)
        {
            return;
        }

        if (IsActionHookKeyword(item.Keyword))
        {
            Increment(report.ActionHookDeclarations, item.Keyword);
        }

        if (IsSchedulerKeyword(item.Keyword))
        {
            Increment(report.SchedulerDeclarations, item.Keyword);
        }
    }

    private static void CountActionBody(WdlBlock body, WdlCoverageReport report)
    {
        foreach (WdlItem item in body.Items)
        {
            if (!item.IsLabel)
            {
                Increment(report.ActionStatementKeywords, item.Keyword);
                CountActionStatementSurface(item, report);
                CountPredefinedSkillReferences(item.Header, report);
                CountPlayerMovementSkillReferences(item.Header, report);
            }

            if (item.Body is { } child)
            {
                CountActionBody(child, report);
            }
        }
    }

    private static void CountActionStatementSurface(WdlItem item, WdlCoverageReport report)
    {
        if (!item.Keyword.Equals("SET", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        int comma = IndexOfKind(item.Header, WdlTokenKind.Comma);
        if (comma <= 0)
        {
            return;
        }

        if (item.Header[0].Kind == WdlTokenKind.Identifier &&
            IsActionHookKeyword(item.Header[0].Text))
        {
            Increment(report.ActionHookReassignments, item.Header[0].Text);
            return;
        }

        if (IsSchedulerSlotSetTarget(item.Header, comma))
        {
            Increment(report.SchedulerReassignments, item.Header[0].Text.ToUpperInvariant());
        }
    }

    private static void CountPredefinedSkillReferences(IReadOnlyList<WdlToken> tokens, WdlCoverageReport report)
    {
        foreach (WdlToken token in tokens)
        {
            if (token.Kind == WdlTokenKind.Identifier &&
                PredefinedRuntimeSkillSet.Contains(token.Text))
            {
                Increment(report.PredefinedSkillReferences, token.Text);
            }
        }
    }

    private static void CountPlayerMovementSkillReferences(IReadOnlyList<WdlToken> tokens, WdlCoverageReport report)
    {
        foreach (WdlToken token in tokens)
        {
            if (token.Kind == WdlTokenKind.Identifier &&
                PlayerMovementSkillSet.Contains(token.Text))
            {
                Increment(report.PlayerMovementSkillReferences, token.Text);
            }
        }
    }

    private static bool IsSchedulerSlotSetTarget(IReadOnlyList<WdlToken> header, int comma)
    {
        if (header.Count == 0 ||
            header[0].Kind != WdlTokenKind.Identifier ||
            !(header[0].Text.Equals("EACH_TICK", StringComparison.OrdinalIgnoreCase) ||
              header[0].Text.Equals("EACH_SEC", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return comma == 2 && header[1].Kind == WdlTokenKind.Number && header[1].Text.StartsWith('.') ||
               comma == 3 && header[1] is { Kind: WdlTokenKind.Operator, Text: "." } &&
               header[2].Kind == WdlTokenKind.Number;
    }

    private static bool IsActionHookKeyword(string keyword) =>
        keyword.StartsWith("IF_", StringComparison.OrdinalIgnoreCase);

    private static bool IsSchedulerKeyword(string keyword) =>
        keyword.Equals("EACH_TICK", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("EACH_SEC", StringComparison.OrdinalIgnoreCase) ||
        keyword.Equals("EACH_CYCLE", StringComparison.OrdinalIgnoreCase);

    private static int IndexOfKind(IReadOnlyList<WdlToken> tokens, WdlTokenKind kind)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == kind)
            {
                return i;
            }
        }

        return -1;
    }

    private static void Increment(Dictionary<string, int> counts, string keyword)
    {
        string normalized = keyword.ToUpperInvariant();
        counts[normalized] = counts.GetValueOrDefault(normalized) + 1;
    }
}

/// <summary>Sanitized WDL runtime coverage counts.</summary>
public sealed class WdlCoverageReport
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    /// <summary>Number of ACTION bodies analyzed.</summary>
    public int ActionCount { get; set; }

    /// <summary>Flattened top-level declaration/item keyword counts.</summary>
    public Dictionary<string, int> DeclarationKeywords { get; } = new(Ci);

    /// <summary>Recursive statement keyword counts inside ACTION bodies.</summary>
    public Dictionary<string, int> ActionStatementKeywords { get; } = new(Ci);

    /// <summary>ACTION-body statement keywords implemented as built-in instructions.</summary>
    public Dictionary<string, int> ImplementedStatementKeywords { get; } = new(Ci);

    /// <summary>ACTION-body statement keywords not implemented as built-in instructions.</summary>
    public Dictionary<string, int> NonBuiltinStatementKeywords { get; } = new(Ci);

    /// <summary>IF_* hook declarations in top-level or object/type declaration surfaces.</summary>
    public Dictionary<string, int> ActionHookDeclarations { get; } = new(Ci);

    /// <summary>IF_* hook reassignment targets found in ACTION-body SET statements.</summary>
    public Dictionary<string, int> ActionHookReassignments { get; } = new(Ci);

    /// <summary>EACH_TICK/EACH_SEC/EACH_CYCLE declarations in top-level or object/type declaration surfaces.</summary>
    public Dictionary<string, int> SchedulerDeclarations { get; } = new(Ci);

    /// <summary>Scheduler reassignment targets found in ACTION-body SET statements.</summary>
    public Dictionary<string, int> SchedulerReassignments { get; } = new(Ci);

    /// <summary>Predefined runtime skill references found in ACTION-body statement headers.</summary>
    public Dictionary<string, int> PredefinedSkillReferences { get; } = new(Ci);

    /// <summary>Player input and velocity skill references found in ACTION-body statement headers.</summary>
    public Dictionary<string, int> PlayerMovementSkillReferences { get; } = new(Ci);

    /// <summary>Merges another report into this report.</summary>
    public void Add(WdlCoverageReport other)
    {
        ArgumentNullException.ThrowIfNull(other);

        ActionCount += other.ActionCount;
        AddCounts(DeclarationKeywords, other.DeclarationKeywords);
        AddCounts(ActionStatementKeywords, other.ActionStatementKeywords);
        AddCounts(ImplementedStatementKeywords, other.ImplementedStatementKeywords);
        AddCounts(NonBuiltinStatementKeywords, other.NonBuiltinStatementKeywords);
        AddCounts(ActionHookDeclarations, other.ActionHookDeclarations);
        AddCounts(ActionHookReassignments, other.ActionHookReassignments);
        AddCounts(SchedulerDeclarations, other.SchedulerDeclarations);
        AddCounts(SchedulerReassignments, other.SchedulerReassignments);
        AddCounts(PredefinedSkillReferences, other.PredefinedSkillReferences);
        AddCounts(PlayerMovementSkillReferences, other.PlayerMovementSkillReferences);
    }

    private static void AddCounts(Dictionary<string, int> target, IReadOnlyDictionary<string, int> source)
    {
        foreach ((string key, int count) in source)
        {
            target[key] = target.GetValueOrDefault(key) + count;
        }
    }
}
