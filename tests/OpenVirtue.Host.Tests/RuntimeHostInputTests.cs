// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Host;
using OpenVirtue.Host.Tests.TestSupport;

namespace OpenVirtue.Host.Tests;

public sealed class RuntimeHostInputTests
{
    [Fact]
    public void Frame_RightwardMouse_SetsForceRotNegative()
    {
        using RuntimeHost host = Open(out FakeInput input, out _);
        input.Motion = new Vector2(32, 0);
        host.Frame(present: false);

        Assert.Equal(-1, host.Runtime.GetSkill("FORCE_ROT"), 6);
    }

    [Fact]
    public void Frame_WHeld_SetsForceAheadToKeySense()
    {
        using RuntimeHost host = Open(out FakeInput input, out _);
        input.Down.Add(HostKey.W);
        host.Frame(present: false);

        Assert.Equal(0.7, host.Runtime.GetSkill("FORCE_AHEAD"), 6);
    }

    private static RuntimeHost Open(out FakeInput input, out FakeWindow window)
    {
        window = new FakeWindow();
        input = new FakeInput();
        var textures = new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase)
        {
            ["wallTex"] = new(1, 1, [255, 255, 255, 255]),
        };
        return RuntimeHost.OpenLevel(TinyLevel(), textures, window, input, new NullAudioSink(), new FakeRender());
    }

    private static Level TinyLevel()
    {
        const string map = """
            VERTEX 0 0 0;#0
            VERTEX 20 0 0;#1
            VERTEX 20 20 0;#2
            VERTEX 0 20 0;#3
            REGION room 0 10;#0
            WALL w 0 1 0 -1 0 0;#0
            WALL w 1 2 0 -1 0 0;#1
            WALL w 2 3 0 -1 0 0;#2
            WALL w 3 0 0 -1 0 0;#3
            PLAYER_START 5 5 0 0;#4
            """;
        const string main = """
            VIDEO S640x480;
            MAPFILE <m.wmp>;
            BMAP m,<t.pcx>,0,0,1,1;
            TEXTURE wallTex { BMAPS m; }
            WALL w { TEXTURE wallTex; }
            """;
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        return LevelLoader.LoadCore("tiny", main, name => resources.GetValueOrDefault(Path.GetFileName(name)));
    }
}
