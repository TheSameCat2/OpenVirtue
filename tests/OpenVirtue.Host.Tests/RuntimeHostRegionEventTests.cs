// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text;
using OpenVirtue.Formats.Wrs;
using OpenVirtue.Host;
using OpenVirtue.Host.Tests.TestSupport;

namespace OpenVirtue.Host.Tests;

/// <summary>
/// Region events around a beam, as measured in OV-20261019-proximity-events: writing
/// <c>PLAYER_X/PLAYER_Y</c> moves the player but keeps the region, so <c>IF_ENTER</c> does not
/// fire; a later <c>LOCATE</c> finds the new region and fires <c>IF_LEAVE</c> and <c>IF_ENTER</c>.
/// </summary>
public sealed class RuntimeHostRegionEventTests : IDisposable
{
    private const string Declarations = """
        VIDEO S640x480;
        MAPFILE <m.wmp>;
        BMAP m,<t.pcx>,0,0,1,1;
        TEXTURE wallTex { BMAPS m; }
        WALL w { TEXTURE wallTex; }
        WALL p { TEXTURE wallTex; FLAGS INVISIBLE, PASSABLE; }
        SKILL entered { VAL 0; }
        SKILL left { VAL 0; }
        REGION room { IF_LEAVE room_leave; }
        REGION pad { IF_ENTER pad_enter; }
        ACTION room_leave { RULE left += 1; }
        ACTION pad_enter { RULE entered += 1; }
        IF_START start;

        """;

    // A room (0) with a pad island (1) at x 10…20, y 10…20; each region is left of its walls.
    private const string Map = """
        VERTEX 0 0 0;#0
        VERTEX 40 0 0;#1
        VERTEX 40 40 0;#2
        VERTEX 0 40 0;#3
        VERTEX 10 10 0;#4
        VERTEX 20 10 0;#5
        VERTEX 20 20 0;#6
        VERTEX 10 20 0;#7
        REGION room 0 10;#0
        REGION pad 0 10;#1
        WALL w 0 1 0 -1 0 0;#0
        WALL w 1 2 0 -1 0 0;#1
        WALL w 2 3 0 -1 0 0;#2
        WALL w 3 0 0 -1 0 0;#3
        WALL p 4 5 1 0 0 0;#4
        WALL p 5 6 1 0 0 0;#5
        WALL p 6 7 1 0 0 0;#6
        WALL p 7 4 1 0 0 0;#7
        PLAYER_START 5 5 0 0;#8
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ov-host-region-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Frame_SetPlayerPositionIntoAnotherRegion_KeepsTheRegionAndFiresNoEvents()
    {
        using RuntimeHost host = Open("ACTION start { WAIT 1; SET PLAYER_X, 15; SET PLAYER_Y, 15; }");
        host.Boot();
        for (int i = 0; i < 4; i++)
        {
            host.Frame(present: false);
        }

        Assert.Equal(15, host.Runtime.GetSkill("PLAYER_X"), 3);
        Assert.Equal(15, host.Runtime.GetSkill("PLAYER_Y"), 3);
        Assert.Equal(0, host.Runtime.GetSkill("PLAYER_REGION"));
        Assert.Equal(0, host.Runtime.GetSkill("entered"));
        Assert.Equal(0, host.Runtime.GetSkill("left"));
    }

    [Fact]
    public void Frame_LocateAfterBeam_FiresLeaveAndEnterOnce()
    {
        using RuntimeHost host = Open("ACTION start { WAIT 1; SET PLAYER_X, 15; SET PLAYER_Y, 15; WAIT 2; LOCATE; }");
        host.Boot();
        for (int i = 0; i < 3; i++)
        {
            host.Frame(present: false);
        }

        Assert.Equal(0, host.Runtime.GetSkill("entered"));
        for (int i = 0; i < 3; i++)
        {
            host.Frame(present: false);
        }

        Assert.Equal(1, host.Runtime.GetSkill("PLAYER_REGION"));
        Assert.Equal(1, host.Runtime.GetSkill("entered"));
        Assert.Equal(1, host.Runtime.GetSkill("left"));
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
