// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Formats.Save;

namespace OpenVirtue.Tools.Tests;

/// <summary>The oracle-diff harness's section-by-section comparison of full saves.</summary>
public sealed class OracleSaveCompareTests
{
    private static FullSave Save(int skill, int statistics, int playerRegion) =>
        new("T.WDL", "T.WMP", "T.WRS", [], [], [], [],
            [new SaveSkill("k", skill), new SaveSkill("TIME_TOTAL", statistics)], [])
        {
            PlayerRegion = playerRegion,
        };

    [Fact]
    public void CompareSaveSections_OnlyUnmodelledParts_MatchAndNameWhatWasSetAside()
    {
        byte[] a = AcknexFullSave.Write(Save(5, 16, 3));
        byte[] b = AcknexFullSave.Write(Save(5, 0, 3));

        (List<string> differing, List<string> setAside) = OracleReplay.CompareSaveSections(a, b)!.Value;

        Assert.Empty(differing);
        Assert.Equal(["TIME_TOTAL"], setAside);
    }

    [Fact]
    public void CompareSaveSections_ModelledDifferences_NameTheSectionAndSkill()
    {
        byte[] a = AcknexFullSave.Write(Save(5, 0, 3));
        byte[] b = AcknexFullSave.Write(Save(6, 0, 4));

        (List<string> differing, _) = OracleReplay.CompareSaveSections(a, b)!.Value;

        Assert.Equal(2, differing.Count);
        Assert.StartsWith("section 0 (first) at +", differing[0], StringComparison.Ordinal);
        Assert.Equal("section 8 (skills): k 0.005/0.006", differing[1]);
    }
}
