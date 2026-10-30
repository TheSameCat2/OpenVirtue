// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text;
using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Formats.Wrs;
using OpenVirtue.Host;
using OpenVirtue.Host.Tests.TestSupport;

namespace OpenVirtue.Host.Tests;

/// <summary>
/// <c>INKEY</c> runs alongside the world (OV-20261029-inkey): the global slots go on while the input
/// waits, typed keys edit the string in place, and Return resumes the waiting action.
/// </summary>
public sealed class RuntimeHostInkeyTests : IDisposable
{
    private const string Map = """
        VERTEX 0 0 0;#0
        VERTEX 40 0 0;#1
        VERTEX 40 40 0;#2
        VERTEX 0 40 0;#3
        REGION room 0 10;#0
        WALL w 0 1 0 -1 0 0;#0
        WALL w 1 2 0 -1 0 0;#1
        WALL w 2 3 0 -1 0 0;#2
        WALL w 3 0 0 -1 0 0;#3
        PLAYER_START 5 5 0 0;#4
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ov-host-inkey-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Frame_Inkey_EditsInPlaceWhileTheWorldRuns()
    {
        var window = new TypingWindow();
        using RuntimeHost host = Open(window, """
            STRING name,"xxxxxxxx";
            SKILL n { VAL 0; }
            SKILL done { VAL 0; }
            ACTION tick { RULE n = n + 1; }
            ACTION start { SET EACH_TICK.1, tick; INKEY name; RULE done = 1; }
            """);
        host.FrameClock = () => 1.0 / 16;
        host.Boot();
        host.Frame(present: false);
        host.Frame(present: false);
        double before = host.Runtime.GetSkill("n");

        window.Keys.Enqueue((InkeyKey.Character, 'a'));
        window.Keys.Enqueue((InkeyKey.Character, 'b'));
        host.Frame(present: false);
        host.Frame(present: false);
        Assert.True(host.Runtime.TryGetString("name", out string editing));
        Assert.Equal("abxxxxxx", editing);
        Assert.Equal(0, host.Runtime.GetSkill("done"));
        Assert.True(host.Runtime.GetSkill("n") > before); // the global slot ran during the input

        window.Keys.Enqueue((InkeyKey.Return, '\0'));
        host.Frame(present: false);
        host.Frame(present: false);

        Assert.Equal(1, host.Runtime.GetSkill("done"));
        Assert.Equal(0, host.Runtime.GetSkill("RESULT"));
        Assert.Equal(8, host.Runtime.GetSkill("STR_LEN"));
        Assert.False(window.TextInput);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private RuntimeHost Open(IPlatformWindow window, string body)
    {
        Directory.CreateDirectory(_directory);
        string archive = Path.Combine(_directory, "TITLE.WRS");
        string wdl = """
            VIDEO S640x480;
            MAPFILE <m.wmp>;
            BMAP m,<t.pcx>,0,0,1,1;
            TEXTURE wallTex { BMAPS m; }
            WALL w { TEXTURE wallTex; }
            REGION room { }
            IF_START start;

            """ + body;
        WrsArchive.WriteFile(
            archive,
            [
                new WrsFile("TITLE.WDL", Encoding.ASCII.GetBytes(wdl)),
                new WrsFile("m.wmp", Encoding.ASCII.GetBytes(Map)),
            ]);
        return RuntimeHost.OpenArchive(archive, "TITLE.WDL", window, new FakeInput(), new NullAudioSink(), new FakeRender());
    }

    private sealed class TypingWindow : IPlatformWindow
    {
        public Queue<(InkeyKey Key, char Character)> Keys { get; } = new();

        public bool TextInput { get; private set; }

        public int Width { get; } = 640;

        public int Height { get; } = 480;

        public string Title { get; set; } = "test";

        public bool IsOpen { get; } = true;

        public bool Focused { get; } = true;

        public bool PointerInside { get; } = true;

        public void Close()
        {
        }

        public void SetCursorVisible(bool visible)
        {
        }

        public string? Prompt(string label, string initialValue, int capacity) => throw new InvalidOperationException("INKEY must not block");

        public bool BeginTextInput() => TextInput = true;

        public void EndTextInput() => TextInput = false;

        public IReadOnlyList<(InkeyKey Key, char Character)> ConsumeTextKeys()
        {
            var keys = Keys.ToArray();
            Keys.Clear();
            return keys;
        }
    }
}
