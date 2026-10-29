// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.Text;
using OpenVirtue.Formats.Compression;

namespace OpenVirtue.Formats.Save;

/// <summary>One skill as a save file holds it: its name and raw 22.10 fixed-point value, minimum and maximum.</summary>
public sealed record SaveSkill(string Name, int RawValue, int RawMin = SaveSkill.NoMin, int RawMax = SaveSkill.NoMax)
{
    /// <summary>The raw minimum of a skill without <c>MIN</c>: −2097151.999.</summary>
    public const int NoMin = -int.MaxValue;

    /// <summary>The raw maximum of a skill without <c>MAX</c>: 2097151.999.</summary>
    public const int NoMax = int.MaxValue;
}

/// <summary>One world object of a full save: its skills by name, as raw 22.10 fixed-point values.</summary>
public sealed record SaveObjectState(IReadOnlyDictionary<string, int> RawSkills);

/// <summary>
/// The save files the original writes with <c>SAVE</c> and <c>SAVE_INFO</c> (<c>NAME&lt;n&gt;.SAV</c>),
/// as measured in OV-20261029-save-format. A file is a WRS-style container — big-endian compressed
/// and uncompressed sizes, then the archive's LZSS stream (<see cref="Lzss"/>). The unpacked body is
/// little-endian: a type word, then (for <c>SAVE_INFO</c>) the body length and a run of sections,
/// each <c>[u32 size][u32 its own offset][size bytes]</c>, and the end marker <c>25 26 00 00</c>. A
/// skill section's payload is a count and that many records of name, NUL, raw value, raw minimum
/// and raw maximum. A <c>SAVE_INFO</c> body holds the <c>GLOBAL</c> and <c>LOCAL</c> skills, then an
/// empty section for the <c>FREEZE</c> bitmaps and one for the <c>INKEY</c> strings; a full
/// <c>SAVE</c> body starts with the world's file names and holds, among much else, a skill section
/// with the <c>GLOBAL</c> and <c>PLAYER</c> skills, the predefined ones included.
/// </summary>
/// <remarks>
/// Layout learned from black-box byte comparison of saves the patched DOS runtime wrote for
/// synthetic levels that differ in one value each (see <c>PROVENANCE.md</c>); not ported from any
/// third-party source.
/// </remarks>
public static class AcknexSaveFile
{
    /// <summary>The type word that opens a <c>SAVE_INFO</c> body.</summary>
    public const uint InfoType = 0x186;

    private const int SkillRecordTail = 12;

    private static readonly byte[] EndMarker = [0x25, 0x26, 0x00, 0x00];

    /// <summary>Unpacks a save file into its body.</summary>
    /// <exception cref="InvalidDataException">The container sizes do not fit the file.</exception>
    public static byte[] Unpack(ReadOnlySpan<byte> file)
    {
        if (file.Length < 8)
        {
            throw new InvalidDataException("A save file has an 8-byte size header.");
        }

        uint packed = BinaryPrimitives.ReadUInt32BigEndian(file);
        uint size = BinaryPrimitives.ReadUInt32BigEndian(file[4..]);
        if (packed > file.Length - 8 || size > 64 * 1024 * 1024)
        {
            throw new InvalidDataException($"Save sizes {packed}/{size} do not fit a {file.Length}-byte file.");
        }

        return Lzss.Decompress(file.Slice(8, (int)packed), (int)size);
    }

