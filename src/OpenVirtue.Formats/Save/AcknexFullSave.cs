// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.Text;

namespace OpenVirtue.Formats.Save;

/// <summary>
/// A thing or actor as a full save's object section holds it (OV-20261029-save-body). It carries its
/// skills at their offsets (see <see cref="AcknexSaveFile.ReadObjects"/>), whether it is an actor, and
/// its texture's number (from 1, in declaration order).
/// </summary>
public sealed record FullSaveEntity(bool Actor, IReadOnlyDictionary<string, int> RawSkills, int Texture)
{
    /// <summary>The object's set <c>FLAGS</c>, which the record's two flag words encode (round 12).</summary>
    public IReadOnlyCollection<string> Flags { get; init; } = [];
}

/// <summary>
/// A wall as a full save's object section holds it (OV-20261029-save-body). It carries its skills, its
/// length and the texture's span across it (raw), its vertices (raw), its right region and its
/// texture's number.
/// </summary>
public sealed record FullSaveWall(
    IReadOnlyDictionary<string, int> RawSkills,
    int RawLength,
    int RawSpan,
    (int X, int Y) First,
    (int X, int Y) Second,
    int RightRegion,
    int Texture)
{
    /// <summary>The wall's set <c>FLAGS</c>, which the record's two flag words encode (round 12).</summary>
    public IReadOnlyCollection<string> Flags { get; init; } = [];

    /// <summary>Whether one region lies on both sides (its second flag word then holds <c>0x200</c>).</summary>
    public bool OneRegion { get; init; }
}

/// <summary>
/// A region with the <c>SAVE</c> flag, as a full save's third section holds it (OV-20261029-save-body
/// round 10). It carries its skills by name (<see cref="AcknexFullSave.RegionFieldOffsets"/>), its floor
/// and ceiling textures' numbers, and whether it is <c>SAVE_ALL</c>.
/// </summary>
public sealed record FullSaveRegion(IReadOnlyDictionary<string, int> RawSkills, int FloorTexture, int CeilingTexture, bool SaveAll);

/// <summary>What a full save holds, as <see cref="AcknexFullSave.Write"/> lays it out.</summary>
/// <param name="WdlName">The level's main WDL file name (up to 12 characters).</param>
/// <param name="WmpName">Its map's file name.</param>
/// <param name="WrsName">Its archive's file name.</param>
/// <param name="Entities">Actors, then things, each in reverse map order.</param>
/// <param name="Walls">Walls in reverse map order.</param>
/// <param name="Slots">The 168 slot words (action numbers, −1 for none).</param>
/// <param name="Synonyms">The synonyms' objects (map place + 1, −1 for none), in reverse declaration order, the predefined ones last.</param>
/// <param name="Skills">The level's non-<c>LOCAL</c> skills and <see cref="AcknexPredefinedSkills"/>.</param>
/// <param name="Actions">The waiting actions.</param>
public sealed record FullSave(
    string WdlName,
    string WmpName,
    string WrsName,
    IReadOnlyList<FullSaveEntity> Entities,
    IReadOnlyList<FullSaveWall> Walls,
    IReadOnlyList<int> Slots,
    IReadOnlyList<int> Synonyms,
    IReadOnlyList<SaveSkill> Skills,
    IReadOnlyList<SaveAction> Actions)
{
    /// <summary>The <c>SAVE</c> regions, in reverse map order.</summary>
    public IReadOnlyList<FullSaveRegion> Regions { get; init; } = [];
}

/// <summary>
/// Writes full <c>SAVE</c> bodies the original loads (OV-20261029-save-body rounds 7 to 9). What the
/// original needs was found by blanking parts of its own saves; the rest is written as its saves
/// hold it in a fresh level, or left empty where it loaded without it:
/// <list type="bullet">
/// <item>the 80-byte first section (zeros);</item>
/// <item>the 200-byte fifth one;</item>
/// <item>the records' pointer-like words (zeros);</item>
/// <item>view bits in the flags;</item>
/// <item>the walls' vertices, which a load recomputes but are written all the same.</item>
/// </list>
/// </summary>
/// <remarks>Layout from the byte analysis in <c>PROVENANCE.md</c>; not ported from any third-party source.</remarks>
public static class AcknexFullSave
{
    /// <summary>The type word that opens a full <c>SAVE</c> body.</summary>
    public const uint FullType = 0x69a;

