// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Text;

namespace OpenVirtue.Formats.Wmp;

/// <summary>
/// A parsed Acknex-3 <c>WMP</c> map: the level geometry (vertices, regions, walls)
/// and placed objects (things, actors, actor ways, player start).
/// </summary>
/// <remarks>
/// <para>
/// WMP is a <b>text</b> format emitted by WED (the 3D GameStudio World EDitor).
/// Records are whitespace-delimited and <c>;</c>-terminated; <c>#</c> starts a
/// comment to end of line (the <c>;#n</c> suffix is just the record's index). Clean-room
/// implementation from observation of the game's own maps — see <c>PROVENANCE.md</c>.
/// </para>
/// <para>Record forms (indices reference the tables in declaration order):</para>
/// <code>
/// VERTEX        x y z
/// REGION        name floor_hgt ceil_hgt
/// WALL          name vertex1 vertex2 region1 region2 offsx offsy
/// THING / ACTOR name x y angle region
/// WAY            name x1 y1 ... xn yn
/// PLAYER_START  x y angle region
/// </code>
/// </remarks>
public sealed class WmpMap
{
    private WmpMap(
        IReadOnlyList<WmpVertex> vertices,
        IReadOnlyList<WmpRegion> regions,
        IReadOnlyList<WmpWall> walls,
        IReadOnlyList<WmpPlacement> things,
        IReadOnlyList<WmpPlacement> actors,
        IReadOnlyList<WmpWay> ways,
        WmpPlayerStart? playerStart,
        IReadOnlyList<(string Kind, int Index)> objectOrder)
    {
        ObjectOrder = objectOrder;
        Vertices = vertices;
        Regions = regions;
        Walls = walls;
        Things = things;
        Actors = actors;
        Ways = ways;
        PlayerStart = playerStart;
    }

    public IReadOnlyList<WmpVertex> Vertices { get; }
    public IReadOnlyList<WmpRegion> Regions { get; }
    public IReadOnlyList<WmpWall> Walls { get; }
    public IReadOnlyList<WmpPlacement> Things { get; }
    public IReadOnlyList<WmpPlacement> Actors { get; }
    public IReadOnlyList<WmpWay> Ways { get; }
    public WmpPlayerStart? PlayerStart { get; }

    /// <summary>
    /// The map's objects in file order, as (record kind, index in that kind's list): walls, things,
    /// actors, ways and the player start share one count, the one the editor writes after <c>#</c>.
    /// The original numbers objects this way in its saves (OV-20261029-save-body round 9).
    /// </summary>
    public IReadOnlyList<(string Kind, int Index)> ObjectOrder { get; }

    /// <summary>Parses a WMP map from its text.</summary>
    /// <exception cref="InvalidDataException">A record is malformed or of an unknown type.</exception>
    public static WmpMap Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var vertices = new List<WmpVertex>();
        var regions = new List<WmpRegion>();
        var walls = new List<WmpWall>();
        var things = new List<WmpPlacement>();
        var actors = new List<WmpPlacement>();
        var ways = new List<WmpWay>();
        WmpPlayerStart? playerStart = null;
        var order = new List<(string, int)>();

        foreach (string[] record in EnumerateRecords(text))
        {
            string kind = record[0].ToUpperInvariant();
            int count = kind switch
            {
                "WALL" => walls.Count,
                "THING" => things.Count,
                "ACTOR" => actors.Count,
                "WAY" => ways.Count,
                "PLAYER_START" => 0,
                _ => -1,
            };
            if (count >= 0)
            {
                order.Add((kind, count));
            }

            switch (kind)
            {
                case "VERTEX":
                    vertices.Add(new WmpVertex(Float(record, 1), Float(record, 2), Float(record, 3)));
                    break;
                case "REGION":
                    regions.Add(new WmpRegion(record[1], Float(record, 2), Float(record, 3)));
                    break;
                case "WALL":
                    walls.Add(new WmpWall(
                        record[1],
                        Int(record, 2), Int(record, 3),
                        Int(record, 4), Int(record, 5),
                        Float(record, 6), Float(record, 7)));
                    break;
                case "THING":
                    things.Add(ReadPlacement(record));
                    break;
                case "ACTOR":
                    actors.Add(ReadPlacement(record));
                    break;
                case "WAY":
                    ways.Add(ReadWay(record));
                    break;
                case "PLAYER_START":
                    playerStart = new WmpPlayerStart(Float(record, 1), Float(record, 2), Float(record, 3), Int(record, 4));
                    break;
                default:
                    throw new InvalidDataException($"Unknown WMP record '{record[0]}'.");
            }
        }

        return new WmpMap(vertices, regions, walls, things, actors, ways, playerStart, order);
    }

    /// <summary>Parses a WMP map from bytes (decoded as Latin1 DOS text).</summary>
    public static WmpMap Read(ReadOnlySpan<byte> data) => Read(Encoding.Latin1.GetString(data));

    /// <summary>Reads and parses a WMP map from a file on disk.</summary>
    public static WmpMap ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Read(File.ReadAllBytes(path));
    }

    private static WmpPlacement ReadPlacement(string[] record) =>
        new(record[1], Float(record, 2), Float(record, 3), Float(record, 4), Int(record, 5));

    private static WmpWay ReadWay(string[] record)
    {
        Require(record, 3);
        if ((record.Length - 2) % 2 != 0)
        {
            throw new InvalidDataException($"WMP WAY record '{record[1]}' has an unmatched coordinate.");
        }

        var points = new List<WmpWayPoint>((record.Length - 2) / 2);
        for (int i = 2; i < record.Length; i += 2)
        {
            points.Add(new WmpWayPoint(Float(record, i), Float(record, i + 1)));
        }

        return new WmpWay(record[1], points);
    }

    /// <summary>
    /// Splits the source into records: whitespace-separated token arrays, one per
    /// <c>;</c>-terminated record, with <c>#</c>…end-of-line comments removed.
    /// </summary>
    private static IEnumerable<string[]> EnumerateRecords(string text)
    {
        var current = new List<string>();
        var token = new StringBuilder();
        bool inComment = false;

        foreach (char c in text)
        {
            if (inComment)
            {
                if (c == '\n')
                {
                    inComment = false;
                }

                continue;
            }

            switch (c)
            {
                case '#':
                    Flush(token, current);
                    inComment = true;
                    break;

                case ';':
                    Flush(token, current);
                    if (current.Count > 0)
                    {
                        yield return current.ToArray();
                        current.Clear();
                    }

                    break;

                case ' ' or '\t' or '\r' or '\n':
                    Flush(token, current);
                    break;

                default:
                    token.Append(c);
                    break;
            }
        }

        Flush(token, current);
        if (current.Count > 0)
        {
            yield return current.ToArray();
        }
    }

    private static void Flush(StringBuilder token, List<string> current)
    {
        if (token.Length > 0)
        {
            current.Add(token.ToString());
            token.Clear();
        }
    }

    private static float Float(string[] record, int index)
    {
        Require(record, index);
        return float.Parse(record[index], CultureInfo.InvariantCulture);
    }

    private static int Int(string[] record, int index)
    {
        Require(record, index);
        return int.Parse(record[index], CultureInfo.InvariantCulture);
    }

    private static void Require(string[] record, int index)
    {
        if (index >= record.Length)
        {
            throw new InvalidDataException($"WMP record '{record[0]}' is missing field {index} (has {record.Length}).");
        }
    }
}
