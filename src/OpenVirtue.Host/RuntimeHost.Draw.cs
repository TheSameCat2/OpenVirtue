// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine;
using OpenVirtue.Engine.Map;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Host;

public sealed partial class RuntimeHost
{
    private void DrawWorld()
    {
        int width = Math.Max(1, _window.Width);
        int height = Math.Max(1, _window.Height);
        VirtualDisplayLayout display = DisplayLayout();
        PixelRect worldPixels = PixelRect.From(WorldViewport(display), width, height);
        PixelRect letterbox = PixelRect.From(display.PhysicalDisplay, width, height);
        Matrix4x4 viewProjection = _camera.View * CurrentProjection().CreateProjectionMatrix(1f, 200000f);
        Matrix4x4 uiProjection = Matrix4x4.CreateOrthographicOffCenter(
            0,
            display.VirtualWidth,
            display.VirtualHeight,
            0,
            0,
            1);
        var frame = new HostFrame
        {
            Width = width,
            Height = height,
            ClearColor = TintedClearColor(),
            ClearDepth = true,
            Zones = Zones(),
            Columns = WorldColumns(display),
        };

        AddSky(frame, worldPixels);
        AddWorldBatches(frame, viewProjection, worldPixels);
        AddSprites(frame, viewProjection, worldPixels);
        AddUiOverlays(frame, display, uiProjection, letterbox);
        AddUiText(frame, uiProjection, letterbox);
        _graphics.Draw(frame);
    }

    private void DrawFlic(FlicPlayback playback)
    {
        int width = Math.Max(1, _window.Width);
        int height = Math.Max(1, _window.Height);
        var source = playback.Animation.Frames[playback.FrameIndex];
        var frame = new HostFrame
        {
            Width = width,
            Height = height,
            ClearColor = new Vector4(0, 0, 0, 1),
            ClearDepth = false,
        };
        AddBatch(
            frame,
            UnitQuad(),
            Matrix4x4.CreateOrthographicOffCenter(0, 1, 1, 0, 0, 1),
            PixelRect.Full(width, height),
            texture: null,
            TextureSlot.World,
            alphaTest: false,
            depthTest: false,
            ambient: 1,
            fade: PaletteFadeTint.None,
            dynamic: new TextureImage(playback.Animation.Width, playback.Animation.Height, source.Rgba));
        _graphics.Draw(frame);
    }

    private void AddSky(HostFrame frame, PixelRect viewport)
    {
        LevelTexture? sky = null;
        foreach (LevelTexture candidate in _level.Textures.Values)
        {
            if (candidate.IsSky)
            {
                sky = candidate;
                break;
            }
        }

        if (sky is not { } texture || texture.Frames.Count == 0)
        {
            return;
        }

        // The measured panorama (OV-20261029-sky): sides spread over the full turn at
        // SCREEN_WIDTH / PLAYER_ARC pixels per radian, offset half a field of view, the bitmap
        // unscaled vertically with its last row on the eye line and its edge rows repeated.
        double width = Math.Max(1, _runtime.GetSkill("SCREEN_WIDTH"));
        double height = Math.Max(1, _runtime.GetSkill("SCREEN_HGT"));
        int bitmapHeight = Math.Max(1, texture.Height);
        double top = SkyPanorama.TopRow(height, _runtime.GetSkill("PLAYER_TILT"), bitmapHeight, _runtime.GetSkill("SKY_OFFS_Y"));
        double bottom = top + bitmapHeight;
        float halfRow = 0.5f / bitmapHeight;
        Matrix4x4 transform = Matrix4x4.CreateOrthographicOffCenter(0, (float)width, (float)height, 0, 0, 1);
        foreach (SkySpan span in SkyPanorama.Columns(
                     width,
                     _runtime.GetSkill("PLAYER_ARC"),
                     _runtime.GetSkill("PLAYER_ANGLE"),
                     Math.Max(1, texture.Sides),
                     _runtime.GetSkill("SKY_OFFS_X")))
        {
            if (TextureAnimation.ResolveRenderTexture(texture, cycle: 1, span.Side) is not { } renderTexture)
            {
                continue;
            }

            var vertices = new List<RenderVertex>(18);
            AddSkyBand(vertices, span, 0, top, halfRow, halfRow);
            AddSkyBand(vertices, span, top, bottom, 0, 1);
            AddSkyBand(vertices, span, bottom, height, 1 - halfRow, 1 - halfRow);
            if (vertices.Count == 0)
            {
                continue;
            }

            AddBatch(
                frame,
                vertices.ToArray(),
                transform,
                viewport,
                renderTexture,
                TextureSlot.World,
                alphaTest: false,
                depthTest: false,
                ambient: 1);
        }
    }

