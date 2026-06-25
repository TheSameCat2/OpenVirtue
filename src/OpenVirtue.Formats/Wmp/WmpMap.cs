// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Text;

namespace OpenVirtue.Formats.Wmp;

/// <summary>
/// A parsed Acknex-3 <c>WMP</c> map: the level geometry (vertices, regions, walls)
/// and placed objects (things, actors, player start).
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
        WmpPlayerStart? playerStart)
    {
        Vertices = vertices;
        Regions = regions;
        Walls = walls;
        Things = things;
        Actors = actors;
        PlayerStart = playerStart;
    }

    public IReadOnlyList<WmpVertex> Vertices { get; }
    public IReadOnlyList<WmpRegion> Regions { get; }
    public IReadOnlyList<WmpWall> Walls { get; }
    public IReadOnlyList<WmpPlacement> Things { get; }
    public IReadOnlyList<WmpPlacement> Actors { get; }
    public WmpPlayerStart? PlayerStart { get; }

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
        WmpPlayerStart? playerStart = null;

        foreach (string[] record in EnumerateRecords(text))
        {
            switch (record[0].ToUpperInvariant())
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
                case "PLAYER_START":
                    playerStart = new WmpPlayerStart(Float(record, 1), Float(record, 2), Float(record, 3), Int(record, 4));
                    break;
                default:
                    throw new InvalidDataException($"Unknown WMP record '{record[0]}'.");
            }
        }

        return new WmpMap(vertices, regions, walls, things, actors, playerStart);
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
