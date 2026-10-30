// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

/// <summary>
/// The headless view test of ADR-0010, on the layouts of OV-20261020-view-activity: the eye at
/// height 8 in a room 16 high, <c>PLAYER_ARC</c> 1.
/// </summary>
public sealed class ViewTestTests
{
    // A plain free-standing wall from (30, -20) to (30, 0), and a pillar region (floor = ceiling)
    // on x 10..14, y 10..14.
    private const string Map =
        "VERTEX 30 -20 0;#0\nVERTEX 30 0 0;#1\n" +
        "VERTEX 10 10 0;#2\nVERTEX 14 10 0;#3\nVERTEX 14 14 0;#4\nVERTEX 10 14 0;#5\n" +
        "REGION room 0 16;#0\nREGION pillar 16 16;#1\n" +
        "WALL screen 0 1 0 0 0 0;#0\n" +
        "WALL side 2 3 1 0 0 0;#1\nWALL side 3 4 1 0 0 0;#2\nWALL side 4 5 1 0 0 0;#3\nWALL side 5 2 1 0 0 0;#4\n" +
        "PLAYER_START 0 0 0 0;#5";

    private static readonly Level Level = Load(Map, "WALL screen { } WALL side { }");
    private static readonly ViewTest View = new(Level);

    [Theory]
    [InlineData(40, 10, true)] // in the cone, clear of the wall's end
    [InlineData(40, -10, false)] // behind the free-standing wall
    [InlineData(-10, 0, false)] // behind the player
    [InlineData(40, 30, false)] // outside the ±0.5 cone
    public void IsSpriteVisible_ConeAndFreeStandingWall_DecideIt(double x, double y, bool expected)
    {
        // OV-20261020-view-activity, view-cone and view-occlude.
        Assert.Equal(expected, View.IsSpriteVisible(Viewer(0, 0, 0), x, y, 1, 0, 2));
    }

    [Theory]
    [InlineData(16.395, false)] // its far edge is deeper, but a billboard is drawn at its centre's depth
    [InlineData(17.145, true)]
    public void IsSpriteVisible_BelowTheScreensBottom_TestsTheCentresDepth(double x, bool expected)
    {
        // OV-20261029-actor-events round 3, way-start: a 2-high sprite 6 to the side of an eye at 8.
        Assert.Equal(expected, View.IsSpriteVisible(Viewer(0, 0, 0), x, -6, 1, 0, 2));
    }

    [Fact]
    public void IsSpriteVisible_HalfBehindTheWallsEnd_IsVisible()
    {
        // OV-20261020-view-activity, occlude-edge: visible once any part clears the wall's end.
        Assert.True(View.IsSpriteVisible(Viewer(0, 0, 0), 40, -0.5, 1, 0, 2));
    }

    [Theory]
    [InlineData(0.49, true)]
    [InlineData(0.52, true)] // the centre is 0.02 outside the cone, the near edge inside
    [InlineData(0.53, false)]
    public void IsSpriteVisible_AtTheConesEdge_CountsTheSpritesWidth(double angle, bool expected)
    {
        // OV-20261020-view-activity, cone-edge: a 2-wide actor 40 ahead went out of view between
        // PLAYER_ANGLE 0.517 and 0.521; its centre leaves the cone at 0.5, its last column at 0.525.
        Assert.Equal(expected, View.IsSpriteVisible(Viewer(0, 30, angle), 40, 30, 1, 0, 2));
    }

    [Fact]
    public void IsSpriteVisible_BehindAPillarRegion_IsHidden()
    {
        // OV-20261020-view-activity, view-pillar: a region whose floor meets its ceiling hides what is behind it.
        Viewer viewer = Viewer(0, 0, Math.PI / 4);

        Assert.False(View.IsSpriteVisible(viewer, 20, 20, 1, 0, 2));
        Assert.True(View.IsSpriteVisible(viewer, 14, 24, 1, 0, 2));
    }

    [Fact]
    public void IsSpriteVisible_BelowTheScreen_IsHiddenUntilTheTiltShowsIt()
    {
        // OV-20261020-view-activity, near-low and tilt-far: 6 ahead, 2 high, the eye at 8.
        Assert.False(View.IsSpriteVisible(Viewer(0, 0, 0), 6, 0, 1, 0, 2));
        Assert.True(View.IsSpriteVisible(Viewer(0, 0, 0, tilt: 1), 6, 0, 1, 0, 2));
        Assert.False(View.IsSpriteVisible(Viewer(0, -30, 0, tilt: -0.8), 30, -30, 1, 0, 2));
    }

    [Theory]
    [InlineData(0, -10, 0, true)] // the wall in front of the viewer
    [InlineData(0, -10, Math.PI, false)] // facing away
    [InlineData(36, -10, Math.PI, true)] // from behind: walls have two faces
    public void IsWallVisible_ConeAndBackFace(double x, double y, double angle, bool expected)
    {
        Wall screen = Level.Walls[0];

        Assert.Equal(expected, View.IsWallVisible(Viewer(x, y, angle), screen));
    }