    /// <summary>One quad of a sky span between two rows, clipped to the window, with texture rows v0…v1.</summary>
    private static void AddSkyBand(List<RenderVertex> vertices, SkySpan span, double y0, double y1, float v0, float v1)
    {
        if (y1 <= y0)
        {
            return;
        }

        float x0 = (float)span.X0;
        float x1 = (float)span.X1;
        float u0 = (float)span.U0;
        float u1 = (float)span.U1;
        float top = (float)y0;
        float bottom = (float)y1;
        vertices.Add(new RenderVertex(x0, bottom, 0, u0, v1));
        vertices.Add(new RenderVertex(x1, bottom, 0, u1, v1));
        vertices.Add(new RenderVertex(x1, top, 0, u1, v0));
        vertices.Add(new RenderVertex(x0, bottom, 0, u0, v1));
        vertices.Add(new RenderVertex(x1, top, 0, u1, v0));
        vertices.Add(new RenderVertex(x0, top, 0, u0, v0));
    }

    private void AddWorldBatches(HostFrame frame, Matrix4x4 viewProjection, PixelRect viewport)
    {
        foreach (CachedBatch batch in _batches)
        {
            string? renderTexture = batch.Texture;
            if (batch.AnimationSource is { } source &&
                batch.Texture is not null &&
                _level.Textures.TryGetValue(batch.Texture, out LevelTexture animated))
            {
                int side = source is Wall wall &&
                    (uint)wall.Vertex1 < (uint)_level.Vertices.Count && (uint)wall.Vertex2 < (uint)_level.Vertices.Count
                    ? TextureAnimation.WallSide(
                        animated,
                        _level.Vertices[wall.Vertex1].X,
                        _level.Vertices[wall.Vertex1].Y,
                        _level.Vertices[wall.Vertex2].X,
                        _level.Vertices[wall.Vertex2].Y,
                        _camera.Position.X,
                        RenderSpace.MapYFromZ(_camera.Position.Z))
                    : 0;
                renderTexture = TextureAnimation.ResolveRenderTexture(animated, source["CYCLE"], side);
                if (renderTexture is null)
                {
                    continue;
                }
            }

            AddBatch(
                frame,
                batch.Vertices,
                viewProjection,
                viewport,
                renderTexture,
                TextureSlot.World,
                batch.AlphaTest,
                depthTest: true,
                _shading ? ShadeAmbient(batch.Texture) : batch.Ambient,
                shaded: _shading,
                depthWrite: !batch.Surface);
        }
    }

