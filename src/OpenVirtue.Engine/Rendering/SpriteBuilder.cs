// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Map;

namespace OpenVirtue.Engine.Rendering;

/// <summary>World-plane orientation used by a sprite texture layer.</summary>
public enum SpriteOrientation
{
    Upright,
    Ground,
}

/// <summary>
/// How a sprite layer mixes with what is behind it (OV-20261104-blend): <c>GHOST</c> draws its texels
/// only where the screen pixel's x + y is odd (a checkerboard); <c>DIAPHANOUS</c> draws the palette
/// colour nearest the mean of texel and background with a <c>BLUR</c> palette, and palette entry 0
/// without one.
/// </summary>
public enum SpriteBlend
{
    None,
    Ghost,
    Diaphanous,
}

/// <summary>
/// A camera-facing static sprite billboard resolved from a placed THING/ACTOR.
/// <see cref="BasePosition"/> is in render space (<see cref="RenderSpace"/>: map y → −z).
/// </summary>
public readonly record struct SpriteBillboard(
    string Texture,
    Vector3 BasePosition,
    float HalfWidth,
    float HalfHeight,
    MapEntity? Source = null,
    bool Mirror = false,
    Vector2 CenterOffset = default,
    SpriteOrientation Orientation = SpriteOrientation.Upright,
    float CameraDepthOffset = 0,
    bool IsAttachment = false,
    SpriteBlend Blend = SpriteBlend.None);

/// <summary>
/// Builds static billboard sprites from the level's placed THINGs/ACTORs. This is the
/// renderer-agnostic layout step; API-specific drawing and facing remain in the app layer.
/// </summary>
public static class SpriteBuilder
{
    /// <summary>
    /// Builds all resolvable static sprite billboards in level order. <paramref name="viewerPosition"/>
    /// is the viewer's <em>map</em> position (x, y), used only to pick directional texture sides.
    /// </summary>
    public static IReadOnlyList<SpriteBillboard> Build(Level level, Vector2? viewerPosition = null)
    {
        ArgumentNullException.ThrowIfNull(level);

        var billboards = new List<SpriteBillboard>((level.Things.Count + level.Actors.Count) * 2);
        foreach (Thing thing in level.Things)
        {
            Add(level, thing, viewerPosition, billboards);
        }

        foreach (Actor actor in level.Actors)
        {
            Add(level, actor, viewerPosition, billboards);
        }

        return billboards;
    }

    /// <summary>Returns sprites ordered far-to-near from <paramref name="cameraPosition"/> (render space).</summary>
    public static IReadOnlyList<SpriteBillboard> OrderBackToFront(
        IEnumerable<SpriteBillboard> billboards,
        Vector3 cameraPosition)
    {
        ArgumentNullException.ThrowIfNull(billboards);
        return billboards
            .OrderByDescending(b => Vector3.DistanceSquared(b.BasePosition, cameraPosition))
            .ToArray();
    }

    /// <summary>
    /// Returns sprites ordered far-to-near by their depth along <paramref name="forward"/> (the view's
    /// level direction, render space), a thing after an actor at the same depth (OV-20261030-below-beam
    /// round 2): with two things and an actor in a row 8 ahead, at y 6, 2 and −2, the original draws the
    /// things over the actor, the one at y 6 too, though it lies farther from the eye. Sprites are painted
    /// in this order without hiding one another by depth.
    /// </summary>
    public static IReadOnlyList<SpriteBillboard> OrderBackToFront(
        IEnumerable<SpriteBillboard> billboards,
        Vector3 cameraPosition,
        Vector3 forward)
    {
        ArgumentNullException.ThrowIfNull(billboards);
        return billboards
            .OrderByDescending(b => MathF.Round(Vector3.Dot(b.BasePosition - cameraPosition, forward) * 1024))
            .ThenBy(static b => b.Source is Thing ? 1 : 0)
            .ToArray();
    }

    /// <summary>
    /// Selects the frontmost upright texture layer per material entity for mouse interaction.
    /// Ground shadows never enlarge the click target.
    /// </summary>
    public static IReadOnlyList<SpriteBillboard> SelectClickableLayers(IEnumerable<SpriteBillboard> billboards)
    {
        ArgumentNullException.ThrowIfNull(billboards);
        return billboards
            .Where(sprite =>
                sprite.Source is not null &&
                sprite.Source["IMMATERIAL"] == 0 &&
                sprite.Orientation == SpriteOrientation.Upright)
            .GroupBy(sprite => sprite.Source!)
            .Select(group => group.OrderBy(sprite => sprite.CameraDepthOffset).First())
            .ToArray();
    }

    /// <summary>Tests a normalized billboard coordinate against its decoded alpha channel.</summary>
    public static bool IsOpaque(TextureImage image, float u, float v, bool mirror = false)
    {
        return !TrySamplePixel(image, u, v, mirror, out _, out _, out bool opaque) || opaque;
    }

    /// <summary>Resolves normalized billboard coordinates to a decoded texture pixel.</summary>
    public static bool TrySamplePixel(
        TextureImage image,
        float u,
        float v,
        bool mirror,
        out int x,
        out int y,
        out bool opaque)
    {
        x = y = 0;
        opaque = true;
        if (image.Width <= 0 || image.Height <= 0 || image.Rgba.Length < image.Width * image.Height * 4 ||
            !float.IsFinite(u) || !float.IsFinite(v) || u < 0 || u > 1 || v < 0 || v > 1)
        {
            return false;
        }

        if (mirror)
        {
            u = 1 - u;
        }

        x = Math.Clamp((int)(u * image.Width), 0, image.Width - 1);
        y = Math.Clamp((int)(v * image.Height), 0, image.Height - 1);
        opaque = image.Rgba[((y * image.Width) + x) * 4 + 3] >= 128;
        return true;
    }

