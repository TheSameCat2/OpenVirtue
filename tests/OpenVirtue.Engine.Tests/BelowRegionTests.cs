// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests;

/// <summary>
/// <c>BELOW</c> regions (manual p.59–61): the map lists only the top region of a stack, and the
/// regions stacked under it share its footprint, with their own heights from the WDL.
/// </summary>
public class BelowRegionTests
{
    // A street (x 0..10, floor 0, ceiling 30) beside a bridge (x 10..20, floor 10, ceiling 30) with
    // an underpass stacked under it (floor 0, ceiling 8). The shared wall runs from (10, 0) to
    // (10, 10) with the street on its left.
    private const string Map =
        "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nVERTEX 20 0 0;#2\nVERTEX 20 10 0;#3\nVERTEX 10 10 0;#4\nVERTEX 0 10 0;#5\n" +
        "REGION street 0 30;#0\nREGION bridge 10 30;#1\n" +
        "WALL w 1 4 0 1 0 0;#0\n" +
        "WALL w 0 1 0 -1 0 0;#1\nWALL w 4 5 0 -1 0 0;#2\nWALL w 5 0 0 -1 0 0;#3\n" +
        "WALL w 1 2 1 -1 0 0;#4\nWALL w 2 3 1 -1 0 0;#5\nWALL w 3 4 1 -1 0 0;#6\n" +
        "PLAYER_START 5 5 0 0;#7";

    private const string Main =
        "MAPFILE <m.wmp>; BMAP m,<t.pcx>,0,0,64,64; TEXTURE wallTex { BMAPS m; } WALL w { TEXTURE wallTex; } " +
        "REGION under { FLOOR_HGT 0; CEIL_HGT 8; } REGION bridge { BELOW under; }";

    [Fact]
    public void LoadCore_BelowRegion_IsStackedUnderTheMapRegion()
    {
        Level level = Load();

        Assert.Equal(3, level.Regions.Count);
        Region under = level.Regions[2];
        Assert.Equal("under", under.Name);
        Assert.Equal(0, under.FloorHeight);
        Assert.Equal(8, under.CeilHeight);
        Assert.Equal(2, level.Regions[1].BelowRegion);
        Assert.Equal(1, under.AboveRegion);
        Assert.Equal(1, under.FootprintRegion);
        Assert.Equal([1, 2], LevelTopology.Stack(level, 1));
        Assert.True(under.TryGetString("TOP", out string? top));
        Assert.Equal("bridge", top);
    }

