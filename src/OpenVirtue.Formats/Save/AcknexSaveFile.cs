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
