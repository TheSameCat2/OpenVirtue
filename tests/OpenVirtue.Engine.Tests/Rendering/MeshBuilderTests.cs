// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Engine.Tests.TestSupport;
using OpenVirtue.Formats.Wrs;

namespace OpenVirtue.Engine.Tests.Rendering;

public class MeshBuilderTests
{
    [Fact]
    public void BuildWalls_EmitsOneTexturedQuadPerWall()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION room 0 10;#0\n" +
                        "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            BMAP m, <t.pcx>, 0, 0, 64, 64;
            TEXTURE floorTex { BMAPS m; }
            WALL w { TEXTURE floorTex; }
            REGION room { FLOOR_HGT 0; CEIL_HGT 10; FLOOR_TEX floorTex; AMBIENT 0.6; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
        LevelMesh mesh = MeshBuilder.BuildWalls(level);

        MeshBatch batch = Assert.Single(mesh.Batches);
        Assert.Equal("floorTex", batch.Texture);
        Assert.Equal(6, batch.Vertices.Count); // one quad = two triangles
        Assert.Equal(6, mesh.VertexCount);

        // The quad spans the region's floor (y=0) to ceiling (y=10).
        Assert.Contains(batch.Vertices, v => v.Y == 0);
        Assert.Contains(batch.Vertices, v => v.Y == 10);
        Assert.All(batch.Vertices, vertex => Assert.Equal(0.6f, vertex.Ambient));
    }

    [Fact]
    public void BuildWalls_WallToASolidRegion_TakesTheLeftRegionsAmbient()
    {
        // OV-20261029-shading round 3: a room with AMBIENT −0.25 darkened its walls to the solid
        // region (AMBIENT 0) by 8 levels, so the brighter side does not win.
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION room 0 10;#0\nREGION solid 10 10;#1\n" +
                        "WALL w 0 1 0 1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            BMAP m, <t.pcx>, 0, 0, 64, 64;
            TEXTURE wallTex { BMAPS m; }
            WALL w { TEXTURE wallTex; }
            REGION room { FLOOR_HGT 0; CEIL_HGT 10; AMBIENT -0.25; }
            REGION solid { FLOOR_HGT 10; CEIL_HGT 10; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
        LevelMesh mesh = MeshBuilder.BuildWalls(level);

        Assert.NotEmpty(mesh.Batches);
        Assert.All(mesh.Batches.SelectMany(static batch => batch.Vertices), vertex => Assert.Equal(-0.25f, vertex.Ambient));
    }

    [Fact]
    public void BuildWalls_TwoSidedWallWithMatchingHeights_IsOpenPortal()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\n" +
                        "REGION left 0 10;#0\nREGION right 0 10;#1\n" +
                        "WALL w 0 1 0 1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            "MAPFILE <m.wmp>; BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { BMAPS m; } " +
            "WALL w { TEXTURE wallTex; }";
        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        LevelMesh mesh = MeshBuilder.BuildWalls(level);

        Assert.Empty(mesh.Batches);
        Assert.Equal(0, mesh.VertexCount);
    }

    [Fact]
    public void BuildWalls_TwoSidedWall_EmitsOnlyFloorAndCeilingHeightDifferences()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\n" +
                        "REGION left 0 12;#0\nREGION right 2 10;#1\n" +
                        "WALL w 0 1 0 1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            "MAPFILE <m.wmp>; BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { BMAPS m; } " +
            "WALL w { TEXTURE wallTex; }";
        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        MeshBatch batch = Assert.Single(MeshBuilder.BuildWalls(level).Batches);

