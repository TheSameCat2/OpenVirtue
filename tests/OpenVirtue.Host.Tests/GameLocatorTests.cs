// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Host;

namespace OpenVirtue.Host.Tests;

public sealed class GameLocatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ov-locator-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void FindTitleArchive_LowerCaseName_IsFound()
    {
        string game = Directory.CreateDirectory(Path.Combine(_root, "game")).FullName;
        File.WriteAllBytes(Path.Combine(game, "title.wrs"), []);

        Assert.Equal(Path.Combine(game, "title.wrs"), GameLocator.FindTitleArchive([game]));
    }

    [Fact]
    public void FindTitleArchive_WholeInstallCopiedAsASubfolder_IsFound()
    {
        string install = Directory.CreateDirectory(Path.Combine(_root, "game", "Saints of Virtue")).FullName;
        File.WriteAllBytes(Path.Combine(install, "TITLE.WRS"), []);

        Assert.Equal(Path.Combine(install, "TITLE.WRS"), GameLocator.FindTitleArchive([Path.Combine(_root, "game")]));
    }

    [Fact]
    public void FindTitleArchive_TakesTheFirstCandidateThatHasIt()
    {
        string first = Directory.CreateDirectory(Path.Combine(_root, "a")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(_root, "b")).FullName;
        File.WriteAllBytes(Path.Combine(second, "TITLE.WRS"), []);
        File.WriteAllBytes(Path.Combine(first, "TITLE.WRS"), []);

        Assert.Equal(Path.Combine(first, "TITLE.WRS"), GameLocator.FindTitleArchive([Path.Combine(_root, "missing"), first, second]));
    }

    [Fact]
    public void FindTitleArchive_NoGameAnywhere_ReturnsNull() =>
        Assert.Null(GameLocator.FindTitleArchive([_root, Path.Combine(_root, "missing")]));

    [Fact]
    public void CandidateDirectories_SearchOrder_OverrideThenProgramThenUserThenWorkingFolder()
    {
        IReadOnlyList<string> candidates = GameLocator.CandidateDirectories("/override", "/prog", "/data", "/cwd");

        Assert.Equal(
            ["/override", Path.Combine("/prog", "game"), "/prog", Path.Combine("/data", "OpenVirtue", "game"), Path.Combine("/cwd", "game"), "/cwd"],
            candidates);
    }
}
