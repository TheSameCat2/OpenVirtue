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
        Assert.Equal("torch", Assert.Single(level.Things).Name);
        Assert.Equal("guard", Assert.Single(level.Actors).Name);
        Assert.NotNull(level.PlayerStart);
        Assert.Equal(45, level.PlayerStart!.Value.Angle);

        // Skills come from both the main script and the INCLUDEd module.
        Assert.Equal(100, level.Skills["energy"]);
        Assert.Equal(50, level.Skills["health"]);
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
            REGION room { FLOOR_HGT 0; CEIL_HGT 10; FLOOR_TEX floorTex; CEIL_TEX floorTex; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Region room = Assert.Single(level.Regions);
        Assert.Equal("floorTex", room.FloorTexture);
        Assert.Equal("floorTex", Assert.Single(level.Walls).Texture);

        LevelTexture texture = level.Textures["floorTex"];
        Assert.Equal("floor.pcx", texture.File);
        Assert.Equal(64, texture.Width);
        Assert.Equal(16, texture.ScaleX);
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
            TEXTURE skyTex { BMAPS sky_map; FLAGS SKY; }
            REGION room { CEIL_TEX skyTex; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.True(level.Textures["skyTex"].IsSky);
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
            THING Plant1 { TEXTURE plantTex; HEIGHT 3; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Thing plant = Assert.Single(level.Things);
        Assert.Equal("plantTex", plant.Texture);
        Assert.Equal(3, plant.Height);
    }

    [Fact]
    public void LoadCore_CapturesSkillMinMaxBounds()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION r 0 10;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main = "MAPFILE <m.wmp>; SKILL myHealth { VAL 100; MAX 100; MIN 0; } SKILL freeVal { VAL 5; }";

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(100, level.Skills["myHealth"]);
        SkillRange health = level.SkillBounds["myHealth"];
        Assert.Equal(0, health.Min);
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