    /// <summary>The number of synonyms the engine declares itself (they follow the level's).</summary>
    public const int PredefinedSynonyms = 21;

    /// <summary>The words of the slot section.</summary>
    public const int SlotWords = 168;

    private const int NameField = 13;

    /// <summary>The number of the first declared action.</summary>
    private const int FirstAction = 11;

    /// <summary>The body of a full save.</summary>
    public static byte[] Write(FullSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        using var body = new MemoryStream();
        AcknexSaveFile.WriteUInt32(body, FullType);
        WriteName(body, save.WdlName);
        WriteName(body, save.WmpName);
        WriteName(body, save.WrsName);
        AcknexSaveFile.WriteUInt32(body, 0);

        AcknexSaveFile.WriteSection(body, new byte[80]);
        AcknexSaveFile.WriteSection(body, []);
        AcknexSaveFile.WriteSection(body, Regions(save.Regions));
        AcknexSaveFile.WriteSection(body, Objects(save));
        AcknexSaveFile.WriteSection(body, Words([0, .. Enumerable.Repeat(-1, 49)]));
        AcknexSaveFile.WriteSection(body, new byte[4]);
        AcknexSaveFile.WriteSection(body, Words(Fit(save.Slots, SlotWords)));
        AcknexSaveFile.WriteSection(body, Words(save.Synonyms));
        AcknexSaveFile.WriteSection(body, Skills(save.Skills));
        AcknexSaveFile.WriteSection(body, Actions(save.Actions));
        AcknexSaveFile.WriteSection(body, new byte[4]);
        AcknexSaveFile.WriteSection(body, new byte[4]);
        AcknexSaveFile.WriteSection(body, []);
        body.Write([0x25, 0x26, 0x00, 0x00]);
        return body.ToArray();
    }

    private static void WriteName(Stream body, string name)
    {
        byte[] field = new byte[NameField];
        byte[] text = Encoding.Latin1.GetBytes(name); // as named: apathy's save holds "apathy.WMP"
        text.AsSpan(0, Math.Min(text.Length, NameField - 1)).CopyTo(field);
        body.Write(field);
    }

    private static byte[] Objects(FullSave save)
    {
        byte[] bytes = new byte[(save.Entities.Count * AcknexSaveFile.EntityRecordSize) + (save.Walls.Count * AcknexSaveFile.WallRecordSize)];
        Span<byte> span = bytes;
        for (int i = 0; i < save.Entities.Count; i++)
        {
            WriteEntity(span.Slice(i * AcknexSaveFile.EntityRecordSize, AcknexSaveFile.EntityRecordSize), save.Entities[i]);
        }

        int wallsStart = save.Entities.Count * AcknexSaveFile.EntityRecordSize;
        for (int i = 0; i < save.Walls.Count; i++)
        {
            WriteWall(span.Slice(wallsStart + (i * AcknexSaveFile.WallRecordSize), AcknexSaveFile.WallRecordSize), save.Walls[i]);
        }

        return bytes;
    }

    /// <summary>The size of a <c>SAVE</c> region's record.</summary>
    public const int RegionRecordSize = 156;

    /// <summary>Where a region record holds each skill (OV-20261029-save-body round 10).</summary>
    public static IReadOnlyList<(string Name, int Offset)> RegionFieldOffsets { get; } =
    [
        ("FLOOR_HGT", 0), ("CEIL_HGT", 4), ("CEIL_OFFS_X", 8), ("CEIL_OFFS_Y", 12), ("FLOOR_OFFS_X", 16), ("FLOOR_OFFS_Y", 20),
        ("AMBIENT", 24), ("SKILL1", 68), ("SKILL2", 72), ("SKILL3", 76), ("SKILL4", 80), ("SKILL5", 84), ("SKILL6", 88),
        ("SKILL7", 92), ("SKILL8", 96),
    ];

