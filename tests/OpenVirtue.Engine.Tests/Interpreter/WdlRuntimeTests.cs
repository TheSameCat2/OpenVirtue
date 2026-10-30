// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Numerics;
using System.Text.Json;
using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Engine.Tests.TestSupport;
using OpenVirtue.Formats.Save;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Engine.Tests.Interpreter;

public class WdlRuntimeTests
{
    [Fact]
    public void Runtime_PredefinedLightingSkills_UseDocumentedDefaults()
    {
        WdlRuntime runtime = new(Load("MAPFILE <m.wmp>;"));

        Assert.Equal(1, runtime.GetSkill("PLAYER_LIGHT"));
        Assert.Equal(10, runtime.GetSkill("LIGHT_DIST"));
        Assert.Equal(1, runtime.GetSkill("PLAYER_ARC"));
        Assert.Equal(1, runtime.GetSkill("ASPECT"));
        Assert.Equal(1, runtime.GetSkill("SOUND_VOL"));
        Assert.Equal(0.5, runtime.GetSkill("MUSIC_VOL")); // V3.963's start values (scheduler-order round 4)
        Assert.Equal(0, runtime.GetSkill("MOUSE_MODE"));
        Assert.Equal(1, runtime.GetSkill("TOUCH_MODE"));
        Assert.Equal(0, runtime.GetSkill("MOUSE_MOVING"));
        Assert.Equal(1, runtime.GetSkill("MOUSE_CALM"));
        Assert.Equal(4, runtime.GetSkill("MOUSE_TIME"));
        Assert.Equal(0, runtime.GetSkill("MICKEY_X"));
        Assert.Equal(0, runtime.GetSkill("MICKEY_Y"));
        Assert.Equal(4, runtime.GetSkill("WALK_PERIOD"));
        Assert.Equal(4, runtime.GetSkill("WALK_TIME"));
        Assert.Equal(20, runtime.GetSkill("WAVE_PERIOD"));
        Assert.Equal(3.5, runtime.GetSkill("PLAYER_SIZE"));
        Assert.Equal(2, runtime.GetSkill("PLAYER_CLIMB"));
        Assert.Equal(1, runtime.GetSkill("KEY_SENSE"));
        Assert.Equal(1, runtime.GetSkill("BUFFER_MODE")); // a 0 here slows the original after a load
        Assert.Equal(3216 / 1024.0, runtime.GetSkill("PI"));
        Assert.Equal(0, runtime.GetSkill("WALK"));
        Assert.Equal(0, runtime.GetSkill("WAVE"));
        Assert.Equal(1, runtime.GetSkill("PSOUND_VOL"));
        Assert.Equal(1, runtime.GetSkill("PSOUND_TONE"));
        Assert.Equal(0, runtime.GetSkill("CHANNEL")); // OV-20261029-audio
        Assert.Equal(1000, runtime.GetSkill("DARK_DIST")); // OV-20261029-shading
        for (int channel = 0; channel < 8; channel++)
        {
            Assert.Equal(0, runtime.GetSkill($"CHANNEL_{channel}"));
        }
    }

    [Fact]
    public void Runtime_PredefinedDefaults_FillDeclarationsWithoutValButPreserveExplicitZero()
    {
        WdlRuntime runtime = new(Load(
            "MAPFILE <m.wmp>; SKILL FRICTION; SKILL INERTIA { VAL 0; } SKILL PLAYER_ARC;"));

        Assert.Equal(0, runtime.GetSkill("FRICTION")); // not the manual's 0.5 (OV-20261029-motion-timing)
        Assert.Equal(0, runtime.GetSkill("INERTIA"));
        Assert.Equal(1, runtime.GetSkill("PLAYER_ARC"));
    }

    private const string MinimalMap =
        "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\nPLAYER_START 0 0 0 0;#1";

    private const string TwoRoomMap =
        "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nVERTEX 10 10 0;#2\nVERTEX 0 10 0;#3\n" +
        "VERTEX 20 0 0;#4\nVERTEX 20 10 0;#5\n" +
        "REGION left 0 10;#0\nREGION right 0 10;#1\n" +
        "WALL left_bottom 0 1 0 -1 0 0;#0\nWALL portal 1 2 0 1 0 0;#1\n" +
        "WALL left_top 2 3 0 -1 0 0;#2\nWALL left_side 3 0 0 -1 0 0;#3\n" +
        "WALL right_bottom 1 4 1 -1 0 0;#4\nWALL right_side 4 5 1 -1 0 0;#5\n" +
        "WALL right_top 5 2 1 -1 0 0;#6\n" +
        "THING pickup 5 5 0 0;#7\nPLAYER_START 5 5 0 0;#8";

    [Fact]
    public void Runtime_SeedsSkillsFromLevel_AndRunsActions()
    {
        Level level = Load("MAPFILE <m.wmp>; SKILL health { VAL 100; } ACTION hurt { SET health, 50; }");
        var runtime = new WdlRuntime(level);

        Assert.Equal(100, runtime.GetSkill("health")); // seeded from the level's declared skill

        Assert.True(runtime.RunAction("hurt"));
        Assert.Equal(50, runtime.GetSkill("health"));   // the action mutated it
    }

    [Fact]
    public void Runtime_SetUiMember_MovesAndShowsPanel()
    {
        Level level = Load(
            """
            MAPFILE <m.wmp>;
            SKILL targetX { VAL 12; }
            BMAP panelMap, <panel.pcx>, 0, 0, 80, 20;
            PANEL menu { PAN_MAP panelMap; POS_X 1; POS_Y 2; }
            ACTION openMenu { SET menu.POS_X, targetX; RULE menu.POS_Y += 3; SET menu.VISIBLE, 1; }
            """);
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("openMenu"));

        WdlUiElement menu = runtime.UiElements["menu"];
        Assert.Equal(12, UiNumber(menu, "POS_X"));
        Assert.Equal(5, UiNumber(menu, "POS_Y"));
        Assert.True(UiFlag(menu, "VISIBLE"));