    [Fact]
    public void IsWallVisible_BehindAnotherWall_IsHidden()
    {
        // OV-20261020-view-activity, berkeley-near O1: the pillar's far side is hidden by its near side.
        Wall farSide = Level.Walls[3]; // (14, 14) -> (10, 14)

        Assert.False(View.IsWallVisible(Viewer(12, 0, Math.PI / 2), farSide));
        Assert.True(View.IsWallVisible(Viewer(12, 20, -Math.PI / 2), farSide));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("FLAGS TRANSPARENT;", true)] // with a solid texture as with one all in colour 0
    [InlineData("FLAGS INVISIBLE;", true)]
    public void IsSpriteVisible_BehindATransparentOrInvisibleWall_IsVisible(string flags, bool expected)
    {
        // OV-20261020-view-activity round 6, occlude-kinds: still actors 40 ahead behind free-standing
        // walls at x = 30.
        const string map =
            "VERTEX 30 -6 0;#0\nVERTEX 30 6 0;#1\nREGION room 0 16;#0\nWALL pane 0 1 0 0 0 0;#0\nPLAYER_START 0 0 0 0;#1";
        var view = new ViewTest(Load(map, $"WALL pane {{ {flags} }}"));

        Assert.Equal(expected, view.IsSpriteVisible(Viewer(0, 0, 0), 40, 0, 1, 0, 2));
    }

    [Fact]
    public void Update_AWallWhoseVerticesMoved_HidesFromItsNewPlace()
    {
        // A free-standing wall from (20, 10) to (20, 20) moved down to (20, -5) .. (20, 5) now hides
        // a sprite 40 ahead.
        const string map =
            "VERTEX 20 10 0;#0\nVERTEX 20 20 0;#1\nREGION room 0 16;#0\nWALL door 0 1 0 0 0 0;#0\nPLAYER_START 0 0 0 0;#1";
        Level level = Load(map, "WALL door { }");
        var view = new ViewTest(level);
        Assert.True(view.IsSpriteVisible(Viewer(0, 0, 0), 40, 0, 1, 0, 2));

        var vertices = (IList<Vertex>)level.Vertices;
        vertices[0] = new Vertex(20, -5, 0);
        vertices[1] = new Vertex(20, 5, 0);

        Assert.True(view.Update(level));
        Assert.False(view.IsSpriteVisible(Viewer(0, 0, 0), 40, 0, 1, 0, 2));
    }

    // OV-20261029-below-view: a room 0..16 with a deck on x −12..−4, y 12..20: the deck's floor at 10,
    // an underpass (BELOW) from 0 to 8 under it.
    private const string StackMap =
        "VERTEX -16 -24 0;#0\nVERTEX 48 -24 0;#1\nVERTEX 48 24 0;#2\nVERTEX -16 24 0;#3\n" +
        "VERTEX -12 12 0;#4\nVERTEX -4 12 0;#5\nVERTEX -4 20 0;#6\nVERTEX -12 20 0;#7\n" +
        "REGION room 0 16;#0\nREGION solid 16 16;#1\nREGION pad 10 16;#2\n" +
        "WALL w 0 1 0 1 0 0;#0\nWALL w 1 2 0 1 0 0;#1\nWALL w 2 3 0 1 0 0;#2\nWALL w 3 0 0 1 0 0;#3\n" +
        "WALL b 4 5 2 0 0 0;#4\nWALL b 5 6 2 0 0 0;#5\nWALL b 6 7 2 0 0 0;#6\nWALL b 7 4 2 0 0 0;#7\n" +
        "PLAYER_START 0 0 0 0;#8";

    [Theory]
    [InlineData(4, true)] // the eye in the underpass's height range sees into it
    [InlineData(15, false)] // above it: only the deck's storey, though a line passes under the slab
    public void IsSpriteVisible_IntoAStackFromOutside_SeesTheEyesStoreyOnly(double eye, bool expected)
    {
        Level level = Load(StackMap, "WALL w { } WALL b { FLAGS PASSABLE; } REGION low { FLOOR_HGT 0; CEIL_HGT 8; } REGION pad { BELOW low; }");
        int low = level.Regions.ToList().FindIndex(static r => r.Name == "low");
        var view = new ViewTest(level);

        // A 16-high, 16-wide sprite on the underpass floor shows only up to the underpass's ceiling.
        Assert.Equal(expected, view.IsSpriteVisible(new Viewer(-8, 2, eye, Math.PI / 2, ProjectionModel.Create(1, 1, 0, 640, 480)), -8, 17, 8, 0, 16, low));
    }

    private static Viewer Viewer(double x, double y, double angle, double tilt = 0) =>
        new(x, y, 8, angle, ProjectionModel.Create(1, 1, tilt, 640, 480));

    private static Level Load(string map, string declarations)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        return LevelLoader.LoadCore("x", "MAPFILE <m.wmp>; " + declarations, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }
}