    private static byte[] Regions(IReadOnlyList<FullSaveRegion> regions)
    {
        byte[] bytes = new byte[regions.Count * RegionRecordSize];
        for (int i = 0; i < regions.Count; i++)
        {
            Span<byte> record = bytes.AsSpan(i * RegionRecordSize, RegionRecordSize);
            Int16(record, 30, 3);
            Int16(record, 32, 0x702);
            Int16(record, 34, regions[i].SaveAll ? 0x2f00 : 0xf00);
            Int32(record, 36, 1024);
            Int32(record, 44, 1024);
            Int32(record, 52, 1024);
            Int32(record, 60, 1024);
            Int32(record, 100, regions[i].FloorTexture);
            Int32(record, 104, regions[i].CeilingTexture);
            Fill(record, 108, RegionRecordSize);
            Skills(record, RegionFieldOffsets, regions[i].RawSkills);
        }

        return bytes;
    }

    /// <summary>
    /// The bits <c>FLAGS</c> set in a thing's or actor's two flag words (+20, +22), found by declaring one
    /// flag at a time (OV-20261029-save-body round 12). <c>INVISIBLE</c> also clears the visibility bits.
    /// </summary>
    private static readonly Dictionary<string, (int First, int Second)> EntityFlagBits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GROUND"] = (0x10, 0), ["FAR"] = (0x80, 0), ["PASSABLE"] = (0x100, 0), ["BERKELEY"] = (0x800, 0),
        ["FLAG1"] = (0x1000, 0), ["FLAG2"] = (0x2000, 0), ["FLAG3"] = (0x4000, 0), ["FLAG4"] = (0x8000, 0),
        ["FLAG5"] = (0, 0x1), ["FLAG6"] = (0, 0x2), ["FLAG7"] = (0, 0x4), ["FLAG8"] = (0, 0x8),
        ["SENSITIVE"] = (0, 0x10), ["FRAGILE"] = (0, 0x20), ["LIBER"] = (0, 0x40), ["CANDELABER"] = (0, 0x400),
        ["IMMATERIAL"] = (0, 0x800),
    };

    /// <summary>The same for walls (round 12). <c>FENCE</c> also clears <c>0x200</c>, <c>INVISIBLE</c> <c>0x400</c>.</summary>
    private static readonly Dictionary<string, (int First, int Second)> WallFlagBits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TRANSPARENT"] = (0x4, 0), ["PORTCULLIS"] = (0x10, 0), ["FAR"] = (0x80, 0), ["PASSABLE"] = (0x100, 0),
        ["BERKELEY"] = (0x800, 0),
        ["FLAG1"] = (0x1000, 0), ["FLAG2"] = (0x2000, 0), ["FLAG3"] = (0x4000, 0), ["FLAG4"] = (0x8000, 0),
        ["FLAG5"] = (0, 0x1), ["FLAG6"] = (0, 0x2), ["FLAG7"] = (0, 0x4), ["FLAG8"] = (0, 0x8),
        ["SENSITIVE"] = (0, 0x10), ["FRAGILE"] = (0, 0x20), ["FENCE"] = (0, 0x400), ["IMMATERIAL"] = (0, 0x800),
        ["IMPASSABLE"] = (0, 0x2000),
    };

    private static (int First, int Second) FlagWords(IReadOnlyCollection<string> flags, Dictionary<string, (int, int)> bits, int first, int second)
    {
        foreach (string flag in flags)
        {
            if (bits.TryGetValue(flag, out (int First, int Second) bit))
            {
                first |= bit.First;
                second |= bit.Second;
            }
        }

        return (first, second);
    }

    private static void WriteEntity(Span<byte> record, FullSaveEntity entity)
    {
        bool Has(string flag) => entity.Flags.Contains(flag, StringComparer.OrdinalIgnoreCase);
        (int first, int second) = FlagWords(entity.Flags, EntityFlagBits, entity.Actor ? 0x627 : 0x607, 0x180);
        if (Has("INVISIBLE"))
        {
            first = (first & ~0x600) | 0x40;
            second &= ~0x80;
        }

        Int16(record, 0, 16383);
        Int16(record, 4, 16383);
        Int16(record, 20, first);
        Int16(record, 22, second);
        if (Has("SENSITIVE"))
        {
            Int16(record, 24, 8);
        }

        Int16(record, 26, 5);
        Int16(record, 92, -255);
        Int32(record, 102, 1024);
        Fill(record, 130, 134);
        Int16(record, 134, entity.Texture);
        Fill(record, 138, 142);
        Fill(record, 146, 170);
        Skills(record, AcknexSaveFile.EntityFieldOffsets, entity.RawSkills);
        if (BinaryPrimitives.ReadInt32LittleEndian(record[102..]) == 0)
        {
            Int32(record, 102, 1024); // CYCLE: a 0 here does not load
        }
    }

    private static void WriteWall(Span<byte> record, FullSaveWall wall)
    {
        Int32(record, 0, wall.RawSpan);
        Int16(record, 4, 16383);
        Int32(record, 8, wall.RawLength);
        bool Has(string flag) => wall.Flags.Contains(flag, StringComparer.OrdinalIgnoreCase);
        (int first, int second) = FlagWords(wall.Flags, WallFlagBits, 0x602, wall.OneRegion ? 0x380 : 0x180);
        if (Has("INVISIBLE"))
        {
            first = (first & ~0x400) | 0x40;
        }

        if (Has("FENCE"))
        {
            second &= ~0x200;
        }

        Int16(record, 20, first);
        Int16(record, 22, second);
        if (Has("SENSITIVE"))
        {
            Int16(record, 24, 8);
        }

        Int16(record, 26, 5);
        Int16(record, 92, -255);
        Int32(record, 102, 1024);
        Int32(record, 110, wall.First.X);
        Int32(record, 114, wall.First.Y);
        Int32(record, 122, wall.Second.X);
        Int32(record, 126, wall.Second.Y);
        Int16(record, 138, wall.RightRegion);
        Int16(record, 142, wall.Texture);
        Fill(record, 146, 178);
        Skills(record, AcknexSaveFile.WallFieldOffsets, wall.RawSkills);
    }

    private static void Skills(Span<byte> record, IReadOnlyList<(string Name, int Offset)> fields, IReadOnlyDictionary<string, int> skills)
    {
        foreach ((string name, int offset) in fields)
        {
            if (skills.TryGetValue(name, out int raw))
            {
                Int32(record, offset, raw);
            }
        }
    }

    private static byte[] Skills(IReadOnlyList<SaveSkill> skills)
    {
        using var payload = new MemoryStream();
        AcknexSaveFile.WriteUInt32(payload, (uint)skills.Count);
        foreach (SaveSkill skill in skills)
        {
            payload.Write(Encoding.Latin1.GetBytes(skill.Name));
            payload.WriteByte(0);
            AcknexSaveFile.WriteUInt32(payload, unchecked((uint)skill.RawValue));
            AcknexSaveFile.WriteUInt32(payload, unchecked((uint)skill.RawMin));
            AcknexSaveFile.WriteUInt32(payload, unchecked((uint)skill.RawMax));
        }

        return payload.ToArray();
    }

    private static byte[] Actions(IReadOnlyList<SaveAction> actions)
    {
        List<SaveAction> waiting = [.. actions.Where(static a => !a.Running)];
        var words = new List<int> { 0, 0, waiting.Count };
        foreach (SaveAction action in waiting)
        {
            words.AddRange([action.Action, action.Remaining, action.Position, action.Next, action.My, action.You]);
        }

        // The action running the SAVE: a load needs its record but does not resume it, and any action
        // at any place does (OV-20261029-save-body round 9).
        List<SaveAction> running = [.. actions.Where(static a => a.Running)];
        if (running.Count == 0)
        {
            running.Add(new SaveAction(FirstAction, 0, 0, 1, -1, -1, Running: true));
        }

        foreach (SaveAction action in running)
        {
            words.AddRange([1, action.Action, action.Remaining, action.Position, action.Next, action.My, action.You]);
        }

        words.Add(0);
        return Words(words);
    }

    private static int[] Fit(IReadOnlyList<int> words, int count) =>
        [.. Enumerable.Range(0, count).Select(i => i < words.Count ? words[i] : -1)];

    private static byte[] Words(IReadOnlyList<int> words)
    {
        byte[] bytes = new byte[words.Count * 4];
        for (int i = 0; i < words.Count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), words[i]);
        }

        return bytes;
    }

    private static void Int16(Span<byte> record, int offset, int value) =>
        BinaryPrimitives.WriteInt16LittleEndian(record[offset..], unchecked((short)value));

    private static void Int32(Span<byte> record, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(record[offset..], value);

    private static void Fill(Span<byte> record, int from, int to) => record[from..to].Fill(0xff);
}
