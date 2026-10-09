// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

/// <summary>
/// The map → render mapping of ADR-0009: render z = −map y, camera yaw = PLAYER_ANGLE + π/2.
/// The handedness it encodes is the one read off the original's asymmetric far wall in
/// OV-20261009-tilt-model-shear-vs-pitch: facing map +x, map +y is on the screen left.
/// </summary>
public sealed class RenderSpaceTests
{
    [Fact]
    public void FromMap_NegatesMapYIntoRenderDepth_AndToMapRoundTrips()
    {
        var map = new Vector3(3, 8, 5); // (map x, height, map y)

        Vector3 render = RenderSpace.FromMap(map);

        Assert.Equal(new Vector3(3, 8, -5), render);
        Assert.Equal(render, RenderSpace.FromMap(3, 8, 5));
        Assert.Equal(map, RenderSpace.ToMap(render));
        Assert.Equal(-10, RenderSpace.ZFromMapY(10));
        Assert.Equal(10, RenderSpace.MapYFromZ(-10));
    }

    [Theory]
    [InlineData(0.0, 1.0, 0.0, 0.0)]                 // PLAYER_ANGLE 0 = east: render +x = map +x
    [InlineData(Math.PI * 0.5, 0.0, 0.0, -1.0)]      // north = map +y = render −z
    [InlineData(Math.PI, -1.0, 0.0, 0.0)]
    [InlineData(Math.PI * 1.5, 0.0, 0.0, 1.0)]
    public void ForwardFromPlayerAngle_FollowsTheMapHeading(double angle, float x, float y, float z)
    {
        Vector3 forward = RenderSpace.ForwardFromPlayerAngle(angle);

        Assert.Equal(x, forward.X, 1e-6);
        Assert.Equal(y, forward.Y, 1e-6);
        Assert.Equal(z, forward.Z, 1e-6);
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(2.0)]
    [InlineData(4.5)]
    public void ForwardFromPlayerAngle_InMapTerms_IsCosSin(double angle)
    {
        // The runtime moves along (cos a, sin a) in map space; the render forward must be the same heading.
        Vector3 map = RenderSpace.ToMap(RenderSpace.ForwardFromPlayerAngle(angle));

        Assert.Equal(Math.Cos(angle), map.X, 1e-6);
        Assert.Equal(Math.Sin(angle), map.Z, 1e-6);
    }

    [Fact]
    public void YawFromPlayerAngle_IsAQuarterTurnAhead_AndRoundTrips()
    {
        Assert.Equal(Math.PI * 0.5, RenderSpace.YawFromPlayerAngle(0), 1e-12);
        Assert.Equal(Math.PI, RenderSpace.YawFromPlayerAngle(Math.PI * 0.5), 1e-12);

        foreach (double angle in new[] { 0, 0.7, Math.PI, 5.9 })
        {
            Assert.Equal(angle, RenderSpace.PlayerAngleFromYaw(RenderSpace.YawFromPlayerAngle(angle)), 1e-9);
        }

        // Normalized into [0, 2π): a yaw of 0 faces PLAYER_ANGLE 3π/2 (south), not −π/2.
        Assert.Equal(Math.PI * 1.5, RenderSpace.PlayerAngleFromYaw(0), 1e-12);
    }

    [Fact]
    public void ScreenRight_FacingMapPlusX_IsMapMinusY()
    {
        // OV-20261009-tilt-model-shear-vs-pitch: facing +x, map +y is on the LEFT, so screen
        // right is map −y, i.e. render +z.
        Vector3 right = RenderSpace.ScreenRight(RenderSpace.ForwardFromPlayerAngle(0));

        Assert.Equal(0, right.X, 1e-6);
        Assert.Equal(0, right.Y, 1e-6);
        Assert.Equal(1, right.Z, 1e-6);
        Assert.Equal(-1, RenderSpace.ToMap(right).Z, 1e-6);
    }

    [Fact]
    public void ScreenRight_IsTheLookAtViewsXAxis()
    {
        // Matrix4x4.CreateLookAt is right-handed: its x-axis is cross(forward, up).
        Vector3 forward = RenderSpace.ForwardFromPlayerAngle(0.8);
        Vector3 right = RenderSpace.ScreenRight(forward);
        Matrix4x4 view = Matrix4x4.CreateLookAt(Vector3.Zero, forward, Vector3.UnitY);

        Vector3 viewSpace = Vector3.Transform(right, view);

        Assert.Equal(1, viewSpace.X, 1e-6);
        Assert.Equal(0, viewSpace.Y, 1e-6);
        Assert.Equal(0, viewSpace.Z, 1e-6);
    }
}