        UiOverlaySprite sprite = Assert.Single(UiOverlayBuilder.Build(level, runtime.UiElements));
        Assert.Equal(12, sprite.X);
        Assert.Equal(5, sprite.Y);
    }

    [Fact]
    public void Runtime_ReadUiMember_ReturnsTextPositionAndVisibility()
    {
        Level level = Load(
            """
            MAPFILE <m.wmp>;
            STRING line, "New Game";
            SKILL pos { VAL 1; }
            SKILL arrowY { VAL 0; }
            SKILL shown { VAL 0; }
            TEXT item1 { POS_Y 3; STRING line; FLAGS VISIBLE; }
            ACTION placeArrow { SET item1.POS_Y, 102; IF (pos == 1) { SET arrowY, item1.POS_Y; } SET shown, item1.VISIBLE + 1; }
            """);
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("placeArrow"));

        Assert.Equal(102, runtime.GetSkill("arrowY"));
        Assert.Equal(2, runtime.GetSkill("shown"));
    }

    [Fact]
    public void Runtime_SetUiMember_ResolvesPanelSynonymAndCanClearVisible()
    {
        Level level = Load(
            """
            MAPFILE <m.wmp>;
            BMAP panelMap, <panel.pcx>, 0, 0, 80, 20;
            PANEL menu { PAN_MAP panelMap; FLAGS VISIBLE; }
            SYNONYM activePanel { TYPE PANEL; DEFAULT menu; }
            ACTION closeMenu { SET activePanel.VISIBLE, 0; }
            """);
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("closeMenu"));

        Assert.False(UiFlag(runtime.UiElements["menu"], "VISIBLE"));
        Assert.Empty(UiOverlayBuilder.Build(level, runtime.UiElements));
    }

    [Fact]
    public void Runtime_SetUiKeywordMember_ChangesTextStringShownByBuilder()
    {
        Level level = Load(
            """
            MAPFILE <m.wmp>;
            STRING oldLine,"old";
            STRING newLine,"new";
            FONT menuFont, <menu.pcx>, 8, 8;
            TEXT prompt { FONT menuFont; STRING oldLine; FLAGS VISIBLE; }
            ACTION swap { SET prompt.STRING, newLine; }
            """);
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("swap"));

        Assert.Equal("newLine", UiKeyword(runtime.UiElements["prompt"], "STRING"));
        Assert.Equal(
            "new",
            new string(UiTextBuilder.Build(level, runtime.Strings, runtime.Skills, runtime.UiElements)
                .Select(glyph => (char)glyph.CodePoint)
                .ToArray()));
    }

    [Fact]
    public void Runtime_SetUiKeywordMember_UsesTextIndexSlot()
    {
        Level level = Load(
            """
            MAPFILE <m.wmp>;
            STRING first,"A";
            STRING second,"B";
            STRING third,"C";
            STRING replacement,"Z";
            FONT menuFont, <menu.pcx>, 8, 8;
            TEXT prompt { FONT menuFont; STRINGS 3; STRING first,second,third; INDEX 2; FLAGS VISIBLE; }
            ACTION swap { SET prompt.STRING, replacement; }
            """);
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("swap"));

        Assert.Equal(["first", "replacement", "third"], UiKeywords(runtime.UiElements["prompt"], "STRING"));
        Assert.Equal(
            ['A', 'Z', 'C'],
            UiTextBuilder.Build(level, runtime.Strings, runtime.Skills, runtime.UiElements)
                .Select(glyph => (char)glyph.CodePoint)
                .ToArray());
    }

    [Fact]
    public void Runtime_SetUiKeywordMember_CopiesActiveTextIndexSlot()
    {
        Level level = Load(
            """
            MAPFILE <m.wmp>;
            STRING first,"A";
            STRING second,"B";
            STRING oldLine,"old";
            FONT menuFont, <menu.pcx>, 8, 8;
            TEXT source { FONT menuFont; STRINGS 2; STRING first,second; INDEX 2; FLAGS VISIBLE; }
            TEXT target { FONT menuFont; STRING oldLine; FLAGS VISIBLE; }
            ACTION copy { SET target.STRING, source.STRING; }
            """);
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("copy"));

        Assert.Equal("second", UiKeyword(runtime.UiElements["target"], "STRING"));
    }

    [Fact]
    public void Runtime_SetUiKeywordMember_ResolvesTextSynonym()
    {
        Level level = Load(
            """
            MAPFILE <m.wmp>;
            STRING oldLine,"old";
            STRING newLine,"new";
            FONT menuFont, <menu.pcx>, 8, 8;
            TEXT prompt { FONT menuFont; STRING oldLine; FLAGS VISIBLE; }
            SYNONYM activeText { TYPE TEXT; DEFAULT prompt; }
            ACTION swap { SET activeText.STRING, newLine; }
            """);
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("swap"));

        Assert.Equal("newLine", UiKeyword(runtime.UiElements["prompt"], "STRING"));
    }

    [Fact]
    public void Runtime_SetUiKeywordMember_ChangesOverlayBitmapShownByBuilder()
    {
        Level level = Load(
            """
            MAPFILE <m.wmp>;
            BMAP oldMap, <old.pcx>, 0, 0, 10, 20;
            BMAP newMap, <new.pcx>, 0, 0, 30, 40;
            OVERLAY logo { BMAP oldMap; FLAGS VISIBLE; }
            ACTION swap { SET logo.BMAP, newMap; }
            """);
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("swap"));

        UiOverlaySprite sprite = Assert.Single(UiOverlayBuilder.Build(level, runtime.UiElements));
        Assert.Equal("newMap", sprite.Bitmap);
        Assert.Equal(30, sprite.Width);
        Assert.Equal(40, sprite.Height);
    }

    [Fact]
    public void Runtime_ActionUsingRuleAndIf()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL x { VAL 10; } SKILL y { VAL 0; } " +
            "ACTION step { RULE x += 5; IF (x > 12) { SET y, 1; } }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("step");

        Assert.Equal(15, runtime.GetSkill("x"));
        Assert.Equal(1, runtime.GetSkill("y"));
    }

    [Fact]
    public void Runtime_ActionCallsAnotherActionByName()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL x { VAL 0; } ACTION outer { inner; } ACTION inner { SET x, 7; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("outer"); // 'outer' body is just the statement `inner;`

        Assert.Equal(7, runtime.GetSkill("x"));
    }

    [Fact]
    public void Runtime_CallInstruction_CallsNamedAction()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL x { VAL 0; } ACTION outer { CALL inner; SET x, x + 1; } ACTION inner { SET x, 7; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("outer");

        Assert.Equal(8, runtime.GetSkill("x"));
    }

    // OV-20261015-branch-tail-jump: BRANCH is a tail jump; nothing after it in the branching action runs.
    [Fact]
    public void Runtime_BranchInstruction_SkipsRestOfBranchingAction()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL x { VAL 0; } ACTION outer { BRANCH inner; SET x, x + 1; } ACTION inner { SET x, 7; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("outer");

        Assert.Equal(7, runtime.GetSkill("x"));
    }

    [Fact]
    public void Runtime_Branch_CallerOfBranchingActionResumes()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL trace { VAL 0; } " +
            "ACTION tgt { RULE trace = trace * 10 + 3; } " +
            "ACTION src { RULE trace = trace * 10 + 1; BRANCH tgt; RULE trace = trace * 10 + 2; } " +
            "ACTION start { CALL src; RULE trace = trace * 10 + 9; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("start");

        Assert.Equal(139, runtime.GetSkill("trace"));
    }

    [Fact]
    public void Runtime_Branch_WaitInTarget_ResumesCallerAndFinishesTargetLater()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL trace { VAL 0; } " +
            "ACTION tgt { RULE trace = trace * 10 + 3; WAIT 1; RULE trace = trace * 10 + 4; } " +
            "ACTION src { RULE trace = trace * 10 + 1; BRANCH tgt; RULE trace = trace * 10 + 2; } " +
            "ACTION start { CALL src; RULE trace = trace * 10 + 9; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("start");
        Assert.Equal(139, runtime.GetSkill("trace"));
        Assert.Equal(1, runtime.WaitingActionCount);

        // The WAIT 1 counts the frame it was armed before and resumes in the next one.
        runtime.Tick(1.0 / 16);
        Assert.Equal(139, runtime.GetSkill("trace"));
        runtime.Tick(1.0 / 16);
        Assert.Equal(1394, runtime.GetSkill("trace"));
        Assert.Equal(0, runtime.WaitingActionCount);
    }

    [Fact]
    public void Runtime_Branch_ThroughAssignedActionSynonym_JumpsToTarget()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL trace { VAL 0; } SYNONYM selected { TYPE ACTION; } " +
            "ACTION tgt { RULE trace = trace * 10 + 3; } " +
            "ACTION src { RULE trace = trace * 10 + 1; BRANCH selected; RULE trace = trace * 10 + 2; } " +
            "ACTION start { SET selected, tgt; CALL src; RULE trace = trace * 10 + 9; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("start");

        Assert.Equal(139, runtime.GetSkill("trace"));
    }

    [Fact]
    public void Runtime_Branch_ThroughNullActionSynonym_IsSkipped()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL trace { VAL 0; } SYNONYM selected { TYPE ACTION; DEFAULT tgt; } " +
            "ACTION tgt { RULE trace = trace * 10 + 3; } " +
            "ACTION src { RULE trace = trace * 10 + 1; BRANCH selected; RULE trace = trace * 10 + 2; } " +
            "ACTION start { SET selected, NULL; CALL src; RULE trace = trace * 10 + 9; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("start");

        Assert.Equal(129, runtime.GetSkill("trace"));
    }

    [Fact]
    public void Runtime_Branch_InsideIfAndWhile_LeavesBlockLoopAndAction()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL trace { VAL 0; } SKILL z { VAL 0; } SKILL n { VAL 0; } " +
            "ACTION tgt { RULE trace = trace * 10 + 3; } " +
            "ACTION src { IF (z == 0) { BRANCH tgt; RULE trace = trace * 10 + 2; } RULE trace = trace * 10 + 5; } " +
            "ACTION loop { WHILE (n < 3) { RULE n += 1; BRANCH tgt; RULE trace = trace * 10 + 6; } RULE trace = trace * 10 + 7; } " +
            "ACTION start { CALL src; CALL loop; RULE trace = trace * 10 + 9; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("start");

        Assert.Equal(339, runtime.GetSkill("trace"));
        Assert.Equal(1, runtime.GetSkill("n"));
    }

    // The title menu's shape (issue #7): the first target moves the compared skill, and the
    // IF_EQUAL chain must not be re-entered, or New Game would also run the fourth entry.
    [Fact]
    public void Runtime_Branch_LegacyIfEqualChain_DoesNotReenterAfterTargetChangesSkill()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL trace { VAL 0; } SKILL pos { VAL 1; } " +
            "ACTION first { RULE trace = trace * 10 + 1; SET pos, 4; } " +
            "ACTION fourth { RULE trace = trace * 10 + 4; } " +
            "ACTION menu { IF_EQUAL pos, 1; BRANCH first; IF_EQUAL pos, 4; BRANCH fourth; RULE trace = trace * 10 + 2; } " +
            "ACTION start { CALL menu; RULE trace = trace * 10 + 9; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("start");

        Assert.Equal(19, runtime.GetSkill("trace"));
        Assert.Equal(4, runtime.GetSkill("pos"));
    }

    [Fact]
    public void Runtime_Branch_ThousandChainedBranches_DoNotNest()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL n { VAL 0; } " +
            "ACTION loop { RULE n += 1; IF (n < 1000) { BRANCH loop; } }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("loop");

        Assert.Equal(1000, runtime.GetSkill("n"));
    }

    [Fact]
    public void Runtime_Branch_FromEachTickAction_SkipsRestOfTickAction()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL trace { VAL 0; } " +
            "ACTION tgt { RULE trace = trace * 10 + 3; } " +
            "ACTION tick { SET EACH_TICK.1, NULL; RULE trace = trace * 10 + 1; BRANCH tgt; RULE trace = trace * 10 + 2; } " +
            "ACTION boot { SET EACH_TICK.1, tick; } " +
            "IF_START boot;");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunStartup());
        runtime.Tick(1.0 / 16);

        Assert.Equal(13, runtime.GetSkill("trace"));
    }

    [Fact]
    public void Runtime_EndInstruction_TerminatesAction()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL x { VAL 0; } ACTION stopEarly { SET x, 1; END; SET x, 2; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("stopEarly");

        Assert.Equal(1, runtime.GetSkill("x"));
    }

    [Fact]
    public void Runtime_ExitInstruction_RequestsExitAndTerminatesAction()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL reached { VAL 0; } STRING bye,\"done\"; ACTION quit { EXIT bye; SET reached, 1; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("quit"));

        Assert.True(runtime.ExitRequested);
        Assert.Equal("done", runtime.ExitMessage);
        Assert.Equal(0, runtime.GetSkill("reached"));
    }

    [Fact]
    public void Runtime_LevelInstruction_RequestsLevelChangeAndTerminatesAction()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL reached { VAL 0; } ACTION change { LEVEL <NEXT.WDL>,\"NEXT.WRS\"; SET reached, 1; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("change"));

        Assert.Equal("NEXT.WDL", runtime.RequestedLevelName);
        Assert.Equal("NEXT.WRS", runtime.RequestedLevelResource);
        Assert.Equal(0, runtime.GetSkill("reached"));
    }

    [Fact]
    public void Runtime_MapInstruction_RequestsMapChangeAndTerminatesAction()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL reached { VAL 0; } ACTION changeMap { MAP <OTHER.WMP>; SET reached, 1; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("changeMap"));

        Assert.Equal("OTHER.WMP", runtime.RequestedMapName);
        Assert.Equal(0, runtime.GetSkill("reached"));
    }

    [Fact]
    public void Runtime_SoundDeclarations_AreAvailableOnLevel()
    {
        Level level = Load("MAPFILE <m.wmp>; SOUND alert,<ALERT.WAV>; SOUND ambient,\"AMBIENT.WAV\";");

        Assert.Equal("ALERT.WAV", level.Sounds["alert"]);
        Assert.Equal("AMBIENT.WAV", level.Sounds["ambient"]);
    }

    [Fact]
    public void Runtime_PlaySound_RecordsDeclaredSoundWithNumericBalance()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SOUND alert,<ALERT.WAV>; SKILL vol { VAL 0.5; } " +
            "ACTION play { PLAY_SOUND alert, vol, -0.25; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("play"));

        WdlSoundRequest request = Assert.Single(runtime.SoundRequests);
        Assert.Equal("alert", request.SoundName);
        Assert.Equal("ALERT.WAV", request.FileName);
        Assert.Equal(0.5, request.Volume);
        Assert.Equal(-0.25, request.Balance);
        Assert.Null(request.EmitterName);
        Assert.False(request.Streamed);
        Assert.Equal(0, request.Channel);
        Assert.Equal(0, runtime.GetSkill("CHANNEL"));
        Assert.Equal(1, runtime.GetSkill("CHANNEL_0"));
        WdlAudioRequest audio = Assert.Single(runtime.AudioRequests);
        Assert.Equal(WdlSoundOperation.Play, audio.Operation);
        Assert.Equal(request, audio.Sound);
    }

    [Fact]
    public void Runtime_SoundChannels_NinthSoundTakesTheOldestChannel()
    {
        // OV-20261029-audio: nine sounds in one cycle take channels 0-7, then 0 again; one cycle
        // apart, the ninth and tenth take channels 0 and 1.
        Level level = Load("MAPFILE <m.wmp>; SOUND alert,<ALERT.WAV>;");
        var runtime = new WdlRuntime(level);

        for (int i = 0; i < 8; i++)
        {
            runtime.PlaySound("alert", null, 1, null, null);
            Assert.Equal(i, runtime.GetSkill("CHANNEL"));
        }

        runtime.PlaySound("alert", null, 1, null, null);
        Assert.Equal(0, runtime.GetSkill("CHANNEL"));
        Assert.Equal(0, runtime.AudioRequests[^1].Sound.Channel);
        runtime.PlaySound("alert", null, 1, null, null);
        Assert.Equal(1, runtime.GetSkill("CHANNEL"));
        Assert.Equal(10, runtime.AudioRequests.Count);
        Assert.All(Enumerable.Range(0, 8), channel =>
            Assert.Equal(1, runtime.GetSkill($"CHANNEL_{channel}")));
    }

    [Fact]
    public void Runtime_SoundChannels_LoopsAreNeverTakenAndAllLoopsGiveMinusOne()
    {
        Level level = Load("MAPFILE <m.wmp>; SOUND alert,<ALERT.WAV>;");
        var runtime = new WdlRuntime(level);
        runtime.PlayTextureSound("alert", 1, loop: true);
        for (int i = 0; i < 7; i++)
        {
            runtime.PlaySound("alert", null, 1, null, null);
        }

        runtime.PlaySound("alert", null, 1, null, null);

        Assert.Equal(1, runtime.GetSkill("CHANNEL"));
        Assert.Equal(2, runtime.GetSkill("CHANNEL_0"));

        var loops = new WdlRuntime(level);
        for (int i = 0; i < 8; i++)
        {
            loops.PlayTextureSound("alert", 1, loop: true);
        }

        loops.PlaySound("alert", null, 1, null, null);
        Assert.Equal(-1, loops.GetSkill("CHANNEL"));
    }

    [Fact]
    public void Runtime_CompletedSound_FreesItsChannelAtTheNextWorldCycleOnly()
    {
        Level level = Load("MAPFILE <m.wmp>; SOUND alert,<ALERT.WAV>;");
        var runtime = new WdlRuntime(level);
        runtime.PlaySound("alert", null, 1, null, null);
        runtime.PlaySound("alert", null, 1, null, null);

        Assert.True(runtime.CompleteSound(0));
        Assert.False(runtime.CompleteSound(0));
        Assert.Equal(1, runtime.GetSkill("CHANNEL_0"));
        runtime.TickFlic(0.07);
        Assert.Equal(1, runtime.GetSkill("CHANNEL_0"));
        runtime.Tick(1 / 60.0);
        Assert.Equal(0, runtime.GetSkill("CHANNEL_0"));

        runtime.PlaySound("alert", null, 1, null, null);
        Assert.Equal(0, runtime.GetSkill("CHANNEL"));
    }

    [Fact]
    public void Runtime_SoundPlayedOutBeforeACycle_FreesItsChannelInThatCycle()
    {
        // OV-20261029-audio: a quarter-second sound started at 13.438 read free at 17.625, the first
        // cycle after it ended; the host is asked once the cycle's TICKS are set.
        Level level = Load("MAPFILE <m.wmp>; SOUND alert,<ALERT.WAV>; SKILL seen { VAL -1; } ACTION look { SET seen, CHANNEL_0; }");
        double end = double.PositiveInfinity;
        var runtime = new WdlRuntime(level);
        runtime.SoundPlayedOut = channel => channel == 0 && runtime.GetSkill("TICKS") >= end;
        runtime.SetEachTickAction(1, "look");
        runtime.Tick(1 / 16.0);
        runtime.PlaySound("alert", null, 1, null, null);
        end = runtime.GetSkill("TICKS") + 0.5;

        runtime.Tick(0.25 / 16); // TICKS 0.25: still playing
        Assert.Equal(1, runtime.GetSkill("seen"));

        runtime.Tick(0.25 / 16); // TICKS 0.5: played out, and this cycle's action sees it free

        Assert.Equal(0, runtime.GetSkill("seen"));
    }

    [Fact]
    public void Runtime_StopSound_FreesNormalChannelsTwoCyclesLaterAndKeepsLoops()
    {
        // OV-20261029-audio stop-lag and stop-sloop.
        Level level = Load("MAPFILE <m.wmp>; SOUND alert,<ALERT.WAV>;");
        var runtime = new WdlRuntime(level);
        runtime.PlayTextureSound("alert", 1, loop: true);
        runtime.PlaySound("alert", null, 1, null, null);
        runtime.PlaySound("alert", null, 1, null, null);

        runtime.StopSounds();
        runtime.PlaySound("alert", null, 1, null, null);

        Assert.Equal(3, runtime.GetSkill("CHANNEL"));
        Assert.Equal([WdlSoundOperation.StopChannel, WdlSoundOperation.StopChannel], runtime.AudioRequests.Skip(3).Take(2).Select(r => r.Operation));
        runtime.Tick(1 / 60.0);
        Assert.Equal(1, runtime.GetSkill("CHANNEL_1"));
        runtime.Tick(1 / 60.0);
        Assert.Equal(2, runtime.GetSkill("CHANNEL_0"));
        Assert.Equal(0, runtime.GetSkill("CHANNEL_1"));
        Assert.Equal(0, runtime.GetSkill("CHANNEL_2"));
        Assert.Equal(1, runtime.GetSkill("CHANNEL_3"));
    }

    [Theory]
    [InlineData("ACTION play { PLAY_SOUND alert, 0; }")]
    [InlineData("ACTION play { SET SOUND_VOL, 0; PLAY_SOUND alert, 1; }")]
    [InlineData("ACTION play { PLAY_SOUND alert, 1, source; }")]
    public void Runtime_InaudibleSound_IsSkippedAndKeepsChannel(string action)
    {
        // OV-20261029-audio skip-channel: the thing is 25 steps away and its texture has SDIST 5.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING source 25 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SOUND alert,<ALERT.WAV>; BMAP b,<B.PCX>; TEXTURE t { BMAPS b; SDIST 5; } THING source { TEXTURE t; } " + action);
        var runtime = new WdlRuntime(level);
        runtime.PlaySound("alert", null, 1, null, null);
        runtime.PlaySound("alert", null, 1, null, null);

        Assert.True(runtime.RunAction("play"));

        Assert.Equal(2, runtime.SoundRequests.Count);
        Assert.Equal(1, runtime.GetSkill("CHANNEL"));
        Assert.Equal(0, runtime.GetSkill("CHANNEL_2"));
    }

    [Fact]
    public void Runtime_TextureSound_ReservesLoopingChannelAndCanStopIt()
    {
        Level level = Load("MAPFILE <m.wmp>; SOUND ambient,<AMBIENT.WAV>;");
        var runtime = new WdlRuntime(level);

        int channel = runtime.PlayTextureSound("ambient", 0.4, loop: true, balance: -0.25);

        Assert.Equal(0, channel);
        WdlAudioRequest play = Assert.Single(runtime.AudioRequests);
        Assert.Equal(WdlSoundOperation.Play, play.Operation);
        Assert.Equal("AMBIENT.WAV", play.Sound.FileName);
        Assert.Equal(0.4, play.Sound.Volume);
        Assert.Equal(-0.25, play.Sound.Balance);
        Assert.True(play.Sound.Loop);
        Assert.Equal(2, runtime.GetSkill("CHANNEL_0"));

        Assert.True(runtime.StopSoundChannel(channel));
        Assert.False(runtime.StopSoundChannel(channel));
        Assert.Equal(0, runtime.GetSkill("CHANNEL_0"));
        WdlAudioRequest stop = runtime.AudioRequests[^1];
        Assert.Equal(WdlSoundOperation.StopChannel, stop.Operation);
        Assert.Equal(channel, stop.Channel);
    }

    [Fact]
    public void Runtime_RestoreSnapshot_ReleasesHostOnlySoundChannels()
    {
        Level level = Load("MAPFILE <m.wmp>; SOUND alert,<ALERT.WAV>;");
        var runtime = new WdlRuntime(level);
        runtime.PlayTextureSound("alert", 1, loop: true);
        WdlRuntimeSnapshot snapshot = runtime.CaptureSnapshot();

        runtime.RestoreSnapshot(snapshot);

        Assert.All(Enumerable.Range(0, 8), channel =>
            Assert.Equal(0, runtime.GetSkill($"CHANNEL_{channel}")));
    }

    [Fact]
    public void Runtime_RestoreSnapshot_KeepsLocalSkillsAsTheyAreNow()
    {
        // OV-20261029-save-body round 7: after a full LOAD, a LOCAL skill read what it was set to
        // before the load, while the others took the save's values.
        Level level = Load("MAPFILE <m.wmp>; SKILL l { TYPE LOCAL; VAL 0; } SKILL p { VAL 0; }");
        var runtime = new WdlRuntime(level);
        runtime.SetSkill("l", 1);
        runtime.SetSkill("p", 7);
        WdlRuntimeSnapshot snapshot = runtime.CaptureSnapshot();
        runtime.SetSkill("l", 2);
        runtime.SetSkill("p", 9);

        runtime.RestoreSnapshot(snapshot);

        Assert.Equal(2, runtime.GetSkill("l"));
        Assert.Equal(7, runtime.GetSkill("p"));
    }

    [Fact]
    public void Runtime_FullSave_KeepsWaitingActionsButNotTheOneSaving()
    {
        // OV-20261029-save-body round 7: after a LOAD the CALLed action waiting at the SAVE resumed
        // (inside its WHILE), and the action that ran the SAVE did not go on.
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL n { VAL 0; } SKILL after { VAL 0; } SKILL k { VAL 0; } " +
            "ACTION note { WHILE (k < 1) { WAIT 2; RULE k = k + 1; } SET n, 1; } " +
            "ACTION main { CALL note; SAVE \"s\", 1; WAIT 1; SET after, 1; }");
        var runtime = new WdlRuntime(level);
        Assert.True(runtime.RunAction("main"));
        WdlRuntimeSnapshot saved = Assert.Single(runtime.PersistenceRequests).Snapshot!;
        WdlRuntimeSnapshot snapshot = JsonSerializer.Deserialize<WdlRuntimeSnapshot>(JsonSerializer.Serialize(saved))!;
        Assert.Single(snapshot.WaitingActions!);

        var loaded = new WdlRuntime(level);
        loaded.RestoreSnapshot(snapshot);
        for (int i = 0; i < 4; i++)
        {
            loaded.Tick(1.0 / 16);
        }

        Assert.Equal(1, loaded.GetSkill("n"));
        Assert.Equal(0, loaded.GetSkill("after"));
    }

    [Fact]
    public void Runtime_ImportOriginalActions_ResumesAtTheSavedFlatPlaces()
    {
        // OV-20261029-save-body round 8: places count every instruction, IF/ELSE bodies in line and a
        // WHILE's end; actions are numbered from 11 in declaration order.
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL k { VAL 0; } SKILL n { VAL 0; } SKILL a { VAL 0; } SKILL done { VAL 0; } " +
            "SKILL b { VAL 0; } SKILL e { VAL 0; } SKILL ran { VAL 0; } " +
            "ACTION loop { SET a, 1; WHILE (k < 1) { RULE n = n + 1; WAITT 2; RULE k = k + 1; } SET done, 1; } " +
            "ACTION branch { IF (e > 5) { RULE b = 1; } ELSE { RULE b = 2; } WAIT 2; SET e, 9; } " +
            "ACTION saver { SET ran, 1; }");
        var runtime = new WdlRuntime(level);
        int taken = runtime.ImportOriginalActions(
            [
                new SaveAction(11, 1024, 3, 4, -1, -1, Running: false), // loop in WAITT 2: SET 0, WHILE 1, RULE 2, WAITT 3
                new SaveAction(12, 1024, 4, 5, -1, -1, Running: false), // branch in WAIT 2: IF 0, RULE 1, ELSE 2, RULE 3, WAIT 4
                new SaveAction(13, 0, 0, 1, -1, -1, Running: true),
            ],
            null);

        Assert.Equal(2, taken);
        for (int i = 0; i < 6; i++)
        {
            runtime.Tick(1.0 / 16);
        }

        Assert.Equal(0, runtime.GetSkill("a")); // resumed after the SET, inside the loop body
        Assert.Equal(0, runtime.GetSkill("n")); // after the WAITT, not at the body's start
        Assert.Equal(1, runtime.GetSkill("k"));
        Assert.Equal(1, runtime.GetSkill("done"));
        Assert.Equal(0, runtime.GetSkill("b")); // after the IF/ELSE
        Assert.Equal(9, runtime.GetSkill("e"));
        Assert.Equal(0, runtime.GetSkill("ran")); // the action that ran the SAVE does not go on
    }

    [Fact]
    public void Runtime_CaptureOriginalSave_RoundTripsThroughTheOriginalsFormat()
    {
        // OV-20261029-save-body round 9: the original loaded a save written this way.
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL k { VAL 0; } SKILL n { VAL 0; } SKILL l { TYPE LOCAL; VAL 0; } SKILL done { VAL 0; } " +
            "ACTION tick { RULE k = k + 1; } " +
            "ACTION loop { SET n, 1; WHILE (k < 50) { WAITT 2; RULE n = n + 1; } SET done, 1; }");
        var runtime = new WdlRuntime(level);
        runtime.SetEachTickAction(2, "tick");
        Assert.True(runtime.RunAction("loop"));
        runtime.SetSkill("n", 7);
        runtime.SetSkill("l", 3);

        byte[] body = AcknexFullSave.Write(runtime.CaptureOriginalSave("TITLE.WDL", "m.wmp", "TITLE.WRS"));
        var loaded = new WdlRuntime(level);
        loaded.RestoreSavedSkills(AcknexSaveFile.ReadSkills(body), _ => true);
        loaded.ImportOriginalActions(AcknexSaveFile.ReadActions(body)!, AcknexSaveFile.ReadSlots(body));

        Assert.Equal(7, loaded.GetSkill("n"));
        Assert.Equal(0, loaded.GetSkill("l")); // LOCAL skills are not in a full save
        Assert.Equal("tick", loaded.CaptureSnapshot().EachTickActions[1]);
        Assert.Equal(1, loaded.WaitingActionCount); // the loop, at its WAITT inside the WHILE
        for (int i = 0; i < 12; i++)
        {
            loaded.Tick(1.0 / 16);
        }

        Assert.True(loaded.GetSkill("n") > 7); // went on round the loop from the saved WAITT
    }

    [Fact]
    public void Runtime_UnplacedThing_CanBeBroughtInByAScript()
    {
        // Saints of Virtue drops pickups this way: IF (HealthUp1.INVISIBLE == 1) { SET HealthUp1.X, MY.X; … }
        Level level = Load(
            "MAPFILE <m.wmp>; THING drop { } SKILL was { VAL 0; } " +
            "ACTION bring { IF (drop.INVISIBLE == 1) { SET was, 1; SET drop.X, 3; SET drop.INVISIBLE, 0; } }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("bring"));

        AcknexObject drop = Assert.IsAssignableFrom<AcknexObject>(runtime.GetObject("drop"));
        Assert.Equal(1, runtime.GetSkill("was"));
        Assert.Equal(0, drop["INVISIBLE"]);
        Assert.Equal(3, ((Thing)drop).X);
    }

    [Fact]
    public void Runtime_CaptureOriginalSave_WallLengthAndVerticesAsTheOriginalComputesThem()
    {
        // OV-20261029-save-body round 13, a retail wall: the map's coordinates are cut toward zero
        // (−579730.4 → −579730), and the length's squares each lose their low 10 bits before the
        // square root, so a 245-step wall reads 243.
        const string map =
            "VERTEX -566.143 0 0;#0\nVERTEX -566.382 0 0;#1\nREGION r 0 10;#0\n" +
            "WALL w 0 1 0 0 0 0;#0\nTHING t 1 1 270 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(map, "MAPFILE <m.wmp>; WALL w { } THING t { IF_HIT hit; } ACTION hit { }");
        var runtime = new WdlRuntime(level);

        FullSave save = runtime.CaptureOriginalSave("T.WDL", "m.wmp", "T.WRS");

        FullSaveWall wall = Assert.Single(save.Walls);
        Assert.Equal((-579730, 0), wall.First);
        Assert.Equal((-579975, 0), wall.Second);
        Assert.Equal(243, wall.RawLength);
        Assert.Equal(1, wall.RawSkills["OFFSET_Y"]); // a placed wall's OFFSET_Y starts one step up
        FullSaveEntity thing = Assert.Single(save.Entities);
        Assert.Equal(4825, thing.RawSkills["ANGLE"]); // 270°, from 0 to 2π and cut toward zero
        Assert.Equal(11, thing.Events["IF_HIT"]);
    }

    [Fact]
    public void Runtime_CaptureOriginalSave_SaveTexturesRegionsAndPaletteAsTheOriginalWritesThem()
    {
        // OV-20261029-save-body rounds 15 and 15b, from LEGALISM's save: a SAVE texture's values and its
        // sound by number; a declared region the map does not place is saved after the map's; AMBIENT 0.2
        // is cut to 204; a region's events and its GENIUS by map object number; the palette is the last
        // FADE_PAL's by declaration.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "WALL w 0 1 0 0 0 0;#0\nTHING lamp 1 1 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SOUND s1,<A.WAV>; SOUND s2,<B.WAV>; PALETTE p1 { PALFILE <P1.PCX>; } PALETTE p2 { PALFILE <P2.PCX>; } " +
            "WALL w { } THING lamp { } ACTION enter { } ACTION fade { FADE_PAL p1, 1; } " +
            "TEXTURE tx { SCALE_XY 14, 16; ALBEDO 0.4; SOUND s2; FLAGS SAVE; } " +
            "REGION r { AMBIENT 0.2; FLAGS SAVE, CEIL_DESCEND; IF_ENTER enter; GENIUS lamp; } REGION spare { FLOOR_HGT -49; FLAGS SAVE; }");
        var runtime = new WdlRuntime(level);

        FullSave before = runtime.CaptureOriginalSave("T.WDL", "m.wmp", "T.WRS");
        Assert.True(runtime.RunAction("fade"));
        FullSave after = runtime.CaptureOriginalSave("T.WDL", "m.wmp", "T.WRS");

        FullSaveTexture texture = Assert.Single(before.Textures);
        Assert.Equal((409, 14336, 16384, 2, 512, 102400), (texture.Words["ALBEDO"], texture.Words["SCALE_X"], texture.Words["SCALE_Y"],
            texture.Words["SOUND"], texture.Words["SVOL"], texture.Words["SDIST"]));
        Assert.Equal(2, before.Regions.Count);
        Assert.Equal(-49 * 1024, before.Regions[0].RawSkills["FLOOR_HGT"]); // spare, after the map's region: saved first
        FullSaveRegion room = before.Regions[1];
        Assert.Equal(204, room.RawSkills["AMBIENT"]);
        Assert.Equal(["CEIL_DESCEND"], room.Flags);
        Assert.True(room.Here);
        Assert.Equal(2, room.Genius);
        Assert.Contains("IF_ENTER", room.Events.Keys);
        Assert.Equal((2, 1), (before.Palette, after.Palette));
    }

    [Fact]
    public void Runtime_CaptureOriginalSave_SlotsSynonymsAndSkillsAsTheOriginalWritesThem()
    {
        // OV-20261029-save-body round 16: key hooks at their scan codes and EACH_SEC.n at 134 + 2n; action
        // and string synonyms by number; a re-declared skill at its first place and an engine skill in the
        // engine's block with its range, which SET x.MAX changes; the map's bounds take in its things.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "WALL w 0 1 0 0 0 0;#0\nTHING t 5 7 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL h { MIN 0; MAX 100; VAL 5; } SKILL r { VAL 0; } SKILL PLAYER_SIZE { MIN 0; VAL 3; } SKILL h { VAL 1; } " +
            "STRING s1, \"a\"; STRING s2, \"b\"; WALL w { } THING t { } " +
            "ACTION go { SET r, h.MAX; SET h.MAX, 50; SET PLAYER_SIZE.MAX, 5; SET IF_ESC, go; SET IF_Z, go; SET EACH_SEC.2, go; } " +
            "SYNONYM sa { TYPE ACTION; DEFAULT go; } SYNONYM ss { TYPE STRING; DEFAULT s2; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("go"));
        FullSave save = runtime.CaptureOriginalSave("T.WDL", "m.wmp", "T.WRS");

        int go = level.ActionOrder.ToList().IndexOf("go") + 11;
        Assert.Equal(100, runtime.GetSkill("r"));
        Assert.Equal((go, go, go), (save.Slots[1], save.Slots[44], save.Slots[138]));
        Assert.Equal([2, go], save.Synonyms.Take(2));
        Assert.Equal(["r", "h"], save.Skills.Take(2).Select(static k => k.Name));
        Assert.Equal(50 * 1024, save.Skills[1].RawMax);
        SaveSkill size = Assert.Single(save.Skills, static k => k.Name == "PLAYER_SIZE");
        Assert.Equal((0, 5 * 1024), (size.RawMin, size.RawMax));
        Assert.Equal((5, 7), (runtime.GetSkill("MAP_MAXX"), runtime.GetSkill("MAP_MAXY")));
    }

    [Fact]
    public void Runtime_CaptureOriginalSave_ObjectEventActionsKeepTheirMy()
    {
        // OV-20261029-save-body round 17: an action an object's event started is saved in the action
        // section's second list, after the record of the action running the SAVE, with MY as the object's
        // map number and YOU 0; the load resumes it for that object.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "WALL w 0 1 0 0 0 0;#0\nTHING t 5 7 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(map, "MAPFILE <m.wmp>; WALL w { } THING t { IF_NEAR go; } ACTION go { WAITT 20; SET MY.X, 25; }");
        var runtime = new WdlRuntime(level);
        AcknexObject thing = Assert.IsAssignableFrom<AcknexObject>(runtime.GetObject("t"));
        Assert.True(runtime.TriggerObjectActionHook(thing, "IF_NEAR"));

        FullSave save = runtime.CaptureOriginalSave("T.WDL", "m.wmp", "T.WRS");

        int go = level.ActionOrder.ToList().IndexOf("go") + 11;
        Assert.Equal(new SaveAction(11, 0, 0, 1, -1, -1, Running: true), save.Actions[0]);
        Assert.Equal(2, save.Actions.Count);
        SaveAction waiting = save.Actions[1];
        Assert.Equal((go, true, 0, 1, 2, 0), (waiting.Action, waiting.Running, waiting.Position, waiting.Next, waiting.My, waiting.You));
        var loaded = new WdlRuntime(level);
        Assert.Equal(1, loaded.ImportOriginalActions(AcknexSaveFile.ReadActions(AcknexFullSave.Write(save))!, null));
        for (int i = 0; i < 200 && ((Thing)loaded.GetObject("t")!).X != 25; i++)
        {
            loaded.Tick(1.0 / 16);
        }

        Assert.Equal(25, ((Thing)loaded.GetObject("t")!).X);
    }

    [Fact]
    public void Runtime_PlaySound_RecordsObjectEmitter()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING source 5 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SOUND alert,<ALERT.WAV>; ACTION play { PLAY_SOUND alert, 1, source; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("play"));

        WdlSoundRequest request = Assert.Single(runtime.SoundRequests);
        Assert.Equal("alert", request.SoundName);
        Assert.Equal("ALERT.WAV", request.FileName);
        Assert.Equal(1, request.Volume);
        Assert.Null(request.Balance);
        Assert.Equal("source", request.EmitterName);
        Assert.False(request.Streamed);
    }

    [Fact]
    public void Runtime_PlaySoundFile_RecordsExplicitFile()
    {
        Level level = Load("MAPFILE <m.wmp>; ACTION play { PLAY_SOUNDFILE <DIRECT.WAV>, 0.25, 0.5; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("play"));

        WdlSoundRequest request = Assert.Single(runtime.SoundRequests);
        Assert.Equal("DIRECT.WAV", request.SoundName);
        Assert.Equal("DIRECT.WAV", request.FileName);
        Assert.Equal(0.25, request.Volume);
        Assert.Equal(0.5, request.Balance);
        Assert.Null(request.EmitterName);
        Assert.True(request.Streamed);
    }

    [Fact]
    public void Runtime_StopSound_RequestsStopAndContinuesAction()
    {
        Level level = Load("MAPFILE <m.wmp>; SKILL after { VAL 0; } ACTION stop { STOP_SOUND; SET after, 1; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("stop"));

        Assert.Equal(1, runtime.StopSoundCount);
        Assert.Equal(1, runtime.GetSkill("after"));
        Assert.Empty(runtime.SoundRequests);
        Assert.Empty(runtime.AudioRequests); // nothing was playing
    }

    [Fact]
    public void Runtime_SoundInstructions_RecordChronologicalAudioRequests()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SOUND alert,<ALERT.WAV>; " +
            "ACTION sounds { PLAY_SOUND alert, 1; STOP_SOUND; PLAY_SOUNDFILE <DIRECT.WAV>, 1; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("sounds"));

        Assert.Collection(
            runtime.AudioRequests,
            request =>
            {
                Assert.Equal(WdlSoundOperation.Play, request.Operation);
                Assert.Equal("alert", request.Sound.SoundName);
                Assert.Equal("ALERT.WAV", request.Sound.FileName);
            },
            request =>
            {
                Assert.Equal(WdlSoundOperation.StopChannel, request.Operation);
                Assert.Equal(0, request.Channel);
            },
            request =>
            {
                Assert.Equal(WdlSoundOperation.Play, request.Operation);
                Assert.Equal("DIRECT.WAV", request.Sound.SoundName);
                Assert.Equal("DIRECT.WAV", request.Sound.FileName);
            });
    }

    [Fact]
    public void Runtime_PaletteDeclarations_AreAvailableOnLevel()
    {
        Level level = Load("MAPFILE <m.wmp>; PALETTE dusk { PALFILE <DUSK.PCX>; }");

        Assert.Equal("DUSK.PCX", level.Palettes["dusk"]);
    }

    [Fact]
    public void Runtime_FadePal_RecordsPaletteFadeRequest()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; PALETTE dusk { PALFILE <DUSK.PCX>; } SKILL fade { VAL 0.5; } " +
            "ACTION fadeOut { FADE_PAL dusk, fade + 0.25; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("fadeOut"));

        WdlPaletteFadeRequest request = Assert.Single(runtime.PaletteFadeRequests);
        Assert.Equal("dusk", request.PaletteName);
        Assert.Equal("DUSK.PCX", request.FileName);
        Assert.Equal(0.75, request.Factor);
    }

    [Fact]
    public void Runtime_BeepInstruction_RecordsRequestAndContinuesAction()
    {
        Level level = Load("MAPFILE <m.wmp>; SKILL after { VAL 0; } ACTION alert { BEEP; SET after, 1; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("alert"));

        Assert.Equal(1, runtime.BeepCount);
        Assert.Equal(1, runtime.GetSkill("after"));
    }

    [Fact]
    public void Runtime_FlicInstructions_RecordPlaybackAndStopRequests()
    {
        // PLAY_FLICFILE does not hold its action, and FLIC_FRAME reads 1 right after it
        // (OV-20261015-flic-play-blocking).
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL after { VAL 0; } SKILL frameAfter { VAL -1; } " +
            "ACTION show { PLAY_FLICFILE <INTRO.FLC>; SET after,1; SET frameAfter, FLIC_FRAME; } " +
            "ACTION stop { STOP_FLIC; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("show"));

        WdlFlicRequest request = Assert.Single(runtime.FlicRequests);
        Assert.True(request.Id > 0);
        Assert.Equal("INTRO.FLC", request.FileName);
        Assert.True(request.Streamed);
        Assert.Equal(0, request.StopsBefore);
        Assert.Equal(1, runtime.GetSkill("after"));
        Assert.Equal(1, runtime.GetSkill("frameAfter"));
        Assert.Equal(0, runtime.WaitingActionCount);

        Assert.True(runtime.RunAction("stop"));

        Assert.Equal(1, runtime.StopFlicCount);
    }

    [Fact]
    public void Runtime_PlayFlicFileThenStopInOneAction_RecordsTheStopCountBeforeTheNextFlic()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; ACTION chain { PLAY_FLICFILE <A.FLC>; STOP_FLIC; PLAY_FLICFILE <B.FLC>; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("chain"));

        Assert.Collection(
            runtime.FlicRequests,
            first => Assert.Equal(0, first.StopsBefore),
            second => Assert.Equal(1, second.StopsBefore));
        Assert.Equal(1, runtime.StopFlicCount);
    }

    [Fact]
    public void Runtime_KeyHookArmedAfterPlayFlicFile_CanStopTheFlic()
    {
        // The retail title's logo order: the hook is armed after PLAY_FLICFILE in the same action,
        // so a key during the flic runs it, and its STOP_FLIC ends the flic
        // (OV-20261015-flic-play-blocking).
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL skipped { VAL 0; } " +
            "ACTION logo { PLAY_FLICFILE <LOGO.FLC>; SET IF_ANYKEY, skip; } " +
            "ACTION skip { STOP_FLIC; SET IF_ANYKEY, NULL; SET skipped, 1; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("logo"));
        Assert.Equal("skip", runtime.ActionHooks["IF_ANYKEY"]);

        runtime.TickFlic(1.0 / 16);
        Assert.True(runtime.TriggerActionHook("IF_ANYKEY"));

        Assert.Equal(1, runtime.StopFlicCount);
        Assert.Equal(1, runtime.GetSkill("skipped"));
        Assert.False(runtime.ActionHooks.TryGetValue("IF_ANYKEY", out string? hook) && hook is not null);
    }

    [Fact]
    public void TickFlic_RunsGlobalSchedulerButPausesWorldAndObjectTicks()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "THING actor 0 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL globalTicks { VAL 0; } SKILL objectTicks { VAL 0; } " +
            "EACH_TICK globalTick; THING actor { EACH_TICK objectTick; } " +
            "ACTION globalTick { RULE globalTicks += 1; } " +
            "ACTION objectTick { RULE objectTicks += 1; }");
        var runtime = new WdlRuntime(level);

        runtime.TickFlic(1.0 / 16);

        Assert.Equal(1, runtime.GetSkill("globalTicks"));
        Assert.Equal(0, runtime.GetSkill("objectTicks"));
        Assert.Equal(1, runtime.GetSkill("TICKS"));
    }

    [Fact]
    public void TickFlic_ResumesWaitingActions()
    {
        // A WAITT started before PLAY_FLICFILE resumes while the flic plays
        // (OV-20261015-flic-play-blocking).
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL waited { VAL 0; } " +
            "ACTION waiter { WAITT 2; SET waited, 1; } " +
            "ACTION logo { CALL waiter; PLAY_FLICFILE <LOGO.FLC>; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("logo"));
        Assert.Equal(1, runtime.WaitingActionCount);

        runtime.TickFlic(1.0 / 16);
        runtime.TickFlic(1.0 / 16);
        Assert.Equal(0, runtime.GetSkill("waited"));
        runtime.TickFlic(1.0 / 16);

        Assert.Equal(1, runtime.GetSkill("waited"));
        Assert.Equal(0, runtime.WaitingActionCount);
    }

    [Fact]
    public void Runtime_ScreenshotInstruction_RecordsBaseNameAndNumber()
    {
        Level level = Load("MAPFILE <m.wmp>; SKILL slot { VAL 7.9; } ACTION snap { SCREENSHOT \"screenShot\", slot; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("snap"));

        WdlScreenshotRequest request = Assert.Single(runtime.ScreenshotRequests);
        Assert.Equal("scree", request.BaseName);
        Assert.Equal(7, request.Number);
    }

    [Fact]
    public void Runtime_SaveLoadInstructions_RecordPersistenceRequests()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; STRING save_name,\"slotName\"; SKILL slot { VAL 12.9; } SKILL after { VAL 0; } " +
            "ACTION persist { SAVE save_name, slot; LOAD \"other\", 4; SAVE_INFO \"stats\", 2; LOAD_INFO \"stats\", 3; SET after, 1; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("persist"));

        Assert.Equal(1, runtime.GetSkill("after"));
        Assert.Collection(
            runtime.PersistenceRequests,
            request =>
            {
                Assert.Equal(WdlPersistenceOperation.Save, request.Operation);
                Assert.Equal("slotN", request.BaseName);
                Assert.Equal(12, request.Number);
                Assert.False(request.InfoOnly);
            },
            request =>
            {
                Assert.Equal(WdlPersistenceOperation.Load, request.Operation);
                Assert.Equal("other", request.BaseName);
                Assert.Equal(4, request.Number);
                Assert.False(request.InfoOnly);
            },
            request =>
            {
                Assert.Equal(WdlPersistenceOperation.Save, request.Operation);
                Assert.Equal("stats", request.BaseName);
                Assert.Equal(2, request.Number);
                Assert.True(request.InfoOnly);
            },
            request =>
            {
                Assert.Equal(WdlPersistenceOperation.Load, request.Operation);
                Assert.Equal("stats", request.BaseName);
                Assert.Equal(3, request.Number);
                Assert.True(request.InfoOnly);
            });
    }

    [Fact]
    public void RuntimeSnapshot_RestoresLiveRuntimeWorldAndPlayerState()
    {
        Level level = LoadWithMap(
            TwoRoomMap,
            "MAPFILE <m.wmp>; SKILL health { VAL 10; } SKILL hook_result { VAL 0; } SKILL tick_result { VAL 0; } " +
            "STRING label,\"saved\"; EACH_TICK.1 tick_a; IF_F1 hook_a; " +
            "ACTION tick_a { SET tick_result, 1; SET EACH_TICK.1, NULL; } " +
            "ACTION tick_b { SET tick_result, 2; SET EACH_TICK.1, NULL; } " +
            "ACTION hook_a { SET hook_result, 1; } ACTION hook_b { SET hook_result, 2; } " +
            "ACTION rebind { SET EACH_TICK.1, tick_b; SET IF_F1, hook_b; }");
        Thing pickup = Assert.Single(level.Things);
        Region left = level.Regions[0];
        var runtime = new WdlRuntime(level);
        var player = new Player(level);
        runtime.RegisterObject("player", player);

        pickup["CYCLE"] = 3;
        pickup.TrySetString("EACH_TICK", "tick_a");
        left["AMBIENT"] = 0.25;
        left.TrySetString("IF_ENTER", "hook_a");
        player["x"] = 6;
        player["y"] = 7;
        player["z"] = 8;
        player["region"] = 1;

        WdlRuntimeSnapshot snapshot = JsonSerializer.Deserialize<WdlRuntimeSnapshot>(
            JsonSerializer.Serialize(runtime.CaptureSnapshot()))!;

        runtime.SetSkill("health", 99);
        runtime.SetString("label", "other");
        runtime.RunAction("rebind");
        ((IList<Vertex>)level.Vertices)[0] = new Vertex(99, 99, 99);
        pickup["CYCLE"] = 12;
        pickup["EXTRA"] = 42;
        pickup.TrySetString("EACH_TICK", null);
        left["AMBIENT"] = 1;
        left.TrySetString("IF_ENTER", null);
        player["x"] = 50;
        player["y"] = 60;
        player["z"] = 70;
        player["region"] = 0;

        runtime.RestoreSnapshot(snapshot);

        Assert.Equal(10, runtime.GetSkill("health"));
        Assert.True(runtime.TryGetString("label", out string label));
        Assert.Equal("saved", label);
        Assert.Equal(0, level.Vertices[0].X);
        Assert.Equal(0, level.Vertices[0].Y);
        Assert.Equal(0, level.Vertices[0].Z);
        Assert.Equal(3, pickup["CYCLE"]);
        Assert.False(pickup.HasSkill("EXTRA"));
        Assert.True(pickup.TryGetString("EACH_TICK", out string? pickupTick));
        Assert.Equal("tick_a", pickupTick);
        Assert.Equal(0.25, left["AMBIENT"]);
        Assert.True(left.TryGetString("IF_ENTER", out string? enterHook));
        Assert.Equal("hook_a", enterHook);
        Assert.Equal(6, player["x"]);
        Assert.Equal(7, player["y"]);
        Assert.Equal(8, player["z"]);
        Assert.Equal(1, player["region"]);

        Assert.True(runtime.TriggerActionHook("IF_F1"));
        Assert.Equal(1, runtime.GetSkill("hook_result"));
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("tick_result"));
    }

    [Fact]
    public void Runtime_InkeyInstruction_SuspendsAndResumesWithAcceptedInput()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; STRING player_name,\"        \"; SKILL after { VAL 0; } " +
            "ACTION enterName { INKEY player_name; SET after, 1; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("enterName"));

        WdlInputRequest request = Assert.Single(runtime.PendingInputRequests);
        Assert.Equal("player_name", request.StringName);
        Assert.Equal(8, request.Capacity);
        Assert.Equal(0, runtime.GetSkill("after"));

        Assert.True(runtime.CompleteInput(request.Id, "Faith   "));

        Assert.Empty(runtime.PendingInputRequests);
        Assert.Equal("Faith", runtime.Strings["player_name"].Value);
        Assert.Equal(0, runtime.GetSkill("RESULT"));
        Assert.Equal(5, runtime.GetSkill("STR_LEN"));
        Assert.Equal(1, runtime.GetSkill("after"));
    }

    [Fact]
    public void Runtime_InkeyInstruction_AbortRestoresPreviousString()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; STRING player_name,\"old\"; SKILL after { VAL 0; } " +
            "ACTION enterName { INKEY player_name; SET after, 1; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("enterName"));

        WdlInputRequest request = Assert.Single(runtime.PendingInputRequests);
        Assert.True(runtime.CompleteInput(request.Id, "new", resultCode: -1));

        Assert.Equal("old", runtime.Strings["player_name"].Value);
        Assert.Equal(-1, runtime.GetSkill("RESULT"));
        Assert.Equal(3, runtime.GetSkill("STR_LEN"));
        Assert.Equal(1, runtime.GetSkill("after"));
    }

    [Theory]
    [InlineData("0.1")]
    [InlineData("0.5")]
    [InlineData("0.99")]
    public void Runtime_WaittBelowOne_GoesStraightOn(string amount)
    {
        // OV-20261029-scheduler-order round 5: WAITT 0, 0.1 … 0.99 resumed in the cycle that armed them.
        Level level = Load($"MAPFILE <m.wmp>; SKILL order {{ VAL 0; }} ACTION waitSome {{ SET order, 1; WAITT {amount}; SET order, 2; }}");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("waitSome"));

        Assert.Equal(2, runtime.GetSkill("order"));
        Assert.Equal(0, runtime.WaitingActionCount);
    }

    [Fact]
    public void Runtime_WaitInstruction_SuspendsAndResumesAfterFrameCycles()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL order { VAL 0; } " +
            "ACTION waitTwo { SET order, 1; WAIT 2; SET order, 2; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("waitTwo"));
        Assert.Equal(1, runtime.GetSkill("order"));
        Assert.Equal(1, runtime.WaitingActionCount);

        // Armed before the first frame, the WAIT counts frames 1 and 2 and resumes in frame 3
        // (OV-20261015-flic-scheduler-detail).
        runtime.Tick(1.0 / 16);
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("order"));
        Assert.Equal(1, runtime.WaitingActionCount);

        runtime.Tick(1.0 / 16);
        Assert.Equal(2, runtime.GetSkill("order"));
        Assert.Equal(0, runtime.WaitingActionCount);
    }

    [Theory]
    [InlineData(0.5, 0)]
    [InlineData(1.5, 2)]
    [InlineData(2.5, 3)]
    public void Runtime_FractionalWait_WaitsWhileOneOrMoreCyclesAreLeft(double amount, int ticks)
    {
        // OV-20261029-scheduler-order round 3: WAIT 0.5 went straight on, WAIT 1.5 took one cycle
        // and WAIT 2.5 two. Armed before the first frame, a wait resumes a frame later than one armed
        // inside a cycle (OV-20261015-flic-scheduler-detail).
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL order { VAL 0; } " +
            string.Create(CultureInfo.InvariantCulture, $"ACTION waitSome {{ SET order, 1; WAIT {amount}; SET order, 2; }}"));
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("waitSome"));
        for (int i = 0; i < ticks; i++)
        {
            Assert.Equal(1, runtime.GetSkill("order"));
            runtime.Tick(1.0 / 16);
        }

        Assert.Equal(2, runtime.GetSkill("order"));
    }

    [Fact]
    public void Runtime_SkipInstruction_AdvancesResumableAction()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL x { VAL 0; } " +
            "ACTION skipWait { SKIP 1; WAIT 1; SET x, 1; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("skipWait"));

        Assert.Equal(1, runtime.GetSkill("x"));
        Assert.Equal(0, runtime.WaitingActionCount);
    }

    [Fact]
    public void Runtime_WaitTInstruction_SuspendsAndResumesAfterFixedTicks()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL order { VAL 0; } " +
            "ACTION waitTick { SET order, 1; WAITT 1; SET order, 2; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("waitTick"));
        Assert.Equal(1, runtime.GetSkill("order"));

        runtime.Tick(1.0 / 32);
        Assert.Equal(1, runtime.GetSkill("order"));
        Assert.Equal(1, runtime.WaitingActionCount);

        runtime.Tick(1.0 / 32);
        Assert.Equal(2, runtime.GetSkill("order"));
        Assert.Equal(0, runtime.WaitingActionCount);
    }

    [Fact]
    public void Runtime_WaitInsideWhile_ResumesLoop()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL x { VAL 0; } SKILL done { VAL 0; } " +
            "ACTION loop { WHILE (x < 3) { RULE x += 1; WAIT 1; } SET done, 1; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("loop"));
        Assert.Equal(1, runtime.GetSkill("x"));
        Assert.Equal(0, runtime.GetSkill("done"));

        // The first WAIT 1 was armed before frame 1, so it resumes in frame 2; each later one
        // is armed in a frame and resumes in the next.
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("x"));
        runtime.Tick(1.0 / 16);
        Assert.Equal(2, runtime.GetSkill("x"));
        Assert.Equal(0, runtime.GetSkill("done"));

        runtime.Tick(1.0 / 16);
        Assert.Equal(3, runtime.GetSkill("x"));
        Assert.Equal(0, runtime.GetSkill("done"));

        runtime.Tick(1.0 / 16);
        Assert.Equal(3, runtime.GetSkill("x"));
        Assert.Equal(1, runtime.GetSkill("done"));
        Assert.Equal(0, runtime.WaitingActionCount);
    }

    [Fact]
    public void Runtime_CallWaitedAction_CallerContinuesWhileCalleeSleeps()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL after { VAL 0; } SKILL delayed { VAL 0; } " +
            "ACTION outer { CALL inner; SET after, 1; } " +
            "ACTION inner { WAIT 1; SET delayed, 1; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("outer"));
        Assert.Equal(1, runtime.GetSkill("after"));
        Assert.Equal(0, runtime.GetSkill("delayed"));
        Assert.Equal(1, runtime.WaitingActionCount);

        runtime.Tick(1.0 / 16);
        Assert.Equal(0, runtime.GetSkill("delayed"));
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("delayed"));
        Assert.Equal(0, runtime.WaitingActionCount);
    }

    [Fact]
    public void Runtime_WaitedObjectAction_RestoresMyOnResume()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING pickup 5 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; THING pickup { IF_NEAR delayed; } " +
            "ACTION delayed { WAIT 1; SET my.x, 9; }");
        Thing pickup = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);
        var previousMy = new Thing("previousMy") { X = 99 };
        runtime.RegisterObject("my", previousMy);

        Assert.True(runtime.TriggerObjectActionHook(pickup, "IF_NEAR"));
        Assert.Same(previousMy, runtime.GetObject("my"));
        Assert.Equal(5, pickup.X);

        runtime.Tick(1.0 / 16);
        Assert.Equal(5, pickup.X);
        runtime.Tick(1.0 / 16);
        Assert.Equal(9, pickup.X);
        Assert.Same(previousMy, runtime.GetObject("my"));
    }

    [Fact]
    public void Tick_WaitedObjectAction_PausesWhileTargetIsInvisible()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING pickup 5 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL resumed { VAL 0; } " +
            "THING pickup { EACH_TICK hideAndWait; } " +
            "ACTION hideAndWait { SET my.EACH_TICK,NULL; SET my.INVISIBLE,1; WAIT 1; ADD resumed,1; } " +
            "ACTION show { SET pickup.INVISIBLE,0; }");
        Thing pickup = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);

        runtime.Tick(1.0 / 16);
        Assert.Equal(1, pickup["INVISIBLE"]);
        Assert.Equal(0, pickup["VISIBLE"]);
        Assert.Equal(1, runtime.WaitingActionCount);

        runtime.Tick(1.0 / 16);
        runtime.Tick(1.0 / 16);
        Assert.Equal(0, runtime.GetSkill("resumed"));
        Assert.Equal(1, runtime.WaitingActionCount);

        Assert.True(runtime.RunAction("show"));
        Assert.Equal(0, pickup["INVISIBLE"]);
        Assert.Equal(1, pickup["VISIBLE"]);

        // While its target was invisible the WAIT did not count down; it counts the first
        // visible frame and resumes in the next.
        runtime.Tick(1.0 / 16);
        Assert.Equal(0, runtime.GetSkill("resumed"));
        runtime.Tick(1.0 / 16);

        Assert.Equal(1, runtime.GetSkill("resumed"));
        Assert.Equal(0, runtime.WaitingActionCount);
    }

    [Fact]
    public void Runtime_GotoInstruction_JumpsToLabel()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL x { VAL 0; } ACTION loop { again: RULE x += 1; IF (x < 4) { GOTO again; } }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("loop");

        Assert.Equal(4, runtime.GetSkill("x"));
    }

    [Fact]
    public void Runtime_WhileInstruction_RepeatsUntilFalse()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL x { VAL 0; } ACTION loop { WHILE (x < 4) { RULE x += 1; } }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("loop");

        Assert.Equal(4, runtime.GetSkill("x"));
    }

    [Fact]
    public void Runtime_MutualRecursion_DoesNotStackOverflow()
    {
        // a -> b -> a -> ... is bounded by the call-depth guard.
        Level level = Load("MAPFILE <m.wmp>; ACTION a { b; } ACTION b { a; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("a"); // must return rather than overflow

        Assert.True(runtime.HasAction("a"));
    }

    [Fact]
    public void Runtime_RunStartup_RunsTheIfStartAction()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL booted { VAL 0; } ACTION boot { SET booted, 1; } IF_START boot;");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunStartup());
        Assert.Equal(1, runtime.GetSkill("booted"));
    }

    [Fact]
    public void Runtime_RunStartup_NoIfStart_ReturnsFalse()
    {
        var runtime = new WdlRuntime(Load("MAPFILE <m.wmp>;"));

        Assert.False(runtime.RunStartup());
    }

    [Fact]
    public void Runtime_RunStartup_AssignsEachTickAction_AndDispatchesOnFirstTick()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; " +
            "SKILL order { VAL 0; } SKILL tick_count { VAL 0; } " +
            "ACTION boot { SET order, 1; SET EACH_TICK.1, probe_tick; } " +
            "ACTION probe_tick { RULE order = order * 10 + 2; RULE tick_count += 1; SET EACH_TICK.1, NULL; } " +
            "IF_START boot;");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunStartup());
        Assert.Equal(1, runtime.GetSkill("order"));
        Assert.Equal(0, runtime.GetSkill("tick_count"));

        runtime.Tick(1.0 / 16);
        Assert.Equal(12, runtime.GetSkill("order"));
        Assert.Equal(1, runtime.GetSkill("tick_count"));

        runtime.Tick(1.0 / 16);
        Assert.Equal(12, runtime.GetSkill("order"));
        Assert.Equal(1, runtime.GetSkill("tick_count"));
    }

    [Fact]
    public void Tick_DispatchesInitialEachTickActionsInOrder()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; " +
            "SKILL order { VAL 0; } " +
            "EACH_TICK first, second; " +
            "ACTION first { RULE order = order * 10 + 1; SET EACH_TICK.1, NULL; } " +
            "ACTION second { RULE order = order * 10 + 2; }");
        var runtime = new WdlRuntime(level);

        runtime.Tick(1.0 / 16);
        Assert.Equal(12, runtime.GetSkill("order"));

        runtime.Tick(1.0 / 16);
        Assert.Equal(122, runtime.GetSkill("order"));
    }

    [Fact]
    public void Runtime_ActionHooks_TriggerAndCanBeReassigned()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; " +
            "SKILL order { VAL 0; } " +
            "IF_F1 first; " +
            "ACTION first { RULE order = order * 10 + 1; } " +
            "ACTION second { RULE order = order * 10 + 2; } " +
            "ACTION rebind { SET IF_F1, second; } " +
            "ACTION clear { SET IF_F1, NULL; }");
        var runtime = new WdlRuntime(level);

        Assert.Equal("first", runtime.ActionHooks["IF_F1"]);
        Assert.True(runtime.TriggerActionHook("IF_F1"));
        Assert.Equal(1, runtime.GetSkill("order"));

        Assert.True(runtime.RunAction("rebind"));
        Assert.Equal("second", runtime.ActionHooks["IF_F1"]);
        Assert.True(runtime.TriggerActionHook("IF_F1"));
        Assert.Equal(12, runtime.GetSkill("order"));

        Assert.True(runtime.RunAction("clear"));
        Assert.Null(runtime.ActionHooks["IF_F1"]);
        Assert.False(runtime.TriggerActionHook("IF_F1"));
        Assert.Equal(12, runtime.GetSkill("order"));
    }

    [Fact]
    public void Runtime_UnknownAction_ReturnsFalse()
    {
        var runtime = new WdlRuntime(Load("MAPFILE <m.wmp>;"));

        Assert.False(runtime.RunAction("nope"));
    }

    [Fact]
    public void Runtime_RegistersUniquePlacedObjectsByName()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING plant 5 6 90 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; ACTION movePlant { SET plant.x, 9; RULE plant.y += 1; }");
        Thing plant = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);

        Assert.Same(plant, runtime.GetObject("plant"));
        Assert.Contains(plant, runtime.LevelObjects);

        Assert.True(runtime.RunAction("movePlant"));
        Assert.Equal(9, plant.X);
        Assert.Equal(7, plant.Y);
    }

    [Fact]
    public void Runtime_DuplicatePlacedObjectNames_ResolveToFirstPlacedObject()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING plant 5 6 90 0;#1\nTHING plant 7 8 90 0;#2\nPLAYER_START 0 0 0 0;#3";
        Level level = LoadWithMap(map, "MAPFILE <m.wmp>; ACTION movePlant { SET plant.X, 11; }");
        var runtime = new WdlRuntime(level);

        Assert.Equal(2, runtime.LevelObjects.Count);
        Assert.Same(level.Things[0], runtime.GetObject("plant"));
        Assert.False(runtime.Objects.ContainsKey("plant"));

        Assert.True(runtime.RunAction("movePlant"));
        Assert.Equal(11, level.Things[0].X);
        Assert.Equal(7, level.Things[1].X);
    }

    [Fact]
    public void Runtime_RegistersRegionsAndWallsByUniqueName()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "WALL gate 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; " +
            "REGION room { AMBIENT 0.25; } " +
            "ACTION updateWorld { RULE room.AMBIENT += 0.25; SET gate.OFFSET_X, 7; }");
        Region room = Assert.Single(level.Regions);
        Wall gate = Assert.Single(level.Walls);
        var runtime = new WdlRuntime(level);

        Assert.Same(room, runtime.GetObject("room"));
        Assert.Same(gate, runtime.GetObject("gate"));
        Assert.Empty(runtime.LevelObjects);

        Assert.True(runtime.RunAction("updateWorld"));
        Assert.Equal(0.5, room.Ambient);
        Assert.Equal(7, gate.OffsetX);
    }

    [Fact]
    public void Runtime_PredefinedHere_ReferencesPlayerStartRegion()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\n" +
            "REGION room0 0 10;#0\nREGION room1 5 15;#1\nPLAYER_START 0 0 0 1;#2";
        Level level = LoadWithMap(map, "MAPFILE <m.wmp>;");
        var runtime = new WdlRuntime(level);

        Assert.Same(level.Regions[1], runtime.GetObject("here"));
    }

    [Fact]
    public void Runtime_DuplicateWallNames_ResolveToFirstPlacedWall()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nVERTEX 2 0 0;#2\nREGION r 0 10;#0\n" +
            "WALL gate 0 1 0 -1 0 0;#0\nWALL gate 1 2 0 -1 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(map, "MAPFILE <m.wmp>; ACTION moveGate { SET gate.OFFSET_X, 5; }");
        var runtime = new WdlRuntime(level);

        Assert.Same(level.Walls[0], runtime.GetObject("gate"));
        Assert.False(runtime.Objects.ContainsKey("gate"));

        Assert.True(runtime.RunAction("moveGate"));
        Assert.Equal(5, level.Walls[0].OffsetX);
        Assert.Equal(0, level.Walls[1].OffsetX);
    }

    [Fact]
    public void Runtime_SetAll_AppliesToEveryMatchingObjectExceptPositions()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nVERTEX 2 0 0;#2\nREGION r 0 10;#0\n" +
            "WALL gate 0 1 0 -1 0 0;#0\nWALL gate 1 2 0 -1 0 0;#1\n" +
            "THING plant 5 6 90 0;#2\nTHING plant 7 8 90 0;#3\nPLAYER_START 0 0 0 0;#4";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; " +
            "ACTION markAll { SET_ALL plant.FLAG3, 1; SET_ALL plant.DIST, 7; SET_ALL plant.X, 99; SET_ALL gate.FLAG2, 1; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("markAll"));

        Assert.All(level.Things, thing =>
        {
            Assert.Equal(1, thing["flag3"]);
            Assert.Equal(7, thing["dist"]);
        });
        Assert.Equal(5, level.Things[0].X);
        Assert.Equal(7, level.Things[1].X);
        Assert.All(level.Walls, wall => Assert.Equal(1, wall["flag2"]));
    }

    [Fact]
    public void Runtime_Set_AssignsDocumentedTextureKeywords()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nVERTEX 2 0 0;#2\n" +
            "REGION tank 0 10;#0\nREGION tank 1 11;#1\n" +
            "WALL gate 0 1 0 -1 0 0;#0\nTHING plant 5 6 90 0;#2\nPLAYER_START 0 0 0 0;#3";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; " +
            "REGION tank { FLOOR_TEX oldFloor; CEIL_TEX oldCeil; } " +
            "WALL gate { TEXTURE oldWall; } THING plant { TEXTURE oldPlant; } " +
            "ACTION swapTextures { SET tank.CEIL_TEX, water_tex; SET_ALL tank.FLOOR_TEX, lava_tex; " +
            "SET gate.TEXTURE, NULL; SET plant.TEXTURE, fern_tex; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("swapTextures"));

        Assert.Equal("water_tex", level.Regions[0].CeilTexture);
        Assert.Equal("oldCeil", level.Regions[1].CeilTexture);
        Assert.All(level.Regions, region => Assert.Equal("lava_tex", region.FloorTexture));
        Assert.Null(level.Walls[0].Texture);
        Assert.Equal("fern_tex", level.Things[0].Texture);
    }

    [Fact]
    public void Runtime_Set_AssignsAndClearsDocumentedEntityKeywordMembers()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "ACTOR guard 4 5 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; ACTOR guard { TARGET MOVE; WAY patrol; OVERLAY icon; ATTACH glow; } " +
            "ACTION retarget { SET guard.TARGET, FOLLOW; SET guard.WAY, NULL; " +
            "SET guard.OVERLAY, alertIcon; SET guard.ATTACH, NULL; }");
        var runtime = new WdlRuntime(level);
        Actor guard = Assert.Single(level.Actors);

        Assert.Equal("MOVE", guard.Target);
        Assert.Equal("patrol", guard.WayName);
        Assert.True(runtime.RunAction("retarget"));

        Assert.Equal("FOLLOW", guard.Target);
        Assert.Null(guard.WayName);
        Assert.Equal("alertIcon", guard.OverlayName);
        Assert.Null(guard.AttachTexture);
    }

    [Fact]
    public void Runtime_If_ComparesDocumentedTextureKeywordsAndNull()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\n" +
            "REGION tank 0 10;#0\nTHING plant 5 6 90 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; " +
            "SKILL matched { VAL 0; } SKILL missing { VAL 0; } SKILL notMatched { VAL 0; } " +
            "REGION tank { FLOOR_TEX water_tex; } " +
            "SYNONYM selected { TYPE TEXTURE; DEFAULT water_tex; } " +
            "ACTION checkTextures { IF (tank.FLOOR_TEX == selected) { SET matched, 1; } " +
            "IF (plant.TEXTURE == NULL) { SET missing, 1; } " +
            "IF (tank.FLOOR_TEX == lava_tex) { SET notMatched, 1; } ELSE { SET notMatched, 2; } }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("checkTextures"));

        Assert.Equal(1, runtime.GetSkill("matched"));
        Assert.Equal(1, runtime.GetSkill("missing"));
        Assert.Equal(2, runtime.GetSkill("notMatched"));
    }

    [Fact]
    public void Runtime_TextureSynonym_CanReceiveObjectTextureKeyword()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\n" +
            "REGION lift_1 0 10;#0\nPLAYER_START 0 0 0 0;#1";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; " +
            "REGION lift_1 { FLOOR_TEX floor_tex; CEIL_TEX old_ceil; } " +
            "SYNONYM lift_syn1 { TYPE REGION; } SYNONYM lift_syn2 { TYPE REGION; } " +
            "SYNONYM lift_floor { TYPE TEXTURE; } " +
            "ACTION lift1_to_lift2 { SET lift_syn1, lift_1; SET lift_syn2, lift_syn1; " +
            "SET lift_floor, lift_syn2.FLOOR_TEX; SET lift_1.CEIL_TEX, lift_floor; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("lift1_to_lift2"));

        Assert.Equal("floor_tex", runtime.KeywordReferences["lift_floor"]);
        Assert.Equal("floor_tex", level.Regions[0].CeilTexture);
    }

    [Fact]
    public void Runtime_ObjectSynonym_SeedsDefaultReferenceByType()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\n" +
            "REGION lift_1 0 10;#0\nPLAYER_START 0 0 0 0;#1";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; " +
            "SYNONYM lift_syn { TYPE REGION; DEFAULT lift_1; } " +
            "ACTION raiseLift { RULE lift_syn.FLOOR_HGT += 2; }");
        var runtime = new WdlRuntime(level);

        Assert.Same(level.Regions[0], runtime.GetObject("lift_syn"));
        Assert.True(runtime.RunAction("raiseLift"));

        Assert.Equal(2, level.Regions[0].FloorHeight);
    }

    [Fact]
    public void Runtime_ActionSynonym_CallsDefaultAndAssignedAction()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL result { VAL 0; } " +
            "SYNONYM selected { TYPE ACTION; DEFAULT first; } " +
            "ACTION first { RULE result = result * 10 + 1; } " +
            "ACTION second { RULE result = result * 10 + 2; } " +
            "ACTION drive { CALL selected; SET selected, second; selected; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("drive"));

        Assert.Equal(12, runtime.GetSkill("result"));
    }

    [Fact]
    public void Runtime_StringSynonym_ReadsAndWritesAssignedString()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; " +
            "STRING source,\"alpha\"; STRING target,\".....\"; STRING sink,\".....\"; " +
            "SYNONYM active { TYPE STRING; DEFAULT source; } " +
            "ACTION editStrings { SET_STRING sink, active; SET active, target; " +
            "SET_STRING active, \"beta\"; ADD_STRING active, \"!\"; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("editStrings"));

        Assert.Equal("alpha", runtime.Strings["sink"].Value);
        Assert.Equal("alpha", runtime.Strings["source"].Value);
        Assert.Equal("beta!", runtime.Strings["target"].Value);
    }

    [Fact]
    public void Runtime_NextThere_CyclesThroughRegionsWithSameName()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\n" +
            "REGION lift1 0 10;#0\nREGION lift1 1 11;#1\nREGION lift1 2 12;#2\n" +
            "PLAYER_START 0 0 0 0;#3";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL visits { VAL 0; } " +
            "ACTION liftAll { SET THERE,lift1; loop1: RULE THERE.FLOOR_HGT += 1; RULE visits += 1; " +
            "NEXT_THERE; IF (THERE != lift1) { GOTO loop1; } }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("liftAll"));

        Assert.Equal([1, 2, 3], level.Regions.Select(region => (int)region.FloorHeight));
        Assert.Equal(3, runtime.GetSkill("visits"));
        Assert.Same(level.Regions[0], runtime.GetObject("there"));
    }

    [Fact]
    public void Runtime_NextMy_CyclesThroughObjectsWithSameName()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING plant 5 6 90 0;#1\nTHING plant 7 8 90 0;#2\nPLAYER_START 0 0 0 0;#3";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL visits { VAL 0; } " +
            "ACTION markPlants { SET MY,plant; loop1: RULE MY.SKILL1 += 1; RULE visits += 1; " +
            "NEXT_MY; IF (MY != plant) { GOTO loop1; } }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("markPlants"));

        Assert.All(level.Things, thing => Assert.Equal(1, thing["skill1"]));
        Assert.Equal(2, runtime.GetSkill("visits"));
        Assert.Same(level.Things[0], runtime.GetObject("my"));
    }

    [Fact]
    public void Runtime_NextMyThere_CyclesThroughObjectsInSameRegion()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\n" +
            "REGION r 0 10;#0\nREGION r 0 10;#1\n" +
            "THING first 5 6 90 0;#0\nTHING second 7 8 90 0;#0\nTHING third 9 10 90 1;#1\n" +
            "PLAYER_START 0 0 0 0;#4";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; ACTION hop { SET MY,first; NEXT_MY_THERE; SET seen, MY.x; NEXT_MY_THERE; SET back, MY.x; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("hop"));

        Assert.Equal(7, runtime.GetSkill("seen"));
        Assert.Equal(5, runtime.GetSkill("back"));
    }

    [Fact]
    public void Runtime_StringInstructions_CopyAppendConvertAndParse()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; " +
            "STRING source,\"abcdef\"; STRING target,\".....\"; STRING cat,\".....\"; STRING number,\"....\"; " +
            "SKILL value { VAL 12.5; } SKILL parsed { VAL 0; } " +
            "ACTION editStrings { SET_STRING target,source; SET_STRING cat,\"ab\"; ADD_STRING cat,\"cdef\"; " +
            "TO_STRING number,value; SET_SKILL parsed,number; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("editStrings"));

        Assert.Equal("abcde", runtime.Strings["target"].Value);
        Assert.Equal("abcde", runtime.Strings["cat"].Value);
        Assert.Equal("12.5", runtime.Strings["number"].Value);
        Assert.Equal(12.5, runtime.GetSkill("parsed"));
    }

    [Fact]
    public void Runtime_SaveInfoAndLoadInfo_UseTheOriginalsFileAndKeepGlobalAndLocalSkills()
    {
        // OV-20261029-save-format: SAVE_INFO writes NAME<n>.SAV with the GLOBAL and LOCAL skills;
        // LOAD_INFO brings those back in the instruction and leaves PLAYER skills alone.
        string saveDir = Path.Combine(Path.GetTempPath(), "OpenVirtue-save-" + Guid.NewGuid().ToString("N"));
        try
        {
            Level level = Load(
                "MAPFILE <m.wmp>; SKILL g { TYPE GLOBAL; VAL 1234.5; } SKILL l { TYPE LOCAL; VAL 77; } SKILL p { VAL 5; } " +
                "SKILL after { VAL 0; } " +
                "ACTION run { SAVE_INFO \"ovsav\", 3; RULE g = 1; RULE l = 1; RULE p = 1; LOAD_INFO \"ovsav\", 3; RULE after = g + l + p; }");
            var runtime = new WdlRuntime(level) { SaveDirectory = saveDir };

            Assert.True(runtime.RunAction("run"));

            Assert.True(File.Exists(Path.Combine(saveDir, "OVSAV3.SAV")));
            Assert.Equal(1234.5, runtime.GetSkill("g"));
            Assert.Equal(77, runtime.GetSkill("l"));
            Assert.Equal(1, runtime.GetSkill("p"));
            Assert.Equal(1234.5 + 77 + 1, runtime.GetSkill("after"));
        }
        finally
        {
            if (Directory.Exists(saveDir))
            {
                Directory.Delete(saveDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Runtime_PrintInstructions_AppendToSelectedTextFile()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "OpenVirtue-print-" + Guid.NewGuid().ToString("N"));
        try
        {
            Level level = Load(
                "MAPFILE <m.wmp>; " +
                "STRING line,\"hello\\nworld\"; SKILL value { VAL 12.3456; } " +
                "ACTION log { PRINTFILE \"ovstate\", 7; PRINT_VALUE value; PRINT_STRING line; PRINT_STRING \"done\\n\"; }");
            var runtime = new WdlRuntime(level, outputDir);

            Assert.True(runtime.RunAction("log"));
            Assert.True(runtime.RunAction("log"));

            string outputPath = Path.Combine(outputDir, "OVSTA7.TXT");
            string text = File.ReadAllText(outputPath);
            // 12.3456 is held as 12641/1024 = 12.3447 (OV-20261029-numeric-edge).
            const string expectedRun = "12.345hello\nworlddone\n";
            Assert.Equal(expectedRun + expectedRun, text);
        }
        finally
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Runtime_RegisterObject_AddsExplicitReference()
    {
        Level level = Load("MAPFILE <m.wmp>; ACTION moveMe { SET my.x, 11; }");
        var runtime = new WdlRuntime(level);
        var thing = new Thing("scratch");

        runtime.RegisterObject("my", thing);
        Assert.True(runtime.RunAction("moveMe"));

        Assert.Equal(11, thing.X);
    }

    [Fact]
    public void Runtime_ObjectActions_ReadDocumentedEntityFieldsAndUniversalSkills()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING pickup 2 3 90 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL result { VAL 0; } " +
            "THING pickup { HEIGHT 2; SKILL1 3; DIST 4; MAP_COLOR 5; FLAGS INVISIBLE; } " +
            "ACTION readPickup { RULE result = my.HEIGHT + my.SKILL1 + my.REGION + my.DIST + my.MAP_COLOR + my.THING + my.INVISIBLE; }");
        Thing pickup = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);

        runtime.RegisterObject("my", pickup);

        Assert.True(runtime.RunAction("readPickup"));
        Assert.Equal(16, runtime.GetSkill("result"));
    }

    [Fact]
    public void Runtime_PlayerReference_CanBeRegisteredForScripts()
    {
        Level level = Load("MAPFILE <m.wmp>; ACTION nudgePlayer { SET player.x, 12; RULE player.y += 3; }");
        var runtime = new WdlRuntime(level);
        var player = new Player(level);

        runtime.RegisterObject("player", player);
        Assert.True(runtime.RunAction("nudgePlayer"));

        Assert.Equal(12, player["x"]);
        Assert.Equal(3, player["y"]);
    }

    [Fact]
    public void Runtime_LocateInstruction_ReassignsObjectRegionFromPosition()
    {
        Level level = LoadWithMap(
            TwoRoomMap,
            "MAPFILE <m.wmp>; " +
            "ACTION beamPickup { SET pickup.x, 15; SET pickup.y, 5; SET pickup.region, 0; LOCATE pickup; }");
        Thing pickup = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("beamPickup"));

        Assert.Equal(15, pickup.X);
        Assert.Equal(5, pickup.Y);
        Assert.Equal(1, pickup.Region);
    }

    [Fact]
    public void Runtime_LocateInstructionWithoutObject_ReassignsPlayerAndHere()
    {
        Level level = LoadWithMap(TwoRoomMap, "MAPFILE <m.wmp>; ACTION locatePlayer { LOCATE; }");
        var runtime = new WdlRuntime(level);
        var player = new Player(level);
        player.MoveTo(0, 15, 5);
        runtime.RegisterObject("player", player);
        // The host keeps PLAYER_X/PLAYER_Y on the player's position; LOCATE reads them, since a script
        // beams the player by setting them (OV-20261029-slope).
        runtime.SetSkill("PLAYER_X", player.Position.X);
        runtime.SetSkill("PLAYER_Y", player.Position.Z);

        Assert.True(runtime.RunAction("locatePlayer"));

        Assert.Equal(1, player.Region);
        Assert.Same(level.Regions[1], runtime.GetObject("here"));
    }

    [Fact]
    public void Runtime_ShakeInstruction_RecordsGeometryInvalidation()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING pickup 2 3 90 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(map, "MAPFILE <m.wmp>; ACTION moved { SET pickup.x, 7; SHAKE pickup; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("moved"));

        WdlShakeRequest request = Assert.Single(runtime.ShakeRequests);
        Assert.Equal("pickup", request.ObjectName);
    }

    [Fact]
    public void Runtime_ShakeInstruction_ResolvesMyReference()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING pickup 2 3 90 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(map, "MAPFILE <m.wmp>; THING pickup { IF_NEAR moved; } ACTION moved { SHAKE my; }");
        Thing pickup = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.TriggerObjectActionHook(pickup, "IF_NEAR"));

        WdlShakeRequest request = Assert.Single(runtime.ShakeRequests);
        Assert.Equal("pickup", request.ObjectName);
    }

    [Fact]
    public void Runtime_DropInstruction_PlacesEntityAlongPlayerSightLine()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nVERTEX 20 20 0;#2\nVERTEX 0 20 0;#3\n" +
            "REGION room 0 10;#0\n" +
            "WALL bottom 0 1 0 -1 0 0;#0\nWALL right 1 2 0 -1 0 0;#1\n" +
            "WALL top 2 3 0 -1 0 0;#2\nWALL left 3 0 0 -1 0 0;#3\n" +
            "THING pickup 0 0 0 0;#4\nPLAYER_START 5 5 0 0;#5";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; ACTION dropIt { SET pickup.DIST, 4; SET PLAYER_ANGLE, 1.5707963267948966; DROP pickup; }");
        Thing pickup = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);
        var player = new Player(level);
        runtime.RegisterObject("player", player);

        Assert.True(runtime.RunAction("dropIt"));

        Assert.Equal(5, pickup.X, 2); // the angle literal is held as 1.5703 (OV-20261029-numeric-edge)
        Assert.Equal(9, pickup.Y, 2);
        Assert.Equal(0, pickup.Region);
        Assert.Equal("pickup", Assert.Single(runtime.ShakeRequests).ObjectName);
    }

    [Fact]
    public void Runtime_ShootInstruction_TriggersNearestObjectInPlayerLineOfSight()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nVERTEX 20 20 0;#2\nVERTEX 0 20 0;#3\n" +
            "REGION room 0 10;#0\n" +
            "WALL bottom 0 1 0 -1 0 0;#0\nWALL right 1 2 0 -1 0 0;#1\n" +
            "WALL top 2 3 0 -1 0 0;#2\nWALL left 3 0 0 -1 0 0;#3\n" +
            "THING switch 9 5 0 0;#4\nPLAYER_START 5 5 0 0;#5";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL hit_seen { VAL 0; } THING switch { IF_HIT hit_switch; } " +
            "ACTION fire { SET PLAYER_ANGLE, 0; SET SHOOT_RANGE, 10; SET SHOOT_FAC, 1; SHOOT; } " +
            "ACTION hit_switch { SET hit_seen, 1; }");
        Thing target = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", new Player(level));

        Assert.True(runtime.RunAction("fire"));

        Assert.Equal(4, runtime.GetSkill("HIT_DIST"), 6);
        Assert.Equal(0.6, runtime.GetSkill("RESULT"), 6);
        Assert.Equal(1, runtime.GetSkill("hit_seen"));
        Assert.Same(target, runtime.GetObject("HIT"));
    }

    [Fact]
    public void Runtime_ShootInstruction_ContinuesThroughTransparentSpritePixel()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nVERTEX 20 10 0;#2\nVERTEX 0 10 0;#3\n" +
            "REGION room 0 10;#0\n" +
            "THING near 8 5 0 0;#1\nTHING far 12 5 0 0;#2\nPLAYER_START 5 5 0 0;#3";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; " +
            "BMAP nearMap,<near.pcx>,0,0,2,6; BMAP farMap,<far.pcx>,0,0,2,6; " +
            "TEXTURE nearTex { BMAPS nearMap; SCALE_X 1; SCALE_Y 1; } " +
            "TEXTURE farTex { BMAPS farMap; SCALE_X 1; SCALE_Y 1; } " +
            "THING near { TEXTURE nearTex; } THING far { TEXTURE farTex; } " +
            "ACTION fire { SET PLAYER_ANGLE,0; SET SHOOT_RANGE,20; SHOOT; }");
        IReadOnlyDictionary<string, TextureImage> images = new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase)
        {
            ["nearTex"] = SolidImage(2, 6, alpha: 0),
            ["farTex"] = SolidImage(2, 6, alpha: 255),
        };
        var runtime = new WdlRuntime(level, hitTextures: images);
        runtime.RegisterObject("player", new Player(level));

        Assert.True(runtime.RunAction("fire"));

        Assert.Same(level.Things.Single(thing => thing.Name == "far"), runtime.GetObject("hit"));
        Assert.Equal(7, runtime.GetSkill("HIT_DIST"), 6);
        // The texel column the renderer steps to at the centre column: 0.714 pixels past the half-way
        // point at a texels-per-pixel step cut to 22.10 (24/1024 here), a little short of 1
        // (OV-20261009-shoot-mapping, sweep).
        Assert.Equal(1012 / 1024.0, runtime.GetSkill("HIT_X"), 6);
        Assert.Equal(4, runtime.GetSkill("HIT_Y")); // the sampled row + 1 (OV-20261009-shoot-mapping)
    }

    [Fact]
    public void Runtime_ShootInstruction_ReportsMirroredSpritePixelCoordinates()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nREGION room 0 10;#0\n" +
            "THING target 9 4.5 0 0;#1\nPLAYER_START 5 5 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; " +
            "BMAP front,<front.pcx>,0,0,2,6; BMAP back,<back.pcx>,0,0,2,6; " +
            "TEXTURE targetTex { SIDES 2; CYCLES 1; BMAPS front,back; MIRROR 0,1; SCALE_X 1; SCALE_Y 1; } " +
            "THING target { TEXTURE targetTex; } " +
            "ACTION fire { SET PLAYER_ANGLE,0; SET SHOOT_RANGE,10; SHOOT; }");
        IReadOnlyDictionary<string, TextureImage> images = new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase)
        {
            [TextureAnimation.FrameKey("targetTex", 1)] = SolidImage(2, 6, alpha: 255),
        };
        var runtime = new WdlRuntime(level, hitTextures: images);
        runtime.RegisterObject("player", new Player(level));

        Assert.True(runtime.RunAction("fire"));

        Assert.Same(Assert.Single(level.Things), runtime.GetObject("hit"));
        Assert.Equal(1, runtime.GetSkill("HIT_X"));
        Assert.Equal(4, runtime.GetSkill("HIT_Y")); // the sampled row + 1 (OV-20261009-shoot-mapping)
    }

    [Fact]
    public void Runtime_ShootInstruction_ShootYUsesProjectedVerticalAim()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nREGION room 0 12;#0\n" +
            "THING highTarget 9 5 0 0;#1\nPLAYER_START 5 5 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; BMAP targetMap,<target.pcx>,0,0,2,6; " +
            "TEXTURE targetTex { BMAPS targetMap; SCALE_X 1; SCALE_Y 1; } " +
            "THING highTarget { TEXTURE targetTex; HEIGHT 4; } " +
            "ACTION fire { SET PLAYER_ANGLE,0; SET PLAYER_ARC,1; SET SHOOT_Y,1; " +
            "SET SHOOT_RANGE,20; SHOOT; }");
        IReadOnlyDictionary<string, TextureImage> images = new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase)
        {
            ["targetTex"] = SolidImage(2, 6, alpha: 255),
        };
        var runtime = new WdlRuntime(level, hitTextures: images);
        runtime.RegisterObject("player", new Player(level));

        Assert.True(runtime.RunAction("fire"));

        // OV-20261009-pixel-aspect-video-modes + OV-20261009-shoot-mapping: SHOOT_Y = +1 aims at
        // the top window edge (positive is up), whose vertical half-tangent is (2/3)·tan(0.5) =
        // 0.364 at the default 640x480 window, so the ray rises 1.457 over the 4-step horizontal
        // distance: eye 3 + 1.457 = 4.457, i.e. 5.54 texture rows below the sprite's top at 10
        // (HIT_Y 5). HIT_DIST is the depth along the line of sight (4), not the ray length.
        Assert.Same(Assert.Single(level.Things), runtime.GetObject("hit"));
        Assert.Equal(4, runtime.GetSkill("HIT_DIST"), 6);
        Assert.Equal(6, runtime.GetSkill("HIT_Y")); // the sampled row + 1 (OV-20261009-shoot-mapping)
    }

    [Fact]
    public void Runtime_ShootInstruction_AspectTwoWidensVerticalAim()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nREGION room 0 12;#0\n" +
            "THING highTarget 9 5 0 0;#1\nPLAYER_START 5 5 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; BMAP targetMap,<target.pcx>,0,0,2,6; " +
            "TEXTURE targetTex { BMAPS targetMap; SCALE_X 1; SCALE_Y 1; } " +
            "THING highTarget { TEXTURE targetTex; HEIGHT 4; } " +
            "ACTION fire { SET PLAYER_ANGLE,0; SET PLAYER_ARC,1; SET ASPECT,2; SET SHOOT_Y,1; " +
            "SET SHOOT_RANGE,20; SHOOT; }");
        IReadOnlyDictionary<string, TextureImage> images = new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase)
        {
            ["targetTex"] = SolidImage(2, 6, alpha: 255),
        };
        var runtime = new WdlRuntime(level, hitTextures: images);
        runtime.RegisterObject("player", new Player(level));

        Assert.True(runtime.RunAction("fire"));

        // ASPECT 2 halves the vertical scale (f_v 329.5 px), so the top edge sits at half-tangent
        // (2/3)·2·tan(0.5) = 0.728: rise 2.914 over 4 steps, hit at height 5.914, 4.09 rows
        // below the top (HIT_Y 4); HIT_DIST stays the depth 4. (ASPECT 0.5 would double f_v
        // and the same SHOOT_Y would pass under the sprite.)
        Assert.Same(Assert.Single(level.Things), runtime.GetObject("hit"));
        Assert.Equal(4, runtime.GetSkill("HIT_DIST"), 6);
        Assert.Equal(5, runtime.GetSkill("HIT_Y")); // the sampled row + 1 (OV-20261009-shoot-mapping)
    }

    [Fact]
    public void Runtime_ShootInstruction_NegativeTiltRaisesCenterRayLikeShootY()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nREGION room 0 12;#0\n" +
            "THING highTarget 9 5 0 0;#1\nPLAYER_START 5 5 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; BMAP targetMap,<target.pcx>,0,0,2,6; " +
            "TEXTURE targetTex { BMAPS targetMap; SCALE_X 1; SCALE_Y 1; } " +
            "THING highTarget { TEXTURE targetTex; HEIGHT 4; } " +
            "ACTION fire { SET PLAYER_ANGLE,0; SET PLAYER_ARC,1; SET PLAYER_TILT,-0.5; " +
            "SET SHOOT_RANGE,20; SHOOT; }");
        IReadOnlyDictionary<string, TextureImage> images = new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase)
        {
            ["targetTex"] = SolidImage(2, 6, alpha: 255),
        };
        var runtime = new WdlRuntime(level, hitTextures: images);
        runtime.RegisterObject("player", new Player(level));

        Assert.True(runtime.RunAction("fire"));

        // OV-20261009-tilt-model-shear-vs-pitch: tilt -0.5 moves the eye line to the bottom
        // edge, so the center ray leaves through the old top edge: the same ray as SHOOT_Y = +1
        // (OV-20261009-shoot-mapping confirmed the shear acts on the SHOOT ray: tilt +0.1 lowered
        // the hit 3.2 steps at 44 steps).
        Assert.Same(Assert.Single(level.Things), runtime.GetObject("hit"));
        Assert.Equal(4, runtime.GetSkill("HIT_DIST"), 6);
        Assert.Equal(6, runtime.GetSkill("HIT_Y")); // the sampled row + 1 (OV-20261009-shoot-mapping)
    }

    [Fact]
    public void Runtime_ShootInstruction_PositiveShootXAimsToScreenRight()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nREGION room 0 10;#0\n" +
            "THING rightTarget 9 3 0 0;#1\nPLAYER_START 5 5 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; BMAP targetMap,<target.pcx>,0,0,2,6; " +
            "TEXTURE targetTex { BMAPS targetMap; SCALE_X 1; SCALE_Y 1; } " +
            "THING rightTarget { TEXTURE targetTex; } " +
            "ACTION fire { SET PLAYER_ANGLE,0; SET PLAYER_ARC,1; SET SHOOT_X,0.7; " +
            "SET SHOOT_RANGE,20; SHOOT; }");
        IReadOnlyDictionary<string, TextureImage> images = new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase)
        {
            ["targetTex"] = SolidImage(2, 6, alpha: 255),
        };
        var runtime = new WdlRuntime(level, hitTextures: images);
        runtime.RegisterObject("player", new Player(level));

        Assert.True(runtime.RunAction("fire"));

        // OV-20261009-shoot-mapping: positive SHOOT_X hit the map −y target (R), and HIT_DIST
        // read 44.000 for a target 44 steps ahead with the ray 12 steps aside — the depth
        // along the line of sight, not the 45.6-step ray length (here 4, not 4.45).
        Assert.Same(Assert.Single(level.Things), runtime.GetObject("hit"));
        Assert.Equal(4, runtime.GetSkill("HIT_DIST"), 6);
        Assert.Equal(1 - (4.0 / 20.0), runtime.GetSkill("RESULT"), 6);
    }

    [Fact]
    public void Runtime_ShootInstruction_WallHitXCountsFromTheSecondVertex()
    {
        // OV-20261009-shoot-mapping: the far-wall bitmap's left half appeared on the screen left,
        // i.e. at the wall's SECOND vertex for a viewer in the wall's left region. The wall runs
        // v1 (10, 0) → v2 (10, 10); the player at (5, 3) facing +x hits (10, 3), 7 steps from v2.
        const string map =
            "VERTEX 10 0 0;#0\nVERTEX 10 10 0;#1\nREGION room 0 10;#0\n" +
            "WALL target 0 1 0 -1 0 0;#0\nPLAYER_START 5 3 0 0;#1";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; BMAP wallMap,<wall.pcx>,0,0,64,64; " +
            "TEXTURE wallTex { BMAPS wallMap; SCALE_X 16; SCALE_Y 16; } " +
            "WALL target { TEXTURE wallTex; } " +
            "ACTION fire { SET PLAYER_ANGLE,0; SET SHOOT_RANGE,20; SHOOT; }");
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", new Player(level));

        Assert.True(runtime.RunAction("fire"));

        Assert.Same(Assert.Single(level.Walls), runtime.GetObject("hit"));
        Assert.Equal(7 * 16, runtime.GetSkill("HIT_X"));
        Assert.Equal(5, runtime.GetSkill("HIT_DIST"), 6);
    }

    [Fact]
    public void Runtime_ShootInstruction_CrossesOpenPortalToHitVisibleActor()
    {
        const string map =
            "VERTEX 10 0 0;#0\nVERTEX 10 10 0;#1\n" +
            "REGION near 0 10;#0\nREGION far 0 10;#1\n" +
            "WALL portal 0 1 0 1 0 0;#0\n" +
            "ACTOR guard 15 5 0 1;#1\nPLAYER_START 5 5 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL hit_seen { VAL 0; } " +
            "ACTOR guard { IF_HIT hit_guard; } " +
            "ACTION fire { SET PLAYER_ANGLE,0; SET SHOOT_RANGE,20; SHOOT; } " +
            "ACTION hit_guard { SET hit_seen,1; }");
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", new Player(level));

        Assert.True(runtime.RunAction("fire"));

        Assert.Same(Assert.Single(level.Actors), runtime.GetObject("hit"));
        Assert.Equal(10, runtime.GetSkill("HIT_DIST"), 6);
        Assert.Equal(1, runtime.GetSkill("hit_seen"));
    }

    [Fact]
    public void Runtime_ShootInstruction_HitsPortalWallWhenOpeningIsAboveEyeHeight()
    {
        const string map =
            "VERTEX 10 0 0;#0\nVERTEX 10 10 0;#1\n" +
            "REGION near 0 10;#0\nREGION far 4 10;#1\n" +
            "WALL portal 0 1 0 1 0 0;#0\n" +
            "ACTOR guard 15 5 0 1;#1\nPLAYER_START 5 5 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; ACTION fire { SET PLAYER_ANGLE,0; SET SHOOT_RANGE,20; SHOOT; }");
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", new Player(level));

        Assert.True(runtime.RunAction("fire"));

        Assert.Same(Assert.Single(level.Walls), runtime.GetObject("hit"));
        Assert.Equal(5, runtime.GetSkill("HIT_DIST"), 6);
    }

    [Fact]
    public void Runtime_ShootInstruction_WallHitReportsOffsetTexturePixelsBeforeHook()
    {
        const string map =
            "VERTEX 10 0 0;#0\nVERTEX 10 10 0;#1\nREGION room 0 10;#0\n" +
            "WALL target 0 1 0 -1 4 8;#0\nPLAYER_START 5 5 0 0;#1";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL seen_x { VAL 0; } SKILL seen_y { VAL 0; } " +
            "BMAP wallMap,<wall.pcx>,0,0,64,64; " +
            "TEXTURE wallTex { BMAPS wallMap; SCALE_X 16; SCALE_Y 16; } " +
            "WALL target { TEXTURE wallTex; IF_HIT record_hit; } " +
            "ACTION fire { SET PLAYER_ANGLE,0; SET SHOOT_RANGE,20; SHOOT; } " +
            "ACTION record_hit { SET seen_x,HIT_X; SET seen_y,HIT_Y; }");
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", new Player(level));

        Assert.True(runtime.RunAction("fire"));

        Assert.Same(Assert.Single(level.Walls), runtime.GetObject("hit"));
        Assert.Equal(84, runtime.GetSkill("HIT_X"));
        Assert.Equal(120, runtime.GetSkill("HIT_Y"));
        Assert.Equal(84, runtime.GetSkill("seen_x"));
        Assert.Equal(120, runtime.GetSkill("seen_y"));
    }

    [Fact]
    public void Runtime_ShootInstructionWithObject_ChecksPlayerVisibilityAndAngle()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nVERTEX 20 20 0;#2\nVERTEX 0 20 0;#3\n" +
            "REGION room 0 10;#0\n" +
            "WALL bottom 0 1 0 -1 0 0;#0\nWALL right 1 2 0 -1 0 0;#1\n" +
            "WALL top 2 3 0 -1 0 0;#2\nWALL left 3 0 0 -1 0 0;#3\n" +
            "ACTOR guard 5 5 0 0;#4\nPLAYER_START 9 5 0 0;#5";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; ACTION checkSight { SET SHOOT_RANGE, 10; SET SHOOT_FAC, 1; SET SHOOT_SECTOR, 3.141592653589793; SHOOT guard; }");
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", new Player(level));

        Assert.True(runtime.RunAction("checkSight"));

        Assert.Equal(4, runtime.GetSkill("HIT_DIST"), 6);
        Assert.Equal(0.6, runtime.GetSkill("RESULT"), 6);
        Assert.Equal(0, runtime.GetSkill("SHOOT_ANGLE"), 6);
    }

    [Fact]
    public void Runtime_ShootInstructionWithObject_SeesPlayerThroughOpenPortal()
    {
        const string map =
            "VERTEX 10 0 0;#0\nVERTEX 10 10 0;#1\n" +
            "REGION near 0 10;#0\nREGION far 0 10;#1\n" +
            "WALL portal 0 1 0 1 0 0;#0\n" +
            "ACTOR guard 15 5 0 1;#1\nPLAYER_START 5 5 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; ACTION checkSight { SET SHOOT_RANGE,20; " +
            "SET SHOOT_SECTOR,6.283185307179586; SHOOT guard; }");
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", new Player(level));

        Assert.True(runtime.RunAction("checkSight"));

        Assert.Equal(10, runtime.GetSkill("HIT_DIST"), 6);
        Assert.Equal(0.5, runtime.GetSkill("RESULT"), 6);
    }

    [Fact]
    public void Runtime_ShootInstructionWithObject_IsBlockedByClosedPortalHeight()
    {
        const string map =
            "VERTEX 10 0 0;#0\nVERTEX 10 10 0;#1\n" +
            "REGION near 0 10;#0\nREGION far 4 10;#1\n" +
            "WALL portal 0 1 0 1 0 0;#0\n" +
            "ACTOR guard 15 5 0 1;#1\nPLAYER_START 5 5 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; ACTION checkSight { SET SHOOT_RANGE,20; " +
            "SET SHOOT_SECTOR,6.283185307179586; SHOOT guard; }");
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", new Player(level));

        Assert.True(runtime.RunAction("checkSight"));

        Assert.Equal(0, runtime.GetSkill("HIT_DIST"));
        Assert.Equal(0, runtime.GetSkill("RESULT"));
    }

    [Fact]
    public void Runtime_PushInstruction_UsesExplicitInteractionDistance()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nVERTEX 20 20 0;#2\nVERTEX 0 20 0;#3\n" +
            "REGION room 0 10;#0\n" +
            "WALL bottom 0 1 0 -1 0 0;#0\nWALL right 1 2 0 -1 0 0;#1\n" +
            "WALL top 2 3 0 -1 0 0;#2\nWALL left 3 0 0 -1 0 0;#3\n" +
            "THING switch 8 5 0 0;#4\nPLAYER_START 5 5 0 0;#5";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL pushed { VAL 0; } THING switch { IF_HIT pushed_switch; } " +
            "ACTION use { SET PLAYER_ANGLE, 0; SET SHOOT_X, 1; PUSH 4; } " +
            "ACTION pushed_switch { SET pushed, 1; }");
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", new Player(level));

        Assert.True(runtime.RunAction("use"));

        Assert.Equal(3, runtime.GetSkill("HIT_DIST"), 6);
        Assert.Equal(0.25, runtime.GetSkill("RESULT"), 6);
        Assert.Equal(1, runtime.GetSkill("pushed"));
    }

    [Fact]
    public void Runtime_ExplodeInstruction_TriggersFragileTargetsAndReportsPlayerHit()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nVERTEX 20 20 0;#2\nVERTEX 0 20 0;#3\n" +
            "REGION room 0 10;#0\n" +
            "WALL bottom 0 1 0 -1 0 0;#0\nWALL right 1 2 0 -1 0 0;#1\n" +
            "WALL top 2 3 0 -1 0 0;#2\nWALL left 3 0 0 -1 0 0;#3\n" +
            "ACTOR barrel 5 5 0 0;#4\nTHING crate 7 5 0 0;#5\nTHING stone 15 15 0 0;#6\nPLAYER_START 8 5 0 0;#7";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL crate_hit { VAL 0; } SKILL stone_hit { VAL 0; } " +
            "THING crate { FLAGS FRAGILE; IF_HIT hit_crate; } THING stone { FLAGS FRAGILE; IF_HIT hit_stone; } " +
            "ACTION boom { SET SHOOT_RANGE, 4; SET SHOOT_FAC, 1; EXPLODE barrel; } " +
            "ACTION hit_crate { SET crate_hit, RESULT; } ACTION hit_stone { SET stone_hit, 1; }");
        Thing crate = level.Things.Single(thing => thing.Name == "crate");
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", new Player(level));

        Assert.True(runtime.RunAction("boom"));

        Assert.Equal(0.5, runtime.GetSkill("crate_hit"), 6);
        Assert.Equal(0, runtime.GetSkill("stone_hit"));
        Assert.Equal(2, runtime.GetSkill("HIT_MINDIST"), 6);
        Assert.Equal(3, runtime.GetSkill("HIT_DIST"), 6);
        Assert.Equal(0.25, runtime.GetSkill("RESULT"), 6);
        Assert.Same(crate, runtime.GetObject("HIT"));
    }

    [Fact]
    public void Runtime_ShiftInstruction_MovesRegionVerticesObjectsAndPlayer()
    {
        Level level = LoadWithMap(TwoRoomMap, "MAPFILE <m.wmp>; ACTION moveLeft { SHIFT left, 2, -1; }");
        Thing pickup = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);
        var player = new Player(level);
        runtime.RegisterObject("player", player);

        Assert.True(runtime.RunAction("moveLeft"));

        Assert.Equal(2, level.Vertices[0].X);
        Assert.Equal(-1, level.Vertices[0].Y);
        Assert.Equal(12, level.Vertices[1].X);
        Assert.Equal(-1, level.Vertices[1].Y);
        Assert.Equal(7, pickup.X);
        Assert.Equal(4, pickup.Y);
        Assert.Equal(7f, player.Position.X);
        Assert.Equal(4f, player.Position.Z);
        Assert.Equal("left", Assert.Single(runtime.ShakeRequests).ObjectName);
    }

    [Fact]
    public void Runtime_LiftInstruction_MovesRegionVerticesAndContainedObjectsVertically()
    {
        Level level = LoadWithMap(TwoRoomMap, "MAPFILE <m.wmp>; ACTION liftLeft { LIFT left, 3; }");
        Thing pickup = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("liftLeft"));

        Assert.Equal(3, level.Vertices[0].Z);
        Assert.Equal(3, level.Vertices[1].Z);
        Assert.Equal(0, level.Vertices[4].Z);
        Assert.Equal(3, pickup.Z);
        Assert.Equal("left", Assert.Single(runtime.ShakeRequests).ObjectName);
    }

    [Fact]
    public void Runtime_RotateInstruction_RotatesRegionVerticesObjectsAndPlayerAroundGenius()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nVERTEX 10 10 0;#2\nVERTEX 0 10 0;#3\n" +
            "REGION room 0 10;#0\n" +
            "WALL bottom 0 1 0 -1 0 0;#0\nWALL right 1 2 0 -1 0 0;#1\n" +
            "WALL top 2 3 0 -1 0 0;#2\nWALL left 3 0 0 -1 0 0;#3\n" +
            "THING center 5 5 0 0;#4\nTHING pickup 7 5 0 0;#5\nPLAYER_START 7 5 0 0;#6";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; REGION room { GENIUS center; } ACTION turn { ROTATE room, 1.5707963267948966; }");
        Thing pickup = Assert.Single(level.Things, thing => thing.Name == "pickup");
        var runtime = new WdlRuntime(level);
        var player = new Player(level);
        runtime.RegisterObject("player", player);

        Assert.True(runtime.RunAction("turn"));

        // The literal angle is held as 1.5703 (fixed point, OV-20261029-numeric-edge), a quarter
        // turn to within 0.0005 rad.
        Assert.Equal(10, level.Vertices[0].X, 2);
        Assert.Equal(0, level.Vertices[0].Y, 2);
        Assert.Equal(10, level.Vertices[1].X, 2);
        Assert.Equal(10, level.Vertices[1].Y, 2);
        Assert.Equal(5, pickup.X, 2);
        Assert.Equal(7, pickup.Y, 2);
        Assert.Equal(1.5707963267948966, pickup.Angle, 2);
        Assert.Equal(5f, player.Position.X, 2);
        Assert.Equal(7f, player.Position.Z, 2);
        Assert.Equal("room", Assert.Single(runtime.ShakeRequests).ObjectName);
    }

    /// <summary>
    /// Boots and ticks every local retail level when user-supplied game data is present.
    /// This guards the current runtime skeleton without asserting hidden scheduler parity.
    /// </summary>
    [Fact]
    public void Runtime_LocalRetailArchives_BootAndTickWithoutThrowing()
    {
        IReadOnlyList<string> archives = ResearchData.WrsFiles();
        if (archives.Count == 0)
        {
            return;
        }

        foreach (string path in archives)
        {
            Level level = LevelLoader.Load(WrsArchive.ReadFile(path), ResearchData.MainWdlName(path));
            var runtime = new WdlRuntime(level);

            runtime.RunStartup();
            for (int i = 0; i < 3; i++)
            {
                runtime.Tick(1.0 / 16);
            }

            Assert.Equal(1.0, runtime.GetSkill("TIME_CORR"), 9);
        }
    }

    [Fact]
    public void Runtime_RealApathy_HasManyActions()
    {
        string? apathy = ResearchData.WrsFiles()
            .FirstOrDefault(p => Path.GetFileName(p).Equals("apathy.wrs", StringComparison.OrdinalIgnoreCase));
        if (apathy is null)
        {
            return; // no local game data — skip
        }

        Level level = LevelLoader.Load(WrsArchive.ReadFile(apathy), "APATHY.WDL");

        Assert.NotEmpty(level.Actions);
    }

    [Fact]
    public void Tick_FirstCycle_KeepsTimeCorrAtOneAndTicksAtZero()
    {
        // OV-20261029-scheduler-order: IF_START and the first cycle read TIME_CORR 1; TICKS counts
        // from the second cycle on, whatever the first cycle lasted.
        var runtime = new WdlRuntime(Load("MAPFILE <m.wmp>;"));
        Assert.Equal(1.0, runtime.GetSkill("TIME_CORR"), 9);

        double tc = runtime.Tick(0.5);

        Assert.Equal(1.0, tc, 9);
        Assert.Equal(1.0, runtime.GetSkill("TIME_FAC"), 9);
        Assert.Equal(0.0, runtime.GetSkill("TICKS"), 9);
        Assert.Equal(0.25, runtime.Tick(1.0 / 64), 9);
        Assert.Equal(0.25, runtime.GetSkill("TICKS"), 9);
    }

    [Fact]
    public void Tick_SetsTimeCorrectionSkill()
    {
        var runtime = new WdlRuntime(Load("MAPFILE <m.wmp>;"));
        runtime.Tick(0); // the start-up cycle

        double tc = runtime.Tick(1.0 / 16); // a 16 fps frame

        Assert.Equal(1.0, tc, 9);
        Assert.Equal(1.0, runtime.GetSkill("TIME_CORR"), 9);
        Assert.Equal(1.0, runtime.GetSkill("TIME_FAC"), 9);
        Assert.Equal(1.0, runtime.GetSkill("TICKS"), 9);
        Assert.Equal(0, runtime.GetSkill("SECS")); // whole seconds only (OV-20261015-each-sec-cadence)
    }

    [Fact]
    public void Tick_AccumulatesDocumentedTimingSkillsBeforeScheduledActions()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL observed { VAL 0; } " +
            "EACH_TICK probe; " +
            "ACTION probe { SET observed, TIME_FAC * 100 + TICKS * 10 + SECS; IF (TICKS > 0) { SET EACH_TICK.1, NULL; } }");
        var runtime = new WdlRuntime(level);
        runtime.Tick(0); // the start-up cycle

        runtime.Tick(1.0 / 32);

        Assert.Equal(0.5, runtime.GetSkill("TIME_CORR"), 9);
        Assert.Equal(2.0, runtime.GetSkill("TIME_FAC"), 9);
        Assert.Equal(0.5, runtime.GetSkill("TICKS"), 9);
        Assert.Equal(0, runtime.GetSkill("SECS"));
        Assert.Equal(205, runtime.GetSkill("observed"), 9);

        runtime.Tick(1.0 / 16);

        Assert.Equal(1.5, runtime.GetSkill("TICKS"), 9);
        Assert.Equal(0, runtime.GetSkill("SECS"));
    }

    [Fact]
    public void Tick_TimeCorrection_ScalesScriptMovement()
    {
        // pos += speed * TIME_CORR — the canonical frame-rate-independent step.
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL pos { VAL 0; } SKILL speed { VAL 32; } " +
            "ACTION move { RULE pos += speed * TIME_CORR; }");
        var runtime = new WdlRuntime(level);

        runtime.Tick(1.0 / 16); // TIME_CORR = 1   -> full step
        runtime.RunAction("move");
        Assert.Equal(32, runtime.GetSkill("pos"), 9);

        runtime.Tick(1.0 / 32); // TIME_CORR = 0.5 -> half step
        runtime.RunAction("move");
        Assert.Equal(48, runtime.GetSkill("pos"), 9); // 32 + 16
    }

    [Fact]
    public void Runtime_AccelInstruction_CanAccelerateObjectMember()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING runner 5 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL force { VAL 6; } ACTION accelRunner { " +
            "SET runner.SPEED, 4; SET TIME_CORR, 0.5; SET FRICTION, 0.25; SET INERTIA, 2; ACCEL runner.SPEED, force; }");
        Thing runner = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("accelRunner"));

        Assert.Equal(5.25, runner["SPEED"], 9); // 4 + 0.5·(6 - 0.25·4) / 2 (OV-20261029-motion-timing)
    }

    [Fact]
    public void Tick_DispatchesRegisteredEachTickAction_WithMyAndTimeCorrection()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING runner 5 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL speed { VAL 4; } SKILL cycles { VAL 0; } " +
            "ACTION cycle { RULE my.x += speed * TIME_CORR; RULE cycles += 1; }");
        Thing runner = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);
        var originalMy = new Thing("originalMy");

        runtime.RegisterObject("my", originalMy);
        runtime.RegisterEachTick(runner, "cycle");

        runtime.Tick(1.0 / 16);
        Assert.Equal(9, runner.X);
        Assert.Equal(1, runtime.GetSkill("cycles"));

        runtime.Tick(1.0 / 32);
        Assert.Equal(11, runner.X);
        Assert.Equal(2, runtime.GetSkill("cycles"));
        Assert.Same(originalMy, runtime.GetObject("my"));
    }

    [Fact]
    public void Tick_DispatchesDocumentedEntityEachTickAction_WithMyAndTimeCorrection()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING runner 5 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL speed { VAL 4; } SKILL tick_count { VAL 0; } " +
            "THING runner { EACH_TICK object_tick; EACH_CYCLE object_cycle; } " +
            "ACTION object_tick { RULE my.x += speed * TIME_CORR; RULE tick_count += 1; } " +
            "ACTION object_cycle { RULE tick_count += 100; }");
        Thing runner = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);
        var originalMy = new Thing("originalMy");

        runtime.RegisterObject("my", originalMy);

        runtime.Tick(1.0 / 16);
        Assert.Equal(9, runner.X);
        Assert.Equal(1, runtime.GetSkill("tick_count"));

        runtime.Tick(1.0 / 32);
        Assert.Equal(11, runner.X);
        Assert.Equal(2, runtime.GetSkill("tick_count"));
        Assert.Same(originalMy, runtime.GetObject("my"));
    }

    [Fact]
    public void Tick_UsesLiveObjectEachTickAssignment()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING runner 5 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL object_ticks { VAL 0; } " +
            "ACTION assign { SET my.EACH_TICK, object_tick; } " +
            "ACTION object_tick { RULE object_ticks += 1; SET my.EACH_TICK, NULL; }");
        Thing runner = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);

        runtime.RegisterObject("my", runner);
        Assert.True(runtime.RunAction("assign"));
        Assert.Equal("object_tick", runner.EachTickAction);

        runtime.Tick(1.0 / 16);
        runtime.Tick(1.0 / 16);

        Assert.Equal(1, runtime.GetSkill("object_ticks"));
        Assert.Null(runner.EachTickAction);
    }

    [Fact]
    public void TriggerEachCycle_DispatchesDocumentedEntityAction_WithoutFrameTickDispatch()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "THING runner 5 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL cycles { VAL 0; } THING runner { EACH_CYCLE object_cycle; } " +
            "ACTION object_cycle { RULE my.x += 2; RULE there.FLOOR_HGT += 3; RULE cycles += 1; }");
        Thing runner = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);
        var previousMy = new Thing("previousMy");
        var previousThere = new Region("previousThere");

        runtime.RegisterObject("my", previousMy);
        runtime.RegisterObject("there", previousThere);

        runtime.Tick(1.0 / 16);
        Assert.Equal(0, runtime.GetSkill("cycles"));

        Assert.True(runtime.TriggerEachCycle(runner));

        Assert.Equal(7, runner.X);
        Assert.Equal(3, level.Regions[0].FloorHeight);
        Assert.Equal(1, runtime.GetSkill("cycles"));
        Assert.Same(previousMy, runtime.GetObject("my"));
        Assert.Same(previousThere, runtime.GetObject("there"));
    }

    [Fact]
    public void TriggerEachCycle_UsesLiveAssignmentForWallsAndRegions()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "WALL gate 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL cycles { VAL 0; } REGION room { EACH_CYCLE region_cycle; } " +
            "WALL gate { EACH_CYCLE wall_cycle; } " +
            "ACTION region_cycle { RULE there.FLOOR_HGT += 5; RULE cycles += 1; } " +
            "ACTION wall_cycle { RULE my.OFFSET_X += 7; RULE cycles += 10; } " +
            "ACTION reassign { SET my.EACH_CYCLE, wall_again; } " +
            "ACTION wall_again { RULE my.OFFSET_X += 11; RULE cycles += 100; }");
        Region room = Assert.Single(level.Regions);
        Wall gate = Assert.Single(level.Walls);
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.TriggerEachCycle(room));
        Assert.Equal(5, room.FloorHeight);
        Assert.Equal(1, runtime.GetSkill("cycles"));

        Assert.True(runtime.TriggerEachCycle(gate));
        Assert.Equal(7, gate.OffsetX);
        Assert.Equal(11, runtime.GetSkill("cycles"));

        runtime.RegisterObject("my", gate);
        Assert.True(runtime.RunAction("reassign"));
        Assert.Equal("wall_again", gate.EachCycleAction);
        Assert.True(runtime.TriggerEachCycle(gate));
        Assert.Equal(18, gate.OffsetX);
        Assert.Equal(111, runtime.GetSkill("cycles"));
    }

    [Fact]
    public void Tick_AdvancesEntityTexturePhasesByDeclaredDelaysAndDispatchesEachCycleOnWrap()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "THING runner 5 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL cycles { VAL 0; } SOUND step,<STEP.WAV>; " +
            "BMAP phase1,<p1.pcx>,0,0,16,16; BMAP phase2,<p2.pcx>,0,0,16,16; " +
            "TEXTURE runnerTex { CYCLES 2; BMAPS phase1,phase2; DELAY 2,1; SOUND step; SCYCLES 0,1; } " +
            "THING runner { TEXTURE runnerTex; POSITION 0.75; EACH_CYCLE object_cycle; } " +
            "ACTION object_cycle { RULE my.x += 2; RULE cycles += 1; }");
        Thing runner = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);
        runtime.Tick(1.0 / 16); // the animation starts its first phase here (OV-20261029-anim-timing)

        runtime.Tick(1.0 / 16);

        Assert.Equal(1, runner["CYCLE"]);
        Assert.Equal(0.75, runner["POSITION"]);
        Assert.Equal(0, runtime.GetSkill("cycles"));

        runtime.Tick(1.0 / 16);
        Assert.Equal(2, runner["CYCLE"]);
        Assert.Equal(0, runtime.GetSkill("cycles"));
        WdlTextureSoundRequest textureSound = Assert.Single(runtime.TextureSoundRequests);
        Assert.Same(runner, textureSound.Target);
        Assert.Equal("step", textureSound.Texture.SoundName);
        Assert.Equal([false, true], textureSound.Texture.SoundCycles);

        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runner["CYCLE"]);
        Assert.Equal(7, runner.X);
        Assert.Equal(1, runtime.GetSkill("cycles"));
    }

    [Fact]
    public void Tick_TextureSoundWithoutScycles_TriggersOnFirstPhaseButSloopDoesNot()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "THING normal 0 0 0 0;#1\nTHING looped 0 0 0 0;#2\nPLAYER_START 0 0 0 0;#3";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SOUND step,<STEP.WAV>; " +
            "BMAP p1,<p1.pcx>,0,0,16,16; BMAP p2,<p2.pcx>,0,0,16,16; " +
            "TEXTURE normalTex { CYCLES 2; BMAPS p1,p2; DELAY 1,1; SOUND step; } " +
            "TEXTURE loopTex { CYCLES 2; BMAPS p1,p2; DELAY 1,1; SOUND step; FLAGS SLOOP; } " +
            "THING normal { TEXTURE normalTex; } THING looped { TEXTURE loopTex; }");
        var runtime = new WdlRuntime(level);
        runtime.Tick(1.0 / 16); // the animation starts its first phase here (OV-20261029-anim-timing)

        runtime.Tick(1.0 / 16);
        Assert.Empty(runtime.TextureSoundRequests);

        runtime.Tick(1.0 / 16);

        WdlTextureSoundRequest request = Assert.Single(runtime.TextureSoundRequests);
        Assert.Equal("normal", request.Target.Name);
    }

    [Fact]
    public void Tick_AdvancesWallAndRegionTexturePhasesAndInvalidatesGeometry()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "WALL gate 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL cycles { VAL 0; } " +
            "BMAP r1,<r1.pcx>,0,0,16,16; BMAP r2,<r2.pcx>,0,0,16,16; " +
            "BMAP w1,<w1.pcx>,0,0,16,16; BMAP w2,<w2.pcx>,0,0,16,16; " +
            "TEXTURE regionTex { CYCLES 2; BMAPS r1,r2; DELAY 1,1; } " +
            "TEXTURE wallTex { CYCLES 2; BMAPS w1,w2; DELAY 2,2; } " +
            "REGION room { FLOOR_TEX regionTex; POSITION 0.25; EACH_CYCLE region_cycle; } " +
            "WALL gate { TEXTURE wallTex; POSITION 0.9; EACH_CYCLE wall_cycle; } " +
            "ACTION region_cycle { RULE there.FLOOR_HGT += 5; RULE cycles += 1; } " +
            "ACTION wall_cycle { RULE my.OFFSET_X += 7; RULE cycles += 10; }");
        Region room = Assert.Single(level.Regions);
        Wall gate = Assert.Single(level.Walls);
        var runtime = new WdlRuntime(level);
        runtime.Tick(1.0 / 16); // the animation starts its first phase here (OV-20261029-anim-timing)

        runtime.Tick(1.0 / 16);

        Assert.Equal(2, room["CYCLE"]);
        Assert.Equal(1, gate["CYCLE"]);
        Assert.Equal(0.25, room["POSITION"]);
        Assert.Equal(WdlFixed.Literal(0.9), gate["POSITION"]); // 0.8994: fixed point (OV-20261029-numeric-edge)
        Assert.Empty(runtime.ShakeRequests);

        runtime.Tick(1.0 / 16);

        Assert.Equal(1, room["CYCLE"]);
        Assert.Equal(2, gate["CYCLE"]);
        Assert.Equal(5, room.FloorHeight);
        Assert.Equal(0, gate.OffsetX);
        Assert.Equal(1, runtime.GetSkill("cycles"));
        Assert.Empty(runtime.ShakeRequests);
    }

    [Fact]
    public void Tick_OneShotTexture_RequiresPlayAndStopsOnLastPhase()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "THING runner 5 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL cycles { VAL 0; } " +
            "BMAP phase1,<p1.pcx>,0,0,16,16; BMAP phase2,<p2.pcx>,0,0,16,16; " +
            "TEXTURE runnerTex { CYCLES 2; BMAPS phase1,phase2; DELAY 1,2; FLAGS ONESHOT; } " +
            "THING runner { TEXTURE runnerTex; EACH_CYCLE done; } " +
            "ACTION done { RULE cycles += 1; }");
        Thing runner = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);

        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runner["CYCLE"]);
        Assert.Equal(0, runtime.GetSkill("cycles"));

        // The loop was fixed in the first cycle (TIME_CORR 1), so the phases last 1 and 2 frames,
        // counted from the cycle after PLAY is seen (OV-20261029-anim-timing).
        runner["PLAY"] = 1;
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runner["CYCLE"]);
        runtime.Tick(1.0 / 16);
        Assert.Equal(2, runner["CYCLE"]);
        Assert.Equal(1, runner["PLAY"]);

        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runner["PLAY"]);
        runtime.Tick(1.0 / 16);

        Assert.Equal(2, runner["CYCLE"]);
        Assert.Equal(0, runner["PLAY"]);
        Assert.Equal(1, runtime.GetSkill("cycles"));
    }

    [Theory]
    [InlineData(0.25, new[] { 4, 8, 12 })]
    [InlineData(0.28125, new[] { 3, 7, 10 })]
    public void Tick_AnimationLoop_FixesPhaseFramesFromTheWrapCyclesTimeCorr(double timeCorrection, int[] frames)
    {
        // OV-20261029-anim-timing anim-delay: DELAY 1, 2, 3 lasted 4, 8, 12 frames in a loop that
        // started at TIME_CORR 0.25 and 3, 7, 10 in one that started at 0.281.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
            "THING runner 5 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; " +
            "BMAP p1,<p1.pcx>,0,0,16,16; BMAP p2,<p2.pcx>,0,0,16,16; BMAP p3,<p3.pcx>,0,0,16,16; " +
            "TEXTURE runnerTex { CYCLES 3; BMAPS p1,p2,p3; DELAY 1,2,3; } " +
            "THING runner { TEXTURE runnerTex; }");
        Thing runner = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);
        double seconds = timeCorrection / 16;

        // The first loop counts with the first cycle's TIME_CORR of 1: 1 + 2 + 3 frames, from the cycle
        // after the one that starts it.
        for (int i = 0; i < 7; i++)
        {
            runtime.Tick(seconds);
        }

        Assert.Equal(1, runner["CYCLE"]);
        Assert.Equal(timeCorrection, runtime.GetSkill("TIME_CORR"), 3);
        var lengths = new List<int>();
        int run = 0;
        double phase = runner["CYCLE"];
        while (lengths.Count < 3)
        {
            runtime.Tick(seconds);
            run++;
            if (runner["CYCLE"] != phase)
            {
                lengths.Add(run);
                run = 0;
                phase = runner["CYCLE"];
            }
        }

        Assert.Equal(frames, lengths);
    }

    [Fact]
    public void Tick_DispatchesDocumentedWallAndRegionEachTickActions_WithDocumentedReferences()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\nREGION old_room 5 15;#1\n" +
            "WALL gate 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL region_ticks { VAL 0; } SKILL region_my_x { VAL -1; } SKILL wall_ticks { VAL 0; } " +
            "REGION room { EACH_TICK region_tick; } WALL gate { EACH_TICK wall_tick; } " +
            "ACTION region_tick { SET region_my_x, my.X; RULE region_ticks += 1; RULE there.FLOOR_HGT += TIME_CORR; } " +
            "ACTION wall_tick { RULE my.OFFSET_X += TIME_CORR * 2; RULE wall_ticks += 1; }");
        var runtime = new WdlRuntime(level);
        var previousMy = new Thing("previousMy") { X = 99 };
        Region previousThere = level.Regions[1];

        runtime.RegisterObject("my", previousMy);
        runtime.RegisterObject("there", previousThere);

        runtime.Tick(1.0 / 16);

        Assert.Equal(1, level.Regions[0].FloorHeight);
        Assert.Equal(2, level.Walls[0].OffsetX);
        Assert.Equal(1, runtime.GetSkill("region_ticks"));
        Assert.Equal(0, runtime.GetSkill("region_my_x"));
        Assert.Equal(1, runtime.GetSkill("wall_ticks"));
        Assert.Same(previousMy, runtime.GetObject("my"));
        Assert.Same(previousThere, runtime.GetObject("there"));
    }

    [Fact]
    public void Runtime_ObjectActionHooks_TriggerWithMyAndRestorePreviousReference()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION old_room 0 10;#0\nREGION pickup_room 5 15;#1\n" +
            "THING pickup 5 0 0 1;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL hits { VAL 0; } " +
            "THING pickup { IF_NEAR near_pickup; IF_FAR NULL; IF_KLICK click_pickup; } " +
            "ACTION near_pickup { RULE my.x += 3; RULE there.FLOOR_HGT += 2; RULE hits += 1; } " +
            "ACTION click_pickup { RULE my.y += 4; RULE hits += 10; }");
        Thing pickup = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);
        var originalMy = new Thing("originalMy");
        Region previousThere = level.Regions[0];

        runtime.RegisterObject("my", originalMy);
        runtime.RegisterObject("there", previousThere);

        Assert.True(runtime.TriggerObjectActionHook(pickup, "IF_NEAR"));
        Assert.Equal(8, pickup.X);
        Assert.Equal(7, level.Regions[1].FloorHeight);
        Assert.Equal(1, runtime.GetSkill("hits"));
        Assert.Same(originalMy, runtime.GetObject("my"));
        Assert.Same(previousThere, runtime.GetObject("there"));

        Assert.True(runtime.TriggerObjectActionHook(pickup, "IF_KLICK"));
        Assert.Equal(4, pickup.Y);
        Assert.Equal(11, runtime.GetSkill("hits"));
        Assert.Same(originalMy, runtime.GetObject("my"));
        Assert.Same(previousThere, runtime.GetObject("there"));

        Assert.False(runtime.TriggerObjectActionHook(pickup, "IF_FAR"));
        Assert.False(runtime.TriggerObjectActionHook(pickup, "IF_HIT"));
    }

    [Fact]
    public void Runtime_WallAndRegionActionHooks_TriggerAndCanBeReassigned()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\nREGION old_room 5 15;#1\n" +
            "WALL gate 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL region_hits { VAL 0; } SKILL region_my_x { VAL -1; } SKILL wall_hits { VAL 0; } " +
            "REGION room { IF_ENTER region_enter; IF_LEAVE NULL; } " +
            "WALL gate { IF_NEAR wall_near; IF_HIT NULL; } " +
            "ACTION region_enter { SET region_my_x, my.X; RULE region_hits += 1; " +
            "RULE there.FLOOR_HGT += 2; SET there.IF_ENTER, region_again; } " +
            "ACTION region_again { RULE region_hits += 10; } " +
            "ACTION wall_near { RULE my.OFFSET_Y += 3; SET my.IF_HIT, wall_hit; RULE wall_hits += 1; } " +
            "ACTION wall_hit { RULE my.OFFSET_Y += 4; RULE wall_hits += 10; }");
        Region room = level.Regions[0];
        Wall gate = Assert.Single(level.Walls);
        var runtime = new WdlRuntime(level);
        var previousMy = new Thing("previousMy") { X = 99 };
        Region previousThere = level.Regions[1];

        runtime.RegisterObject("my", previousMy);
        runtime.RegisterObject("there", previousThere);

        Assert.True(runtime.TriggerObjectActionHook(room, "IF_ENTER"));
        Assert.Equal(2, room.FloorHeight);
        Assert.Equal(1, runtime.GetSkill("region_hits"));
        Assert.Equal(0, runtime.GetSkill("region_my_x"));
        Assert.Equal("region_again", room.ActionHooks["IF_ENTER"]);
        Assert.Same(previousMy, runtime.GetObject("my"));
        Assert.Same(previousThere, runtime.GetObject("there"));

        Assert.True(runtime.TriggerObjectActionHook(room, "IF_ENTER"));
        Assert.False(runtime.TriggerObjectActionHook(room, "IF_LEAVE"));
        Assert.Equal(11, runtime.GetSkill("region_hits"));

        Assert.True(runtime.TriggerObjectActionHook(gate, "IF_NEAR"));
        Assert.Equal(3 + (1 / 1024.0), gate.OffsetY); // a placed wall starts one raw step up
        Assert.Equal("wall_hit", gate.ActionHooks["IF_HIT"]);
        Assert.Equal(1, runtime.GetSkill("wall_hits"));

        Assert.True(runtime.TriggerObjectActionHook(gate, "IF_HIT"));
        Assert.Equal(7 + (1 / 1024.0), gate.OffsetY);
        Assert.Equal(11, runtime.GetSkill("wall_hits"));
        Assert.Same(previousMy, runtime.GetObject("my"));
        Assert.Same(previousThere, runtime.GetObject("there"));
    }

    [Fact]
    public void Tick_PlayerRegionChange_EntersAfterTheGlobalSlots()
    {
        // OV-20261019-actor-contact round 8: the border-climb walkers' ENTER prints the cycle count the
        // global EACH_TICK slot has just raised, so region events come after the global slots; the
        // slot already reads the new region through HERE (clip-contact border traces).
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nVERTEX 10 10 0;#2\nVERTEX 0 10 0;#3\n" +
            "REGION old_room 0 10;#0\nREGION new_room 5 15;#1\nPLAYER_START 1 1 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL n { VAL 0; } SKILL seen { VAL 0; } SKILL h { VAL 0; } SKILL hseen { VAL 0; } " +
            "REGION new_room { SKILL1 7; IF_ENTER enter_new; } " +
            "ACTION count { RULE n = n + 1; RULE h = HERE.SKILL1; } ACTION enter_new { RULE seen = n; RULE hseen = h; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        runtime.SetEachTickAction(1, "count");
        runtime.Tick(1.0 / 16);

        player.MoveTo(1, 1, 1);
        runtime.Tick(1.0 / 16);

        Assert.Equal(2, runtime.GetSkill("seen"));
        Assert.Equal(7, runtime.GetSkill("hseen")); // HERE moved with the player, before the slot read it
    }

    [Fact]
    public void Tick_PlayerRegionChange_TriggersLeaveAndEnterHooks()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nVERTEX 10 10 0;#2\nVERTEX 0 10 0;#3\n" +
            "REGION old_room 0 10;#0\nREGION new_room 5 15;#1\nPLAYER_START 1 1 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL left_count { VAL 0; } SKILL entered_count { VAL 0; } " +
            "REGION old_room { IF_LEAVE leave_old; } REGION new_room { IF_ENTER enter_new; } " +
            "ACTION leave_old { RULE there.FLOOR_HGT += 2; RULE left_count += 1; } " +
            "ACTION enter_new { RULE there.CEIL_HGT += 3; RULE entered_count += 1; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        var previousThere = new Region("previousThere");

        runtime.RegisterObject("player", player);
        runtime.RegisterObject("there", previousThere);

        runtime.Tick(1.0 / 16);
        Assert.Equal(0, runtime.GetSkill("left_count"));
        Assert.Equal(0, runtime.GetSkill("entered_count"));
        Assert.Same(level.Regions[0], runtime.GetObject("here"));

        player.MoveTo(1, 1, 1);
        runtime.Tick(1.0 / 16);

        Assert.Equal(1, runtime.GetSkill("left_count"));
        Assert.Equal(1, runtime.GetSkill("entered_count"));
        Assert.Equal(2, level.Regions[0].FloorHeight);
        Assert.Equal(18, level.Regions[1].CeilHeight);
        Assert.Same(level.Regions[1], runtime.GetObject("here"));
        Assert.Same(previousThere, runtime.GetObject("there"));

        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("left_count"));
        Assert.Equal(1, runtime.GetSkill("entered_count"));
    }

    [Fact]
    public void Tick_PlayerProximity_TriggersEntityNearAndFarOnThresholdCrossings()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nVERTEX 10 10 0;#2\nVERTEX 0 10 0;#3\n" +
            "REGION room 0 10;#0\nTHING pickup 5 1 0 0;#1\nPLAYER_START 1 1 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL near_count { VAL 0; } SKILL far_count { VAL 0; } " +
            "THING pickup { DIST 2; IF_NEAR pickup_near; IF_FAR pickup_far; } " +
            "ACTION pickup_near { RULE near_count += 1; RULE my.X += 1; } " +
            "ACTION pickup_far { RULE far_count += 1; RULE my.Y += 1; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        Thing pickup = Assert.Single(level.Things);

        runtime.RegisterObject("player", player);

        runtime.Tick(1.0 / 16);
        Assert.Equal(0, runtime.GetSkill("near_count"));
        Assert.Equal(0, runtime.GetSkill("far_count"));

        player.MoveTo(0, 4, 1);
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("near_count"));
        Assert.Equal(0, runtime.GetSkill("far_count"));
        Assert.Equal(6, pickup.X);

        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("near_count"));

        player.MoveTo(0, 1, 1);
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("near_count"));
        Assert.Equal(1, runtime.GetSkill("far_count"));
        Assert.Equal(2, pickup.Y);
    }

    [Fact]
    public void Tick_PlayerProximity_ComparesDistWithSumOfAxisDistancesAndFiresOncePerCrossing()
    {
        // OV-20261019-proximity-events: DIST is compared with |dx| + |dy| (MY.DISTANCE), IF_NEAR
        // runs once on the way in and IF_FAR once on the way out, whatever moves the player.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nVERTEX 20 20 0;#2\nVERTEX 0 20 0;#3\n" +
            "REGION room 0 10;#0\nTHING post 5 5 0 0;#1\nPLAYER_START 1 1 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL near_count { VAL 0; } SKILL far_count { VAL 0; } " +
            "THING post { DIST 4; IF_NEAR post_near; IF_FAR post_far; } " +
            "ACTION post_near { RULE near_count += 1; } " +
            "ACTION post_far { RULE far_count += 1; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        runtime.Tick(1.0 / 16);

        player.MoveTo(0, 7.5f, 7.5f); // 3.54 straight-line, 5 by axes
        runtime.Tick(1.0 / 16);
        Assert.Equal(0, runtime.GetSkill("near_count"));

        player.MoveTo(0, 7, 6.5f); // 3.5 by axes
        runtime.Tick(1.0 / 16);
        runtime.Tick(1.0 / 16);
        player.MoveTo(0, 8.5f, 5); // moving, still within DIST
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("near_count"));
        Assert.Equal(0, runtime.GetSkill("far_count"));

        player.MoveTo(0, 7.5f, 7.5f);
        runtime.Tick(1.0 / 16);
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("near_count"));
        Assert.Equal(1, runtime.GetSkill("far_count"));
    }

    [Fact]
    public void Tick_PlayerProximity_NearAndFarActionsReadDistanceAsSumOfAxisDistances()
    {
        // OV-20261019-proximity-events: MY.DISTANCE reads |dx| + |dy|, the value DIST is compared with.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nVERTEX 20 20 0;#2\nVERTEX 0 20 0;#3\n" +
            "REGION room 0 10;#0\nTHING post 5 5 0 0;#1\nPLAYER_START 1 1 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL near_dist { VAL -1; } SKILL far_dist { VAL -1; } " +
            "THING post { DIST 4; IF_NEAR post_near; IF_FAR post_far; } " +
            "ACTION post_near { RULE near_dist = my.DISTANCE; } " +
            "ACTION post_far { RULE far_dist = my.DISTANCE; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        runtime.Tick(1.0 / 16);

        player.MoveTo(0, 7, 6.5f);
        runtime.Tick(1.0 / 16);
        Assert.Equal(3.5, runtime.GetSkill("near_dist"), 6);
        Assert.Equal(-1, runtime.GetSkill("far_dist"));

        player.MoveTo(0, 0.5f, 9);
        runtime.Tick(1.0 / 16);
        Assert.Equal(8.5, runtime.GetSkill("far_dist"), 6);
    }

    [Fact]
    public void Tick_EachTickActions_ReadDistanceFromCurrentPlayerAndMovedActorPositions()
    {
        // MY.DISTANCE is refreshed every cycle, and again after the actors move, for every thing
        // and actor whether or not it has a proximity hook.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 20 0 0;#1\nVERTEX 20 20 0;#2\nVERTEX 0 20 0;#3\n" +
            "REGION room 0 10;#0\nTHING post 5 5 0 0;#1\nACTOR runner 10 5 0 0;#2\n" +
            "PLAYER_START 1 1 0 0;#3";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL post_dist { VAL -1; } SKILL runner_dist { VAL -1; } " +
            "THING post { EACH_TICK post_tick; } " +
            "ACTOR runner { TARGET MOVE; SPEED 2; EACH_TICK runner_tick; } " +
            "ACTION post_tick { RULE post_dist = my.DISTANCE; } " +
            "ACTION runner_tick { RULE runner_dist = my.DISTANCE; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        Actor runner = Assert.Single(level.Actors);
        runtime.RegisterObject("player", player);

        runtime.Tick(1.0 / 16);
        Assert.Equal(8, runtime.GetSkill("post_dist"), 6);
        Assert.Equal(12, runner.X, 6);
        Assert.Equal(15, runtime.GetSkill("runner_dist"), 6);

        player.MoveTo(0, 7, 6.5f);
        runtime.Tick(1.0 / 16);
        Assert.Equal(3.5, runtime.GetSkill("post_dist"), 6);
        Assert.Equal(14 - 7 + 1.5, runtime.GetSkill("runner_dist"), 6);
    }

    [Fact]
    public void Tick_PlayerProximity_ComparesWallDistWithSumOfAxisDistancesToNearestVertex()
    {
        // OV-20261019-wall-distance: a wall's DIST is compared with |dx| + |dy| to its nearer
        // vertex, so the middle of a wall longer than 2 x DIST never fires, and IF_NEAR/IF_FAR
        // read the compared value as MY.DISTANCE.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nREGION room 0 10;#0\n" +
            "WALL rail 0 1 0 -1 0 0;#0\nPLAYER_START 5 5 0 0;#1";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL near_count { VAL 0; } SKILL near_dist { VAL -1; } SKILL far_dist { VAL -1; } " +
            "WALL rail { DIST 3; IF_NEAR near_rail; IF_FAR far_rail; } " +
            "ACTION near_rail { RULE near_count += 1; RULE near_dist = my.DISTANCE; } " +
            "ACTION far_rail { RULE far_dist = my.DISTANCE; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        runtime.Tick(1.0 / 16);

        player.MoveTo(0, 5, 1); // 1 from the segment, 6 by axes from either vertex
        runtime.Tick(1.0 / 16);
        player.MoveTo(0, 2, 1.5f); // 2.5 straight-line from v1, 3.5 by axes
        runtime.Tick(1.0 / 16);
        Assert.Equal(0, runtime.GetSkill("near_count"));

        player.MoveTo(0, 1.5f, 1);
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("near_count"));
        Assert.Equal(2.5, runtime.GetSkill("near_dist"), 6);

        player.MoveTo(0, 5, 5);
        runtime.Tick(1.0 / 16);
        Assert.Equal(10, runtime.GetSkill("far_dist"), 6);

        player.MoveTo(0, 9, 1); // 2 by axes from v2
        runtime.Tick(1.0 / 16);
        Assert.Equal(2, runtime.GetSkill("near_count"));
        Assert.Equal(2, runtime.GetSkill("near_dist"), 6);
    }

    [Fact]
    public void Tick_EachTickActions_ReadWallDistanceToNearestVertex()
    {
        // OV-20261019-wall-distance: every wall's MY.DISTANCE is |dx| + |dy| to its nearer vertex,
        // with or without hooks, at any range.
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nVERTEX 40 0 0;#2\nVERTEX 40 10 0;#3\nREGION room 0 10;#0\n" +
            "WALL rail 0 1 0 -1 0 0;#0\nWALL plain 2 3 0 -1 0 0;#1\nPLAYER_START 5 5 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL rail_dist { VAL -1; } " +
            "WALL rail { EACH_TICK rail_tick; } WALL plain { } " +
            "ACTION rail_tick { RULE rail_dist = my.DISTANCE; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        Wall plain = level.Walls[1];
        runtime.RegisterObject("player", player);

        runtime.Tick(1.0 / 16);
        Assert.Equal(10, runtime.GetSkill("rail_dist"), 6);
        Assert.Equal(35 + 5, plain["DISTANCE"], 6);

        player.MoveTo(0, 7, 1.5f);
        runtime.Tick(1.0 / 16);
        Assert.Equal(3 + 1.5, runtime.GetSkill("rail_dist"), 6);
        Assert.Equal(33 + 1.5, plain["DISTANCE"], 6);
    }

    // A free-standing wall from (20, -8) to (20, 8) with the room on both sides.
    private const string GateMap =
        "VERTEX 20 -8 0;#0\nVERTEX 20 8 0;#1\nREGION room 0 10;#0\n" +
        "WALL gate 0 1 0 0 0 0;#0\nPLAYER_START 10 0 0 0;#1";

    private const string GateCounters =
        "MAPFILE <m.wmp>; SKILL near_count { VAL 0; } SKILL far_count { VAL 0; } SKILL near_dist { VAL -1; } " +
        "ACTION near_gate { RULE near_count += 1; RULE near_dist = my.DISTANCE; } " +
        "ACTION far_gate { RULE far_count += 1; } ";

    [Fact]
    public void Tick_PlayerProximity_WallCrossingFiresNearOutsideDistOnly()
    {
        // OV-20261019-wall-pass-clip: walking through a passable wall fires IF_NEAR in either
        // direction while the player is outside DIST, with no IF_FAR; within DIST, or by a beam,
        // a crossing fires nothing.
        Level level = LoadWithMap(
            GateMap,
            GateCounters + "WALL gate { DIST 6; IF_NEAR near_gate; IF_FAR far_gate; FLAGS PASSABLE; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        player.MoveTo(0, 16, 0);
        runtime.Tick(1.0 / 16);

        player.MoveHorizontal(8, 0);
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("near_count"));
        Assert.Equal(4 + 8, runtime.GetSkill("near_dist"), 4);
        runtime.Tick(1.0 / 16);
        player.MoveHorizontal(-8, 0);
        runtime.Tick(1.0 / 16);
        Assert.Equal(2, runtime.GetSkill("near_count"));

        player.MoveTo(0, 24, 0);
        runtime.Tick(1.0 / 16);
        player.MoveTo(0, 16, 0);
        runtime.Tick(1.0 / 16);
        Assert.Equal(2, runtime.GetSkill("near_count"));

        player.MoveTo(0, 17.5f, 5.5f); // 2.5 + 2.5 from v2: enters DIST
        runtime.Tick(1.0 / 16);
        Assert.Equal(3, runtime.GetSkill("near_count"));
        player.MoveHorizontal(5, 0); // through the wall within DIST, to 2.5 + 2.5 from v2
        runtime.Tick(1.0 / 16);
        Assert.Equal(3, runtime.GetSkill("near_count"));
        Assert.Equal(0, runtime.GetSkill("far_count"));

        player.MoveTo(0, 30, 5);
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("far_count"));
    }

    [Fact]
    public void Tick_PlayerProximity_DistZeroWallFiresNearOnCrossingIncludingEndPoints()
    {
        // OV-20261019-wall-pass-clip: with DIST 0 a crossing of the segment fires IF_NEAR, end
        // points included, a step onto the line and the step off it both count, a crossing beyond
        // the vertex or coming close does not, and IF_FAR never fires.
        Level level = LoadWithMap(
            GateMap,
            GateCounters + "WALL gate { IF_NEAR near_gate; IF_FAR far_gate; FLAGS PASSABLE; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        player.MoveTo(0, 16, 0);
        runtime.Tick(1.0 / 16);

        player.MoveHorizontal(3.5f, 0); // to x = 19.5, short of the wall
        runtime.Tick(1.0 / 16);
        Assert.Equal(0, runtime.GetSkill("near_count"));

        player.MoveHorizontal(1, 0);
        runtime.Tick(1.0 / 16);
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("near_count"));

        player.MoveTo(0, 19.75f, 7.75f);
        runtime.Tick(1.0 / 16);
        player.MoveHorizontal(0.25f, 0.25f); // onto v2
        runtime.Tick(1.0 / 16);
        Assert.Equal(2, runtime.GetSkill("near_count"));
        Assert.Equal(0, runtime.GetSkill("near_dist"), 6);
        player.MoveHorizontal(0.25f, 0.25f); // off it
        runtime.Tick(1.0 / 16);
        Assert.Equal(3, runtime.GetSkill("near_count"));

        player.MoveTo(0, 19.75f, 8.25f);
        runtime.Tick(1.0 / 16);
        player.MoveHorizontal(0.5f, 0.5f); // crosses the line at y = 8.5
        runtime.Tick(1.0 / 16);
        Assert.Equal(3, runtime.GetSkill("near_count"));
        Assert.Equal(0, runtime.GetSkill("far_count"));
    }

    [Fact]
    public void Tick_PlayerProximity_PushingAgainstSolidWallFiresNearEveryCycleOutsideDist()
    {
        // OV-20261019-wall-pass-clip: every cycle of pushing against a solid wall fires IF_NEAR
        // while the player is outside DIST; standing against it does not, and within DIST the
        // contact is silent.
        Level level = LoadWithMap(
            GateMap,
            GateCounters + "WALL gate { DIST 6; IF_NEAR near_gate; IF_FAR far_gate; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        player.MoveTo(0, 17, 0);
        runtime.Tick(1.0 / 16);

        player.MoveHorizontal(2, 0);
        runtime.Tick(1.0 / 16);
        Assert.True(player.Position.X < 20 - 1);
        Assert.Equal(1, runtime.GetSkill("near_count"));
        player.MoveHorizontal(1, 0);
        runtime.Tick(1.0 / 16);
        runtime.Tick(1.0 / 16);
        Assert.Equal(2, runtime.GetSkill("near_count"));

        player.MoveTo(0, 17, 5); // enters DIST
        runtime.Tick(1.0 / 16);
        player.MoveHorizontal(2, 0);
        runtime.Tick(1.0 / 16);
        player.MoveHorizontal(1, 0.5f);
        runtime.Tick(1.0 / 16);
        Assert.Equal(3, runtime.GetSkill("near_count"));
        Assert.Equal(0, runtime.GetSkill("far_count"));
    }

    [Fact]
    public void Tick_ObjectEachTick_WallRunsOnlyWithinClipDist()
    {
        // OV-20261019-wall-pass-clip: a wall's EACH_TICK runs only while |dx| + |dy| to its nearer
        // vertex is below the CLIP_DIST of the player's region.
        Level level = LoadWithMap(
            GateMap,
            "MAPFILE <m.wmp>; SKILL runs { VAL 0; } REGION room { CLIP_DIST 10; } " +
            "WALL gate { EACH_TICK gate_tick; FLAGS PASSABLE; } ACTION gate_tick { RULE runs += 1; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);

        Assert.Equal(1, Runs(11, 8)); // 9
        Assert.Equal(0, Runs(10, 8)); // 10: the border is outside
        Assert.Equal(0, Runs(14, 4)); // 6 + 4, 7.2 in a straight line
        Assert.Equal(1, Runs(15, 4)); // 5 + 4

        double Runs(float x, float y)
        {
            player.MoveTo(0, x, y);
            double before = runtime.GetSkill("runs");
            runtime.Tick(1.0 / 16);
            return runtime.GetSkill("runs") - before;
        }
    }

    [Fact]
    public void Tick_ObjectEachTick_ThingsAndActorsUseThingAndActorDistRatios()
    {
        // OV-20261019-wall-pass-clip: a thing's EACH_TICK runs only below CLIP_DIST x THING_DIST,
        // an actor's below CLIP_DIST x ACTOR_DIST (both 1 by default). LIBER lifts the gate (p.71).
        const string map =
            "VERTEX 0 0 0;#0\nREGION room 0 10;#0\n" +
            "THING post 20 -12 0 0;#0\nACTOR guard 20 12 0 0;#1\nTHING beacon -40 0 0 0;#2\n" +
            "PLAYER_START 0 0 0 0;#3";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL post_runs { VAL 0; } SKILL guard_runs { VAL 0; } SKILL beacon_runs { VAL 0; } " +
            "REGION room { CLIP_DIST 20; } " +
            "THING post { EACH_TICK post_tick; } ACTOR guard { EACH_TICK guard_tick; } " +
            "THING beacon { EACH_TICK beacon_tick; FLAGS LIBER; } " +
            "ACTION post_tick { RULE post_runs += 1; } ACTION guard_tick { RULE guard_runs += 1; } " +
            "ACTION beacon_tick { RULE beacon_runs += 1; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        Assert.Equal(1, runtime.GetSkill("THING_DIST"));
        Assert.Equal(1, runtime.GetSkill("ACTOR_DIST"));

        Assert.Equal((1, 0), Runs(1, -12)); // 19 from the post
        Assert.Equal((0, 0), Runs(0, -12));
        Assert.Equal((0, 1), Runs(1, 12)); // 19 from the guard
        Assert.Equal((0, 0), Runs(8, 4)); // 12 + 8 from the guard

        runtime.SetSkill("THING_DIST", 0.5);
        runtime.SetSkill("ACTOR_DIST", 0.25);
        Assert.Equal((1, 0), Runs(11, -12)); // 9 from the post
        Assert.Equal((0, 0), Runs(10, -12));
        Assert.Equal((0, 1), Runs(16, 12)); // 4 from the guard
        Assert.Equal((0, 0), Runs(15, 12));
        Assert.Equal(8, runtime.GetSkill("beacon_runs"));

        (double Post, double Guard) Runs(float x, float y)
        {
            player.MoveTo(0, x, y);
            double post = runtime.GetSkill("post_runs");
            double guard = runtime.GetSkill("guard_runs");
            runtime.Tick(1.0 / 16);
            return (runtime.GetSkill("post_runs") - post, runtime.GetSkill("guard_runs") - guard);
        }
    }

    // Two regions joined at x = 0: room (x < 0, region 0) and annex (x > 0, region 1).
    private const string BorderMap =
        "VERTEX 0 -8 0;#0\nVERTEX 0 8 0;#1\nREGION room 0 10;#0\nREGION annex 0 10;#1\n" +
        "WALL border 0 1 0 1 0 0;#0\nPLAYER_START -4 0 0 0;#1";

    [Fact]
    public void Tick_PlayerProximity_CrossingRegionBorderFiresNearWithoutPassable()
    {
        // OV-20261019-clip-contact: a wall between two regions is passed whatever its flags, and
        // every crossing fires its IF_NEAR (DIST 0), in either direction, with no IF_FAR.
        Level level = LoadWithMap(
            BorderMap,
            GateCounters + "WALL border { IF_NEAR near_gate; IF_FAR far_gate; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        runtime.Tick(1.0 / 16);

        player.MoveHorizontal(8, 0);
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, player.Region);
        Assert.Equal(1, runtime.GetSkill("near_count"));
        runtime.Tick(1.0 / 16);
        player.MoveHorizontal(-8, 0);
        runtime.Tick(1.0 / 16);
        Assert.Equal(0, player.Region);
        Assert.Equal(2, runtime.GetSkill("near_count"));
        Assert.Equal(0, runtime.GetSkill("far_count"));
    }

    // A 2-step wide post at (10, 0) (contact within 2.2 steps) and a guard of the same size at (10, 20).
    private const string ContactMap =
        "VERTEX 0 0 0;#0\nREGION room 0 10;#0\nTHING post 10 0 0 0;#0\nACTOR guard 10 20 0 0;#1\n" +
        "PLAYER_START 5 0 0 0;#2";

    [Fact]
    public void Tick_PlayerProximity_PushingAgainstThingOrActorFiresNearEveryCycleOutsideDist()
    {
        // OV-20261019-clip-contact: every cycle of pushing against a solid thing or actor fires its
        // IF_NEAR while the player is outside its DIST, with no IF_FAR; standing against it does
        // not, and within DIST the contact is silent.
        Level level = LoadWithMap(
            ContactMap,
            GateCounters + "SKILL guard_near { VAL 0; } SKILL guard_far { VAL 0; } " +
            "BMAP postMap, <post.pcx>, 0, 0, 32, 16; TEXTURE postTex { BMAPS postMap; } " +
            "THING post { TEXTURE postTex; IF_NEAR near_gate; IF_FAR far_gate; } " +
            "ACTOR guard { TEXTURE postTex; DIST 8; IF_NEAR guard_n; IF_FAR guard_f; } " +
            "ACTION guard_n { RULE guard_near += 1; } ACTION guard_f { RULE guard_far += 1; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        runtime.Tick(1.0 / 16);

        player.MoveHorizontal(4, 0);
        runtime.Tick(1.0 / 16);
        Assert.Equal(10 - LevelCollisionWorld.ContactDistance(2.2f), player.Position.X, 3);
        Assert.Equal(1, runtime.GetSkill("near_count"));
        player.MoveHorizontal(1, 0);
        runtime.Tick(1.0 / 16);
        runtime.Tick(1.0 / 16);
        Assert.Equal(2, runtime.GetSkill("near_count"));
        player.MoveHorizontal(-4, 0);
        runtime.Tick(1.0 / 16);
        Assert.Equal(2, runtime.GetSkill("near_count"));
        Assert.Equal(0, runtime.GetSkill("far_count"));

        player.MoveTo(0, 1, 20); // 9 from the guard
        runtime.Tick(1.0 / 16);
        player.MoveHorizontal(1, 0); // 8: enters DIST
        runtime.Tick(1.0 / 16);
        player.MoveHorizontal(10, 0); // pushes against it within DIST
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("guard_near"));
        Assert.Equal(0, runtime.GetSkill("guard_far"));
    }

    [Fact]
    public void Tick_PlayerProximity_DistBorderItselfIsOutside()
    {
        // OV-20261019-actor-contact, dist-edge: |dx| + |dy| = DIST is not near; 0.5 closer is.
        Level level = LoadWithMap(
            ContactMap,
            GateCounters + "ACTOR guard { DIST 10; IF_NEAR near_gate; IF_FAR far_gate; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);

        player.MoveTo(0, 10, 10);
        runtime.Tick(1.0 / 16);
        Assert.Equal(0, runtime.GetSkill("near_count"));

        player.MoveTo(0, 10, 10.5f);
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("near_count"));
        Assert.Equal(9.5, runtime.GetSkill("near_dist"), 3);
    }

    private const string PushMap =
        "VERTEX 0 0 0;#0\nREGION room 0 10;#0\nACTOR pusher 0 0 0 0;#0\nPLAYER_START 4 0 0 0;#1";

    private const string PushCounters =
        "MAPFILE <m.wmp>; SKILL near_count { VAL 0; } " +
        "ACTION near_push { RULE near_count += 1; } " +
        "BMAP pushMap, <push.pcx>, 0, 0, 32, 16; TEXTURE pushTex { BMAPS pushMap; } ";

    [Theory]
    [InlineData(3, 1)]
    [InlineData(8, 0)]
    [InlineData(15, 0)]
    public void Tick_PlayerProximity_ThingIsNearOnlyWhileTheEyeIsWithinDistOfItsCentre(float eye, int nearCount)
    {
        // OV-20261019-actor-contact, dist-eye: a 2-high thing (centre at 1) with DIST 6.5 fires with
        // the eye at 3, but not at 8 (7 above it) or 15.
        Level level = LoadWithMap(
            ContactMap,
            GateCounters + "BMAP postMap, <post.pcx>, 0, 0, 32, 32; TEXTURE postTex { BMAPS postMap; } " +
            "THING post { TEXTURE postTex; DIST 6.5; IF_NEAR near_gate; IF_FAR far_gate; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        player.Position = new System.Numerics.Vector3(-10, eye, 0);
        runtime.Tick(1.0 / 16);

        player.Position = new System.Numerics.Vector3(5, eye, 0); // 5 from the thing, no contact
        runtime.Tick(1.0 / 16);

        Assert.Equal(nearCount, runtime.GetSkill("near_count"));
    }

    [Fact]
    public void Tick_ActorTargets_PlainActorPushesThePlayerAndFiresNearEveryPushingCycle()
    {
        // OV-20261019-actor-contact, actor-push: the player is held at half the actor's width plus
        // PLAYER_WIDTH ahead of it, and the actor's IF_NEAR fires in every cycle it pushes.
        Level level = LoadWithMap(
            PushMap,
            PushCounters + "ACTOR pusher { TEXTURE pushTex; TARGET MOVE; SPEED 1; IF_NEAR near_push; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        runtime.SetSkill("SKIP_FRAMES", 0); // the actor pushes from behind the player
        player.MoveTo(0, 4, 0);
        Actor pusher = Assert.Single(level.Actors);

        runtime.Tick(1.0 / 16); // the actor at 1, 3 from the player: no contact
        Assert.Equal(0, runtime.GetSkill("near_count"));
        Assert.Equal(4, player.Position.X, 3);

        runtime.Tick(1.0 / 16);
        runtime.Tick(1.0 / 16);
        Assert.True(MapEntitySizing.TryGetSize(level, pusher, out MapEntitySize size));
        Assert.Equal(3, pusher.X, 3);
        Assert.Equal(3 + LevelCollisionWorld.ContactDistance((size.Width / 2) + player.Width), player.Position.X, 3);
        Assert.Equal(2, runtime.GetSkill("near_count"));
    }

    [Fact]
    public void Tick_ActorTargets_CarefulActorStopsShortOfThePlayerAndFiresNearWhileBlocked()
    {
        // OV-20261019-actor-contact, actor-push-careful: the actor halves its step until the point two
        // steps ahead clears the contact distance, stops when no step does, and does not push.
        Level level = LoadWithMap(
            PushMap,
            PushCounters + "ACTOR pusher { TEXTURE pushTex; TARGET MOVE; SPEED 1.6; IF_NEAR near_push; FLAGS CAREFULLY; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        runtime.SetSkill("SKIP_FRAMES", 0); // the actor pushes from behind the player
        player.MoveTo(0, 10.05f, 0);
        Actor pusher = Assert.Single(level.Actors);

        for (int i = 0; i < 20; i++)
        {
            runtime.Tick(1.0 / 16);
        }

        // SPEED 1.6 is held as 1.5996 (fixed point, OV-20261029-numeric-edge), so the halved steps
        // land a little differently than with an exact 1.6.
        double gap = player.Position.X - pusher.X;
        Assert.InRange(gap, 1 + player.Width, 1 + player.Width + 0.16);
        Assert.Equal(10.05, player.Position.X, 3);
        Assert.True(runtime.GetSkill("near_count") >= 1);
    }

    [Fact]
    public void Apply_PlayerBeamedInsideASolidThing_IsPushedOutRadiallyAndFiresItsNear()
    {
        // OV-20261019-actor-contact, beam-inside: out along the line from the centre, to contact.
        Level level = LoadWithMap(
            ContactMap,
            GateCounters + "BMAP postMap, <post.pcx>, 0, 0, 32, 16; TEXTURE postTex { BMAPS postMap; } " +
            "THING post { TEXTURE postTex; IF_NEAR near_gate; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        runtime.Tick(1.0 / 16);

        player.MoveTo(0, 9, 0.5f);
        WdlPlayerMotion.Apply(runtime, player, 1);
        // Round 8: the contact's IF_NEAR runs right after the movement, in the beam's own cycle.
        runtime.DispatchContactEvents();
        Assert.Equal(1, runtime.GetSkill("near_count"));
        runtime.Tick(1.0 / 16);

        float contact = LevelCollisionWorld.ContactDistance(1 + player.Width);
        Assert.Equal(10 - (contact * 2 / MathF.Sqrt(5)), player.Position.X, 3);
        Assert.Equal(contact / MathF.Sqrt(5), player.Position.Z, 3);
        Assert.Equal(1, runtime.GetSkill("near_count"));
    }

    [Theory]
    [InlineData(10f, 0.5f, false)]
    [InlineData(11f, 0f, true)]
    public void Apply_PlayerInsideTwoActors_IsPushedOutOfTheLastPlacedOnlyAndOnlyOnce(float x, float y, bool onCentre)
    {
        // OV-20261029-overlap: the first overlapping object in reverse map order decides; on its exact
        // centre the player stays, and it is not pushed out of the other one afterwards either.
        Level level = LoadWithMap(
            "VERTEX 0 0 0;#0\nREGION room 0 10;#0\nACTOR first 9 0 0 0;#0\nACTOR last 11 0 0 0;#1\nPLAYER_START 5 0 0 0;#2",
            "MAPFILE <m.wmp>; BMAP postMap, <post.pcx>, 0, 0, 32, 16; TEXTURE postTex { BMAPS postMap; } " +
            "ACTOR first { TEXTURE postTex; } ACTOR last { TEXTURE postTex; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        WdlPlayerMotion.Apply(runtime, player, 1);

        player.MoveTo(0, x, y);
        WdlPlayerMotion.Apply(runtime, player, 1);
        WdlPlayerMotion.Apply(runtime, player, 1);

        if (onCentre)
        {
            Assert.Equal(new Vector2(x, y), new Vector2(player.Position.X, player.Position.Z));
            return;
        }

        float contact = LevelCollisionWorld.ContactDistance(1 + player.Width);
        Assert.Equal(11 - (contact * 2 / MathF.Sqrt(5)), player.Position.X, 3);
        Assert.Equal(contact / MathF.Sqrt(5), player.Position.Z, 3);
        Assert.True(Vector2.Distance(new Vector2(9, 0), new Vector2(player.Position.X, player.Position.Z)) < contact);
    }

    [Fact]
    public void Tick_Distance_ReadsTheSentinelUntilThePlayerFirstComesWithinOneAndAHalfBorders()
    {
        // OV-20261019-actor-contact, distance-refresh and refresh-20: CLIP_DIST 10.
        Level level = LoadWithMap(
            "VERTEX 0 0 0;#0\nREGION room 0 10;#0\nTHING post 40 0 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "MAPFILE <m.wmp>; REGION room { CLIP_DIST 10; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        Thing post = Assert.Single(level.Things);

        runtime.Tick(1.0 / 16);
        Assert.Equal(65535.999, post["DISTANCE"], 3);
        player.MoveTo(0, 25, 0); // 15: not yet
        runtime.Tick(1.0 / 16);
        Assert.Equal(65535.999, post["DISTANCE"], 3);
        player.MoveTo(0, 26, 0);
        runtime.Tick(1.0 / 16);
        Assert.Equal(14, post["DISTANCE"], 3);
        player.MoveTo(0, 0, 0);
        runtime.Tick(1.0 / 16);
        Assert.Equal(40, post["DISTANCE"], 3);
    }

    [Fact]
    public void Tick_PlayerProximity_ThingOutsideClipDistIsNotDistTestedUnlessLiber()
    {
        // OV-20261019-clip-contact: with CLIP_DIST 10 and DIST 15, a thing fires IF_NEAR only when it
        // comes within the CLIP_DIST border while the player is within DIST, and no IF_FAR when it
        // leaves the border; a LIBER thing fires at DIST both ways.
        const string map =
            "VERTEX 0 0 0;#0\nREGION room 0 10;#0\nTHING post 24 -16 0 0;#0\nTHING beacon 24 16 0 0;#1\n" +
            "PLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            GateCounters + "SKILL beacon_near { VAL 0; } SKILL beacon_far { VAL 0; } REGION room { CLIP_DIST 10; } " +
            "THING post { DIST 15; IF_NEAR near_gate; IF_FAR far_gate; } " +
            "THING beacon { DIST 15; IF_NEAR beacon_n; IF_FAR beacon_f; FLAGS LIBER; } " +
            "ACTION beacon_n { RULE beacon_near += 1; } ACTION beacon_f { RULE beacon_far += 1; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);

        Assert.Equal((0, 0), Hooks(12, -16)); // 12: within DIST, outside the border
        Assert.Equal((1, 0), Hooks(15, -16)); // 9
        Assert.Equal((1, 0), Hooks(12, -16));
        Assert.Equal((1, 0), Hooks(0, -16)); // 24: beyond DIST, no IF_FAR

        Assert.Equal(0, runtime.GetSkill("beacon_near"));
        player.MoveTo(0, 10, 16); // 14
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("beacon_near"));
        player.MoveTo(0, 0, 16);
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("beacon_far"));

        (double Near, double Far) Hooks(float x, float y)
        {
            player.MoveTo(0, x, y);
            runtime.Tick(1.0 / 16);
            return (runtime.GetSkill("near_count"), runtime.GetSkill("far_count"));
        }
    }

    [Fact]
    public void Tick_ActorTargets_ActorOutsideClipDistDoesNotMoveUnlessLiber()
    {
        // OV-20261019-clip-contact: a FOLLOW or WAY actor stands still while outside
        // CLIP_DIST x ACTOR_DIST and moves again within it; a LIBER actor moves at any distance.
        // A TARGET naming a way assigns that way (p.66).
        const string map =
            "VERTEX 0 0 0;#0\nREGION room 0 10;#0\nACTOR follower 40 0 180 0;#0\nACTOR patrol 30 12 0 0;#1\n" +
            "ACTOR scout 40 -12 180 0;#2\nWAY route 30 12 46 12;#3\nPLAYER_START 0 0 0 0;#4";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; WAY route; REGION room { CLIP_DIST 10; } " +
            "ACTOR follower { TARGET FOLLOW; SPEED 1; } ACTOR patrol { TARGET route; SPEED 1; } " +
            "ACTOR scout { TARGET FOLLOW; SPEED 1; FLAGS LIBER; }");
        Actor follower = level.Actors[0];
        Actor patrol = level.Actors[1];
        Actor scout = level.Actors[2];
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        runtime.SetSkill("SKIP_FRAMES", 0); // the patrol is out of view at the last stand

        runtime.Tick(1.0 / 16);
        Assert.Equal(40, follower.X, 6);
        Assert.Equal(30, patrol.X, 6);
        Assert.True(scout.X < 40);

        player.MoveTo(0, 31, 0); // 9 from the follower
        runtime.Tick(1.0 / 16);
        Assert.Equal(39, follower.X, 6);
        player.MoveTo(0, 0, 0);
        runtime.Tick(1.0 / 16);
        Assert.Equal(39, follower.X, 6);

        player.MoveTo(0, 32, 8); // 6 from the patrol, which starts on its first waypoint
        runtime.Tick(1.0 / 16);
        runtime.Tick(1.0 / 16);
        Assert.Equal(31, patrol.X, 6);
    }

    [Fact]
    public void Tick_ObjectEachCycle_ThingOutsideClipDistDoesNotCycle()
    {
        // OV-20261019-clip-contact: a thing's EACH_CYCLE runs only within CLIP_DIST x THING_DIST.
        const string map =
            "VERTEX 0 0 0;#0\nREGION room 0 10;#0\nTHING runner 20 0 0 0;#0\nPLAYER_START 0 0 0 0;#1";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL cycles { VAL 0; } REGION room { CLIP_DIST 10; } " +
            "BMAP p1,<p1.pcx>,0,0,16,16; BMAP p2,<p2.pcx>,0,0,16,16; " +
            "TEXTURE runnerTex { CYCLES 2; BMAPS p1,p2; DELAY 1,1; } " +
            "THING runner { TEXTURE runnerTex; EACH_CYCLE runner_cycle; } ACTION runner_cycle { RULE cycles += 1; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);

        for (int i = 0; i < 8; i++)
        {
            runtime.Tick(1.0 / 16);
        }

        Assert.Equal(0, runtime.GetSkill("cycles"));
        player.MoveTo(0, 15, 0);
        for (int i = 0; i < 8; i++)
        {
            runtime.Tick(1.0 / 16);
        }

        Assert.True(runtime.GetSkill("cycles") > 0);
    }

    [Fact]
    public void Tick_ObjectEachTick_ClipDistIsTheSmallerOfThePlayersAndTheObjectsRegion()
    {
        // OV-20261019-clip-contact: the CLIP_DIST border is the smaller of the player's region's and
        // the object's own region's CLIP_DIST.
        const string map =
            "VERTEX 0 -8 0;#0\nVERTEX 0 8 0;#1\nREGION room 0 10;#0\nREGION annex 0 10;#1\n" +
            "WALL border 0 1 0 1 0 0;#0\nTHING near_post -20 0 0 0;#1\nTHING far_post 20 0 0 1;#2\n" +
            "PLAYER_START -4 0 0 0;#3";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL near_runs { VAL 0; } SKILL far_runs { VAL 0; } " +
            "REGION room { CLIP_DIST 30; } REGION annex { CLIP_DIST 10; } " +
            "THING near_post { EACH_TICK near_tick; } THING far_post { EACH_TICK far_tick; } " +
            "ACTION near_tick { RULE near_runs += 1; } ACTION far_tick { RULE far_runs += 1; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);

        Assert.Equal((1, 0), Runs(0, -4)); // room: 16 from near_post (< 30), 24 from far_post (annex, 10)
        Assert.Equal((0, 0), Runs(1, 4)); // annex: 24 from near_post (room, but the player's 10)
        Assert.Equal((0, 1), Runs(1, 12)); // 8 from far_post

        (double Near, double Far) Runs(int region, float x)
        {
            player.MoveTo(region, x, 0);
            double near = runtime.GetSkill("near_runs");
            double far = runtime.GetSkill("far_runs");
            runtime.Tick(1.0 / 16);
            return (runtime.GetSkill("near_runs") - near, runtime.GetSkill("far_runs") - far);
        }
    }

    private const string BorderWallMap =
        "VERTEX 0 -8 0;#0\nVERTEX 0 8 0;#1\nREGION room 0 10;#0\nREGION annex 0 10;#1\n" +
        "WALL border 0 1 0 1 0 0;#0\nPLAYER_START -4 0 0 0;#1";

    [Theory]
    [InlineData("", 1, 0, 1)]
    [InlineData(" FLAGS INVISIBLE;", 0, 0, 0)]
    public void Tick_ObjectEachTick_WallBetweenRegionsUsesThePlayersRegionClipDistAndInvisibleWallsRunNothing(
        string flags, int fromRoom, int farInAnnex, int nearInAnnex)
    {
        // OV-20261019-clip-detail, wall-regions-visible: a wall between the room (CLIP_DIST 30) and
        // the annex (10) runs at 28 with the player in the room, not at 20 from the annex; an
        // INVISIBLE wall runs no EACH_TICK at all.
        Level level = LoadWithMap(
            BorderWallMap,
            "MAPFILE <m.wmp>; SKILL runs { VAL 0; } " +
            "REGION room { CLIP_DIST 30; } REGION annex { CLIP_DIST 10; } " +
            $"WALL border {{ EACH_TICK wall_tick;{flags} }} ACTION wall_tick {{ RULE runs += 1; }}");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);

        Assert.Equal(fromRoom, Runs(0, -20)); // 20 + 8 from the nearer vertex
        Assert.Equal(farInAnnex, Runs(1, 12));
        Assert.Equal(nearInAnnex, Runs(1, 1));

        double Runs(int region, float x)
        {
            player.MoveTo(region, x, 0);
            double before = runtime.GetSkill("runs");
            runtime.Tick(1.0 / 16);
            return runtime.GetSkill("runs") - before;
        }
    }

    [Fact]
    public void Tick_PlayerProximity_WallOutsideClipDistIsNotDistTested()
    {
        // OV-20261019-clip-detail, wall-dist-clip: CLIP_DIST 10 and a wall with DIST 20. Within DIST
        // but outside the border nothing fires; entering the border fires IF_NEAR, leaving it nothing.
        Level level = LoadWithMap(
            GateMap,
            GateCounters + "REGION room { CLIP_DIST 10; } WALL gate { DIST 20; IF_NEAR near_gate; IF_FAR far_gate; FLAGS PASSABLE; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);

        runtime.Tick(1.0 / 16); // 10 + 8 from v1
        Assert.Equal(0, runtime.GetSkill("near_count"));
        player.MoveTo(0, 15, 4); // 5 + 4 from v2
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("near_count"));
        player.MoveTo(0, 10, 0);
        runtime.Tick(1.0 / 16);
        Assert.Equal(0, runtime.GetSkill("far_count"));
    }

    [Fact]
    public void Tick_ObjectEachTick_BerkeleyThingRunsOnlyWithinDistAndAnEyeHighAboveItFiresNoHooks()
    {
        // OV-20261019-clip-contact: a BERKELEY thing's EACH_TICK runs only while the player is within
        // its DIST, also when it is in view. It fired neither IF_NEAR nor IF_FAR because the eye (8)
        // was more than its DIST 4 above the 2-high thing's centre (OV-20261019-actor-contact).
        const string map =
            "VERTEX 0 0 0;#0\nREGION room 0 16;#0\nTHING ghost 10 0 0 0;#0\nPLAYER_START 0 0 0 0;#1";
        Level level = LoadWithMap(
            map,
            GateCounters + "SKILL runs { VAL 0; } " +
            "BMAP ghostMap, <ghost.pcx>, 0, 0, 32, 32; TEXTURE ghostTex { BMAPS ghostMap; } " +
            "THING ghost { TEXTURE ghostTex; DIST 4; IF_NEAR near_gate; IF_FAR far_gate; EACH_TICK ghost_tick; FLAGS BERKELEY; } " +
            "ACTION ghost_tick { RULE runs += 1; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        runtime.RegisterObject("player", player);
        player.Position = new System.Numerics.Vector3(0, 8, 0);

        runtime.Tick(1.0 / 16);
        Assert.Equal(0, runtime.GetSkill("runs"));
        player.Position = new System.Numerics.Vector3(7, 8, 0);
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("runs"));
        player.Position = new System.Numerics.Vector3(0, 8, 0);
        runtime.Tick(1.0 / 16);
        Assert.Equal(1, runtime.GetSkill("runs"));
        Assert.Equal(0, runtime.GetSkill("near_count"));
        Assert.Equal(0, runtime.GetSkill("far_count"));
    }

    [Fact]
    public void Runtime_ObjectActionHooks_CanBeReassignedWithSetInstructions()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING pickup 5 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL order { VAL 0; } " +
            "THING pickup { IF_NEAR first; IF_HIT NULL; } " +
            "ACTION first { RULE order = order * 10 + 1; SET my.IF_NEAR, second; SET my.IF_HIT, my.IF_NEAR; } " +
            "ACTION second { IF (my.IF_HIT == second) { RULE order = order * 10 + 2; } " +
            "SET my.IF_HIT, NULL; IF (my.IF_HIT == NULL) { RULE order = order * 10 + 3; } }");
        Thing pickup = Assert.Single(level.Things);
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.TriggerObjectActionHook(pickup, "IF_NEAR"));
        Assert.Equal(1, runtime.GetSkill("order"));
        Assert.Equal("second", pickup.ActionHooks["IF_NEAR"]);
        Assert.Equal("second", pickup.ActionHooks["IF_HIT"]);

        Assert.True(runtime.TriggerObjectActionHook(pickup, "IF_HIT"));
        Assert.Equal(123, runtime.GetSkill("order"));
        Assert.Null(pickup.ActionHooks["IF_HIT"]);
    }

    [Fact]
    public void Runtime_ObjectActionHooks_CanBeReassignedWithSetAll()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
            "THING pickup 5 0 0 0;#1\nTHING pickup 6 0 0 0;#2\nPLAYER_START 0 0 0 0;#3";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL hits { VAL 0; } " +
            "THING pickup { IF_NEAR first; } " +
            "ACTION rebind { SET_ALL pickup.IF_NEAR, shared; } " +
            "ACTION first { RULE hits += 10; } " +
            "ACTION shared { RULE hits += 1; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("rebind"));

        Thing first = level.Things[0];
        Thing second = level.Things[1];
        Assert.Equal("shared", first.ActionHooks["IF_NEAR"]);
        Assert.Equal("shared", second.ActionHooks["IF_NEAR"]);

        Assert.True(runtime.TriggerObjectActionHook(first, "IF_NEAR"));
        Assert.True(runtime.TriggerObjectActionHook(second, "IF_NEAR"));
        Assert.Equal(2, runtime.GetSkill("hits"));
    }

    [Fact]
    public void SetSkill_ClampsAssignmentsToDeclaredBounds()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL myHealth { VAL 100; MAX 100; MIN 0; } " +
            "ACTION overheal { RULE myHealth += 50; } ACTION drain { RULE myHealth -= 1000; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("overheal");
        Assert.Equal(100, runtime.GetSkill("myHealth")); // pinned at MAX, not 150

        runtime.RunAction("drain");
        Assert.Equal(0, runtime.GetSkill("myHealth"));   // pinned at MIN, not -900
    }

    [Fact]
    public void Runtime_IfMin_UsesDeclaredSkillMinimum()
    {
        Level level = Load(
            "MAPFILE <m.wmp>; SKILL health { VAL 0; MAX 100; MIN 0; } SKILL result { VAL 0; } " +
            "ACTION check { IF_MIN health; SET result, 1; ELSE; SET result, 2; ENDIF; }");
        var runtime = new WdlRuntime(level);

        Assert.True(runtime.RunAction("check"));

        Assert.Equal(1, runtime.GetSkill("result"));
    }

    [Fact]
    public void SetSkill_WithoutDeclaredBounds_DoesNotClamp()
    {
        Level level = Load("MAPFILE <m.wmp>; SKILL freeVal { VAL 0; } ACTION big { SET freeVal, 99999; }");
        var runtime = new WdlRuntime(level);

        runtime.RunAction("big");
        Assert.Equal(99999, runtime.GetSkill("freeVal"));
    }

    [Fact]
    public void Runtime_RealApathy_BootsAndTicksWithoutThrowing()
    {
        string? apathy = ResearchData.WrsFiles()
            .FirstOrDefault(p => Path.GetFileName(p).Equals("apathy.wrs", StringComparison.OrdinalIgnoreCase));
        if (apathy is null)
        {
            return; // no local game data — skip
        }

        Level level = LevelLoader.Load(WrsArchive.ReadFile(apathy), "APATHY.WDL");
        var runtime = new WdlRuntime(level);

        Assert.Equal(1, runtime.GetSkill("PLAYER_ARC"));
        runtime.RunStartup(); // run the level's real IF_START path — must not throw
        for (int i = 0; i < 3; i++)
        {
            runtime.Tick(1.0 / 16);
        }

        Assert.Equal(1.0, runtime.GetSkill("TIME_CORR"), 9);
    }

    [Fact]
    public void Runtime_RealApathy_LeftInputRunsRetailWeaponPathWithFiniteHitState()
    {
        string? apathy = ResearchData.WrsFiles()
            .FirstOrDefault(p => Path.GetFileName(p).Equals("apathy.wrs", StringComparison.OrdinalIgnoreCase));
        if (apathy is null)
        {
            return;
        }

        Level level = LevelLoader.Load(WrsArchive.ReadFile(apathy), "APATHY.WDL");
        var runtime = new WdlRuntime(level);
        var player = new Player(level);
        runtime.RegisterObject("player", player);
        WdlPlayerMotion.Synchronize(runtime, player);
        Assert.True(runtime.RunStartup());
        runtime.Tick(1.0 / 16);

        Assert.True(runtime.ActionHooks.TryGetValue("IF_LEFT", out string? fireAction));
        Assert.False(string.IsNullOrWhiteSpace(fireAction));
        Assert.True(runtime.TriggerActionHook("IF_LEFT"));
        runtime.Tick(1.0 / 16);

        Assert.True(double.IsFinite(runtime.GetSkill("HIT_DIST")));
        Assert.True(double.IsFinite(runtime.GetSkill("HIT_X")));
        Assert.True(double.IsFinite(runtime.GetSkill("HIT_Y")));
        Assert.True(double.IsFinite(runtime.GetSkill("RESULT")));
        Assert.InRange(player.Region, 0, level.Regions.Count - 1);
    }

    [Fact]
    public void LevelTransitionGlobals_PreservesOnlyGlobalsDeclaredByBothLevels()
    {
        Level sourceLevel = Load("""
            MAPFILE <m.wmp>;
            SKILL retained { TYPE GLOBAL; VAL 1; }
            SKILL sourceOnly { TYPE GLOBAL; VAL 2; }
            SKILL ordinary { VAL 3; }
            """);
        var source = new WdlRuntime(sourceLevel);
        source.SetSkill("retained", 11);
        source.SetSkill("sourceOnly", 12);
        source.SetSkill("ordinary", 13);

        IReadOnlyDictionary<string, double> globals = source.CaptureLevelTransitionGlobals();

        Level targetLevel = Load("""
            MAPFILE <m.wmp>;
            SKILL retained { TYPE GLOBAL; VAL 21; }
            SKILL targetOnly { TYPE GLOBAL; VAL 22; }
            SKILL sourceOnly { VAL 23; }
            SKILL ordinary { VAL 24; }
            """);
        var target = new WdlRuntime(targetLevel);
        target.RestoreLevelTransitionGlobals(globals);

        Assert.Equal(11, target.GetSkill("retained"));
        Assert.Equal(22, target.GetSkill("targetOnly"));
        Assert.Equal(23, target.GetSkill("sourceOnly"));
        Assert.Equal(24, target.GetSkill("ordinary"));
    }

    private static Level Load(string main)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = MinimalMap };
        return LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }

    private static Level LoadWithMap(string map, string main)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        return LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }

    private static TextureImage SolidImage(int width, int height, byte alpha)
    {
        var rgba = new byte[width * height * 4];
        for (int i = 3; i < rgba.Length; i += 4)
        {
            rgba[i] = alpha;
        }

        return new TextureImage(width, height, rgba);
    }

    private static double UiNumber(WdlUiElement element, string property) =>
        double.Parse(
            element.Properties.Single(p => p.Keyword.Equals(property, StringComparison.OrdinalIgnoreCase)).Values[0].Text,
            System.Globalization.CultureInfo.InvariantCulture);

    private static bool UiFlag(WdlUiElement element, string flag) =>
        element.Properties.Any(property =>
            property.Keyword.Equals("FLAGS", StringComparison.OrdinalIgnoreCase) &&
            property.Values.Any(value => value.Text.Equals(flag, StringComparison.OrdinalIgnoreCase)));

    private static string? UiKeyword(WdlUiElement element, string property) =>
        UiKeywords(element, property).SingleOrDefault();

    private static string[] UiKeywords(WdlUiElement element, string property) =>
        element.Properties
            .Single(p => p.Keyword.Equals(property, StringComparison.OrdinalIgnoreCase))
            .Values
            .Where(value => !value.Kind.Equals("Comma", StringComparison.OrdinalIgnoreCase))
            .Select(value => value.Text)
            .ToArray();
}
