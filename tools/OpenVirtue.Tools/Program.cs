// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text;
using OpenVirtue.Engine;
using OpenVirtue.Formats.Pcx;
using OpenVirtue.Formats.Wav;
using OpenVirtue.Formats.Wdl;
using OpenVirtue.Formats.Wmp;
using OpenVirtue.Formats.Wrs;

return Cli.Run(args);

internal static class Cli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            return Usage();
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "wrs" => Wrs(args[1..]),
                "pcx" => Pcx(args[1..]),
                "wmp" => Wmp(args[1..]),
                "wav" => Wav(args[1..]),
                "wdl" => Wdl(args[1..]),
                "level" => Level(args[1..]),
                "-h" or "--help" or "help" => Usage(),
                _ => Usage($"Unknown command '{args[0]}'."),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Wrs(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage("wrs requires a subcommand and an archive path.");
        }

        string verb = args[0].ToLowerInvariant();
        string archivePath = args[1];
        WrsArchive archive = WrsArchive.ReadFile(archivePath);

        switch (verb)
        {
            case "list":
                ListEntries(archive, archivePath);
                return 0;

            case "extract":
                string outDir = args.Length >= 3 ? args[2] : Path.GetFileNameWithoutExtension(archivePath);
                ExtractEntries(archive, outDir);
                return 0;

            default:
                return Usage($"Unknown wrs subcommand '{verb}'.");
        }
    }

    private static int Pcx(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage("pcx requires a subcommand and a file path.");
        }

        string verb = args[0].ToLowerInvariant();
        string path = args[1];

        switch (verb)
        {
            case "info":
                PcxImage image = PcxImage.Read(File.ReadAllBytes(path));
                Console.WriteLine($"{Path.GetFileName(path)} — {image.Width}x{image.Height}, 256-color palette");
                string sample = string.Join(" ", Enumerable.Range(0, 4)
                    .Select(i => $"({image.Palette[i].R},{image.Palette[i].G},{image.Palette[i].B})"));
                Console.WriteLine($"palette[0..3]: {sample}");
                return 0;

            default:
                return Usage($"Unknown pcx subcommand '{verb}'.");
        }
    }

    private static int Wmp(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage("wmp requires a subcommand and a file path.");
        }

        string verb = args[0].ToLowerInvariant();
        string path = args[1];

        switch (verb)
        {
            case "info":
                WmpMap map = WmpMap.ReadFile(path);
                Console.WriteLine(
                    $"{Path.GetFileName(path)} — {map.Vertices.Count} vertices, {map.Regions.Count} regions, " +
                    $"{map.Walls.Count} walls, {map.Things.Count} things, {map.Actors.Count} actors");
                if (map.PlayerStart is { } ps)
                {
                    Console.WriteLine($"player start: ({ps.X}, {ps.Y}) angle {ps.Angle} in region {ps.Region}");
                }

                return 0;

            default:
                return Usage($"Unknown wmp subcommand '{verb}'.");
        }
    }

    private static int Wav(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage("wav requires a subcommand and a file path.");
        }

        string verb = args[0].ToLowerInvariant();
        string path = args[1];

        switch (verb)
        {
            case "info":
                WavSound sound = WavSound.ReadFile(path);
                int bytesPerSecond = sound.SampleRate * sound.Channels * sound.BitsPerSample / 8;
                double seconds = bytesPerSecond > 0 ? sound.Data.Length / (double)bytesPerSecond : 0;
                Console.WriteLine(
                    $"{Path.GetFileName(path)} — {sound.Channels}ch {sound.SampleRate}Hz " +
                    $"{sound.BitsPerSample}-bit (format {sound.AudioFormat}), {sound.Data.Length} bytes (~{seconds:0.00}s)");
                return 0;

            default:
                return Usage($"Unknown wav subcommand '{verb}'.");
        }
    }

    private static int Wdl(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage("wdl requires a subcommand and a file path.");
        }

        string verb = args[0].ToLowerInvariant();
        string path = args[1];

        switch (verb)
        {
            case "info":
                // WDL is DOS text; Latin1 maps every byte 1:1.
                WdlDocument doc = WdlParser.Parse(Encoding.Latin1.GetString(File.ReadAllBytes(path)));
                Console.WriteLine($"{Path.GetFileName(path)} — {doc.Items.Count} top-level items, grouped by keyword:");
                var byKeyword = doc.Items
                    .Where(i => !i.IsLabel)
                    .GroupBy(i => i.Keyword.ToUpperInvariant())
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key);
                foreach (var group in byKeyword)
                {
                    Console.WriteLine($"  {group.Key,-18} {group.Count()}");
                }

                return 0;

            default:
                return Usage($"Unknown wdl subcommand '{verb}'.");
        }
    }

    private static int Level(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage("level requires a subcommand and an archive path.");
        }

        string verb = args[0].ToLowerInvariant();
        string archivePath = args[1];

        switch (verb)
        {
            case "info":
                WrsArchive archive = WrsArchive.ReadFile(archivePath);
                // Convention: a level's main script is named after its archive (apathy.wrs -> APATHY.WDL).
                string mainWdl = args.Length >= 3
                    ? args[2]
                    : Path.GetFileNameWithoutExtension(archivePath) + ".WDL";

                OpenVirtue.Engine.Level level = LevelLoader.Load(archive, mainWdl);
                Console.WriteLine($"{Path.GetFileName(archivePath)} -> {level.Name}");
                Console.WriteLine(
                    $"  {level.Vertices.Count} vertices, {level.Regions.Count} regions, {level.Walls.Count} walls, " +
                    $"{level.Things.Count} things, {level.Actors.Count} actors");
                Console.WriteLine($"  {level.Skills.Count} global skills, {level.Textures.Count} textures");
                if (level.PlayerStart is { } ps)
                {
                    Console.WriteLine($"  player start: ({ps.X}, {ps.Y}) angle {ps.Angle} in region {ps.Region}");
                }

                return 0;

            default:
                return Usage($"Unknown level subcommand '{verb}'.");
        }
    }

    private static void ListEntries(WrsArchive archive, string archivePath)
    {
        Console.WriteLine($"{Path.GetFileName(archivePath)} — {archive.Entries.Count} entries");
        Console.WriteLine($"{"name",-16} {"compressed",12} {"size",12}");
        Console.WriteLine(new string('-', 42));

        long totalCompressed = 0;
        long totalSize = 0;
        var byExtension = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (WrsEntry entry in archive.Entries)
        {
            Console.WriteLine($"{entry.Name,-16} {entry.CompressedSize,12:n0} {entry.UncompressedSize,12:n0}");
            totalCompressed += entry.CompressedSize;
            totalSize += entry.UncompressedSize;

            string ext = Path.GetExtension(entry.Name);
            ext = string.IsNullOrEmpty(ext) ? "(none)" : ext.ToLowerInvariant();
            byExtension[ext] = byExtension.GetValueOrDefault(ext) + 1;
        }

        Console.WriteLine(new string('-', 42));
        Console.WriteLine($"{"total",-16} {totalCompressed,12:n0} {totalSize,12:n0}");
        Console.WriteLine();
        Console.WriteLine("by type: " + string.Join(", ", byExtension.Select(kv => $"{kv.Key}={kv.Value}")));
    }

    private static void ExtractEntries(WrsArchive archive, string outDir)
    {
        Directory.CreateDirectory(outDir);
        foreach (WrsEntry entry in archive.Entries)
        {
            // Guard against path traversal from archive-controlled names.
            string safeName = Path.GetFileName(entry.Name);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                continue;
            }

            string destination = Path.Combine(outDir, safeName);
            File.WriteAllBytes(destination, entry.GetData());
        }

        Console.WriteLine($"Extracted {archive.Entries.Count} entries to {outDir}");
    }

    private static int Usage(string? error = null)
    {
        if (error is not null)
        {
            Console.Error.WriteLine($"error: {error}");
        }

        Console.WriteLine(
            """
            OpenVirtue.Tools — inspection utilities for Acknex-3 game data

            Usage:
              ovtool wrs list <archive.wrs>
              ovtool wrs extract <archive.wrs> [output-dir]
              ovtool pcx info <image.pcx>
              ovtool wmp info <map.wmp>
              ovtool wav info <sound.wav>
              ovtool wdl info <script.wdl>
              ovtool level info <archive.wrs> [main.wdl]

            Notes:
              These tools operate on game data you supply; no game data ships with
              OpenVirtue. Extracted files are the user's own copy.
            """);

        return error is null ? 0 : 2;
    }
}