    private static void Add(
        Level level,
        MapEntity entity,
        Vector2? viewerPosition,
        List<SpriteBillboard> billboards)
    {
        if (entity["INVISIBLE"] > 0)
        {
            return;
        }

        float regionFloor = 0;
        if (entity.Region >= 0 && entity.Region < level.Regions.Count)
        {
            regionFloor = (float)LevelTopology.FloorHeightAt(level, entity.Region, entity.X, entity.Y);
        }
        float baseFloor = entity["GROUND"] <= 0 ? regionFloor : 0;
        Vector3 basePosition = RenderSpace.FromMap((float)entity.X, baseFloor + (float)entity.Z, (float)entity.Y);
        if (ResolveLayer(level, entity, entity.Texture, viewerPosition) is not { } baseLayer)
        {
            return;
        }

        billboards.Add(new SpriteBillboard(
            baseLayer.RenderTexture,
            basePosition,
            baseLayer.Size.Width * 0.5f,
            baseLayer.Size.Height * 0.5f,
            entity,
            baseLayer.Mirror,
            baseLayer.FrameOffset,
            Blend: BlendOf(baseLayer.Texture)));

        // OV-20261104-attach: the attach layer is an upright sprite of its own size whose upper left
        // corner sits POS_X, POS_Y pixels of the base texture (at the base's scale) from the base's
        // upper left; a SHADOW attach is centred on the region's floor height instead, POS_Y still
        // shifting it; BEHIND draws it behind the base.
        if (ResolveLayer(level, entity, entity.AttachTexture, viewerPosition) is not { } attach)
        {
            return;
        }

        float baseLeft = baseLayer.FrameOffset.X - (baseLayer.Size.Width * 0.5f);
        float baseTop = baseLayer.FrameOffset.Y + baseLayer.Size.Height;
        float dx = (float)(attach.Texture.PosX / baseLayer.ScaleX);
        float dy = (float)(attach.Texture.PosY / baseLayer.ScaleY);
        float centerX = baseLeft + dx + (attach.Size.Width * 0.5f) + attach.FrameOffset.X;
        float bottom = attach.Texture.IsShadow
            ? regionFloor - basePosition.Y - (attach.Size.Height * 0.5f) + dy
            : baseTop - dy - attach.Size.Height;
        bottom += attach.FrameOffset.Y;
        billboards.Add(new SpriteBillboard(
            attach.RenderTexture,
            basePosition,
            attach.Size.Width * 0.5f,
            attach.Size.Height * 0.5f,
            entity,
            attach.Mirror,
            new Vector2(centerX, bottom),
            SpriteOrientation.Upright,
            attach.Texture.IsBehind ? 0.01f : -0.01f,
            IsAttachment: true,
            Blend: BlendOf(attach.Texture)));
    }

    private static SpriteBlend BlendOf(LevelTexture texture) =>
        texture.IsDiaphanous ? SpriteBlend.Diaphanous : texture.IsGhost ? SpriteBlend.Ghost : SpriteBlend.None;

    /// <summary>A texture layer of an entity as drawn: its frame, size in steps, scale and frame offset.</summary>
    private readonly record struct ResolvedLayer(
        LevelTexture Texture,
        string RenderTexture,
        MapEntitySize Size,
        double ScaleX,
        double ScaleY,
        bool Mirror,
        Vector2 FrameOffset);

    private static ResolvedLayer? ResolveLayer(Level level, MapEntity entity, string? textureName, Vector2? viewerPosition)
    {
        if (textureName is null || !level.Textures.TryGetValue(textureName, out LevelTexture texture))
        {
            return null;
        }

        int side = viewerPosition is { } viewer ? SideFacingViewer(entity, viewer, texture.Sides) : 0;
        string? renderTexture = TextureAnimation.ResolveRenderTexture(texture, entity["CYCLE"], side);
        if (renderTexture is null ||
            !MapEntitySizing.TryGetTextureSize(texture, entity["CYCLE"], side, out MapEntitySize size))
        {
            return null;
        }

        (double offsetX, double offsetY) = TextureAnimation.FrameOffset(texture, entity["CYCLE"], side);
        double scaleX = texture.ScaleX > 0 ? texture.ScaleX : 16;
        double scaleY = texture.ScaleY > 0 ? texture.ScaleY : 16;
        return new ResolvedLayer(
            texture,
            renderTexture,
            size,
            scaleX,
            scaleY,
            TextureAnimation.IsMirrored(texture, side),
            new Vector2((float)(offsetX / scaleX), (float)(-offsetY / scaleY)));
    }

    /// <summary>Returns the directional texture side facing a viewer in map coordinates.</summary>
    public static int SideFacingViewer(MapEntity entity, Vector2 viewer, int sideCount)
    {
        if (sideCount <= 1)
        {
            return 0;
        }

        double viewerAngle = Math.Atan2(viewer.Y - entity.Y, viewer.X - entity.X);
        double relative = viewerAngle - entity.FacingAngleRadians;
        double clockwiseSide = -relative * sideCount / Math.Tau;
        int nearestSide = (int)Math.Floor(clockwiseSide + 0.5);
        return ((nearestSide % sideCount) + sideCount) % sideCount;
    }
}
