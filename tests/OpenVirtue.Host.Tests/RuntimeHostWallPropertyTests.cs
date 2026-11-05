// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Formats.Wrs;
using OpenVirtue.Host;
using OpenVirtue.Host.Tests.TestSupport;

namespace OpenVirtue.Host.Tests;

/// <summary>
/// A wall's <c>ATTACH</c> and <c>POSITION</c> changed by a script while the level runs
/// (OV-20261105-wall-pieces): retail's <c>SET curtainWallIndic1.ATTACH, indicator2_tex;</c> has to reach
/// the mesh, which until then only a <c>SHAKE</c> rebuilt.
/// </summary>
public sealed class RuntimeHostWallPropertyTests : IDisposable
{
    /// <summary>
    /// The 16 px wall texture repeats every step, so its attach's 16 px square is one step tall; its
    /// POS_Y moves that square up and down the wall in <c>POS_Y / 16</c> steps.
    /// </summary>
    private static string Declarations(double attachPosY) => FormattableString.Invariant($$"""
        VIDEO S640x480;
        MAPFILE <m.wmp>;
        BMAP m,<t.pcx>,0,0,16,16;
        BMAP q,<q.pcx>,0,0,16,16;
        TEXTURE wallTex { BMAPS m; }
        TEXTURE square { BMAPS q; POS_Y {{attachPosY}}; }
        TEXTURE other { BMAPS q; }
        WALL wall { TEXTURE wallTex; }
        WALL w { TEXTURE wallTex; ATTACH square; FLAGS PORTCULLIS; POSITION 0; }
        REGION room { }
        IF_START start;

        """);

    // A 40-step room whose walls are "wall", and a free-standing wall "w" from (20, 10) to (20, 20).
    private const string Map = """
        VERTEX 0 0 0;#0
        VERTEX 40 0 0;#1
        VERTEX 40 40 0;#2
        VERTEX 0 40 0;#3
        VERTEX 20 10 0;#4
        VERTEX 20 20 0;#5
        REGION room 0 16;#0
        WALL wall 0 1 0 -1 0 0;#0
        WALL wall 1 2 0 -1 0 0;#1
        WALL wall 2 3 0 -1 0 0;#2
        WALL wall 3 0 0 -1 0 0;#3
        WALL w 4 5 0 0 0 0;#4
        PLAYER_START 5 5 0 0;#5
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ov-host-wall-property-" + Guid.NewGuid().ToString("N"));
    private readonly FakeRender _render = new();

    [Fact]
    public void Frame_SetWallAttach_DrawsTheWallWithTheNewTexture()
    {
        using RuntimeHost host = Open(0, "ACTION start { WAIT 5; SET w.ATTACH, other; }");
        host.Boot();
        for (int i = 0; i < 2; i++)
        {
            host.Frame(present: false);
        }

        Assert.Contains("square", DrawnTextures());
        Assert.DoesNotContain("other", DrawnTextures());

        for (int i = 0; i < 8; i++)
        {
            host.Frame(present: false);
        }

        Assert.Contains("other", DrawnTextures());
        Assert.DoesNotContain("square", DrawnTextures());
    }

    [Fact]
    public void Frame_SetWallTexture_DrawsTheWallWithTheNewTexture()
    {
        using RuntimeHost host = Open(0, "ACTION start { WAIT 5; SET w.TEXTURE, other; }");
        host.Boot();
        for (int i = 0; i < 2; i++)
        {
            host.Frame(present: false);
        }

        Assert.DoesNotContain("other", DrawnTextures());

        for (int i = 0; i < 8; i++)
        {
            host.Frame(present: false);
        }

        // The room's walls keep wallTex; the free wall "w" is drawn with the script's texture.
        Assert.Contains("wallTex", DrawnTextures());
        Assert.Contains("other", DrawnTextures());
    }

    [Fact]
    public void Frame_SetWallPosition_MovesTheAttachToThePiecesOtherEdge()
    {
        using RuntimeHost host = Open(144, "ACTION start { WAIT 5; SET w.POSITION, 1; }");
        host.Boot();
        for (int i = 0; i < 2; i++)
        {
            host.Frame(present: false);
        }

        // POSITION 0 measures the attach from height 0: with POS_Y 144 (nine steps) its 1-step quad
        // ends 8 steps under the floor, so nothing of it shows.
        Assert.DoesNotContain("square", DrawnTextures());

        for (int i = 0; i < 8; i++)
        {
            host.Frame(present: false);
        }

        // POSITION 1 measures from the ceiling (16): the same quad lands 7…8, inside the piece.
        Assert.Equal(7, AttachVertices().Min(static v => v.Y), 2);
        Assert.Equal(8, AttachVertices().Max(static v => v.Y), 2);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private List<string?> DrawnTextures() =>
        [.. _render.LastFrame!.Batches.Select(static batch => batch.Texture)];

    private List<RenderVertex> AttachVertices() =>
        [.. _render.LastFrame!.Batches.Where(static batch => batch.Texture == "square").SelectMany(static batch => batch.Vertices)];

    private RuntimeHost Open(double attachPosY, string start)
    {
        Directory.CreateDirectory(_directory);
        string archive = Path.Combine(_directory, "TITLE.WRS");
        WrsArchive.WriteFile(
            archive,
            [
                new WrsFile("TITLE.WDL", Encoding.ASCII.GetBytes(Declarations(attachPosY) + start)),
                new WrsFile("m.wmp", Encoding.ASCII.GetBytes(Map)),
            ]);
        return RuntimeHost.OpenArchive(archive, "TITLE.WDL", new FakeWindow(), new FakeInput(), new NullAudioSink(), _render);
    }
}
