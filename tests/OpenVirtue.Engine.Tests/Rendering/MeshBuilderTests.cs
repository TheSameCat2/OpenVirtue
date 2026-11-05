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
    [InlineData(2, 1, 0.5f)] // the lamp before the wall's middle: ALBEDO 1 adds 16 levels (0.5 of ambient)
    [InlineData(2, 9, 0.5f)] // nine times as far: the same
    [InlineData(3, 1, 0.35355338f)] // 45° from the midpoint: cos 45°
    [InlineData(6, 0, 0f)] // on the wall's own line: nothing
    [InlineData(2, -1, -0.5f)] // behind the wall: 16 levels darker
    public void BuildWalls_GeniusLamp_LightsTheWallByTheMidpointCosine(int lampX, int lampY, float expected)
    {
        // OV-20261104-genius: a region's GENIUS with an AMBIENT texture lights an ALBEDO wall by one
        // value, 16 · ALBEDO · cos of the angle at the wall's midpoint, whatever the distance.
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
            vertex => Assert.Equal(expected, vertex.Ambient, 1e-5f));
    }

    [Fact]
    public void BuildWalls_WallToASolidRegion_TakesTheAmbientOfThePieceItFaces()
    {
        // OV-20261029-shading round 3 with OV-20261105-wall-light: a room with AMBIENT −0.25 darkened
        // its walls to the solid region (AMBIENT 0) by 8 levels, so the brighter side does not win; the
        // solid's floor is 10 up, so the one piece the wall shows is the room's, which the ceiling tie
        // also gives (the left region's).
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
    public void BuildWalls_SolidRegionOnTheLeft_ShowsTheRoomsFaceToItsCeiling()
    {
        // OV-20261105-wall-light with round 6's limit: region 0…10 against a solid 40…40 shows one
        // piece, 0…10 — the lower piece ends at the room's ceiling — lit as the room, whose floor is
        // the lower one. The solid's own 10…40 face never shows, so its ambient never applies.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION solid 40 40;#0\nREGION room 0 10;#1\n" +
            "WALL w 0 1 0 1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { BMAPS m; } WALL w { TEXTURE wallTex; } " +
            "REGION solid { AMBIENT 0; } REGION room { AMBIENT 0.5; }");

        List<RenderVertex> vertices = [.. MeshBuilder.BuildWalls(level).Batches.SelectMany(static b => b.Vertices)];

        Assert.Equal(6, vertices.Count);
        Assert.Contains(vertices, static v => v.Y == 10);
        Assert.DoesNotContain(vertices, static v => v.Y == 40);
        Assert.All(vertices, v => Assert.Equal(0.5f, v.Ambient));
    }

    [Fact]
    public void BuildWalls_StepUpToAPad_TakesTheLowerFloorRegionsAmbient()
    {
        // OV-20261105-wall-light (room 0.5, pad 0.25, a step up to the pad, measured): both pieces show
        // the room's — the lower piece faces the region with the lower floor, the upper the one with the
        // higher ceiling.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION room 0 10;#0\nREGION pad 2 8;#1\n" +
            "WALL w 0 1 0 1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { BMAPS m; } WALL w { TEXTURE wallTex; } " +
            "REGION room { AMBIENT 0.5; } REGION pad { AMBIENT 0.25; }");

        LevelMesh mesh = MeshBuilder.BuildWalls(level);

        Assert.All(mesh.Batches.SelectMany(static b => b.Vertices), vertex => Assert.Equal(0.5f, vertex.Ambient));
        Assert.Contains(mesh.Batches.SelectMany(static b => b.Vertices), static v => v.Y == 2);
        Assert.Contains(mesh.Batches.SelectMany(static b => b.Vertices), static v => v.Y == 8);
    }

    [Fact]
    public void BuildWalls_DroppedPadCeiling_TakesTheHigherCeilingRegionsAmbient()
    {
        // OV-20261105-wall-light (a dropped pad ceiling, measured: the room's): the pad is the LEFT
        // region here, so the left region's AMBIENT would be the pad's 0.25; the one piece is the room's.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION pad 0 8;#0\nREGION room 0 10;#1\n" +
            "WALL w 0 1 0 1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { BMAPS m; } WALL w { TEXTURE wallTex; } " +
            "REGION pad { AMBIENT 0.25; } REGION room { AMBIENT 0.5; }");

        List<RenderVertex> vertices = [.. MeshBuilder.BuildWalls(level).Batches.SelectMany(static b => b.Vertices)];

        Assert.Equal(6, vertices.Count); // the floors tie, so only the ceiling piece 8…10 shows
        Assert.All(vertices, vertex => Assert.Equal(0.5f, vertex.Ambient));
    }

    [Fact]
    public void BuildWalls_Pit_TakesTheLowerFloorRegionsAmbient()
    {
        // OV-20261105-wall-light (a pit, far step, the eye in the room, measured: the pad's): the pit is
        // the right region and 4 lower, and the lower piece faces it.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION room 0 10;#0\nREGION pit -4 10;#1\n" +
            "WALL w 0 1 0 1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { BMAPS m; } WALL w { TEXTURE wallTex; } " +
            "REGION room { AMBIENT 0.5; } REGION pit { AMBIENT 0.25; }");

        LevelMesh mesh = MeshBuilder.BuildWalls(level);

        Assert.All(mesh.Batches.SelectMany(static b => b.Vertices), vertex => Assert.Equal(0.25f, vertex.Ambient));
        Assert.Contains(mesh.Batches.SelectMany(static b => b.Vertices), static v => v.Y == -4);
    }

    [Theory]
    [InlineData(false, 0, -1f, -2f)] // no flag: the repeats stack from height 0
    [InlineData(true, 0, 0f, -1f)] // POSITION 0: from the piece's lower edge
    [InlineData(true, 1, 1f, 0f)] // POSITION 1: from its upper edge
    public void BuildWalls_PortcullisOnALowerPiece_StacksFromThePositionsEdge(
        bool portcullis, int position, float vAtBottom, float vAtTop)
    {
        // OV-20261105-wall-pieces (portcullis): a wall's rows hang from the edge its POSITION names, at
        // each vertex's own edge of its own piece. The 64 px bitmap at 16 px a step repeats every 4
        // steps, and the piece (the two floors, 4 and 8) is one repeat.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION low 4 16;#0\nREGION high 8 16;#1\n" +
            "WALL w 0 1 0 1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { SCALE_XY 16, 16; BMAPS m; } " +
            (portcullis
                ? FormattableString.Invariant($"WALL w {{ TEXTURE wallTex; FLAGS PORTCULLIS; POSITION {position}; }}")
                : "WALL w { TEXTURE wallTex; }"));

        List<RenderVertex> vertices = [.. MeshBuilder.BuildWalls(level).Batches.SelectMany(static b => b.Vertices)];

        Assert.Equal(6, vertices.Count); // one piece, 4…8
        Assert.All(vertices.Where(static v => v.Y == 4), v => Assert.Equal(vAtBottom, v.V, 1e-3f));
        Assert.All(vertices.Where(static v => v.Y == 8), v => Assert.Equal(vAtTop, v.V, 1e-3f));
    }

    [Theory]
    [InlineData(0, 0f, -1f)] // POSITION 0: from the upper piece's lower edge, 12
    [InlineData(1, 1f, 0f)] // POSITION 1: from its own upper edge, 16
    public void BuildWalls_PortcullisOnAnUpperPiece_StacksFromThePositionsEdge(int position, float vAtBottom, float vAtTop)
    {
        // OV-20261105-wall-pieces: the ceiling piece 12…16 of two regions whose floors tie.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION low 0 12;#0\nREGION high 0 16;#1\n" +
            "WALL w 0 1 0 1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { SCALE_XY 16, 16; BMAPS m; } " +
            FormattableString.Invariant($"WALL w {{ TEXTURE wallTex; FLAGS PORTCULLIS; POSITION {position}; }}"));

        List<RenderVertex> vertices = [.. MeshBuilder.BuildWalls(level).Batches.SelectMany(static b => b.Vertices)];

        Assert.Equal(6, vertices.Count);
        Assert.All(vertices.Where(static v => v.Y == 12), v => Assert.Equal(vAtBottom, v.V, 1e-3f));
        Assert.All(vertices.Where(static v => v.Y == 16), v => Assert.Equal(vAtTop, v.V, 1e-3f));
    }

    [Theory]
    [InlineData(0, 0f, -1f, 0f, -1f)] // each piece anchors at its own lower edge, 4 and 12
    [InlineData(1, 1f, 0f, 1f, 0f)] // and at its own upper edge, 8 and 16
    public void BuildWalls_PortcullisOnATwoPieceWall_AnchorsEachPieceItself(
        int position, float vAtLowerBottom, float vAtLowerTop, float vAtUpperBottom, float vAtUpperTop)
    {
        // OV-20261105-wall-pieces: pieces 4…8 and 12…16, so each Y belongs to one piece only.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION low 4 12;#0\nREGION high 8 16;#1\n" +
            "WALL w 0 1 0 1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { SCALE_XY 16, 16; BMAPS m; } " +
            FormattableString.Invariant($"WALL w {{ TEXTURE wallTex; FLAGS PORTCULLIS; POSITION {position}; }}"));

        List<RenderVertex> vertices = [.. MeshBuilder.BuildWalls(level).Batches.SelectMany(static b => b.Vertices)];

        Assert.Equal(12, vertices.Count); // two pieces
        Assert.All(vertices.Where(static v => v.Y == 4), v => Assert.Equal(vAtLowerBottom, v.V, 1e-3f));
        Assert.All(vertices.Where(static v => v.Y == 8), v => Assert.Equal(vAtLowerTop, v.V, 1e-3f));
        Assert.All(vertices.Where(static v => v.Y == 12), v => Assert.Equal(vAtUpperBottom, v.V, 1e-3f));
        Assert.All(vertices.Where(static v => v.Y == 16), v => Assert.Equal(vAtUpperTop, v.V, 1e-3f));
    }

    [Theory]
    [InlineData(0, 0f, 0f, -1.5f, -1.5f)] // the bottom edge's row stays put along the slope
    [InlineData(1, 1.5f, 1.5f, 0f, 0f)] // and so does the top edge's
    public void BuildWalls_PortcullisOnASlopedEdge_KeepsTheAnchorsRowAlongIt(
        int position, float vAtFirstBottom, float vAtSecondBottom, float vAtFirstTop, float vAtSecondTop)
    {
        // OV-20261105-wall-pieces (measured: the row next to the sloped edge stays the bitmap's last row
        // at POSITION 0 and first row at POSITION 1 along the whole wall): each vertex anchors at its own
        // edge of the piece, so the sloped bottom (4 at x 0, 6 at x 4) holds v 0 on both ends rather than
        // −0.5 at the second.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 2;#1\nREGION room 4 10;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { SCALE_XY 16, 16; BMAPS m; } " +
            FormattableString.Invariant($"WALL w {{ TEXTURE wallTex; FLAGS PORTCULLIS; POSITION {position}; }} ") +
            "REGION room { FLAGS FLOOR_ASCEND, CEIL_ASCEND; }");

        List<RenderVertex> vertices = [.. MeshBuilder.BuildWalls(level).Batches.SelectMany(static b => b.Vertices)];

        // The ends differ in height (4 and 6 below, 10 and 12 above) but each bottom vertex is its own
        // piece's lower edge and each top vertex its upper one; Y tells the two ends apart.
        Assert.All(vertices.Where(static v => v.Y == 4), v => Assert.Equal(vAtFirstBottom, v.V, 1e-3f));
        Assert.All(vertices.Where(static v => v.Y == 6), v => Assert.Equal(vAtSecondBottom, v.V, 1e-3f));
        Assert.All(vertices.Where(static v => v.Y == 10), v => Assert.Equal(vAtFirstTop, v.V, 1e-3f));
        Assert.All(vertices.Where(static v => v.Y == 12), v => Assert.Equal(vAtSecondTop, v.V, 1e-3f));
    }

    [Fact]
    public void BuildWalls_PortcullisWithOffsetY_SlidesTheAnchoredRows()
    {
        // OV-20261105-wall-pieces: OFFSET_Y still counts on top of the anchor, one pixel of the bitmap
        // (64 rows) taken off v, and a placed wall carries the original's extra 1/1024 step.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION low 4 16;#0\nREGION high 8 16;#1\n" +
            "WALL w 0 1 0 1 0 8;#0\nPLAYER_START 0 0 0 0;#1",
            "BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { SCALE_XY 16, 16; BMAPS m; } " +
            "WALL w { TEXTURE wallTex; FLAGS PORTCULLIS; POSITION 0; }");

        List<RenderVertex> vertices = [.. MeshBuilder.BuildWalls(level).Batches.SelectMany(static b => b.Vertices)];

        Assert.All(vertices.Where(static v => v.Y == 4), v => Assert.Equal(-0.125f, v.V, 1e-3f));
        Assert.All(vertices.Where(static v => v.Y == 8), v => Assert.Equal(-1.125f, v.V, 1e-3f));
    }

    [Theory]
    [InlineData(0, 8, 0, 16, 16, 11f, 12f)] // the upper piece 8…16: one repeat down from 16 is 12
    [InlineData(0, 8, 0, 16, 48, float.NaN, float.NaN)] // above the piece: nothing
    [InlineData(0, 8, 0, 16, 30, 8f, 8.5f)] // cut at the piece's edge, half the square
    public void BuildWalls_AttachOnATwoRegionWall_IsCutToThePieceItLiesOn(
        int leftFloor, int leftCeiling, int rightFloor, int rightCeiling, double posY, float expectedLow, float expectedHigh)
    {
        // OV-20261105-wall-pieces (measured on pieces 8…16 with R 16): POS_Y 16 shows z 11–12, POS_Y 48
        // nothing, POS_Y 30 only 8…8.5.
        Level level = AttachLevel(
            Wall8(FormattableString.Invariant($"REGION a {leftFloor} {leftCeiling};#0\nREGION b {rightFloor} {rightCeiling};#1\n")),
            0,
            posY);

        MeshBatch? attach = Square(level);

        if (float.IsNaN(expectedLow))
        {
            Assert.Null(attach); // nothing inside the piece to draw
            return;
        }

        Assert.NotNull(attach);
        Assert.Equal(12, attach.Vertices.Count); // one quad a hair off each face
        Assert.Equal(expectedLow, attach.Vertices.Min(static v => v.Y), 1e-3f);
        Assert.Equal(expectedHigh, attach.Vertices.Max(static v => v.Y), 1e-3f);
    }

    [Fact]
    public void BuildWalls_AttachOnAFreeWall_MeasuresItsRepeatsFromHeightZero()
    {
        // OV-20261105-wall-pieces (measured): a free wall in a region with floor 9 and R 8 hides the
        // attach at POS_Y 8 (top 7, under the floor) and shows it at POS_Y −24 at z 10–11. The anchor is
        // the fixed height 0, not the region's floor.
        Level freeWall = AttachLevel(
            "VERTEX 32 -4 0;#0\nVERTEX 32 4 0;#1\nREGION room 9 17;#0\n" + "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            0,
            8,
            wallScale: "SCALE_XY 4, 8");

        Assert.Null(Square(freeWall));

        Level below = AttachLevel(
            "VERTEX 32 -4 0;#0\nVERTEX 32 4 0;#1\nREGION room 9 17;#0\n" + "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            0,
            -24,
            wallScale: "SCALE_XY 4, 8");

        MeshBatch attach = Assert.IsType<MeshBatch>(Square(below));
        Assert.Equal(10, attach.Vertices.Min(static v => v.Y), 1e-3f);
        Assert.Equal(11, attach.Vertices.Max(static v => v.Y), 1e-3f);

        // A pit's floor 4 below: the same fixed 0 leaves the attach at z −3…−2 (measured).
        Level pit = AttachLevel(
            "VERTEX 32 -4 0;#0\nVERTEX 32 4 0;#1\nREGION pit -4 0;#0\n" + "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            0,
            72);

        MeshBatch inPit = Assert.IsType<MeshBatch>(Square(pit));
        Assert.Equal(-3, inPit.Vertices.Min(static v => v.Y), 1e-3f);
        Assert.Equal(-2, inPit.Vertices.Max(static v => v.Y), 1e-3f);
    }

    [Theory]
    // Only the upper piece 11…16, POSITION 0, POS_Y 48: from the lowest lower edge, 11, to 15, so 14–15.
    [InlineData("0 11", "0 16", 0, 48, 14f, 15f)]
    // Pieces 0…3 and 11…16, POSITION 0, POS_Y 56: from 0, to 2, so the lower piece's 1–2 only.
    [InlineData("0 11", "3 16", 0, 56, 1f, 2f)]
    // The same pieces, POS_Y 4: to 15, so the upper piece's 14–15.
    [InlineData("0 11", "3 16", 0, 4, 14f, 15f)]
    // The same pieces, POSITION 1, POS_Y 72: from the highest upper edge, 16, to 14, so 13–14.
    [InlineData("0 11", "3 16", 1, 72, 13f, 14f)]
    // Only the lower piece 0…5, POSITION 1, POS_Y 72: from 5 to 3, so 2–3.
    [InlineData("0 11", "5 11", 1, 72, 2f, 3f)]
    public void BuildWalls_AttachOnAPortcullisWall_MeasuresFromThePositionsEdge(
        string leftRegion, string rightRegion, int position, double posY, float expectedLow, float expectedHigh)
    {
        // OV-20261105-wall-pieces (measured): POSITION 0 anchors the attach at the lowest lower edge of
        // the pieces the wall shows and POSITION 1 at the highest upper edge, then one repeat R 16.
        Level level = AttachLevel(
            Wall8(
                "REGION a " + leftRegion + ";#0\nREGION b " + rightRegion + ";#1\n",
                "WALL w 0 1 0 1 0 0"),
            0,
            posY,
            FormattableString.Invariant($"FLAGS PORTCULLIS; POSITION {position};"));

        MeshBatch attach = Assert.IsType<MeshBatch>(Square(level));

        Assert.Equal(12, attach.Vertices.Count); // one quad, cut to the one piece it lies on
        Assert.Equal(expectedLow, attach.Vertices.Min(static v => v.Y), 1e-3f);
        Assert.Equal(expectedHigh, attach.Vertices.Max(static v => v.Y), 1e-3f);
    }

    [Theory]
    [InlineData("a 0 13", "b 13 13", "SCALE_XY 4, 8", 0, -24, 10f, 11f)] // lower piece 0…13, R 8
    [InlineData("pit -4 0", "pit 0 0", "SCALE_XY 4, 4", 0, 56, -3f, -2f)] // pit piece −4…0, R 16
    public void BuildWalls_AttachOnAPortcullisWall_UsesThePiecesOwnEdges(
        string leftRegion,
        string rightRegion,
        string wallScale,
        int position,
        double posY,
        float expectedLow,
        float expectedHigh)
    {
        // OV-20261105-wall-pieces (measured): a lower piece 0…13 with R 8 and POSITION 0, POS_Y −24 shows
        // z 10–11; a pit piece −4…0 with POSITION 0, POS_Y 56 shows z −3…−2.
        string wall = leftRegion.StartsWith("pit", StringComparison.Ordinal) ? "WALL w 0 1 0 -1 0 0" : "WALL w 0 1 0 1 0 0";
        Level level = AttachLevel(
            Wall8("REGION " + leftRegion + ";#0\nREGION " + rightRegion + ";#1\n", wall),
            0,
            posY,
            FormattableString.Invariant($"FLAGS PORTCULLIS; POSITION {position};"),
            wallScale);

        MeshBatch attach = Assert.IsType<MeshBatch>(Square(level));

        Assert.Equal(expectedLow, attach.Vertices.Min(static v => v.Y), 1e-3f);
        Assert.Equal(expectedHigh, attach.Vertices.Max(static v => v.Y), 1e-3f);
    }

    [Fact]
    public void BuildWalls_AttachFollowsTheWallsOffsets()
    {
        // OV-20261105-wall-pieces (measured: R 16, SCALE 4, OFFSET 8,8 and POS 16,16 put s0 at 2 and the
        // top at 10): OFFSET_X slides the attach back along the wall, OFFSET_Y further down.
        Level level = AttachLevel(
            Wall8("REGION a 0 8;#0\nREGION b 0 16;#1\n", "WALL w 0 1 0 1 8 8"),
            16,
            16);

        MeshBatch attach = Assert.IsType<MeshBatch>(Square(level));

        // s0 2 of 8 steps from the second vertex at map y 4, one step wide: map y 2…1, render z −2…−1.
        Assert.Equal(-2, attach.Vertices.Min(static v => v.Z), 1e-2f);
        Assert.Equal(-1, attach.Vertices.Max(static v => v.Z), 1e-2f);
        Assert.Equal(9, attach.Vertices.Min(static v => v.Y), 1e-3f);
        Assert.Equal(10, attach.Vertices.Max(static v => v.Y), 1e-3f);
    }

    [Fact]
    public void BuildWalls_AttachOnAWallToASolidRegion_StaysBelowTheRoomsCeiling()
    {
        // OV-20261105-wall-pieces round 6: a wall to a solid region shows the room's face 0…10 only, so
        // the pieces cannot overlap and the attach is one quad, inside that face.
        Level level = AttachLevel(
            Wall8("REGION room 0 10;#0\nREGION solid 40 40;#1\n", "WALL w 0 1 0 1 0 0"),
            0,
            48);

        MeshBatch attach = Assert.IsType<MeshBatch>(Square(level));

        Assert.Equal(12, attach.Vertices.Count); // one quad a hair off each face
        Assert.Equal(3, attach.Vertices.Min(static v => v.Y), 1e-3f);
        Assert.Equal(4, attach.Vertices.Max(static v => v.Y), 1e-3f);
    }

    [Theory]
    [InlineData(41)]
    [InlineData(43)]
    public void BuildWalls_PortcullisAgainstASolidRegion_AnchorsAtTheRoomsCeiling(int solid)
    {
        // OV-20261105-wall-pieces round 6 (sol-p1, sol-p1-b): a room 0…16 against a solid whose floor
        // and ceiling are 41 (and 43, which separates the room's ceiling from the height-0 grid): the
        // wall shows only 0…16, so POSITION 1 anchors at 16 and the row just below it is the bitmap's
        // first, where 41 would put row 2 and 43 row 6 there.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION room 0 16;#0\nREGION solid " + solid + " " + solid + ";#1\n" +
            "WALL w 0 1 0 1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { SCALE_XY 16, 16; BMAPS m; } " +
            "WALL w { TEXTURE wallTex; FLAGS PORTCULLIS; POSITION 1; }");

        List<RenderVertex> vertices = [.. MeshBuilder.BuildWalls(level).Batches.SelectMany(static b => b.Vertices)];

        Assert.Equal(6, vertices.Count); // one piece, 0…16
        Assert.DoesNotContain(vertices, static v => v.Y > 16);
        Assert.All(vertices.Where(static v => v.Y == 16), v => Assert.Equal(0, v.V, 1e-3f));
        Assert.All(vertices.Where(static v => v.Y == 0), v => Assert.Equal(4, v.V, 1e-3f)); // 16 at 4 px a step
    }

    [Fact]
    public void BuildWalls_PortcullisAgainstASolidRegionAtPositionZero_AnchorsAtTheFloor()
    {
        // OV-20261105-wall-pieces round 6 (sol-p0): POSITION 0 on the same wall counts from the limited
        // piece's bottom 0, the plain grid: the bitmap's first row sits at z 0, its last just above
        // that, and its first again one repeat up at z 4.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION room 0 16;#0\nREGION solid 41 41;#1\n" +
            "WALL w 0 1 0 1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { SCALE_XY 16, 16; BMAPS m; } " +
            "WALL w { TEXTURE wallTex; FLAGS PORTCULLIS; POSITION 0; }");

        List<RenderVertex> vertices = [.. MeshBuilder.BuildWalls(level).Batches.SelectMany(static b => b.Vertices)];

        Assert.All(vertices.Where(static v => v.Y == 0), v => Assert.Equal(0, v.V, 1e-3f));
        Assert.All(vertices.Where(static v => v.Y == 16), v => Assert.Equal(-4, v.V, 1e-3f));
    }

    [Fact]
    public void BuildWalls_PortcullisWithACeilingOffTheRepeat_AnchorsAtTheRoomsCeiling()
    {
        // OV-20261105-wall-pieces round 6: a room whose ceiling is 10 against a solid 40…40 shows 0…10,
        // and POSITION 1 anchors at 10 — off the 4-step repeat, so v at the floor is 2.5, not a whole
        // repeat that the height-0 grid would have given.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION room 0 10;#0\nREGION solid 40 40;#1\n" +
            "WALL w 0 1 0 1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { SCALE_XY 16, 16; BMAPS m; } " +
            "WALL w { TEXTURE wallTex; FLAGS PORTCULLIS; POSITION 1; }");

        List<RenderVertex> vertices = [.. MeshBuilder.BuildWalls(level).Batches.SelectMany(static b => b.Vertices)];

        Assert.DoesNotContain(vertices, static v => v.Y > 10);
        Assert.All(vertices.Where(static v => v.Y == 10), v => Assert.Equal(0, v.V, 1e-3f));
        Assert.All(vertices.Where(static v => v.Y == 0), v => Assert.Equal(2.5f, v.V, 1e-3f));
    }

    [Theory]
    [InlineData(80, 11f, 12f)] // 16 + 16 − 20: the room's ceiling plus one repeat
    [InlineData(180, float.NaN, float.NaN)] // 16 + 16 − 45 is under the floor; 41 would show 11–12
    public void BuildWalls_AttachOnAWallToASolidRegion_MeasuresFromTheRoomsCeiling(
        double posY, float expectedLow, float expectedHigh)
    {
        // OV-20261105-wall-pieces round 6 (sol-att-a, sol-att-b): the attach's POSITION 1 origin is the
        // limited piece's top 16, which the attach carriers separate from both the height-0 grid (which
        // would hide both squares) and the solid's own top 41 (which would hide sol-att-a and show
        // sol-att-b).
        Level level = AttachLevel(
            Wall8("REGION room 0 16;#0\nREGION solid 41 41;#1\n", "WALL w 0 1 0 1 0 0"),
            0,
            posY,
            "FLAGS PORTCULLIS; POSITION 1;");

        MeshBatch? attach = Square(level);

        if (float.IsNaN(expectedLow))
        {
            Assert.Null(attach);
            return;
        }

        Assert.NotNull(attach);
        Assert.Equal(12, attach.Vertices.Count);
        Assert.Equal(expectedLow, attach.Vertices.Min(static v => v.Y), 1e-3f);
        Assert.Equal(expectedHigh, attach.Vertices.Max(static v => v.Y), 1e-3f);
    }

    [Fact]
    public void BuildWalls_AttachOnAWallToASolidRegionAtPositionZero_MeasuresFromTheFloor()
    {
        // OV-20261105-wall-pieces round 6 (sol-att-c): POSITION 0 counts from the limited piece's
        // bottom 0, so POS_Y 48 (12 steps) puts the square at 3–4.
        Level level = AttachLevel(
            Wall8("REGION room 0 16;#0\nREGION solid 41 41;#1\n", "WALL w 0 1 0 1 0 0"),
            0,
            48,
            "FLAGS PORTCULLIS; POSITION 0;");

        MeshBatch attach = Assert.IsType<MeshBatch>(Square(level));

        Assert.Equal(3, attach.Vertices.Min(static v => v.Y), 1e-3f);
        Assert.Equal(4, attach.Vertices.Max(static v => v.Y), 1e-3f);
    }

    [Fact]
    public void BuildWalls_GapUnderARaisedPad_ShowsTheRoomsFaceAndThePadsStep()
    {
        // OV-20261105-wall-pieces round 6 (gap-p1, gap-p1-b): a room 0…16 against a pad 19…24 shows
        // 0…16 and 19…24 — the lower piece ends at the room's ceiling, the upper starts at the pad's
        // floor — so the band between them is the opening, not an overlap.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION room 0 16;#0\nREGION pad 19 24;#1\n" +
            "WALL w 0 1 0 1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { SCALE_XY 16, 16; BMAPS m; } WALL w { TEXTURE wallTex; }");

        List<RenderVertex> vertices = [.. MeshBuilder.BuildWalls(level).Batches.SelectMany(static b => b.Vertices)];

        Assert.Equal(12, vertices.Count);
        Assert.Equal([0f, 16f, 19f, 24f], vertices.Select(static v => v.Y).Distinct().Order().ToArray());
    }

    [Fact]
    public void BuildWalls_AttachOnARetailSignWall_ShowsBelowTheRoomsCeiling()
    {
        // OV-20261105-wall-pieces round 6, by reasoning: retail's mall sign wall stands in a region
        // 0…10 against a solid border at 40…40, its 128-row bitmap at SCALE_Y 16 (a repeat of 8). The
        // wall shows 0…10 only, and the attach at POSITION 0, POS_Y −30 lands at 0 + 8 + 30/16 = 9.875,
        // so its one step hangs just under the ceiling, inside the face.
        Level level = AttachLevel(
            Wall8("REGION mall 0 10;#0\nREGION border 40 40;#1\n", "WALL w 0 1 0 1 0 0"),
            0,
            -30,
            "FLAGS PORTCULLIS; POSITION 0;",
            wallScale: "SCALE_XY 16, 16",
            wallBitmapHeight: 128);

        MeshBatch attach = Assert.IsType<MeshBatch>(Square(level));

        Assert.Equal(8.875f, attach.Vertices.Min(static v => v.Y), 1e-3f);
        Assert.Equal(9.875f, attach.Vertices.Max(static v => v.Y), 1e-3f);
        List<RenderVertex> wall = [.. MeshBuilder.BuildWalls(level).Batches
            .Where(static b => b.Texture == "wallTex").SelectMany(static b => b.Vertices)];
        Assert.Equal(6, wall.Count); // the room's face 0…10, not the border's 40
        Assert.All(wall, v => Assert.True(v.Y <= 10, FormattableString.Invariant($"wall vertex at {v.Y}")));
    }

    [Fact]
    public void BuildWalls_WallTextureSharedWithASprite_UsesThePlainMapping()
    {
        // OV-20261105-wall-pieces round 6 (round 5 review finding): a bitmap a sprite also draws keeps
        // its real rows, so the wall maps it with its own height — 12 rows at 4 px a step repeat every
        // 3 steps, a 12-step wall being four repeats — and the image the wall samples matches.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION room 0 12;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nTHING sprite 0 0 0 0;#1\nPLAYER_START 0 0 0 0;#2",
            "BMAP m,<t.pcx>,0,0,8,12; TEXTURE shared { SCALE_XY 4, 4; BMAPS m; } " +
            "WALL w { TEXTURE shared; } THING sprite { TEXTURE shared; }");

        List<RenderVertex> vertices = [.. MeshBuilder.BuildWalls(level).Batches.SelectMany(static b => b.Vertices)];

        Assert.Empty(TextureLoader.RowMaskedWallTextures(level));
        Assert.Equal(-4, vertices.Min(static v => v.V), 1e-3f);
        Assert.Equal(0, vertices.Max(static v => v.V), 1e-3f);
    }

    [Fact]
    public void BuildWalls_WallRetexturedAtRunTime_KeepsTheLoadTimeSet()
    {
        // OV-20261105-wall-pieces round 6 (round 5 review finding): the set is the walls as loaded,
        // because that is when the bitmaps were expanded. A script naming a bitmap no wall named then
        // leaves it out, so the wall maps it plainly — the 12 rows at 4 px a step, four repeats over a
        // 12-step wall — and the image it samples is the unexpanded one.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION room 0 12;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "BMAP a,<a.pcx>,0,0,8,16; BMAP b,<b.pcx>,0,0,8,12; " +
            "TEXTURE wallTex { SCALE_XY 4, 4; BMAPS a; } TEXTURE other { SCALE_XY 4, 4; BMAPS b; } " +
            "WALL w { TEXTURE wallTex; }");

        Assert.Empty(TextureLoader.RowMaskedWallTextures(level));
        level.Walls[0].Texture = "other";

        List<RenderVertex> vertices = [.. MeshBuilder.BuildWalls(level).Batches.SelectMany(static b => b.Vertices)];

        Assert.Empty(TextureLoader.RowMaskedWallTextures(level)); // still the load-time set
        Assert.Equal(-4, vertices.Min(static v => v.V), 1e-3f);
    }

    [Fact]
    public void BuildWalls_WallTextureSharedWithASurface_MapsBothWithThePeriod()
    {
        // OV-20261105-wall-pieces round 6 (round 5 review finding): a 12-row bitmap a placed wall names
        // is expanded to its 16-row period, and a floor sharing it reads that same image, so both span
        // the period: 4 px a step over 16 rows is a quarter of v a step, the walls' 12 steps three
        // quarters of it and the floor's 4 map steps a whole one.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nVERTEX 4 4 0;#2\nVERTEX 0 4 0;#3\n" +
            "REGION room 0 12;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\nWALL w 2 3 0 -1 0 0;#2\n" +
            "WALL w 3 0 0 -1 0 0;#3\nPLAYER_START 0 0 0 0;#4",
            "BMAP m,<t.pcx>,0,0,8,12; TEXTURE shared { SCALE_XY 4, 4; BMAPS m; } " +
            "WALL w { TEXTURE shared; } REGION room { FLOOR_TEX shared; }");

        LevelMesh mesh = MeshBuilder.Build(level);

        Assert.Contains("shared", TextureLoader.RowMaskedWallTextures(level));
        MeshBatch surface = Assert.Single(mesh.Batches, static b => b.Surface);
        Assert.Equal(0, surface.Vertices.Min(static v => v.V), 1e-3f);
        Assert.Equal(1, surface.Vertices.Max(static v => v.V), 1e-3f); // 4 map steps at 4/16 a step
        MeshBatch wall = Assert.Single(mesh.Batches, static b => b.Texture == "shared" && !b.Surface);
        Assert.Equal(-3, wall.Vertices.Min(static v => v.V), 1e-3f); // 12 steps at 4/16, not 4/12
    }

    [Fact]
    public void Build_SurfaceTextureOutsideTheWallSet_UsesThePlainMapping()
    {
        // OV-20261105-wall-pieces round 6: a floor's bitmap that no wall names keeps its real rows and
        // its own height's mapping, so 4 map steps at 4 px over 12 rows are a third of the bitmap.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nVERTEX 4 4 0;#2\nVERTEX 0 4 0;#3\n" +
            "REGION room 0 12;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\nWALL w 2 3 0 -1 0 0;#2\n" +
            "WALL w 3 0 0 -1 0 0;#3\nPLAYER_START 0 0 0 0;#4",
            "BMAP m,<t.pcx>,0,0,64,64; BMAP floorMap,<f.pcx>,0,0,8,12; " +
            "TEXTURE wallTex { BMAPS m; } TEXTURE floorTex { SCALE_XY 4, 4; BMAPS floorMap; } " +
            "WALL w { TEXTURE wallTex; } REGION room { FLOOR_TEX floorTex; }");

        LevelMesh mesh = MeshBuilder.Build(level);

        Assert.DoesNotContain("floorTex", TextureLoader.RowMaskedWallTextures(level));
        MeshBatch surface = Assert.Single(mesh.Batches, static b => b.Surface);
        Assert.Equal(1.3333f, surface.Vertices.Max(static v => v.V), 1e-3f);
    }

    [Theory]
    [InlineData(72, 0.25f)] // the lower piece −4…0: the pit's (the pad's, measured)
    [InlineData(24, 0.5f)] // the upper piece 8…10: the room's
    public void BuildWalls_AttachTakesTheAmbientOfThePieceItLiesOn(double posY, float expected)
    {
        // OV-20261105-wall-light: a room 0…10 (0.5) and a pad 4 lower with a ceiling 2 lower (0.25).
        Level level = AttachLevel(
            Wall8("REGION room 0 10;#0\nREGION pad -4 8;#1\n", "WALL w 0 1 0 1 0 0"),
            0,
            posY,
            declarations: "REGION room { AMBIENT 0.5; } REGION pad { AMBIENT 0.25; }");

        MeshBatch attach = Assert.IsType<MeshBatch>(Square(level));

        Assert.All(attach.Vertices, vertex => Assert.Equal(expected, vertex.Ambient));
    }

    [Fact]
    public void BuildWalls_CyclingAttach_AnimatesWithTheWall()
    {
        // OV-20261105-wall-pieces: retail's indicator attach has CYCLES 2 on a wall whose texture has
        // CYCLES 2 and toggles with it, so the attach's batch resolves its frame from the wall's CYCLE.
        Level level = LevelFrom(
            "VERTEX 32 -4 0;#0\nVERTEX 32 4 0;#1\nREGION room 0 16;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "BMAP m,<t.pcx>,0,0,64,64; BMAP q,<q.pcx>,0,0,16,16; BMAP q2,<q2.pcx>,0,0,16,16; " +
            "TEXTURE wallTex { CYCLES 2; BMAPS m, m; } " +
            "TEXTURE square { CYCLES 2; BMAPS q, q2; POS_Y 16; } " +
            "WALL w { TEXTURE wallTex; ATTACH square; }");

        MeshBatch attach = Assert.Single(MeshBuilder.BuildWalls(level).Batches, static b => b.Texture == "square");

        Assert.Same(level.Walls[0], attach.AnimationSource);
        Assert.True(attach.AlphaTest);
        Assert.Equal(2, level.Textures["square"].Cycles);
    }

    [Fact]
    public void BuildWalls_AttachIsDrawnOnATwoRegionWall()
    {
        // OV-20261105-wall-pieces: before this a wall between two regions never called for its attach.
        Level level = AttachLevel(
            Wall8("REGION a 0 8;#0\nREGION b 0 16;#1\n", "WALL w 0 1 0 1 0 0"),
            0,
            16);

        Assert.NotNull(Square(level));
    }

    [Fact]
    public void BuildWalls_NonPowerOfTwoBitmap_ReadsTheMaskedRowPeriod()
    {
        // OV-20261105-wall-pieces: a 12-row bitmap reads rows 0…11 and then 8…11 again — a period of 16
        // — so at 4 px a step one repeat is 16 rows and a 4-step wall is one of them, not 4/12.
        Level level = LevelFrom(
            "VERTEX 0 0 0;#0\nVERTEX 4 0 0;#1\nREGION room 0 4;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            "BMAP m,<t.pcx>,0,0,8,12; TEXTURE wallTex { SCALE_XY 4, 4; BMAPS m; } WALL w { TEXTURE wallTex; }");

        MeshBatch batch = Assert.Single(MeshBuilder.BuildWalls(level).Batches);

        Assert.Equal(-1, batch.Vertices.Min(static v => v.V), 1e-3f);
        Assert.Equal(0, batch.Vertices.Max(static v => v.V), 1e-3f);
    }

    [Fact]
    public void BuildWalls_AttachRepeat_UsesTheBitmapsRealHeight()
    {
        // OV-20261105-wall-pieces (measured: 60 rows at 4 px a step repeat every 15 steps, not the
        // period's 16): the attach is one repeat down from 0, so its top is 14.
        Level level = AttachLevel(
            "VERTEX 32 -4 0;#0\nVERTEX 32 4 0;#1\nREGION room 0 16;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nPLAYER_START 0 0 0 0;#1",
            0,
            4,
            wallScale: "SCALE_XY 4, 4",
            wallBitmapHeight: 60);

        MeshBatch attach = Assert.IsType<MeshBatch>(Square(level));

        Assert.Equal(13, attach.Vertices.Min(static v => v.Y), 1e-3f);
        Assert.Equal(14, attach.Vertices.Max(static v => v.Y), 1e-3f);
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
        // OV-20261105-wall-pieces: the attach now counts the wall's OFFSET_Y too, and a placed wall
        // carries the original's one raw step above its WMP/WDL value (1/1024 of a step).
        Assert.Equal(11, attach.Vertices.Min(v => v.Y), 3);
        Assert.Equal(12, attach.Vertices.Max(v => v.Y), 3);
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
        // OV-20261104-texmap: a surface's columns run along −x and its rows along +y; OV-20261104-blend:
        // FLOOR_OFFS_X 16 of 64 comes off the column, FLOOR_OFFS_Y 32 adds to the row.
        Assert.Equal(-1.25f, floor.Vertices.Min(v => v.U));
        Assert.Equal(-0.25f, floor.Vertices.Max(v => v.U));
        Assert.Equal(0.5f, floor.Vertices.Min(v => v.V));
        Assert.Equal(1.5f, floor.Vertices.Max(v => v.V));

        MeshBatch ceiling = mesh.Batches.Single(b => b.Texture == "ceilTex");
        Assert.Equal(-1.125f, ceiling.Vertices.Min(v => v.U));
        Assert.Equal(-0.125f, ceiling.Vertices.Max(v => v.U));
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

    /// <summary>A level from one map and the WDL that declares its textures (no image files).</summary>
    private static Level LevelFrom(string map, string main)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        return LevelLoader.LoadCore("x", "MAPFILE <m.wmp>; " + main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }

    /// <summary>An 8-step wall at x 32 from map y −4 to 4, so its u origin (the second vertex) is at y 4.</summary>
    private static string Wall8(string regions, string wall = "WALL w 0 1 0 1 0 0") =>
        "VERTEX 32 -4 0;#0\nVERTEX 32 4 0;#1\n" + regions + wall + ";#0\nPLAYER_START 0 0 0 0;#1";

    /// <summary>
    /// A wall on <see cref="Wall8"/> whose wall texture is 64 px tall at <paramref name="wallScale"/>
    /// (16 steps a repeat by default), its <c>ATTACH</c> a 16 px square (1 step) at POS_X/POS_Y.
    /// </summary>
    private static Level AttachLevel(
        string map,
        double posX,
        double posY,
        string wallProperties = "",
        string wallScale = "SCALE_XY 4, 4",
        string declarations = "",
        int wallBitmapHeight = 64)
    {
        string main =
            FormattableString.Invariant($"BMAP m,<t.pcx>,0,0,64,{wallBitmapHeight}; BMAP q,<q.pcx>,0,0,16,16; ") +
            $"TEXTURE wallTex {{ {wallScale}; BMAPS m; }} " +
            FormattableString.Invariant($"TEXTURE square {{ BMAPS q; POS_X {posX}; POS_Y {posY}; }} ") +
            FormattableString.Invariant($"WALL w {{ TEXTURE wallTex; ATTACH square; {wallProperties} }} ") +
            declarations;
        return LevelFrom(map, main);
    }

    /// <summary>The wall's attach batch, or null when nothing of it shows.</summary>
    private static MeshBatch? Square(Level level) =>
        MeshBuilder.BuildWalls(level).Batches.FirstOrDefault(static batch => batch.Texture == "square");
}