    /// <summary>Packs a body into a save file (a literal-only LZSS stream, which the original reads like any other).</summary>
    public static byte[] Pack(ReadOnlySpan<byte> body)
    {
        byte[] packed = Lzss.Compress(body);
        byte[] file = new byte[8 + packed.Length];
        BinaryPrimitives.WriteUInt32BigEndian(file, (uint)packed.Length);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), (uint)body.Length);
        packed.CopyTo(file.AsSpan(8));
        return file;
    }

    /// <summary>
    /// The body of a <c>SAVE_INFO</c> file with these skills (the original lists them in reverse
    /// declaration order), no <c>FREEZE</c> bitmaps and no <c>INKEY</c> strings.
    /// </summary>
    public static byte[] WriteInfo(IReadOnlyList<SaveSkill> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        using var body = new MemoryStream();
        WriteUInt32(body, InfoType);
        WriteUInt32(body, 0); // the body length, filled in below

        using var skillPayload = new MemoryStream();
        WriteUInt32(skillPayload, (uint)skills.Count);
        foreach (SaveSkill skill in skills)
        {
            skillPayload.Write(Encoding.Latin1.GetBytes(skill.Name));
            skillPayload.WriteByte(0);
            WriteUInt32(skillPayload, unchecked((uint)skill.RawValue));
            WriteUInt32(skillPayload, unchecked((uint)skill.RawMin));
            WriteUInt32(skillPayload, unchecked((uint)skill.RawMax));
        }

        WriteSection(body, skillPayload.ToArray());
        WriteSection(body, [0, 0, 0, 0]); // FREEZE bitmaps: none
        WriteSection(body, [0, 0, 0, 0]); // INKEY strings: none
        body.Write(EndMarker);
        byte[] bytes = body.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        return bytes;
    }

    /// <summary>
    /// The skills of a save body: those of the first section whose payload parses as skill records
    /// (the only one in a <c>SAVE_INFO</c> body; the <c>GLOBAL</c> and <c>PLAYER</c> skills of a full
    /// <c>SAVE</c>). Empty if there is none.
    /// </summary>
    public static IReadOnlyList<SaveSkill> ReadSkills(ReadOnlySpan<byte> body)
    {
        for (int offsetField = 4; offsetField + 8 <= body.Length; offsetField++)
        {
            // A section is [size][own offset][payload]: look for an offset field that names itself.
            if (BinaryPrimitives.ReadUInt32LittleEndian(body[offsetField..]) != offsetField)
            {
                continue;
            }

            uint size = BinaryPrimitives.ReadUInt32LittleEndian(body[(offsetField - 4)..]);
            int payload = offsetField + 4;
            if (size < 4 || size > body.Length - payload)
            {
                continue;
            }

            if (TryReadSkillRecords(body.Slice(payload, (int)size), out List<SaveSkill> skills) && skills.Count > 0)
            {
                return skills;
            }
        }

        return [];
    }

    /// <summary>The size of a thing's or actor's record in a full save's object section.</summary>
    public const int EntityRecordSize = 170;

    /// <summary>The size of a wall's record in a full save's object section.</summary>
    public const int WallRecordSize = 178;

    /// <summary>Where the skills of a thing's or actor's record lie (OV-20261029-save-body).</summary>
    private static readonly (string Name, int Offset)[] EntityFields =
    [
        ("ANGLE", 8), ("TARGET_X", 12), ("TARGET_Y", 16), ("HEIGHT", 36), ("SPEED", 40), ("VSPEED", 44), ("DIST", 52),
        ("SKILL1", 60), ("SKILL2", 64), ("SKILL3", 68), ("SKILL4", 72), ("SKILL5", 76), ("SKILL6", 80), ("SKILL7", 84),
        ("SKILL8", 88), ("CYCLE", 102), ("X", 110), ("Y", 114), ("Z", 118),
    ];

    /// <summary>Where the skills of a wall's record lie (OV-20261029-save-body).</summary>
    private static readonly (string Name, int Offset)[] WallFields =
    [
        ("OFFSET_X", 12), ("OFFSET_Y", 16), ("DIST", 52), ("SKILL1", 60), ("SKILL2", 64), ("SKILL3", 68), ("SKILL4", 72),
    ];

    /// <summary>
    /// The things, actors and walls of a full <c>SAVE</c> body. Its fourth section holds one record per
    /// thing and actor, then one per wall, each kind in reverse map order (the order the scheduler visits
    /// them). <paramref name="entityCount"/> and <paramref name="wallCount"/> are the level's counts;
    /// <c>null</c> when the body has no object section of that size.
    /// </summary>
    public static (IReadOnlyList<SaveObjectState> Entities, IReadOnlyList<SaveObjectState> Walls)? ReadObjects(
        ReadOnlySpan<byte> body,
        int entityCount,
        int wallCount)
    {
        if (Sections(body) is not { Count: > 3 } sections)
        {
            return null;
        }

        (int payload, int size) = sections[3];
        if (size != (entityCount * EntityRecordSize) + (wallCount * WallRecordSize))
        {
            return null;
        }

        ReadOnlySpan<byte> objects = body.Slice(payload, size);
        var entities = new List<SaveObjectState>(entityCount);
        for (int i = 0; i < entityCount; i++)
        {
            entities.Add(ReadRecord(objects.Slice(i * EntityRecordSize, EntityRecordSize), EntityFields));
        }

        var walls = new List<SaveObjectState>(wallCount);
        int wallsStart = entityCount * EntityRecordSize;
        for (int i = 0; i < wallCount; i++)
        {
            walls.Add(ReadRecord(objects.Slice(wallsStart + (i * WallRecordSize), WallRecordSize), WallFields));
        }

        return (entities, walls);
    }

    private static SaveObjectState ReadRecord(ReadOnlySpan<byte> record, (string Name, int Offset)[] fields)
    {
        var skills = new Dictionary<string, int>(fields.Length, StringComparer.OrdinalIgnoreCase);
        foreach ((string name, int offset) in fields)
        {
            skills[name] = BinaryPrimitives.ReadInt32LittleEndian(record[offset..]);
        }

        return new SaveObjectState(skills);
    }

    /// <summary>
    /// The sections of a body in order, as (payload offset, size): the chain that starts at the first
    /// section whose offset field names itself, each next section following the last one's payload.
    /// </summary>
    private static List<(int Payload, int Size)>? Sections(ReadOnlySpan<byte> body)
    {
        for (int offsetField = 4; offsetField + 4 <= body.Length; offsetField++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(body[offsetField..]) != offsetField)
            {
                continue;
            }

            var sections = new List<(int, int)>();
            int field = offsetField;
            while (field + 4 <= body.Length && BinaryPrimitives.ReadUInt32LittleEndian(body[field..]) == field)
            {
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(body[(field - 4)..]);
                if (size > body.Length - field - 4)
                {
                    break;
                }

                sections.Add((field + 4, (int)size));
                field += 4 + (int)size + 4;
            }

            if (sections.Count > 1)
            {
                return sections;
            }
        }

        return null;
    }

    private static bool TryReadSkillRecords(ReadOnlySpan<byte> payload, out List<SaveSkill> skills)
    {
        skills = [];
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        int position = 4;
        for (uint i = 0; i < count; i++)
        {
            int end = payload[position..].IndexOf((byte)0);
            if (end <= 0 || position + end + 1 + SkillRecordTail > payload.Length)
            {
                return false;
            }

            string name = Encoding.Latin1.GetString(payload.Slice(position, end));
            if (!IsSkillName(name))
            {
                return false;
            }

            position += end + 1;
            skills.Add(new SaveSkill(
                name,
                BinaryPrimitives.ReadInt32LittleEndian(payload[position..]),
                BinaryPrimitives.ReadInt32LittleEndian(payload[(position + 4)..]),
                BinaryPrimitives.ReadInt32LittleEndian(payload[(position + 8)..])));
            position += SkillRecordTail;
        }

        return position == payload.Length;
    }

    private static bool IsSkillName(string name) =>
        name.Length > 0 && name.All(static c => c == '_' || char.IsAsciiLetterOrDigit(c)) && !char.IsAsciiDigit(name[0]);

    private static void WriteSection(Stream body, byte[] payload)
    {
        WriteUInt32(body, (uint)payload.Length);
        WriteUInt32(body, (uint)body.Position);
        body.Write(payload);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
