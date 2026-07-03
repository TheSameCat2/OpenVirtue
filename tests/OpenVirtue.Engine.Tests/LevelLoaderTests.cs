// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Tests.TestSupport;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Engine.Tests;

public class LevelLoaderTests
{
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

        // Skills come from both the main script and the INCLUDEd module.
        Assert.Equal(100, level.Skills["energy"]);
        Assert.Equal(50, level.Skills["health"]);
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
            BMAP floor_map, <floor.pcx>, 0, 0, 64, 64;
            TEXTURE floorTex { SCALE_XY 16, 16; BMAPS floor_map; }
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
        Assert.Equal("floorTex", Assert.Single(level.Walls).Texture);

        LevelTexture texture = level.Textures["floorTex"];
        Assert.Equal("floor.pcx", texture.File);
        Assert.Equal(64, texture.Width);
        Assert.Equal(16, texture.ScaleX);
        Assert.Equal(0, texture.Ambient);
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
        Assert.Equal(6, wall.OffsetY);
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
            REGION r { CLIP_DIST 512; FLOOR_ANGLE 0.25; CEIL_ANGLE 0.5; }
            WALL gate { CYCLE 2; POSITION 0.75; MAP_COLOR 12; DIST 30; }
            THING pickup { CYCLE 3; MAP_COLOR 22; DIST 40; }
            ACTOR guard
            {
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
        Assert.Equal(22, pickup["map_color"]);
        Assert.Equal(40, pickup["dist"]);

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
        Assert.Equal(1073, level.Regions.Count);
        Assert.Equal(5894, level.Walls.Count);
        Assert.Equal(492, level.Things.Count);
        Assert.Equal(138, level.Actors.Count);
        Assert.NotNull(level.PlayerStart);
        Assert.True(level.Skills.Count > 200, $"expected >200 global skills, got {level.Skills.Count}");

        // Textures were linked from the WDL declarations.
        Assert.NotEmpty(level.Textures);
        Assert.Contains(level.Regions, r => r.FloorTexture is not null);
        Assert.All(level.Textures.Values, t => Assert.EndsWith(".pcx", t.File, StringComparison.OrdinalIgnoreCase));
    }

}
