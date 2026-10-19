// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text.Json;
using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Engine.Tests.TestSupport;
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
        Assert.Equal(1, runtime.GetSkill("MUSIC_VOL"));
        Assert.Equal(0, runtime.GetSkill("MOUSE_MODE"));
        Assert.Equal(1, runtime.GetSkill("TOUCH_MODE"));
        Assert.Equal(0, runtime.GetSkill("MOUSE_MOVING"));
        Assert.Equal(3, runtime.GetSkill("MOUSE_CALM"));
        Assert.Equal(4, runtime.GetSkill("MOUSE_TIME"));
        Assert.Equal(0, runtime.GetSkill("MICKEY_X"));
        Assert.Equal(0, runtime.GetSkill("MICKEY_Y"));
        Assert.Equal(4, runtime.GetSkill("WALK_PERIOD"));
        Assert.Equal(4, runtime.GetSkill("WALK_TIME"));
        Assert.Equal(16, runtime.GetSkill("WAVE_PERIOD"));
        Assert.Equal(0, runtime.GetSkill("WALK"));
        Assert.Equal(0, runtime.GetSkill("WAVE"));
        Assert.Equal(1, runtime.GetSkill("PSOUND_VOL"));
        Assert.Equal(1, runtime.GetSkill("PSOUND_TONE"));
        Assert.Equal(-1, runtime.GetSkill("CHANNEL"));
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

        Assert.Equal(0.5, runtime.GetSkill("FRICTION"));
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
    public void Runtime_SoundChannels_SaturateSynchronouslyAndReuseCompletedSlot()
    {
        Level level = Load("MAPFILE <m.wmp>; SOUND alert,<ALERT.WAV>;");
        var runtime = new WdlRuntime(level);

        for (int i = 0; i < 9; i++)
        {
            runtime.PlaySound("alert", null, 1, null, null);
        }

        Assert.Equal(8, runtime.AudioRequests.Count);
        Assert.Equal(-1, runtime.GetSkill("CHANNEL"));
        Assert.All(Enumerable.Range(0, 8), channel =>
            Assert.Equal(1, runtime.GetSkill($"CHANNEL_{channel}")));

        Assert.True(runtime.CompleteSound(3));
        Assert.False(runtime.CompleteSound(3));
        runtime.PlaySound("alert", null, 1, null, null);

        Assert.Equal(9, runtime.AudioRequests.Count);
        Assert.Equal(3, runtime.GetSkill("CHANNEL"));
        Assert.Equal(3, runtime.AudioRequests[^1].Sound.Channel);
        Assert.Equal(1, runtime.GetSkill("CHANNEL_3"));
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
        Assert.Equal(1, runtime.GetSkill("CHANNEL_0"));

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
        WdlAudioRequest request = Assert.Single(runtime.AudioRequests);
        Assert.Equal(WdlSoundOperation.StopAll, request.Operation);
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
                Assert.Equal(WdlSoundOperation.StopAll, request.Operation);
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

            string outputPath = Path.Combine(outputDir, "ovsta007.TXT");
            string text = File.ReadAllText(outputPath);
            string expectedRun =
                "12.346" + Environment.NewLine +
                "hello" + Environment.NewLine +
                "worlddone" + Environment.NewLine;
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

        Assert.Equal(5, pickup.X, 6);
        Assert.Equal(9, pickup.Y, 6);
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
        Assert.Equal(1, runtime.GetSkill("HIT_X"));
        Assert.Equal(3, runtime.GetSkill("HIT_Y"));
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
        Assert.Equal(3, runtime.GetSkill("HIT_Y"));
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
        Assert.Equal(5, runtime.GetSkill("HIT_Y"));
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
        Assert.Equal(4, runtime.GetSkill("HIT_Y"));
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
        Assert.Equal(5, runtime.GetSkill("HIT_Y"));
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

        Assert.Equal(10, level.Vertices[0].X, 6);
        Assert.Equal(0, level.Vertices[0].Y, 6);
        Assert.Equal(10, level.Vertices[1].X, 6);
        Assert.Equal(10, level.Vertices[1].Y, 6);
        Assert.Equal(5, pickup.X, 6);
        Assert.Equal(7, pickup.Y, 6);
        Assert.Equal(1.5707963267948966, pickup.Angle, 12);
        Assert.Equal(5f, player.Position.X, 6);
        Assert.Equal(7f, player.Position.Z, 6);
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
    public void Tick_SetsTimeCorrectionSkill()
    {
        var runtime = new WdlRuntime(Load("MAPFILE <m.wmp>;"));

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
            "ACTION probe { SET observed, TIME_FAC * 100 + TICKS * 10 + SECS; SET EACH_TICK.1, NULL; }");
        var runtime = new WdlRuntime(level);

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

        Assert.Equal(5, runner["SPEED"], 9);
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

        runtime.Tick(1.0 / 16);

        Assert.Equal(2, room["CYCLE"]);
        Assert.Equal(1, gate["CYCLE"]);
        Assert.Equal(0.25, room["POSITION"]);
        Assert.Equal(0.9, gate["POSITION"]);
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

        runner["PLAY"] = 1;
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
        Assert.Equal(3, gate.OffsetY);
        Assert.Equal("wall_hit", gate.ActionHooks["IF_HIT"]);
        Assert.Equal(1, runtime.GetSkill("wall_hits"));

        Assert.True(runtime.TriggerObjectActionHook(gate, "IF_HIT"));
        Assert.Equal(7, gate.OffsetY);
        Assert.Equal(11, runtime.GetSkill("wall_hits"));
        Assert.Same(previousMy, runtime.GetObject("my"));
        Assert.Same(previousThere, runtime.GetObject("there"));
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
    public void Tick_PlayerProximity_TriggersWallNearFromSegmentDistance()
    {
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nREGION room 0 10;#0\n" +
            "WALL rail 0 1 0 -1 0 0;#0\nPLAYER_START 5 5 0 0;#1";
        Level level = LoadWithMap(
            map,
            "MAPFILE <m.wmp>; SKILL wall_near { VAL 0; } " +
            "WALL rail { DIST 1.5; IF_NEAR near_rail; } " +
            "ACTION near_rail { RULE wall_near += 1; RULE my.OFFSET_X += 2; }");
        var player = new Player(level);
        var runtime = new WdlRuntime(level);
        Wall wall = Assert.Single(level.Walls);

        runtime.RegisterObject("player", player);

        runtime.Tick(1.0 / 16);
        Assert.Equal(0, runtime.GetSkill("wall_near"));

        player.MoveTo(0, 5, 1);
        runtime.Tick(1.0 / 16);

        Assert.Equal(1, runtime.GetSkill("wall_near"));
        Assert.Equal(2, wall.OffsetX);
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