        Assert.Equal(12, batch.Vertices.Count);
        Assert.Contains(batch.Vertices, vertex => vertex.Y == 0);
        Assert.Contains(batch.Vertices, vertex => vertex.Y == 2);
        Assert.Contains(batch.Vertices, vertex => vertex.Y == 10);
        Assert.Contains(batch.Vertices, vertex => vertex.Y == 12);
    }

    [Fact]
    public void Build_SkyTextures_AreExcludedFromPerspectiveGeometry()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] =
                "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nVERTEX 4 4 0;#2\nVERTEX 0 4 0;#3\n" +
                "REGION room 0 10;#0\n" +
                "WALL skyWall 0 1 0 -1 0 0;#0\nWALL skyWall 1 2 0 -1 0 0;#1\n" +
                "WALL skyWall 2 3 0 -1 0 0;#2\nWALL skyWall 3 0 0 -1 0 0;#3\n" +
                "PLAYER_START 0 0 0 0;#4",
        };
        const string main =
            "MAPFILE <m.wmp>; BMAP skyMap,<sky.pcx>,0,0,64,64; " +
            "TEXTURE skyTex { BMAPS skyMap; FLAGS SKY; } " +
            "REGION room { FLOOR_TEX skyTex; CEIL_TEX skyTex; } WALL skyWall { TEXTURE skyTex; }";
        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        LevelMesh mesh = MeshBuilder.Build(level);

        Assert.Empty(mesh.Batches);
        Assert.Equal(0, mesh.VertexCount);
    }

    [Fact]
    public void Build_AnimatedWorldSurfaces_SelectCurrentTexturePhase()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] =
                "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nVERTEX 4 4 0;#2\nVERTEX 0 4 0;#3\n" +
                "REGION room 0 10;#0\n" +
                "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\n" +
                "WALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\nPLAYER_START 0 0 0 0;#4",
        };
        const string main =
            "MAPFILE <m.wmp>; BMAP p1,<p1.pcx>,0,0,64,64; BMAP p2,<p2.pcx>,0,0,64,64; " +
            "TEXTURE animated { CYCLES 2; BMAPS p1,p2; } " +
            "REGION room { FLOOR_TEX animated; CEIL_TEX animated; CYCLE 2; } " +
            "WALL w { TEXTURE animated; CYCLE 2; }";
        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        LevelMesh mesh = MeshBuilder.Build(level);

        Assert.All(mesh.Batches, batch => Assert.Equal("animated", batch.Texture));
        MeshBatch regionBatch = Assert.Single(
            mesh.Batches,
            batch => ReferenceEquals(batch.AnimationSource, level.Regions[0]));
        Assert.Equal(12, regionBatch.Vertices.Count);
        Assert.Equal(4, mesh.Batches.Count(batch => batch.AnimationSource is Wall));
        Assert.Equal(36, mesh.VertexCount);
    }

    [Fact]
    public void BuildWalls_UsesTextureScaleAsPixelsPerStep()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION room 0 4;#0\n" +
                        "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            BMAP m, <t.pcx>, 0, 0, 64, 64;
            TEXTURE wallTex { SCALE_XY 16, 16; BMAPS m; }
            WALL w { TEXTURE wallTex; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
        MeshBatch batch = Assert.Single(MeshBuilder.BuildWalls(level).Batches);

        Assert.Equal(0, batch.Vertices.Min(v => v.U));
        Assert.Equal(1, batch.Vertices.Max(v => v.U));
        Assert.Equal(0, batch.Vertices.Min(v => v.V));
        Assert.Equal(1, batch.Vertices.Max(v => v.V));
    }

    [Fact]
    public void BuildWalls_AppliesWallTextureOffsetsInPixels()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION room 0 4;#0\n" +
                        "WALL w 0 1 0 -1 16 32;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            BMAP m, <t.pcx>, 0, 0, 64, 64;
            TEXTURE wallTex { SCALE_XY 16, 16; BMAPS m; }
            WALL w { TEXTURE wallTex; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
        MeshBatch batch = Assert.Single(MeshBuilder.BuildWalls(level).Batches);

        Assert.Equal(0.25f, batch.Vertices.Min(v => v.U));
        Assert.Equal(1.25f, batch.Vertices.Max(v => v.U));
        Assert.Equal(0.5f, batch.Vertices.Min(v => v.V));
        Assert.Equal(1.5f, batch.Vertices.Max(v => v.V));
    }

    [Fact]
    public void BuildWalls_SharedTextureSeparatesTransparentAndOpaqueMaterials()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nVERTEX 4 4 0;#2\n" +
                        "REGION room 0 4;#0\nWALL solid 0 1 0 -1 0 0;#0\n" +
                        "WALL grate 1 2 0 -1 0 0;#1\nPLAYER_START 0 0 0 0;#2",
        };
        const string main =
            "MAPFILE <m.wmp>; BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { BMAPS m; } " +
            "WALL solid { TEXTURE wallTex; } WALL grate { TEXTURE wallTex; FLAGS TRANSPARENT; }";
        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        IReadOnlyList<MeshBatch> batches = MeshBuilder.BuildWalls(level).Batches;

        Assert.Equal(2, batches.Count);
        Assert.Single(batches, batch => !batch.AlphaTest);
        Assert.Single(batches, batch => batch.AlphaTest);
        Assert.All(batches, batch => Assert.Equal(6, batch.Vertices.Count));
    }

    [Fact]
    public void Build_AddsFloorAndCeilingForASquareRegion()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] =
                "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nVERTEX 4 4 0;#2\nVERTEX 0 4 0;#3\n" +
                "REGION room 0 10;#0\n" +
                "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\n" +
                "WALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\nPLAYER_START 0 0 0 0;#4",
        };
        const string main = "MAPFILE <m.wmp>; REGION room { FLOOR_HGT 0; CEIL_HGT 10; }";
        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        LevelMesh wallsOnly = MeshBuilder.BuildWalls(level);
        LevelMesh full = MeshBuilder.Build(level);

        // The square region adds a floor (2 triangles) and a ceiling (2 triangles) = 12 vertices.
        Assert.Equal(wallsOnly.VertexCount + 12, full.VertexCount);
    }

    [Fact]
    public void Build_UsesVertexHeightOffsetsForWallsFloorsAndCeilings()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] =
                "VERTEX 0 0 2;#0\nVERTEX 4 0 2;#1\nVERTEX 4 4 0;#2\nVERTEX 0 4 0;#3\n" +
                "REGION room 5 15;#0\n" +
                "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\n" +
                "WALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\nPLAYER_START 0 0 0 0;#4",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            BMAP m, <t.pcx>, 0, 0, 64, 64;
            TEXTURE floorTex { BMAPS m; }
            REGION room { FLOOR_TEX floorTex; CEIL_TEX floorTex; FLAGS FLOOR_ASCEND, CEIL_ASCEND; }
            WALL w { TEXTURE floorTex; }
            """;
        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        MeshBatch batch = Assert.Single(MeshBuilder.Build(level).Batches);

        Assert.Contains(batch.Vertices, v => v.Y == 7);
        Assert.Contains(batch.Vertices, v => v.Y == 17);
        Assert.Contains(batch.Vertices, v => v.Y == 5);
        Assert.Contains(batch.Vertices, v => v.Y == 15);
    }

    [Fact]
    public void Build_AppliesRegionSurfaceTextureOffsetsInPixels()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] =
                "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nVERTEX 4 4 0;#2\nVERTEX 0 4 0;#3\n" +
                "REGION room 0 4;#0\n" +
                "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\n" +
                "WALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\nPLAYER_START 0 0 0 0;#4",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            BMAP floorMap, <floor.pcx>, 0, 0, 64, 64;
            BMAP ceilMap, <ceil.pcx>, 0, 0, 64, 64;
            TEXTURE floorTex { SCALE_XY 16, 16; BMAPS floorMap; }
            TEXTURE ceilTex { SCALE_XY 16, 16; BMAPS ceilMap; }
            REGION room
            {
                FLOOR_TEX floorTex;
                CEIL_TEX ceilTex;
                FLOOR_OFFS_X 16;
                FLOOR_OFFS_Y 32;
                CEIL_OFFS_X 8;
                CEIL_OFFS_Y 24;
            }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
        LevelMesh mesh = MeshBuilder.Build(level);

        MeshBatch floor = mesh.Batches.Single(b => b.Texture == "floorTex");
        Assert.Equal(0.25f, floor.Vertices.Min(v => v.U));
        Assert.Equal(1.25f, floor.Vertices.Max(v => v.U));
        Assert.Equal(0.5f, floor.Vertices.Min(v => v.V));
        Assert.Equal(1.5f, floor.Vertices.Max(v => v.V));

        MeshBatch ceiling = mesh.Batches.Single(b => b.Texture == "ceilTex");
        Assert.Equal(0.125f, ceiling.Vertices.Min(v => v.U));
        Assert.Equal(1.125f, ceiling.Vertices.Max(v => v.U));
        Assert.Equal(0.375f, ceiling.Vertices.Min(v => v.V));
        Assert.Equal(1.375f, ceiling.Vertices.Max(v => v.V));
    }

    /// <summary>
    /// Mirrors the `ovtool level info` mesh path for every local retail archive when
    /// user-supplied game data is present. No-op otherwise.
    /// </summary>
    [Fact]
    public void Build_LocalRetailArchives_ProducesInspectionMeshes()
    {
        IReadOnlyList<string> archives = ResearchData.WrsFiles();
        if (archives.Count == 0)
        {
            return;
        }

        foreach (string path in archives)
        {
            Level level = LevelLoader.Load(WrsArchive.ReadFile(path), ResearchData.MainWdlName(path));

            LevelMesh wallsOnly = MeshBuilder.BuildWalls(level);
            LevelMesh full = MeshBuilder.Build(level);

            Assert.True(wallsOnly.VertexCount > 0, $"{Path.GetFileName(path)}: expected wall geometry");
            Assert.True(full.VertexCount >= wallsOnly.VertexCount, $"{Path.GetFileName(path)}: full mesh should include walls");
            Assert.True(full.VertexCount % 3 == 0, $"{Path.GetFileName(path)}: vertices must form whole triangles");
        }
    }

    [Fact]
    public void Build_RealApathy_AddsSurfacesBeyondWalls()
    {
        string? apathy = ResearchData.WrsFiles()
            .FirstOrDefault(p => Path.GetFileName(p).Equals("apathy.wrs", StringComparison.OrdinalIgnoreCase));
        if (apathy is null)
        {
            return; // no local game data — skip
        }

        Level level = LevelLoader.Load(WrsArchive.ReadFile(apathy), "APATHY.WDL");

        LevelMesh wallsOnly = MeshBuilder.BuildWalls(level);
        LevelMesh full = MeshBuilder.Build(level);

        Assert.True(full.VertexCount > wallsOnly.VertexCount, "expected floor/ceiling surfaces to add geometry");
        Assert.True(full.VertexCount % 3 == 0, "vertices must form whole triangles");
    }

    [Fact]
    public void BuildWalls_RealApathy_ProducesGeometry()
    {
        string? apathy = ResearchData.WrsFiles()
            .FirstOrDefault(p => Path.GetFileName(p).Equals("apathy.wrs", StringComparison.OrdinalIgnoreCase));
        if (apathy is null)
        {
            return; // no local game data — skip
        }

        Level level = LevelLoader.Load(WrsArchive.ReadFile(apathy), "APATHY.WDL");
        LevelMesh mesh = MeshBuilder.BuildWalls(level);

        Assert.NotEmpty(mesh.Batches);
        Assert.True(mesh.VertexCount > 0);
        Assert.True(mesh.VertexCount % 3 == 0, "vertices must form whole triangles");
    }

}
