// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Buffers.Binary;
using System.Text;

namespace OpenVirtue.Formats.Save;

/// <summary>A record of a full save's object section: a <see cref="FullSaveEntity"/> or a <see cref="FullSaveWall"/>.</summary>
public abstract record FullSaveObject
{
    /// <summary>The object's set <c>FLAGS</c>, which the record's two flag words encode (round 12).</summary>
    public IReadOnlyCollection<string> Flags { get; init; } = [];

    /// <summary>The number of its <c>ATTACH</c> texture, −1 for none (round 13).</summary>
    public int Attach { get; init; } = -1;

    /// <summary>
    /// Its event actions' numbers by event (<see cref="AcknexFullSave.EventNames"/>); an event left out
    /// has none (round 13).
    /// </summary>
    public IReadOnlyDictionary<string, int> Events { get; init; } = new Dictionary<string, int>();

    /// <summary>Its <c>MAP_COLOR</c> (round 13).</summary>
    public int MapColor { get; init; } = 1;

    /// <summary>
    /// Whether the WDL declares it but the map does not place it (round 13): such a wall has never been
    /// seen (its view bits are clear), and such a thing holds 0 where other things hold −1 (+130).
    /// </summary>
    public bool Unplaced { get; init; }
}

/// <summary>
/// A thing or actor as a full save's object section holds it (OV-20261029-save-body). It carries its
/// skills at their offsets (see <see cref="AcknexSaveFile.ReadObjects"/>), whether it is an actor, and
/// its texture's number (from 1, in declaration order).
/// </summary>
public sealed record FullSaveEntity(bool Actor, IReadOnlyDictionary<string, int> RawSkills, int Texture) : FullSaveObject
{
    /// <summary>
    /// Its texture's width and height as (pixels·1024 − 1) / SCALE, 16383 without a texture (round 13).
    /// </summary>
    public (int Width, int Height) Size { get; init; } = (16383, 16383);

    /// <summary>The region it is in (round 13).</summary>
    public int Region { get; init; }

    /// <summary>The floor's height under it (raw, round 13).</summary>
    public int Floor { get; init; }
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
    int Texture) : FullSaveObject
{
    /// <summary>Whether one region lies on both sides (its second flag word then holds <c>0x200</c>).</summary>
    public bool OneRegion { get; init; }

    /// <summary>The region on its left (round 13).</summary>
    public int LeftRegion { get; init; }

    /// <summary>Its vertices' heights (raw, round 13).</summary>
    public (int First, int Second) Heights { get; init; }

    /// <summary>Its texture's height as (pixels·1024 − 1) / SCALE_Y, 16383 without a texture (round 13).</summary>
    public int TextureStep { get; init; } = 16383;
}

/// <summary>
/// A region with the <c>SAVE</c> flag, as a full save's third section holds it (OV-20261029-save-body
/// round 10). It carries its skills by name (<see cref="AcknexFullSave.RegionFieldOffsets"/>), its floor
/// and ceiling textures' numbers, and whether it is <c>SAVE_ALL</c>.
/// </summary>
public sealed record FullSaveRegion(IReadOnlyDictionary<string, int> RawSkills, int FloorTexture, int CeilingTexture, bool SaveAll)
{
    /// <summary>Its <c>FLAGS</c> by name (round 15); <c>SAVE</c> and <c>SAVE_ALL</c> are written regardless.</summary>
    public IReadOnlyCollection<string> Flags { get; init; } = [];

    /// <summary>
    /// Whether the player is in it, which sets the runtime bits a region out of sight lacks: the
    /// probe's room, the player's, saves <c>0x702</c>/<c>0xF00</c>, every SAVE region of a retail save
    /// <c>0x2</c>/<c>0x900</c> (round 15). Whether a region merely in view sets them is unmeasured.
    /// </summary>
    public bool Here { get; init; }

    /// <summary>Its event actions' numbers by event (<see cref="AcknexFullSave.RegionEventNames"/>; round 15).</summary>
    public IReadOnlyDictionary<string, int> Events { get; init; } = new Dictionary<string, int>();

