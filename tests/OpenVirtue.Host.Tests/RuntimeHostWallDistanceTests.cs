// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text;
using OpenVirtue.Formats.Wrs;
using OpenVirtue.Host;
using OpenVirtue.Host.Tests.TestSupport;

namespace OpenVirtue.Host.Tests;

/// <summary>
/// A wall's <c>DISTANCE</c> after a beam, as measured in OV-20261019-wall-distance: the value is
/// computed once per cycle before the actions, so a wall's <c>EACH_TICK</c> in the cycle of a beam
/// still reads the distance from before it, and the next cycle reads the new one.
/// </summary>
public sealed class RuntimeHostWallDistanceTests : IDisposable
{
    private const string Declarations = """
        VIDEO S640x480;
        MAPFILE <m.wmp>;
        BMAP m,<t.pcx>,0,0,1,1;
        TEXTURE wallTex { BMAPS m; }
        WALL w { TEXTURE wallTex; }
        WALL d { TEXTURE wallTex; EACH_TICK d_tick; FLAGS PASSABLE; }
        REGION room { }
        SKILL beamed { VAL 0; }
        SKILL same_cycle { VAL -1; }
        SKILL next_cycle { VAL -1; }
        ACTION d_tick
        {
            IF (beamed == 2) { RULE next_cycle = my.DISTANCE; RULE beamed = 3; }
            IF (beamed == 1) { RULE same_cycle = my.DISTANCE; RULE beamed = 2; }
        }
        IF_START start;

        """;

    // A room with a free-standing wall from (20, 10) to (20, 20), the room on both sides.
    private const string Map = """
        VERTEX 0 0 0;#0
        VERTEX 40 0 0;#1
        VERTEX 40 40 0;#2
        VERTEX 0 40 0;#3
        VERTEX 20 10 0;#4
        VERTEX 20 20 0;#5
        REGION room 0 10;#0
        WALL w 0 1 0 -1 0 0;#0
        WALL w 1 2 0 -1 0 0;#1
        WALL w 2 3 0 -1 0 0;#2
        WALL w 3 0 0 -1 0 0;#3
        WALL d 4 5 0 0 0 0;#4
        PLAYER_START 5 5 0 0;#5
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ov-host-wall-distance-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Frame_BeamInAnAction_ShowsInWallDistanceFromTheNextCycle()
    {
        using RuntimeHost host = Open("ACTION start { WAIT 1; SET PLAYER_X, 30; SET PLAYER_Y, 5; RULE beamed = 1; }");
        host.Boot();
        for (int i = 0; i < 4; i++)
        {
            host.Frame(present: false);
        }

        Assert.Equal(15 + 5, host.Runtime.GetSkill("same_cycle"), 3);
        Assert.Equal(10 + 5, host.Runtime.GetSkill("next_cycle"), 3);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private RuntimeHost Open(string start)
    {
        Directory.CreateDirectory(_directory);
        string archive = Path.Combine(_directory, "TITLE.WRS");
        WrsArchive.WriteFile(
            archive,
            [
                new WrsFile("TITLE.WDL", Encoding.ASCII.GetBytes(Declarations + start)),
                new WrsFile("m.wmp", Encoding.ASCII.GetBytes(Map)),
            ]);
        return RuntimeHost.OpenArchive(archive, "TITLE.WDL", new FakeWindow(), new FakeInput(), new NullAudioSink(), new FakeRender());
    }
}
