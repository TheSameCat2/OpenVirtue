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

    [Theory]
    [InlineData(4, 8, 0, 1)]     // west of the wall, square on: green
    [InlineData(4, 8, -12, 0)]   // west, towards v1: red
    [InlineData(4, 8, 12, 1)]    // west, towards v2: green
    [InlineData(4, 40, 0, 3)]    // east, square on: blue
    [InlineData(4, 40, -12, 2)]  // east, towards v1: yellow
    [InlineData(4, 40, 12, 3)]   // east, towards v2: blue
    [InlineData(2, 8, 0, 1)]
    [InlineData(2, 8, -12, 0)]
    [InlineData(2, 8, 12, 1)]
    [InlineData(2, 40, 0, 1)]    // a two-sided wall shows the same pair from both faces
    [InlineData(2, 40, -12, 0)]
    public void BuildWalls_SidedTexture_ShowsTheSideTheViewpointPicks(int sides, double eyeX, double eyeY, int side)
    {
        // OV-20261030-wall-sides: a wall from v1 (24, -6) to v2 (24, 6), screenshotted in V3.963 from
        // 16 west or east of it, square on or off towards an end.
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 24 -6 0;#0\nVERTEX 24 6 0;#1\nREGION room 0 10;#0\nWALL w 0 1 0 0 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        string main = "MAPFILE <m.wmp>; BMAP a, <a.pcx>; BMAP b, <b.pcx>; BMAP c, <c.pcx>; BMAP d, <d.pcx>; " +
            (sides == 4 ? "TEXTURE t { SIDES 4; BMAPS a, b, c, d; }" : "TEXTURE t { SIDES 2; BMAPS a, b; }") + " WALL w { TEXTURE t; }";
        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        MeshBatch batch = Assert.Single(MeshBuilder.BuildWalls(level).Batches);
        Wall wall = Assert.IsType<Wall>(batch.AnimationSource); // its own batch, to pick the side per frame
        Assert.Equal(side, TextureAnimation.WallSide(level.Textures["t"], 24, -6, 24, 6, eyeX, eyeY));
        Assert.Same(level.Walls[0], wall);
    }

    [Theory]
    [InlineData(2, 1, 0.5f)] // the lamp on the room's side: ALBEDO 1 adds 16 levels (0.5 of ambient)
    [InlineData(2, -1, 0f)] // behind the wall: nothing
    public void BuildWalls_GeniusLamp_BrightensTheFaceItStandsBefore(int lampX, int lampY, float expected)
    {
        // OV-20261029-shading rounds 4 and 5: a region's GENIUS with an AMBIENT texture lights ALBEDO walls.
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION room 0 10;#0\nREGION solid 10 10;#1\n" +
                        "WALL w 0 1 0 1 0 0;#0\n" + FormattableString.Invariant($"THING lamp {lampX} {lampY} 0 0;#1\n") + "PLAYER_START 0 0 0 0;#2",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            BMAP m, <t.pcx>, 0, 0, 64, 64;
            TEXTURE wallTex { BMAPS m; ALBEDO 1; }
            TEXTURE lampTex { BMAPS m; AMBIENT 1; }
            THING lamp { TEXTURE lampTex; }
            WALL w { TEXTURE wallTex; }
            REGION room { FLOOR_HGT 0; CEIL_HGT 10; GENIUS lamp; }
            REGION solid { FLOOR_HGT 10; CEIL_HGT 10; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.All(
            MeshBuilder.BuildWalls(level).Batches.SelectMany(static batch => batch.Vertices),
            vertex => Assert.Equal(expected, vertex.Ambient));
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
        // OV-20261104-attach: the bitmap's bottom row sits on the floor and v falls with the height.
        Assert.Equal(-1, batch.Vertices.Min(v => v.V), 1e-4);
        Assert.Equal(0, batch.Vertices.Max(v => v.V), 1e-4);
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
        // OV-20261104-texmap: OFFSET_Y 32 of 64 slides the texture half a repeat down.
        Assert.Equal(-1.5f, batch.Vertices.Min(v => v.V), 1e-4f);
        Assert.Equal(-0.5f, batch.Vertices.Max(v => v.V), 1e-4f);
    }

    [Fact]
    public void BuildWalls_TextureRepeatsStackFromHeightZero()
    {
        // OV-20261104-texmap (raised, pit): a wall from height 4 to 8 with a 16-step repeat shows the
        // rows a quarter to a half repeat down from the grid's top at 16, not from its own floor.
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION room 4 8;#0\n" +
                        "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            BMAP m, <t.pcx>, 0, 0, 64, 64;
            TEXTURE wallTex { SCALE_XY 4, 4; BMAPS m; }
            WALL w { TEXTURE wallTex; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
        MeshBatch batch = Assert.Single(MeshBuilder.BuildWalls(level).Batches);

        Assert.Equal(-0.5f, batch.Vertices.Min(v => v.V), 1e-4f);
        Assert.Equal(-0.25f, batch.Vertices.Max(v => v.V), 1e-4f);
    }

    [Fact]
    public void BuildWalls_AttachTexture_LiesAtItsPosFromTheWallTexturesUpperLeft()
    {
        // OV-20261104-attach (wall-pos): a wall 8 steps long at x 32 from map y −4 to 4, its 64 px
        // texture at 4 px a step (16 steps a repeat), the 16 px square attached at POS_X 24, POS_Y 16:
        // the square is 1 step wide at its own default scale, 6 to 7 steps from the u origin at the
        // second vertex and 4 to 5 steps below the top of the first repeat (12 down to 11).
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 32 -4 0;#0\nVERTEX 32 4 0;#1\nREGION room 0 16;#0\n" +
                        "WALL w 0 1 0 0 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string main =
            """
            MAPFILE <m.wmp>;
            BMAP m, <t.pcx>, 0, 0, 64, 64;
            BMAP q, <q.pcx>, 0, 0, 16, 16;
            TEXTURE wallTex { SCALE_XY 4, 4; BMAPS m; }
            TEXTURE square { BMAPS q; POS_X 24; POS_Y 16; }
            WALL w { TEXTURE wallTex; ATTACH square; }
            """;

        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
        LevelMesh mesh = MeshBuilder.BuildWalls(level);

        MeshBatch attach = Assert.Single(mesh.Batches, b => b.Texture == "square");
        Assert.True(attach.AlphaTest);
        Assert.Equal(12, attach.Vertices.Count); // one quad a hair off each face
        Assert.Equal(11, attach.Vertices.Min(v => v.Y), 4);
        Assert.Equal(12, attach.Vertices.Max(v => v.Y), 4);
        // Map y 4 (the second vertex) is render z −4; 6 to 7 steps toward the first vertex.
        Assert.Equal(-(4 - 7), attach.Vertices.Max(v => v.Z), 1);
        Assert.Equal(-(4 - 6), attach.Vertices.Min(v => v.Z), 1);
        Assert.Equal(0, attach.Vertices.Min(v => v.U));
        Assert.Equal(1, attach.Vertices.Max(v => v.U));
        Assert.Equal(0, attach.Vertices.Min(v => v.V));
        Assert.Equal(1, attach.Vertices.Max(v => v.V));
    }

    [Fact]
    public void BuildWalls_AttachTexture_IsCutToTheWall()
    {
        // OV-20261104-attach (wall-half, wall-neg): half a step past the u origin shows half the
        // square; a square wholly beyond the wall is not drawn.
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = "VERTEX 32 -4 0;#0\nVERTEX 32 4 0;#1\nREGION room 0 16;#0\n" +
                        "WALL w 0 1 0 0 0 0;#0\nPLAYER_START 0 0 0 0;#1",
        };
        const string half =
            """
            MAPFILE <m.wmp>;
            BMAP m, <t.pcx>, 0, 0, 64, 64;
            BMAP q, <q.pcx>, 0, 0, 16, 16;
            TEXTURE wallTex { SCALE_XY 4, 4; BMAPS m; }
            TEXTURE square { BMAPS q; POS_X -2; POS_Y 0; }
            WALL w { TEXTURE wallTex; ATTACH square; }
            """;

        Level level = LevelLoader.LoadCore("x", half, n => resources.GetValueOrDefault(Path.GetFileName(n)));
        MeshBatch attach = Assert.Single(MeshBuilder.BuildWalls(level).Batches, b => b.Texture == "square");
        Assert.Equal(0.5f, attach.Vertices.Min(v => v.U));
        Assert.Equal(1, attach.Vertices.Max(v => v.U));
        Assert.Equal(-4, attach.Vertices.Min(v => v.Z), 1);
        Assert.Equal(-3.5f, attach.Vertices.Max(v => v.Z), 1);

        Level beyond = LevelLoader.LoadCore("x", half.Replace("POS_X -2; POS_Y 0;", "POS_X -8; POS_Y -8;"), n => resources.GetValueOrDefault(Path.GetFileName(n)));
        Assert.DoesNotContain(MeshBuilder.BuildWalls(beyond).Batches, b => b.Texture == "square");
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

        // The walls and the surfaces share the texture but are separate batches: the surfaces
        // are drawn without writing depth (OV-20261104-attach).
        LevelMesh mesh = MeshBuilder.Build(level);
        Assert.Equal(2, mesh.Batches.Count);
        Assert.Single(mesh.Batches, static batch => batch.Surface);
        List<RenderVertex> vertices = mesh.Batches.SelectMany(static batch => batch.Vertices).ToList();

        Assert.Contains(vertices, v => v.Y == 7);
        Assert.Contains(vertices, v => v.Y == 17);
        Assert.Contains(vertices, v => v.Y == 5);
        Assert.Contains(vertices, v => v.Y == 15);
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
        // OV-20261104-texmap: a surface's columns run along −x and its rows along +y.
        Assert.Equal(-0.75f, floor.Vertices.Min(v => v.U));
        Assert.Equal(0.25f, floor.Vertices.Max(v => v.U));
        Assert.Equal(0.5f, floor.Vertices.Min(v => v.V));
        Assert.Equal(1.5f, floor.Vertices.Max(v => v.V));

        MeshBatch ceiling = mesh.Batches.Single(b => b.Texture == "ceilTex");
        Assert.Equal(-0.875f, ceiling.Vertices.Min(v => v.U));
        Assert.Equal(0.125f, ceiling.Vertices.Max(v => v.U));
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
