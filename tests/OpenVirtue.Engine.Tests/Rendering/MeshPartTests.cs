// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

/// <summary>The level's mesh split into the moved regions' part and the rest.</summary>
public sealed class MeshPartTests
{
    // A room (0) with a raised pad (1) inside it and a second room (2) beside it across a portal.
    private const string Map = """
        VERTEX 0 0 0;#0
        VERTEX 20 0 0;#1
        VERTEX 20 20 0;#2
        VERTEX 0 20 0;#3
        VERTEX 5 5 0;#4
        VERTEX 10 5 0;#5
        VERTEX 10 10 0;#6
        VERTEX 5 10 0;#7
        VERTEX 40 0 0;#8
        VERTEX 40 20 0;#9
        REGION room 0 16;#0
        REGION pad 2 16;#1
        REGION hall 0 12;#2
        WALL w 0 1 0 -1 0 0;#0
        WALL w 1 2 0 2 0 0;#1
        WALL w 2 3 0 -1 0 0;#2
        WALL w 3 0 0 -1 0 0;#3
        WALL w 4 5 1 0 0 0;#4
        WALL w 5 6 1 0 0 0;#5
        WALL w 6 7 1 0 0 0;#6
        WALL w 7 4 1 0 0 0;#7
        WALL w 1 8 2 -1 0 0;#8
        WALL w 8 9 2 -1 0 0;#9
        WALL w 9 2 2 -1 0 0;#10
        PLAYER_START 2 2 0 0;
        """;

    private const string Main = """
        MAPFILE <m.wmp>;
        BMAP wm,<W.PCX>;
        TEXTURE wt { SCALE_X 4; SCALE_Y 4; BMAPS wm; }
        WALL w { TEXTURE wt; }
        REGION room { FLOOR_TEX wt; CEIL_TEX wt; }
        REGION pad { FLOOR_TEX wt; CEIL_TEX wt; }
        REGION hall { FLOOR_TEX wt; CEIL_TEX wt; }
        """;

    [Fact]
    public void AddMoved_Pad_TakesTheRoomAroundIt()
    {
        Level level = Load();
        var moving = new HashSet<int>();

        Assert.True(MeshPart.AddMoved(level, 1, moving));
        Assert.False(MeshPart.AddMoved(level, 1, moving));

        // The pad cuts the room's floor, so the room moves with it; the hall beyond does not.
        Assert.Equal([0, 1], moving.Order());
    }

    [Fact]
    public void Build_MovingPartAndRest_MakeTheWholeMesh()
    {
        Level level = Load();
        var moving = new HashSet<int>();
        MeshPart.AddMoved(level, 1, moving);

        int whole = Count(MeshBuilder.Build(level));
        int rest = Count(MeshBuilder.Build(level, part: new MeshPart(moving, movingPart: false)));
        int part = Count(MeshBuilder.Build(level, part: new MeshPart(moving, movingPart: true)));

        Assert.True(rest > 0);
        Assert.True(part > 0);
        Assert.Equal(whole, rest + part);
        Assert.Equal(
            MeshBuilder.BuildOccluders(level).Length,
            MeshBuilder.BuildOccluders(level, new MeshPart(moving, false)).Length + MeshBuilder.BuildOccluders(level, new MeshPart(moving, true)).Length);
    }

    private static int Count(LevelMesh mesh) => mesh.Batches.Sum(batch => batch.Vertices.Count);

    private static Level Load()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = Map };
        return LevelLoader.LoadCore("x", Main, name => resources.GetValueOrDefault(Path.GetFileName(name)));
    }
}
