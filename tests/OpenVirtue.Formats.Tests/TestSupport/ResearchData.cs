// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Formats.Tests.TestSupport;

/// <summary>
/// Locates real, user-supplied game data under the git-ignored <c>_research/</c>
/// folder so integration tests can validate against it when present. Returns
/// nothing when the data is absent (e.g. CI), so such tests become no-ops. No
/// game data or fixtures are ever committed.
/// </summary>
internal static class ResearchData
{
    private static readonly HashSet<string> RetailArchiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "APATHY.WRS",
        "HEART.WRS",
        "LEGALISM.WRS",
        "NEWAGE.WRS",
        "START.WRS",
        "TITLE.WRS",
    };

    /// <summary>The absolute path to <c>_research/</c>, or <c>null</c> if not found.</summary>
    public static string? Directory
    {
        get
        {
            for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "OpenVirtue.slnx")))
                {
                    string research = Path.Combine(dir.FullName, "_research");
                    return System.IO.Directory.Exists(research) ? research : null;
                }
            }

            return null;
        }
    }

    /// <summary>Enumerates every <c>*.wrs</c> file under <c>_research/</c> (empty if none).</summary>
    public static IReadOnlyList<string> WrsFiles()
    {
        string? root = Directory;
        if (root is null)
        {
            return [];
        }

        return System.IO.Directory
            .EnumerateFiles(root, "*.wrs", SearchOption.AllDirectories)
            .Where(static p => p.EndsWith(".wrs", StringComparison.OrdinalIgnoreCase))
            .Where(static p => RetailArchiveNames.Contains(Path.GetFileName(p)))
            .Where(static p => !IsLocalArtifact(p))
            .ToArray();
    }

    private static bool IsLocalArtifact(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static part => part.Equals("oracle-runs", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("_scratch", StringComparison.OrdinalIgnoreCase));
}
