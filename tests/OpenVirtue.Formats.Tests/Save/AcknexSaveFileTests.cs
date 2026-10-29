// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.Text;
using OpenVirtue.Formats.Save;
using OpenVirtue.Formats.Tests.TestSupport;

namespace OpenVirtue.Formats.Tests.Save;

/// <summary>The original's save files (OV-20261029-save-format).</summary>
public class AcknexSaveFileTests
{
    [Fact]
    public void WriteInfo_TwoSkills_HasTheMeasuredLayout()
    {
        // LOCAL sv_l 77 and GLOBAL sv_g 1234.5 in the order the original wrote them.
        byte[] body = AcknexSaveFile.WriteInfo([new SaveSkill("sv_l", 77 * 1024), new SaveSkill("sv_g", 1264128)]);

        Assert.Equal(82, body.Length);
        Assert.Equal(0x186u, BinaryPrimitives.ReadUInt32LittleEndian(body));
        Assert.Equal(82u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4)));
        Assert.Equal(38u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8))); // the skill section's size
        Assert.Equal(12u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12))); // its own offset
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(16)));
        Assert.Equal("sv_l\0", Encoding.Latin1.GetString(body, 20, 5));
        Assert.Equal(new byte[] { 0x00, 0x34, 0x01, 0x00, 0x01, 0x00, 0x00, 0x80, 0xff, 0xff, 0xff, 0x7f }, body[25..37]);
        Assert.Equal(0x3au, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x3a))); // the bitmap section's offset
        Assert.Equal(0x46u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x46))); // the string section's offset
        Assert.Equal(new byte[] { 0x25, 0x26, 0x00, 0x00 }, body[^4..]);
    }

    [Fact]
    public void PackUnpack_RoundTripsAndReadSkillsFindsTheRecords()
    {
        SaveSkill[] skills = [new("lives", 3 * 1024), new("score", -5, -100, 100)];

        byte[] file = AcknexSaveFile.Pack(AcknexSaveFile.WriteInfo(skills));
        IReadOnlyList<SaveSkill> read = AcknexSaveFile.ReadSkills(AcknexSaveFile.Unpack(file));

        Assert.Equal(skills, read);
    }

    [Fact]
    public void ReadSkills_SavesTheDosRuntimeWrote_HoldTheirSkills()
    {
        // Guarded: the save-format probe's DOS outputs, when present locally.
        string? root = ResearchData.Directory;
        string probe = root is null ? "" : Path.Combine(root, "oracle-runs", "OV-20261029-save-format");
        if (!System.IO.Directory.Exists(probe))
        {
            return;
        }

        string info = Path.Combine(probe, "info-1", "work", "OVSAV1.SAV");
        string full = Path.Combine(probe, "full-1", "work", "OVSAV2.SAV");
        if (!File.Exists(info) || !File.Exists(full))
        {
            return;
        }

        byte[] infoBody = AcknexSaveFile.Unpack(File.ReadAllBytes(info));
        Assert.Equal(AcknexSaveFile.WriteInfo(AcknexSaveFile.ReadSkills(infoBody)), infoBody);

        IReadOnlyList<SaveSkill> fullSkills = AcknexSaveFile.ReadSkills(AcknexSaveFile.Unpack(File.ReadAllBytes(full)));
        Assert.Contains(fullSkills, s => s.Name == "sv_g" && s.RawValue == 1264128);
        Assert.Contains(fullSkills, s => s.Name == "PLAYER_X" && s.RawValue == 5 * 1024);
        Assert.DoesNotContain(fullSkills, s => s.Name == "sv_l"); // LOCAL skills are not in a full SAVE
    }
}
