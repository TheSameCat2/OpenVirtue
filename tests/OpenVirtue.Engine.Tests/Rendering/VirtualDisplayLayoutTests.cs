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

        Assert.Equal(1.5f, layout.Scale);
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
}
