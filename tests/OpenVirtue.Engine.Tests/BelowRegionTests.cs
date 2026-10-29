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

    private static Level Load()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = Map };
        return LevelLoader.LoadCore("x", Main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }
}