    private void AddSprites(HostFrame frame, Matrix4x4 viewProjection, PixelRect viewport)
    {
        if (_sprites.Count == 0)
        {
            return;
        }

        Vector3 right = _camera.Right;
        Vector3 up = Vector3.UnitY;
        Vector3 horizontalForward = _camera.LevelForward;
        IReadOnlyList<SpriteBillboard> sorted = SpriteBuilder.OrderBackToFront(_sprites, _camera.Position);
        for (int start = 0; start < sorted.Count;)
        {
            string texture = sorted[start].Texture;
            SpriteBlend blend = sorted[start].Blend;
            int end = start + 1;
            while (end < sorted.Count && sorted[end].Texture.Equals(texture, StringComparison.OrdinalIgnoreCase) && sorted[end].Blend == blend)
            {
                end++;
            }

            var vertices = new List<RenderVertex>((end - start) * 6);
            for (int i = start; i < end; i++)
            {
                SpriteBillboard billboard = sorted[i];
                Vector3 r = right * billboard.HalfWidth;
                Vector3 u;
                Vector3 center;
                if (billboard.Orientation == SpriteOrientation.Ground)
                {
                    u = horizontalForward * billboard.HalfHeight;
                    center = billboard.BasePosition +
                        (right * billboard.CenterOffset.X) +
                        (horizontalForward * billboard.CenterOffset.Y);
                }
                else
                {
                    u = up * billboard.HalfHeight;
                    center = billboard.BasePosition +
                        (horizontalForward * billboard.CameraDepthOffset) +
                        (right * billboard.CenterOffset.X) +
                        (up * (billboard.HalfHeight + billboard.CenterOffset.Y));
                }

                Vector3 bottomLeft = center - r - u;
                Vector3 bottomRight = center + r - u;
                Vector3 topRight = center + r + u;
                Vector3 topLeft = center - r + u;
                float leftU = billboard.Mirror ? 1 : 0;
                float rightU = billboard.Mirror ? 0 : 1;
                float localAmbient = billboard.Source is { } source &&
                    source.Region >= 0 && source.Region < _level.Regions.Count
                    ? (float)_level.Regions[source.Region].Ambient
                    : 0;
                vertices.Add(new RenderVertex(bottomLeft.X, bottomLeft.Y, bottomLeft.Z, leftU, 1, localAmbient));
                vertices.Add(new RenderVertex(bottomRight.X, bottomRight.Y, bottomRight.Z, rightU, 1, localAmbient));
                vertices.Add(new RenderVertex(topRight.X, topRight.Y, topRight.Z, rightU, 0, localAmbient));
                vertices.Add(new RenderVertex(bottomLeft.X, bottomLeft.Y, bottomLeft.Z, leftU, 1, localAmbient));
                vertices.Add(new RenderVertex(topRight.X, topRight.Y, topRight.Z, rightU, 0, localAmbient));
                vertices.Add(new RenderVertex(topLeft.X, topLeft.Y, topLeft.Z, leftU, 0, localAmbient));
            }

            AddBatch(
                frame,
                vertices.ToArray(),
                viewProjection,
                viewport,
                texture,
                TextureSlot.World,
                alphaTest: true,
                depthTest: true,
                _shading ? ShadeAmbient(texture) : TextureAmbient(texture),
                shaded: _shading,
                blend: BatchBlendOf(blend),
                spriteMarker: PaletteShading.SpriteMarker);
            start = end;
        }
    }

    /// <summary>
    /// OV-20261104-blend: a DIAPHANOUS texture blends only with a BLUR palette's table; without one the
    /// original draws palette entry 0.
    /// </summary>
    private BatchBlend BatchBlendOf(SpriteBlend blend) => blend switch
    {
        SpriteBlend.Ghost => BatchBlend.Ghost,
        SpriteBlend.Diaphanous => _level.PaletteShading.Values.Any(static palette => palette.Blur) ? BatchBlend.Diaphanous : BatchBlend.Black,
        _ => BatchBlend.None,
    };

    private void AddUiOverlays(HostFrame frame, VirtualDisplayLayout display, Matrix4x4 projection, PixelRect viewport)
    {
        Dictionary<string, UiBitmapSize> bitmapSizes = BitmapSizes();
        IReadOnlyDictionary<string, WdlUiElement>? uiElements = _runtime?.UiElements;
        IReadOnlyList<UiOverlaySprite> overlays = UiOverlayBuilder.Build(_level, uiElements, _runtime?.Skills);
        IReadOnlyList<UiOverlaySprite> dynamicWidgets = _runtime is not null
            ? UiOverlayBuilder.BuildPanelWidgets(_level, _runtime.Skills, bitmapSizes, uiElements, _pressedUiButton)
            : [];
        foreach (UiOverlaySprite overlay in overlays.Concat(dynamicWidgets))
        {
            TextureImage? image = _uiImages.TryGetValue(overlay.Bitmap, out TextureImage found) ? found : null;
            float width = overlay.Width > 0 ? overlay.Width : image?.Width ?? 0;
            float height = overlay.Height > 0 ? overlay.Height : image?.Height ?? 0;
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            float x = overlay.X;
            float y = overlay.Y;
            float u0 = 0;
            float v0 = 0;
            float u1 = 1;
            float v1 = 1;
            if (image is not null && overlay.SourceWidth > 0 && overlay.SourceHeight > 0)
            {
                TextureImage source = image.Value;
                u0 = Math.Clamp(overlay.SourceX, 0, source.Width) / (float)source.Width;
                v0 = Math.Clamp(overlay.SourceY, 0, source.Height) / (float)source.Height;
                u1 = Math.Clamp(overlay.SourceX + overlay.SourceWidth, 0, source.Width) / (float)source.Width;
                v1 = Math.Clamp(overlay.SourceY + overlay.SourceHeight, 0, source.Height) / (float)source.Height;
            }

            AddBatch(
                frame,
                ScreenQuad(x, y, x + width, y + height, u0, v0, u1, v1),
                projection,
                viewport,
                overlay.Bitmap,
                TextureSlot.Ui,
                alphaTest: true,
                depthTest: false,
                ambient: 1);
        }
    }

