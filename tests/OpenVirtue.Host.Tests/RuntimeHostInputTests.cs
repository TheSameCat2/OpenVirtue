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

        Assert.Equal(1, host.Runtime.GetSkill("FORCE_AHEAD"), 6); // KEY_SENSE starts at 1 on V3.963
    }

    [Fact]
    public void Frame_SpacePressed_RunsTheKeysOwnHookThenIfAnykeyInTheNextCycle()
    {
        // OV-20261029-scheduler-order round 8 (hook-both): IF_SPACE before IF_ANYKEY, both in the
        // cycle after the press reaches the host, with KEY_SPACE and KEY_ANY reading 1 there.
        const string hooks = """
            SKILL order { VAL 0; }
            SKILL seen { VAL 0; }
            ACTION s { RULE order = order * 10 + 1; RULE seen = KEY_SPACE + KEY_ANY; }
            ACTION a { RULE order = order * 10 + 2; }
            ACTION arm { SET IF_SPACE, s; SET IF_ANYKEY, a; }
            IF_START arm;
            """;
        using RuntimeHost host = Open(out FakeInput input, out _, hooks);
        host.Boot();
        host.Frame(present: false);

        input.Down.Add(HostKey.Space);
        input.PressedKeys.Add(HostKey.Space);
        host.Frame(present: false);

        Assert.Equal(12, host.Runtime.GetSkill("order"));
        Assert.Equal(2, host.Runtime.GetSkill("seen"));
    }

    private static RuntimeHost Open(out FakeInput input, out FakeWindow window, string extraMain = "")
    {
        window = new FakeWindow();
        input = new FakeInput();
        var textures = new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase)
        {
            ["wallTex"] = new(1, 1, [255, 255, 255, 255]),
        };
        return RuntimeHost.OpenLevel(TinyLevel(extraMain), textures, window, input, new NullAudioSink(), new FakeRender());
    }

    private static Level TinyLevel(string extraMain = "")
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
        return LevelLoader.LoadCore("tiny", main + "\n" + extraMain, name => resources.GetValueOrDefault(Path.GetFileName(name)));
    }
}
