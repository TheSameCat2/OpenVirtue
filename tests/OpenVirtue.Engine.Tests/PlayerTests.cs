// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;

namespace OpenVirtue.Engine.Tests;

public class PlayerTests
{
    // Region 0 (a) borders region 1 (b, a portal) along y=0, and region 2 (solid, no headroom)
    // along x=0. Player spawns in region 0 at (5, -5).
    private const string Map =
        "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nVERTEX 0 -20 0;#2\n" +
        "REGION a 0 20;#0\nREGION b 0 20;#1\nREGION solid 0 0;#2\n" +
        "WALL portal 0 1 0 1 0 0;#0\nWALL solidw 0 2 0 2 0 0;#1\n" +
        "PLAYER_START 5 -5 0 0;#3";

    [Fact]
    public void MoveHorizontal_ThroughPortal_ChangesRegion()
    {
        var player = new Player(Load());
        Assert.Equal(0, player.Region);

        player.MoveHorizontal(0, 10); // cross y=0 into region b

        Assert.Equal(1, player.Region);
        Assert.Equal(5f, player.Position.Z);
    }

    [Fact]
    public void MoveHorizontal_IntoSolidWall_IsBlocked()
    {
        var player = new Player(Load());

        player.MoveHorizontal(-10, 0); // toward the solid region across x=0

        Assert.Equal(0, player.Region);   // did not cross
        Assert.Equal(5f, player.Position.X); // position unchanged
    }

    [Fact]
    public void Tick_AppliesGravityAndLandsOnFloor()
    {
        var player = new Player(Load());
        float groundY = player.Position.Y; // spawned on the floor (eye height)
        Vector3 position = player.Position;
        position.Y = groundY + 1f;         // nudge up
        player.Position = position;

        player.Tick();

        Assert.Equal(groundY, player.Position.Y); // fell back and landed
        Assert.True(player.Grounded);
    }

    [Fact]
    public void Jump_RaisesThePlayer()
    {
        var player = new Player(Load());
        player.Tick(); // ensure grounded
        Assert.True(player.Grounded);

        player.Jump();
        player.Tick();

        Assert.False(player.Grounded);          // airborne after jumping
        Assert.True(player.VelocityY > 0 || player.Position.Y > 0);
    }

    private static Level Load()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = Map };
        return LevelLoader.LoadCore("x", "MAPFILE <m.wmp>;", n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }
}