    private void AddUiText(HostFrame frame, Matrix4x4 projection, PixelRect viewport)
    {
        if (_fontImages.Count == 0 || _runtime is null)
        {
            return;
        }

        IReadOnlyList<UiTextGlyph> glyphs = UiTextBuilder.Build(_level, _runtime.Strings, _runtime.Skills, _runtime.UiElements);
        foreach (IGrouping<string, UiTextGlyph> group in glyphs.GroupBy(glyph => glyph.Font, StringComparer.OrdinalIgnoreCase))
        {
            if (!_fontImages.TryGetValue(group.Key, out TextureImage image))
            {
                continue;
            }

            var vertices = new List<RenderVertex>();
            foreach (UiTextGlyph glyph in group)
            {
                if (glyph.Width <= 0 || glyph.Height <= 0)
                {
                    continue;
                }

                int columns = Math.Max(1, image.Width / glyph.Width);
                int rows = Math.Max(1, image.Height / glyph.Height);
                int glyphCount = columns * rows;
                int code = FontAtlasIndex(glyph.CodePoint, glyphCount);
                if (code < 0 || code >= glyphCount)
                {
                    continue;
                }

                int sourceOffsetX = Math.Clamp(glyph.SourceX, 0, glyph.Width);
                int sourceOffsetY = Math.Clamp(glyph.SourceY, 0, glyph.Height);
                int sourceWidth = glyph.SourceWidth > 0 ? Math.Min(glyph.SourceWidth, glyph.Width - sourceOffsetX) : glyph.Width - sourceOffsetX;
                int sourceHeight = glyph.SourceHeight > 0 ? Math.Min(glyph.SourceHeight, glyph.Height - sourceOffsetY) : glyph.Height - sourceOffsetY;
                int drawWidth = glyph.DrawWidth > 0 ? glyph.DrawWidth : sourceWidth;
                int drawHeight = glyph.DrawHeight > 0 ? glyph.DrawHeight : sourceHeight;
                if (sourceWidth <= 0 || sourceHeight <= 0 || drawWidth <= 0 || drawHeight <= 0)
                {
                    continue;
                }

                int sourceX = ((code % columns) * glyph.Width) + sourceOffsetX;
                int sourceY = ((code / columns) * glyph.Height) + sourceOffsetY;
                float u0 = (float)sourceX / image.Width;
                float v0 = (float)sourceY / image.Height;
                float u1 = (float)(sourceX + sourceWidth) / image.Width;
                float v1 = (float)(sourceY + sourceHeight) / image.Height;
                vertices.AddRange(ScreenQuad(glyph.X, glyph.Y, glyph.X + drawWidth, glyph.Y + drawHeight, u0, v0, u1, v1));
            }

            if (vertices.Count == 0)
            {
                continue;
            }

            AddBatch(
                frame,
                vertices.ToArray(),
                projection,
                viewport,
                group.Key,
                TextureSlot.Font,
                alphaTest: true,
                depthTest: false,
                ambient: 1);
        }
    }

