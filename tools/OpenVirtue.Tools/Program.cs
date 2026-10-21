// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Text;
using System.Numerics;
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
                "oracle" => Oracle(args[1..]),
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
            return Usage("wrs requires a subcommand and path arguments.");
        }

        string verb = args[0].ToLowerInvariant();

        switch (verb)
        {
            case "list":
            {
                string archivePath = args[1];
                WrsArchive archive = WrsArchive.ReadFile(archivePath);
                ListEntries(archive, archivePath);
                return 0;
            }

            case "extract":
            {
                string archivePath = args[1];
                WrsArchive archive = WrsArchive.ReadFile(archivePath);
                string outDir = args.Length >= 3 ? args[2] : Path.GetFileNameWithoutExtension(archivePath);
                ExtractEntries(archive, outDir);
                return 0;
            }

            case "pack":
                if (args.Length < 3)
                {
                    return Usage("wrs pack requires an input directory and output archive path.");
                }

                PackEntries(args[1], args[2]);
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
            case "coverage":
                return WdlCoverage(args[1..]);

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

    private static int WdlCoverage(string[] args)
    {
        if (args.Length < 1)
        {
            return Usage("wdl coverage requires an archive path or directory.");
        }

        string inputPath = args[0];
        string? explicitMainWdl = args.Length >= 2 ? args[1] : null;
        List<string> archives = DiscoverArchives(inputPath).ToList();
        if (archives.Count == 0)
        {
            throw new FileNotFoundException($"No .wrs archives found at '{inputPath}'.");
        }

        var aggregate = new WdlCoverageReport();
        var analyzed = new List<(string Archive, string MainWdl)>();
        var failed = new List<(string Archive, string MainWdl, string Error)>();

        foreach (string archivePath in archives)
        {
            string mainWdl = explicitMainWdl ?? Path.GetFileNameWithoutExtension(archivePath) + ".WDL";
            try
            {
                WdlProgram program = LoadWdlProgramFromArchive(archivePath, mainWdl);
                aggregate.Add(WdlCoverageAnalyzer.Analyze(program));
                analyzed.Add((Path.GetFileName(archivePath), mainWdl));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                failed.Add((Path.GetFileName(archivePath), mainWdl, ex.Message));
            }
        }

        PrintWdlCoverage(aggregate, analyzed, failed);
        return analyzed.Count > 0 && failed.Count == 0 ? 0 : 1;
    }

    private static IEnumerable<string> DiscoverArchives(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            yield return inputPath;
            yield break;
        }

        if (!Directory.Exists(inputPath))
        {
            throw new FileNotFoundException($"Path not found: {inputPath}");
        }

        foreach (string path in Directory.EnumerateFiles(inputPath, "*.wrs", SearchOption.AllDirectories)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            yield return path;
        }
    }

    private static WdlProgram LoadWdlProgramFromArchive(string archivePath, string mainWdl)
    {
        WrsArchive archive = WrsArchive.ReadFile(archivePath);
        Dictionary<string, string> modules = archive.Entries
            .Where(e => e.Name.EndsWith(".wdl", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(e => e.Name, e => Encoding.Latin1.GetString(e.GetData()), StringComparer.OrdinalIgnoreCase);

        if (!modules.TryGetValue(mainWdl, out string? mainSource))
        {
            throw new InvalidDataException($"Main WDL '{mainWdl}' was not found in the archive.");
        }

        return WdlPreprocessor.Flatten(
            mainSource,
            name => modules.TryGetValue(Path.GetFileName(name), out string? source) ? source : null);
    }

    private static void PrintWdlCoverage(
        WdlCoverageReport aggregate,
        List<(string Archive, string MainWdl)> analyzed,
        List<(string Archive, string MainWdl, string Error)> failed)
    {
        Console.WriteLine("WDL runtime coverage (sanitized)");
        Console.WriteLine("No WDL source lines, literal strings, action bodies, or proprietary assets are emitted.");
        Console.WriteLine();

        Console.WriteLine("sources analyzed:");
        foreach ((string archive, string mainWdl) in analyzed.OrderBy(s => s.Archive, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {archive} -> {mainWdl}");
        }

        if (failed.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("sources skipped:");
            foreach ((string archive, string mainWdl, string error) in failed.OrderBy(s => s.Archive, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  {archive} -> {mainWdl}: {error}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"actions analyzed: {aggregate.ActionCount}");
        PrintCounts("declaration/item keywords", aggregate.DeclarationKeywords);
        PrintCounts("ACTION-body statement keywords", aggregate.ActionStatementKeywords);
        PrintCounts("implemented built-in ACTION statements", aggregate.ImplementedStatementKeywords);
        PrintCounts("not built-in ACTION statements", aggregate.NonBuiltinStatementKeywords);
        PrintCounts("IF_* hook declarations", aggregate.ActionHookDeclarations);
        PrintCounts("IF_* hook reassignments", aggregate.ActionHookReassignments);
        PrintCounts("scheduler declarations", aggregate.SchedulerDeclarations);
        PrintCounts("scheduler reassignments", aggregate.SchedulerReassignments);
        PrintCounts("predefined runtime skill references", aggregate.PredefinedSkillReferences);
        PrintCounts("player movement skill references", aggregate.PlayerMovementSkillReferences);
    }

    private static void PrintCounts(string title, Dictionary<string, int> counts)
    {
        Console.WriteLine();
        Console.WriteLine($"{title}:");
        if (counts.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }

        foreach ((string keyword, int count) in counts
                     .OrderByDescending(kv => kv.Value)
                     .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {keyword,-24} {count,8:n0}");
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
                    level.VideoMode is { } video
                        ? $"  video mode: {video.Name} ({video.Width}x{video.Height})"
                        : "  video mode: (none declared)");
                Console.WriteLine(
                    $"  {level.Vertices.Count} vertices, {level.Regions.Count} regions, {level.Walls.Count} walls, " +
                    $"{level.Things.Count} things, {level.Actors.Count} actors");
                Console.WriteLine($"  {level.Skills.Count} global skills, {level.Textures.Count} textures");
                int soundTextures = level.Textures.Values.Count(static texture => texture.SoundName is not null);
                int loopingSoundTextures = level.Textures.Values.Count(
                    static texture => texture.SoundName is not null && texture.IsSoundLoop);
                Console.WriteLine($"  texture sounds: {soundTextures} ({loopingSoundTextures} looping)");
                bool UsesLoopingSound(string? textureName) =>
                    textureName is not null &&
                    level.Textures.TryGetValue(textureName, out OpenVirtue.Engine.LevelTexture texture) &&
                    texture.SoundName is not null &&
                    texture.IsSoundLoop;
                int loopingWalls = level.Walls.Count(wall => UsesLoopingSound(wall.Texture));
                int loopingEntities = level.Things.Count(thing => UsesLoopingSound(thing.Texture)) +
                                      level.Actors.Count(actor => UsesLoopingSound(actor.Texture));
                int loopingCeilings = level.Regions.Count(region => UsesLoopingSound(region.CeilTexture));
                Console.WriteLine(
                    $"  looping placements: {loopingWalls} walls, {loopingEntities} entities, " +
                    $"{loopingCeilings} ceilings");
                Console.WriteLine(
                    $"  lighting declarations: AMBIENT {level.Skills.GetValueOrDefault("AMBIENT")}, " +
                    $"PLAYER_LIGHT {level.Skills.GetValueOrDefault("PLAYER_LIGHT")}, " +
                    $"LIGHT_DIST {level.Skills.GetValueOrDefault("LIGHT_DIST")}");
                int attachedWalls = level.Walls.Count(static wall => wall.AttachTexture is not null);
                int lightmappedWalls = level.Walls.Count(wall =>
                    wall.AttachTexture is { } attach &&
                    level.Textures.TryGetValue(attach, out OpenVirtue.Engine.LevelTexture texture) &&
                    texture.IsLightmap);
                Console.WriteLine($"  attached walls: {attachedWalls} ({lightmappedWalls} lightmaps)");
                IEnumerable<OpenVirtue.Engine.Map.MapEntity> entities = level.Things.Cast<OpenVirtue.Engine.Map.MapEntity>()
                    .Concat(level.Actors);
                int attachedEntities = entities.Count(static entity => entity.AttachTexture is not null);
                int shadowEntities = entities.Count(entity =>
                    entity.AttachTexture is { } attach &&
                    level.Textures.TryGetValue(attach, out OpenVirtue.Engine.LevelTexture texture) &&
                    texture.IsShadow);
                Console.WriteLine($"  attached entities: {attachedEntities} ({shadowEntities} shadows)");
                int wallVerts = OpenVirtue.Engine.Rendering.MeshBuilder.BuildWalls(level).VertexCount;
                int fullVerts = OpenVirtue.Engine.Rendering.MeshBuilder.Build(level).VertexCount;
                Console.WriteLine($"  mesh: {wallVerts} wall verts, {fullVerts - wallVerts} floor/ceiling verts");
                if (level.PlayerStart is { } ps)
                {
                    Console.WriteLine($"  player start: ({ps.X}, {ps.Y}) angle {ps.Angle} in region {ps.Region}");
                    if (ps.Region >= 0 && ps.Region < level.Regions.Count)
                    {
                        OpenVirtue.Engine.Map.Region startRegion = level.Regions[ps.Region];
                        Console.WriteLine(
                            $"  start region: {startRegion.Name}, ambient {startRegion.Ambient}, " +
                            $"floor {startRegion.FloorTexture ?? "NULL"}, ceiling {startRegion.CeilTexture ?? "NULL"}");
                        string adjacentTextures = string.Join(", ", level.Walls
                            .Where(wall => wall.LeftRegion == ps.Region || wall.RightRegion == ps.Region)
                            .GroupBy(wall => wall.Texture ?? "NULL", StringComparer.OrdinalIgnoreCase)
                            .OrderByDescending(group => group.Count())
                            .Select(group => $"{group.Key} x{group.Count()}"));
                        Console.WriteLine($"  start walls: {adjacentTextures}");
                        int[] boundaryVertices = level.Walls
                            .Where(wall => wall.LeftRegion == ps.Region || wall.RightRegion == ps.Region)
                            .SelectMany(wall => new[] { wall.Vertex1, wall.Vertex2 })
                            .Where(index => index >= 0 && index < level.Vertices.Count)
                            .Distinct()
                            .ToArray();
                        if (boundaryVertices.Length > 0)
                        {
                            Console.WriteLine(
                                $"  start bounds: x {boundaryVertices.Min(i => level.Vertices[i].X):F1}.." +
                                $"{boundaryVertices.Max(i => level.Vertices[i].X):F1}, y " +
                                $"{boundaryVertices.Min(i => level.Vertices[i].Y):F1}.." +
                                $"{boundaryVertices.Max(i => level.Vertices[i].Y):F1}, " +
                                $"{boundaryVertices.Length} boundary vertices");

                            float yaw = (float)(-ps.Angle * Math.PI / 180.0);
                            var eye = new Vector3((float)ps.X, (float)startRegion.FloorHeight + 3, (float)ps.Y);
                            var forward = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw));
                            Matrix4x4 viewProjection = Matrix4x4.CreateLookAt(eye, eye + forward, Vector3.UnitY) *
                                Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 640f / 419f, 1, 200000);
                            int visibleCorners = 0;
                            foreach (int index in boundaryVertices)
                            {
                                OpenVirtue.Engine.Map.Vertex vertex = level.Vertices[index];
                                foreach (float height in new[] { (float)startRegion.FloorHeight, (float)startRegion.CeilHeight })
                                {
                                    Vector4 clip = Vector4.Transform(
                                        new Vector4(vertex.X, height + vertex.Z, vertex.Y, 1),
                                        viewProjection);
                                    if (clip.W > 0 && Math.Abs(clip.X) <= clip.W && Math.Abs(clip.Y) <= clip.W &&
                                        clip.Z >= 0 && clip.Z <= clip.W)
                                    {
                                        visibleCorners++;
                                    }
                                }
                            }

                            Console.WriteLine($"  start clip: {visibleCorners}/{boundaryVertices.Length * 2} wall corners in frustum");
                            var degrees = new Dictionary<int, int>();
                            int edgeCount = 0;
                            foreach (OpenVirtue.Engine.Map.Wall wall in level.Walls.Where(
                                         wall => wall.LeftRegion == ps.Region || wall.RightRegion == ps.Region))
                            {
                                if (wall.Vertex1 == wall.Vertex2)
                                {
                                    continue;
                                }

                                degrees[wall.Vertex1] = degrees.GetValueOrDefault(wall.Vertex1) + 1;
                                degrees[wall.Vertex2] = degrees.GetValueOrDefault(wall.Vertex2) + 1;
                                edgeCount++;
                            }

                            Console.WriteLine(
                                $"  start topology: {edgeCount} edges, degree range " +
                                $"{degrees.Values.DefaultIfEmpty().Min()}..{degrees.Values.DefaultIfEmpty().Max()}");
                        }

                        IReadOnlyDictionary<string, OpenVirtue.Engine.Rendering.TextureImage> decoded =
                            OpenVirtue.Engine.Rendering.TextureLoader.Load(archive, level);
                        IEnumerable<string?> startTextures = level.Walls
                            .Where(wall => wall.LeftRegion == ps.Region || wall.RightRegion == ps.Region)
                            .Select(wall => wall.Texture)
                            .Append(startRegion.FloorTexture)
                            .Append(startRegion.CeilTexture)
                            .Distinct(StringComparer.OrdinalIgnoreCase);
                        foreach (string? textureName in startTextures)
                        {
                            if (textureName is null || !decoded.TryGetValue(textureName, out var image))
                            {
                                Console.WriteLine($"    texture {textureName ?? "NULL"}: unresolved");
                                continue;
                            }

                            long brightness = 0;
                            int pixels = 0;
                            for (int i = 0; i + 3 < image.Rgba.Length; i += 4)
                            {
                                brightness += image.Rgba[i] + image.Rgba[i + 1] + image.Rgba[i + 2];
                                pixels++;
                            }

                            double average = pixels > 0 ? brightness / (pixels * 3.0) : 0;
                            Console.WriteLine($"    texture {textureName}: {image.Width}x{image.Height}, avg {average:F1}/255");
                        }
                    }
                }

                var sprites = new Dictionary<string, (int W, int H, double Height, int Count)>(StringComparer.OrdinalIgnoreCase);
                void Tally(string? tex, double h)
                {
                    if (tex is null || !level.Textures.TryGetValue(tex, out var lt))
                    {
                        return;
                    }

                    sprites[tex] = sprites.TryGetValue(tex, out var e) ? (e.W, e.H, e.Height, e.Count + 1) : (lt.Width, lt.Height, h, 1);
                }

                foreach (var t in level.Things) Tally(t.Texture, t.Height);
                foreach (var a in level.Actors) Tally(a.Texture, a.Height);
                Console.WriteLine("  sprites (texture, WxH px, HEIGHT, count):");
                foreach (var kv in sprites.OrderByDescending(k => k.Value.Count).Take(12))
                {
                    Console.WriteLine($"    {kv.Key,-20} {kv.Value.W}x{kv.Value.H}  HEIGHT={kv.Value.Height}  x{kv.Value.Count}");
                }

                return 0;

            default:
                return Usage($"Unknown level subcommand '{verb}'.");
        }
    }

    private static int Oracle(string[] args)
    {
        if (args.Length < 1)
        {
            return Usage("oracle requires a subcommand.");
        }

        string verb = args[0].ToLowerInvariant();
        return verb switch
        {
            "prepare" => OracleProbeGenerator.Prepare(args[1..]),
            _ => Usage($"Unknown oracle subcommand '{verb}'."),
        };
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

    private static void PackEntries(string inputDir, string archivePath)
    {
        string fullInputDir = Path.GetFullPath(inputDir);
        if (!Directory.Exists(fullInputDir))
        {
            throw new DirectoryNotFoundException($"Input directory not found: {fullInputDir}");
        }

        string fullArchivePath = Path.GetFullPath(archivePath);
        string? archiveDir = Path.GetDirectoryName(fullArchivePath);
        if (!string.IsNullOrEmpty(archiveDir))
        {
            Directory.CreateDirectory(archiveDir);
        }

        WrsFile[] files = Directory.EnumerateFiles(fullInputDir)
            .Where(path => !Path.GetFullPath(path).Equals(fullArchivePath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path => new WrsFile(Path.GetFileName(path), File.ReadAllBytes(path)))
            .ToArray();

        WrsArchive.WriteFile(fullArchivePath, files);
        Console.WriteLine($"Packed {files.Length} entries into {fullArchivePath}");
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
              ovtool wrs pack <input-dir> <archive.wrs>
              ovtool pcx info <image.pcx>
              ovtool wmp info <map.wmp>
              ovtool wav info <sound.wav>
              ovtool wdl info <script.wdl>
              ovtool wdl coverage <archive-or-dir> [main.wdl]
              ovtool level info <archive.wrs> [main.wdl]
              ovtool oracle prepare scheduler-if-start-cycle [output-dir] [--runtime-dir <dir>] [--carrier-dir <dir>] [--dosbox-x <exe>]
              ovtool oracle prepare projection-fov|pixel-aspect|tilt-model [output-dir] [--runtime <VRUN-patched.EXE>] [--dosbox-x <exe>] [--arcs|--aspects|--tilts a,b,c] [--videos m1,m2]
              ovtool oracle prepare shoot-mapping [output-dir] [--runtime <VRUN-patched.EXE>] [--dosbox-x <exe>] [--shoot-x a,b] [--shoot-y a,b] [--tilts a,b] [--videos m1,m2]
              ovtool oracle prepare branch-semantics|each-sec-cadence|flic-play|flic-detail|proximity-events|wall-distance|wall-pass-clip|clip-contact|border-climb|eye-clamp|actor-contact|border-kinds|clip-detail|view-activity|distance-refresh [output-dir] [--runtime <VRUN-patched.EXE>] [--dosbox-x <exe>]

            Notes:
              These tools operate on game data you supply; no game data ships with
              OpenVirtue. Extracted files are the user's own copy.
            """);

        return error is null ? 0 : 2;
    }
}
