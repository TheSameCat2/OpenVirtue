// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine;
using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

/// <summary>The picture's <c>CLIP_DIST</c> rules (OV-20261106-clip-view).</summary>
public sealed class ViewClipTests
{
    // A room with a free wall from (20, −5) to (20, 5), 25 away by axes, and a second region across
    // the wall at x = 48.
    private const string Map = """
        VERTEX 0 -24 0;#0
        VERTEX 48 -24 0;#1
        VERTEX 48 24 0;#2
        VERTEX 0 24 0;#3
        VERTEX 20 -5 0;#4
        VERTEX 20 5 0;#5
        VERTEX 60 -24 0;#6
        VERTEX 60 24 0;#7
        REGION room 0 16;#0
        REGION far 4 16;#1
        WALL plain 0 1 0 -1 0 0;#0
        WALL plain 1 2 0 1 0 0;#1
        WALL plain 2 3 0 -1 0 0;#2
        WALL plain 3 0 0 -1 0 0;#3
        WALL WALLNAME 4 5 0 0 0 0;#4
        WALL plain 6 7 1 -1 0 0;#5
        PLAYER_START 0 0 0 0;
        """;

    private static Level Load(double clip, string flags = "", string walls = "")
    {
        string main = $$"""
            MAPFILE <m.wmp>;
            BMAP wm,<W.PCX>;
            TEXTURE wt { SCALE_X 4; SCALE_Y 4; BMAPS wm; }
            WALL plain { TEXTURE wt; }
            WALL inner { TEXTURE wt; {{flags}} }
            REGION room { FLOOR_HGT 0; CEIL_HGT 16; FLOOR_TEX wt; CEIL_TEX wt; CLIP_DIST {{clip}}; }
            REGION far { FLOOR_HGT 4; CEIL_HGT 16; FLOOR_TEX wt; CEIL_TEX wt; }
            {{walls}}
            """;
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = Map.Replace("WALLNAME", "inner", StringComparison.Ordinal),
        };
        return LevelLoader.LoadCore("x", main, name => resources.GetValueOrDefault(Path.GetFileName(name)));
    }

    private static Wall Inner(Level level) => level.Walls.Single(static wall => wall.Vertex1 == 4);

    [Theory]
    [InlineData(25.5, false)]
    [InlineData(25, true)]
    [InlineData(20, true)]
    public void IsWallOutOfReach_ComparesAxesToTheNearerVertex(double clip, bool expected)
    {
        Level level = Load(clip);

        Assert.Equal(expected, new ViewClip(level).IsWallOutOfReach(Inner(level), 0, 0));
    }

    [Fact]
    public void IsWallOutOfReach_OneVertexInReach_KeepsTheWall()
    {
        // The room's side wall runs from 24 to 72 away by axes: within reach by its nearer vertex.
        Level level = Load(30);
        Wall side = level.Walls.Single(static wall => wall.Vertex1 == 2);

        Assert.False(new ViewClip(level).IsWallOutOfReach(side, 0, 0));
    }

    [Fact]
    public void IsWallOutOfReach_SeenFromAClosedRegion_KeepsTheWall()
    {
        // OV-20261110-retail-play round 7: the region past x = 48 closed, floor on ceiling, with CLIP_DIST 0, as
        // retail's "border". Its wall at x = 60 puts the eye on that region's side of its line, but no column is
        // walked through a closed region, so the wall is not out of reach (Legalism's maze wall past a corner
        // filled the corner's column down the screen).
        string main = """
            MAPFILE <m.wmp>;
            BMAP wm,<W.PCX>;
            TEXTURE wt { SCALE_X 4; SCALE_Y 4; BMAPS wm; }
            WALL plain { TEXTURE wt; }
            WALL inner { TEXTURE wt; }
            REGION room { FLOOR_HGT 0; CEIL_HGT 16; FLOOR_TEX wt; CEIL_TEX wt; }
            REGION far { FLOOR_HGT 16; CEIL_HGT 16; FLOOR_TEX wt; CEIL_TEX wt; CLIP_DIST 0; }
            """;
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.wmp"] = Map.Replace("WALLNAME", "inner", StringComparison.Ordinal).Replace("REGION far 4 16", "REGION far 16 16", StringComparison.Ordinal),
        };
        Level level = LevelLoader.LoadCore("x", main, name => resources.GetValueOrDefault(Path.GetFileName(name)));
        Wall outer = level.Walls.Single(static wall => wall.Vertex1 == 6);

        Assert.False(new ViewClip(level).IsWallOutOfReach(outer, 0, 0));
    }

    [Fact]
    public void IsWallOutOfReach_FarFlag_KeepsTheWall()
    {
        Level level = Load(20, "FLAGS FAR;");

        Assert.False(new ViewClip(level).IsWallOutOfReach(Inner(level), 0, 0));
    }

    [Fact]
    public void AddFills_WallsOutOfReachInFront_ReachBothWays()
    {
        Level level = Load(20);
        var fills = new List<RenderVertex>();

        new ViewClip(level).AddFills(0, 0, 1, 0, fills);

        // The free wall and the room's far wall; the side walls' nearer vertices are in reach of 20?
        // They are 24 away, so they go too. Behind-the-eye walls do not.
        Assert.NotEmpty(fills);
        Assert.Equal(0, fills.Count % 6);
        Assert.Contains(fills, static vertex => vertex is { X: 20, Y: ViewClip.Reach });
        Assert.Contains(fills, static vertex => vertex is { X: 20, Y: -ViewClip.Reach });
    }

    [Fact]
    public void AddFills_EverythingBehindTheEye_AddsNothing()
    {
        Level level = Load(1);
        var fills = new List<RenderVertex>();

        new ViewClip(level).AddFills(100, 0, 1, 0, fills);

        Assert.Empty(fills);
    }

    [Fact]
    public void IsEntityInReach_MeasuresAxesAgainstTheSmallerOfTheTwoRegions()
    {
        Level level = Load(30);
        var thing = new Thing("t") { X = 22, Y = 7, Region = 0 };
        var farOff = new Thing("t") { X = 22, Y = 8, Region = 0 };

        Assert.True(ViewClip.IsEntityInReach(level, thing, level.Regions[0], 0, 0));
        Assert.False(ViewClip.IsEntityInReach(level, farOff, level.Regions[0], 0, 0));
        Assert.True(ViewClip.IsEntityInReach(level, new Thing("t") { X = 40, Y = 0, Region = 1 }, null, 0, 0));
        Assert.False(ViewClip.IsEntityInReach(level, new Thing("t") { X = 40, Y = 0, Region = 1 }, level.Regions[0], 0, 0));
    }

    [Fact]
    public void BuildOccluders_ReachUpFromAnUpperPieceAndDownFromALowerOne()
    {
        // The wall at x = 48 steps from floor 0 to floor 4: only a lower piece, reaching down from 4.
        Level level = Load(1000);

        RenderVertex[] occluders = MeshBuilder.BuildOccluders(level);

        List<RenderVertex> step = [.. occluders.Where(static vertex => vertex.X == 48)];
        Assert.Contains(step, static vertex => vertex.Y == 4);
        Assert.Contains(step, static vertex => vertex.Y == -ViewClip.Reach);

        // A wall of one region reaches both ways.
        Assert.Contains(occluders, static vertex => vertex is { X: 20, Y: ViewClip.Reach });
        Assert.DoesNotContain(step, static vertex => vertex.Y == ViewClip.Reach);
        Assert.Contains(occluders, static vertex => vertex is { X: 20, Y: -ViewClip.Reach });
    }

    [Fact]
    public void Build_RegionWithAnIsland_HasAFloorAroundTheHole()
    {
        // A room 0…40 square with a pad 10…20 square inside it: no single loop. The room's floor must
        // cover its area less the pad's, and the pad's its own.
        const string map = """
            VERTEX 0 0 0;#0
            VERTEX 40 0 0;#1
            VERTEX 40 40 0;#2
            VERTEX 0 40 0;#3
            VERTEX 10 10 0;#4
            VERTEX 20 10 0;#5
            VERTEX 20 20 0;#6
            VERTEX 10 20 0;#7
            REGION room 0 16;#0
            REGION pad 4 16;#1
            WALL plain 0 1 0 -1 0 0;#0
            WALL plain 1 2 0 -1 0 0;#1
            WALL plain 2 3 0 -1 0 0;#2
            WALL plain 3 0 0 -1 0 0;#3
            WALL plain 4 5 1 0 0 0;#4
            WALL plain 5 6 1 0 0 0;#5
            WALL plain 6 7 1 0 0 0;#6
            WALL plain 7 4 1 0 0 0;#7
            PLAYER_START 5 5 0 0;
            """;
        const string main = """
            MAPFILE <m.wmp>;
            BMAP wm,<W.PCX>;
            BMAP fm,<F.PCX>;
            BMAP gm,<G.PCX>;
            TEXTURE wt { SCALE_X 4; SCALE_Y 4; BMAPS wm; }
            TEXTURE ft { SCALE_X 4; SCALE_Y 4; BMAPS fm; }
            TEXTURE gt { SCALE_X 4; SCALE_Y 4; BMAPS gm; }
            WALL plain { TEXTURE wt; }
            REGION room { FLOOR_HGT 0; CEIL_HGT 16; FLOOR_TEX ft; CEIL_TEX wt; }
            REGION pad { FLOOR_HGT 4; CEIL_HGT 16; FLOOR_TEX gt; CEIL_TEX wt; }
            """;
        Level level = LevelLoader.LoadCore("x", main, name => Path.GetFileName(name) == "m.wmp" ? map : null);

        LevelMesh mesh = MeshBuilder.Build(level);

        Assert.Equal(1500f, Area(mesh, "ft"), 3);
        Assert.Equal(100f, Area(mesh, "gt"), 3);
    }

    private static float Area(LevelMesh mesh, string texture)
    {
        float area = 0;
        foreach (MeshBatch batch in mesh.Batches.Where(batch => batch.Texture == texture))
        {
            List<RenderVertex> vertices = [.. batch.Vertices];
            for (int i = 0; i + 2 < vertices.Count; i += 3)
            {
                RenderVertex a = vertices[i];
                RenderVertex b = vertices[i + 1];
                RenderVertex c = vertices[i + 2];
                area += MathF.Abs(((b.X - a.X) * (c.Z - a.Z)) - ((b.Z - a.Z) * (c.X - a.X))) / 2;
            }
        }

        return area;
    }

    [Fact]
    public void SpriteHidingStrips_BeyondAnOpenPad_KeepsItsDepth()
    {
        // OV-20261106-clip-view round 5 p-behind: a thing seen across a raised pad's open borders is cut
        // at them, so they do not pull its hiding depth ahead.
        var clip = new ViewClip(LoadPadRoom());

        Assert.Equal([new ViewClip.SpriteStrip(0, 1, 30)], clip.SpriteHidingStrips(0, 0, 0, 0, 1, 0, 30, 0, 2));
    }

    [Fact]
    public void SpriteHidingStrips_WideBesideItsRegionsWalls_ComesAheadOfThem()
    {
        // OV-20261106-clip-view p-wide: the walls onto a closed region bound the room; a wide thing shows
        // over them, its hiding depth ahead of the nearest within its columns.
        var clip = new ViewClip(LoadPadRoom());

        IReadOnlyList<ViewClip.SpriteStrip> strips = clip.SpriteHidingStrips(0, 0, 0, 0, 1, 0, 46, 0, 30);

        Assert.Equal(0, strips[0].From);
        Assert.Equal(1, strips[^1].To);
        // Its ends reach past the side walls at y -24 and 24 and come ahead of them; its middle keeps its depth.
        Assert.InRange(strips[0].Depth, 30, 45);
        Assert.InRange(strips[^1].Depth, 30, 45);
        Assert.Contains(strips, strip => strip.Depth == 46);
    }

    [Fact]
    public void SpriteHidingStrips_BehindAPillarOfItsOwnRegion_KeepsItsDepth()
    {
        // A closed pillar standing in the room between the eye and a thing still hides it: Apathy's
        // monsters showed through the pillars of the room they shared with the player.
        var clip = new ViewClip(LoadPillarRoom());

        Assert.Equal([new ViewClip.SpriteStrip(0, 1, 30)], clip.SpriteHidingStrips(0, 0, 0, 0, 1, 0, 30, 0, 2));
    }

    [Fact]
    public void SpriteHidingStrips_HalfBehindAPillarAndThroughAWall_HidesOnlyThePillarsColumns()
    {
        // A thing 16 wide at (30, -21): its centre shows past the pillar's corner, its left end stands
        // behind the pillar (columns up to 0.5) and its right end reaches through the room's wall at
        // y -24. One depth for the whole thing drew it over the pillar.
        var clip = new ViewClip(LoadPillarRoom());

        IReadOnlyList<ViewClip.SpriteStrip> strips = clip.SpriteHidingStrips(0, 0, 0, 0, 1, 0, 30, 21, 8);

        Assert.Equal(2, strips.Count);
        Assert.Equal(30, strips[0].Depth);
        Assert.Equal(0.125f, strips[0].To, 3);
        Assert.Equal(0.125f, strips[1].From, 3);
        Assert.Equal(1, strips[1].To);
        Assert.InRange(strips[1].Depth, 24, 25);
    }

    [Fact]
    public void SpriteHidingStrips_BehindOneOfTwoWallsAlongALine_IsNotPulledAheadByTheOther()
    {
        // The pillar's front is two walls along x = 10. A thing behind the first, wide enough to reach the
        // second's columns, took the second's depth and showed through both.
        var clip = new ViewClip(LoadPillarRoom(splitFront: true));

        IReadOnlyList<ViewClip.SpriteStrip> strips = clip.SpriteHidingStrips(0, 0, 0, 0, 1, 0, 30, 6, 12);

        Assert.All(strips.Where(strip => strip.To <= 0.75f), strip => Assert.Equal(30, strip.Depth));
    }

    [Fact]
    public void SpriteHidingStrips_SeenAcrossAStacksOpenBorder_StillComesAheadOfItsSideBorders()
    {
        // below-view deck: the player, pushed out of the deck's footprint but still counted on the deck,
        // sees the deck's 16-wide thing across the deck's near border, whole. That border has an opening
        // and hides nothing, and the thing comes ahead of the side border it reaches across.
        const string map = """
            VERTEX -16 -24 0;#0
            VERTEX 48 -24 0;#1
            VERTEX 48 24 0;#2
            VERTEX -16 24 0;#3
            VERTEX -12 12 0;#4
            VERTEX -4 12 0;#5
            VERTEX -4 20 0;#6
            VERTEX -12 20 0;#7
            REGION room 0 16;#0
            REGION solid 16 16;#1
            REGION pad 10 16;#2
            WALL plain 0 1 0 1 0 0;#0
            WALL plain 1 2 0 1 0 0;#1
            WALL plain 2 3 0 1 0 0;#2
            WALL plain 3 0 0 1 0 0;#3
            WALL plain 4 5 2 0 0 0;#4
            WALL plain 5 6 2 0 0 0;#5
            WALL plain 6 7 2 0 0 0;#6
            WALL plain 7 4 2 0 0 0;#7
            PLAYER_START 0 0 0 0;
            """;
        const string main = """
            MAPFILE <m.wmp>;
            BMAP wm,<W.PCX>;
            TEXTURE wt { SCALE_X 4; SCALE_Y 4; BMAPS wm; }
            WALL plain { TEXTURE wt; }
            REGION low { FLOOR_HGT 0; CEIL_HGT 8; }
            REGION pad { BELOW low; }
            """;
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        var clip = new ViewClip(LevelLoader.LoadCore("x", main, name => resources.GetValueOrDefault(Path.GetFileName(name))));

        // The eye at (-9.4, 9.5) looks along +y at the thing at (-6, 18).
        IReadOnlyList<ViewClip.SpriteStrip> strips = clip.SpriteHidingStrips(2, 2, -9.4f, 9.5f, 0, 1, 8.5f, 3.4f, 8);

        Assert.True(strips[0].Depth < 8.5f, $"left end at {strips[0].Depth}");
        Assert.True(strips[^1].Depth < 8.5f, $"right end at {strips[^1].Depth}");
    }

    private static Level LoadPillarRoom(bool splitFront = false)
    {
        string map = """
            VERTEX 0 -24 0;#0
            VERTEX 48 -24 0;#1
            VERTEX 48 24 0;#2
            VERTEX 0 24 0;#3
            VERTEX 10 -5 0;#4
            VERTEX 20 -5 0;#5
            VERTEX 20 5 0;#6
            VERTEX 10 5 0;#7
            REGION room 0 16;#0
            REGION solid 16 16;#1
            WALL plain 0 1 0 1 0 0;#0
            WALL plain 1 2 0 1 0 0;#1
            WALL plain 2 3 0 1 0 0;#2
            WALL plain 3 0 0 1 0 0;#3
            WALL plain 4 5 1 0 0 0;#4
            WALL plain 5 6 1 0 0 0;#5
            WALL plain 6 7 1 0 0 0;#6
            WALL plain 7 4 1 0 0 0;#7
            PLAYER_START 1 0 0 0;
            """;
        if (splitFront)
        {
            map = map.Replace("WALL plain 7 4 1 0 0 0;#7", "VERTEX 10 0 0;#8\nWALL plain 7 8 1 0 0 0;#7\nWALL plain 8 4 1 0 0 0;#8", StringComparison.Ordinal);
        }

        const string main = """
            MAPFILE <m.wmp>;
            BMAP wm,<W.PCX>;
            TEXTURE wt { SCALE_X 4; SCALE_Y 4; BMAPS wm; }
            WALL plain { TEXTURE wt; }
            """;
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        return LevelLoader.LoadCore("x", main, name => resources.GetValueOrDefault(Path.GetFileName(name)));
    }

    private static Level LoadPadRoom()
    {
        const string map = """
            VERTEX 0 -24 0;#0
            VERTEX 48 -24 0;#1
            VERTEX 48 24 0;#2
            VERTEX 0 24 0;#3
            VERTEX 10 -5 0;#4
            VERTEX 20 -5 0;#5
            VERTEX 20 5 0;#6
            VERTEX 10 5 0;#7
            REGION room 0 16;#0
            REGION pad 4 16;#1
            REGION solid 16 16;#2
            WALL plain 0 1 0 2 0 0;#0
            WALL plain 1 2 0 2 0 0;#1
            WALL plain 2 3 0 2 0 0;#2
            WALL plain 3 0 0 2 0 0;#3
            WALL plain 4 5 1 0 0 0;#4
            WALL plain 5 6 1 0 0 0;#5
            WALL plain 6 7 1 0 0 0;#6
            WALL plain 7 4 1 0 0 0;#7
            PLAYER_START 1 0 0 0;
            """;
        const string main = """
            MAPFILE <m.wmp>;
            BMAP wm,<W.PCX>;
            TEXTURE wt { SCALE_X 4; SCALE_Y 4; BMAPS wm; }
            WALL plain { TEXTURE wt; }
            """;
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        return LevelLoader.LoadCore("x", main, name => resources.GetValueOrDefault(Path.GetFileName(name)));
    }

    [Fact]
    public void BuildOccluders_InvisibleStep_HidesNothing()
    {
        // OV-20261106-clip-view p-top: past a raised pad's INVISIBLE corner the far wall shows.
        Level level = LoadInvisibleStep();

        Assert.DoesNotContain(MeshBuilder.BuildOccluders(level), static vertex => vertex.X == 48);
    }

    [Fact]
    public void Build_InvisibleStep_ContinuesTheHigherFloorsPlaneOverItsRows()
    {
        // OV-20261106-clip-view p-top: the step's rows, 0 to 4, show the far floor's plane at 4.
        Level level = LoadInvisibleStep();

        List<RenderVertex> riser = [.. MeshBuilder.Build(level).Batches
            .Where(static batch => batch.Surface)
            .SelectMany(static batch => batch.Vertices)
            .Where(static vertex => vertex.PlaneFill > 0)];

        Assert.Equal(6, riser.Count);
        Assert.All(riser, static vertex => Assert.Equal(48, vertex.X));
        Assert.All(riser, static vertex => Assert.Equal(4, vertex.PlaneY));
        Assert.Contains(riser, static vertex => vertex.Y == 0);
        Assert.Contains(riser, static vertex => vertex.Y == 4);
    }

    private static Level LoadInvisibleStep()
    {
        const string main = """
            MAPFILE <m.wmp>;
            BMAP wm,<W.PCX>;
            TEXTURE wt { SCALE_X 4; SCALE_Y 4; BMAPS wm; }
            WALL plain { TEXTURE wt; }
            WALL inner { TEXTURE wt; }
            WALL step { TEXTURE wt; FLAGS INVISIBLE; }
            REGION room { FLOOR_HGT 0; CEIL_HGT 16; FLOOR_TEX wt; CEIL_TEX wt; }
            REGION far { FLOOR_HGT 4; CEIL_HGT 16; FLOOR_TEX wt; CEIL_TEX wt; }
            """;
        string map = Map
            .Replace("WALLNAME", "inner", StringComparison.Ordinal)
            .Replace("WALL plain 1 2 0 1 0 0;#1", "WALL step 1 2 0 1 0 0;#1", StringComparison.Ordinal);
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        return LevelLoader.LoadCore("x", main, name => resources.GetValueOrDefault(Path.GetFileName(name)));
    }

    [Fact]
    public void BuildOccluders_SeeThroughWall_HidesNothing()
    {
        Level level = Load(1000, "FLAGS TRANSPARENT;");

        Assert.DoesNotContain(MeshBuilder.BuildOccluders(level), static vertex => vertex.X == 20);
    }
}