    private void AddBatch(
        HostFrame frame,
        RenderVertex[] vertices,
        Matrix4x4 transform,
        PixelRect viewport,
        string? texture,
        TextureSlot slot,
        bool alphaTest,
        bool depthTest,
        float ambient,
        PaletteFadeTint? fade = null,
        TextureImage? dynamic = null,
        bool shaded = false,
        bool depthWrite = true,
        BatchBlend blend = BatchBlend.None,
        int spriteMarker = 0)
    {
        if (vertices.Length == 0)
        {
            return;
        }

        frame.Batches.Add(new DrawBatch
        {
            Texture = texture,
            Slot = slot,
            DynamicImage = dynamic,
            Transform = transform,
            Viewport = viewport,
            AlphaTest = alphaTest,
            DepthTest = depthTest,
            DepthWrite = depthWrite,
            Blend = blend,
            SpriteMarker = spriteMarker,
            Ambient = ambient,
            Fade = fade ?? _fade,
            Vertices = vertices,
            Shaded = shaded,
        });
    }

    private VirtualDisplayLayout DisplayLayout()
    {
        int width = Math.Max(1, _window.Width);
        int height = Math.Max(1, _window.Height);
        if (_level.VideoMode is { } mode)
        {
            // Every documented VGA mode was shown on a 4:3 display. 320x200 and X320x400
            // frames are stretched to it.
            return VirtualDisplayLayout.Create(width, height, mode.Width, mode.Height, LevelVideoMode.DisplayAspect);
        }

        return VirtualDisplayLayout.Create(width, height, width, height);
    }

    /// <summary>
    /// OV-20261105-shade-global: the frame's light as the zones a shaded batch is drawn with —
    /// <c>Qf</c> for surfaces, <c>Qw</c> for walls and sprites, and φ — computed once from the live
    /// <c>PLAYER_LIGHT</c> and <c>LIGHT_DIST</c>.
    /// </summary>
    private Vector3 Zones()
    {
        double light = _runtime.GetSkill("PLAYER_LIGHT");
        double distance = _runtime.GetSkill("LIGHT_DIST");
        return new Vector3(
            (float)PaletteShading.SurfaceQ(light, distance),
            (float)PaletteShading.WallQ(light, distance),
            (float)PaletteShading.Phi(light, distance));
    }

    /// <summary>The live projection in 3-D-window pixels.</summary>
    private ProjectionModel CurrentProjection() =>
        ProjectionModel.Create(
            _runtime.GetSkill("PLAYER_ARC"),
            _runtime.GetSkill("ASPECT"),
            _runtime.GetSkill("PLAYER_TILT"),
            (int)Math.Round(_runtime.GetSkill("SCREEN_WIDTH")),
            (int)Math.Round(_runtime.GetSkill("SCREEN_HGT")));

    private DisplayRectangle WorldViewport(VirtualDisplayLayout display)
    {
        (float x, float y, float width, float height) = WorldWindow(display);
        return display.ToPhysical(x, y, width, height);
    }

    /// <summary>
    /// The 3-D window in the virtual display's own pixels, clamped to it as the original clamps it:
    /// the width a wall's shade steps are counted across (OV-20261105-shade-global,
    /// <see cref="HostFrame.Columns"/>).
    /// </summary>
    private (float X, float Y, float Width, float Height) WorldWindow(VirtualDisplayLayout display)
    {
        float x = (float)Math.Clamp(_runtime.GetSkill("SCREEN_X"), 0, display.VirtualWidth - 1);
        float y = (float)Math.Clamp(_runtime.GetSkill("SCREEN_Y"), 0, display.VirtualHeight - 1);
        float width = (float)Math.Clamp(_runtime.GetSkill("SCREEN_WIDTH"), 1, display.VirtualWidth - x);
        float height = (float)Math.Clamp(_runtime.GetSkill("SCREEN_HGT"), 1, display.VirtualHeight - y);
        return (x, y, width, height);
    }

    /// <summary>The 3-D window's width in virtual columns, the space a wall's shade steps are counted in.</summary>
    private float WorldColumns(VirtualDisplayLayout display) => WorldWindow(display).Width;