    /// <summary>Its <c>GENIUS</c> object's number, counting the map's walls, things and actors from 1; −1 for none (round 15).</summary>
    public int Genius { get; init; } = -1;
}

/// <summary>
/// A texture with the <c>SAVE</c> flag, as a full save's second section holds it (OV-20261029-save-body
/// round 15b): its words by name (<see cref="AcknexFullSave.TextureWordOffsets"/>), raw values and
/// numbers alike. A reference left out reads −1.
/// </summary>
public sealed record FullSaveTexture(IReadOnlyDictionary<string, int> Words);

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
    /// <summary>The <c>SAVE</c> textures, newest declaration first (round 15b).</summary>
    public IReadOnlyList<FullSaveTexture> Textures { get; init; } = [];

    /// <summary>The <c>SAVE</c> regions, in reverse map order.</summary>
    public IReadOnlyList<FullSaveRegion> Regions { get; init; } = [];

    /// <summary>
    /// The walls, things and actors the WDL declares but the map does not place, in reverse declaration
    /// order; they open the object section (round 13).
    /// </summary>
    public IReadOnlyList<FullSaveObject> Unplaced { get; init; } = [];

    /// <summary>The player's region, which the first section holds twice (+60, +64; round 13).</summary>
    public int PlayerRegion { get; init; }

    /// <summary>
    /// The palette in use, numbered from 1 in declaration order, which the first section holds twice
    /// (+48, +56). Left 0 on a retail level, the original reports two runtime errors after loading
    /// (round 13).
    /// </summary>
    public int Palette { get; init; }
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

    /// <summary>
    /// The slot section's word for each key hook: the key's PC scan code (US layout: <c>IF_Y</c> 21,
    /// <c>IF_Z</c> 44), then the mouse buttons and <c>IF_ANYKEY</c> from 128 (OV-20261029-save-body
    /// round 16). <c>IF_BRACKR</c> shares 27 with <c>IF_PLUS</c>: set first, it was overwritten there.
    /// </summary>
    public static IReadOnlyDictionary<string, int> KeySlotWords { get; } = BuildKeySlotWords();

    /// <summary>The slot section's word for <c>EACH_SEC.n</c>: 134 + 2n, between the <c>EACH_TICK</c> words (round 16).</summary>
    public static int EachSecSlotWord(int slot) => 134 + (2 * slot);

    private static Dictionary<string, int> BuildKeySlotWords()
    {
        var words = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["IF_ESC"] = 1, ["IF_0"] = 11, ["IF_SZ"] = 12, ["IF_APO"] = 13, ["IF_BKSP"] = 14, ["IF_TAB"] = 15,
            ["IF_BRACKL"] = 26, ["IF_BRACKR"] = 27, ["IF_PLUS"] = 27, ["IF_ENTER"] = 28, ["IF_CTRL"] = 29,
            ["IF_CAL"] = 51, ["IF_CAR"] = 52, ["IF_MINUS"] = 53, ["IF_ALT"] = 56, ["IF_SPACE"] = 57,
            ["IF_PAUSE"] = 69, ["IF_HOME"] = 71, ["IF_CUU"] = 72, ["IF_PGUP"] = 73, ["IF_CUL"] = 75, ["IF_CUR"] = 77,
            ["IF_END"] = 79, ["IF_CUD"] = 80, ["IF_PGDN"] = 81, ["IF_INS"] = 82, ["IF_DEL"] = 83, ["IF_F11"] = 87, ["IF_F12"] = 88,
            ["IF_LEFT"] = 128, ["IF_MIDDLE"] = 129, ["IF_RIGHT"] = 130, ["IF_ANYKEY"] = 131,
        };
        for (int digit = 1; digit <= 9; digit++)
        {
            words["IF_" + digit.ToString(System.Globalization.CultureInfo.InvariantCulture)] = digit + 1;
        }

        for (int key = 1; key <= 10; key++)
        {
            words["IF_F" + key.ToString(System.Globalization.CultureInfo.InvariantCulture)] = 58 + key;
        }

        foreach ((string row, int first) in new[] { ("QWERTYUIOP", 16), ("ASDFGHJKL", 30), ("ZXCVBNM", 44) })
        {
            for (int i = 0; i < row.Length; i++)
            {
                words["IF_" + row[i]] = first + i;
            }
        }

        return words;
    }

    private const int NameField = 13;

    /// <summary>The number of the first declared action.</summary>
    private const int FirstAction = 11;

    /// <summary>The body of a full save.</summary>
    public static byte[] Write(FullSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        using var body = new MemoryStream();
        AcknexSaveFile.WriteUInt32(body, FullType);
        WriteName(body, save.WdlName, scriptField: true);
        WriteName(body, save.WmpName);
        WriteName(body, save.WrsName);
        long lengthWord = body.Position;
        AcknexSaveFile.WriteUInt32(body, 0); // the body's length + 10000, filled in below

        // The first section as the original's saves hold it; the words at +36 and +40, which change
        // from save to save, are left 0, which loads (round 13).
        byte[] first = new byte[80];
        Int32(first, 32, 1);
        Int32(first, 48, save.Palette);
        Int32(first, 52, 1024);
        Int32(first, 56, save.Palette);
        Int32(first, 60, save.PlayerRegion);
        Int32(first, 64, save.PlayerRegion);
        Int32(first, 68, -1);
        AcknexSaveFile.WriteSection(body, first);
        AcknexSaveFile.WriteSection(body, Textures(save.Textures));
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
        // The header's last word is the body's length plus 10000 in every save the original wrote
        // (8763 → 18763, 1187701 → 1197701; round 13).
        byte[] bytes = body.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan((int)lengthWord), bytes.Length + 10000);
        return bytes;
    }

    private static void WriteName(Stream body, string name, bool scriptField = false)
    {
        byte[] field = new byte[NameField];
        if (scriptField)
        {
            // The script's field keeps the end of a 12-character name it held before ("…DL"), which
            // shows past a shorter name: TITLE.WDL, NUL, "DL", NUL (OV-20261029-save-body round 13).
            field[10] = (byte)'D';
            field[11] = (byte)'L';
        }

        byte[] text = Encoding.Latin1.GetBytes(name); // as named: apathy's save holds "apathy.WMP"
        int length = Math.Min(text.Length, NameField - 1);
        text.AsSpan(0, length).CopyTo(field);
        field[length] = 0;
        body.Write(field);
    }

    private static byte[] Objects(FullSave save)
    {
        FullSaveObject[] records = [.. save.Unplaced, .. save.Entities, .. save.Walls];
        byte[] bytes = new byte[records.Sum(static r => r is FullSaveWall ? AcknexSaveFile.WallRecordSize : AcknexSaveFile.EntityRecordSize)];
        int at = 0;
        foreach (FullSaveObject record in records)
        {
            if (record is FullSaveWall wall)
            {
                WriteWall(bytes.AsSpan(at, AcknexSaveFile.WallRecordSize), wall);
                at += AcknexSaveFile.WallRecordSize;
            }
            else
            {
                WriteEntity(bytes.AsSpan(at, AcknexSaveFile.EntityRecordSize), (FullSaveEntity)record);
                at += AcknexSaveFile.EntityRecordSize;
            }
        }

        return bytes;
    }

    /// <summary>The events an object record holds, in <see cref="FullSaveObject.Events"/>'s keys.</summary>
    public static IReadOnlyList<string> EventNames { get; } = ["IF_NEAR", "IF_FAR", "IF_ARRIVED", "IF_HIT", "EACH_TICK", "EACH_CYCLE"];

    /// <summary>The events a region record holds, in the order of their slots from +128 (round 15).</summary>
    public static IReadOnlyList<string> RegionEventNames { get; } = ["IF_ENTER", "IF_LEAVE", "IF_DIVE", "IF_ARISE", "EACH_TICK", "EACH_CYCLE"];

    /// <summary>
    /// The bits <c>FLAGS</c> set in a region record's two flag words (+32, +34), found by declaring one
    /// flag at a time (round 15). A slope flag also clears a runtime bit of the player's region:
    /// <c>FLOOR_*</c> <c>0x200</c>, <c>CEIL_*</c> <c>0x400</c>. V3.963 rejects <c>SEEN</c> and <c>PLAY</c> on a region.
    /// </summary>
    private static readonly Dictionary<string, (int First, int Second)> RegionFlagBits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BASE"] = (0x10, 0), ["STICKY"] = (0x800, 0),
        ["FLAG1"] = (0x1000, 0), ["FLAG2"] = (0x2000, 0), ["FLAG3"] = (0x4000, 0), ["FLAG4"] = (0x8000, 0),
        ["FLAG5"] = (0, 0x1), ["FLAG6"] = (0, 0x2), ["FLAG7"] = (0, 0x4), ["FLAG8"] = (0, 0x8),
        ["FLOOR_ASCEND"] = (0, 0x10), ["FLOOR_DESCEND"] = (0, 0x20), ["CEIL_ASCEND"] = (0, 0x40), ["CEIL_DESCEND"] = (0, 0x80),
    };

    /// <summary>Where a thing's or actor's record holds each event's action (round 13); a wall's are 8 bytes on.</summary>
    private static readonly Dictionary<string, int> EntityEventOffsets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IF_NEAR"] = 146, ["IF_FAR"] = 150, ["EACH_CYCLE"] = 154, ["IF_HIT"] = 158, ["EACH_TICK"] = 162, ["IF_ARRIVED"] = 166,
    };

    private static void WriteEvents(Span<byte> record, FullSaveObject obj, int shift)
    {
        foreach ((string name, int action) in obj.Events)
        {
            if (EntityEventOffsets.TryGetValue(name, out int offset))
            {
                Int32(record, offset + shift, action);
            }
        }
    }

    // MAP_COLOR is the low byte of the word at +92; the high byte holds 0xFF (round 13).
    private static void MapColor(Span<byte> record, int mapColor) => Int16(record, 92, unchecked((short)(0xFF00 | (mapColor & 0xFF))));

    /// <summary>The size of a <c>SAVE</c> texture's record.</summary>
    public const int TextureRecordSize = 68;

    /// <summary>
    /// Where a texture record holds each word (round 15b): values raw, the font, touch string and sound
    /// by their number among their kind's declarations, the events by action number, <c>ATTACH</c> by
    /// texture number. <c>RADIANCE</c> and <c>RANDOM</c> are not saved; +36 reads 0.
    /// </summary>
    public static IReadOnlyList<(string Name, int Offset)> TextureWordOffsets { get; } =
    [
        ("AMBIENT", 0), ("ALBEDO", 4), ("SCALE_X", 8), ("SCALE_Y", 12), ("FONT", 16), ("IF_TOUCH", 20), ("IF_RELEASE", 24),
        ("IF_KLICK", 28), ("TOUCH", 32), ("ATTACH", 40), ("SOUND", 44), ("SVOL", 48), ("SDIST", 52), ("SVDIST", 56),
        ("POS_X", 60), ("POS_Y", 64),
    ];

    private static byte[] Textures(IReadOnlyList<FullSaveTexture> textures)
    {
        byte[] bytes = new byte[textures.Count * TextureRecordSize];
        for (int i = 0; i < textures.Count; i++)
        {
            Span<byte> record = bytes.AsSpan(i * TextureRecordSize, TextureRecordSize);
            Fill(record, 16, 36);
            Fill(record, 40, 48);
            Skills(record, TextureWordOffsets, textures[i].Words);
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
            FullSaveRegion region = regions[i];
            int here = 0x600;
            if (region.Flags.Any(static f => f.StartsWith("FLOOR_", StringComparison.OrdinalIgnoreCase)))
            {
                here &= ~0x200;
            }

            if (region.Flags.Any(static f => f.StartsWith("CEIL_", StringComparison.OrdinalIgnoreCase)))
            {
                here &= ~0x400;
            }

            (int first, int second) = FlagWords(region.Flags, RegionFlagBits, region.Here ? 0x702 : 0x2, 0x900 | (region.Here ? here : 0));
            Int16(record, 30, 3);
            Int16(record, 32, first);
            Int16(record, 34, second | (region.SaveAll ? 0x2000 : 0));
            Int32(record, 36, 1024);
            Int32(record, 44, 1024);
            Int32(record, 52, 1024);
            Int32(record, 60, 1024);
            Int32(record, 100, region.FloorTexture);
            Int32(record, 104, region.CeilingTexture);
            Fill(record, 108, RegionRecordSize);
            Skills(record, RegionFieldOffsets, region.RawSkills);
            for (int e = 0; e < RegionEventNames.Count; e++)
            {
                if (region.Events.TryGetValue(RegionEventNames[e], out int action))
                {
                    Int32(record, 128 + (4 * e), action);
                }
            }

            Int32(record, 152, region.Genius);
        }

        return bytes;
    }

    /// <summary>
    /// The bits <c>FLAGS</c> set in a thing's or actor's two flag words (+20, +22), found by declaring one
    /// flag at a time (OV-20261029-save-body round 12). <c>INVISIBLE</c> also clears the visibility bits.
    /// </summary>
    private static readonly Dictionary<string, (int First, int Second)> EntityFlagBits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CAREFULLY"] = (0x8, 0), ["GROUND"] = (0x10, 0), ["FAR"] = (0x80, 0), ["PASSABLE"] = (0x100, 0), ["BERKELEY"] = (0x800, 0),
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

        Int16(record, 0, unchecked((short)entity.Size.Width));
        Int16(record, 4, unchecked((short)entity.Size.Height));
        Int16(record, 20, first);
        Int16(record, 22, second);
        if (Has("SENSITIVE"))
        {
            Int16(record, 24, 8);
        }

        Int16(record, 26, 5);
        MapColor(record, entity.MapColor);
        Int32(record, 102, 1024);
        Int32(record, 126, entity.Region);
        if (!entity.Unplaced || entity.Actor)
        {
            Fill(record, 130, 134);
        }

        Int16(record, 134, entity.Texture);
        Int32(record, 138, entity.Attach);
        Fill(record, 146, 170);
        WriteEvents(record, entity, 0);
        Skills(record, AcknexSaveFile.EntityFieldOffsets, entity.RawSkills);
        Int32(record, 122, entity.Floor);

        if (BinaryPrimitives.ReadInt32LittleEndian(record[102..]) == 0)
        {
            Int32(record, 102, 1024); // CYCLE: a 0 here does not load
        }
    }

    private static void WriteWall(Span<byte> record, FullSaveWall wall)
    {
        Int32(record, 0, wall.RawSpan);
        Int16(record, 4, unchecked((short)wall.TextureStep));
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

        if (wall.Unplaced)
        {
            first &= ~0x200;
            second &= ~0x80;
        }

        Int16(record, 20, first);
        Int16(record, 22, second);
        if (Has("SENSITIVE"))
        {
            Int16(record, 24, 8);
        }

        Int16(record, 26, 5);
        MapColor(record, wall.MapColor);
        Int32(record, 102, 1024);
        Int32(record, 110, wall.First.X);
        Int32(record, 114, wall.First.Y);
        Int32(record, 118, wall.Heights.First);
        Int32(record, 122, wall.Second.X);
        Int32(record, 126, wall.Second.Y);
        Int32(record, 130, wall.Heights.Second);
        Int16(record, 134, wall.LeftRegion);
        Int16(record, 138, wall.RightRegion);
        Int16(record, 142, wall.Texture);
        Int32(record, 146, wall.Attach);
        Fill(record, 154, 178);
        WriteEvents(record, wall, 8);
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
