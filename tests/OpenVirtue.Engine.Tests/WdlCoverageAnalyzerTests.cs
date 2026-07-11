// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Formats.Wdl;

namespace OpenVirtue.Engine.Tests;

public class WdlCoverageAnalyzerTests
{
    [Fact]
    public void Analyze_CountsDeclarationsStatementsHooksSchedulersAndRuntimeSkills()
    {
        const string source =
            """
            SKILL health { VAL 100; }
            IF_START boot;
            EACH_TICK tick;
            REGION room
            {
                IF_ENTER entered;
                EACH_CYCLE spin;
            }
            ACTION boot
            {
                SET TIME_CORR, 1;
                WAIT 1;
                IF (TIME_CORR > 0)
                {
                    RULE PLAYER_VX = FORCE_AHEAD * PLAYER_COS;
                    RULE PLAYER_VY = FORCE_AHEAD * PLAYER_SIN;
                    ACCEL PLAYER_VROT, FORCE_ROT;
                    SET FRICTION, TIME_FAC;
                }
                SET IF_F1, tick;
                SET EACH_TICK.2, tick;
            }
            """;

        WdlCoverageReport report = Analyze(source);
        WdlProgram program = WdlPreprocessor.Flatten(source, _ => null);

        Assert.Equal(1, report.ActionCount);
        Assert.Equal(1, report.DeclarationKeywords["SKILL"]);
        Assert.Equal(1, report.DeclarationKeywords["IF_START"]);
        Assert.Equal(4, report.ActionStatementKeywords["SET"]);
        Assert.Equal(1, report.ActionStatementKeywords["WAIT"]);
        Assert.Equal(1, report.ActionStatementKeywords["IF"]);
        Assert.Equal(2, report.ActionStatementKeywords["RULE"]);
        Assert.Equal(1, report.ActionStatementKeywords["ACCEL"]);
        Assert.Equal(2, report.ActionHookDeclarations.Count);
        Assert.Equal(1, report.ActionHookDeclarations["IF_START"]);
        Assert.Equal(1, report.ActionHookDeclarations["IF_ENTER"]);
        Assert.Equal(1, report.ActionHookReassignments["IF_F1"]);
        Assert.Equal(1, report.SchedulerDeclarations["EACH_TICK"]);
        Assert.Equal(1, report.SchedulerDeclarations["EACH_CYCLE"]);
        Assert.Equal(1, report.SchedulerReassignments["EACH_TICK"]);
        Assert.Equal(2, report.PredefinedSkillReferences["TIME_CORR"]);
        Assert.Equal(1, report.PredefinedSkillReferences["TIME_FAC"]);
        Assert.Equal(1, report.PlayerMovementSkillReferences["PLAYER_VX"]);
        Assert.Equal(1, report.PlayerMovementSkillReferences["PLAYER_VY"]);
        Assert.Equal(2, report.PlayerMovementSkillReferences["FORCE_AHEAD"]);
        Assert.Equal(1, report.PlayerMovementSkillReferences["PLAYER_COS"]);
        Assert.Equal(1, report.PlayerMovementSkillReferences["PLAYER_SIN"]);
        Assert.Equal(1, report.PlayerMovementSkillReferences["PLAYER_VROT"]);
        Assert.Equal(1, report.PlayerMovementSkillReferences["FORCE_ROT"]);
        Assert.Equal(1, report.PlayerMovementSkillReferences["FRICTION"]);
        Assert.True(WdlCoverageAnalyzer.ReferencesPlayerMovement(program.Items.Single(item => item.Keyword == "ACTION").Body!));
    }

    [Fact]
    public void Analyze_ClassifiesKnownImplementedStatementsAndUnknownStatements()
    {
        const string source =
            """
            ACTION boot
            {
                SET health, 100;
                RULE health -= 1;
                IF_EQUAL health, 99;
                ENDIF;
                IF_MIN health;
                ENDIF;
                SKIP 1;
                BRANCH nextAction;
                LEVEL next_level;
                EXIT;
                PLAY_SOUND alert, 0.5;
                PLAY_SOUNDFILE <DIRECT.WAV>, 1;
                STOP_SOUND;
                FADE_PAL dusk, 0.5;
                LOCATE player;
                SHAKE player;
                DROP pickup;
                SHOOT pickup;
                PUSH 4;
                EXPLODE barrel;
                SHIFT room, 1, 2;
                LIFT room, 3;
                ROTATE room, 0.5;
                BEEP;
                PLAY_FLICFILE <INTRO.FLC>;
                STOP_FLIC;
                SCREENSHOT "snap", 1;
                INKEY player_name;
                ACCEL PLAYER_VX, force_x;
                ADD score, 1;
                ADDT drift, 2;
                RANDOMIZE;
                SAVE "game", 1;
                LOAD "game", 1;
                SAVE_INFO "game", 1;
                LOAD_INFO "game", 1;
                WAIT 1;
                WAITT 16;
                UNKNOWN_INSTRUCTION health;
            }
            """;

        WdlCoverageReport report = Analyze(source);

        Assert.Equal(1, report.ImplementedStatementKeywords["SET"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["RULE"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["IF_EQUAL"]);
        Assert.Equal(2, report.ImplementedStatementKeywords["ENDIF"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["IF_MIN"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["SKIP"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["BRANCH"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["LEVEL"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["EXIT"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["PLAY_SOUND"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["PLAY_SOUNDFILE"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["STOP_SOUND"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["FADE_PAL"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["LOCATE"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["SHAKE"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["DROP"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["SHOOT"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["PUSH"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["EXPLODE"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["SHIFT"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["LIFT"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["ROTATE"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["BEEP"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["PLAY_FLICFILE"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["STOP_FLIC"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["SCREENSHOT"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["INKEY"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["ACCEL"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["ADD"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["ADDT"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["RANDOMIZE"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["SAVE"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["LOAD"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["SAVE_INFO"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["LOAD_INFO"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["WAIT"]);
        Assert.Equal(1, report.ImplementedStatementKeywords["WAITT"]);
        Assert.DoesNotContain("SET", report.NonBuiltinStatementKeywords.Keys);
        Assert.Equal(1, report.NonBuiltinStatementKeywords["UNKNOWN_INSTRUCTION"]);
    }

    [Fact]
    public void Analyze_DoesNotCountInactiveActionBodyPreprocessorBranchesAfterFlattening()
    {
        const string source =
            """
            DEFINE ACTIVE;
            ACTION boot
            {
                IFDEF ACTIVE;
                    SET x, 1;
                IFELSE;
                    UNKNOWN_INACTIVE x;
                ENDIF;
            }
            """;

        WdlCoverageReport report = Analyze(source);

        Assert.Equal(1, report.ActionStatementKeywords["SET"]);
        Assert.DoesNotContain("IFDEF", report.ActionStatementKeywords.Keys);
        Assert.DoesNotContain("IFELSE", report.ActionStatementKeywords.Keys);
        Assert.DoesNotContain("ENDIF", report.ActionStatementKeywords.Keys);
        Assert.DoesNotContain("UNKNOWN_INACTIVE", report.ActionStatementKeywords.Keys);
    }

    private static WdlCoverageReport Analyze(string source)
    {
        WdlProgram program = WdlPreprocessor.Flatten(source, _ => null);
        return WdlCoverageAnalyzer.Analyze(program);
    }
}