    /// <summary>The global and the texture's <c>AMBIENT</c> for a shaded batch; the region's comes per vertex.</summary>
    private float ShadeAmbient(string? texture)
    {
        double ambient = _runtime.GetSkill("AMBIENT");
        string? logicalTexture = texture is null ? null : TextureAnimation.LogicalTextureName(texture);
        if (logicalTexture is not null && _level.Textures.TryGetValue(logicalTexture, out LevelTexture levelTexture))
        {
            ambient += levelTexture.Ambient;
        }

        return (float)ambient;
    }

    private float TextureAmbient(string? texture)
    {
        double light = _runtime.GetSkill("AMBIENT") + _runtime.GetSkill("PLAYER_LIGHT");
        string? logicalTexture = texture is null ? null : TextureAnimation.LogicalTextureName(texture);
        if (logicalTexture is not null && _level.Textures.TryGetValue(logicalTexture, out LevelTexture levelTexture))
        {
            light += levelTexture.Ambient;
        }

        return (float)light;
    }

    private Vector4 ChooseBackgroundColor()
    {
        foreach (LevelTexture texture in _level.Textures.Values)
        {
            if (texture.IsSky && _textureImages.TryGetValue(texture.Name, out TextureImage image))
            {
                return AverageColor(image);
            }
        }

        return DefaultClearColor;
    }

    /// <summary>
    /// The colour the screen is cleared to: palette entry <c>FRAME_COLOR</c> of the palette the
    /// screen shows (OV-20261104-retail-frames: the original's border around a 3-D window smaller
    /// than the screen is that entry, black under the title's gray fade), or, without a palette, the
    /// level's sky average under the fade.
    /// </summary>
    private Vector4 TintedClearColor()
    {
        if (ShownPalette is { } palette)
        {
            int index = Math.Clamp((int)_runtime.GetSkill("FRAME_COLOR"), 0, 255);
            return new Vector4(palette[index * 3] / 255f, palette[(index * 3) + 1] / 255f, palette[(index * 3) + 2] / 255f, 1f);
        }

        Vector3 faded = _fade.Apply(new Vector3(_clearColor.X, _clearColor.Y, _clearColor.Z));
        return new Vector4(Math.Clamp(faded.X, 0, 1), Math.Clamp(faded.Y, 0, 1), Math.Clamp(faded.Z, 0, 1), _clearColor.W);
    }

    private static Vector4 AverageColor(TextureImage image)
    {
        long r = 0;
        long g = 0;
        long b = 0;
        long count = 0;
        ReadOnlySpan<byte> rgba = image.Rgba;
        for (int i = 0; i + 3 < rgba.Length; i += 4)
        {
            r += rgba[i];
            g += rgba[i + 1];
            b += rgba[i + 2];
            count++;
        }

        if (count == 0)
        {
            return DefaultClearColor;
        }

        float scale = 1.0f / (255.0f * count);
        return new Vector4(r * scale, g * scale, b * scale, 1f);
    }

    private static int FontAtlasIndex(int codePoint, int glyphCount)
    {
        if (glyphCount == 11)
        {
            if (codePoint is >= '0' and <= '9')
            {
                return codePoint - '0';
            }

            // The minus of a negative DIGITS value has no cell in a digit font: the original draws
            // the space there (OV-20261029-ui-layout).
            return codePoint is ' ' or '-' ? 10 : -1;
        }

        return codePoint;
    }

    private static RenderVertex[] UnitQuad() =>
    [
        new(0, 1, 0, 0, 1),
        new(1, 1, 0, 1, 1),
        new(1, 0, 0, 1, 0),
        new(0, 1, 0, 0, 1),
        new(1, 0, 0, 1, 0),
        new(0, 0, 0, 0, 0),
    ];

    private static RenderVertex[] ScreenQuad(
        float x,
        float y,
        float right,
        float bottom,
        float u0,
        float v0,
        float u1,
        float v1) =>
    [
        new(x, bottom, 0, u0, v1),
        new(right, bottom, 0, u1, v1),
        new(right, y, 0, u1, v0),
        new(x, bottom, 0, u0, v1),
        new(right, y, 0, u1, v0),
        new(x, y, 0, u0, v0),
    ];
}
