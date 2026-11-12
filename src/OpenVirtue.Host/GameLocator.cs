// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

namespace OpenVirtue.Host;

/// <summary>
/// Finds the user's own Saints of Virtue files, so the game starts with no command-line arguments: the folder
/// holding <c>TITLE.WRS</c>, the archive the original's <c>VRUN TITLE</c> starts from. Names are matched without
/// regard to case, since installs mix them (<c>apathy.wrs</c> beside <c>HEART.WRS</c>), and a folder may hold the
/// game directly or in one subfolder (a whole copied install such as <c>Saints of Virtue/</c>).
/// </summary>
public static class GameLocator
{
    /// <summary>The archive the game starts from.</summary>
    public const string TitleArchive = "TITLE.WRS";

    /// <summary>The title archive's main script.</summary>
    public const string TitleScript = "TITLE.WDL";

    /// <summary>The environment variable that names the game folder outright.</summary>
    public const string GameDirectoryVariable = "OPENVIRTUE_GAME_DIR";

    /// <summary>
    /// The folders searched, in order: <see cref="GameDirectoryVariable"/>; <c>game/</c> beside the program and the
    /// program's own folder; the per-user data folder's <c>OpenVirtue/game/</c>; <c>game/</c> in the working folder
    /// and the working folder itself.
    /// </summary>
    public static IReadOnlyList<string> CandidateDirectories(string? overrideDirectory, string programDirectory, string userDataDirectory, string workingDirectory)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            candidates.Add(overrideDirectory);
        }

        candidates.Add(Path.Combine(programDirectory, "game"));
        candidates.Add(programDirectory);
        candidates.Add(UserGameDirectory(userDataDirectory));
        candidates.Add(Path.Combine(workingDirectory, "game"));
        candidates.Add(workingDirectory);
        return candidates.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>The per-user folder players are told to copy their files into.</summary>
    public static string UserGameDirectory(string userDataDirectory) => Path.Combine(userDataDirectory, "OpenVirtue", "game");

    /// <summary>The default search, from this process's environment.</summary>
    public static IReadOnlyList<string> DefaultCandidates() =>
        CandidateDirectories(
            Environment.GetEnvironmentVariable(GameDirectoryVariable),
            AppContext.BaseDirectory,
            DefaultUserDataDirectory(),
            Environment.CurrentDirectory);

    /// <summary>
    /// The per-user data folder: <c>%LOCALAPPDATA%</c> on Windows, <c>$XDG_DATA_HOME</c> or <c>~/.local/share</c>
    /// elsewhere.
    /// </summary>
    public static string DefaultUserDataDirectory() =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);

    /// <summary>
    /// The path of <see cref="TitleArchive"/> in the first candidate folder that holds it, directly or one subfolder
    /// down, or null when none does.
    /// </summary>
    public static string? FindTitleArchive(IEnumerable<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        foreach (string directory in candidates)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            if (FindFile(directory, TitleArchive) is { } direct)
            {
                return direct;
            }

            foreach (string sub in SafeDirectories(directory))
            {
                if (FindFile(sub, TitleArchive) is { } nested)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    /// <summary>The file in <paramref name="directory"/> named <paramref name="name"/> in any case, or null.</summary>
    public static string? FindFile(string directory, string name)
    {
        string exact = Path.Combine(directory, name);
        if (File.Exists(exact))
        {
            return exact;
        }

        if (!Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(directory)
                .Where(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static List<string> SafeDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
