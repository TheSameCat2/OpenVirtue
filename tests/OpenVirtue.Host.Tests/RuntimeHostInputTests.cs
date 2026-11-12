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

        // OV-20261112-input-forces: −16/1024 a mickey at MOUSE_SENSE 1.
        Assert.Equal(-0.5, host.Runtime.GetSkill("FORCE_ROT"), 6);
    }

    [Fact]
    public void Frame_MouseSteersThePlayer_CapturesThePointer()
    {
        // A turn must not stop at the window's edge: the hidden pointer is captured while
        // MOUSE_MODE is 0, as the original's mouse driver reported mickeys wherever it was.
        using RuntimeHost host = Open(out _, out FakeWindow window);
        host.Frame(present: true);

        Assert.True(window.PointerCaptured);
    }

    [Fact]
    public void Frame_PointerCaptured_TakesMickeysAsTheDeviceMovedNotScaledToTheWindow()
    {
        using RuntimeHost host = Open(out FakeInput input, out FakeWindow window);
        host.Frame(present: true);
        Assert.True(window.PointerCaptured);

        input.Motion = new Vector2(5, -3);
        host.Frame(present: true);

        Assert.Equal(5, host.Runtime.GetSkill("MICKEY_X"), 6);
        Assert.Equal(-3, host.Runtime.GetSkill("MICKEY_Y"), 6);
    }

    [Fact]
    public void Frame_PointerShown_ReleasesThePointer()
    {
        using RuntimeHost host = Open(out _, out FakeWindow window);
        host.Frame(present: true);
        host.Runtime.SetSkill("MOUSE_MODE", 1);
        host.Frame(present: true);

        Assert.False(window.PointerCaptured);
    }

    [Fact]
    public void Frame_WindowLosesFocus_ReleasesThePointerAndWaitsForItToComeBackInside()
    {
        using RuntimeHost host = Open(out _, out FakeWindow window);
        host.Frame(present: true);
        window.Focused = false;
        host.Frame(present: true);
        Assert.False(window.PointerCaptured);

        window.Focused = true;
        window.PointerInside = false;
        host.Frame(present: true);
        Assert.False(window.PointerCaptured);

        window.PointerInside = true;
        host.Frame(present: true);
        Assert.True(window.PointerCaptured);
    }

    [Fact]
    public void Frame_CursorUpHeld_RampsForceAheadAQuarterACycleToKeySense()
    {
        // OV-20261112-input-forces: 256, 512, 768, then 1024 (KEY_SENSE starts at 1 on V3.963).
        using RuntimeHost host = Open(out FakeInput input, out _);
        input.Down.Add(HostKey.Up);
        var readings = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            host.Frame(present: false);
            readings.Add(host.Runtime.GetSkill("FORCE_AHEAD"));
        }

        Assert.Equal(new[] { 0.25, 0.5, 0.75, 1, 1 }, readings);
        input.Down.Remove(HostKey.Up);
        host.Frame(present: false);
        Assert.Equal(0, host.Runtime.GetSkill("FORCE_AHEAD"));
    }

    [Fact]
    public void Frame_KeyAndMouseOnOneAxis_TheKeyWins()
    {
        using RuntimeHost host = Open(out FakeInput input, out _);
        input.Down.Add(HostKey.Left);
        input.Motion = new Vector2(32, 0);
        host.Frame(present: false);

        Assert.Equal(204.0 / 1024, host.Runtime.GetSkill("FORCE_ROT"), 6);
    }

    [Fact]
    public void Frame_CommaHeld_StrafesLeftAsAPositiveForce()
    {
        using RuntimeHost host = Open(out FakeInput input, out _);
        input.Down.Add(HostKey.Comma);
        host.Frame(present: false);

        Assert.Equal(0.25, host.Runtime.GetSkill("FORCE_STRAFE"), 6);
    }

    [Fact]
    public void Frame_AltWithCursorRight_StrafesRightAndLeavesForceRot()
    {
        using RuntimeHost host = Open(out FakeInput input, out _);
        host.Runtime.SetSkill("FORCE_ROT", 0.375);
        input.Down.Add(HostKey.LeftAlt);
        input.Down.Add(HostKey.Right);
        host.Frame(present: false);

        Assert.Equal(-204.0 / 1024, host.Runtime.GetSkill("FORCE_STRAFE"), 6);
        Assert.Equal(0.375, host.Runtime.GetSkill("FORCE_ROT"), 6);
    }

    [Theory]
    [InlineData(HostKey.W)]
    [InlineData(HostKey.S)]
    [InlineData(HostKey.A)]
    [InlineData(HostKey.D)]
    [InlineData(HostKey.Space)]
    public void Frame_KeyTheManualGivesNoMovement_LeavesTheForcesAtZero(HostKey key)
    {
        // Saints of Virtue binds S, D and Space to its own actions; only the manual's keys move the player.
        using RuntimeHost host = Open(out FakeInput input, out _);
        input.Down.Add(key);
        host.Frame(present: false);

        foreach (string force in new[] { "FORCE_AHEAD", "FORCE_STRAFE", "FORCE_ROT", "FORCE_UP" })
        {
            Assert.Equal(0, host.Runtime.GetSkill(force), 6);
        }
    }

    [Fact]
    public void Frame_ForwardMouse_WalksAhead()
    {
        // FORCE_AHEAD is "entered by mouse, joystick or keyboard" (manual p.119); a forward mickey is negative Y.
        using RuntimeHost host = Open(out FakeInput input, out _);
        input.Motion = new Vector2(0, -32);
        host.Frame(present: false);

        Assert.Equal(0.5, host.Runtime.GetSkill("FORCE_AHEAD"), 6);
        Assert.Equal(0, host.Runtime.GetSkill("FORCE_TILT"), 6);
    }

    [Fact]
    public void Frame_ForwardMouseWithMouseLook_TiltsUp()
    {
        using RuntimeHost host = Open(out FakeInput input, out _);
        host.MouseLook = true;
        input.Motion = new Vector2(0, -32);
        host.Frame(present: false);

        Assert.Equal(0, host.Runtime.GetSkill("FORCE_AHEAD"), 6);
        Assert.Equal(-0.5, host.Runtime.GetSkill("FORCE_TILT"), 6);
    }

    [Fact]
    public void Frame_RightwardMouseWithAltHeld_StrafesRight()
    {
        // FORCE_STRAFE: "[,] and [.] keys or by pressing [Alt] and moving the mouse ... sidewards" (p.120).
        using RuntimeHost host = Open(out FakeInput input, out _);
        input.Down.Add(HostKey.LeftAlt);
        input.Motion = new Vector2(32, 0);
        host.Frame(present: false);

        Assert.Equal(-0.5, host.Runtime.GetSkill("FORCE_STRAFE"), 6); // to the right
        Assert.Equal(0, host.Runtime.GetSkill("FORCE_ROT"), 6);
    }

    [Fact]
    public void Frame_CursorUpWithShift_RampsToShiftSense()
    {
        // Shift held first: 512, 1536, 2048 (SHIFT_SENSE 2).
        using RuntimeHost host = Open(out FakeInput input, out _);
        input.Down.Add(HostKey.Up);
        input.Down.Add(HostKey.LeftShift);
        var readings = new List<double>();
        for (int i = 0; i < 3; i++)
        {
            host.Frame(present: false);
            readings.Add(host.Runtime.GetSkill("FORCE_AHEAD"));
        }

        Assert.Equal(new[] { 0.5, 1.5, 2 }, readings);
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
