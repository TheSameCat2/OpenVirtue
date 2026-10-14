// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

public sealed class VirtualDisplayLayoutTests
{
    [Fact]
    public void Create_WideClient_CentersFourByThreeVirtualDisplay()
    {
        VirtualDisplayLayout layout = VirtualDisplayLayout.Create(1280, 720, 640, 480);

        Assert.Equal(1.5f, layout.ScaleX);
        Assert.Equal(1.5f, layout.ScaleY);
        Assert.Equal(new DisplayRectangle(160, 0, 960, 720), layout.PhysicalDisplay);
        Assert.Equal(Vector2.Zero, layout.ToVirtual(160, 0));
        Assert.Equal(new Vector2(640, 480), layout.ToVirtual(1120, 720));
    }

    [Fact]
    public void ToPhysical_MapsRuntimeScreenWindowInsideLetterbox()
    {
        VirtualDisplayLayout layout = VirtualDisplayLayout.Create(1280, 720, 640, 480);

        DisplayRectangle viewport = layout.ToPhysical(32, 40, 576, 400);

        Assert.Equal(new DisplayRectangle(208, 60, 864, 600), viewport);
    }

    [Fact]
    public void Create_320x200WithFourByThreeDisplay_StretchesRowsByOnePointTwo()
    {
        // OV-20261009-pixel-aspect-video-modes: 320x200 is presented on a 4:3 display, 1.2:1 pixels.
        VirtualDisplayLayout layout = VirtualDisplayLayout.Create(1280, 960, 320, 200, 4.0 / 3.0);

        Assert.Equal(4f, layout.ScaleX, 1e-5f);
        Assert.Equal(4.8f, layout.ScaleY, 1e-5f);
        DisplayRectangle display = layout.PhysicalDisplay;
        Assert.Equal(0, display.X, 1e-3f);
        Assert.Equal(0, display.Y, 1e-3f);
        Assert.Equal(1280, display.Width, 1e-3f);
        Assert.Equal(960, display.Height, 1e-3f);
        Vector2 corner = layout.ToVirtual(1280, 960);
        Assert.Equal(320, corner.X, 1e-3f);
        Assert.Equal(200, corner.Y, 1e-3f);
    }

    [Fact]
    public void Create_X320x400WithFourByThreeDisplay_SquashesRowsToZeroPointSix()
    {
        VirtualDisplayLayout layout = VirtualDisplayLayout.Create(1280, 960, 320, 400, 4.0 / 3.0);

        Assert.Equal(4f, layout.ScaleX, 1e-5f);
        Assert.Equal(2.4f, layout.ScaleY, 1e-5f);
    }

    [Fact]
    public void Create_TallClientWithFourByThreeDisplay_LetterboxesToDisplayAspect()
    {
        VirtualDisplayLayout layout = VirtualDisplayLayout.Create(800, 1000, 320, 200, 4.0 / 3.0);

        DisplayRectangle display = layout.PhysicalDisplay;
        Assert.Equal(0, display.X, 1e-3f);
        Assert.Equal(200, display.Y, 1e-3f);
        Assert.Equal(800, display.Width, 1e-3f);
        Assert.Equal(600, display.Height, 1e-3f);
        Assert.Equal(2.5f, layout.ScaleX, 1e-5f);
        Assert.Equal(3f, layout.ScaleY, 1e-5f);
    }

    [Theory]
    [InlineData("320x200", 320, 200, 1.2)]
    [InlineData("X320x240", 320, 240, 1.0)]
    [InlineData("X320x400", 320, 400, 0.6)]
    [InlineData("S640x480", 640, 480, 1.0)]
    public void LevelVideoMode_DocumentedModes_HaveFourByThreeDisplayPixelAspect(string name, int width, int height, double pixelAspect)
    {
        var mode = new LevelVideoMode(name, width, height);

        Assert.Equal(4.0 / 3.0, LevelVideoMode.DisplayAspect, 1e-12);
        Assert.Equal(pixelAspect, mode.PixelAspect, 1e-9);
    }

    [Fact]
    public void Create_SquarePixelMode_IsUnchangedByDisplayAspect()
    {
        VirtualDisplayLayout plain = VirtualDisplayLayout.Create(1280, 720, 640, 480);
        VirtualDisplayLayout fourByThree = VirtualDisplayLayout.Create(1280, 720, 640, 480, 4.0 / 3.0);

        Assert.Equal(plain, fourByThree);
    }
}
