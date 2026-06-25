// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

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
            REGION room { FLOOR_HGT 0; CEIL_HGT 10; FLOOR_TEX floorTex; }
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
