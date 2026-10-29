// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using OpenVirtue.Engine.Map;

namespace OpenVirtue.Engine.Tests;

public class LevelTopologyTests
{
    [Fact]
    public void TryGetSurfaceHeightOffset_AfterVerticesMove_MatchesAFreshlyLoadedLevel()
    {
        (float X, float Y, float Z)[] square = [(0, 0, 10), (10, 0, 0), (10, 10, 4), (0, 10, 0)];
        Level level = Load(square);
        Sample(level);

        // Pull each corner inward in turn, past the opposite diagonal, so the region turns
        // concave there and its floor triangulates along the other diagonal. The cached
        // surface must give what a level loaded with the moved vertex gives.
        var vertices = (IList<Vertex>)level.Vertices;
        for (int corner = 0; corner < square.Length; corner++)
        {
            (float X, float Y, float Z)[] moved = [.. square];
            moved[corner] = (square[corner].X == 0 ? 7 : 3, square[corner].Y == 0 ? 7 : 3, square[corner].Z + 3);
            for (int i = 0; i < moved.Length; i++)
            {
                vertices[i] = new Vertex(moved[i].X, moved[i].Y, moved[i].Z);
            }

            Assert.Equal(Sample(Load(moved)), Sample(level));
            Assert.Equal(LevelTopology.LocateRegion(Load(moved), 9, 5), LevelTopology.LocateRegion(level, 9, 5));
        }
    }

    private static List<double?> Sample(Level level)
    {
        var offsets = new List<double?>();
        for (double y = 0.5; y < 10; y += 1)
        {
            for (double x = 0.5; x < 10; x += 1)
            {
                offsets.Add(LevelTopology.TryGetSurfaceHeightOffset(level, 0, x, y, out double offset) ? offset : null);
            }
        }

        return offsets;
    }

    [Theory]
    [InlineData(2, 2, "room")]
    [InlineData(6, 6, "pad")]
    public void LocateRegion_RoomWithAnIsland_FindsTheInnermostRegion(double x, double y, string region)
    {
        // A room (0..10) around a pad island (4..8), the room's walls bordering an outer region: the
        // room's walls form two loops and the outer region shares the room's outline
        // (OV-20261029-slope's carrier).
        const string map =
            "VERTEX 0 0 0;#0\nVERTEX 10 0 0;#1\nVERTEX 10 10 0;#2\nVERTEX 0 10 0;#3\n" +
            "VERTEX 4 4 0;#4\nVERTEX 8 4 0;#5\nVERTEX 8 8 0;#6\nVERTEX 4 8 0;#7\n" +
            "REGION room 0 16;#0\nREGION solid 16 16;#1\nREGION pad 0 16;#2\n" +
            "WALL w 0 1 0 1 0 0;#0\nWALL w 1 2 0 1 0 0;#1\nWALL w 2 3 0 1 0 0;#2\nWALL w 3 0 0 1 0 0;#3\n" +
            "WALL w 4 5 2 0 0 0;#4\nWALL w 5 6 2 0 0 0;#5\nWALL w 6 7 2 0 0 0;#6\nWALL w 7 4 2 0 0 0;#7\n" +
            "PLAYER_START 2 2 0 0;#8";
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        Level level = LevelLoader.LoadCore("x", "MAPFILE <m.wmp>;", n => resources.GetValueOrDefault(Path.GetFileName(n)));

        int? found = LevelTopology.LocateRegion(level, x, y);

        Assert.NotNull(found);
        Assert.Equal(region, level.Regions[found.Value].Name);
    }

    private static Level Load((float X, float Y, float Z)[] corners)
    {
        string map = string.Concat(corners.Select((c, i) =>
                string.Create(CultureInfo.InvariantCulture, $"VERTEX {c.X} {c.Y} {c.Z};#{i}\n"))) +
            "REGION room 0 12;#0\n" +
            "WALL w 0 1 0 -1 0 0;#0\nWALL w 1 2 0 -1 0 0;#1\n" +
            "WALL w 2 3 0 -1 0 0;#2\nWALL w 3 0 0 -1 0 0;#3\n" +
            "PLAYER_START 5 5 0 0;#4";
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["m.wmp"] = map };
        return LevelLoader.LoadCore("x", "MAPFILE <m.wmp>;", n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }
}
