// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Tests.TestSupport;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Engine.Tests;

public class LevelLoaderTests
{
    [Fact]
    public void LoadCore_VideoMode_PreservesActiveVirtualResolution()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\nPLAYER_START 0 0 0 0;#1",
        };

        Level level = LevelLoader.LoadCore(
            "x",
            "VIDEO S640x480; MAPFILE <m.wmp>;",
            n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(new LevelVideoMode("S640x480", 640, 480), level.VideoMode);
    }

    [Fact]
    public void LoadCore_CombinesWdlDeclarationsAndWmpGeometry()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["test.wmp"] =
                "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0.0 10.0;#0\n" +
                "WALL w 0 1 0 -1 0.0 0.0;#0\nTHING torch 5 6 90 0;#1\n" +
                "ACTOR guard 7 8 180 0;#2\nPLAYER_START 1 2 45 0;#3",
            ["mod.wdl"] = "SKILL energy { VAL 100; }",
        };
        const string main = "MAPFILE <test.wmp>; INCLUDE <mod.wdl>; SKILL health { VAL 50; }";

        Level level = LevelLoader.LoadCore("test.wdl", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(2, level.Vertices.Count);
        Assert.Equal("room", Assert.Single(level.Regions).Name);
        Assert.Single(level.Walls);
        Thing torch = Assert.Single(level.Things);
        Actor guard = Assert.Single(level.Actors);
        Assert.Equal("torch", torch.Name);
        Assert.Equal(90, torch.PlacementAngleDegrees);
        Assert.Equal(0, torch.Angle);
        Assert.Equal(Math.PI / 2, torch.FacingAngleRadians, precision: 12);
        Assert.Equal("guard", guard.Name);
        Assert.Equal(180, guard.PlacementAngleDegrees);
        Assert.Equal(0, guard.Angle);
        Assert.Equal(Math.PI, guard.FacingAngleRadians, precision: 12);
        Assert.NotNull(level.PlayerStart);
        Assert.Equal(45, level.PlayerStart!.Value.Angle);
        Assert.Equal(Math.PI / 4, level.PlayerStart!.Value.AngleRadians, precision: 12);

        // Skills come from both the main script and the INCLUDEd module.
        Assert.Equal(100, level.Skills["energy"]);
        Assert.Equal(50, level.Skills["health"]);
    }

    [Fact]
    public void LoadCore_DistinguishesImplicitSkillZeroFromExplicitValZero()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main = "MAPFILE <m.wmp>; SKILL implicitZero; SKILL explicitZero { VAL 0; }";

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(0, level.Skills["implicitZero"]);
        Assert.Equal(0, level.Skills["explicitZero"]);
        Assert.DoesNotContain("implicitZero", level.ExplicitlyInitializedSkills);
        Assert.Contains("explicitZero", level.ExplicitlyInitializedSkills);
    }

    [Fact]
    public void LoadCore_SkillTypes_TracksOnlyLatestGlobalDeclarations()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main = """
            MAPFILE <m.wmp>;
            SKILL retained { TYPE GLOBAL; VAL 1; }
            SKILL ordinary { VAL 2; }
            SKILL localValue { TYPE LOCAL; VAL 3; }
            SKILL redefined { TYPE GLOBAL; VAL 4; }
            SKILL redefined { TYPE PLAYER; VAL 5; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(["retained"], level.GlobalSkills);
    }

    [Fact]
    public void LoadCore_MapFileMustComeFromMainWdl()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["test.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\nPLAYER_START 0 0 0 0;#1",
            ["mod.wdl"] = "MAPFILE <test.wmp>; SKILL energy { VAL 100; }",
        };
        const string main = "INCLUDE <mod.wdl>; SKILL health { VAL 50; }";

        InvalidDataException ex = Assert.Throws<InvalidDataException>(
            () => LevelLoader.LoadCore("test.wdl", main, n => resources.GetValueOrDefault(Path.GetFileName(n))));

        Assert.Contains("no MAPFILE", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadCore_MapFileInMainWdl_CanUseDefineFromIncludedModule()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["test.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\nPLAYER_START 0 0 0 0;#1",
            ["mod.wdl"] = "DEFINE MAP,MAPFILE;",
        };
        const string main = "INCLUDE <mod.wdl>; MAP <test.wmp>;";

        Level level = LevelLoader.LoadCore("test.wdl", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal("room", Assert.Single(level.Regions).Name);
    }

    [Fact]
    public void LoadCore_MapOverride_UsesCurrentWdlDeclarationsWithReplacementMap()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["first.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION first 0 10;#0\nPLAYER_START 0 0 0 0;#1",
            ["second.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION second 0 10;#0\nTHING torch 2 3 90 0;#1\nPLAYER_START 2 3 180 0;#2",
        };
        const string main = "MAPFILE <first.wmp>; SKILL health { VAL 50; } THING torch { HEIGHT 7; }";

        Level level = LevelLoader.LoadCore(
            "test.wdl",
            main,
            n => resources.GetValueOrDefault(Path.GetFileName(n)),
            mapOverrideName: "second.wmp");

        Assert.Equal("second", Assert.Single(level.Regions).Name);
        Assert.Equal(50, level.Skills["health"]);
        Assert.Equal(7, Assert.Single(level.Things)["HEIGHT"]);
        Assert.Equal(180, level.PlayerStart!.Value.Angle);
    }

    [Fact]
    public void LoadCore_CapturesStringDeclarationsWithFixedCapacity()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["test.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0.0 10.0;#0\nPLAYER_START 1 2 45 0;#1",
        };
        const string main = "MAPFILE <test.wmp>; STRING empty; STRING prompt,\"hello\";";

        Level level = LevelLoader.LoadCore("test.wdl", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal("", level.Strings["empty"].Value);
        Assert.Equal(0, level.Strings["empty"].Capacity);
        Assert.Equal("hello", level.Strings["prompt"].Value);
        Assert.Equal(5, level.Strings["prompt"].Capacity);
    }

    [Fact]
    public void LoadCore_CapturesSynonymDeclarations()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["test.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0.0 10.0;#0\nPLAYER_START 1 2 45 0;#1",
        };
        const string main = "MAPFILE <test.wmp>; SYNONYM floor_syn { TYPE TEXTURE; DEFAULT water_tex; }";

        Level level = LevelLoader.LoadCore("test.wdl", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal("TEXTURE", level.Synonyms["floor_syn"].Type);
        Assert.Equal("water_tex", level.Synonyms["floor_syn"].DefaultKeyword);
    }

    [Fact]
    public void LoadCore_CapturesInitialEachTickActions()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["test.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0.0 10.0;#0\nPLAYER_START 1 2 45 0;#1",
        };
        string main =
            "MAPFILE <test.wmp>; " +
            "EACH_TICK first, NULL, second, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, ignored;";

        Level level = LevelLoader.LoadCore("test.wdl", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(16, level.EachTickActions.Count);
        Assert.Equal("first", level.EachTickActions[0]);
        Assert.Null(level.EachTickActions[1]);
        Assert.Equal("second", level.EachTickActions[2]);
        Assert.Equal("a16", level.EachTickActions[15]);
        Assert.DoesNotContain("ignored", level.EachTickActions);
    }

    [Fact]
    public void LoadCore_CapturesInitialEachSecActions()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["test.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0.0 10.0;#0\nPLAYER_START 1 2 45 0;#1",
        };
        string main =
            "MAPFILE <test.wmp>; EACH_TICK tick; " +
            "EACH_SEC first, NULL, second, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, ignored;";

        Level level = LevelLoader.LoadCore("test.wdl", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(16, level.EachSecActions.Count);
        Assert.Equal("first", level.EachSecActions[0]);
        Assert.Null(level.EachSecActions[1]);
        Assert.Equal("second", level.EachSecActions[2]);
        Assert.Equal("a16", level.EachSecActions[15]);
        Assert.DoesNotContain("ignored", level.EachSecActions);
        Assert.Equal("tick", level.EachTickActions[0]);
        Assert.DoesNotContain("first", level.EachTickActions);
    }

    [Fact]
    public void LoadCore_CapturesGlobalActionHooks()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["test.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0.0 10.0;#0\nPLAYER_START 1 2 45 0;#1",
        };
        const string main = "MAPFILE <test.wmp>; IF_F1 help; IF_LEFT click; IF_ANYKEY NULL; IF_START boot;";

        Level level = LevelLoader.LoadCore("test.wdl", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal("help", level.ActionHooks["IF_F1"]);
        Assert.Equal("click", level.ActionHooks["IF_LEFT"]);
        Assert.Null(level.ActionHooks["IF_ANYKEY"]);
        Assert.False(level.ActionHooks.ContainsKey("IF_START"));
    }

    [Fact]
    public void LoadCore_CapturesSearchPaths()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["test.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0.0 10.0;#0\nPLAYER_START 1 2 45 0;#1",
        };
        string main =
            "MAPFILE <test.wmp>; " +
            "PATH \"gfx\"; PATH \"sound\"; PATH \"p03\"; PATH \"p04\"; PATH \"p05\"; PATH \"p06\"; " +
            "PATH \"p07\"; PATH \"p08\"; PATH \"p09\"; PATH \"p10\"; PATH \"p11\"; PATH \"p12\"; " +
            "PATH \"p13\"; PATH \"p14\"; PATH \"p15\"; PATH \"p16\"; PATH \"ignored\";";

        Level level = LevelLoader.LoadCore("test.wdl", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(16, level.SearchPaths.Count);
        Assert.Equal("gfx", level.SearchPaths[0]);
        Assert.Equal("sound", level.SearchPaths[1]);
        Assert.Equal("p16", level.SearchPaths[15]);
        Assert.DoesNotContain("ignored", level.SearchPaths);
    }

    [Fact]
    public void LoadCore_CapturesStructuralUiDeclarations()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["test.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0.0 10.0;#0\nPLAYER_START 1 2 45 0;#1",
        };
        const string main =
            """
            MAPFILE <test.wmp>;
            BMAP titleMap, <title.pcx>, 4, 5, 64, 32;
            FONT menuFont, <menu.pcx>, 8, 8;
            OVERLAY titleOverlay { BMAP titleMap; POS_X 12; FLAGS VISIBLE; }
            PANEL hudPanel { POS_X 1; POS_Y 2; OVLY titleOverlay; }
            TEXT promptText { STRING prompt; FONT menuFont; POS_X 3; }
            VIDEO introVideo { FILE <intro.fli>; }
            """;

        Level level = LevelLoader.LoadCore("test.wdl", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        WdlUiElement font = level.UiElements["menuFont"];
        Assert.Equal("FONT", font.Keyword);
        Assert.Equal("FileRef", font.Header[1].Kind);
        Assert.Equal("menu.pcx", font.Header[1].Text);

        LevelBitmap bitmap = level.Bitmaps["titleMap"];
        Assert.Equal("title.pcx", bitmap.File);
        Assert.Equal(4, bitmap.X);
        Assert.Equal(5, bitmap.Y);
        Assert.Equal(64, bitmap.Width);
        Assert.Equal(32, bitmap.Height);

        WdlUiElement overlay = level.UiElements["titleOverlay"];
        Assert.Equal("OVERLAY", overlay.Keyword);
        Assert.Contains(overlay.Properties, property => property.Keyword == "BMAP" && property.Values[0].Text == "titleMap");
        Assert.Contains(overlay.Properties, property => property.Keyword == "POS_X" && property.Values[0].Text == "12");

        WdlUiElement panel = level.UiElements["hudPanel"];
        Assert.Contains(panel.Properties, property => property.Keyword == "OVLY" && property.Values[0].Text == "titleOverlay");

        WdlUiElement text = level.UiElements["promptText"];
        Assert.Contains(text.Properties, property => property.Keyword == "FONT" && property.Values[0].Text == "menuFont");

        WdlUiElement video = level.UiElements["introVideo"];
        Assert.Contains(video.Properties, property => property.Keyword == "FILE" && property.Values[0].Text == "intro.fli");
    }

    [Fact]
    public void LoadCore_LinksTexturesThroughTheDeclarationChain()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
                        "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            SOUND step, <step.wav>;
            BMAP floor_map, <floor.pcx>, 0, 0, 64, 64;
            TEXTURE floorTex
            {
                SCALE_XY 16, 16;
                SOUND step;
                SVOL 0.75;
                RANDOM 0.2;
                SDIST 42;
                SVDIST 7;
                SCYCLES 1, 0;
                FLAGS SLOOP;
                BMAPS floor_map;
            }
            WALL w { TEXTURE floorTex; }
            REGION room
            {
                FLOOR_HGT 0;
                CEIL_HGT 10;
                FLOOR_TEX floorTex;
                CEIL_TEX floorTex;
                FLOOR_OFFS_X 12;
                FLOOR_OFFS_Y 13;
                CEIL_OFFS_X 14;
                CEIL_OFFS_Y 15;
                AMBIENT 0.25;
                GENIUS wheel_center;
            }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Region room = Assert.Single(level.Regions);
        Assert.Equal("floorTex", room.FloorTexture);
        Assert.Equal(0.25, room.Ambient);
        Assert.Equal(12, room.FloorOffsetX);
        Assert.Equal(13, room.FloorOffsetY);
        Assert.Equal(14, room.CeilOffsetX);
        Assert.Equal(15, room.CeilOffsetY);
        Assert.Equal("wheel_center", room.GeniusName);
        Assert.Equal("floorTex", Assert.Single(level.Walls).Texture);

        LevelTexture texture = level.Textures["floorTex"];
        Assert.Equal("floor.pcx", texture.File);
        Assert.Equal(64, texture.Width);
        Assert.Equal(16, texture.ScaleX);
        Assert.Equal(0, texture.Ambient);
        Assert.Equal(42, texture.SoundDistance);
        Assert.Equal(7, texture.SoundVerticalDistance);
        Assert.Equal("step", texture.SoundName);
        Assert.Equal(0.75, texture.SoundVolume);
        Assert.Equal([true, false], texture.SoundCycles);
        Assert.True(texture.IsSoundLoop);
        Assert.Equal(0.2, texture.RandomDelayFactor);
    }

    [Fact]
    public void LoadCore_BmapWithoutRectangle_ResolvesWholeImageTexture()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
                        "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            BMAP floor_map, <floor.pcx>;
            TEXTURE floorTex { BMAPS floor_map; }
            WALL w { TEXTURE floorTex; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        LevelTexture texture = level.Textures["floorTex"];
        Assert.Equal("floor.pcx", texture.File);
        Assert.Equal(0, texture.X);
        Assert.Equal(0, texture.Y);
        Assert.Equal(0, texture.Width);
        Assert.Equal(0, texture.Height);
        Assert.Equal(16, texture.ScaleX);
        Assert.Equal(16, texture.ScaleY);
        Assert.Equal(100, texture.SoundDistance);
        Assert.Equal(0, texture.SoundVerticalDistance);
        Assert.Null(texture.SoundName);
        Assert.Equal(0.5, texture.SoundVolume);
        Assert.Empty(texture.SoundCycles);
        Assert.False(texture.IsSoundLoop);
        Assert.Equal(0, texture.RandomDelayFactor);
    }

    [Fact]
    public void LoadCore_TextureScaleAssignments_CaptureSeparateAxes()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
                        "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            BMAP floor_map, <floor.pcx>;
            TEXTURE floorTex { SCALE_X 8; SCALE_Y 12; BMAPS floor_map; }
            WALL w { TEXTURE floorTex; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        LevelTexture texture = level.Textures["floorTex"];
        Assert.Equal(8, texture.ScaleX);
        Assert.Equal(12, texture.ScaleY);
    }

    [Fact]
    public void LoadCore_TextureAnimation_PreservesMetadataAndNullFrameSlots()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
                        "THING actor 0 0 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            "MAPFILE <m.wmp>; " +
            "BMAP front1,<f1.pcx>,0,0,16,16; BMAP front2,<f2.pcx>,0,0,20,18; " +
            "BMAP back1,<b1.pcx>,0,0,24,22; " +
            "TEXTURE animated { SIDES 2; CYCLES 2; BMAPS front1,front2,back1,NULL; " +
            "DELAY 3,5; MIRROR 0,1; OFFSET_X 1,2,3,4; OFFSET_Y 5,6,7,8; " +
            "FLAGS ONESHOT; } THING actor { TEXTURE animated; }";

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        LevelTexture texture = level.Textures["animated"];
        Assert.Equal(2, texture.Sides);
        Assert.Equal(2, texture.Cycles);
        Assert.Equal([3d, 5d], texture.Delays);
        Assert.Equal([false, true], texture.Mirrors);
        Assert.Equal([1d, 2d, 3d, 4d], texture.FrameOffsetsX);
        Assert.Equal([5d, 6d, 7d, 8d], texture.FrameOffsetsY);
        Assert.True(texture.IsOneShot);
        Assert.Equal(4, texture.Frames.Count);
        Assert.Equal("front2", texture.Frames[1]?.Name);
        Assert.Equal("back1", texture.Frames[2]?.Name);
        Assert.Null(texture.Frames[3]);
    }

    [Fact]
    public void FindTexture_ResolvesAnyCaseAndRemembersEachName()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main = "MAPFILE <m.wmp>; BMAP stone,<s.pcx>,0,0,16,16; TEXTURE stoneTex { BMAPS stone; }";

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        LevelTextureEntry? entry = level.FindTexture("STONETEX");
        Assert.NotNull(entry);
        Assert.Equal(level.Textures["stoneTex"], entry.Texture);
        Assert.Same(entry, level.FindTexture("STONETEX"));
        Assert.Equal(entry.Texture, level.FindTexture(new string("stonetex".AsSpan()))?.Texture);
        Assert.Null(level.FindTexture("missing"));
        Assert.Null(level.FindTexture(null));
    }

    [Fact]
    public void LoadCore_DeclaredButUnplacedObjects_ExistInvisibleBeforeThePlacedOnesInSaves()
    {
        // Manual p.70 and OV-20261029-save-body round 13: a declared object the map does not place
        // exists with INVISIBLE set; the original's saves list them first, last declared first.
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
                        "WALL placed 0 1 0 -1 0 0;#0\nTHING crate 0.5 0.5 0 0;#1\nPLAYER_START 0 0 0 0;#2",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            THING crate { }
            THING drop { }
            ACTOR shot { }
            WALL placed { }
            WALL spare { }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(["drop", "shot", "spare"], level.UnplacedObjects.Select(static o => o.Name));
        Assert.All(level.UnplacedObjects, static o => Assert.Equal(1, o["INVISIBLE"]));
        Assert.Equal(["crate", "drop"], level.Things.Select(static t => t.Name));
        Assert.Equal(["shot"], level.Actors.Select(static a => a.Name));
        Assert.Equal(["placed"], level.Walls.Select(static w => w.Name)); // an unplaced wall has no geometry
        Assert.Equal(["spare", "shot", "drop", "crate", "placed"], level.SaveObjectOrder().Select(static o => o.Name));
    }

    [Fact]
    public void LoadCore_ActionOrder_NumbersAnActionWhereItsNameFirstAppears()
    {
        // OV-20261029-save-body round 13: a reference ahead of the declaration takes the number.
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = "REGION room 0 10;#0\nPLAYER_START 0 0 0 0;#1" };
        const string main =
            """
            MAPFILE <m.wmp>;
            THING t { IF_NEAR later; }
            ACTION first { CALL third; }
            ACTION later { }
            ACTION third { }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(["later", "first", "third"], level.ActionOrder);
    }

    [Fact]
    public void LoadCore_TextureBmapKeyword_ListsTheBitmapsLikeBmaps()
    {
        // Saints of Virtue's music room writes BMAP in its textures; the original reads it as BMAPS.
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = "REGION room 0 10;#0\nPLAYER_START 0 0 0 0;#1" };
        const string main =
            """
            MAPFILE <m.wmp>;
            BMAP a, <t.pcx>, 0, 0, 24, 40;
            BMAP b, <t.pcx>, 24, 0, 24, 40;
            TEXTURE spin { CYCLES 2; BMAP a, b; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        LevelTexture texture = level.Textures["spin"];
        Assert.Equal(40, texture.Height);
        Assert.Equal(2, texture.Frames.Count);
    }

    [Fact]
    public void LoadCore_WallOffsetDeclarations_OverridePlacementOffsets()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
                        "WALL w 0 1 0 -1 1 2;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            WALL w { OFFSET_X 5; OFFSET_Y 6; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Wall wall = Assert.Single(level.Walls);
        Assert.Equal(5, wall.OffsetX);
        Assert.Equal(6 + (1 / 1024.0), wall.OffsetY); // one raw step up, as the original starts it
    }

    [Fact]
    public void LoadCore_CapturesSkyTextureFlag()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
                        "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            BMAP sky_map, <sky.pcx>, 0, 0, 128, 128;
            TEXTURE skyTex { BMAPS sky_map; AMBIENT 0.7; FLAGS SKY; }
            REGION room { CEIL_TEX skyTex; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.True(level.Textures["skyTex"].IsSky);
        Assert.Equal(0.7, level.Textures["skyTex"].Ambient);
    }

    [Fact]
    public void LoadCore_CapturesWallAttachAndLightmapTextureFlag()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\n" +
                        "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            "MAPFILE <m.wmp>; BMAP lightMap,<light.pcx>,0,0,64,64; " +
            "TEXTURE lightTex { BMAPS lightMap; FLAGS LIGHTMAP,BEHIND,SHADOW; } " +
            "WALL w { ATTACH lightTex; }";

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.True(level.Textures["lightTex"].IsLightmap);
        Assert.True(level.Textures["lightTex"].IsBehind);
        Assert.True(level.Textures["lightTex"].IsShadow);
        Wall wall = Assert.Single(level.Walls);
        Assert.Equal("lightTex", wall.AttachTexture);
        Assert.True(wall.TryGetString("ATTACH", out string? attach));
        Assert.Equal("lightTex", attach);
    }

    [Fact]
    public void LoadCore_DressesThingsFromTheirTypeDefinitions()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
                        "THING Plant1 5 6 90 0;#1\nPLAYER_START 0 0 0 0;#2",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            BMAP plant_bmp, <plant.pcx>, 0, 0, 32, 64;
            TEXTURE plantTex { BMAPS plant_bmp; }
            THING Plant1 { TEXTURE plantTex; HEIGHT -1; ANGLE -0.5; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Thing plant = Assert.Single(level.Things);
        Assert.Equal("plantTex", plant.Texture);
        Assert.Equal(-1, plant.Height);
        Assert.Equal(-1, plant.Z);
        Assert.Equal(90, plant.PlacementAngleDegrees);
        Assert.Equal(-0.5, plant.Angle);
        Assert.Equal((Math.PI / 2) - 0.5, plant.FacingAngleRadians, precision: 12);
    }

    [Fact]
    public void LoadCore_DressesObjectsFromUniversalSkillDefinitions()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
                        "WALL gate 0 1 0 -1 0 0;#0\nTHING pickup 2 3 90 0;#1\n" +
                        "ACTOR guard 4 5 180 0;#2\nPLAYER_START 0 0 0 0;#3",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            REGION r { SKILL1 10; }
            WALL gate { SKILL2 -20; }
            THING pickup { SKILL3 3.5; }
            ACTOR guard { SKILL4 4; SKILL8 -8; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(10, Assert.Single(level.Regions)["skill1"]);
        Assert.Equal(-20, Assert.Single(level.Walls)["skill2"]);
        Assert.Equal(3.5, Assert.Single(level.Things)["skill3"]);

        Actor guard = Assert.Single(level.Actors);
        Assert.Equal(4, guard["skill4"]);
        Assert.Equal(-8, guard["skill8"]);
    }

    [Fact]
    public void LoadCore_DressesObjectsFromDocumentedNumericProperties()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
                        "WALL gate 0 1 0 -1 0 0;#0\nTHING pickup 2 3 90 0;#1\n" +
                        "ACTOR guard 4 5 180 0;#2\nPLAYER_START 0 0 0 0;#3",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            REGION r { CYCLE 0.5; POSITION 0.25; CLIP_DIST 512; FLOOR_ANGLE 0.25; CEIL_ANGLE 0.5; }
            WALL gate { CYCLE 2; POSITION 0.75; MAP_COLOR 12; DIST 30; }
            THING pickup { CYCLE 3; POSITION 0.125; MAP_COLOR 22; DIST 40; OVERLAY pickupOverlay; ATTACH glowTex; }
            ACTOR guard
            {
                TARGET FOLLOW;
                WAY patrolWay;
                CYCLE 4;
                WAYPOINT 5;
                TARGET_X 6;
                TARGET_Y 7;
                REL_ANGLE 0.125;
                REL_DIST 9;
                SPEED 10;
                VSPEED -11;
                ASPEED 12;
                MAP_COLOR 13;
                DIST 14;
            }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Region region = Assert.Single(level.Regions);
        Assert.Equal(0.5, region["cycle"]);
        Assert.Equal(0.25, region["position"]);
        Assert.Equal(512, region["clip_dist"]);
        Assert.Equal(0.25, region["floor_angle"]);
        Assert.Equal(0.5, region["ceil_angle"]);

        Wall wall = Assert.Single(level.Walls);
        Assert.Equal(2, wall["cycle"]);
        Assert.Equal(0.75, wall["position"]);
        Assert.Equal(12, wall["map_color"]);
        Assert.Equal(30, wall["dist"]);

        Thing pickup = Assert.Single(level.Things);
        Assert.Equal(3, pickup["cycle"]);
        Assert.Equal(0.125, pickup["position"]);
        Assert.Equal(22, pickup["map_color"]);
        Assert.Equal(40, pickup["dist"]);
        Assert.Equal("pickupOverlay", pickup.OverlayName);
        Assert.Equal("glowTex", pickup.AttachTexture);

        Actor guard = Assert.Single(level.Actors);
        Assert.Equal(4, guard["cycle"]);
        Assert.Equal(5, guard["waypoint"]);
        Assert.Equal(6, guard["target_x"]);
        Assert.Equal(7, guard["target_y"]);
        Assert.Equal(0.125, guard["rel_angle"]);
        Assert.Equal(9, guard["rel_dist"]);
        Assert.Equal(10, guard["speed"]);
        Assert.Equal(-11, guard["vspeed"]);
        Assert.Equal(12, guard["aspeed"]);
        Assert.Equal(13, guard["map_color"]);
        Assert.Equal(14, guard["dist"]);
        Assert.Equal("FOLLOW", guard.Target);
        Assert.Equal("patrolWay", guard.WayName);
    }

    [Fact]
    public void LoadCore_CapturesDocumentedEntitySchedulerActions()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
                        "THING pickup 2 3 90 0;#1\nACTOR guard 4 5 180 0;#2\nPLAYER_START 0 0 0 0;#3",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            THING pickup { EACH_TICK tick_pickup; EACH_CYCLE cycle_pickup; }
            ACTOR guard { EACH_TICK tick_guard; EACH_CYCLE cycle_guard; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Thing pickup = Assert.Single(level.Things);
        Assert.Equal("tick_pickup", pickup.EachTickAction);
        Assert.Equal("cycle_pickup", pickup.EachCycleAction);

        Actor guard = Assert.Single(level.Actors);
        Assert.Equal("tick_guard", guard.EachTickAction);
        Assert.Equal("cycle_guard", guard.EachCycleAction);
    }

    [Fact]
    public void LoadCore_CapturesDocumentedWallAndRegionSchedulerActions()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
                        "WALL gate 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            REGION r { EACH_TICK tick_region; EACH_CYCLE cycle_region; }
            WALL gate { EACH_TICK tick_wall; EACH_CYCLE cycle_wall; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Region region = Assert.Single(level.Regions);
        Assert.Equal("tick_region", region.EachTickAction);
        Assert.Equal("cycle_region", region.EachCycleAction);

        Wall wall = Assert.Single(level.Walls);
        Assert.Equal("tick_wall", wall.EachTickAction);
        Assert.Equal("cycle_wall", wall.EachCycleAction);
    }

    [Fact]
    public void LoadCore_CapturesDocumentedEntityEventActions()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
                        "THING pickup 2 3 90 0;#1\nACTOR guard 4 5 180 0;#2\nPLAYER_START 0 0 0 0;#3",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            THING pickup { IF_NEAR pickup_near; IF_FAR pickup_far; IF_HIT NULL; IF_KLICK pickup_click; }
            ACTOR guard { IF_ARRIVED guard_arrived; IF_HIT guard_hit; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Thing pickup = Assert.Single(level.Things);
        Assert.Equal("pickup_near", pickup.ActionHooks["IF_NEAR"]);
        Assert.Equal("pickup_far", pickup.ActionHooks["IF_FAR"]);
        Assert.Null(pickup.ActionHooks["IF_HIT"]);
        Assert.Equal("pickup_click", pickup.ActionHooks["IF_KLICK"]);

        Actor guard = Assert.Single(level.Actors);
        Assert.Equal("guard_arrived", guard.ActionHooks["IF_ARRIVED"]);
        Assert.Equal("guard_hit", guard.ActionHooks["IF_HIT"]);
    }

    [Fact]
    public void LoadCore_CapturesDocumentedWallAndRegionEventActions()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
                        "WALL gate 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            REGION r { IF_ENTER enter_region; IF_LEAVE leave_region; IF_DIVE dive_region; IF_ARISE NULL; }
            WALL gate { IF_NEAR near_wall; IF_FAR far_wall; IF_HIT NULL; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Region region = Assert.Single(level.Regions);
        Assert.Equal("enter_region", region.ActionHooks["IF_ENTER"]);
        Assert.Equal("leave_region", region.ActionHooks["IF_LEAVE"]);
        Assert.Equal("dive_region", region.ActionHooks["IF_DIVE"]);
        Assert.Null(region.ActionHooks["IF_ARISE"]);

        Wall wall = Assert.Single(level.Walls);
        Assert.Equal("near_wall", wall.ActionHooks["IF_NEAR"]);
        Assert.Equal("far_wall", wall.ActionHooks["IF_FAR"]);
        Assert.Null(wall.ActionHooks["IF_HIT"]);
    }

    [Fact]
    public void LoadCore_TopLevelClipDistance_DefaultsRegionsUnlessOverridden()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\nREGION q 0 10;#1\nPLAYER_START 0 0 0 0;#2",
        };
        const string main = "MAPFILE <m.wmp>; CLIP_DIST 777; REGION q { CLIP_DIST 256; }";

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(777, level.Regions[0]["clip_dist"]);
        Assert.Equal(256, level.Regions[1]["clip_dist"]);
    }

    [Fact]
    public void LoadCore_RegionClipDistance_DefaultsToDocumentedEngineValue()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main = "MAPFILE <m.wmp>;";

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(1000, Assert.Single(level.Regions)["clip_dist"]);
    }

    [Fact]
    public void LoadCore_AppliesDocumentedWallAndEntityDefaults()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
                        "WALL gate 0 1 0 -1 0 0;#0\nTHING pickup 2 3 90 0;#1\n" +
                        "ACTOR guard 4 5 180 0;#2\nPLAYER_START 0 0 0 0;#3",
        };
        const string main = "MAPFILE <m.wmp>;";

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Wall wall = Assert.Single(level.Walls);
        Assert.Equal(1, wall["map_color"]);
        Assert.Equal(0, wall["left"]);
        Assert.Equal(-1, wall["right"]);

        Thing thing = Assert.Single(level.Things);
        Assert.Equal(1, thing["map_color"]);
        Assert.Equal(0, thing["save"]);

        Actor actor = Assert.Single(level.Actors);
        Assert.Equal(1, actor["map_color"]);
        Assert.Equal(1, actor["save"]);
    }

    [Fact]
    public void LoadCore_DressesObjectsFromDocumentedFlags()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\n" +
                        "WALL gate 0 1 0 -1 0 0;#0\nTHING pickup 2 3 90 0;#1\n" +
                        "ACTOR guard 4 5 180 0;#2\nPLAYER_START 0 0 0 0;#3",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            REGION r { FLAGS SAVE, FLAG1; }
            WALL gate { FLAGS PASSABLE, FENCE, FLAG2; }
            THING pickup { FLAGS INVISIBLE, FLAG3; }
            ACTOR guard { FLAGS FAR, MASTER, FLAG4; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Region region = Assert.Single(level.Regions);
        Assert.Equal(1, region["save"]);
        Assert.Equal(1, region["flag1"]);

        Wall wall = Assert.Single(level.Walls);
        Assert.Equal(1, wall["passable"]);
        Assert.Equal(1, wall["fence"]);
        Assert.Equal(1, wall["flag2"]);

        Thing pickup = Assert.Single(level.Things);
        Assert.Equal(1, pickup["thing"]);
        Assert.Equal(0, pickup["actor"]);
        Assert.Equal(1, pickup["invisible"]);
        Assert.Equal(1, pickup["flag3"]);

        Actor guard = Assert.Single(level.Actors);
        Assert.Equal(1, guard["thing"]);
        Assert.Equal(1, guard["actor"]);
        Assert.Equal(1, guard["far"]);
        Assert.Equal(1, guard["master"]);
        Assert.Equal(1, guard["flag4"]);
    }

    [Fact]
    public void LoadCore_CapturesSkillMinMaxBounds()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main = "MAPFILE <m.wmp>; SKILL myHealth { VAL -10; MAX 100; MIN -20; } SKILL freeVal { VAL 5; }";

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(-10, level.Skills["myHealth"]);
        SkillRange health = level.SkillBounds["myHealth"];
        Assert.Equal(-20, health.Min);
        Assert.Equal(100, health.Max);
        Assert.False(level.SkillBounds.ContainsKey("freeVal")); // no MIN/MAX => no bounds entry
    }

    [Fact]
    public void LoadCore_NoMapFile_Throws()
    {
        Assert.Throws<InvalidDataException>(
            () => LevelLoader.LoadCore("x", "SKILL a { VAL 1; }", _ => null));
    }

    /// <summary>
    /// Loads every local retail archive end-to-end when user-supplied game data is present.
    /// No-op otherwise, so CI does not need proprietary data.
    /// </summary>
    [Fact]
    public void Load_LocalRetailArchives_MaterializesLevelInfo()
    {
        IReadOnlyList<string> archives = ResearchData.WrsFiles();
        if (archives.Count == 0)
        {
            return;
        }

        foreach (string path in archives)
        {
            WrsArchive archive = WrsArchive.ReadFile(path);
            Level level = LevelLoader.Load(archive, ResearchData.MainWdlName(path));

            Assert.NotEmpty(level.Vertices);
            Assert.NotEmpty(level.Regions);
            Assert.NotEmpty(level.Walls);
            Assert.NotNull(level.PlayerStart);
            Assert.NotEmpty(level.Textures);
        }
    }

    /// <summary>
    /// Loads the real apathy level end-to-end (when present): WMP geometry materialized as
    /// engine objects + skills from the flattened WDL. Pinned to apathy's known counts. No-op otherwise.
    /// </summary>
    [Fact]
    public void Load_RealApathyLevel_MaterializesGeometryAndSkills()
    {
        string? apathy = ResearchData.WrsFiles()
            .FirstOrDefault(p => Path.GetFileName(p).Equals("apathy.wrs", StringComparison.OrdinalIgnoreCase));
        if (apathy is null)
        {
            return; // no local game data — skip
        }

        WrsArchive archive = WrsArchive.ReadFile(apathy);
        Level level = LevelLoader.Load(archive, "APATHY.WDL");

        Assert.Equal(5192, level.Vertices.Count);
        // 1073 map regions, then the 69 BELOW regions the map stacks under 69 of them.
        Assert.Equal(1073 + 69, level.Regions.Count);
        Assert.Equal(69, level.Regions.Count(static r => r.AboveRegion >= 0));
        Assert.All(level.Regions.Skip(1073), static r => Assert.InRange(r.FootprintRegion, 0, 1072));
        Assert.Equal(5894, level.Walls.Count);
        // 492 placed things and 138 placed actors, then the 46 declared objects the map does not
        // place (21 things, 15 actors, 10 walls), as many as the original's retail save holds.
        Assert.Equal(492 + 21, level.Things.Count);
        Assert.Equal(138 + 15, level.Actors.Count);
        Assert.Equal(46, level.UnplacedObjects.Count);
        Assert.Equal(10, level.UnplacedObjects.OfType<Wall>().Count());
        Assert.All(level.UnplacedObjects, static o => Assert.Equal(1, o["INVISIBLE"]));
        Assert.NotNull(level.PlayerStart);
        Assert.True(level.Skills.Count > 200, $"expected >200 global skills, got {level.Skills.Count}");

        // Textures were linked from the WDL declarations.
        Assert.NotEmpty(level.Textures);
        Assert.Contains(level.Regions, r => r.FloorTexture is not null);
        Assert.All(level.Textures.Values, t => Assert.EndsWith(".pcx", t.File, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Load_RealGameplayLevels_DeclareMatchingGlobalSkillSets()
    {
        string[] expectedArchives = ["APATHY.WRS", "HEART.WRS", "LEGALISM.WRS", "NEWAGE.WRS"];
        Dictionary<string, string> archives = ResearchData.WrsFiles()
            .Where(path => expectedArchives.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            .GroupBy(path => Path.GetFileName(path)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (expectedArchives.Any(name => !archives.ContainsKey(name)))
        {
            return;
        }

        List<Level> levels = expectedArchives
            .Select(name => LevelLoader.Load(WrsArchive.ReadFile(archives[name]), Path.ChangeExtension(name, ".WDL")))
            .ToList();
        IReadOnlySet<string> expectedGlobals = levels[0].GlobalSkills;

        Assert.NotEmpty(expectedGlobals);
        Assert.All(
            levels.Skip(1),
            level => Assert.True(
                expectedGlobals.SetEquals(level.GlobalSkills),
                $"Expected matching GLOBAL declarations; counts were {expectedGlobals.Count} and {level.GlobalSkills.Count}."));
    }


    [Fact]
    public void Load_WholeImageBitmap_TakesItsSizeFromThePcxHeader()
    {
        // BMAP tgt,<T.PCX>; names the whole image: a thing with it needs the image's 64 x 256 size
        // to be a sprite at all (found by the shoot-mapping replay).
        byte[] pcx = new byte[128];
        pcx[0] = 0x0A;
        BitConverter.TryWriteBytes(pcx.AsSpan(8), (ushort)63);
        BitConverter.TryWriteBytes(pcx.AsSpan(10), (ushort)255);
        const string map = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\nTHING t 5 0 0 0;#1\nPLAYER_START 0 0 0 0;#2";
        const string main = "MAPFILE <m.wmp>; BMAP tgt,<T.PCX>; TEXTURE tex { SCALE_X 16; SCALE_Y 16; BMAPS tgt; } THING t { TEXTURE tex; }";
        WrsArchive archive = WrsArchive.Read(WrsArchive.Write(
        [
            new WrsFile("MAIN.WDL", System.Text.Encoding.ASCII.GetBytes(main)),
            new WrsFile("M.WMP", System.Text.Encoding.ASCII.GetBytes(map)),
            new WrsFile("T.PCX", pcx),
        ]));

        Level level = LevelLoader.Load(archive, "MAIN.WDL");

        Assert.True(OpenVirtue.Engine.Map.MapEntitySizing.TryGetSize(level, Assert.Single(level.Things), out var size));
        Assert.Equal(4, size.Width, 3);
        Assert.Equal(16, size.Height, 3);
    }
}