    [Fact]
    public void LoadCore_RegionsOutsideTheMap_FollowItInDeclarationOrder()
    {
        // OV-20261030-save-body round 15: the original saves the regions the map does not list by the
        // order their types are declared in, an unused declaration included, not by their tops' order.
        string main = Main.Replace("REGION under", "REGION alone { FLOOR_HGT 3; } REGION under", StringComparison.Ordinal) +
            " REGION deep { FLOOR_HGT -4; CEIL_HGT 0; } REGION street { BELOW deep; }";
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = Map };
        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(["street", "bridge", "alone", "under", "deep"], level.Regions.Select(static r => r.Name));
        Region alone = level.Regions[2];
        Assert.Equal(3, alone.FloorHeight);
        Assert.Equal(-1, alone.AboveRegion);
        Assert.Same(alone, alone.Top);
        Assert.Equal(3, level.Regions[1].BelowRegion);
        Assert.Equal(1, level.Regions[3].AboveRegion);
        Assert.Equal(4, level.Regions[0].BelowRegion);
        Assert.Equal(0, level.Regions[4].AboveRegion);
        Assert.Equal(0, level.Regions[4].FootprintRegion);
    }

    [Theory]
    [InlineData("", 0, 1)]
    [InlineData("FLAGS GROUND;", 2, 2)]
    [InlineData("FLAGS GROUND;", 12, 1)]
    public void LoadCore_ThingUnderABridge_StartsOnTheStoreyTheManualNames(string flags, double height, int region)
    {
        // Manual p.61: without GROUND the uppermost region; with GROUND the lowest region whose
        // ceiling is above the thing's feet (the underpass's ceiling is 8).
        string map = Map.Replace("PLAYER_START", "THING crate 15 5 0 1;#7\nPLAYER_START", StringComparison.Ordinal);
        string main = Main + FormattableString.Invariant($" THING crate {{ HEIGHT {height}; {flags} }}");
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        Assert.Equal(region, Assert.Single(level.Things).Region);
    }

    [Fact]
    public void TraceRegionLoop_BelowRegion_UsesItsTopRegionsWalls()
    {
        Level level = Load();

        Assert.Equal(LevelTopology.TraceRegionLoop(level, 1), LevelTopology.TraceRegionLoop(level, 2));
        Assert.NotNull(LevelTopology.TraceRegionLoop(level, 2));
    }

    [Fact]
    public void StackRegionAt_PicksTheStoreyHoldingTheHeight()
    {
        Level level = Load();

        Assert.Equal(2, LevelTopology.StackRegionAt(level, 1, 0));
        Assert.Equal(1, LevelTopology.StackRegionAt(level, 1, 12));
        Assert.Equal(0, LevelTopology.StackRegionAt(level, 0, 5));
    }

    [Fact]
    public void BuildWalls_BesideABridge_CoversOnlyTheSlabEdge()
    {
        Level level = Load();

        // The street is open from 0 to 30; the bridge column from 0 to 8 and from 10 to 30. The shared
        // wall shows only the slab between 8 and 10.
        IEnumerable<RenderVertex> shared = MeshBuilder.BuildWalls(level).Batches
            .SelectMany(static b => b.Vertices.Chunk(6))
            .Where(static quad => quad.All(static v => v.X == 10))
            .SelectMany(static quad => quad);
        Assert.Equal([8f, 10f], shared.Select(static v => v.Y).Distinct().Order());
    }

    [Fact]
    public void Build_BelowRegion_HasItsOwnFloorAndCeiling()
    {
        Level level = Load();

        IEnumerable<float> heights = MeshBuilder.Build(level).Batches
            .SelectMany(static b => b.Vertices)
            .Where(static v => v.X > 10 && v.X <= 20)
            .Select(static v => v.Y)
            .Distinct();
        Assert.Contains(0f, heights);
        Assert.Contains(8f, heights);
        Assert.Contains(10f, heights);
    }

    [Fact]
    public void MoveHorizontal_FromTheStreet_WalksUnderTheBridge()
    {
        var player = new Player(Load());
        Assert.Equal(0, player.Region);

        player.MoveHorizontal(10, 0);

        Assert.Equal(2, player.Region);
        Assert.Equal(0, player.FloorHeight);
        Assert.Equal(8, player.CeilingHeight);
    }

    [Fact]
    public void MoveHorizontal_FromUnderTheBridge_WalksBackIntoTheStreet()
    {
        var player = new Player(Load());
        player.MoveTo(2, 15, 5);

        player.MoveHorizontal(-10, 0);

        Assert.Equal(0, player.Region);
    }

    [Theory]
    [InlineData(5, 4, 15, 2, 0, 2, true)] // from the street, under the bridge
    [InlineData(5, 4, 15, 1, 10, 12, false)] // from the street at eye 4, the bridge deck's edge hides it
    [InlineData(12, 14, 18, 2, 0, 2, false)] // from the bridge, the slab hides the underpass
    [InlineData(12, 14, 18, 1, 10, 12, true)] // from the bridge, on the bridge
    public void ViewTest_BelowStack_SeesThroughStoreysAndNotThroughSlabs(
        double eyeX, double eyeHeight, double x, int region, double bottom, double top, bool visible)
    {
        Level level = Load();
        var view = new ViewTest(level);
        var viewer = new Viewer(eyeX, 5, eyeHeight, 0, ProjectionModel.Default(640, 480));

        Assert.Equal(visible, view.IsSpriteVisible(viewer, x, 5, 0.5, bottom, top, region));
    }

    [Fact]
    public void MeshBuilder_LowerStoreyWallPieces_ShowTheTexturesNextFourSides()
    {
        // Manual p.38: of a wall texture with more than four sides, "the first four sides are assigned
        // to the uppermost region, the BELOW region has the next four sides".
        string main = Main.Replace(
            "TEXTURE wallTex { BMAPS m; }",
            "BMAP u,<u.pcx>,0,0,64,64; TEXTURE wallTex { SIDES 8; BMAPS m,m,m,m,u,u,u,u; }",
            StringComparison.Ordinal);
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = Map };
        Level level = LevelLoader.LoadCore("x", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));

        MeshBatch lower = Assert.Single(MeshBuilder.Build(level).Batches, b => b.Texture == TextureAnimation.FrameKey("wallTex", 4));

        Assert.All(lower.Vertices, v => Assert.InRange(v.Y, 0, 8));
    }

    private static Level Load()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = Map };
        return LevelLoader.LoadCore("x", Main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }
}
