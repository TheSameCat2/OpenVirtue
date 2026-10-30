// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using OpenVirtue.Formats.Wdl;

namespace OpenVirtue.Engine;

/// <summary>
/// Indexes a flattened WDL program's declarations needed to dress the geometry:
/// <c>BMAP</c>/<c>OVLY</c> bitmaps, <c>TEXTURE</c> definitions, and the <c>REGION</c>/<c>WALL</c>
/// type definitions that name textures. Resolves a texture name through
/// <c>TEXTURE → BMAPS → BMAP</c> to a concrete <see cref="LevelTexture"/>.
/// </summary>
public sealed class WdlDeclarations
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private const double DefaultTextureScale = 16;
    private static readonly HashSet<string> RegionNumericProperties = new(Ci)
    {
        "CYCLE",
        "POSITION",
        "CLIP_DIST",
        "FLOOR_ANGLE",
        "CEIL_ANGLE",
    };
    private static readonly HashSet<string> RegionActionHookProperties = new(Ci)
    {
        "IF_ENTER",
        "IF_LEAVE",
        "IF_DIVE",
        "IF_ARISE",
    };
    private static readonly HashSet<string> WallNumericProperties = new(Ci)
    {
        "CYCLE",
        "POSITION",
        "MAP_COLOR",
        "DIST",
    };
    private static readonly HashSet<string> WallActionHookProperties = new(Ci)
    {
        "IF_NEAR",
        "IF_FAR",
        "IF_HIT",
    };
    private static readonly HashSet<string> EntityNumericProperties = new(Ci)
    {
        "CYCLE",
        "POSITION",
        "WAYPOINT",
        "TARGET_X",
        "TARGET_Y",
        "REL_ANGLE",
        "REL_DIST",
        "SPEED",
        "VSPEED",
        "ASPEED",
        "MAP_COLOR",
        "DIST",
    };
    private static readonly HashSet<string> EntityActionHookProperties = new(Ci)
    {
        "IF_NEAR",
        "IF_FAR",
        "IF_ARRIVED",
        "IF_HIT",
        "IF_KLICK",
    };
    private static readonly HashSet<string> EntityKeywordProperties = new(Ci)
    {
        "TARGET",
        "WAY",
        "OVERLAY",
        "ATTACH",
    };

    private readonly Dictionary<string, BitmapDef> _bitmaps = new(Ci);
    private readonly Dictionary<string, TextureDef> _textures = new(Ci);
    private readonly Dictionary<string, RegionTextures> _regions = new(Ci);
    private readonly Dictionary<string, WallDef> _walls = new(Ci);
    private readonly Dictionary<string, EntityDef> _entities = new(Ci);
    private readonly Dictionary<string, WdlBlock> _actions = new(Ci);
    private readonly List<string> _actionOrder = [];
    private readonly List<string> _textureDeclarations = [];
    private readonly List<(string Kind, string Name)> _objectDeclarations = [];
    private readonly List<string> _regionDeclarations = [];
    private readonly List<SaveTextureDeclaration> _saveTextures = [];
    private readonly Dictionary<string, List<string>> _numbered = new(Ci) { ["FONT"] = [], ["STRING"] = [], ["SOUND"] = [] };

    /// <summary>The names of all defined textures.</summary>
    public IReadOnlyCollection<string> TextureNames => _textures.Keys;

    /// <summary>The names of all declared bitmap-like resources.</summary>
    public IReadOnlyCollection<string> BitmapNames => _bitmaps.Keys;

    /// <summary>The body of each <c>ACTION</c> declaration, by name.</summary>
    public IReadOnlyDictionary<string, WdlBlock> Actions => _actions;

    /// <summary>
    /// The declared actions in the order their names first appear, as a declaration or as a
    /// reference before it (an <c>IF_NEAR</c>, a <c>CALL</c>, a slot), which is how the original
    /// numbers them in its saves (OV-20261029-save-body round 13: all 29 numbers read from a
    /// retail save fit this order, none the declaration order).
    /// </summary>
    public IReadOnlyList<string> ActionOrder => _actionOrder;

    /// <summary>Every <c>TEXTURE</c> declaration's name in order, repeats included.</summary>
    public IReadOnlyList<string> TextureDeclarations => _textureDeclarations;

    /// <summary>
    /// The <c>WALL</c>, <c>THING</c> and <c>ACTOR</c> declarations with a body, as (kind, name) in
    /// declaration order; a repeated name keeps its first place.
    /// </summary>
    public IReadOnlyList<(string Kind, string Name)> ObjectDeclarations => _objectDeclarations;

    /// <summary>The <c>REGION</c> declarations with a body, by name in declaration order; a repeated name keeps its first place.</summary>
    public IReadOnlyList<string> RegionDeclarations => _regionDeclarations;

    /// <summary>The <c>TEXTURE</c> declarations with <c>FLAGS SAVE</c>, in declaration order (OV-20261029-save-body round 15b).</summary>
    public IReadOnlyList<SaveTextureDeclaration> SaveTextures => _saveTextures;

    /// <summary>Builds the index from a flattened program.</summary>
    public static WdlDeclarations Index(WdlProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var declarations = new WdlDeclarations();

        foreach (WdlItem item in program.Items)
        {
            if (declarations._numbered.TryGetValue(item.Keyword, out List<string>? numbered) && item.Header.Count > 0)
            {
                numbered.Add(item.Header[0].Text);
            }

            switch (item.Keyword.ToUpperInvariant())
            {
                case "BMAP":
                case "OVLY":
                    declarations.IndexBitmap(item);
                    break;
                case "TEXTURE":
                    declarations.IndexTexture(item);
                    break;
                case "REGION" when item.HasBody:
                    declarations.IndexRegion(item);
                    if (item.Header.Count > 0 && !declarations._regionDeclarations.Exists(n => n.Equals(item.Header[0].Text, StringComparison.OrdinalIgnoreCase)))
                    {
                        declarations._regionDeclarations.Add(item.Header[0].Text);
                    }

                    break;
                case "WALL" when item.HasBody:
                    declarations.IndexWall(item);
                    declarations.AddObjectDeclaration(item);
                    break;
                case "THING" when item.HasBody:
                case "ACTOR" when item.HasBody:
                    declarations.IndexEntity(item);
                    declarations.AddObjectDeclaration(item);
                    break;
                case "ACTION" when item.HasBody && item.Header.Count > 0:
                    declarations._actions[item.Header[0].Text] = item.Body!;
                    break;
            }
        }

        var seen = new HashSet<string>(Ci);
        foreach (WdlItem item in program.Items)
        {
            declarations.AddActionMentions(item, seen);
        }

        // A save names a texture's font, touch string and sound by their number among the declarations
        // of their kind, from 1 (round 15b: the third FONT reads 3, the fourth STRING 4, the second SOUND 2).
        for (int i = 0; i < declarations._saveTextures.Count; i++)
        {
            SaveTextureDeclaration texture = declarations._saveTextures[i];
            declarations._saveTextures[i] = texture with
            {
                Font = declarations.Number("FONT", texture.FontName),
                Touch = declarations.Number("STRING", texture.TouchName),
                Sound = declarations.Number("SOUND", texture.SoundName),
            };
        }

        return declarations;
    }

    private void AddActionMentions(WdlItem item, HashSet<string> seen)
    {
        void Mention(string name)
        {
            if (_actions.ContainsKey(name) && seen.Add(name))
            {
                _actionOrder.Add(name);
            }
        }

        Mention(item.Keyword);
        foreach (WdlToken token in item.Header)
        {
            if (token.Kind == WdlTokenKind.Identifier)
            {
                Mention(token.Text);
            }
        }

        if (item.Body is { } body)
        {
            foreach (WdlItem inner in body.Items)
            {
                AddActionMentions(inner, seen);
            }
        }
    }

    private void AddObjectDeclaration(WdlItem item)
    {
        if (item.Header.Count > 0 && !_objectDeclarations.Exists(d => d.Name.Equals(item.Header[0].Text, StringComparison.OrdinalIgnoreCase)))
        {
            _objectDeclarations.Add((item.Keyword.ToUpperInvariant(), item.Header[0].Text));
        }
    }

    private int Number(string kind, string? name) =>
        name is null ? -1 : _numbered[kind].FindIndex(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) is >= 0 and int i ? i + 1 : -1;

    /// <summary>The floor/ceiling texture names for a region type, if defined.</summary>
    public RegionTextures? GetRegionTextures(string name) =>
        _regions.TryGetValue(name, out RegionTextures value) ? value : null;

    /// <summary>The texture name for a wall type, if defined.</summary>
    public string? GetWallTexture(string name) => GetWall(name)?.Texture;

    /// <summary>The texture and optional placement overrides for a wall type, if defined.</summary>
    public WallDef? GetWall(string name) =>
        _walls.TryGetValue(name, out WallDef value) ? value : null;

    /// <summary>The sprite texture, height, and WDL angle offset for a THING/ACTOR type, if defined.</summary>
    public EntityDef? GetEntity(string name) =>
        _entities.TryGetValue(name, out EntityDef value) ? value : null;

    /// <summary>Resolves a top-level <c>BMAP</c> or <c>OVLY</c> declaration by name.</summary>
    public LevelBitmap? ResolveBitmap(string? bitmapName)
    {
        if (bitmapName is null ||
            !_bitmaps.TryGetValue(bitmapName, out BitmapDef bitmap))
        {
            return null;
        }

        return new LevelBitmap(bitmapName, bitmap.File, bitmap.X, bitmap.Y, bitmap.Width, bitmap.Height);
    }

    /// <summary>Resolves a texture name to its source PCX file, sub-rectangle, and scale.</summary>
    public LevelTexture? ResolveTexture(string? textureName)
    {
        if (textureName is null ||
            !_textures.TryGetValue(textureName, out TextureDef texture))
        {
            return null;
        }

        LevelBitmap?[] frames = texture.Bitmaps
            .Select(ResolveBitmap)
            .ToArray();
        LevelBitmap? firstFrame = frames.FirstOrDefault(static frame => frame is not null);
        if (firstFrame is not { } bitmap)
        {
            return null;
        }

        return new LevelTexture(
            textureName, bitmap.File, bitmap.X, bitmap.Y, bitmap.Width, bitmap.Height,
            texture.ScaleX, texture.ScaleY, texture.Ambient, texture.IsSky, texture.IsLightmap,
            texture.IsBehind, texture.IsShadow, frames,
            texture.Sides, texture.Cycles, texture.Delays, texture.IsOneShot,
            texture.Mirrors, texture.FrameOffsetsX, texture.FrameOffsetsY,
            texture.SoundDistance, texture.SoundVerticalDistance,
            texture.SoundName, texture.SoundVolume, texture.SoundCycles, texture.IsSoundLoop,
            texture.RandomDelayFactor, texture.Albedo);
    }

    private void IndexBitmap(WdlItem item)
    {
        // BMAP/OVLY name, <file.pcx>[, x, y, width, height]
        if (item.Header.Count == 0)
        {
            return;
        }

        string name = item.Header[0].Text;
        string? file = FirstFileRef(item.Header);
        int[] numbers = Numbers(item.Header);
        if (file is null)
        {
            return;
        }

        _bitmaps[name] = numbers.Length >= 4
            ? new BitmapDef(file, numbers[0], numbers[1], numbers[2], numbers[3])
            : new BitmapDef(file, 0, 0, 0, 0);
    }

    private void IndexTexture(WdlItem item)
    {
        // TEXTURE name { SCALE_XY sx, sy; BMAPS bmap; }
        if (item.Header.Count == 0 || item.Body is not { } body)
        {
            return;
        }

        string? bitmap = null;
        var bitmaps = new List<string>();
        double scaleX = DefaultTextureScale, scaleY = DefaultTextureScale;
        double ambient = 0;
        double albedo = 0;
        double soundDistance = 100;
        double soundVerticalDistance = 0;
        string? soundName = null;
        double soundVolume = 0.5;
        bool[] soundCycles = [];
        double randomDelayFactor = 0;
        int sides = 1;
        int cycles = 1;
        double[] delays = [];
        bool[] mirrors = [];
        double[] frameOffsetsX = [];
        double[] frameOffsetsY = [];
        bool isSky = false;
        bool isLightmap = false;
        bool isBehind = false;
        bool isShadow = false;
        bool isOneShot = false;
        bool isSoundLoop = false;
        bool isSave = false;
        double posX = 0, posY = 0;
        string? fontName = null, touchName = null, attachName = null;
        var events = new Dictionary<string, string>(Ci);
        foreach (WdlItem property in body.Items)
        {
            if (property.Keyword.ToUpperInvariant() is "IF_TOUCH" or "IF_RELEASE" or "IF_KLICK" &&
                FirstIdentifierOrNull(property.Header) is { } eventAction)
            {
                events[property.Keyword] = eventAction;
            }
            else if (property.Keyword.Equals("POS_X", StringComparison.OrdinalIgnoreCase) && FirstNumber(property.Header) is { } parsedPosX)
            {
                posX = parsedPosX;
            }
            else if (property.Keyword.Equals("POS_Y", StringComparison.OrdinalIgnoreCase) && FirstNumber(property.Header) is { } parsedPosY)
            {
                posY = parsedPosY;
            }
            else if (property.Keyword.Equals("FONT", StringComparison.OrdinalIgnoreCase))
            {
                fontName = FirstIdentifierOrNull(property.Header);
            }
            else if (property.Keyword.Equals("TOUCH", StringComparison.OrdinalIgnoreCase))
            {
                touchName = FirstIdentifierOrNull(property.Header);
            }
            else if (property.Keyword.Equals("ATTACH", StringComparison.OrdinalIgnoreCase))
            {
                attachName = FirstIdentifierOrNull(property.Header);
            }

            // The manual names the list BMAPS; the original also takes BMAP, which Saints of Virtue's
            // music room uses (its retail save holds those sprites' sizes, OV-20261029-save-body round 13).
            if ((property.Keyword.Equals("BMAPS", StringComparison.OrdinalIgnoreCase) ||
                 property.Keyword.Equals("BMAP", StringComparison.OrdinalIgnoreCase)) && property.Header.Count > 0)
            {
                bitmaps.AddRange(IdentifierValues(property.Header));
                bitmap = bitmaps.Count > 0 ? bitmaps[0] : property.Header[0].Text;
            }
            else if (property.Keyword.Equals("SIDES", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedSides)
            {
                sides = Math.Max(1, (int)parsedSides);
            }
            else if (property.Keyword.Equals("CYCLES", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedCycles)
            {
                cycles = Math.Max(1, (int)parsedCycles);
            }
            else if (property.Keyword.Equals("DELAY", StringComparison.OrdinalIgnoreCase))
            {
                delays = NumberValues(property.Header);
            }
            else if (property.Keyword.Equals("RANDOM", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedRandomDelayFactor)
            {
                randomDelayFactor = Math.Clamp(parsedRandomDelayFactor, 0, 1);
            }
            else if (property.Keyword.Equals("MIRROR", StringComparison.OrdinalIgnoreCase))
            {
                mirrors = NumberValues(property.Header).Select(static value => value != 0).ToArray();
            }
            else if (property.Keyword.Equals("OFFSET_X", StringComparison.OrdinalIgnoreCase))
            {
                frameOffsetsX = NumberValues(property.Header);
            }
            else if (property.Keyword.Equals("OFFSET_Y", StringComparison.OrdinalIgnoreCase))
            {
                frameOffsetsY = NumberValues(property.Header);
            }
            else if (property.Keyword.Equals("SCALE_XY", StringComparison.OrdinalIgnoreCase))
            {
                double[] scale = NumberValues(property.Header);
                if (scale.Length >= 2)
                {
                    scaleX = scale[0];
                    scaleY = scale[1];
                }
            }
            else if (property.Keyword.Equals("SCALE_X", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedScaleX)
            {
                scaleX = parsedScaleX;
            }
            else if (property.Keyword.Equals("SCALE_Y", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedScaleY)
            {
                scaleY = parsedScaleY;
            }
            else if (property.Keyword.Equals("AMBIENT", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedAmbient)
            {
                ambient = parsedAmbient;
            }
            else if (property.Keyword.Equals("ALBEDO", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedAlbedo)
            {
                albedo = parsedAlbedo;
            }
            else if (property.Keyword.Equals("SOUND", StringComparison.OrdinalIgnoreCase))
            {
                soundName = FirstIdentifierOrNull(property.Header);
            }
            else if (property.Keyword.Equals("SVOL", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedSoundVolume)
            {
                soundVolume = Math.Clamp(parsedSoundVolume, 0, 1);
            }
            else if (property.Keyword.Equals("SDIST", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedSoundDistance)
            {
                soundDistance = Math.Max(0, parsedSoundDistance);
            }
            else if (property.Keyword.Equals("SVDIST", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedSoundVerticalDistance)
            {
                soundVerticalDistance = Math.Max(0, parsedSoundVerticalDistance);
            }
            else if (property.Keyword.Equals("SCYCLES", StringComparison.OrdinalIgnoreCase))
            {
                soundCycles = NumberValues(property.Header).Select(static value => value != 0).ToArray();
            }
            else if (property.Keyword.Equals("FLAGS", StringComparison.OrdinalIgnoreCase))
            {
                isSky |= property.Header.Any(static t => t.Text.Equals("SKY", StringComparison.OrdinalIgnoreCase));
                isLightmap |= property.Header.Any(static t => t.Text.Equals("LIGHTMAP", StringComparison.OrdinalIgnoreCase));
                isBehind |= property.Header.Any(static t => t.Text.Equals("BEHIND", StringComparison.OrdinalIgnoreCase));
                isShadow |= property.Header.Any(static t => t.Text.Equals("SHADOW", StringComparison.OrdinalIgnoreCase));
                isOneShot |= property.Header.Any(static t => t.Text.Equals("ONESHOT", StringComparison.OrdinalIgnoreCase));
                isSoundLoop |= property.Header.Any(static t => t.Text.Equals("SLOOP", StringComparison.OrdinalIgnoreCase));
                isSave |= property.Header.Any(static t => t.Text.Equals("SAVE", StringComparison.OrdinalIgnoreCase));
            }
        }

        if (isSave)
        {
            _saveTextures.Add(new SaveTextureDeclaration(
                item.Header[0].Text, ambient, albedo, scaleX, scaleY, soundVolume, soundDistance, soundVerticalDistance, posX, posY)
            {
                FontName = fontName,
                TouchName = touchName,
                SoundName = soundName,
                AttachName = attachName,
                Events = events,
            });
        }

        _textureDeclarations.Add(item.Header[0].Text);
        _textures[item.Header[0].Text] = new TextureDef(
            bitmap, bitmaps.ToArray(), scaleX, scaleY, ambient, isSky, isLightmap, isBehind, isShadow,
            sides, cycles, delays, isOneShot,
            mirrors, frameOffsetsX, frameOffsetsY, soundDistance, soundVerticalDistance,
            soundName, soundVolume, soundCycles, isSoundLoop, randomDelayFactor, albedo);
    }

    private void IndexRegion(WdlItem item)
    {
        string? floor = null, ceiling = null;
        string? genius = null;
        string? below = null;
        double? floorHeight = null, ceilHeight = null;
        double ambient = 0;
        double floorOffsetX = 0, floorOffsetY = 0, ceilOffsetX = 0, ceilOffsetY = 0;
        string? eachTickAction = null;
        string? eachCycleAction = null;
        var actionHooks = new Dictionary<string, string?>(Ci);
        var skills = new Dictionary<string, double>(Ci);
        foreach (WdlItem property in item.Body!.Items)
        {
            if (property.Keyword.Equals("FLOOR_TEX", StringComparison.OrdinalIgnoreCase))
            {
                floor = property.Header.Count > 0 ? property.Header[0].Text : null;
            }
            else if (property.Keyword.Equals("CEIL_TEX", StringComparison.OrdinalIgnoreCase))
            {
                ceiling = property.Header.Count > 0 ? property.Header[0].Text : null;
            }
            else if (property.Keyword.Equals("GENIUS", StringComparison.OrdinalIgnoreCase))
            {
                genius = FirstIdentifierOrNull(property.Header);
            }
            else if (property.Keyword.Equals("BELOW", StringComparison.OrdinalIgnoreCase))
            {
                below = FirstIdentifierOrNull(property.Header);
            }
            else if (property.Keyword.Equals("FLOOR_HGT", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedFloorHeight)
            {
                floorHeight = parsedFloorHeight;
            }
            else if (property.Keyword.Equals("CEIL_HGT", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedCeilHeight)
            {
                ceilHeight = parsedCeilHeight;
            }
            else if (property.Keyword.Equals("AMBIENT", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedAmbient)
            {
                ambient = parsedAmbient;
            }
            else if (property.Keyword.Equals("FLOOR_OFFS_X", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedFloorOffsetX)
            {
                floorOffsetX = parsedFloorOffsetX;
            }
            else if (property.Keyword.Equals("FLOOR_OFFS_Y", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedFloorOffsetY)
            {
                floorOffsetY = parsedFloorOffsetY;
            }
            else if (property.Keyword.Equals("CEIL_OFFS_X", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedCeilOffsetX)
            {
                ceilOffsetX = parsedCeilOffsetX;
            }
            else if (property.Keyword.Equals("CEIL_OFFS_Y", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedCeilOffsetY)
            {
                ceilOffsetY = parsedCeilOffsetY;
            }
            else if (property.Keyword.Equals("EACH_TICK", StringComparison.OrdinalIgnoreCase))
            {
                eachTickAction = FirstIdentifierOrNull(property.Header);
            }
            else if (property.Keyword.Equals("EACH_CYCLE", StringComparison.OrdinalIgnoreCase))
            {
                eachCycleAction = FirstIdentifierOrNull(property.Header);
            }
            else if (RegionActionHookProperties.Contains(property.Keyword))
            {
                actionHooks[property.Keyword] = FirstIdentifierOrNull(property.Header);
            }
            else if (TryUniversalSkill(property, out string skillName, out double skillValue))
            {
                skills[skillName] = skillValue;
            }
            else if (TryNumericProperty(property, RegionNumericProperties, out string propertyName, out double propertyValue))
            {
                skills[propertyName] = propertyValue;
            }
            else if (property.Keyword.Equals("FLAGS", StringComparison.OrdinalIgnoreCase))
            {
                AddFlags(property, skills);
            }
        }

        _regions[item.Header[0].Text] = new RegionTextures(
            floor, ceiling, genius, below, ambient, floorOffsetX, floorOffsetY, ceilOffsetX, ceilOffsetY,
            eachTickAction, eachCycleAction, actionHooks, skills, floorHeight, ceilHeight);
    }

    private void IndexWall(WdlItem item)
    {
        string? texture = null;
        string? attach = null;
        double? offsetX = null;
        double? offsetY = null;
        string? eachTickAction = null;
        string? eachCycleAction = null;
        var actionHooks = new Dictionary<string, string?>(Ci);
        var skills = new Dictionary<string, double>(Ci);
        foreach (WdlItem property in item.Body!.Items)
        {
            if (property.Keyword.Equals("TEXTURE", StringComparison.OrdinalIgnoreCase) && property.Header.Count > 0)
            {
                texture = property.Header[0].Text;
            }
            else if (property.Keyword.Equals("ATTACH", StringComparison.OrdinalIgnoreCase))
            {
                attach = FirstIdentifierOrNull(property.Header);
            }
            else if (property.Keyword.Equals("OFFSET_X", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedOffsetX)
            {
                offsetX = parsedOffsetX;
            }
            else if (property.Keyword.Equals("OFFSET_Y", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedOffsetY)
            {
                offsetY = parsedOffsetY;
            }
            else if (property.Keyword.Equals("EACH_TICK", StringComparison.OrdinalIgnoreCase))
            {
                eachTickAction = FirstIdentifierOrNull(property.Header);
            }
            else if (property.Keyword.Equals("EACH_CYCLE", StringComparison.OrdinalIgnoreCase))
            {
                eachCycleAction = FirstIdentifierOrNull(property.Header);
            }
            else if (WallActionHookProperties.Contains(property.Keyword))
            {
                actionHooks[property.Keyword] = FirstIdentifierOrNull(property.Header);
            }
            else if (TryUniversalSkill(property, out string skillName, out double skillValue))
            {
                skills[skillName] = skillValue;
            }
            else if (TryNumericProperty(property, WallNumericProperties, out string propertyName, out double propertyValue))
            {
                skills[propertyName] = propertyValue;
            }
            else if (property.Keyword.Equals("FLAGS", StringComparison.OrdinalIgnoreCase))
            {
                AddFlags(property, skills);
            }
        }

        _walls[item.Header[0].Text] = new WallDef(
            texture, attach, offsetX, offsetY, eachTickAction, eachCycleAction, actionHooks, skills);
    }

    private void IndexEntity(WdlItem item)
    {
        if (item.Header.Count == 0)
        {
            return;
        }

        string? texture = null;
        double height = 0;
        double angle = 0;
        string? eachTickAction = null;
        string? eachCycleAction = null;
        var actionHooks = new Dictionary<string, string?>(Ci);
        var strings = new Dictionary<string, string?>(Ci);
        var skills = new Dictionary<string, double>(Ci);
        foreach (WdlItem property in item.Body!.Items)
        {
            if (property.Keyword.Equals("TEXTURE", StringComparison.OrdinalIgnoreCase) && property.Header.Count > 0)
            {
                texture = property.Header[0].Text;
            }
            else if (property.Keyword.Equals("HEIGHT", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsed)
            {
                height = parsed;
            }
            else if (property.Keyword.Equals("ANGLE", StringComparison.OrdinalIgnoreCase) &&
                     FirstNumber(property.Header) is { } parsedAngle)
            {
                angle = parsedAngle;
            }
            else if (property.Keyword.Equals("EACH_TICK", StringComparison.OrdinalIgnoreCase))
            {
                eachTickAction = FirstIdentifierOrNull(property.Header);
            }
            else if (property.Keyword.Equals("EACH_CYCLE", StringComparison.OrdinalIgnoreCase))
            {
                eachCycleAction = FirstIdentifierOrNull(property.Header);
            }
            else if (EntityActionHookProperties.Contains(property.Keyword))
            {
                actionHooks[property.Keyword] = FirstIdentifierOrNull(property.Header);
            }
            else if (EntityKeywordProperties.Contains(property.Keyword))
            {
                strings[property.Keyword] = FirstIdentifierOrNull(property.Header);
            }
            else if (TryUniversalSkill(property, out string skillName, out double skillValue))
            {
                skills[skillName] = skillValue;
            }
            else if (TryNumericProperty(property, EntityNumericProperties, out string propertyName, out double propertyValue))
            {
                skills[propertyName] = propertyValue;
            }
            else if (property.Keyword.Equals("FLAGS", StringComparison.OrdinalIgnoreCase))
            {
                AddFlags(property, skills);
            }
        }

        _entities[item.Header[0].Text] = new EntityDef(
            texture, height, angle, eachTickAction, eachCycleAction, actionHooks, strings, skills);
    }

    private static string? FirstFileRef(IReadOnlyList<WdlToken> tokens)
    {
        foreach (WdlToken token in tokens)
        {
            if (token.Kind == WdlTokenKind.FileRef)
            {
                return token.Text;
            }
        }

        return null;
    }

    private static double? FirstNumber(IReadOnlyList<WdlToken> tokens)
    {
        if (tokens.Count == 0)
        {
            return null;
        }

        int sign = 1;
        int index = 0;
        if (tokens.Count > 1 &&
            tokens[0].Kind == WdlTokenKind.Operator &&
            (tokens[0].Text == "-" || tokens[0].Text == "+"))
        {
            sign = tokens[0].Text == "-" ? -1 : 1;
            index = 1;
        }

        return tokens[index].Kind == WdlTokenKind.Number &&
               double.TryParse(tokens[index].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? sign * value
            : null;
    }

    private static int[] Numbers(IReadOnlyList<WdlToken> tokens)
    {
        double[] values = NumberValues(tokens);
        var numbers = new int[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            numbers[i] = (int)values[i];
        }

        return numbers;
    }

    private static double[] NumberValues(IReadOnlyList<WdlToken> tokens)
    {
        var numbers = new List<double>();
        for (int i = 0; i < tokens.Count; i++)
        {
            WdlToken token = tokens[i];
            int sign = 1;
            if (token.Kind == WdlTokenKind.Operator &&
                (token.Text == "-" || token.Text == "+") &&
                i + 1 < tokens.Count &&
                tokens[i + 1].Kind == WdlTokenKind.Number)
            {
                sign = token.Text == "-" ? -1 : 1;
                token = tokens[++i];
            }

            if (token.Kind == WdlTokenKind.Number &&
                double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                numbers.Add(sign * value);
            }
        }

        return [.. numbers];
    }

    private static bool TryUniversalSkill(WdlItem item, out string name, out double value)
    {
        name = item.Keyword;
        if (!IsUniversalSkill(name) || FirstNumber(item.Header) is not { } parsed)
        {
            value = 0;
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryNumericProperty(
        WdlItem item, HashSet<string> propertyNames, out string name, out double value)
    {
        name = item.Keyword;
        if (!propertyNames.Contains(name) || FirstNumber(item.Header) is not { } parsed)
        {
            value = 0;
            return false;
        }

        value = parsed;
        return true;
    }

    private static string? FirstIdentifierOrNull(IReadOnlyList<WdlToken> tokens)
    {
        foreach (WdlToken token in tokens)
        {
            if (token.Kind == WdlTokenKind.Identifier)
            {
                return token.Text.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : token.Text;
            }
        }

        return null;
    }

    private static IEnumerable<string> IdentifierValues(IReadOnlyList<WdlToken> tokens)
    {
        foreach (WdlToken token in tokens)
        {
            if (token.Kind == WdlTokenKind.Identifier)
            {
                yield return token.Text;
            }
        }
    }

    private static void AddFlags(WdlItem item, Dictionary<string, double> skills)
    {
        foreach (WdlToken token in item.Header)
        {
            if (token.Kind == WdlTokenKind.Identifier)
            {
                skills[token.Text] = 1;
            }
        }
    }

    private static bool IsUniversalSkill(string keyword)
    {
        if (!keyword.StartsWith("SKILL", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> suffix = keyword.AsSpan("SKILL".Length);
        return suffix.Length == 1 && suffix[0] >= '1' && suffix[0] <= '8';
    }

    private readonly record struct BitmapDef(string File, int X, int Y, int Width, int Height);

    private readonly record struct TextureDef(
        string? Bitmap,
        IReadOnlyList<string> Bitmaps,
        double ScaleX,
        double ScaleY,
        double Ambient,
        bool IsSky,
        bool IsLightmap,
        bool IsBehind,
        bool IsShadow,
        int Sides,
        int Cycles,
        IReadOnlyList<double> Delays,
        bool IsOneShot,
        IReadOnlyList<bool> Mirrors,
        IReadOnlyList<double> FrameOffsetsX,
        IReadOnlyList<double> FrameOffsetsY,
        double SoundDistance,
        double SoundVerticalDistance,
        string? SoundName,
        double SoundVolume,
        IReadOnlyList<bool> SoundCycles,
        bool IsSoundLoop,
        double RandomDelayFactor,
        double Albedo);

    /// <summary>The texture name and optional WDL pixel offsets declared for a wall type.</summary>
    public readonly record struct WallDef(
        string? Texture,
        string? AttachTexture,
        double? OffsetX,
        double? OffsetY,
        string? EachTickAction,
        string? EachCycleAction,
        IReadOnlyDictionary<string, string?> ActionHooks,
        IReadOnlyDictionary<string, double> Skills);

    /// <summary>The surface texture names and ambient brightness declared for a region type.</summary>
    public readonly record struct RegionTextures(
        string? Floor,
        string? Ceiling,
        string? Genius,
        string? Below,
        double Ambient,
        double FloorOffsetX,
        double FloorOffsetY,
        double CeilOffsetX,
        double CeilOffsetY,
        string? EachTickAction,
        string? EachCycleAction,
        IReadOnlyDictionary<string, string?> ActionHooks,
        IReadOnlyDictionary<string, double> Skills,
        double? FloorHeight = null,
        double? CeilHeight = null);

    /// <summary>The sprite texture name, height, and WDL radian angle offset declared for a THING/ACTOR type.</summary>
    public readonly record struct EntityDef(
        string? Texture,
        double Height,
        double AngleRadians,
        string? EachTickAction,
        string? EachCycleAction,
        IReadOnlyDictionary<string, string?> ActionHooks,
        IReadOnlyDictionary<string, string?> Strings,
        IReadOnlyDictionary<string, double> Skills);
    /// <summary>
    /// A <c>TEXTURE</c> declared with <c>FLAGS SAVE</c>: the values and references a full save keeps of
    /// it (OV-20261029-save-body round 15b). The font, touch string and sound are numbered among their
    /// kind's declarations from 1, or −1 for none.
    /// </summary>
    public sealed record SaveTextureDeclaration(
        string Name,
        double Ambient,
        double Albedo,
        double ScaleX,
        double ScaleY,
        double SoundVolume,
        double SoundDistance,
        double SoundVerticalDistance,
        double PosX,
        double PosY)
    {
        /// <summary>The <c>FONT</c> named.</summary>
        public string? FontName { get; init; }

        /// <summary>The <c>TOUCH</c> string named.</summary>
        public string? TouchName { get; init; }

        /// <summary>The <c>SOUND</c> named.</summary>
        public string? SoundName { get; init; }

        /// <summary>The <c>ATTACH</c> texture named.</summary>
        public string? AttachName { get; init; }

        /// <summary>The <c>IF_TOUCH</c>, <c>IF_RELEASE</c> and <c>IF_KLICK</c> actions, by event.</summary>
        public IReadOnlyDictionary<string, string> Events { get; init; } = new Dictionary<string, string>();

        /// <summary>The font's number.</summary>
        public int Font { get; init; } = -1;

        /// <summary>The touch string's number.</summary>
        public int Touch { get; init; } = -1;

        /// <summary>The sound's number.</summary>
        public int Sound { get; init; } = -1;
    }
}
