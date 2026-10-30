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

    [Fact]
    public void ReadObjects_ReadsTheFourthSectionsEntityAndWallRecords()
    {
        // OV-20261029-save-body: a header, three sections, then the object section with one
        // 170-byte entity record and one 178-byte wall record.
        byte[] objects = new byte[AcknexSaveFile.EntityRecordSize + AcknexSaveFile.WallRecordSize];
        BitConverter.TryWriteBytes(objects.AsSpan(110), 21 * 1024 + 512); // X 21.5
        BitConverter.TryWriteBytes(objects.AsSpan(114), -8 * 1024); // Y -8
        BitConverter.TryWriteBytes(objects.AsSpan(8), 768); // ANGLE 0.75
        BitConverter.TryWriteBytes(objects.AsSpan(88), 7 * 1024); // SKILL8 7
        BitConverter.TryWriteBytes(objects.AsSpan(AcknexSaveFile.EntityRecordSize + 12), 3 * 1024 + 512); // OFFSET_X 3.5
        using var body = new MemoryStream();
        body.Write([0x9a, 0x06, 0, 0, (byte)'T', 0, 0, 0]);
        foreach (byte[] payload in new[] { new byte[80], [], [], objects, new byte[4] })
        {
            body.Write(BitConverter.GetBytes(payload.Length));
            body.Write(BitConverter.GetBytes((int)body.Position));
            body.Write(payload);
        }

        var saved = AcknexSaveFile.ReadObjects(body.ToArray(), entityCount: 1, wallCount: 1);

        Assert.NotNull(saved);
        SaveObjectState entity = Assert.Single(saved.Value.Entities);
        Assert.Equal(21 * 1024 + 512, entity.RawSkills["X"]);
        Assert.Equal(-8 * 1024, entity.RawSkills["Y"]);
        Assert.Equal(768, entity.RawSkills["ANGLE"]);
        Assert.Equal(7 * 1024, entity.RawSkills["SKILL8"]);
        Assert.Equal(3 * 1024 + 512, Assert.Single(saved.Value.Walls).RawSkills["OFFSET_X"]);
        Assert.Null(AcknexSaveFile.ReadObjects(body.ToArray(), entityCount: 2, wallCount: 1));
    }

    [Fact]
    public void ReadObjects_SavesTheDosRuntimeWrote_HoldTheMovedThing()
    {
        // Guarded: the save-body probe's thing-a save (the thing's Y, Z and ANGLE set to 0.501,
        // 0.563, 0.626), when present locally.
        string? root = ResearchData.Directory;
        string path = root is null ? "" : Path.Combine(root, "oracle-runs", "OV-20261029-save-body", "round6", "thing-a", "work", "OVSAV2.SAV");
        if (!File.Exists(path))
        {
            return;
        }

        var saved = AcknexSaveFile.ReadObjects(AcknexSaveFile.Unpack(File.ReadAllBytes(path)), entityCount: 2, wallCount: 13);

        Assert.NotNull(saved);
        Assert.Equal(20 * 1024, saved.Value.Entities[0].RawSkills["X"]); // the actor first
        Assert.Equal(0x201, saved.Value.Entities[1].RawSkills["Y"]);
        Assert.Equal(0x241, saved.Value.Entities[1].RawSkills["Z"]);
        Assert.Equal(0x281, saved.Value.Entities[1].RawSkills["ANGLE"]);
    }

    [Fact]
    public void WriteFull_ReadsBackThroughTheReaders()
    {
        var save = new FullSave(
            "TITLE.WDL",
            "TITLE.WMP",
            "TITLE.WRS",
            [new FullSaveEntity(true, new Dictionary<string, int> { ["X"] = 25 * 1024, ["SKILL1"] = 7 }, 3)],
            [new FullSaveWall(new Dictionary<string, int> { ["OFFSET_X"] = 512 }, 8 * 1024, 32 * 1024, (30 * 1024, -4 * 1024), (30 * 1024, 4 * 1024), 0, 2)],
            [.. Enumerable.Repeat(-1, 139), 13],
            [-1, 14],
            [new SaveSkill("sb_n", 5 * 1024), .. AcknexPredefinedSkills.Names.Select(static n => new SaveSkill(n, 0))],
            [new SaveAction(14, 1024, 1, 2, -1, -1, Running: false)])
        {
            Regions = [new FullSaveRegion(new Dictionary<string, int> { ["FLOOR_HGT"] = -2048, ["SKILL1"] = 33 * 1024 }, 6, 7, SaveAll: false)],
        };

        byte[] body = AcknexFullSave.Write(save);

        // The header as the original writes it: the script's name over an older one's "…DL", and the
        // body's length + 10000 after the archive's name (OV-20261029-save-body round 13).
        Assert.Equal("TITLE.WDL\0DL\0"u8.ToArray(), body[4..17]);
        Assert.Equal(body.Length + 10000, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(43)));
        var objects = AcknexSaveFile.ReadObjects(body, entityCount: 1, wallCount: 1);
        Assert.NotNull(objects);
        Assert.Equal(25 * 1024, objects.Value.Entities[0].RawSkills["X"]);
        Assert.Equal(1024, objects.Value.Entities[0].RawSkills["CYCLE"]); // a 0 there does not load
        Assert.Equal(512, objects.Value.Walls[0].RawSkills["OFFSET_X"]);
        Assert.Equal([.. save.Actions, new SaveAction(11, 0, 0, 1, -1, -1, Running: true)], AcknexSaveFile.ReadActions(body));
        Assert.Equal(13, AcknexSaveFile.ReadSlots(body)![139]);
        Assert.Equal(239, AcknexSaveFile.ReadSkills(body).Count);
        Assert.Equal(5 * 1024, AcknexSaveFile.ReadSkills(body)[0].RawValue);
        Assert.Equal(238, AcknexPredefinedSkills.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        SaveObjectState region = Assert.Single(AcknexSaveFile.ReadRegions(body));
        Assert.Equal(-2048, region.RawSkills["FLOOR_HGT"]);
        Assert.Equal(33 * 1024, region.RawSkills["SKILL1"]);
        Assert.Equal([-1, 14], AcknexSaveFile.ReadSynonyms(body));
    }

    [Fact]
    public void WriteFull_UnplacedEventsAndAttach_LandWhereTheOriginalKeepsThem()
    {
        // OV-20261029-save-body round 13: unplaced objects first; the event block at +146 of an
        // entity (NEAR, FAR, EACH_CYCLE, HIT, EACH_TICK, ARRIVED) and 8 bytes later in a wall.
        var events = new Dictionary<string, int> { ["IF_NEAR"] = 11, ["IF_FAR"] = 12, ["IF_ARRIVED"] = 13, ["IF_HIT"] = 14, ["EACH_TICK"] = 15, ["EACH_CYCLE"] = 16 };
        var save = new FullSave(
            "T.WDL", "T.WMP", "T.WRS",
            [new FullSaveEntity(false, new Dictionary<string, int>(), 1) { Events = events, Attach = 2, MapColor = 0, Size = (2559, 2133), Region = 4 }],
            [new FullSaveWall(new Dictionary<string, int>(), 0, 0, (0, 0), (0, 0), 3, 1) { Events = events.Where(static e => e.Key != "IF_ARRIVED").ToDictionary(), Attach = 5, LeftRegion = 6, TextureStep = 65535 }],
            [], [], [], [])
        {
            Unplaced =
            [
                new FullSaveWall(new Dictionary<string, int>(), 0, 0, (0, 0), (0, 0), 0, 1) { Unplaced = true, OneRegion = true, Flags = ["INVISIBLE"] },
                new FullSaveEntity(false, new Dictionary<string, int>(), 1) { Unplaced = true, Flags = ["INVISIBLE"] },
            ],
        };

        byte[] body = AcknexFullSave.Write(save);

        Assert.Equal(4, AcknexSaveFile.ReadObjectRecords(body, [true, false, false, true])!.Count);
        int at = FindObjects(body);
        int Word(int offset) => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(at + offset));
        int Half(int offset) => System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(at + offset));
        Assert.Equal(0x42, Half(20)); // the unplaced wall: never seen
        Assert.Equal(0x300, Half(22));
        at += 178;
        Assert.Equal(0, Word(130)); // the unplaced thing
        at += 170;
        Assert.Equal([11, 12, 16, 14, 15, 13], [Word(146), Word(150), Word(154), Word(158), Word(162), Word(166)]);
        Assert.Equal(2, Word(138));
        Assert.Equal(-256, Half(92)); // MAP_COLOR 0
        Assert.Equal((2559, 2133), (Half(0), Half(4)));
        Assert.Equal(4, Word(126));
        Assert.Equal(-1, Word(130));
        at += 170;
        // A wall takes no IF_ARRIVED; its slot (+174) stays −1.
        Assert.Equal([5, 0, 11, 12, 16, 14, 15, -1], [Word(146), Word(150), Word(154), Word(158), Word(162), Word(166), Word(170), Word(174)]);
        Assert.Equal(6, Half(134));
        Assert.Equal(-1, Half(4)); // a 16-bit word: 65535 wraps
    }

    [Theory]
    [InlineData(false, new string[0], 0x607, 0x180)]
    [InlineData(true, new string[0], 0x627, 0x180)]
    [InlineData(false, new[] { "PASSABLE", "FLAG2", "FLAG6" }, 0x2707, 0x182)]
    [InlineData(true, new[] { "INVISIBLE" }, 0x67, 0x100)]
    [InlineData(true, new[] { "CAREFULLY" }, 0x62f, 0x180)] // apathy's floating TVs (save-body round 14)
    [InlineData(false, new[] { "IMMATERIAL", "LIBER", "GROUND" }, 0x617, 0x9c0)]
    public void WriteFull_EntityFlags_SetTheMeasuredBits(bool actor, string[] flags, int first, int second)
    {
        // OV-20261029-save-body round 12: each FLAGS declared alone on the original's objects.
        var save = new FullSave("T.WDL", "T.WMP", "T.WRS", [new FullSaveEntity(actor, new Dictionary<string, int>(), 1) { Flags = flags }], [],
            [], [], [], []);

        byte[] body = AcknexFullSave.Write(save);

        int record = FindObjects(body);
        Assert.Equal(first, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(record + 20)));
        Assert.Equal(second, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(record + 22)));
    }

    /// <summary>The object section's payload: the fourth section, after the header, three sections and their 8-byte heads.</summary>
    private static int FindObjects(byte[] body)
    {
        int at = 51; // the first section's own-offset field
        for (int section = 0; section < 3; section++)
        {
            at += 4 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(at - 4)) + 4;
        }

        return at + 4;
    }

    [Fact]
    public void ReadActions_TheIpProbesSave_ListsTheWaitingActionsThenTheSavingOne()
    {
        // Guarded: the save-body probe's round-8b save, eleven actions waiting in WAITT 100, when present locally.
        string? root = ResearchData.Directory;
        string path = root is null ? "" : Path.Combine(root, "oracle-runs", "OV-20261029-save-body", "round8b", "ip2", "work", "OVSAV2.SAV");
        if (!File.Exists(path))
        {
            return;
        }

        IReadOnlyList<SaveAction>? actions = AcknexSaveFile.ReadActions(AcknexSaveFile.Unpack(File.ReadAllBytes(path)));

        Assert.NotNull(actions);
        Assert.Equal(12, actions.Count);
        Assert.Equal(new SaveAction(11, 101312, 0, 0, -1, -1, Running: false), actions[0]); // WAITT 100 alone
        Assert.Equal(new SaveAction(12, 101312, 0, 1, -1, -1, Running: false), actions[1]); // WAITT 100, then more
        Assert.Equal(new SaveAction(20, 101312, 4, 0, -1, -1, Running: false), actions[9]); // after IF/ELSE
        Assert.True(actions[11].Running);
        Assert.Equal(22, actions[11].Action);
    }
}
