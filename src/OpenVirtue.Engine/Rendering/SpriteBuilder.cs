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
    bool IsAttachment = false);

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
            regionFloor = (float)level.Regions[entity.Region].FloorHeight;
            if (LevelTopology.TryGetSurfaceHeightOffset(
                    level,
                    entity.Region,
                    entity.X,
                    entity.Y,
                    out double slopeOffset))
            {
                regionFloor += (float)slopeOffset;
            }
        }
        float baseFloor = entity["GROUND"] <= 0 ? regionFloor : 0;
        Vector3 basePosition = RenderSpace.FromMap((float)entity.X, baseFloor + (float)entity.Z, (float)entity.Y);
        AddTextureLayer(level, entity, entity.Texture, viewerPosition, basePosition, regionFloor, isAttachment: false, billboards);
        AddTextureLayer(level, entity, entity.AttachTexture, viewerPosition, basePosition, regionFloor, isAttachment: true, billboards);
    }

    private static void AddTextureLayer(
        Level level,
        MapEntity entity,
        string? textureName,
        Vector2? viewerPosition,
        Vector3 basePosition,
        float floor,
        bool isAttachment,
        List<SpriteBillboard> billboards)
    {
        if (textureName is null || !level.Textures.TryGetValue(textureName, out LevelTexture texture))
        {
            return;
        }

        int side = viewerPosition is { } viewer ? SideFacingViewer(entity, viewer, texture.Sides) : 0;
        string? renderTexture = TextureAnimation.ResolveRenderTexture(texture, entity["CYCLE"], side);
        if (renderTexture is null ||
            !MapEntitySizing.TryGetTextureSize(texture, entity["CYCLE"], side, out MapEntitySize size))
        {
            return;
        }

        (double offsetX, double offsetY) = TextureAnimation.FrameOffset(texture, entity["CYCLE"], side);
        double scaleX = texture.ScaleX > 0 ? texture.ScaleX : 16;
        double scaleY = texture.ScaleY > 0 ? texture.ScaleY : 16;
        SpriteOrientation orientation = isAttachment && texture.IsShadow
            ? SpriteOrientation.Ground
            : SpriteOrientation.Upright;
        Vector3 layerPosition = orientation == SpriteOrientation.Ground
            ? new Vector3(basePosition.X, floor + 0.01f, basePosition.Z)
            : basePosition;
        float depthOffset = !isAttachment || orientation == SpriteOrientation.Ground
            ? 0
            : texture.IsBehind ? 0.01f : -0.01f;

        billboards.Add(new SpriteBillboard(
            renderTexture,
            layerPosition,
            size.Width * 0.5f,
            size.Height * 0.5f,
            entity,
            TextureAnimation.IsMirrored(texture, side),
            new Vector2((float)(offsetX / scaleX), (float)(-offsetY / scaleY)),
            orientation,
            depthOffset,
            isAttachment));
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
